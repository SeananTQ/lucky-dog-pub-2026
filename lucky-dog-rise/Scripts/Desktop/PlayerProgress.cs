#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Godot;
using DataTables;
using IOFile = System.IO.File;

namespace LuckyDogRise;

/// <summary>事件来源决定是否应写入玩家长期进度。Debug 行为永不计入。</summary>
public enum PlayerProgressSource
{
    Gameplay,
    BlindBox,
    Debug,
}

public sealed class PlayerProgressProfile
{
    public const int CurrentVersion = 3;
    public int Version { get; set; } = CurrentVersion;
    public string OwnerProvider { get; set; } = "";
    public string OwnerAccountId { get; set; } = "";
    public Dictionary<string, long> Statistics { get; set; } = new();
    public Dictionary<string, long> PlatformStatisticBaselines { get; set; } = new();
    public bool ChipLedgerInitialized { get; set; }
    public long? ChipLedgerMigrationBalance { get; set; }
    public bool ChipLedgerMigrationMayPreserveLocalBalance { get; set; }
    public long PendingChipLedgerCredits { get; set; }
    public long PendingChipLedgerDebits { get; set; }
    public HashSet<string> OccurredEventKeys { get; set; } = new();
    public HashSet<string> UnlockedAchievementApiNames { get; set; } = new();
    public HashSet<string> PlatformSuppressedAchievementApiNames { get; set; } = new();
    public bool ExternalInventoryBackfilled { get; set; }
    public string PlaytestFirstObservedAtUtc { get; set; } = "";
    public long PokerGuidanceRuntimeBaseline { get; set; }
    public long PokerGuidanceHandsBaseline { get; set; }
    public string UpdatedAt { get; set; } = "";
}

public readonly record struct PlatformStatisticSyncState(
    string StatisticKey,
    string ApiName,
    EPlayerStatisticType StatisticType,
    long LocalValue,
    bool HasBaseline,
    long BaselineValue);

/// <summary>
/// 与可重置游戏存档分离的账号级长期进度。
/// 未接平台时只用于保留未来同步所需的事实与提供 DEBUG 控制台验收。
/// </summary>
public sealed class PlayerProgress
{
    public const string ChipLedgerCreditsStatisticKey = "ChipLedgerCredits";
    public const string ChipLedgerDebitsStatisticKey = "ChipLedgerDebits";

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly AccountStorageContext _storageContext;
    private string SavePath => _storageContext.PlayerProgressPath;
    private string BackupPath => _storageContext.PlayerProgressBackupPath;
    private string TempPath => _storageContext.PlayerProgressTempPath;
    private readonly Dictionary<string, PlayerStatistic> _statisticsByKey;
    private readonly Achievement[] _enabledAchievements;
    private readonly HashSet<string> _enabledAchievementApiNames;
    private readonly Dictionary<EItemType, string> _externalItemStatisticKeys = new()
    {
        [EItemType.Dog] = "ExternalDogAcquiredCount",
        [EItemType.Headwear] = "ExternalHeadwearAcquiredCount",
        [EItemType.Eyewear] = "ExternalEyewearAcquiredCount",
        [EItemType.Arm] = "ExternalArmAcquiredCount",
        [EItemType.Clothes] = "ExternalClothesAcquiredCount",
        [EItemType.Table] = "ExternalTableAcquiredCount",
        [EItemType.Background] = "ExternalBackgroundAcquiredCount",
        [EItemType.Accessory] = "ExternalAccessoryAcquiredCount",
        [EItemType.Refreshment] = "ExternalRefreshmentAcquiredCount",
        [EItemType.CardBack] = "ExternalCardBackAcquiredCount",
        [EItemType.CardFace] = "ExternalCardFaceAcquiredCount",
        [EItemType.BodyDecoration] = "ExternalBodyDecorationAcquiredCount",
    };
    private readonly Dictionary<ERarity, string> _externalRarityStatisticKeys = new()
    {
        [ERarity.Common] = "ExternalCommonItemAcquiredCount",
        [ERarity.Uncommon] = "ExternalUncommonItemAcquiredCount",
        [ERarity.Rare] = "ExternalRareItemAcquiredCount",
        [ERarity.Epic] = "ExternalEpicItemAcquiredCount",
        [ERarity.Legendary] = "ExternalLegendaryItemAcquiredCount",
        [ERarity.Mythic] = "ExternalMythicItemAcquiredCount",
    };
    private readonly Dictionary<EHandRank, string> _handRankStatisticKeys = new()
    {
        [EHandRank.Nothing] = "PokerHandRankNothingCount",
        [EHandRank.OnePair] = "PokerHandRankOnePairCount",
        [EHandRank.TwoPair] = "PokerHandRankTwoPairCount",
        [EHandRank.ThreeOfAKind] = "PokerHandRankThreeOfAKindCount",
        [EHandRank.Straight] = "PokerHandRankStraightCount",
        [EHandRank.Flush] = "PokerHandRankFlushCount",
        [EHandRank.FullHouse] = "PokerHandRankFullHouseCount",
        [EHandRank.FourOfAKind] = "PokerHandRankFourOfAKindCount",
        [EHandRank.StraightFlush] = "PokerHandRankStraightFlushCount",
        [EHandRank.RoyalFlush] = "PokerHandRankRoyalFlushCount",
    };
    private readonly Dictionary<string, double> _durationRemainders = new();
    private PlayerProgressProfile _profile;
    private bool _dirty;
    private bool _immediateSaveRequested;
    private DateTime _inputBucketStart;
    private long _inputBucketChips;
    private bool _writesFrozen;

#if DEBUG
    private long _debugMultiplier = 1;
    private PlayerProgressProfile? _debugSimulationProfileSnapshot;
    private Dictionary<string, double>? _debugSimulationDurationRemaindersSnapshot;
    private DateTime _debugSimulationInputBucketStart;
    private long _debugSimulationInputBucketChips;
#endif

    public PlayerProgress(AccountStorageContext storageContext)
    {
        _storageContext = storageContext ?? throw new ArgumentNullException(nameof(storageContext));
        EnsureStorageDirectory();
        _statisticsByKey = LubanData.Tables.TbPlayerStatistic.DataList
            .Where(stat => stat.IsEnabled)
            .ToDictionary(stat => stat.StatisticKey, StringComparer.Ordinal);
        _enabledAchievements = LubanData.Tables.TbAchievement.DataList
            .Where(achievement => achievement.IsEnabled)
            .ToArray();
        _enabledAchievementApiNames = _enabledAchievements
            .Select(achievement => achievement.ApiName)
            .ToHashSet(StringComparer.Ordinal);
        _profile = LoadOrCreate();
        GD.Print(
            $"[PlayerProgress] Loaded account={_storageContext}, Version={_profile.Version}, Path={AbsoluteSavePath}");
        ValidateDefinitions();
        EvaluateHistoricalAchievements();
    }

    public string AbsoluteSavePath => ProjectSettings.GlobalizePath(SavePath);
    public IReadOnlyDictionary<string, long> Statistics => _profile.Statistics
        .Where(pair => _statisticsByKey.ContainsKey(pair.Key))
        .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
    public long GlobalInputChipsEarned => GetStatistic("GlobalInputChipsEarned");
    public bool IsChipLedgerAvailable =>
        IsCounterDefinition(ChipLedgerCreditsStatisticKey)
        && IsCounterDefinition(ChipLedgerDebitsStatisticKey);
    public bool IsChipLedgerInitialized => _profile.ChipLedgerInitialized && IsChipLedgerAvailable;
    public long ChipLedgerCredits => GetStatistic(ChipLedgerCreditsStatisticKey);
    public long ChipLedgerDebits => GetStatistic(ChipLedgerDebitsStatisticKey);
    public IReadOnlyCollection<string> UnlockedAchievementApiNames => _profile.UnlockedAchievementApiNames
        .Where(_enabledAchievementApiNames.Contains)
        .ToArray();
    public int PlatformSuppressedAchievementCount => _profile.PlatformSuppressedAchievementApiNames.Count(
        _enabledAchievementApiNames.Contains);
    public bool IsDirty => _dirty;
    public bool RequiresImmediateSave => _dirty && _immediateSaveRequested;
    public bool IsPlatformSyncAllowed
    {
        get
        {
#if DEBUG
            return !_writesFrozen && _debugSimulationProfileSnapshot == null && _debugMultiplier == 1;
#else
            return !_writesFrozen;
#endif
        }
    }

    public IEnumerable<string> GetPlatformSyncEligibleAchievementApiNames()
    {
#if DEBUG
        if (_debugSimulationProfileSnapshot != null)
            return Array.Empty<string>();
#endif
        return _profile.UnlockedAchievementApiNames.Where(apiName =>
            _enabledAchievementApiNames.Contains(apiName)
            && !_profile.PlatformSuppressedAchievementApiNames.Contains(apiName));
    }

    public IReadOnlyList<PlatformStatisticSyncState> GetPlatformSyncStatisticStates()
    {
        if (!IsPlatformSyncAllowed)
            return [];

        return _statisticsByKey.Values
            .Where(definition => definition.SyncToPlatform
                                 && !string.IsNullOrWhiteSpace(definition.PlatformApiName))
            .Select(definition => new PlatformStatisticSyncState(
                definition.StatisticKey,
                definition.PlatformApiName,
                definition.StatisticType,
                GetStatistic(definition.StatisticKey),
                _profile.PlatformStatisticBaselines.TryGetValue(definition.StatisticKey, out _),
                _profile.PlatformStatisticBaselines.GetValueOrDefault(definition.StatisticKey)))
            .OrderBy(state => state.ApiName, StringComparer.Ordinal)
            .ToArray();
    }

    public void CommitPlatformStatisticSync(string statisticKey, long synchronizedValue)
    {
        if (!_statisticsByKey.TryGetValue(statisticKey, out var definition)
            || !definition.SyncToPlatform
            || synchronizedValue < 0)
            return;

        if (definition.StatisticType == EPlayerStatisticType.Flag)
            synchronizedValue = synchronizedValue > 0 ? 1 : 0;

        var changed = GetStatistic(statisticKey) != synchronizedValue;
        if (changed)
        {
            _profile.Statistics[statisticKey] = synchronizedValue;
            EvaluateStatisticAchievements(statisticKey);
        }

        if (_profile.PlatformStatisticBaselines.TryGetValue(statisticKey, out var baseline)
            && baseline == synchronizedValue
            && !changed)
            return;

        _profile.PlatformStatisticBaselines[statisticKey] = synchronizedValue;
        _dirty = true;
        RequestImmediateSave();
    }

    /// <summary>
    /// Captures the local balance that existed before the first successful Steam ledger read.
    /// It is only used when both remote ledger values are zero, so a fresh device cannot
    /// grant its starting chips to an account that already has a remote balance.
    /// </summary>
    public void PrepareChipLedgerMigration(long currentLocalBalance, bool mayPreserveLocalBalance)
    {
        currentLocalBalance = Math.Max(0, currentLocalBalance);
        if (!IsChipLedgerAvailable || IsChipLedgerInitialized || _profile.ChipLedgerMigrationBalance.HasValue)
            return;

        _profile.ChipLedgerMigrationBalance = currentLocalBalance;
        _profile.ChipLedgerMigrationMayPreserveLocalBalance = mayPreserveLocalBalance;
        _dirty = true;
        RequestImmediateSave();
    }

    /// <summary>
    /// Completes first-use migration only after both Steam ledger stats were read.
    /// Remote data wins over the local migration candidate; locally earned/spent chips
    /// accumulated while waiting for Steam are then appended to that remote ledger.
    /// </summary>
    public bool TryCompleteChipLedgerMigration()
    {
        if (!IsChipLedgerAvailable || IsChipLedgerInitialized)
            return IsChipLedgerInitialized;

        if (!_profile.PlatformStatisticBaselines.ContainsKey(ChipLedgerCreditsStatisticKey)
            || !_profile.PlatformStatisticBaselines.ContainsKey(ChipLedgerDebitsStatisticKey))
            return false;

        var (credits, debits) = CalculateInitialChipLedger(
            ChipLedgerCredits,
            ChipLedgerDebits,
            _profile.ChipLedgerMigrationBalance.GetValueOrDefault(),
            _profile.ChipLedgerMigrationMayPreserveLocalBalance,
            _profile.PendingChipLedgerCredits,
            _profile.PendingChipLedgerDebits);
        _profile.Statistics[ChipLedgerCreditsStatisticKey] = credits;
        _profile.Statistics[ChipLedgerDebitsStatisticKey] = debits;
        _profile.ChipLedgerInitialized = true;
        _profile.ChipLedgerMigrationBalance = null;
        _profile.ChipLedgerMigrationMayPreserveLocalBalance = false;
        _profile.PendingChipLedgerCredits = 0;
        _profile.PendingChipLedgerDebits = 0;
        _dirty = true;
        RequestImmediateSave();
        return true;
    }

    public long RecoverChipLedgerBalance(long currentLocalBalance)
    {
        currentLocalBalance = Math.Max(0, currentLocalBalance);
        if (!IsChipLedgerInitialized)
            return currentLocalBalance;

        var ledgerBalance = GetChipLedgerBalance();
        if (currentLocalBalance <= ledgerBalance)
            return ledgerBalance;

        RecordCounter(
            ChipLedgerCreditsStatisticKey,
            currentLocalBalance - ledgerBalance,
            PlayerProgressSource.Gameplay,
            applyDebugMultiplier: false);
        RequestImmediateSave();
        return currentLocalBalance;
    }

    public void RecordChipBalanceDelta(int delta, PlayerProgressSource source)
    {
        if (source == PlayerProgressSource.Debug || delta == 0 || !IsChipLedgerAvailable)
            return;

        if (!IsChipLedgerInitialized)
        {
            if (delta > 0)
                _profile.PendingChipLedgerCredits = checked(_profile.PendingChipLedgerCredits + delta);
            else
                _profile.PendingChipLedgerDebits = checked(_profile.PendingChipLedgerDebits - (long)delta);
            _dirty = true;
            RequestImmediateSave();
            return;
        }

        if (delta > 0)
            RecordCounter(ChipLedgerCreditsStatisticKey, delta, source, applyDebugMultiplier: false);
        else
            RecordCounter(ChipLedgerDebitsStatisticKey, -(long)delta, source, applyDebugMultiplier: false);
    }

    public long GetChipLedgerBalance()
    {
        if (!IsChipLedgerInitialized)
            return 0;

        return CalculateChipLedgerBalance(ChipLedgerCredits, ChipLedgerDebits);
    }

    internal static long CalculateChipLedgerBalance(long credits, long debits) =>
        Math.Max(0, Math.Max(0, credits) - Math.Max(0, debits));

    internal static (long Credits, long Debits) CalculateInitialChipLedger(
        long remoteCredits,
        long remoteDebits,
        long migrationBalance,
        bool mayPreserveLocalBalance,
        long pendingCredits,
        long pendingDebits)
    {
        var credits = Math.Max(0, remoteCredits);
        var debits = Math.Max(0, remoteDebits);
        migrationBalance = Math.Max(0, migrationBalance);
        var remoteBalance = CalculateChipLedgerBalance(credits, debits);
        if (credits == 0 && debits == 0)
            credits = migrationBalance;
        else if (mayPreserveLocalBalance && migrationBalance > remoteBalance)
            credits = checked(credits + migrationBalance - remoteBalance);

        return (
            checked(credits + Math.Max(0, pendingCredits)),
            checked(debits + Math.Max(0, pendingDebits)));
    }

    /// <summary>平台侧已解锁项是账号事实；合并到本地并解除旧的 Debug 上传抑制。</summary>
    public int ImportPlatformAchievements(IEnumerable<string> achievementApiNames)
    {
        var changedCount = 0;
        foreach (var apiName in achievementApiNames
                     .Where(apiName => !string.IsNullOrWhiteSpace(apiName))
                     .Where(_enabledAchievementApiNames.Contains)
                     .Distinct(StringComparer.Ordinal))
        {
            var added = _profile.UnlockedAchievementApiNames.Add(apiName);
            var unsuppressed = _profile.PlatformSuppressedAchievementApiNames.Remove(apiName);
            if (!added && !unsuppressed)
                continue;

            changedCount++;
            _dirty = true;
            RequestImmediateSave();
        }
        return changedCount;
    }

    public void RecordAppLaunch() => RecordCounter("AppLaunchCount", 1, PlayerProgressSource.Gameplay);

    public void RecordPlaytestLaunch(DateTimeOffset observedAtUtc)
    {
        if (BuildInfo.Channel != BuildChannel.Playtest)
            return;

        observedAtUtc = observedAtUtc.ToUniversalTime();
        RecordFlag("PlaytestCohortMember", PlayerProgressSource.Gameplay);
        if (!DateTimeOffset.TryParse(
                _profile.PlaytestFirstObservedAtUtc,
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.AssumeUniversal
                | System.Globalization.DateTimeStyles.AdjustToUniversal,
                out var firstObservedAtUtc))
        {
            _profile.PlaytestFirstObservedAtUtc = observedAtUtc.ToString("O");
            _profile.PokerGuidanceRuntimeBaseline = GetStatistic("GameRuntimeSeconds");
            _profile.PokerGuidanceHandsBaseline = GetStatistic("PokerHandsPlayed");
            _dirty = true;
            RequestImmediateSave();
            return;
        }

        foreach (var statisticKey in GetReturnStatisticKeys(observedAtUtc - firstObservedAtUtc))
            RecordFlag(statisticKey, PlayerProgressSource.Gameplay);
    }

    public void RecordPokerBasicsGuidanceCompleted()
    {
        if (BuildInfo.Channel != BuildChannel.Playtest
            || GetStatistic("PokerBasicsGuidanceCompleted") > 0)
            return;

        var completionSeconds = Math.Max(
            0,
            GetStatistic("GameRuntimeSeconds") - _profile.PokerGuidanceRuntimeBaseline);
        var completionHands = Math.Max(
            0,
            GetStatistic("PokerHandsPlayed") - _profile.PokerGuidanceHandsBaseline);
        if (completionSeconds > 0)
            RecordCounter("PokerBasicsGuidanceCompletionSeconds", completionSeconds, PlayerProgressSource.Gameplay);
        if (completionHands > 0)
            RecordCounter("PokerBasicsGuidanceCompletionHandsPlayed", completionHands, PlayerProgressSource.Gameplay);
        RecordFlag("PokerBasicsGuidanceCompleted", PlayerProgressSource.Gameplay);
    }

    public void RecordPlaytestSteamBlindBoxClaim(int scheduleId, bool isPlatformReward, bool completesSchedule)
    {
        if (BuildInfo.Channel != BuildChannel.Playtest)
            return;

        var statisticKey = GetSteamBlindBoxClaimStatisticKey(
            scheduleId,
            isPlatformReward,
            completesSchedule);
        if (statisticKey.Length > 0)
            RecordFlag(statisticKey, PlayerProgressSource.BlindBox);
    }

    internal static IReadOnlyList<string> GetReturnStatisticKeys(TimeSpan elapsed)
    {
        var keys = new List<string>(3);
        if (elapsed >= TimeSpan.FromHours(24))
            keys.Add("ReturnedAfter24Hours");
        if (elapsed >= TimeSpan.FromHours(72))
            keys.Add("ReturnedAfter72Hours");
        if (elapsed >= TimeSpan.FromHours(168))
            keys.Add("ReturnedAfter168Hours");
        return keys;
    }

    internal static string GetSteamBlindBoxClaimStatisticKey(
        int scheduleId,
        bool isPlatformReward,
        bool completesSchedule)
    {
        if (!isPlatformReward || !completesSchedule)
            return string.Empty;

        return scheduleId switch
        {
            1001 => "NewcomerBox1SteamRewardClaimed",
            1004 => "NewcomerBox4SteamRewardClaimed",
            2001 => "FirstLoopBoxSteamRewardClaimed",
            _ => string.Empty,
        };
    }

    public void RecordPlaytestLinkTreeRewardClaimed()
    {
        if (BuildInfo.Channel == BuildChannel.Playtest)
            RecordFlag("AnyLinkTreeRewardClaimed", PlayerProgressSource.Gameplay);
    }

    public void RecordDuration(string statisticKey, double seconds, PlayerProgressSource source)
    {
        if (source == PlayerProgressSource.Debug || seconds <= 0.0)
            return;

        _durationRemainders.TryGetValue(statisticKey, out var remainder);
        var total = remainder + seconds;
        var wholeSeconds = (long)Math.Floor(total);
        _durationRemainders[statisticKey] = total - wholeSeconds;
        if (wholeSeconds > 0)
            RecordCounter(statisticKey, wholeSeconds, source);
    }

    public void RecordInputChips(int count, PlayerProgressSource source)
    {
        if (source == PlayerProgressSource.Debug || count <= 0)
            return;

        RecordCounter("GlobalInputCount", count, source);
        RecordCounter("GlobalInputChipsEarned", count, source);

        var now = DateTime.Now;
        var bucketStart = new DateTime(now.Year, now.Month, now.Day, now.Hour, now.Minute < 30 ? 0 : 30, 0);
        if (_inputBucketStart != default && bucketStart != _inputBucketStart)
        {
            RecordMaximum("PeakHalfHourInputChipsEarned", _inputBucketChips, source);
            _inputBucketChips = 0;
        }

        _inputBucketStart = bucketStart;
        _inputBucketChips = checked(_inputBucketChips + count);
        _dirty = true;
    }

    public void RecordPokerHandStarted(int bet, PlayerProgressSource source)
    {
        RecordCounter("PokerHandsPlayed", 1, source);
        RecordCounter("PokerChipsWagered", bet, source);
    }

    public void RecordPokerHandResolved(EHandRank rank, int payout, bool askedDogHint, PlayerProgressSource source)
    {
        if (source == PlayerProgressSource.Debug)
            return;

        if (_handRankStatisticKeys.TryGetValue(rank, out var rankKey))
            RecordCounter(rankKey, 1, source);
        RecordCounter(payout > 0 ? "PokerHandsWon" : "PokerHandsLost", 1, source);
        if (payout > 0 && askedDogHint)
            RecordCounter("DogHintAskedWinningHandCount", 1, source);
    }

    public void RecordPokerPayoutCollected(int payout, PlayerProgressSource source) =>
        RecordCounter("PokerChipsWon", payout, source);

    public void RecordExternalItemAcquired(Item item, int count, PlayerProgressSource source)
    {
        if (source == PlayerProgressSource.Debug
            || count <= 0
            || item.AcquisitionType is EAcquisitionType.Initial or EAcquisitionType.Retired)
            return;

        RecordCounter("ExternalItemAcquiredCount", count, source);
        if (source == PlayerProgressSource.BlindBox)
            RecordCounter("BlindBoxItemAcquiredCount", count, source);
        var itemTypeTrackingEnabled = TryGetEnabledExternalItemStatisticKey(item.ItemType, out var itemKey);
        if (itemTypeTrackingEnabled)
            RecordCounter(itemKey, count, source);
        if (_externalRarityStatisticKeys.TryGetValue(item.ItemRarity, out var rarityKey))
            RecordCounter(rarityKey, count, source);

        EvaluateAchievements(achievement => achievement.RuleType switch
        {
            EAchievementRuleType.FirstExternalItemType => itemTypeTrackingEnabled
                && string.Equals(achievement.TargetKey, item.ItemType.ToString(), StringComparison.Ordinal),
            EAchievementRuleType.FirstExternalItemRarity => string.Equals(achievement.TargetKey, item.ItemRarity.ToString(), StringComparison.Ordinal),
            _ => false,
        });
        RequestImmediateSave();
    }

    /// <summary>为成就系统上线前已有的本地存档补建非初始物品统计与首次成就。</summary>
    public void BackfillExternalInventory(PlayerInventory inventory)
    {
        if (_profile.ExternalInventoryBackfilled)
            return;

        foreach (var (itemId, count) in inventory.GetOwnedItemCounts())
        {
            var item = LubanData.Tables.TbItem.GetOrDefault(itemId);
            if (item != null)
                RecordExternalItemAcquired(item, count, PlayerProgressSource.Gameplay);
        }

        _profile.ExternalInventoryBackfilled = true;
        _dirty = true;
        RequestImmediateSave();
    }

    public void RecordBlindBoxOpened(PlayerProgressSource source) => RecordCounter("BlindBoxOpenedCount", 1, source);
    public void RecordBlindBoxChipsSpent(int chips, PlayerProgressSource source) => RecordCounter("BlindBoxChipsSpent", chips, source);
    public void RecordBlindBoxRewardClaimed(PlayerProgressSource source) => RecordCounter("BlindBoxRewardClaimedCount", 1, source);

    public void RecordFirstEvent(string eventKey, PlayerProgressSource source)
    {
        if (source == PlayerProgressSource.Debug || !_profile.OccurredEventKeys.Add(eventKey))
            return;

        _dirty = true;
        EvaluateAchievements(achievement => achievement.RuleType == EAchievementRuleType.FirstEvent
            && string.Equals(achievement.TargetKey, eventKey, StringComparison.Ordinal));
        RequestImmediateSave();
    }

    public void FlushSession()
    {
        if (_inputBucketStart != default && _inputBucketChips > 0)
            RecordMaximum("PeakHalfHourInputChipsEarned", _inputBucketChips, PlayerProgressSource.Gameplay);
        _inputBucketStart = default;
        _inputBucketChips = 0;
        SaveIfDirty();
    }

    public void SaveIfDirty()
    {
        if (!_dirty || _writesFrozen)
            return;

#if DEBUG
        if (_debugSimulationProfileSnapshot != null)
        {
            _dirty = false;
            _immediateSaveRequested = false;
            return;
        }
#endif

        try
        {
            _profile.Version = PlayerProgressProfile.CurrentVersion;
            _profile.OwnerProvider = _storageContext.Provider;
            _profile.OwnerAccountId = _storageContext.AccountId;
            _profile.UpdatedAt = DateTimeOffset.UtcNow.ToString("O");
            var json = JsonSerializer.Serialize(_profile, JsonOptions);
            var absoluteSavePath = ProjectSettings.GlobalizePath(SavePath);
            var absoluteBackupPath = ProjectSettings.GlobalizePath(BackupPath);
            var absoluteTempPath = ProjectSettings.GlobalizePath(TempPath);

            IOFile.WriteAllText(absoluteTempPath, json);
            if (IOFile.Exists(absoluteSavePath))
                IOFile.Replace(absoluteTempPath, absoluteSavePath, absoluteBackupPath, ignoreMetadataErrors: true);
            else
                IOFile.Move(absoluteTempPath, absoluteSavePath);

            _dirty = false;
            _immediateSaveRequested = false;
        }
        catch (Exception exception)
        {
            GD.PushError($"[PlayerProgress] Failed to save progress: {exception.Message}");
        }
    }

    public void Reset()
    {
        _profile = CreateEmptyProfile();
        _durationRemainders.Clear();
        _inputBucketStart = default;
        _inputBucketChips = 0;
        _dirty = true;
        SaveIfDirty();
        GD.Print($"[PlayerProgress] Reset local progress: {AbsoluteSavePath}");
    }

    public void FreezeWrites(string reason)
    {
        _writesFrozen = true;
        GD.PushError($"[PlayerProgress] Account storage writes frozen: {reason}");
    }

#if DEBUG
    public bool IsDebugSimulationActive => _debugSimulationProfileSnapshot != null;

    public bool BeginDebugSimulation()
    {
        if (BuildInfo.Channel != BuildChannel.Dev || _debugSimulationProfileSnapshot != null)
            return false;

        SaveIfDirty();
        _debugSimulationProfileSnapshot = _profile;
        _debugSimulationDurationRemaindersSnapshot = new Dictionary<string, double>(_durationRemainders);
        _debugSimulationInputBucketStart = _inputBucketStart;
        _debugSimulationInputBucketChips = _inputBucketChips;
        _profile = CreateEmptyProfile();
        _durationRemainders.Clear();
        _inputBucketStart = default;
        _inputBucketChips = 0;
        _dirty = false;
        _immediateSaveRequested = false;
        GD.Print("[PlayerProgress] Entered in-memory blind-box debug simulation; platform sync is disabled.");
        return true;
    }

    public void EndDebugSimulation()
    {
        if (_debugSimulationProfileSnapshot == null)
            return;

        _profile = _debugSimulationProfileSnapshot;
        _debugSimulationProfileSnapshot = null;
        _durationRemainders.Clear();
        if (_debugSimulationDurationRemaindersSnapshot != null)
        {
            foreach (var (key, value) in _debugSimulationDurationRemaindersSnapshot)
                _durationRemainders[key] = value;
        }
        _debugSimulationDurationRemaindersSnapshot = null;
        _inputBucketStart = _debugSimulationInputBucketStart;
        _inputBucketChips = _debugSimulationInputBucketChips;
        _dirty = false;
        _immediateSaveRequested = false;
        GD.Print("[PlayerProgress] Left blind-box debug simulation and restored real local progress.");
    }

    public void SetDebugMultiplier(int multiplier)
    {
        _debugMultiplier = Math.Max(1, multiplier);
        GD.Print($"[PlayerProgress] DEBUG statistic multiplier set to x{_debugMultiplier}.");
    }
#endif

    private void RecordCounter(
        string statisticKey,
        long amount,
        PlayerProgressSource source,
        bool applyDebugMultiplier = true)
    {
        if (source == PlayerProgressSource.Debug || amount <= 0)
            return;
        if (!_statisticsByKey.TryGetValue(statisticKey, out var definition))
            return;
        if (definition.StatisticType != EPlayerStatisticType.Counter)
        {
            GD.PushError($"[PlayerProgress] Statistic '{statisticKey}' is not a Counter.");
            return;
        }

        var applied = applyDebugMultiplier ? ApplyMultiplier(amount, source) : amount;
        _profile.Statistics[statisticKey] = checked(GetStatistic(statisticKey) + applied);
        _dirty = true;
        EvaluateStatisticAchievements(statisticKey);
    }

    private void RecordMaximum(string statisticKey, long value, PlayerProgressSource source)
    {
        if (source == PlayerProgressSource.Debug || value < 0)
            return;
        if (!_statisticsByKey.TryGetValue(statisticKey, out var definition))
            return;
        if (definition.StatisticType != EPlayerStatisticType.Maximum)
        {
            GD.PushError($"[PlayerProgress] Statistic '{statisticKey}' is not a Maximum.");
            return;
        }

        var applied = ApplyMultiplier(value, source);
        if (applied <= GetStatistic(statisticKey))
            return;

        _profile.Statistics[statisticKey] = applied;
        _dirty = true;
        EvaluateStatisticAchievements(statisticKey);
    }

    private void RecordFlag(string statisticKey, PlayerProgressSource source)
    {
        if (source == PlayerProgressSource.Debug
            || !_statisticsByKey.TryGetValue(statisticKey, out var definition))
            return;
        if (definition.StatisticType != EPlayerStatisticType.Flag)
        {
            GD.PushError($"[PlayerProgress] Statistic '{statisticKey}' is not a Flag.");
            return;
        }
        if (GetStatistic(statisticKey) > 0)
            return;

        _profile.Statistics[statisticKey] = 1;
        _dirty = true;
        EvaluateStatisticAchievements(statisticKey);
        RequestImmediateSave();
    }

    private long GetStatistic(string statisticKey) => _profile.Statistics.TryGetValue(statisticKey, out var value) ? value : 0L;

    private bool IsCounterDefinition(string statisticKey) =>
        _statisticsByKey.TryGetValue(statisticKey, out var definition)
        && definition.StatisticType == EPlayerStatisticType.Counter;

    private long ApplyMultiplier(long amount, PlayerProgressSource source)
    {
#if DEBUG
        if (source != PlayerProgressSource.Debug)
            return checked(amount * _debugMultiplier);
#endif
        return amount;
    }

    private void EvaluateStatisticAchievements(string changedStatisticKey) =>
        EvaluateAchievements(achievement => achievement.RuleType == EAchievementRuleType.StatisticAtLeast
            && string.Equals(achievement.TargetKey, changedStatisticKey, StringComparison.Ordinal)
            && GetStatistic(changedStatisticKey) >= achievement.TargetValue);

    /// <summary>
    /// Re-evaluates every data-defined achievement against persisted player facts.
    /// This runs on every startup so adding a new achievement row can unlock it
    /// for players who already satisfied its condition in an earlier version.
    /// </summary>
    private void EvaluateHistoricalAchievements()
    {
        var unlockedCount = 0;
        foreach (var achievement in _enabledAchievements)
        {
            if (!IsHistoricalAchievementSatisfied(achievement) || !UnlockAchievement(achievement.ApiName))
                continue;

            unlockedCount++;
        }

        if (unlockedCount <= 0)
            return;

        _dirty = true;
        RequestImmediateSave();
        GD.Print($"[Achievement] Historical re-evaluation unlocked {unlockedCount} achievement(s).");
    }

    private bool IsHistoricalAchievementSatisfied(Achievement achievement)
    {
        return achievement.RuleType switch
        {
            EAchievementRuleType.StatisticAtLeast =>
                GetStatistic(achievement.TargetKey) >= achievement.TargetValue,
            EAchievementRuleType.FirstEvent =>
                _profile.OccurredEventKeys.Contains(achievement.TargetKey),
            EAchievementRuleType.FirstExternalItemType =>
                TryGetExternalItemTypeAcquisitionCount(achievement.TargetKey, out var itemTypeCount)
                && itemTypeCount >= Math.Max(1, achievement.TargetValue),
            EAchievementRuleType.FirstExternalItemRarity =>
                TryGetExternalItemRarityAcquisitionCount(achievement.TargetKey, out var rarityCount)
                && rarityCount >= Math.Max(1, achievement.TargetValue),
            _ => false,
        };
    }

    private bool TryGetExternalItemTypeAcquisitionCount(string itemTypeName, out long count)
    {
        count = 0;
        if (!Enum.TryParse<EItemType>(itemTypeName, ignoreCase: false, out var itemType)
            || !TryGetEnabledExternalItemStatisticKey(itemType, out var statisticKey))
            return false;

        count = GetStatistic(statisticKey);
        return true;
    }

    private bool TryGetEnabledExternalItemStatisticKey(EItemType itemType, out string statisticKey)
    {
        if (_externalItemStatisticKeys.TryGetValue(itemType, out statisticKey!)
            && _statisticsByKey.ContainsKey(statisticKey))
            return true;

        statisticKey = string.Empty;
        return false;
    }

    private bool TryGetExternalItemRarityAcquisitionCount(string rarityName, out long count)
    {
        count = 0;
        if (!Enum.TryParse<ERarity>(rarityName, ignoreCase: false, out var rarity)
            || !_externalRarityStatisticKeys.TryGetValue(rarity, out var statisticKey))
            return false;

        count = GetStatistic(statisticKey);
        return true;
    }

    private void EvaluateAchievements(Func<Achievement, bool> predicate)
    {
        foreach (var achievement in _enabledAchievements.Where(predicate))
        {
            if (!UnlockAchievement(achievement.ApiName))
                continue;

            _dirty = true;
            RequestImmediateSave();
#if DEBUG
            GD.Print($"[Achievement] Satisfied: ID={achievement.AchievementId} | {achievement.Notes} | ApiName={achievement.ApiName} (rule={achievement.RuleType}, target={achievement.TargetKey})");
#endif
        }
    }

    private bool UnlockAchievement(string apiName)
    {
        if (!_profile.UnlockedAchievementApiNames.Add(apiName))
            return false;

#if DEBUG
        if (_debugSimulationProfileSnapshot != null || _debugMultiplier != 1)
        {
            _profile.PlatformSuppressedAchievementApiNames.Add(apiName);
            var reason = _debugSimulationProfileSnapshot != null
                ? "blind-box debug simulation is active"
                : $"DEBUG multiplier is x{_debugMultiplier}";
            GD.Print($"[Achievement] Platform upload suppressed because {reason}: {apiName}");
        }
#endif
        return true;
    }

    private PlayerProgressProfile LoadOrCreate()
    {
        if (TryLoad(SavePath, out var profile))
            return profile;

        if (TryLoad(BackupPath, out profile))
        {
            try
            {
                IOFile.Copy(ProjectSettings.GlobalizePath(BackupPath), ProjectSettings.GlobalizePath(SavePath), overwrite: true);
                GD.PushWarning("[PlayerProgress] Restored the backup progress file.");
            }
            catch (Exception exception)
            {
                GD.PushWarning($"[PlayerProgress] Backup loaded but could not restore the primary file. {exception.Message}");
            }
            return profile;
        }

        if (FileAccess.FileExists(SavePath) || FileAccess.FileExists(BackupPath))
            GD.PushWarning("[PlayerProgress] Could not load progress or its backup, using a new profile.");
        return CreateEmptyProfile();
    }

    private bool TryLoad(string path, out PlayerProgressProfile profile)
    {
        profile = null!;
        if (!FileAccess.FileExists(path))
            return false;

        try
        {
            string json;
            using (var file = FileAccess.Open(path, FileAccess.ModeFlags.Read))
                json = file.GetAsText();
            profile = JsonSerializer.Deserialize<PlayerProgressProfile>(json, JsonOptions) ?? new PlayerProgressProfile();
            if (profile.Version != PlayerProgressProfile.CurrentVersion)
            {
                ArchiveRejectedProfile(path, "unsupported");
                profile = null!;
                return false;
            }
            if (!_storageContext.Owns(profile.OwnerProvider, profile.OwnerAccountId))
            {
                ArchiveRejectedProfile(path, "owner_mismatch");
                profile = null!;
                return false;
            }
            profile.Statistics ??= new Dictionary<string, long>();
            profile.PlatformStatisticBaselines ??= new Dictionary<string, long>();
            profile.OccurredEventKeys ??= new HashSet<string>();
            profile.UnlockedAchievementApiNames ??= new HashSet<string>();
            profile.PlatformSuppressedAchievementApiNames ??= new HashSet<string>();
            return true;
        }
        catch (Exception exception)
        {
            ArchiveRejectedProfile(path, "corrupt");
            GD.PushWarning($"[PlayerProgress] Progress file was unreadable and was archived: {exception.Message}");
            return false;
        }
    }

    private PlayerProgressProfile CreateEmptyProfile() => new()
    {
        Version = PlayerProgressProfile.CurrentVersion,
        OwnerProvider = _storageContext.Provider,
        OwnerAccountId = _storageContext.AccountId,
    };

    private void EnsureStorageDirectory()
    {
        if (!DirAccess.DirExistsAbsolute(_storageContext.RootPath))
            DirAccess.MakeDirRecursiveAbsolute(_storageContext.RootPath);
    }

    private void ArchiveRejectedProfile(string path, string reason)
    {
        try
        {
            var absolutePath = ProjectSettings.GlobalizePath(path);
            var archivePath = reason == "corrupt" && string.Equals(path, SavePath, StringComparison.Ordinal)
                ? ProjectSettings.GlobalizePath(_storageContext.PlayerProgressCorruptPath)
                : $"{absolutePath}.{reason}_{DateTimeOffset.UtcNow:yyyyMMddTHHmmssfffZ}";
            if (IOFile.Exists(archivePath))
                archivePath = $"{archivePath}.{DateTimeOffset.UtcNow:yyyyMMddTHHmmssfffZ}";
            IOFile.Move(absolutePath, archivePath, overwrite: false);
            GD.PushWarning($"[PlayerProgress] Rejected {reason} progress was archived: {archivePath}");
        }
        catch (Exception exception)
        {
            GD.PushError($"[PlayerProgress] Failed to archive rejected progress: {exception.Message}");
        }
    }

    private void RequestImmediateSave() => _immediateSaveRequested = true;

    private void ValidateDefinitions()
    {
#if DEBUG
        var achievements = LubanData.Tables.TbAchievement.DataList;
        var statistics = LubanData.Tables.TbPlayerStatistic.DataList;
        ValidateNoDuplicates(achievements.Select(row => row.ApiName), "Achievement ApiName");
        ValidateNoDuplicates(statistics.Select(row => row.StatisticKey), "PlayerStatistic StatisticKey");
        ValidateNoDuplicates(statistics.Where(row => !string.IsNullOrWhiteSpace(row.PlatformApiName)).Select(row => row.PlatformApiName), "PlayerStatistic PlatformApiName");

        foreach (var statistic in _statisticsByKey.Values)
        {
            var flagType = statistic.StatisticType == EPlayerStatisticType.Flag;
            var flagUnit = statistic.Unit == EPlayerStatisticUnit.Flag;
            if (flagType != flagUnit)
                GD.PushError($"[PlayerProgress] Flag Unit/StatisticType mismatch: {statistic.StatisticKey}.");
        }

        foreach (var achievement in achievements.Where(achievement => achievement.IsEnabled))
        {
            bool valid = achievement.RuleType switch
            {
                EAchievementRuleType.FirstExternalItemType =>
                    Enum.TryParse<EItemType>(achievement.TargetKey, out var itemType)
                    && TryGetEnabledExternalItemStatisticKey(itemType, out _)
                    && achievement.TargetValue == 1,
                EAchievementRuleType.FirstExternalItemRarity => Enum.TryParse<ERarity>(achievement.TargetKey, out _) && achievement.TargetValue == 1,
                EAchievementRuleType.FirstEvent => achievement.TargetValue == 1 && IsKnownEventKey(achievement.TargetKey),
                EAchievementRuleType.StatisticAtLeast => achievement.TargetValue > 0 && _statisticsByKey.ContainsKey(achievement.TargetKey),
                _ => false,
            };
            if (!valid)
                GD.PushError($"[PlayerProgress] Invalid achievement definition: {achievement.ApiName}.");
        }
#endif
    }

#if DEBUG
    private static void ValidateNoDuplicates(IEnumerable<string> values, string label)
    {
        foreach (var duplicate in values.GroupBy(value => value, StringComparer.Ordinal).Where(group => group.Count() > 1))
            GD.PushError($"[PlayerProgress] Duplicate {label}: {duplicate.Key}");
    }

    private static bool IsKnownEventKey(string key) => key is "DogHintAsked" or "DogHintRefused" or "DesktopStarstruckEntered";
#endif
}
