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
    public int Version { get; set; } = 1;
    public Dictionary<string, long> Statistics { get; set; } = new();
    public HashSet<string> OccurredEventKeys { get; set; } = new();
    public HashSet<string> UnlockedAchievementApiNames { get; set; } = new();
    public bool ExternalInventoryBackfilled { get; set; }
    public string UpdatedAt { get; set; } = "";
}

/// <summary>
/// 与可重置游戏存档分离的账号级长期进度。
/// 未接平台时只用于保留未来同步所需的事实与提供 DEBUG 控制台验收。
/// </summary>
public sealed class PlayerProgress
{
    private const string SavePath = "user://player_progress_0.json";
    private const string BackupPath = "user://player_progress_0.backup.json";
    private const string TempPath = "user://player_progress_0.temp.json";
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

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
        [EHandRank.JacksOrBetter] = "PokerHandRankJacksOrBetterCount",
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

#if DEBUG
    private long _debugMultiplier = 1;
#endif

    public PlayerProgress()
    {
        _statisticsByKey = LubanData.Tables.TbPlayerStatistic.DataList
            .ToDictionary(stat => stat.StatisticKey, StringComparer.Ordinal);
        _profile = LoadOrCreate();
        ValidateDefinitions();
        EvaluateAllStatisticAchievements();
    }

    public string AbsoluteSavePath => ProjectSettings.GlobalizePath(SavePath);
    public IReadOnlyDictionary<string, long> Statistics => _profile.Statistics;
    public IReadOnlyCollection<string> UnlockedAchievementApiNames => _profile.UnlockedAchievementApiNames;
    public bool IsDirty => _dirty;
    public bool RequiresImmediateSave => _dirty && _immediateSaveRequested;

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
        if (!_dirty)
            return;

        try
        {
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
        _profile = new PlayerProgressProfile();
        _durationRemainders.Clear();
        _inputBucketStart = default;
        _inputBucketChips = 0;
        _dirty = true;
        SaveIfDirty();
        GD.Print($"[PlayerProgress] Reset local progress: {AbsoluteSavePath}");
    }

#if DEBUG
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

    private void EvaluateAllStatisticAchievements()
    {
        foreach (var achievement in LubanData.Tables.TbAchievement.DataList.Where(achievement =>
                     achievement.RuleType == EAchievementRuleType.StatisticAtLeast
                     && GetStatistic(achievement.TargetKey) >= achievement.TargetValue))
        {
            if (_profile.UnlockedAchievementApiNames.Add(achievement.ApiName))
                _dirty = true;
        }
    }

    private void EvaluateAchievements(Func<Achievement, bool> predicate)
    {
        foreach (var achievement in LubanData.Tables.TbAchievement.DataList.Where(predicate))
        {
            if (!_profile.UnlockedAchievementApiNames.Add(achievement.ApiName))
                continue;

            _dirty = true;
            RequestImmediateSave();
#if DEBUG
            GD.Print($"[Achievement] Satisfied: ID={achievement.AchievementId} | {achievement.Notes} | ApiName={achievement.ApiName} (rule={achievement.RuleType}, target={achievement.TargetKey})");
#endif
        }
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
        return new PlayerProgressProfile();
    }

    private static bool TryLoad(string path, out PlayerProgressProfile profile)
    {
        profile = null!;
        if (!FileAccess.FileExists(path))
            return false;

        try
        {
            using var file = FileAccess.Open(path, FileAccess.ModeFlags.Read);
            profile = JsonSerializer.Deserialize<PlayerProgressProfile>(file.GetAsText(), JsonOptions) ?? new PlayerProgressProfile();
            profile.Statistics ??= new Dictionary<string, long>();
            profile.OccurredEventKeys ??= new HashSet<string>();
            profile.UnlockedAchievementApiNames ??= new HashSet<string>();
            return true;
        }
        catch
        {
            return false;
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
