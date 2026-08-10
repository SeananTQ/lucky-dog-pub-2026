#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Serialization;
using DataTables;
using Godot;

namespace LuckyDogRise;

public sealed class PendingBlindBoxReward
{
    public int BlindBoxId { get; set; }
    public int ScheduleId { get; set; }
    public int ItemId { get; set; }
    public int RevealPathId { get; set; }
    public int RevealStep { get; set; }
    public bool RewardShown { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool IsPlatformInventoryReward { get; set; }
    public double TotalPlaySeconds { get; set; }
    public string DebugText { get; set; } = "";
}

public sealed class PendingPlatformBlindBoxOpen
{
    public int BlindBoxId { get; set; }
    public int ScheduleId { get; set; }
    public int InputItemDefId { get; set; }
    public ulong InputInstanceId { get; set; }
    public int ExchangeTargetItemDefId { get; set; }
    public int ReservedChipCost { get; set; }
    public Dictionary<ulong, uint> InventoryQuantitiesBeforeExchange { get; set; } = new();
    public double TotalPlaySeconds { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool IsDeferredBacklog { get; set; }
}

public sealed class BlindBoxOpenResult
{
    public required BlindBox Box { get; init; }
    public required BlindBoxSchedule Schedule { get; init; }
    public required Item Item { get; init; }
    public required PendingBlindBoxReward PendingReward { get; init; }
}

public sealed class BlindBoxScheduleState
{
    public int PendingCount { get; set; }
    public int ProcessedGrantCount { get; set; }
}

public sealed class BlindBoxSteamPlaytimeDropState
{
    public int ProcessedGrantCount { get; set; }
}

public sealed class BlindBoxRuntimeState
{
    public int SequenceIndex { get; set; }
    /// <summary>
    /// 盲盒专用的连续调度时钟。它按真实帧时间持续累积，但流速由等待时间倍率控制；
    /// 与真实的 TotalPlaySeconds 分离，避免倍率变化导致时间轴跳变。
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public double ScheduleSeconds { get; set; }
    /// <summary>最近一次奖励入账时的盲盒调度时钟值。</summary>
    public double LastClaimSeconds { get; set; }
    /// <summary>正常阶段下一个本地展示点的调度表时间。</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public double NextLoopPresentationSeconds { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public double NextLoopTriggerSeconds { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public int LockedLoopScheduleId { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public int LockedLoopBlindBoxId { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool LoopStageStarted { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool LoopDropVerificationPending { get; set; }

    // Kept for v7 save signature verification. Version 9 migration clears these fields.
    public Dictionary<int, BlindBoxScheduleState> LoopTrackStates { get; set; } = new();
    public Dictionary<int, BlindBoxSteamPlaytimeDropState> SteamPlaytimeDropStates { get; set; } = new();
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Dictionary<int, int> DeferredPlatformScheduleCounts { get; set; } = new();
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public double NextDeferredPlatformPresentationSeconds { get; set; }
}

public enum BlindBoxHintStatus
{
    Waiting,
    Ready,
    NotEnoughChips,
    PendingReward,
    PlatformSyncing,
    PlatformUnavailable,
    Opening,
}

public enum BlindBoxPaymentSource
{
    Unknown,
    Chips,
    LocalRefreshment,
    SteamVoucher,
    SteamFallback,
}

public sealed class BlindBoxHintState
{
    public BlindBoxHintStatus Status { get; init; }
    public BlindBox? Box { get; init; }
    public int Cost { get; init; }
    public double RemainingSeconds { get; init; }
    public BlindBoxPaymentSource PaymentSource { get; init; }
}

public sealed class BlindBoxService
{
    private readonly GameData _gameData;
    private readonly Random _random = new();

    public BlindBoxService(GameData gameData)
    {
        _gameData = gameData;
        ValidateDefinitions();
    }

    public void AdvanceScheduleClock(BlindBoxRuntimeState runtimeState, double realDeltaSeconds)
    {
        if (realDeltaSeconds <= 0.0)
            return;

        runtimeState.ScheduleSeconds += realDeltaSeconds * GetScheduleClockRate();
    }

    public bool TryGetNextSteamPlaytimeDrop(
        BlindBoxRuntimeState runtimeState,
        out BlindBoxSchedule? schedule,
        out BlindBox? box,
        out int grantCount)
    {
        NormalizeSteamPlaytimeDropProgress(runtimeState);
        var leadScheduleSeconds = GetSteamPlaytimeRequestLeadSeconds() * GetScheduleClockRate();
        var eligibilitySeconds = runtimeState.ScheduleSeconds + leadScheduleSeconds;

        var candidate = LubanData.Tables.TbBlindBoxSchedule.DataList
            .Where(entry => entry.IsEnabled
                            && !entry.IsLoopTrack
                            && entry.SteamPlaytimeGeneratorItemDefId > 0)
            .Select(entry =>
            {
                var progress = GetSteamPlaytimeDropState(runtimeState, entry.Id);
                var nextGrantCount = progress.ProcessedGrantCount + 1;
                return new
                {
                    Schedule = entry,
                    Box = LubanData.Tables.TbBlindBox.GetOrDefault(entry.BlindBoxId),
                    NextGrantCount = nextGrantCount,
                    DueSeconds = GetGrantDueScheduleSeconds(entry, nextGrantCount),
                    EligibleGrantCount = GetDueGrantCount(entry, eligibilitySeconds),
                };
            })
            .Where(entry => entry.Box != null
                            && entry.Box.IsEnabled
                            && entry.Box.SteamOpenCostItemDefId > 0
                            && entry.EligibleGrantCount >= entry.NextGrantCount)
            .OrderBy(entry => entry.DueSeconds)
            .ThenBy(entry => entry.Schedule.Id)
            .FirstOrDefault();

        if (candidate != null)
        {
            schedule = candidate.Schedule;
            box = candidate.Box;
            grantCount = candidate.NextGrantCount;
            return true;
        }

        EnsureLoopStageInitialized(runtimeState);
        var loopSchedule = GetLoopSchedule();
        var loopBox = loopSchedule == null
            ? null
            : LubanData.Tables.TbBlindBox.GetOrDefault(loopSchedule.BlindBoxId);
        if (!runtimeState.LoopStageStarted
            || runtimeState.LoopDropVerificationPending
            || runtimeState.ScheduleSeconds < runtimeState.NextLoopTriggerSeconds
            || loopSchedule == null
            || loopBox is not { IsEnabled: true }
            || loopSchedule.SteamPlaytimeGeneratorItemDefId <= 0)
        {
            schedule = null;
            box = null;
            grantCount = 0;
            return false;
        }

        schedule = loopSchedule;
        box = loopBox;
        grantCount = 1;
        return true;
    }

    public bool IsSteamPlaytimeDropDue(
        BlindBoxRuntimeState runtimeState,
        BlindBoxSchedule schedule,
        int grantCount) => schedule.IsLoopTrack
        ? runtimeState.LoopStageStarted
          && runtimeState.ScheduleSeconds >= runtimeState.NextLoopTriggerSeconds
        : GetDueGrantCount(schedule, runtimeState.ScheduleSeconds) >= grantCount;

    public double GetSteamPlaytimeDropRetryDelaySeconds(
        BlindBoxRuntimeState runtimeState,
        BlindBoxSchedule schedule,
        int grantCount)
    {
        if (schedule.IsLoopTrack)
        {
            var remaining = Math.Max(
                0.0,
                runtimeState.NextLoopTriggerSeconds - runtimeState.ScheduleSeconds);
            return Math.Max(5.0, ToRealSeconds(remaining));
        }

        var remainingScheduleSeconds = Math.Max(
            0.0,
            GetGrantDueScheduleSeconds(schedule, grantCount) - runtimeState.ScheduleSeconds);
        return Math.Max(5.0, ToRealSeconds(remainingScheduleSeconds));
    }

    public void CompleteSteamPlaytimeDrop(
        BlindBoxRuntimeState runtimeState,
        int scheduleId,
        int grantCount)
    {
        var schedule = LubanData.Tables.TbBlindBoxSchedule.GetOrDefault(scheduleId);
        if (schedule == null || grantCount <= 0)
            return;

        if (schedule.IsLoopTrack)
        {
            runtimeState.LoopDropVerificationPending = false;
            AdvanceLoopTriggerHeartbeat(runtimeState);
            return;
        }

        var progress = GetSteamPlaytimeDropState(runtimeState, scheduleId);
        progress.ProcessedGrantCount = Math.Max(progress.ProcessedGrantCount, grantCount);
    }

    public void BeginSteamPlaytimeDrop(
        BlindBoxRuntimeState runtimeState,
        BlindBoxSchedule schedule)
    {
        if (schedule.IsLoopTrack)
            runtimeState.LoopDropVerificationPending = true;
    }

    public bool PrepareLoopDropRetryAfterInventoryVerification(BlindBoxRuntimeState runtimeState)
    {
        if (!runtimeState.LoopDropVerificationPending)
            return false;

        runtimeState.LoopDropVerificationPending = false;
        return true;
    }

    public bool ReopenCurrentSteamPlaytimeDrop(
        BlindBoxRuntimeState runtimeState,
        BlindBoxSchedule schedule)
    {
        if (schedule.IsLoopTrack)
            return false;

        var progress = GetSteamPlaytimeDropState(runtimeState, schedule.Id);
        var reopenedGrantCount = Math.Min(progress.ProcessedGrantCount, 0);
        if (progress.ProcessedGrantCount == reopenedGrantCount)
            return false;

        progress.ProcessedGrantCount = reopenedGrantCount;
        return true;
    }

    public bool CompleteCurrentSteamPlaytimeDropFromInventory(
        BlindBoxRuntimeState runtimeState,
        BlindBoxSchedule schedule)
    {
        if (schedule.IsLoopTrack)
            return false;

        var progress = GetSteamPlaytimeDropState(runtimeState, schedule.Id);
        if (progress.ProcessedGrantCount >= 1)
            return false;

        progress.ProcessedGrantCount = 1;
        return true;
    }

    public bool MaintainLoopPresentation(
        BlindBoxRuntimeState runtimeState,
        bool canLockPresentation,
        int selectedBlindBoxId)
    {
        var changed = EnsureLoopStageInitialized(runtimeState);
        if (!runtimeState.LoopStageStarted)
            return changed;

        var interval = GetLoopIntervalScheduleSeconds();
        if (runtimeState.NextLoopPresentationSeconds <= 0.0)
        {
            runtimeState.NextLoopPresentationSeconds = runtimeState.ScheduleSeconds + interval;
            changed = true;
        }

        if (runtimeState.ScheduleSeconds < runtimeState.NextLoopPresentationSeconds)
            return changed;

        var elapsedIntervals = Math.Floor(
            (runtimeState.ScheduleSeconds - runtimeState.NextLoopPresentationSeconds) / interval) + 1.0;
        runtimeState.NextLoopPresentationSeconds += elapsedIntervals * interval;
        changed = true;

        if (!canLockPresentation || runtimeState.LockedLoopBlindBoxId > 0)
            return changed;

        var schedule = GetLoopSchedule();
        var box = LubanData.Tables.TbBlindBox.GetOrDefault(selectedBlindBoxId);
        if (schedule == null || box is not { IsEnabled: true })
            return changed;

        runtimeState.LockedLoopScheduleId = schedule.Id;
        runtimeState.LockedLoopBlindBoxId = box.Id;
        return true;
    }

    public BlindBox? GetNextAvailableBox(
        BlindBoxRuntimeState runtimeState,
        PendingBlindBoxReward? pendingReward)
    {
        if (pendingReward != null)
            return LubanData.Tables.TbBlindBox.GetOrDefault(pendingReward.BlindBoxId);

        return GetAvailableSchedules(runtimeState)
            .Select(entry => entry.Box)
            .FirstOrDefault();
    }

    public BlindBox? GetFallbackRefreshmentBox() =>
        LubanData.Tables.TbBlindBox.DataList
            .Where(box => box.IsEnabled
                          && box.BoxType == EBlindBoxType.Refreshment
                          && !box.IsPlatformInventoryRequired)
            .OrderBy(box => box.Id)
            .FirstOrDefault();

    public bool TryGetLoopSchedule(out BlindBoxSchedule? schedule, out BlindBox? box)
    {
        schedule = GetLoopSchedule();
        box = schedule == null
            ? null
            : LubanData.Tables.TbBlindBox.GetOrDefault(schedule.BlindBoxId);
        return schedule != null && box is { IsEnabled: true };
    }

    public BlindBoxHintState CreateReadyHintState(BlindBox box)
    {
        var cost = GetDisplayCost(box);
        return new BlindBoxHintState
        {
            Status = _gameData.Chips >= cost
                ? BlindBoxHintStatus.Ready
                : BlindBoxHintStatus.NotEnoughChips,
            Box = box,
            Cost = cost,
            PaymentSource = box.IsPlatformInventoryRequired && box.SteamOpenCostItemDefId > 0
                ? BlindBoxPaymentSource.SteamVoucher
                : box.BoxType == EBlindBoxType.Refreshment
                    ? BlindBoxPaymentSource.LocalRefreshment
                    : BlindBoxPaymentSource.Chips,
        };
    }

    public int GetDisplayCost(BlindBox box)
    {
        var config = LubanData.Tables.TbGameDevelopConfig.DataList.FirstOrDefault();
        var scale = config == null || config.BlindBoxCostScale <= 0 ? 1f : config.BlindBoxCostScale;
        return Mathf.Max(0, Mathf.RoundToInt(box.CostChips * scale));
    }

    public string BuildDebugStatus(
        double totalPlaySeconds,
        BlindBoxRuntimeState runtimeState,
        PendingBlindBoxReward? pendingReward)
    {
        var scheduleSeconds = runtimeState.ScheduleSeconds;
        var durationMultiplier = GetWaitDurationMultiplier();
        var builder = new StringBuilder();
        builder.AppendLine($"游玩: {FormatSeconds(totalPlaySeconds)}");
        builder.AppendLine($"调度表时间: {FormatSeconds(scheduleSeconds)}");
        builder.AppendLine($"等待倍率: x{durationMultiplier:0.###}，时钟速度: x{GetScheduleClockRate():0.###}");
        builder.AppendLine($"上次领取: {FormatSeconds(runtimeState.LastClaimSeconds)}");
        builder.AppendLine($"正式展示门槛: {FormatSeconds(ToRealSeconds(Math.Max(0, runtimeState.NextLoopPresentationSeconds - scheduleSeconds)))} 后");

        if (pendingReward != null)
        {
            var box = LubanData.Tables.TbBlindBox.GetOrDefault(pendingReward.BlindBoxId);
            var item = LubanData.Tables.TbItem.GetOrDefault(pendingReward.ItemId);
            builder.AppendLine("状态: 待领取奖品");
            builder.AppendLine($"盲盒: {box?.Name ?? "缺失"} ({pendingReward.BlindBoxId})");
            builder.AppendLine($"调度: {pendingReward.ScheduleId}");
            builder.AppendLine($"奖品: {item?.Name ?? "缺失"} ({pendingReward.ItemId})");
            return builder.ToString().TrimEnd();
        }

        var available = GetAvailableSchedules(runtimeState).FirstOrDefault();
        if (available.Schedule != null && available.Box != null)
        {
            var cost = GetDisplayCost(available.Box);
            builder.AppendLine(_gameData.Chips >= cost ? "状态: 可领取" : "状态: 筹码不足");
            builder.AppendLine($"下个: {available.Box.Name} ({available.Box.Id})");
            builder.AppendLine($"调度: {available.Schedule.Id}, 循环={available.Schedule.IsLoopTrack}");
            builder.AppendLine($"消耗: {cost}, 筹码: {_gameData.Chips}");
        }
        else
        {
            builder.AppendLine("状态: 等待中");
        }

        var sequence = GetCurrentSequenceSchedule(runtimeState);
        if (sequence != null)
        {
            var box = LubanData.Tables.TbBlindBox.GetOrDefault(sequence.BlindBoxId);
            var waitScaledSeconds = runtimeState.SequenceIndex == 0
                ? Math.Max(0, Math.Max(sequence.StartSeconds, sequence.IntervalSeconds) - scheduleSeconds)
                : Math.Max(0, sequence.IntervalSeconds - (scheduleSeconds - runtimeState.LastClaimSeconds));
            builder.AppendLine($"新手: #{runtimeState.SequenceIndex}, 调度={sequence.Id}, {box?.Name ?? "缺失"}");
            builder.AppendLine($"新手等待: {FormatSeconds(ToRealSeconds(waitScaledSeconds))}");
        }
        else
        {
            builder.AppendLine($"新手: 已完成 #{runtimeState.SequenceIndex}");
        }

        var loopSchedule = GetLoopSchedule();
        var lockedBox = LubanData.Tables.TbBlindBox.GetOrDefault(runtimeState.LockedLoopBlindBoxId);
        builder.AppendLine($"正常阶段: {(runtimeState.LoopStageStarted ? "已启动" : "未启动")}");
        builder.AppendLine($"循环调度: {loopSchedule?.Id.ToString() ?? "缺失"}");
        builder.AppendLine($"锁定气球: {lockedBox?.Name ?? "无"} ({runtimeState.LockedLoopBlindBoxId})");
        builder.AppendLine($"下个展示点: {FormatSeconds(ToRealSeconds(Math.Max(0, runtimeState.NextLoopPresentationSeconds - scheduleSeconds)))} 后");
        builder.AppendLine($"下个Steam心跳: {FormatSeconds(ToRealSeconds(Math.Max(0, runtimeState.NextLoopTriggerSeconds - scheduleSeconds)))} 后");
        builder.AppendLine($"循环请求待验证: {(runtimeState.LoopDropVerificationPending ? "是" : "否")}");

        return builder.ToString().TrimEnd();
    }

    public BlindBoxHintState GetHintState(
        BlindBoxRuntimeState runtimeState,
        PendingBlindBoxReward? pendingReward)
    {
        if (pendingReward != null)
        {
            return new BlindBoxHintState
            {
                Status = BlindBoxHintStatus.PendingReward,
                Box = LubanData.Tables.TbBlindBox.GetOrDefault(pendingReward.BlindBoxId),
            };
        }

        var available = GetAvailableSchedules(runtimeState).FirstOrDefault();
        if (available.Schedule != null && available.Box != null)
        {
            var cost = GetDisplayCost(available.Box);
            return new BlindBoxHintState
            {
                Status = _gameData.Chips >= cost ? BlindBoxHintStatus.Ready : BlindBoxHintStatus.NotEnoughChips,
                Box = available.Box,
                Cost = cost,
                PaymentSource = available.Box.IsPlatformInventoryRequired
                    && available.Box.SteamOpenCostItemDefId > 0
                        ? BlindBoxPaymentSource.SteamVoucher
                        : available.Box.BoxType == EBlindBoxType.Refreshment
                            ? BlindBoxPaymentSource.LocalRefreshment
                            : BlindBoxPaymentSource.Chips,
            };
        }

        return new BlindBoxHintState
        {
            Status = BlindBoxHintStatus.Waiting,
            RemainingSeconds = GetNextReadyRemainingSeconds(runtimeState),
        };
    }

    public BlindBoxOpenResult? TryOpenNext(
        double totalPlaySeconds,
        BlindBoxRuntimeState runtimeState)
    {
        var candidate = GetAvailableSchedules(runtimeState).FirstOrDefault();
        if (candidate.Schedule == null || candidate.Box == null)
            return null;

        var cost = GetDisplayCost(candidate.Box);
        if (_gameData.Chips < cost)
        {
            GD.PushWarning($"[BlindBox] Not enough chips. Need {cost}, current {_gameData.Chips}.");
            return null;
        }

        var item = RollReward(candidate.Box);
        if (item == null)
        {
            GD.PushError($"[BlindBox] No reward candidate for box {candidate.Box.Id} ({candidate.Box.Name}).");
            return null;
        }

        _gameData.ModifyChips(-cost);
        return CreateOpenResult(totalPlaySeconds, candidate.Schedule, candidate.Box, item, cost);
    }

    public BlindBoxOpenResult? TryOpenFallback(
        double totalPlaySeconds,
        BlindBoxSchedule originalSchedule,
        BlindBox fallbackBox)
    {
        var cost = GetDisplayCost(fallbackBox);
        if (_gameData.Chips < cost)
        {
            GD.PushWarning($"[BlindBox] Not enough chips. Need {cost}, current {_gameData.Chips}.");
            return null;
        }

        var item = RollReward(fallbackBox);
        if (item == null)
        {
            GD.PushError(
                $"[BlindBox] No reward candidate for fallback box {fallbackBox.Id} ({fallbackBox.Name}).");
            return null;
        }

        _gameData.ModifyChips(-cost);
        return CreateOpenResult(totalPlaySeconds, originalSchedule, fallbackBox, item, cost);
    }

    public bool TryGetNextAvailable(
        BlindBoxRuntimeState runtimeState,
        out BlindBoxSchedule? schedule,
        out BlindBox? box)
    {
        var candidate = GetAvailableSchedules(runtimeState).FirstOrDefault();
        schedule = candidate.Schedule;
        box = candidate.Box;
        return schedule != null && box != null;
    }

    public bool TryGetNextPresentationCandidate(
        BlindBoxRuntimeState runtimeState,
        out BlindBoxSchedule? schedule,
        out BlindBox? box)
    {
        schedule = GetCurrentSequenceSchedule(runtimeState);
        if (schedule != null)
        {
            box = LubanData.Tables.TbBlindBox.GetOrDefault(schedule.BlindBoxId);
            return box is { IsEnabled: true };
        }

        var candidate = GetAvailableSchedules(runtimeState).FirstOrDefault();
        schedule = candidate.Schedule;
        box = candidate.Box;
        return schedule != null && box != null;
    }

    public BlindBoxOpenResult? CreateOpenResult(
        double totalPlaySeconds,
        BlindBoxSchedule schedule,
        BlindBox box,
        Item item,
        int cost)
    {
        var revealPath = RollRevealPath(item.ItemRarity);
        if (revealPath == null)
        {
            GD.PushError($"[BlindBox] No reveal path for rarity {item.ItemRarity}.");
            return null;
        }

        var pending = new PendingBlindBoxReward
        {
            BlindBoxId = box.Id,
            ScheduleId = schedule.Id,
            ItemId = item.Id,
            RevealPathId = revealPath.Id,
            RevealStep = 0,
            RewardShown = box.DisplayMode == EBlindBoxDisplayMode.DirectReward,
            TotalPlaySeconds = totalPlaySeconds,
            DebugText = BuildDebugText(box, schedule, item, revealPath, totalPlaySeconds, cost),
        };

        return new BlindBoxOpenResult
        {
            Box = box,
            Schedule = schedule,
            Item = item,
            PendingReward = pending,
        };
    }

    public bool IsRewardCandidate(BlindBox box, Item item) =>
        LubanData.Tables.TbBlindBoxRarityRate.DataList
            .Where(rate => rate.IsEnabled && rate.BlindBoxId == box.Id && rate.Weight > 0)
            .SelectMany(rate => GetRewardCandidates(box, rate.Rarity))
            .Any(candidate => candidate.Item.Id == item.Id);

    private IEnumerable<(BlindBoxSchedule Schedule, BlindBox Box)> GetAvailableSchedules(
        BlindBoxRuntimeState runtimeState)
    {
        var scheduleSeconds = runtimeState.ScheduleSeconds;

        var sequenceSchedule = GetCurrentSequenceSchedule(runtimeState);
        if (sequenceSchedule != null && IsSequenceAvailable(sequenceSchedule, scheduleSeconds, runtimeState))
        {
            var box = LubanData.Tables.TbBlindBox.GetOrDefault(sequenceSchedule.BlindBoxId);
            if (box != null && box.IsEnabled)
                return [(sequenceSchedule, box)];
        }

        if (sequenceSchedule != null)
            return [];

        if (runtimeState.LockedLoopScheduleId <= 0 || runtimeState.LockedLoopBlindBoxId <= 0)
            return [];

        var lockedSchedule = LubanData.Tables.TbBlindBoxSchedule.GetOrDefault(runtimeState.LockedLoopScheduleId);
        var lockedBox = LubanData.Tables.TbBlindBox.GetOrDefault(runtimeState.LockedLoopBlindBoxId);
        return lockedSchedule is { IsEnabled: true, IsLoopTrack: true }
               && lockedBox is { IsEnabled: true }
            ? [(lockedSchedule, lockedBox)]
            : [];
    }

    public void ConsumeOpenedSchedule(BlindBoxRuntimeState runtimeState, BlindBoxSchedule schedule)
    {
        if (schedule.IsLoopTrack)
        {
            runtimeState.LockedLoopScheduleId = 0;
            runtimeState.LockedLoopBlindBoxId = 0;
            return;
        }

        var sequenceSchedules = GetSequenceSchedules();
        if (runtimeState.SequenceIndex < sequenceSchedules.Count
            && sequenceSchedules[runtimeState.SequenceIndex].Id == schedule.Id)
        {
            runtimeState.SequenceIndex++;
        }
    }

    /// <summary>奖励真正进入玩家背包后，再从此刻开始计算下一段新手间隔。</summary>
    public void CompleteClaimedSchedule(BlindBoxRuntimeState runtimeState, int scheduleId)
    {
        var schedule = LubanData.Tables.TbBlindBoxSchedule.GetOrDefault(scheduleId);
        if (schedule == null)
        {
            GD.PushError($"[BlindBox] Claimed reward references missing schedule {scheduleId}.");
            return;
        }

        var scheduleSeconds = runtimeState.ScheduleSeconds;
        runtimeState.LastClaimSeconds = scheduleSeconds;
        if (!schedule.IsLoopTrack)
        {
            if (GetCurrentSequenceSchedule(runtimeState) == null)
                StartLoopStage(runtimeState);
            return;
        }
    }

    private static BlindBoxSchedule? GetCurrentSequenceSchedule(BlindBoxRuntimeState runtimeState)
    {
        var sequenceSchedules = GetSequenceSchedules();
        if (runtimeState.SequenceIndex < 0)
            runtimeState.SequenceIndex = 0;
        if (runtimeState.SequenceIndex >= sequenceSchedules.Count)
            return null;
        return sequenceSchedules[runtimeState.SequenceIndex];
    }

    private static List<BlindBoxSchedule> GetSequenceSchedules()
    {
        return LubanData.Tables.TbBlindBoxSchedule.DataList
            .Where(schedule => schedule.IsEnabled && !schedule.IsLoopTrack)
            .OrderBy(schedule => schedule.StartSeconds)
            .ThenBy(schedule => schedule.Id)
            .ToList();
    }

    private static bool IsSequenceAvailable(
        BlindBoxSchedule schedule,
        double scheduleSeconds,
        BlindBoxRuntimeState runtimeState)
    {
        if (scheduleSeconds < schedule.StartSeconds)
            return false;

        var waitSeconds = Mathf.Max(0, schedule.IntervalSeconds);
        if (runtimeState.SequenceIndex == 0)
            return scheduleSeconds >= waitSeconds;

        return scheduleSeconds - runtimeState.LastClaimSeconds >= waitSeconds;
    }

    private static double GetNextReadyRemainingSeconds(BlindBoxRuntimeState runtimeState)
    {
        var scheduleSeconds = runtimeState.ScheduleSeconds;

        var sequence = GetCurrentSequenceSchedule(runtimeState);
        if (sequence != null)
        {
            var waitScaledSeconds = runtimeState.SequenceIndex == 0
                ? Math.Max(0, Math.Max(sequence.StartSeconds, sequence.IntervalSeconds) - scheduleSeconds)
                : Math.Max(0, sequence.IntervalSeconds - (scheduleSeconds - runtimeState.LastClaimSeconds));
            return ToRealSeconds(waitScaledSeconds);
        }

        if (runtimeState.LockedLoopBlindBoxId > 0)
            return 0.0;

        return ToRealSeconds(Math.Max(
            0.0,
            runtimeState.NextLoopPresentationSeconds - scheduleSeconds));
    }

    private static bool EnsureLoopStageInitialized(BlindBoxRuntimeState runtimeState)
    {
        if (runtimeState.LoopStageStarted || GetCurrentSequenceSchedule(runtimeState) != null)
            return false;

        StartLoopStage(runtimeState);
        return true;
    }

    private static void StartLoopStage(BlindBoxRuntimeState runtimeState)
    {
        var now = runtimeState.ScheduleSeconds;
        runtimeState.LoopStageStarted = true;
        runtimeState.LockedLoopScheduleId = 0;
        runtimeState.LockedLoopBlindBoxId = 0;
        runtimeState.NextLoopPresentationSeconds = now + GetLoopIntervalScheduleSeconds();
        runtimeState.NextLoopTriggerSeconds = now;
        runtimeState.LoopDropVerificationPending = false;
    }

    private static void AdvanceLoopTriggerHeartbeat(BlindBoxRuntimeState runtimeState)
    {
        var interval = GetLoopIntervalScheduleSeconds();
        var next = runtimeState.NextLoopTriggerSeconds;
        if (next <= 0.0)
            next = runtimeState.ScheduleSeconds;
        do
        {
            next += interval;
        } while (next <= runtimeState.ScheduleSeconds);
        runtimeState.NextLoopTriggerSeconds = next;
    }

    private static BlindBoxSchedule? GetLoopSchedule()
    {
        return LubanData.Tables.TbBlindBoxSchedule.DataList
            .Where(schedule => schedule.IsEnabled && schedule.IsLoopTrack)
            .OrderBy(schedule => schedule.Id)
            .FirstOrDefault();
    }

    private static double GetLoopIntervalScheduleSeconds()
    {
        var config = LubanData.Tables.TbGameDevelopConfig.DataList.FirstOrDefault();
        return Math.Max(1.0, config?.BlindBoxLoopIntervalSeconds ?? 1.0);
    }

    private static int GetDueGrantCount(BlindBoxSchedule schedule, double scheduleSeconds)
    {
        if (scheduleSeconds < schedule.StartSeconds)
            return 0;

        var effectiveEnd = schedule.EndSeconds < 0
            ? scheduleSeconds
            : Math.Min(scheduleSeconds, schedule.EndSeconds);
        if (effectiveEnd < schedule.StartSeconds)
            return 0;

        var due = schedule.IntervalSeconds <= 0
            ? 1
            : 1 + (int)Math.Floor((effectiveEnd - schedule.StartSeconds) / schedule.IntervalSeconds);
        if (schedule.MaxGrantCount >= 0)
            due = Math.Min(due, schedule.MaxGrantCount);
        return Math.Max(0, due);
    }

    private static double GetGrantDueScheduleSeconds(BlindBoxSchedule schedule, int grantCount)
    {
        if (grantCount <= 1 || schedule.IntervalSeconds <= 0)
            return schedule.StartSeconds;
        return schedule.StartSeconds + (grantCount - 1) * (double)schedule.IntervalSeconds;
    }

    private static BlindBoxSteamPlaytimeDropState GetSteamPlaytimeDropState(
        BlindBoxRuntimeState runtimeState,
        int scheduleId)
    {
        if (runtimeState.SteamPlaytimeDropStates.TryGetValue(scheduleId, out var state))
            return state;

        state = new BlindBoxSteamPlaytimeDropState();
        runtimeState.SteamPlaytimeDropStates[scheduleId] = state;
        return state;
    }

    private static void NormalizeSteamPlaytimeDropProgress(BlindBoxRuntimeState runtimeState)
    {
        var sequenceSchedules = GetSequenceSchedules();
        for (var index = 0; index < Math.Min(runtimeState.SequenceIndex, sequenceSchedules.Count); index++)
        {
            var schedule = sequenceSchedules[index];
            GetSteamPlaytimeDropState(runtimeState, schedule.Id).ProcessedGrantCount = 1;
        }
    }

    private static double GetScheduleClockRate()
    {
        return 1.0 / GetWaitDurationMultiplier();
    }

    private static float GetWaitDurationMultiplier()
    {
        var config = LubanData.Tables.TbGameDevelopConfig.DataList.FirstOrDefault();
        return config == null || config.BlindBoxWaitDurationMultiplier <= 0
            ? 1f
            : config.BlindBoxWaitDurationMultiplier;
    }

    private static double GetSteamPlaytimeRequestLeadSeconds()
    {
        var config = LubanData.Tables.TbGameDevelopConfig.DataList.FirstOrDefault();
        return config == null ? 0.0 : Math.Max(0.0, config.SteamPlaytimeRequestLeadSeconds);
    }

    private static double ToRealSeconds(double scheduleSeconds) =>
        scheduleSeconds * GetWaitDurationMultiplier();

    private void ValidateDefinitions()
    {
#if DEBUG
        var config = LubanData.Tables.TbGameDevelopConfig.DataList.FirstOrDefault();
        if (config == null)
        {
            GD.PushError("[BlindBox] Missing GameDevelopConfig row.");
        }
        else
        {
            if (config.BlindBoxWaitDurationMultiplier <= 0)
                GD.PushError("[BlindBox] BlindBoxWaitDurationMultiplier must be positive.");
            if (config.BlindBoxLoopIntervalSeconds <= 0)
                GD.PushError("[BlindBox] BlindBoxLoopIntervalSeconds must be positive.");
            if (config.SteamPlaytimeDropLeadSeconds < 0)
                GD.PushError("[BlindBox] SteamPlaytimeDropLeadSeconds cannot be negative.");
            if (config.SteamPlaytimeRequestLeadSeconds < 0)
                GD.PushError("[BlindBox] SteamPlaytimeRequestLeadSeconds cannot be negative.");
            if (config.BlindBoxLoopSteamVoucherInventoryLimit < 0)
                GD.PushError("[BlindBox] BlindBoxLoopSteamVoucherInventoryLimit cannot be negative.");
            if (config.SteamPlaytimeRequestLeadSeconds < config.SteamPlaytimeDropLeadSeconds)
            {
                GD.PushError(
                    "[BlindBox] SteamPlaytimeRequestLeadSeconds must be greater than or equal to " +
                    "SteamPlaytimeDropLeadSeconds.");
            }
        }

        var enabledSchedules = LubanData.Tables.TbBlindBoxSchedule.DataList
            .Where(schedule => schedule.IsEnabled)
            .ToList();
        if (!enabledSchedules.Any(schedule => !schedule.IsLoopTrack))
            GD.PushError("[BlindBox] No enabled newbie schedules.");
        if (enabledSchedules.Count(schedule => schedule.IsLoopTrack) != 1)
            GD.PushError("[BlindBox] Exactly one enabled loop schedule is required.");
        if (GetFallbackRefreshmentBox() == null)
            GD.PushError("[BlindBox] No enabled local Refreshment box is available for Steam fallback.");

        foreach (var schedule in enabledSchedules)
        {
            var box = LubanData.Tables.TbBlindBox.GetOrDefault(schedule.BlindBoxId);
            if (box == null || !box.IsEnabled)
            {
                GD.PushError($"[BlindBox] Schedule {schedule.Id} references a missing or disabled box {schedule.BlindBoxId}.");
                continue;
            }

            var upgradeIds = new HashSet<int>();
            foreach (var upgradeBoxId in schedule.VoucherUpgradeBlindBoxIds)
            {
                if (!upgradeIds.Add(upgradeBoxId))
                {
                    GD.PushError(
                        $"[BlindBox] Schedule {schedule.Id} contains duplicate voucher upgrade box {upgradeBoxId}.");
                    continue;
                }

                var upgradeBox = LubanData.Tables.TbBlindBox.GetOrDefault(upgradeBoxId);
                if (upgradeBox is not { IsEnabled: true })
                {
                    GD.PushError(
                        $"[BlindBox] Schedule {schedule.Id} references missing or disabled voucher upgrade box {upgradeBoxId}.");
                    continue;
                }

                if (!upgradeBox.IsPlatformInventoryRequired
                    || upgradeBox.SteamOpenCostItemDefId <= 0
                    || upgradeBox.SteamExchangeTargetItemDefId <= 0)
                {
                    GD.PushError(
                        $"[BlindBox] Schedule {schedule.Id} voucher upgrade box {upgradeBoxId} " +
                        "must configure a platform inventory cost and exchange target.");
                }
            }

            var rates = LubanData.Tables.TbBlindBoxRarityRate.DataList
                .Where(rate => rate.IsEnabled && rate.BlindBoxId == box.Id && rate.Weight > 0)
                .ToList();
            if (rates.Count == 0)
            {
                GD.PushError($"[BlindBox] Box {box.Id} has no enabled rarity rates.");
                continue;
            }

            foreach (var rarity in rates.Select(rate => rate.Rarity).Distinct())
            {
                if (GetRewardCandidates(box, rarity).Count == 0)
                    GD.PushError($"[BlindBox] Box {box.Id} can roll {rarity}, but has no reward candidate.");
                if (!LubanData.Tables.TbBlindBoxRevealPath.DataList.Any(path =>
                        path.IsEnabled && path.ActualRarity == rarity && path.Weight > 0))
                    GD.PushError($"[BlindBox] Box {box.Id} can roll {rarity}, but has no reveal path.");
            }
        }

        ValidatePresentationScheduling();
        ValidateSteamPlaytimeScheduling();
#endif
    }

#if DEBUG
    /// <summary>使用当前表数据回归检查固定展示点、气球锁定与跳过规则。</summary>
    private void ValidatePresentationScheduling()
    {
        var clockState = new BlindBoxRuntimeState();
        AdvanceScheduleClock(clockState, GetWaitDurationMultiplier() * 10.0);
        if (Math.Abs(clockState.ScheduleSeconds - 10.0) > 0.001)
            GD.PushError("[BlindBox] Regression check failed: schedule clock rate does not match the wait-duration multiplier.");

        var sequenceSchedules = GetSequenceSchedules();
        var loopSchedule = GetLoopSchedule();
        var fallbackBox = GetFallbackRefreshmentBox();
        if (sequenceSchedules.Count == 0 || loopSchedule == null || fallbackBox == null)
            return;

        var state = new BlindBoxRuntimeState
        {
            SequenceIndex = sequenceSchedules.Count - 1,
            ScheduleSeconds = sequenceSchedules[^1].StartSeconds,
        };
        var finalNewbieSchedule = sequenceSchedules[^1];
        ConsumeOpenedSchedule(state, finalNewbieSchedule);
        CompleteClaimedSchedule(state, finalNewbieSchedule.Id);
        var expectedFirstPoint = state.ScheduleSeconds + GetLoopIntervalScheduleSeconds();
        if (!state.LoopStageStarted
            || Math.Abs(state.NextLoopPresentationSeconds - expectedFirstPoint) > 0.001
            || Math.Abs(state.NextLoopTriggerSeconds - state.ScheduleSeconds) > 0.001
            || GetAvailableSchedules(state).Any())
            GD.PushError("[BlindBox] Regression check failed: loop stage initialization is incorrect.");

        var restoredCountdownState = new BlindBoxRuntimeState
        {
            SequenceIndex = sequenceSchedules.Count,
            ScheduleSeconds = state.ScheduleSeconds + GetLoopIntervalScheduleSeconds() * 0.25,
            LoopStageStarted = true,
            NextLoopPresentationSeconds = state.NextLoopPresentationSeconds,
            NextLoopTriggerSeconds = state.NextLoopTriggerSeconds,
        };
        MaintainLoopPresentation(
            restoredCountdownState,
            canLockPresentation: true,
            fallbackBox.Id);
        if (restoredCountdownState.LockedLoopBlindBoxId != 0
            || Math.Abs(
                restoredCountdownState.NextLoopPresentationSeconds
                - state.NextLoopPresentationSeconds) > 0.001)
        {
            GD.PushError(
                "[BlindBox] Regression check failed: restoring a running loop countdown " +
                "locked or advanced its display point before it was due.");
        }

        state.ScheduleSeconds = expectedFirstPoint;
        MaintainLoopPresentation(state, canLockPresentation: true, loopSchedule.BlindBoxId);
        var firstLoop = GetAvailableSchedules(state).FirstOrDefault();
        if (firstLoop.Schedule?.Id != loopSchedule.Id || firstLoop.Box?.Id != loopSchedule.BlindBoxId)
        {
            GD.PushError("[BlindBox] Regression check failed: due loop presentation was not locked.");
            return;
        }

        var nextPoint = state.NextLoopPresentationSeconds;
        state.ScheduleSeconds = nextPoint;
        MaintainLoopPresentation(state, canLockPresentation: true, fallbackBox.Id);
        if (state.LockedLoopBlindBoxId != loopSchedule.BlindBoxId
            || state.NextLoopPresentationSeconds <= nextPoint)
            GD.PushError("[BlindBox] Regression check failed: an existing balloon did not skip the next display point.");

        ConsumeOpenedSchedule(state, loopSchedule);
        state.ScheduleSeconds = state.NextLoopPresentationSeconds;
        MaintainLoopPresentation(state, canLockPresentation: true, fallbackBox.Id);
        if (state.LockedLoopBlindBoxId != fallbackBox.Id)
            GD.PushError("[BlindBox] Regression check failed: fallback presentation was not locked at a later point.");
    }

    private void ValidateSteamPlaytimeScheduling()
    {
        var firstSchedule = LubanData.Tables.TbBlindBoxSchedule.DataList
            .Where(schedule => schedule.IsEnabled && schedule.SteamPlaytimeGeneratorItemDefId > 0)
            .OrderBy(schedule => schedule.StartSeconds)
            .ThenBy(schedule => schedule.Id)
            .FirstOrDefault();
        if (firstSchedule == null)
            return;

        var leadScheduleSeconds = GetSteamPlaytimeRequestLeadSeconds() * GetScheduleClockRate();
        var triggerSeconds = Math.Max(0.0, firstSchedule.StartSeconds - leadScheduleSeconds);
        if (triggerSeconds > 0.01)
        {
            var beforeState = new BlindBoxRuntimeState { ScheduleSeconds = triggerSeconds - 0.01 };
            if (TryGetNextSteamPlaytimeDrop(beforeState, out _, out _, out _))
                GD.PushError("[BlindBox] Regression check failed: Steam playtime drop became eligible too early.");
        }

        var eligibleState = new BlindBoxRuntimeState { ScheduleSeconds = triggerSeconds };
        if (!TryGetNextSteamPlaytimeDrop(
                eligibleState,
                out var selectedSchedule,
                out _,
                out var grantCount)
            || selectedSchedule?.Id != firstSchedule.Id
            || grantCount != 1)
        {
            GD.PushError("[BlindBox] Regression check failed: Steam playtime drop lead timing is incorrect.");
            return;
        }

        CompleteSteamPlaytimeDrop(eligibleState, firstSchedule.Id, grantCount);
        if (eligibleState.SteamPlaytimeDropStates[firstSchedule.Id].ProcessedGrantCount != 1)
            GD.PushError("[BlindBox] Regression check failed: Steam playtime drop progress was not recorded.");

        if (!ReopenCurrentSteamPlaytimeDrop(eligibleState, firstSchedule)
            || eligibleState.SteamPlaytimeDropStates[firstSchedule.Id].ProcessedGrantCount != 0)
        {
            GD.PushError("[BlindBox] Regression check failed: missing current Steam voucher was not reopened.");
        }
        if (!CompleteCurrentSteamPlaytimeDropFromInventory(eligibleState, firstSchedule)
            || eligibleState.SteamPlaytimeDropStates[firstSchedule.Id].ProcessedGrantCount != 1)
        {
            GD.PushError("[BlindBox] Regression check failed: an owned current Steam voucher did not satisfy its drop.");
        }

        var sequenceSchedules = GetSequenceSchedules();
        var sequenceIndex = sequenceSchedules.FindIndex(schedule => schedule.Id == firstSchedule.Id);
        if (sequenceIndex >= 0)
        {
            var legacyState = new BlindBoxRuntimeState
            {
                SequenceIndex = sequenceIndex + 1,
                ScheduleSeconds = firstSchedule.StartSeconds,
            };
            NormalizeSteamPlaytimeDropProgress(legacyState);
            if (legacyState.SteamPlaytimeDropStates[firstSchedule.Id].ProcessedGrantCount != 1)
                GD.PushError("[BlindBox] Regression check failed: completed legacy schedules would be triggered again.");
        }

        var loopSchedule = GetLoopSchedule();
        if (loopSchedule == null)
            return;

        var loopState = new BlindBoxRuntimeState
        {
            SequenceIndex = sequenceSchedules.Count,
            ScheduleSeconds = sequenceSchedules[^1].StartSeconds,
        };
        EnsureLoopStageInitialized(loopState);
        if (!TryGetNextSteamPlaytimeDrop(
                loopState,
                out var selectedLoopSchedule,
                out _,
                out var loopGrantCount)
            || selectedLoopSchedule?.Id != loopSchedule.Id
            || loopGrantCount != 1)
        {
            GD.PushError("[BlindBox] Regression check failed: initial loop Trigger heartbeat is incorrect.");
            return;
        }

        BeginSteamPlaytimeDrop(loopState, loopSchedule);
        if (!loopState.LoopDropVerificationPending
            || TryGetNextSteamPlaytimeDrop(loopState, out _, out _, out _))
        {
            GD.PushError("[BlindBox] Regression check failed: loop Trigger verification did not block duplicate requests.");
            return;
        }

        var previousTrigger = loopState.NextLoopTriggerSeconds;
        CompleteSteamPlaytimeDrop(loopState, loopSchedule.Id, loopGrantCount);
        if (loopState.LoopDropVerificationPending
            || loopState.NextLoopTriggerSeconds <= previousTrigger)
            GD.PushError("[BlindBox] Regression check failed: loop Trigger completion did not advance its heartbeat.");
    }
#endif

    private Item? RollReward(BlindBox box)
    {
        var rarity = RollRarity(box.Id);
        if (rarity == null)
            return null;

        var candidates = GetRewardCandidates(box, rarity.Value);
        return PickWeighted(candidates, entry => entry.Weight).Item;
    }

    private static List<(Item Item, int Weight)> GetRewardCandidates(BlindBox box, ERarity rarity)
    {
        var expectedAcquisition = box.BoxType switch
        {
            EBlindBoxType.Decoration => EAcquisitionType.DecorationBlindBox,
            EBlindBoxType.NewbieDecoration => EAcquisitionType.DecorationBlindBox,
            EBlindBoxType.Refreshment => EAcquisitionType.RefreshmentBlindBox,
            EBlindBoxType.Event => EAcquisitionType.EventReward,
            _ => EAcquisitionType.DebugOnly,
        };

        var candidates = LubanData.Tables.TbItem.DataList
            .Select(item => (Item: item, Weight: GetItemWeight(box.BoxType, item)))
            .Where(entry => entry.Weight > 0)
            .Where(entry => entry.Item.ItemRarity == rarity)
            .Where(entry => entry.Item.AcquisitionType == expectedAcquisition)
            .ToList();

        if (candidates.Count == 0)
        {
            candidates = LubanData.Tables.TbItem.DataList
                .Select(item => (Item: item, Weight: GetItemWeight(box.BoxType, item)))
                .Where(entry => entry.Weight > 0)
                .Where(entry => entry.Item.ItemRarity == rarity)
                .ToList();
        }

        return candidates;
    }

    private ERarity? RollRarity(int blindBoxId)
    {
        var rates = LubanData.Tables.TbBlindBoxRarityRate.DataList
            .Where(rate => rate.IsEnabled && rate.BlindBoxId == blindBoxId && rate.Weight > 0)
            .ToList();
        if (rates.Count == 0)
            return null;

        return PickWeighted(rates, rate => rate.Weight).Rarity;
    }

    private BlindBoxRevealPath? RollRevealPath(ERarity actualRarity)
    {
        var paths = LubanData.Tables.TbBlindBoxRevealPath.DataList
            .Where(path => path.IsEnabled && path.ActualRarity == actualRarity && path.Weight > 0)
            .ToList();
        if (paths.Count == 0)
            return null;

        return PickWeighted(paths, path => path.Weight);
    }

    private static int GetItemWeight(EBlindBoxType boxType, Item item)
    {
        return boxType switch
        {
            EBlindBoxType.Decoration => item.StandardBoxWeight,
            EBlindBoxType.NewbieDecoration => item.NewbieBoxWeight,
            EBlindBoxType.Refreshment => item.RefreshmentBoxWeight,
            EBlindBoxType.Event => item.EventBoxWeight,
            _ => 0,
        };
    }

    private T PickWeighted<T>(IReadOnlyList<T> entries, Func<T, int> getWeight)
    {
        var total = entries.Sum(getWeight);
        if (entries.Count == 0 || total <= 0)
            return default!;

        var roll = _random.Next(total);
        foreach (var entry in entries)
        {
            roll -= getWeight(entry);
            if (roll < 0)
                return entry;
        }
        return entries[^1];
    }

    private static string BuildDebugText(
        BlindBox box,
        BlindBoxSchedule schedule,
        Item item,
        BlindBoxRevealPath revealPath,
        double totalPlaySeconds,
        int cost)
    {
        return $"BlindBox: {box.Name} ({box.Id})\n"
            + $"Schedule: {schedule.Id}, Loop: {schedule.IsLoopTrack}\n"
            + $"RevealPath: {revealPath.Id}\n"
            + $"Time: {totalPlaySeconds:0.0}s\n"
            + $"Cost: {cost}\n"
            + $"Reward: {item.Name} ({item.Id})\n"
            + $"Type: {item.ItemType}, Rarity: {item.ItemRarity}";
    }

    private static string FormatSeconds(double seconds)
    {
        return TimeSpan.FromSeconds(Math.Max(0, seconds)).ToString(@"hh\:mm\:ss");
    }
}
