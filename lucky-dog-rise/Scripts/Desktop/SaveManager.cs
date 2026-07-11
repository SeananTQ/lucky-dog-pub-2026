#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Godot;

namespace LuckyDogRise;

public sealed class SaveProfile
{
    public int Version { get; set; } = SaveManager.CurrentVersion;
    public int IntegrityVersion { get; set; }
    public string IntegrityTag { get; set; } = "";
    public int Chips { get; set; } = GameData.StartingChips;
    public double TotalPlaySeconds { get; set; }
    public List<int> OwnedItemIds { get; set; } = new();
    public Dictionary<int, int> OwnedItemCounts { get; set; } = new();
    public Dictionary<string, int> EquippedItemIdsByType { get; set; } = new();
    public List<int> NewItemIds { get; set; } = new();
    public Dictionary<int, int> BlindBoxClaimedCountsBySchedule { get; set; } = new();
    public BlindBoxRuntimeState BlindBoxRuntimeState { get; set; } = new();
    public PendingBlindBoxReward? PendingBlindBoxReward { get; set; }
    public string CreatedAt { get; set; } = "";
    public string UpdatedAt { get; set; } = "";
}

public static class SaveManager
{
    public const int CurrentVersion = 1;

    private const string SaveDir = "user://saves";
    private const string SavePath = "user://saves/profile_0.json";
    private const string BackupPath = "user://saves/profile_0.backup.json";
    private const string CorruptBackupPath = "user://saves/profile_0.corrupt.json";
    private const string InvalidSignaturePath = "user://saves/profile_0.invalid_signature.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
    };

    public static SaveProfile LoadOrCreate()
    {
        EnsureSaveDir();

        if (!FileAccess.FileExists(SavePath))
        {
            var fresh = CreateDefaultProfile();
            Save(fresh);
            return fresh;
        }

        if (TryLoadVerified(SavePath, out var profile, out var failure))
            return Normalize(profile!);

        GD.PushError($"[Save] Primary save rejected: {failure}.");
        if (TryLoadVerified(BackupPath, out profile, out _))
        {
            CopyFile(BackupPath, SavePath);
            GD.PushWarning("[Save] Restored the verified backup save.");
            return Normalize(profile!);
        }

        BackupRejectedSave(failure == "invalid signature" ? InvalidSignaturePath : CorruptBackupPath);
        var replacement = CreateDefaultProfile();
        SaveInternal(replacement, backupExisting: false);
        return replacement;
    }

    public static void Save(SaveProfile profile)
    {
        SaveInternal(profile, backupExisting: true);
    }

    private static void SaveInternal(SaveProfile profile, bool backupExisting)
    {
        EnsureSaveDir();
        var existing = TryLoadExistingWithoutRecovery();
        profile.Version = CurrentVersion;
        if (string.IsNullOrWhiteSpace(profile.CreatedAt))
            profile.CreatedAt = string.IsNullOrWhiteSpace(existing?.CreatedAt)
                ? DateTimeOffset.UtcNow.ToString("O")
                : existing.CreatedAt;
        profile.UpdatedAt = DateTimeOffset.UtcNow.ToString("O");

        profile = Normalize(profile);
        profile.IntegrityVersion = SaveIntegrity.CurrentVersion;
        profile.IntegrityTag = SaveIntegrity.Sign(profile);
        var json = JsonSerializer.Serialize(profile, JsonOptions);
        if (backupExisting && FileAccess.FileExists(SavePath))
            CopyFile(SavePath, BackupPath);

        using var file = FileAccess.Open(SavePath, FileAccess.ModeFlags.Write);
        file.StoreString(json);
    }

    public static SaveProfile CreateDefaultProfile()
    {
        var profile = new SaveProfile
        {
            Version = CurrentVersion,
            Chips = GameData.StartingChips,
            CreatedAt = DateTimeOffset.UtcNow.ToString("O"),
        };

        profile.OwnedItemCounts = LubanData.Tables.TbItem.DataList
            .Where(item => item.AcquisitionType == DataTables.EAcquisitionType.Initial)
            .Select(item => item.Id)
            .Distinct()
            .OrderBy(id => id)
            .ToDictionary(id => id, _ => 1);
        profile.OwnedItemIds = profile.OwnedItemCounts.Keys.ToList();

        var inventory = new PlayerInventory();
        inventory.LoadState(profile.OwnedItemCounts, new Dictionary<string, int>(), emitChanged: false);
        profile.EquippedItemIdsByType = inventory.GetEquippedIdsByTypeName();
        return profile;
    }

    public static SaveProfile ResetLocalSave()
    {
        var profile = CreateDefaultProfile();
        Save(profile);
        return profile;
    }

    private static SaveProfile Normalize(SaveProfile profile)
    {
        if (profile.Version <= 0)
            profile.Version = 1;

        profile.Chips = Math.Max(0, profile.Chips);
        profile.OwnedItemIds ??= new List<int>();
        profile.OwnedItemCounts ??= new Dictionary<int, int>();
        profile.EquippedItemIdsByType ??= new Dictionary<string, int>();
        profile.NewItemIds ??= new List<int>();
        profile.BlindBoxClaimedCountsBySchedule ??= new Dictionary<int, int>();
        profile.BlindBoxRuntimeState ??= new BlindBoxRuntimeState();
        profile.BlindBoxRuntimeState.LoopTrackStates ??= new Dictionary<int, BlindBoxScheduleState>();
        profile.TotalPlaySeconds = Math.Max(0, profile.TotalPlaySeconds);

        var validIds = LubanData.Tables.TbItem.DataList
            .Select(item => item.Id)
            .ToHashSet();

        if (profile.OwnedItemCounts.Count == 0 && profile.OwnedItemIds.Count > 0)
            profile.OwnedItemCounts = profile.OwnedItemIds
                .Where(validIds.Contains)
                .Distinct()
                .ToDictionary(id => id, _ => 1);

        profile.OwnedItemCounts = profile.OwnedItemCounts
            .Where(pair => validIds.Contains(pair.Key) && pair.Value > 0)
            .OrderBy(pair => pair.Key)
            .ToDictionary(pair => pair.Key, pair => pair.Value);
        profile.OwnedItemIds = profile.OwnedItemCounts.Keys.ToList();

        profile.NewItemIds = profile.NewItemIds
            .Where(id => validIds.Contains(id) && profile.OwnedItemCounts.ContainsKey(id))
            .Distinct()
            .OrderBy(id => id)
            .ToList();

        var validScheduleIds = LubanData.Tables.TbBlindBoxSchedule.DataList
            .Select(schedule => schedule.Id)
            .ToHashSet();
        profile.BlindBoxClaimedCountsBySchedule = profile.BlindBoxClaimedCountsBySchedule
            .Where(pair => validScheduleIds.Contains(pair.Key) && pair.Value > 0)
            .OrderBy(pair => pair.Key)
            .ToDictionary(pair => pair.Key, pair => pair.Value);
        profile.BlindBoxRuntimeState.SequenceIndex = Math.Max(0, profile.BlindBoxRuntimeState.SequenceIndex);
        profile.BlindBoxRuntimeState.LastClaimSeconds = Math.Max(0, profile.BlindBoxRuntimeState.LastClaimSeconds);
        profile.BlindBoxRuntimeState.LoopTrackStates = profile.BlindBoxRuntimeState.LoopTrackStates
            .Where(pair => validScheduleIds.Contains(pair.Key) && pair.Value != null)
            .OrderBy(pair => pair.Key)
            .ToDictionary(
                pair => pair.Key,
                pair => new BlindBoxScheduleState
                {
                    PendingCount = Math.Max(0, pair.Value.PendingCount),
                    ProcessedGrantCount = Math.Max(0, pair.Value.ProcessedGrantCount),
                });

        if (profile.PendingBlindBoxReward != null)
        {
            var pending = profile.PendingBlindBoxReward;
            var validPending = validIds.Contains(pending.ItemId)
                && LubanData.Tables.TbBlindBox.GetOrDefault(pending.BlindBoxId) != null
                && validScheduleIds.Contains(pending.ScheduleId);
            if (!validPending)
                profile.PendingBlindBoxReward = null;
        }

        var inventory = new PlayerInventory();
        inventory.LoadState(profile.OwnedItemCounts, profile.EquippedItemIdsByType, profile.NewItemIds, emitChanged: false);
        profile.EquippedItemIdsByType = inventory.GetEquippedIdsByTypeName();
        return profile;
    }

    private static void EnsureSaveDir()
    {
        if (!DirAccess.DirExistsAbsolute(SaveDir))
            DirAccess.MakeDirRecursiveAbsolute(SaveDir);
    }

    private static void BackupRejectedSave(string destination)
    {
        if (!FileAccess.FileExists(SavePath))
            return;

        CopyFile(SavePath, destination);
    }

    private static void CopyFile(string from, string to)
    {
        using var input = FileAccess.Open(from, FileAccess.ModeFlags.Read);
        using var output = FileAccess.Open(to, FileAccess.ModeFlags.Write);
        output.StoreBuffer(input.GetBuffer((long)input.GetLength()));
    }

    private static SaveProfile? TryLoadExistingWithoutRecovery()
    {
        if (!FileAccess.FileExists(SavePath))
            return null;

        try
        {
            using var file = FileAccess.Open(SavePath, FileAccess.ModeFlags.Read);
            return JsonSerializer.Deserialize<SaveProfile>(file.GetAsText(), JsonOptions);
        }
        catch
        {
            return null;
        }
    }

    private static bool TryLoadVerified(string path, out SaveProfile? profile, out string failure)
    {
        profile = null;
        failure = "missing";
        if (!FileAccess.FileExists(path))
            return false;

        try
        {
            using var file = FileAccess.Open(path, FileAccess.ModeFlags.Read);
            profile = JsonSerializer.Deserialize<SaveProfile>(file.GetAsText(), JsonOptions);
            if (profile == null)
            {
                failure = "empty profile";
                return false;
            }

            var unsigned = profile.IntegrityVersion == 0 && string.IsNullOrWhiteSpace(profile.IntegrityTag);
            if (unsigned && BuildInfo.IsDevelopment)
            {
                failure = string.Empty;
                return true;
            }

            if (!SaveIntegrity.Verify(profile))
            {
                failure = "invalid signature";
                return false;
            }

            failure = string.Empty;
            return true;
        }
        catch (Exception ex)
        {
            failure = ex.GetType().Name;
            return false;
        }
    }
}
