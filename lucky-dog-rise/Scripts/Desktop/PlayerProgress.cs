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
    public HashSet<string> OccurredEventKeys { get; set; } = new();
    public HashSet<string> UnlockedAchievementApiNames { get; set; } = new();
    public HashSet<string> PlatformSuppressedAchievementApiNames { get; set; } = new();
    public bool ExternalInventoryBackfilled { get; set; }
    public string UpdatedAt { get; set; } = "";
}

/// <summary>
/// 与可重置游戏存档分离的账号级长期进度。
/// 未接平台时只用于保留未来同步所需的事实与提供 DEBUG 控制台验收。
/// </summary>
public sealed class PlayerProgress
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly AccountStorageContext _storageContext;
    private string SavePath => _storageContext.PlayerProgressPath;
    private string BackupPath => _storageContext.PlayerProgressBackupPath;
    private string TempPath => _storageContext.PlayerProgressTempPath;
    private readonly Dictionary<string, PlayerStatistic> _statisticsByKey;
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
            .ToDictionary(stat => stat.StatisticKey, StringComparer.Ordinal);
        _profile = LoadOrCreate();
        GD.Print(
            $"[PlayerProgress] Loaded account={_storageContext}, Version={_profile.Version}, Path={AbsoluteSavePath}");
        ValidateDefinitions();
        EvaluateHistoricalAchievements();
    }

    public string AbsoluteSavePath => ProjectSettings.GlobalizePath(SavePath);
    public IReadOnlyDictionary<string, long> Statistics => _profile.Statistics;
    public IReadOnlyCollection<string> UnlockedAchievementApiNames => _profile.UnlockedAchievementApiNames;
    public int PlatformSuppressedAchievementCount => _profile.PlatformSuppressedAchievementApiNames.Count;
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
            !_profile.PlatformSuppressedAchievementApiNames.Contains(apiName));
    }

    /// <summary>平台侧已解锁项是账号事实；合并到本地并解除旧的 Debug 上传抑制。</summary>
    public int ImportPlatformAchievements(IEnumerable<string> achievementApiNames)
    {
        var changedCount = 0;
        foreach (var apiName in achievementApiNames
                     .Where(apiName => !string.IsNullOrWhiteSpace(apiName))
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
        if (source == PlayerProgressSource.Debug || count <= 0 || item.AcquisitionType == EAcquisitionType.Initial)
            return;

        RecordCounter("ExternalItemAcquiredCount", count, source);
        if (source == PlayerProgressSource.BlindBox)
            RecordCounter("BlindBoxItemAcquiredCount", count, source);
        if (_externalItemStatisticKeys.TryGetValue(item.ItemType, out var itemKey))
            RecordCounter(itemKey, count, source);
        if (_externalRarityStatisticKeys.TryGetValue(item.ItemRarity, out var rarityKey))
            RecordCounter(rarityKey, count, source);

        EvaluateAchievements(achievement => achievement.RuleType switch
        {
            EAchievementRuleType.FirstExternalItemType => string.Equals(achievement.TargetKey, item.ItemType.ToString(), StringComparison.Ordinal),
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

    private void RecordCounter(string statisticKey, long amount, PlayerProgressSource source)
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

        var applied = ApplyMultiplier(amount, source);
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

    private long GetStatistic(string statisticKey) => _profile.Statistics.TryGetValue(statisticKey, out var value) ? value : 0L;

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
        foreach (var achievement in LubanData.Tables.TbAchievement.DataList)
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
            || !_externalItemStatisticKeys.TryGetValue(itemType, out var statisticKey))
            return false;

        count = GetStatistic(statisticKey);
        return true;
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
        foreach (var achievement in LubanData.Tables.TbAchievement.DataList.Where(predicate))
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
        ValidateNoDuplicates(achievements.Select(row => row.ApiName), "Achievement ApiName");
        ValidateNoDuplicates(_statisticsByKey.Keys, "PlayerStatistic StatisticKey");
        ValidateNoDuplicates(_statisticsByKey.Values.Where(row => !string.IsNullOrWhiteSpace(row.PlatformApiName)).Select(row => row.PlatformApiName), "PlayerStatistic PlatformApiName");

        foreach (var achievement in achievements)
        {
            bool valid = achievement.RuleType switch
            {
                EAchievementRuleType.FirstExternalItemType => Enum.TryParse<EItemType>(achievement.TargetKey, out _) && achievement.TargetValue == 1,
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
