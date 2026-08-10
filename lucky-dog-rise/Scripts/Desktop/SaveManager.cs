#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
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
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<int>? AppliedLinkTreeRewardIds { get; set; } = new();
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? LinkTreeRewardLedgerInitialized { get; set; } = true;
    public Dictionary<int, int> BlindBoxClaimedCountsBySchedule { get; set; } = new();
    public BlindBoxRuntimeState BlindBoxRuntimeState { get; set; } = new();
    public PendingBlindBoxReward? PendingBlindBoxReward { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public PendingPlatformBlindBoxOpen? PendingPlatformBlindBoxOpen { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public PendingLinkTreeClaim? PendingLinkTreeClaim { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public LuckyDealBuffState? LuckyDealBuffState { get; set; } = new();
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public RefreshmentRuntimeState? RefreshmentRuntimeState { get; set; } = new();
    public string CreatedAt { get; set; } = "";
    public string UpdatedAt { get; set; } = "";
}

public sealed class PendingLinkTreeClaim
{
    public int LinkTreeId { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public int SteamPromoItemDefId { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public int SteamClaimBundleItemDefId { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public int SteamReceiptItemDefId { get; set; }
}

public enum SaveLoadDisposition
{
    Primary,
    Created,
    BackupRecovered,
    ReplacedInvalid,
    ReplacedLegacy,
}

public sealed record SaveLoadResult(SaveProfile Profile, SaveLoadDisposition Disposition, string Detail);

public static class SaveManager
{
    public const int CurrentVersion = 14;
    public const int MinimumSupportedVersion = 10;

    private const string SaveDir = "user://saves";
    private const string SavePath = "user://saves/profile_0.json";
    private const string BackupPath = "user://saves/profile_0.backup.json";
    private const string CorruptBackupPath = "user://saves/profile_0.corrupt.json";
    private const string InvalidSignaturePath = "user://saves/profile_0.invalid_signature.json";
    private const string TempPath = "user://saves/profile_0.tmp.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
    };

    public static SaveProfile LoadOrCreate()
    {
        return LoadOrCreateDetailed().Profile;
    }

    public static SaveLoadResult LoadOrCreateDetailed()
    {
        EnsureSaveDir();

        if (!FileAccess.FileExists(SavePath))
        {
            var fresh = CreateDefaultProfile();
            Save(fresh);
            return ReportLoad(fresh, SaveLoadDisposition.Created, "No save existed; created a new profile.");
        }

        if (TryLoadVerified(SavePath, out var profile, out var failure))
        {
            bool replacedLegacy = profile!.Version < MinimumSupportedVersion;
            var loaded = LoadSupportedOrReplaceLegacy(profile!, "primary");
            var disposition = replacedLegacy
                ? SaveLoadDisposition.ReplacedLegacy
                : SaveLoadDisposition.Primary;
            return ReportLoad(loaded, disposition, "Loaded the verified primary save.");
        }

        GD.PushError($"[Save] Primary save rejected: {failure}.");
        if (TryLoadVerified(BackupPath, out profile, out _))
        {
            if (profile!.Version < MinimumSupportedVersion)
                return ReportLoad(
                    ReplaceLegacySave(profile, "backup"),
                    SaveLoadDisposition.ReplacedLegacy,
                    "The verified backup was from an unsupported version and was replaced.");

            RestoreVerifiedBackupAtomically();
            return ReportLoad(
                Normalize(profile),
                SaveLoadDisposition.BackupRecovered,
                $"Primary was rejected ({failure}); restored the verified backup.");
        }

        BackupRejectedSave(failure == "invalid signature" ? InvalidSignaturePath : CorruptBackupPath);
        var replacement = CreateDefaultProfile();
        SaveInternal(replacement, backupExisting: false);
        return ReportLoad(
            replacement,
            SaveLoadDisposition.ReplacedInvalid,
            $"Primary was rejected ({failure}) and no verified backup was available.");
    }

    private static SaveLoadResult ReportLoad(
        SaveProfile profile,
        SaveLoadDisposition disposition,
        string detail)
    {
        if (disposition != SaveLoadDisposition.Primary)
            GD.PushWarning($"[SaveRecovery] {disposition}: {detail}");
        else
            GD.Print($"[SaveRecovery] {disposition}: {detail}");
        DiagnosticLog.Record("save_loaded", new Dictionary<string, object?>
        {
            ["disposition"] = disposition.ToString(),
            ["version"] = profile.Version,
            ["integrityVersion"] = profile.IntegrityVersion,
            ["chips"] = profile.Chips,
            ["updatedAt"] = profile.UpdatedAt,
            ["detail"] = detail,
        });
        return new SaveLoadResult(profile, disposition, detail);
    }

    private static SaveProfile LoadSupportedOrReplaceLegacy(SaveProfile profile, string source)
    {
        return profile.Version < MinimumSupportedVersion
            ? ReplaceLegacySave(profile, source)
            : Normalize(profile);
    }

    private static SaveProfile ReplaceLegacySave(SaveProfile profile, string source)
    {
        var legacyVersion = Math.Max(1, profile.Version);
        if (!TryArchiveActiveSaves(legacyVersion, out var archiveDescription))
        {
            GD.PushError(
                $"[Save] Failed to archive unsupported {source} save V{legacyVersion}. " +
                "Continuing with the legacy migration path to avoid losing player data.");
            return Normalize(profile);
        }

        var replacement = CreateDefaultProfile();
        SaveInternal(replacement, backupExisting: false);
        GD.PushWarning(
            $"[Save] Unsupported {source} save V{legacyVersion} was archived ({archiveDescription}). " +
            $"Created a fresh V{CurrentVersion} profile; Steam achievements and inventory remain platform-owned.");
        return replacement;
    }

    private static bool TryArchiveActiveSaves(int legacyVersion, out string archiveDescription)
    {
        var uniqueId = Guid.NewGuid().ToString("N")[..8];
        var suffix = $"v{legacyVersion}_{DateTimeOffset.UtcNow:yyyyMMddTHHmmssfffZ}_{uniqueId}";
        var candidates = new[]
        {
            (Source: SavePath, Destination: $"{SaveDir}/profile_0.legacy_{suffix}.json"),
            (Source: BackupPath, Destination: $"{SaveDir}/profile_0.backup.legacy_{suffix}.json"),
        };
        var archived = new List<(string Source, string Destination)>();

        try
        {
            foreach (var candidate in candidates)
            {
                if (!FileAccess.FileExists(candidate.Source))
                    continue;

                System.IO.File.Copy(
                    ProjectSettings.GlobalizePath(candidate.Source),
                    ProjectSettings.GlobalizePath(candidate.Destination),
                    overwrite: false);
                archived.Add(candidate);
            }

            foreach (var candidate in archived)
                System.IO.File.Delete(ProjectSettings.GlobalizePath(candidate.Source));

            archiveDescription = archived.Count == 0
                ? "no active save files found"
                : string.Join(", ", archived.Select(candidate => candidate.Destination));
            return true;
        }
        catch (Exception ex)
        {
            archiveDescription = ex.Message;
            return false;
        }
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
        WriteProfileAtomically(json, backupExisting);
    }

    public static SaveProfile CreateDefaultProfile()
    {
        var profile = new SaveProfile
        {
            Version = CurrentVersion,
            Chips = GameData.StartingChips,
            LinkTreeRewardLedgerInitialized = true,
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

#if DEBUG
    public static SaveProfile ResetLocalSave()
    {
        var profile = CreateDefaultProfile();
        Save(profile);
        return profile;
    }
#endif

    private static SaveProfile Normalize(SaveProfile profile)
    {
        if (profile.Version <= 0)
            profile.Version = 1;

        var loadedVersion = profile.Version;

        profile.Chips = Math.Max(0, profile.Chips);
        profile.OwnedItemIds ??= new List<int>();
        profile.OwnedItemCounts ??= new Dictionary<int, int>();
        profile.EquippedItemIdsByType ??= new Dictionary<string, int>();
        profile.NewItemIds ??= new List<int>();
        profile.AppliedLinkTreeRewardIds ??= new List<int>();
        profile.AppliedLinkTreeRewardIds = profile.AppliedLinkTreeRewardIds
            .Where(id => id > 0)
            .Distinct()
            .OrderBy(id => id)
            .ToList();
        if (loadedVersion < 14)
        {
            // Older builds had no durable local-reward ledger. Existing Steam receipts
            // become the first baseline and must not trigger historical chip grants.
            profile.LinkTreeRewardLedgerInitialized = false;
        }
        profile.BlindBoxClaimedCountsBySchedule ??= new Dictionary<int, int>();
        profile.BlindBoxRuntimeState ??= new BlindBoxRuntimeState();
        if (profile.PendingPlatformBlindBoxOpen is { } pendingPlatformOpen)
        {
            pendingPlatformOpen.InventoryQuantitiesBeforeExchange ??= new Dictionary<ulong, uint>();
            pendingPlatformOpen.ReservedChipCost = Math.Max(0, pendingPlatformOpen.ReservedChipCost);
            var validPlatformOpen = pendingPlatformOpen.BlindBoxId > 0
                && pendingPlatformOpen.ScheduleId > 0
                && pendingPlatformOpen.InputItemDefId > 0
                && pendingPlatformOpen.InputInstanceId > 0
                && pendingPlatformOpen.ExchangeTargetItemDefId > 0
                && LubanData.Tables.TbBlindBox.GetOrDefault(pendingPlatformOpen.BlindBoxId) != null
                && LubanData.Tables.TbBlindBoxSchedule.GetOrDefault(pendingPlatformOpen.ScheduleId) != null;
            if (!validPlatformOpen)
            {
                profile.Chips = checked(profile.Chips + pendingPlatformOpen.ReservedChipCost);
                profile.PendingPlatformBlindBoxOpen = null;
            }
        }
        if (profile.PendingLinkTreeClaim is { } pendingLinkTreeClaim
            && (pendingLinkTreeClaim.LinkTreeId <= 0
                || pendingLinkTreeClaim.SteamClaimBundleItemDefId <= 0
                || pendingLinkTreeClaim.SteamReceiptItemDefId <= 0))
        {
            profile.PendingLinkTreeClaim = null;
        }
        profile.LuckyDealBuffState ??= new LuckyDealBuffState();
        profile.LuckyDealBuffState.RemainingHands = Math.Max(0, profile.LuckyDealBuffState.RemainingHands);
        profile.LuckyDealBuffState.TriggerChance = Math.Clamp(profile.LuckyDealBuffState.TriggerChance, 0f, 1f);
        if (!Enum.IsDefined(profile.LuckyDealBuffState.LuckyDealMode)
            || profile.LuckyDealBuffState.LuckyDealMode == 0)
        {
            profile.LuckyDealBuffState.LuckyDealMode = DataTables.ELuckyDealMode.GuidedDraw;
        }
        profile.RefreshmentRuntimeState ??= new RefreshmentRuntimeState();
        profile.BlindBoxRuntimeState.LoopTrackStates ??= new Dictionary<int, BlindBoxScheduleState>();
        profile.BlindBoxRuntimeState.SteamPlaytimeDropStates ??=
            new Dictionary<int, BlindBoxSteamPlaytimeDropState>();
        profile.BlindBoxRuntimeState.DeferredPlatformScheduleCounts ??= new Dictionary<int, int>();
        profile.TotalPlaySeconds = Math.Max(0, profile.TotalPlaySeconds);

        var validIds = LubanData.Tables.TbItem.DataList
            .Select(item => item.Id)
            .ToHashSet();
        var initialItemIds = LubanData.Tables.TbItem.DataList
            .Where(item => item.AcquisitionType == DataTables.EAcquisitionType.Initial)
            .Select(item => item.Id)
            .ToHashSet();

        if (profile.OwnedItemCounts.Count == 0 && profile.OwnedItemIds.Count > 0)
            profile.OwnedItemCounts = profile.OwnedItemIds
                .Where(validIds.Contains)
                .Distinct()
                .ToDictionary(id => id, _ => 1);

        // Initial items are permanent local entitlements. Enforce this on every load so
        // table additions and platform inventory reconciliation cannot strip base visuals.
        foreach (var itemId in initialItemIds)
            profile.OwnedItemCounts.TryAdd(itemId, 1);

        if (loadedVersion < 4)
        {
            profile.Version = 4;
        }

        profile.OwnedItemCounts = profile.OwnedItemCounts
            .Where(pair => validIds.Contains(pair.Key) && pair.Value > 0)
            .OrderBy(pair => pair.Key)
            .ToDictionary(pair => pair.Key, pair => pair.Value);
        profile.OwnedItemIds = profile.OwnedItemCounts.Keys.ToList();

        profile.NewItemIds = profile.NewItemIds
            .Where(id => validIds.Contains(id)
                         && !initialItemIds.Contains(id)
                         && profile.OwnedItemCounts.ContainsKey(id))
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
        profile.BlindBoxRuntimeState.NextLoopPresentationSeconds = Math.Max(0, profile.BlindBoxRuntimeState.NextLoopPresentationSeconds);
        profile.BlindBoxRuntimeState.NextLoopTriggerSeconds = Math.Max(
            0,
            profile.BlindBoxRuntimeState.NextLoopTriggerSeconds);
        profile.BlindBoxRuntimeState.NextDeferredPlatformPresentationSeconds = Math.Max(
            0,
            profile.BlindBoxRuntimeState.NextDeferredPlatformPresentationSeconds);
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
        profile.BlindBoxRuntimeState.SteamPlaytimeDropStates = profile.BlindBoxRuntimeState.SteamPlaytimeDropStates
            .Where(pair => validScheduleIds.Contains(pair.Key) && pair.Value != null)
            .OrderBy(pair => pair.Key)
            .ToDictionary(
                pair => pair.Key,
                pair => new BlindBoxSteamPlaytimeDropState
                {
                    ProcessedGrantCount = Math.Max(0, pair.Value.ProcessedGrantCount),
                });
        profile.BlindBoxRuntimeState.DeferredPlatformScheduleCounts =
            profile.BlindBoxRuntimeState.DeferredPlatformScheduleCounts
                .Where(pair => validScheduleIds.Contains(pair.Key) && pair.Value > 0)
                .OrderBy(pair => pair.Key)
                .ToDictionary(pair => pair.Key, pair => pair.Value);

        if (loadedVersion < 5)
        {
            profile.BlindBoxRuntimeState.ScheduleSeconds = InferLegacyBlindBoxScheduleSeconds(profile);
            profile.Version = 5;
            GD.Print($"[Save] Migrated blind-box schedule clock to {profile.BlindBoxRuntimeState.ScheduleSeconds:0.0}s.");
        }
        else
        {
            profile.BlindBoxRuntimeState.ScheduleSeconds = Math.Max(
                0.0,
                profile.BlindBoxRuntimeState.ScheduleSeconds);
        }

        if (loadedVersion < 9)
        {
            profile.BlindBoxRuntimeState.LoopTrackStates.Clear();
            profile.BlindBoxRuntimeState.DeferredPlatformScheduleCounts.Clear();
            profile.BlindBoxRuntimeState.NextDeferredPlatformPresentationSeconds = 0.0;
            profile.BlindBoxRuntimeState.NextLoopPresentationSeconds = 0.0;
            profile.BlindBoxRuntimeState.NextLoopTriggerSeconds = 0.0;
            profile.BlindBoxRuntimeState.LockedLoopScheduleId = 0;
            profile.BlindBoxRuntimeState.LockedLoopBlindBoxId = 0;
            profile.BlindBoxRuntimeState.LoopStageStarted = false;
            profile.BlindBoxRuntimeState.LoopDropVerificationPending = false;
            if (profile.PendingPlatformBlindBoxOpen != null)
                profile.PendingPlatformBlindBoxOpen.IsDeferredBacklog = false;
            profile.Version = 9;
            GD.Print("[Save] Migrated blind-box loop state to fixed display points and Steam voucher backlog.");
        }

        if (loadedVersion < 10)
        {
            // The old field represented a directly granted receipt. New claims target a Bundle
            // and verify a separate permanent receipt, so an old in-flight transaction is unsafe
            // to reinterpret after an update.
            profile.PendingLinkTreeClaim = null;
            profile.Version = 10;
            GD.Print("[Save] Cleared legacy LinkTree claim transaction for Bundle-based rewards.");
        }

        var newPlayerScheduleCount = LubanData.Tables.TbBlindBoxSchedule.DataList.Count(
            schedule => schedule.IsEnabled && !schedule.IsLoopTrack);
        if (profile.BlindBoxRuntimeState.SequenceIndex < newPlayerScheduleCount)
        {
            profile.BlindBoxRuntimeState.LoopStageStarted = false;
            profile.BlindBoxRuntimeState.NextLoopPresentationSeconds = 0.0;
            profile.BlindBoxRuntimeState.NextLoopTriggerSeconds = 0.0;
            profile.BlindBoxRuntimeState.LockedLoopScheduleId = 0;
            profile.BlindBoxRuntimeState.LockedLoopBlindBoxId = 0;
            profile.BlindBoxRuntimeState.LoopDropVerificationPending = false;
        }
        else if (!profile.BlindBoxRuntimeState.LoopStageStarted)
        {
            profile.BlindBoxRuntimeState.LockedLoopScheduleId = 0;
            profile.BlindBoxRuntimeState.LockedLoopBlindBoxId = 0;
        }

        var lockedLoopSchedule = LubanData.Tables.TbBlindBoxSchedule.GetOrDefault(
            profile.BlindBoxRuntimeState.LockedLoopScheduleId);
        var lockedLoopBox = LubanData.Tables.TbBlindBox.GetOrDefault(
            profile.BlindBoxRuntimeState.LockedLoopBlindBoxId);
        if (lockedLoopSchedule is not { IsEnabled: true, IsLoopTrack: true }
            || lockedLoopBox is not { IsEnabled: true })
        {
            profile.BlindBoxRuntimeState.LockedLoopScheduleId = 0;
            profile.BlindBoxRuntimeState.LockedLoopBlindBoxId = 0;
        }

        if (loadedVersion < CurrentVersion)
            profile.Version = CurrentVersion;

        if (profile.PendingBlindBoxReward != null)
        {
            var pending = profile.PendingBlindBoxReward;
            var validPending = validIds.Contains(pending.ItemId)
                && LubanData.Tables.TbBlindBox.GetOrDefault(pending.BlindBoxId) != null
                && validScheduleIds.Contains(pending.ScheduleId);
            if (!validPending)
                profile.PendingBlindBoxReward = null;
        }

        var legacyRefreshmentItemId = TryGetLegacyRefreshmentItemId(
            profile.EquippedItemIdsByType,
            profile.OwnedItemCounts);
        var inventory = new PlayerInventory();
        inventory.LoadState(profile.OwnedItemCounts, profile.EquippedItemIdsByType, profile.NewItemIds, emitChanged: false);
        profile.EquippedItemIdsByType = inventory.GetEquippedIdsByTypeName();
        profile.RefreshmentRuntimeState = NormalizeRefreshmentRuntimeState(
            profile.RefreshmentRuntimeState,
            profile.LuckyDealBuffState,
            profile.OwnedItemCounts,
            legacyRefreshmentItemId);
        return profile;
    }

    private static int TryGetLegacyRefreshmentItemId(
        IReadOnlyDictionary<string, int> equippedItemIdsByType,
        IReadOnlyDictionary<int, int> ownedItemCounts)
    {
        if (!equippedItemIdsByType.TryGetValue(DataTables.EItemType.Refreshment.ToString(), out var itemId)
            || itemId <= 0
            || !ownedItemCounts.TryGetValue(itemId, out var count)
            || count <= 0)
            return 0;

        var item = LubanData.Tables.TbItem.GetOrDefault(itemId);
        return item is { ItemType: DataTables.EItemType.Refreshment } ? itemId : 0;
    }

    private static RefreshmentRuntimeState NormalizeRefreshmentRuntimeState(
        RefreshmentRuntimeState? state,
        LuckyDealBuffState luckyDealBuffState,
        IReadOnlyDictionary<int, int> ownedItemCounts,
        int legacyRefreshmentItemId)
    {
        state ??= new RefreshmentRuntimeState();

        var validRefreshmentIds = LubanData.Tables.TbItem.DataList
            .Where(item => item.ItemType == DataTables.EItemType.Refreshment)
            .Select(item => item.Id)
            .ToHashSet();

        if (!validRefreshmentIds.Contains(state.CurrentItemId)
            || !ownedItemCounts.TryGetValue(state.CurrentItemId, out var currentCount)
            || currentCount <= 0)
        {
            state.CurrentItemId = 0;
        }

        if (!validRefreshmentIds.Contains(state.BuffSourceItemId))
            state.BuffSourceItemId = 0;

        state.BuffTotalHands = Math.Max(0, state.BuffTotalHands);
        if (!Enum.IsDefined(typeof(TableRefreshmentStatus), state.Status))
            state.Status = TableRefreshmentStatus.Empty;

        if (luckyDealBuffState.RemainingHands <= 0)
        {
            if (state.Status == TableRefreshmentStatus.BuffActive)
                return new RefreshmentRuntimeState();

            if (state.CurrentItemId <= 0)
                state.CurrentItemId = legacyRefreshmentItemId > 0
                    ? legacyRefreshmentItemId
                    : GetFirstOwnedRefreshmentId(ownedItemCounts, validRefreshmentIds);

            state.Status = state.CurrentItemId > 0
                ? TableRefreshmentStatus.ReadyToUse
                : TableRefreshmentStatus.Empty;
            state.BuffSourceItemId = 0;
            state.BuffTotalHands = 0;
            return state;
        }

        if (state.Status == TableRefreshmentStatus.BuffActive && state.BuffSourceItemId > 0)
        {
            state.CurrentItemId = state.BuffSourceItemId;
            if (state.BuffTotalHands <= 0)
                state.BuffTotalHands = luckyDealBuffState.RemainingHands;
            return state;
        }

        return state.CurrentItemId > 0
            ? new RefreshmentRuntimeState
            {
                CurrentItemId = state.CurrentItemId,
                Status = TableRefreshmentStatus.ReadyToUse,
            }
            : new RefreshmentRuntimeState();
    }

    private static int GetFirstOwnedRefreshmentId(
        IReadOnlyDictionary<int, int> ownedItemCounts,
        IReadOnlySet<int> validRefreshmentIds)
    {
        return ownedItemCounts
            .Where(pair => pair.Value > 0 && validRefreshmentIds.Contains(pair.Key))
            .Select(pair => pair.Key)
            .OrderBy(id => id)
            .FirstOrDefault();
    }

    private static double InferLegacyBlindBoxScheduleSeconds(SaveProfile profile)
    {
        var config = LubanData.Tables.TbGameDevelopConfig.DataList.FirstOrDefault();
        var durationMultiplier = config == null || config.BlindBoxWaitDurationMultiplier <= 0
            ? 1.0
            : config.BlindBoxWaitDurationMultiplier;
        var scheduleSeconds = profile.TotalPlaySeconds / durationMultiplier;
        scheduleSeconds = Math.Max(scheduleSeconds, profile.BlindBoxRuntimeState.LastClaimSeconds);

        foreach (var (scheduleId, state) in profile.BlindBoxRuntimeState.LoopTrackStates)
        {
            if (state.ProcessedGrantCount <= 0)
                continue;

            var schedule = LubanData.Tables.TbBlindBoxSchedule.GetOrDefault(scheduleId);
            if (schedule == null)
                continue;

            var lastProcessedDue = schedule.IntervalSeconds <= 0
                ? schedule.StartSeconds
                : schedule.StartSeconds + (state.ProcessedGrantCount - 1) * schedule.IntervalSeconds;
            if (schedule.EndSeconds >= 0)
                lastProcessedDue = Math.Min(lastProcessedDue, schedule.EndSeconds);
            scheduleSeconds = Math.Max(scheduleSeconds, lastProcessedDue);
        }

        return Math.Max(0.0, scheduleSeconds);
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

    private static void WriteProfileAtomically(string json, bool backupExisting)
    {
        var temp = ProjectSettings.GlobalizePath(TempPath);
        var primary = ProjectSettings.GlobalizePath(SavePath);
        var backup = ProjectSettings.GlobalizePath(BackupPath);

        try
        {
            var bytes = Encoding.UTF8.GetBytes(json);
            using (var stream = new System.IO.FileStream(
                       temp,
                       System.IO.FileMode.Create,
                       System.IO.FileAccess.Write,
                       System.IO.FileShare.None,
                       4096,
                       System.IO.FileOptions.WriteThrough))
            {
                stream.Write(bytes);
                stream.Flush(flushToDisk: true);
            }

            if (!TryLoadVerified(TempPath, out _, out var tempFailure))
                throw new System.IO.InvalidDataException($"Temporary save verification failed: {tempFailure}.");

            bool primaryExists = System.IO.File.Exists(primary);
            bool primaryVerified = TryLoadVerified(SavePath, out _, out _);
            if (primaryExists && backupExisting && primaryVerified)
            {
                System.IO.File.Replace(temp, primary, backup, ignoreMetadataErrors: true);
            }
            else if (primaryExists)
            {
                // Never replace a known-good backup with an invalid primary.
                System.IO.File.Move(temp, primary, overwrite: true);
            }
            else
            {
                System.IO.File.Move(temp, primary);
            }

            if (!TryLoadVerified(SavePath, out _, out var committedFailure))
                throw new System.IO.InvalidDataException($"Committed save verification failed: {committedFailure}.");
            DiagnosticLog.Record("save_committed", new Dictionary<string, object?>
            {
                ["bytes"] = Encoding.UTF8.GetByteCount(json),
                ["backupUpdated"] = primaryExists && backupExisting && primaryVerified,
            });
        }
        finally
        {
            if (System.IO.File.Exists(temp))
                System.IO.File.Delete(temp);
        }
    }

    private static void RestoreVerifiedBackupAtomically()
    {
        var backup = ProjectSettings.GlobalizePath(BackupPath);
        var temp = ProjectSettings.GlobalizePath(TempPath);
        var primary = ProjectSettings.GlobalizePath(SavePath);

        System.IO.File.Copy(backup, temp, overwrite: true);
        using (var stream = new System.IO.FileStream(
                   temp,
                   System.IO.FileMode.Open,
                   System.IO.FileAccess.ReadWrite,
                   System.IO.FileShare.None))
            stream.Flush(flushToDisk: true);

        if (!TryLoadVerified(TempPath, out _, out var failure))
            throw new System.IO.InvalidDataException($"Backup restore verification failed: {failure}.");

        System.IO.File.Move(temp, primary, overwrite: true);
        GD.PushWarning("[Save] Restored the verified backup save atomically.");
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
