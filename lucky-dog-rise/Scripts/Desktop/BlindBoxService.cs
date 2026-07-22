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
    public double TotalPlaySeconds { get; set; }
    public string DebugText { get; set; } = "";
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
    /// <summary>正式阶段下一个盲盒允许在本地展示的调度表时间。</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public double NextLoopPresentationSeconds { get; set; }
    public Dictionary<int, BlindBoxScheduleState> LoopTrackStates { get; set; } = new();
}

public enum BlindBoxHintStatus
{
    Waiting,
    Ready,
    NotEnoughChips,
    PendingReward,
}

public sealed class BlindBoxHintState
{
    public BlindBoxHintStatus Status { get; init; }
    public BlindBox? Box { get; init; }
    public int Cost { get; init; }
    public double RemainingSeconds { get; init; }
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
        RefreshLoopTracks(runtimeState);

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
            builder.AppendLine($"调度: {available.Schedule.Id}, 循环={available.Schedule.IsLoopTrack}, 优先={available.Schedule.Priority}");
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

        builder.AppendLine("循环轨道:");
        foreach (var schedule in LubanData.Tables.TbBlindBoxSchedule.DataList
                     .Where(schedule => schedule.IsEnabled && schedule.IsLoopTrack)
                     .OrderByDescending(schedule => schedule.Priority)
                     .ThenBy(schedule => schedule.Id))
        {
            runtimeState.LoopTrackStates.TryGetValue(schedule.Id, out var state);
            var box = LubanData.Tables.TbBlindBox.GetOrDefault(schedule.BlindBoxId);
            var nextDue = schedule.IntervalSeconds <= 0
                ? schedule.StartSeconds
                : schedule.StartSeconds + (state?.ProcessedGrantCount ?? 0) * schedule.IntervalSeconds;
            var nextDueRealSeconds = ToRealSeconds(Math.Max(0, nextDue - scheduleSeconds));
            builder.AppendLine($"- {schedule.Id} {box?.Name ?? "缺失"} 待={state?.PendingCount ?? 0}, 已={state?.ProcessedGrantCount ?? 0}, 下次资格={nextDueRealSeconds:0.0}s");
        }

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

        RefreshLoopTracks(runtimeState);
        var available = GetAvailableSchedules(runtimeState).FirstOrDefault();
        if (available.Schedule != null && available.Box != null)
        {
            var cost = GetDisplayCost(available.Box);
            return new BlindBoxHintState
            {
                Status = _gameData.Chips >= cost ? BlindBoxHintStatus.Ready : BlindBoxHintStatus.NotEnoughChips,
                Box = available.Box,
                Cost = cost,
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
        RefreshLoopTracks(runtimeState);

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

        var revealPath = RollRevealPath(item.ItemRarity);
        if (revealPath == null)
        {
            GD.PushError($"[BlindBox] No reveal path for rarity {item.ItemRarity}.");
            return null;
        }

        _gameData.ModifyChips(-cost);

        var pending = new PendingBlindBoxReward
        {
            BlindBoxId = candidate.Box.Id,
            ScheduleId = candidate.Schedule.Id,
            ItemId = item.Id,
            RevealPathId = revealPath.Id,
            RevealStep = 0,
            RewardShown = candidate.Box.DisplayMode == EBlindBoxDisplayMode.DirectReward,
            TotalPlaySeconds = totalPlaySeconds,
            DebugText = BuildDebugText(candidate.Box, candidate.Schedule, item, revealPath, totalPlaySeconds, cost),
        };

        return new BlindBoxOpenResult
        {
            Box = candidate.Box,
            Schedule = candidate.Schedule,
            Item = item,
            PendingReward = pending,
        };
    }

    private IEnumerable<(BlindBoxSchedule Schedule, BlindBox Box)> GetAvailableSchedules(
        BlindBoxRuntimeState runtimeState)
    {
        var scheduleSeconds = runtimeState.ScheduleSeconds;
        RefreshLoopTracks(runtimeState);

        var sequenceSchedule = GetCurrentSequenceSchedule(runtimeState);
        if (sequenceSchedule != null && IsSequenceAvailable(sequenceSchedule, scheduleSeconds, runtimeState))
        {
            var box = LubanData.Tables.TbBlindBox.GetOrDefault(sequenceSchedule.BlindBoxId);
            if (box != null && box.IsEnabled)
                return [(sequenceSchedule, box)];
        }

        if (sequenceSchedule != null)
            return [];

        if (scheduleSeconds < runtimeState.NextLoopPresentationSeconds)
            return [];

        return LubanData.Tables.TbBlindBoxSchedule.DataList
            .Where(schedule => schedule.IsEnabled && schedule.IsLoopTrack)
            .Select(schedule => (Schedule: schedule, Box: LubanData.Tables.TbBlindBox.GetOrDefault(schedule.BlindBoxId)))
            .Where(entry => entry.Box != null && entry.Box.IsEnabled)
            .Where(entry => runtimeState.LoopTrackStates.TryGetValue(entry.Schedule.Id, out var state) && state.PendingCount > 0)
            .OrderByDescending(entry => entry.Schedule.Priority)
            .ThenBy(entry => entry.Schedule.StartSeconds)
            .ThenBy(entry => entry.Schedule.Id);
    }

    public void ConsumeOpenedSchedule(BlindBoxRuntimeState runtimeState, BlindBoxSchedule schedule)
    {
        if (schedule.IsLoopTrack)
        {
            if (runtimeState.LoopTrackStates.TryGetValue(schedule.Id, out var state))
                state.PendingCount = Mathf.Max(0, state.PendingCount - 1);
            return;
        }

        var sequenceSchedules = GetSequenceSchedules();
        if (runtimeState.SequenceIndex < sequenceSchedules.Count
            && sequenceSchedules[runtimeState.SequenceIndex].Id == schedule.Id)
        {
            runtimeState.SequenceIndex++;
        }
    }

    /// <summary>奖励真正进入玩家背包后，再从此刻开始计算新手间隔或积压短 CD。</summary>
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
                runtimeState.NextLoopPresentationSeconds = scheduleSeconds + GetPostNewbieDelaySeconds();
            return;
        }

        RefreshLoopTracks(runtimeState);
        var hasBacklog = runtimeState.LoopTrackStates.Values.Any(state => state.PendingCount > 0);
        runtimeState.NextLoopPresentationSeconds = scheduleSeconds + (hasBacklog ? GetBacklogClaimDelaySeconds() : 0.0);
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

        var waits = new List<double>();
        foreach (var schedule in LubanData.Tables.TbBlindBoxSchedule.DataList
                     .Where(schedule => schedule.IsEnabled && schedule.IsLoopTrack))
        {
            runtimeState.LoopTrackStates.TryGetValue(schedule.Id, out var state);
            if ((state?.PendingCount ?? 0) > 0)
            {
                waits.Add(0.0);
                continue;
            }

            var processed = state?.ProcessedGrantCount ?? 0;
            var nextDue = schedule.IntervalSeconds <= 0
                ? schedule.StartSeconds
                : schedule.StartSeconds + processed * schedule.IntervalSeconds;
            waits.Add(Math.Max(0, nextDue - scheduleSeconds));
        }

        var qualificationWait = waits.Count == 0 ? 0.0 : waits.Min();
        var presentationWait = Math.Max(0, runtimeState.NextLoopPresentationSeconds - scheduleSeconds);
        return ToRealSeconds(Math.Max(qualificationWait, presentationWait));
    }


    private static void RefreshLoopTracks(BlindBoxRuntimeState runtimeState)
    {
        var scheduleSeconds = runtimeState.ScheduleSeconds;
        foreach (var schedule in LubanData.Tables.TbBlindBoxSchedule.DataList
                     .Where(schedule => schedule.IsEnabled && schedule.IsLoopTrack))
        {
            if (!runtimeState.LoopTrackStates.TryGetValue(schedule.Id, out var state))
            {
                state = new BlindBoxScheduleState();
                runtimeState.LoopTrackStates[schedule.Id] = state;
            }

            var due = GetDueGrantCount(schedule, scheduleSeconds);
            if (due <= state.ProcessedGrantCount)
                continue;

            var maxPending = GetMaxPendingCount(schedule);
            for (var i = state.ProcessedGrantCount; i < due; i++)
            {
                if (state.PendingCount < maxPending)
                    state.PendingCount++;
            }
            state.ProcessedGrantCount = due;
        }
    }

    private static int GetMaxPendingCount(BlindBoxSchedule schedule)
    {
        if (!schedule.CanAccumulate)
            return 1;
        return schedule.MaxPendingCount < 0 ? int.MaxValue : Mathf.Max(0, schedule.MaxPendingCount);
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

    private static double ToRealSeconds(double scheduleSeconds) =>
        scheduleSeconds * GetWaitDurationMultiplier();

    private static double GetBacklogClaimDelaySeconds()
    {
        var config = LubanData.Tables.TbGameDevelopConfig.DataList.FirstOrDefault();
        return config == null ? 0.0 : Math.Max(0.0, config.BlindBoxBacklogClaimDelaySeconds);
    }

    private static double GetPostNewbieDelaySeconds()
    {
        var config = LubanData.Tables.TbGameDevelopConfig.DataList.FirstOrDefault();
        return config == null ? 0.0 : Math.Max(0.0, config.BlindBoxPostNewbieDelaySeconds);
    }

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
            if (config.BlindBoxBacklogClaimDelaySeconds < 0)
                GD.PushError("[BlindBox] BlindBoxBacklogClaimDelaySeconds cannot be negative.");
            if (config.BlindBoxPostNewbieDelaySeconds < 0)
                GD.PushError("[BlindBox] BlindBoxPostNewbieDelaySeconds cannot be negative.");
        }

        var enabledSchedules = LubanData.Tables.TbBlindBoxSchedule.DataList
            .Where(schedule => schedule.IsEnabled)
            .ToList();
        if (!enabledSchedules.Any(schedule => !schedule.IsLoopTrack))
            GD.PushError("[BlindBox] No enabled newbie schedules.");
        if (!enabledSchedules.Any(schedule => schedule.IsLoopTrack))
            GD.PushError("[BlindBox] No enabled loop schedules.");

        foreach (var schedule in enabledSchedules)
        {
            var box = LubanData.Tables.TbBlindBox.GetOrDefault(schedule.BlindBoxId);
            if (box == null || !box.IsEnabled)
            {
                GD.PushError($"[BlindBox] Schedule {schedule.Id} references a missing or disabled box {schedule.BlindBoxId}.");
                continue;
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
#endif
    }

#if DEBUG
    /// <summary>使用当前表数据回归检查：正式资格可后台积压，但本地受新手后延迟和积压短 CD 控制。</summary>
    private void ValidatePresentationScheduling()
    {
        var clockState = new BlindBoxRuntimeState();
        AdvanceScheduleClock(clockState, GetWaitDurationMultiplier() * 10.0);
        if (Math.Abs(clockState.ScheduleSeconds - 10.0) > 0.001)
            GD.PushError("[BlindBox] Regression check failed: schedule clock rate does not match the wait-duration multiplier.");

        var sequenceSchedules = GetSequenceSchedules();
        var loopSchedules = LubanData.Tables.TbBlindBoxSchedule.DataList
            .Where(schedule => schedule.IsEnabled && schedule.IsLoopTrack)
            .ToList();
        if (sequenceSchedules.Count == 0 || loopSchedules.Count < 2)
            return;

        var scheduleNow = loopSchedules.Max(schedule => schedule.StartSeconds)
            + loopSchedules.Max(schedule => Math.Max(1, schedule.IntervalSeconds)) * 2.0;
        var state = new BlindBoxRuntimeState
        {
            SequenceIndex = sequenceSchedules.Count - 1,
            ScheduleSeconds = scheduleNow,
        };
        RefreshLoopTracks(state);
        if (state.LoopTrackStates.Values.Count(track => track.PendingCount > 0) < 2)
        {
            GD.PushError("[BlindBox] Regression check failed: loop qualifications did not accumulate during newbie progression.");
            return;
        }

        var finalNewbieSchedule = sequenceSchedules[^1];
        ConsumeOpenedSchedule(state, finalNewbieSchedule);
        CompleteClaimedSchedule(state, finalNewbieSchedule.Id);
        var expectedPostNewbieGate = scheduleNow + GetPostNewbieDelaySeconds();
        if (Math.Abs(state.NextLoopPresentationSeconds - expectedPostNewbieGate) > 0.001
            || GetAvailableSchedules(state).Any())
            GD.PushError("[BlindBox] Regression check failed: post-newbie presentation gate is incorrect.");

        state.ScheduleSeconds = expectedPostNewbieGate;
        var firstLoop = GetAvailableSchedules(state).FirstOrDefault();
        if (firstLoop.Schedule == null || firstLoop.Schedule.Priority != loopSchedules.Max(schedule => schedule.Priority))
        {
            GD.PushError("[BlindBox] Regression check failed: accumulated loop priority is incorrect.");
            return;
        }

        ConsumeOpenedSchedule(state, firstLoop.Schedule);
        CompleteClaimedSchedule(state, firstLoop.Schedule.Id);
        var expectedBacklogGate = expectedPostNewbieGate + GetBacklogClaimDelaySeconds();
        if (Math.Abs(state.NextLoopPresentationSeconds - expectedBacklogGate) > 0.001
            || GetAvailableSchedules(state).Any())
            GD.PushError("[BlindBox] Regression check failed: backlog presentation gate is incorrect.");
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
            _ => item.BlindBoxWeight,
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
            + $"Schedule: {schedule.Id}, Priority: {schedule.Priority}\n"
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
