#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using DataTables;
using Godot;

namespace LuckyDogRise;

public sealed class PendingBlindBoxReward
{
    public int BlindBoxId { get; set; }
    public int ScheduleId { get; set; }
    public int ItemId { get; set; }
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
    public double LastClaimSeconds { get; set; }
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
    }

    public BlindBox? GetNextAvailableBox(
        double totalPlaySeconds,
        BlindBoxRuntimeState runtimeState,
        PendingBlindBoxReward? pendingReward)
    {
        if (pendingReward != null)
            return LubanData.Tables.TbBlindBox.GetOrDefault(pendingReward.BlindBoxId);

        return GetAvailableSchedules(totalPlaySeconds, runtimeState)
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
        RefreshLoopTracks(totalPlaySeconds, runtimeState);

        var scaledSeconds = GetScaledSeconds(totalPlaySeconds);
        var timeScale = GetTimeScale();
        var builder = new StringBuilder();
        builder.AppendLine($"游玩: {FormatSeconds(totalPlaySeconds)}");
        builder.AppendLine($"调度表时间: {FormatSeconds(scaledSeconds)}");
        builder.AppendLine($"上次领取: {FormatSeconds(runtimeState.LastClaimSeconds)}");

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

        var available = GetAvailableSchedules(totalPlaySeconds, runtimeState).FirstOrDefault();
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
                ? Math.Max(0, Math.Max(sequence.StartSeconds, sequence.IntervalSeconds) - scaledSeconds)
                : Math.Max(0, sequence.IntervalSeconds - (scaledSeconds - runtimeState.LastClaimSeconds));
            builder.AppendLine($"新手: #{runtimeState.SequenceIndex}, 调度={sequence.Id}, {box?.Name ?? "缺失"}");
            builder.AppendLine($"新手等待: {FormatSeconds(waitScaledSeconds * timeScale)}");
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
            var cooldownScaledSeconds = Math.Max(0, schedule.IntervalSeconds - (scaledSeconds - runtimeState.LastClaimSeconds));
            var cooldownRealSeconds = cooldownScaledSeconds * timeScale;
            builder.AppendLine($"- {schedule.Id} {box?.Name ?? "缺失"} 待={state?.PendingCount ?? 0}, 已={state?.ProcessedGrantCount ?? 0}, CD={cooldownRealSeconds:0.0}s");
        }

        return builder.ToString().TrimEnd();
    }

    public BlindBoxHintState GetHintState(
        double totalPlaySeconds,
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

        RefreshLoopTracks(totalPlaySeconds, runtimeState);
        var available = GetAvailableSchedules(totalPlaySeconds, runtimeState).FirstOrDefault();
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
            RemainingSeconds = GetNextReadyRemainingSeconds(totalPlaySeconds, runtimeState),
        };
    }

    public BlindBoxOpenResult? TryOpenNext(
        double totalPlaySeconds,
        BlindBoxRuntimeState runtimeState)
    {
        RefreshLoopTracks(totalPlaySeconds, runtimeState);

        var candidate = GetAvailableSchedules(totalPlaySeconds, runtimeState).FirstOrDefault();
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

        var pending = new PendingBlindBoxReward
        {
            BlindBoxId = candidate.Box.Id,
            ScheduleId = candidate.Schedule.Id,
            ItemId = item.Id,
            TotalPlaySeconds = totalPlaySeconds,
            DebugText = BuildDebugText(candidate.Box, candidate.Schedule, item, totalPlaySeconds, cost),
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
        double totalPlaySeconds,
        BlindBoxRuntimeState runtimeState)
    {
        var scaledSeconds = GetScaledSeconds(totalPlaySeconds);
        RefreshLoopTracks(totalPlaySeconds, runtimeState);

        var sequenceSchedule = GetCurrentSequenceSchedule(runtimeState);
        if (sequenceSchedule != null && IsSequenceAvailable(sequenceSchedule, scaledSeconds, runtimeState))
        {
            var box = LubanData.Tables.TbBlindBox.GetOrDefault(sequenceSchedule.BlindBoxId);
            if (box != null && box.IsEnabled)
                return [(sequenceSchedule, box)];
        }

        if (sequenceSchedule != null)
            return [];

        return LubanData.Tables.TbBlindBoxSchedule.DataList
            .Where(schedule => schedule.IsEnabled && schedule.IsLoopTrack)
            .Select(schedule => (Schedule: schedule, Box: LubanData.Tables.TbBlindBox.GetOrDefault(schedule.BlindBoxId)))
            .Where(entry => entry.Box != null && entry.Box.IsEnabled)
            .Where(entry => runtimeState.LoopTrackStates.TryGetValue(entry.Schedule.Id, out var state) && state.PendingCount > 0)
            .Where(entry => IsClaimCooldownReady(entry.Schedule, scaledSeconds, runtimeState))
            .OrderByDescending(entry => entry.Schedule.Priority)
            .ThenBy(entry => entry.Schedule.StartSeconds)
            .ThenBy(entry => entry.Schedule.Id);
    }

    public void ConsumeOpenedSchedule(BlindBoxRuntimeState runtimeState, BlindBoxSchedule schedule, double totalPlaySeconds)
    {
        if (schedule.IsLoopTrack)
        {
            if (runtimeState.LoopTrackStates.TryGetValue(schedule.Id, out var state))
                state.PendingCount = Mathf.Max(0, state.PendingCount - 1);
            runtimeState.LastClaimSeconds = GetScaledSeconds(totalPlaySeconds);
            return;
        }

        var sequenceSchedules = GetSequenceSchedules();
        if (runtimeState.SequenceIndex < sequenceSchedules.Count
            && sequenceSchedules[runtimeState.SequenceIndex].Id == schedule.Id)
        {
            runtimeState.SequenceIndex++;
            runtimeState.LastClaimSeconds = GetScaledSeconds(totalPlaySeconds);
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
        double scaledSeconds,
        BlindBoxRuntimeState runtimeState)
    {
        if (scaledSeconds < schedule.StartSeconds)
            return false;

        var waitSeconds = Mathf.Max(0, schedule.IntervalSeconds);
        if (runtimeState.SequenceIndex == 0)
            return scaledSeconds >= waitSeconds;

        return scaledSeconds - runtimeState.LastClaimSeconds >= waitSeconds;
    }

    private static bool IsClaimCooldownReady(
        BlindBoxSchedule schedule,
        double scaledSeconds,
        BlindBoxRuntimeState runtimeState)
    {
        var waitSeconds = Mathf.Max(0, schedule.IntervalSeconds);
        return scaledSeconds - runtimeState.LastClaimSeconds >= waitSeconds;
    }

    private static double GetNextReadyRemainingSeconds(double totalPlaySeconds, BlindBoxRuntimeState runtimeState)
    {
        var scaledSeconds = GetScaledSeconds(totalPlaySeconds);
        var timeScale = GetTimeScale();

        var sequence = GetCurrentSequenceSchedule(runtimeState);
        if (sequence != null)
        {
            var waitScaledSeconds = runtimeState.SequenceIndex == 0
                ? Math.Max(0, Math.Max(sequence.StartSeconds, sequence.IntervalSeconds) - scaledSeconds)
                : Math.Max(0, sequence.IntervalSeconds - (scaledSeconds - runtimeState.LastClaimSeconds));
            return waitScaledSeconds * timeScale;
        }

        var waits = new List<double>();
        foreach (var schedule in LubanData.Tables.TbBlindBoxSchedule.DataList
                     .Where(schedule => schedule.IsEnabled && schedule.IsLoopTrack))
        {
            runtimeState.LoopTrackStates.TryGetValue(schedule.Id, out var state);
            var cooldown = Math.Max(0, schedule.IntervalSeconds - (scaledSeconds - runtimeState.LastClaimSeconds));
            if ((state?.PendingCount ?? 0) > 0)
            {
                waits.Add(cooldown);
                continue;
            }

            var processed = state?.ProcessedGrantCount ?? 0;
            var nextDue = schedule.IntervalSeconds <= 0
                ? schedule.StartSeconds
                : schedule.StartSeconds + processed * schedule.IntervalSeconds;
            waits.Add(Math.Max(0, nextDue - scaledSeconds));
        }

        return waits.Count == 0 ? 0 : waits.Min() * timeScale;
    }


    private static void RefreshLoopTracks(double totalPlaySeconds, BlindBoxRuntimeState runtimeState)
    {
        var scaledSeconds = GetScaledSeconds(totalPlaySeconds);
        foreach (var schedule in LubanData.Tables.TbBlindBoxSchedule.DataList
                     .Where(schedule => schedule.IsEnabled && schedule.IsLoopTrack))
        {
            if (!runtimeState.LoopTrackStates.TryGetValue(schedule.Id, out var state))
            {
                state = new BlindBoxScheduleState();
                runtimeState.LoopTrackStates[schedule.Id] = state;
            }

            var due = GetDueGrantCount(schedule, scaledSeconds);
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

    private static int GetDueGrantCount(BlindBoxSchedule schedule, double scaledSeconds)
    {
        if (scaledSeconds < schedule.StartSeconds)
            return 0;

        var effectiveEnd = schedule.EndSeconds < 0
            ? scaledSeconds
            : Math.Min(scaledSeconds, schedule.EndSeconds);
        if (effectiveEnd < schedule.StartSeconds)
            return 0;

        var due = schedule.IntervalSeconds <= 0
            ? 1
            : 1 + (int)Math.Floor((effectiveEnd - schedule.StartSeconds) / schedule.IntervalSeconds);
        if (schedule.MaxGrantCount >= 0)
            due = Math.Min(due, schedule.MaxGrantCount);
        return Math.Max(0, due);
    }

    private static double GetScaledSeconds(double totalPlaySeconds)
    {
        return totalPlaySeconds / GetTimeScale();
    }

    private static float GetTimeScale()
    {
        var config = LubanData.Tables.TbGameDevelopConfig.DataList.FirstOrDefault();
        return config == null || config.BlindBoxTimeScale <= 0 ? 1f : config.BlindBoxTimeScale;
    }

    private Item? RollReward(BlindBox box)
    {
        var rarity = RollRarity(box.Id);
        if (rarity == null)
            return null;

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
            .Where(entry => entry.Item.ItemRarity == rarity.Value)
            .Where(entry => entry.Item.AcquisitionType == expectedAcquisition)
            .ToList();

        if (candidates.Count == 0)
        {
            candidates = LubanData.Tables.TbItem.DataList
                .Select(item => (Item: item, Weight: GetItemWeight(box.BoxType, item)))
                .Where(entry => entry.Weight > 0)
                .Where(entry => entry.Item.ItemRarity == rarity.Value)
                .ToList();
        }

        return PickWeighted(candidates, entry => entry.Weight).Item;
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
        double totalPlaySeconds,
        int cost)
    {
        return $"BlindBox: {box.Name} ({box.Id})\n"
            + $"Schedule: {schedule.Id}, Priority: {schedule.Priority}\n"
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
