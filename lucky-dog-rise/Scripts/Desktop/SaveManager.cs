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
    public int Chips { get; set; } = GameData.StartingChips;
    public List<int> OwnedItemIds { get; set; } = new();
    public Dictionary<int, int> OwnedItemCounts { get; set; } = new();
    public Dictionary<string, int> EquippedItemIdsByType { get; set; } = new();
    public List<int> NewItemIds { get; set; } = new();
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

        try
        {
            using var file = FileAccess.Open(SavePath, FileAccess.ModeFlags.Read);
            var json = file.GetAsText();
            var profile = JsonSerializer.Deserialize<SaveProfile>(json, JsonOptions);
            if (profile == null)
                throw new InvalidOperationException("Save profile was empty.");

            return Normalize(profile);
        }
        catch (Exception ex)
        {
            GD.PushError($"[Save] Failed to load save. Creating a new profile. {ex.Message}");
            BackupCorruptSave();
            var fresh = CreateDefaultProfile();
            Save(fresh);
            return fresh;
        }
    }

    public static void Save(SaveProfile profile)
    {
        EnsureSaveDir();
        var existing = TryLoadExistingWithoutRecovery();
        profile.Version = CurrentVersion;
        if (string.IsNullOrWhiteSpace(profile.CreatedAt))
            profile.CreatedAt = string.IsNullOrWhiteSpace(existing?.CreatedAt)
                ? DateTimeOffset.UtcNow.ToString("O")
                : existing.CreatedAt;
        profile.UpdatedAt = DateTimeOffset.UtcNow.ToString("O");

        var json = JsonSerializer.Serialize(Normalize(profile), JsonOptions);
        if (FileAccess.FileExists(SavePath))
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

    private static void BackupCorruptSave()
    {
        if (!FileAccess.FileExists(SavePath))
            return;

        CopyFile(SavePath, CorruptBackupPath);
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
}
