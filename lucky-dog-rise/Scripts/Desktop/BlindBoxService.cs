#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
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
        IReadOnlyDictionary<int, int> claimedCountsBySchedule,
        PendingBlindBoxReward? pendingReward)
    {
        if (pendingReward != null)
            return LubanData.Tables.TbBlindBox.GetOrDefault(pendingReward.BlindBoxId);

        return GetAvailableSchedules(totalPlaySeconds, claimedCountsBySchedule)
            .Select(entry => entry.Box)
            .FirstOrDefault();
    }

    public int GetDisplayCost(BlindBox box)
    {
        var config = LubanData.Tables.TbGameDevelopConfig.DataList.FirstOrDefault();
        var scale = config == null || config.BlindBoxCostScale <= 0 ? 1f : config.BlindBoxCostScale;
        return Mathf.Max(0, Mathf.RoundToInt(box.CostChips * scale));
    }

    public BlindBoxOpenResult? TryOpenNext(
        double totalPlaySeconds,
        IReadOnlyDictionary<int, int> claimedCountsBySchedule)
    {
        var candidate = GetAvailableSchedules(totalPlaySeconds, claimedCountsBySchedule).FirstOrDefault();
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
        IReadOnlyDictionary<int, int> claimedCountsBySchedule)
    {
        var scaledSeconds = GetScaledSeconds(totalPlaySeconds);

        return LubanData.Tables.TbBlindBoxSchedule.DataList
            .Where(schedule => schedule.IsEnabled)
            .Select(schedule => (Schedule: schedule, Box: LubanData.Tables.TbBlindBox.GetOrDefault(schedule.BlindBoxId)))
            .Where(entry => entry.Box != null && entry.Box.IsEnabled)
            .Where(entry => GetPendingCount(entry.Schedule, scaledSeconds, claimedCountsBySchedule) > 0)
            .OrderByDescending(entry => entry.Schedule.Priority)
            .ThenBy(entry => entry.Schedule.StartSeconds)
            .ThenBy(entry => entry.Schedule.Id);
    }

    private static int GetPendingCount(
        BlindBoxSchedule schedule,
        double scaledSeconds,
        IReadOnlyDictionary<int, int> claimedCountsBySchedule)
    {
        var due = GetDueGrantCount(schedule, scaledSeconds);
        claimedCountsBySchedule.TryGetValue(schedule.Id, out var claimed);
        var pending = Mathf.Max(0, due - claimed);
        if (!schedule.CanAccumulate)
            pending = Mathf.Min(pending, 1);
        if (schedule.MaxPendingCount >= 0)
            pending = Mathf.Min(pending, schedule.MaxPendingCount);
        return pending;
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
        var config = LubanData.Tables.TbGameDevelopConfig.DataList.FirstOrDefault();
        var scale = config == null || config.BlindBoxTimeScale <= 0 ? 1f : config.BlindBoxTimeScale;
        return totalPlaySeconds * scale;
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
            .Where(entry => !entry.Item.IsUnique || !_gameData.Inventory.Owns(entry.Item.Id))
            .ToList();

        if (candidates.Count == 0)
        {
            candidates = LubanData.Tables.TbItem.DataList
                .Select(item => (Item: item, Weight: GetItemWeight(box.BoxType, item)))
                .Where(entry => entry.Weight > 0)
                .Where(entry => entry.Item.ItemRarity == rarity.Value)
                .Where(entry => !entry.Item.IsUnique || !_gameData.Inventory.Owns(entry.Item.Id))
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
}
