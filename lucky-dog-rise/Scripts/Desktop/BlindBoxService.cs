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
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public ulong PlatformInstanceId { get; set; }
    public bool CompletesSchedule { get; set; } = true;
    public double TotalPlaySeconds { get; set; }
    public string DebugText { get; set; } = "";
}

public enum BlindBoxPreparationPhase
{
    Submitted,
    RevalidationRequired,
    RetryWaiting,
}

public sealed class PendingBlindBoxPreparation
{
    public int ScheduleId { get; set; }
    public int BlindBoxId { get; set; }
    public int GeneratorItemDefId { get; set; }
    public BlindBoxPreparationPhase Phase { get; set; }
    public bool IsLate { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool StopRetryAfterFallback { get; set; }
    public double SubmittedAtTotalPlaySeconds { get; set; }
    public double RetryNotBeforeTotalPlaySeconds { get; set; }
    public Dictionary<ulong, uint> InventoryQuantitiesBeforeRequest { get; set; } = new();
}

public sealed class PreparedBlindBoxReward
{
    public const double InventoryVisibilityGraceSeconds = 30.0;
    public const int MinimumMissingInventorySnapshots = 2;

    public int ScheduleId { get; set; }
    public int BlindBoxId { get; set; }
    public ulong PlatformInstanceId { get; set; }
    public int SteamItemDefId { get; set; }
    public int ItemId { get; set; }
    public bool IsLate { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? ConfirmedAtTotalPlaySeconds { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? FirstMissingAtTotalPlaySeconds { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public int ConsecutiveMissingInventorySnapshots { get; set; }

    public void MarkConfirmed(double totalPlaySeconds)
    {
        ConfirmedAtTotalPlaySeconds ??= totalPlaySeconds;
        MarkPresent();
    }

    public bool MarkMissingAndShouldDiscard(double totalPlaySeconds)
    {
        ConfirmedAtTotalPlaySeconds ??= totalPlaySeconds;
        FirstMissingAtTotalPlaySeconds ??= totalPlaySeconds;
        ConsecutiveMissingInventorySnapshots++;
        return ConsecutiveMissingInventorySnapshots >= MinimumMissingInventorySnapshots
               && totalPlaySeconds - FirstMissingAtTotalPlaySeconds.Value
               >= InventoryVisibilityGraceSeconds;
    }

    public bool MarkPresent()
    {
        if (FirstMissingAtTotalPlaySeconds == null
            && ConsecutiveMissingInventorySnapshots == 0)
            return false;

        FirstMissingAtTotalPlaySeconds = null;
        ConsecutiveMissingInventorySnapshots = 0;
        return true;
    }
}

public sealed class PendingPlaytimeGeneratorActivation
{
    public int ScheduleId { get; set; }
    public int BlindBoxId { get; set; }
    public int GeneratorItemDefId { get; set; }
    public double SubmittedAtTotalPlaySeconds { get; set; }
    public bool CallbackCompleted { get; set; }
    public bool CallbackSucceeded { get; set; }
    public Dictionary<ulong, uint> InventoryQuantitiesBeforeRequest { get; set; } = new();
}

public sealed class PlaytimeGeneratorActivationState
{
    public Dictionary<int, double> ActivatedAtTotalPlaySecondsByGenerator { get; set; } = new();
    public PendingPlaytimeGeneratorActivation? PendingActivation { get; set; }
    public PreparedBlindBoxReward? DeferredReward { get; set; }
}

public enum LockedBlindBoxPresentationKind
{
    ScheduledLocal,
    PreparedSteam,
    Fallback,
    LateSteam,
    /// <summary>
    /// Steam reward arrived after one or more Fallback presentations, but the newcomer
    /// Schedule deliberately stayed at the same entry. Uses late-reward pricing while
    /// completing that Schedule when claimed.
    /// </summary>
    DeferredSequenceSteam,
}

public sealed class LockedBlindBoxPresentation
{
    public int ScheduleId { get; set; }
    public int BlindBoxId { get; set; }
    public LockedBlindBoxPresentationKind Kind { get; set; }
    public ulong PreparedPlatformInstanceId { get; set; }
}

public sealed class BlindBoxOpenResult
{
    public required BlindBox Box { get; init; }
    public required BlindBoxSchedule Schedule { get; init; }
    public required Item Item { get; init; }
    public required PendingBlindBoxReward PendingReward { get; init; }
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
    public bool LoopStageStarted { get; set; }
    public PendingBlindBoxPreparation? PendingPreparation { get; set; }
    public PreparedBlindBoxReward? PreparedReward { get; set; }
    public LockedBlindBoxPresentation? LockedPresentation { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public PlaytimeGeneratorActivationState? GeneratorActivation { get; set; }
}

public enum BlindBoxHintStatus
{
    Waiting,
    Ready,
    NotEnoughChips,
    PendingReward,
    Opening,
}

public enum BlindBoxPaymentSource
{
    Unknown,
    Chips,
    LocalRefreshment,
    SteamPrepared,
    SteamLate,
    SteamFallback,
}

public readonly record struct BlindBoxPrice(
    int ActualCost,
    int DisplayValue,
    EBlindBoxValueMode ValueMode,
    bool StrikeThrough);

public sealed class BlindBoxHintState
{
    public BlindBoxHintStatus Status { get; init; }
    public BlindBox? Box { get; init; }
    public int Cost { get; init; }
    public int DisplayValue { get; init; }
    public EBlindBoxValueMode ValueMode { get; init; } = EBlindBoxValueMode.Chips;
    public bool StrikeThrough { get; init; }
    public double RemainingSeconds { get; init; }
    public BlindBoxPaymentSource PaymentSource { get; init; }
}

public sealed class BlindBoxService
{
    public const double SteamPlaytimeDropMinimumAttemptIntervalSeconds = 65.0;
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

        runtimeState.ScheduleSeconds += realDeltaSeconds;
    }

    public bool TryGetPreparationCandidate(
        BlindBoxRuntimeState runtimeState,
        out BlindBoxSchedule? schedule,
        out BlindBox? box)
    {
        schedule = null;
        box = null;
        if (runtimeState.PendingPreparation != null
            || runtimeState.PreparedReward != null
            || runtimeState.GeneratorActivation?.DeferredReward != null)
            return false;

        var sequence = GetCurrentSequenceSchedule(runtimeState);
        if (sequence != null)
        {
            if (sequence.SteamPlaytimeGeneratorItemDefId <= 0)
                return false;

            schedule = sequence;
            box = LubanData.Tables.TbBlindBox.GetOrDefault(sequence.BlindBoxId);
            return box is { IsEnabled: true, IsPlatformInventoryRequired: true };
        }

        EnsureLoopStageInitialized(runtimeState);
        var loopSchedule = GetLoopSchedule();
        if (!runtimeState.LoopStageStarted
            || runtimeState.ScheduleSeconds < runtimeState.NextLoopTriggerSeconds
            || loopSchedule?.SteamPlaytimeGeneratorItemDefId <= 0)
            return false;

        schedule = loopSchedule;
        box = LubanData.Tables.TbBlindBox.GetOrDefault(loopSchedule!.BlindBoxId);
        return box is { IsEnabled: true, IsPlatformInventoryRequired: true };
    }

    public void MarkPreparationRequestAccepted(
        BlindBoxRuntimeState runtimeState,
        BlindBoxSchedule schedule)
    {
        if (schedule.IsLoopTrack)
            AdvanceLoopTriggerHeartbeat(runtimeState);
    }

    public bool MaintainPresentation(BlindBoxRuntimeState runtimeState)
    {
        var changed = EnsureLoopStageInitialized(runtimeState);
        if (runtimeState.LockedPresentation != null)
            return changed;

        var schedule = GetCurrentSequenceSchedule(runtimeState);
        if (schedule != null)
        {
            if (GetSequencePresentationRemainingSeconds(runtimeState, schedule) > 0.001)
                return changed;
        }
        else
        {
            schedule = GetLoopSchedule();
            if (schedule == null
                || !runtimeState.LoopStageStarted
                || runtimeState.ScheduleSeconds < runtimeState.NextLoopPresentationSeconds)
                return changed;
        }

        var configuredBox = LubanData.Tables.TbBlindBox.GetOrDefault(schedule.BlindBoxId);
        if (configuredBox is not { IsEnabled: true })
            return changed;

        if (runtimeState.PreparedReward is { } prepared)
        {
            var kind = prepared.IsLate
                ? !schedule.IsLoopTrack && prepared.ScheduleId == schedule.Id
                    ? LockedBlindBoxPresentationKind.DeferredSequenceSteam
                    : LockedBlindBoxPresentationKind.LateSteam
                : LockedBlindBoxPresentationKind.PreparedSteam;
            runtimeState.LockedPresentation = new LockedBlindBoxPresentation
            {
                ScheduleId = kind == LockedBlindBoxPresentationKind.LateSteam
                    ? prepared.ScheduleId
                    : schedule.Id,
                BlindBoxId = prepared.BlindBoxId,
                Kind = kind,
                PreparedPlatformInstanceId = prepared.PlatformInstanceId,
            };
        }
        else if (!configuredBox.IsPlatformInventoryRequired
                 || schedule.SteamPlaytimeGeneratorItemDefId <= 0)
        {
            runtimeState.LockedPresentation = new LockedBlindBoxPresentation
            {
                ScheduleId = schedule.Id,
                BlindBoxId = configuredBox.Id,
                Kind = LockedBlindBoxPresentationKind.ScheduledLocal,
            };
        }
        else if (GetFallbackRefreshmentBox(schedule) is { } fallback)
        {
            runtimeState.LockedPresentation = new LockedBlindBoxPresentation
            {
                ScheduleId = schedule.Id,
                BlindBoxId = fallback.Id,
                Kind = LockedBlindBoxPresentationKind.Fallback,
            };
            if (runtimeState.PendingPreparation != null)
                runtimeState.PendingPreparation.IsLate = true;
        }

        if (runtimeState.LockedPresentation == null)
            return changed;

        if (schedule.IsLoopTrack)
        {
            var interval = GetLoopIntervalScheduleSeconds();
            do
            {
                runtimeState.NextLoopPresentationSeconds += interval;
            } while (runtimeState.NextLoopPresentationSeconds <= runtimeState.ScheduleSeconds);
        }
        return true;
    }

    public BlindBox? GetNextAvailableBox(
        BlindBoxRuntimeState runtimeState,
        PendingBlindBoxReward? pendingReward)
    {
        if (pendingReward != null)
            return LubanData.Tables.TbBlindBox.GetOrDefault(pendingReward.BlindBoxId);

        return runtimeState.LockedPresentation == null
            ? null
            : LubanData.Tables.TbBlindBox.GetOrDefault(runtimeState.LockedPresentation.BlindBoxId);
    }

    private static BlindBox? GetFallbackRefreshmentBox(BlindBoxSchedule schedule)
    {
        if (schedule.FallbackBlindBoxId <= 0)
            return null;

        var fallback = LubanData.Tables.TbBlindBox.GetOrDefault(schedule.FallbackBlindBoxId);
        return fallback is
        {
            IsEnabled: true,
            BoxType: EBlindBoxType.Refreshment,
            IsPlatformInventoryRequired: false,
        }
            ? fallback
            : null;
    }

    public bool TryGetLoopSchedule(out BlindBoxSchedule? schedule, out BlindBox? box)
    {
        schedule = GetLoopSchedule();
        box = schedule == null
            ? null
            : LubanData.Tables.TbBlindBox.GetOrDefault(schedule.BlindBoxId);
        return schedule != null && box is { IsEnabled: true };
    }

    public bool TryGetCurrentSchedule(
        BlindBoxRuntimeState runtimeState,
        out BlindBoxSchedule? schedule)
    {
        schedule = GetCurrentSequenceSchedule(runtimeState);
        if (schedule != null)
            return true;

        schedule = GetLoopSchedule();
        return runtimeState.LoopStageStarted && schedule != null;
    }

    public IReadOnlyList<BlindBoxSchedule> GetGeneratorActivationOrder(
        BlindBoxRuntimeState runtimeState)
    {
        var sequenceSchedules = GetSequenceSchedules();
        var firstIndex = Math.Clamp(runtimeState.SequenceIndex, 0, sequenceSchedules.Count);
        if (firstIndex < sequenceSchedules.Count)
        {
            var current = sequenceSchedules[firstIndex];
            if (current.SteamPlaytimeGeneratorItemDefId > 0)
                return [current];

            var nextPlatformSchedule = sequenceSchedules
                .Skip(firstIndex + 1)
                .FirstOrDefault(schedule => schedule.SteamPlaytimeGeneratorItemDefId > 0);
            if (nextPlatformSchedule != null)
                return [nextPlatformSchedule];
        }

        return GetLoopSchedule() is { SteamPlaytimeGeneratorItemDefId: > 0 } loopSchedule
            ? [loopSchedule]
            : [];
    }

    public double GetSteamEligibilityRealSeconds(BlindBoxSchedule schedule) =>
        CalculateSteamEligibilityRealSeconds(schedule);

    public BlindBoxHintState CreateReadyHintState(
        BlindBoxSchedule schedule,
        BlindBox box,
        LockedBlindBoxPresentationKind kind)
    {
        var price = ResolvePrice(schedule, box, UsesSchedulePriceOverride(kind));
        return new BlindBoxHintState
        {
            Status = _gameData.Chips >= price.ActualCost
                ? BlindBoxHintStatus.Ready
                : BlindBoxHintStatus.NotEnoughChips,
            Box = box,
            Cost = price.ActualCost,
            DisplayValue = price.DisplayValue,
            ValueMode = price.ValueMode,
            StrikeThrough = price.StrikeThrough,
            PaymentSource = kind switch
            {
                LockedBlindBoxPresentationKind.PreparedSteam => BlindBoxPaymentSource.SteamPrepared,
                LockedBlindBoxPresentationKind.DeferredSequenceSteam => BlindBoxPaymentSource.SteamLate,
                LockedBlindBoxPresentationKind.LateSteam => BlindBoxPaymentSource.SteamLate,
                LockedBlindBoxPresentationKind.Fallback => BlindBoxPaymentSource.SteamFallback,
                _ when box.BoxType == EBlindBoxType.Refreshment => BlindBoxPaymentSource.LocalRefreshment,
                _ => BlindBoxPaymentSource.Chips,
            },
        };
    }

    public int GetDisplayCost(BlindBox box)
    {
        var config = LubanData.Tables.TbGameDevelopConfig.DataList.FirstOrDefault();
        var scale = config == null || config.BlindBoxCostScale <= 0 ? 1f : config.BlindBoxCostScale;
        return Mathf.Max(0, Mathf.RoundToInt(box.CostChips * scale));
    }

    public BlindBoxPrice ResolvePrice(
        BlindBoxSchedule schedule,
        BlindBox box,
        bool useScheduleOverride)
    {
        var scale = GetCostScale();
        if (useScheduleOverride && schedule.CostChipsOverride < 0)
        {
            return new BlindBoxPrice(
                0,
                Mathf.Max(0, Mathf.RoundToInt(Math.Abs(schedule.CostChipsOverride) * scale)),
                EBlindBoxValueMode.Chips,
                true);
        }

        var configuredCost = useScheduleOverride && schedule.CostChipsOverride > 0
            ? schedule.CostChipsOverride
            : box.CostChips;
        var cost = Mathf.Max(0, Mathf.RoundToInt(configuredCost * scale));
        return new BlindBoxPrice(cost, cost, box.HintValueMode, false);
    }

    private static float GetCostScale()
    {
        var config = LubanData.Tables.TbGameDevelopConfig.DataList.FirstOrDefault();
        return config == null || config.BlindBoxCostScale <= 0 ? 1f : config.BlindBoxCostScale;
    }

    public string BuildDebugStatus(
        double totalPlaySeconds,
        BlindBoxRuntimeState runtimeState,
        PendingBlindBoxReward? pendingReward)
    {
        var scheduleSeconds = runtimeState.ScheduleSeconds;
        var builder = new StringBuilder();
        builder.AppendLine($"游玩: {FormatSeconds(totalPlaySeconds)}");
        builder.AppendLine($"调度表时间: {FormatSeconds(scheduleSeconds)}");
        builder.AppendLine($"上次领取: {FormatSeconds(runtimeState.LastClaimSeconds)}");
        builder.AppendLine($"正式展示门槛: {FormatSeconds(Math.Max(0, runtimeState.NextLoopPresentationSeconds - scheduleSeconds))} 后");

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

        if (TryGetLockedPresentation(runtimeState, out var availableSchedule, out var availableBox, out var locked))
        {
            var price = ResolvePrice(availableSchedule!, availableBox!, UsesSchedulePriceOverride(locked!.Kind));
            builder.AppendLine(_gameData.Chips >= price.ActualCost ? "状态: 可领取" : "状态: 筹码不足");
            builder.AppendLine($"下个: {availableBox!.Name} ({availableBox.Id})");
            builder.AppendLine($"调度: {availableSchedule!.Id}, 类型={locked.Kind}");
            builder.AppendLine($"消耗: {price.ActualCost}, 筹码: {_gameData.Chips}");
        }
        else
        {
            builder.AppendLine("状态: 等待中");
        }

        var sequence = GetCurrentSequenceSchedule(runtimeState);
        if (sequence != null)
        {
            var box = LubanData.Tables.TbBlindBox.GetOrDefault(sequence.BlindBoxId);
            builder.AppendLine($"新手: #{runtimeState.SequenceIndex}, 调度={sequence.Id}, {box?.Name ?? "缺失"}");
            builder.AppendLine(
                $"新手等待: {FormatSeconds(GetSequencePresentationRemainingSeconds(runtimeState, sequence))}");
        }
        else
        {
            builder.AppendLine($"新手: 已完成 #{runtimeState.SequenceIndex}");
        }

        var loopSchedule = GetLoopSchedule();
        var lockedBox = runtimeState.LockedPresentation == null
            ? null
            : LubanData.Tables.TbBlindBox.GetOrDefault(runtimeState.LockedPresentation.BlindBoxId);
        builder.AppendLine($"正常阶段: {(runtimeState.LoopStageStarted ? "已启动" : "未启动")}");
        builder.AppendLine($"循环调度: {loopSchedule?.Id.ToString() ?? "缺失"}");
        builder.AppendLine($"锁定气球: {lockedBox?.Name ?? "无"} ({runtimeState.LockedPresentation?.Kind.ToString() ?? "None"})");
        builder.AppendLine($"下个展示点: {FormatSeconds(Math.Max(0, runtimeState.NextLoopPresentationSeconds - scheduleSeconds))} 后");
        builder.AppendLine($"下个Steam心跳: {FormatSeconds(Math.Max(0, runtimeState.NextLoopTriggerSeconds - scheduleSeconds))} 后");
        builder.AppendLine($"准备请求: {runtimeState.PendingPreparation?.Phase.ToString() ?? "无"}");
        builder.AppendLine($"待揭晓实例: {runtimeState.PreparedReward?.PlatformInstanceId.ToString() ?? "无"}");

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

        if (TryGetLockedPresentation(runtimeState, out var schedule, out var box, out var locked))
            return CreateReadyHintState(schedule!, box!, locked!.Kind);

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
        if (!TryGetLockedPresentation(runtimeState, out var schedule, out var box, out var locked)
            || schedule == null || box == null || locked == null)
            return null;

        var price = ResolvePrice(schedule, box, UsesSchedulePriceOverride(locked.Kind));
        if (_gameData.Chips < price.ActualCost)
        {
            GD.PushWarning($"[BlindBox] Not enough chips. Need {price.ActualCost}, current {_gameData.Chips}.");
            return null;
        }

        var item = RollReward(box);
        if (item == null)
        {
            GD.PushError($"[BlindBox] No reward candidate for box {box.Id} ({box.Name}).");
            return null;
        }

        _gameData.ModifyChips(-price.ActualCost);
        return CreateOpenResult(totalPlaySeconds, schedule, box, item, price.ActualCost);
    }

    public bool TryGetNextAvailable(
        BlindBoxRuntimeState runtimeState,
        out BlindBoxSchedule? schedule,
        out BlindBox? box)
    {
        return TryGetLockedPresentation(runtimeState, out schedule, out box, out _);
    }

    public bool TryGetNextPresentationCandidate(
        BlindBoxRuntimeState runtimeState,
        out BlindBoxSchedule? schedule,
        out BlindBox? box)
    {
        return TryGetLockedPresentation(runtimeState, out schedule, out box, out _);
    }

    public BlindBoxOpenResult? CreateOpenResult(
        double totalPlaySeconds,
        BlindBoxSchedule schedule,
        BlindBox box,
        Item item,
        int cost,
        ulong platformInstanceId = 0,
        bool completesSchedule = true)
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
            IsPlatformInventoryReward = platformInstanceId > 0,
            PlatformInstanceId = platformInstanceId,
            CompletesSchedule = completesSchedule,
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

    public bool TryGetLockedPresentation(
        BlindBoxRuntimeState runtimeState,
        out BlindBoxSchedule? schedule,
        out BlindBox? box,
        out LockedBlindBoxPresentation? presentation)
    {
        presentation = runtimeState.LockedPresentation;
        schedule = presentation == null
            ? null
            : LubanData.Tables.TbBlindBoxSchedule.GetOrDefault(presentation.ScheduleId);
        box = presentation == null
            ? null
            : LubanData.Tables.TbBlindBox.GetOrDefault(presentation.BlindBoxId);
        return presentation != null
               && schedule is { IsEnabled: true }
               && box is { IsEnabled: true };
    }

    public bool ConsumeOpenedPresentation(BlindBoxRuntimeState runtimeState)
    {
        var presentation = runtimeState.LockedPresentation;
        if (presentation == null)
            return false;

        var schedule = LubanData.Tables.TbBlindBoxSchedule.GetOrDefault(presentation.ScheduleId);
        var completesSchedule = presentation.Kind != LockedBlindBoxPresentationKind.LateSteam;
        if (presentation.Kind == LockedBlindBoxPresentationKind.Fallback
            && schedule is
            {
                IsLoopTrack: false,
                SteamPlaytimeGeneratorItemDefId: > 0,
            }
            && runtimeState.PendingPreparation is { } pending
            && pending.ScheduleId == schedule.Id)
        {
            // The Fallback now completes this newcomer step. Let an already submitted
            // request finish once so a real late reward is not lost, but never keep
            // retrying the skipped Generator and starving the next Schedule.
            pending.IsLate = true;
            pending.StopRetryAfterFallback = true;
        }
        runtimeState.LockedPresentation = null;
        if (!completesSchedule || schedule == null || schedule.IsLoopTrack)
            return completesSchedule;

        var sequenceSchedules = GetSequenceSchedules();
        if (runtimeState.SequenceIndex < sequenceSchedules.Count
            && sequenceSchedules[runtimeState.SequenceIndex].Id == schedule.Id)
            runtimeState.SequenceIndex++;
        return true;
    }

    public static bool UsesSchedulePriceOverride(LockedBlindBoxPresentationKind kind) =>
        kind is not LockedBlindBoxPresentationKind.LateSteam
            and not LockedBlindBoxPresentationKind.DeferredSequenceSteam;

    /// <summary>奖励真正进入玩家背包后，再从此刻开始计算下一段新手间隔。</summary>
    public void CompleteClaimedPresentation(
        BlindBoxRuntimeState runtimeState,
        int scheduleId,
        bool completedSchedule)
    {
        var schedule = LubanData.Tables.TbBlindBoxSchedule.GetOrDefault(scheduleId);
        if (schedule == null)
        {
            GD.PushError($"[BlindBox] Claimed reward references missing schedule {scheduleId}.");
            return;
        }

        var scheduleSeconds = runtimeState.ScheduleSeconds;
        runtimeState.LastClaimSeconds = scheduleSeconds;
        if (completedSchedule && !schedule.IsLoopTrack)
        {
            if (GetCurrentSequenceSchedule(runtimeState) == null)
                StartLoopStage(runtimeState);
            return;
        }
    }

    /// <summary>
    /// 将 Steam 永久回执证明的新手进度合并到本地。只允许向前推进；调用方需保证当前没有
    /// 已锁定气球、待揭晓奖励或正在播放的表演，避免远端进度替换玩家已经看见的内容。
    /// </summary>
    public bool MergeSequenceCompletionCount(
        BlindBoxRuntimeState runtimeState,
        int completedScheduleCount)
    {
        var sequenceSchedules = GetSequenceSchedules();
        var target = Math.Clamp(completedScheduleCount, 0, sequenceSchedules.Count);
        if (target <= runtimeState.SequenceIndex)
            return false;

        runtimeState.SequenceIndex = target;
        runtimeState.LastClaimSeconds = runtimeState.ScheduleSeconds;
        runtimeState.PendingPreparation = null;
        runtimeState.PreparedReward = null;
        runtimeState.LockedPresentation = null;
        if (target >= sequenceSchedules.Count)
        {
            StartLoopStage(runtimeState);
        }
        else
        {
            runtimeState.LoopStageStarted = false;
            runtimeState.NextLoopPresentationSeconds = 0.0;
            runtimeState.NextLoopTriggerSeconds = 0.0;
        }
        return true;
    }

    public double GetMinimumRealPlaySecondsForSequenceCompletion(int completedScheduleCount)
    {
        var sequenceSchedules = GetSequenceSchedules();
        var completed = Math.Clamp(completedScheduleCount, 0, sequenceSchedules.Count);
        if (completed == 0)
            return 0.0;

        return Math.Max(0.0, sequenceSchedules[completed - 1].StartSeconds);
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

    public static double GetSequencePresentationReadyAtSeconds(
        BlindBoxRuntimeState runtimeState,
        BlindBoxSchedule schedule)
    {
        // The first presentation uses the absolute onboarding start time. Once the player
        // has claimed a Fallback, that same Steam Schedule remains current, but its next
        // presentation must wait a full interval from the claim instead of becoming ready
        // again immediately.
        if (runtimeState.SequenceIndex == 0 && runtimeState.LastClaimSeconds <= 0.0)
            return Math.Max(schedule.StartSeconds, Math.Max(0, schedule.IntervalSeconds));

        return runtimeState.LastClaimSeconds + Math.Max(0, schedule.IntervalSeconds);
    }

    private static double GetSequencePresentationRemainingSeconds(
        BlindBoxRuntimeState runtimeState,
        BlindBoxSchedule schedule)
    {
        return Math.Max(
            0.0,
            GetSequencePresentationReadyAtSeconds(runtimeState, schedule)
            - runtimeState.ScheduleSeconds);
    }

    private static double GetNextReadyRemainingSeconds(BlindBoxRuntimeState runtimeState)
    {
        var scheduleSeconds = runtimeState.ScheduleSeconds;

        var sequence = GetCurrentSequenceSchedule(runtimeState);
        if (sequence != null)
            return GetSequencePresentationRemainingSeconds(runtimeState, sequence);

        if (runtimeState.LockedPresentation != null)
            return 0.0;

        return Math.Max(
            0.0,
            runtimeState.NextLoopPresentationSeconds - scheduleSeconds);
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
        runtimeState.LockedPresentation = null;
        runtimeState.NextLoopPresentationSeconds = now + GetLoopIntervalScheduleSeconds();
        runtimeState.NextLoopTriggerSeconds = now;
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
        var loopSchedule = GetLoopSchedule();
        return Math.Max(1.0, loopSchedule?.IntervalSeconds ?? 1.0);
    }

    private static double CalculateSteamEligibilityRealSeconds(BlindBoxSchedule schedule)
    {
        return Math.Max(
            60.0,
            Math.Ceiling(Math.Max(0, schedule.SteamDropIntervalSeconds) / 60.0) * 60.0);
    }

#if DEBUG
    public double GetSteamEligibilityRealSecondsForDebug(BlindBoxSchedule schedule) =>
        CalculateSteamEligibilityRealSeconds(schedule);
#endif

    private void ValidateDefinitions()
    {
#if DEBUG
        var config = LubanData.Tables.TbGameDevelopConfig.DataList.FirstOrDefault();
        if (config == null)
            GD.PushError("[BlindBox] Missing GameDevelopConfig row.");

        var enabledSchedules = LubanData.Tables.TbBlindBoxSchedule.DataList
            .Where(schedule => schedule.IsEnabled)
            .ToList();
        if (!enabledSchedules.Any(schedule => !schedule.IsLoopTrack))
            GD.PushError("[BlindBox] No enabled newbie schedules.");
        if (enabledSchedules.Count(schedule => schedule.IsLoopTrack) != 1)
            GD.PushError("[BlindBox] Exactly one enabled loop schedule is required.");
        foreach (var schedule in enabledSchedules)
        {
            var box = LubanData.Tables.TbBlindBox.GetOrDefault(schedule.BlindBoxId);
            if (box == null || !box.IsEnabled)
            {
                GD.PushError($"[BlindBox] Schedule {schedule.Id} references a missing or disabled box {schedule.BlindBoxId}.");
                continue;
            }

            if (box.IsPlatformInventoryRequired && schedule.SteamPlaytimeGeneratorItemDefId <= 0)
                GD.PushError($"[BlindBox] Platform box Schedule {schedule.Id} requires a PlaytimeGenerator.");
            if (!box.IsPlatformInventoryRequired && schedule.SteamPlaytimeGeneratorItemDefId > 0)
                GD.PushError($"[BlindBox] Local box Schedule {schedule.Id} cannot request a PlaytimeGenerator.");

            if (schedule.IntervalSeconds <= 0)
                GD.PushError($"[BlindBox] Schedule {schedule.Id} IntervalSeconds must be positive.");
            if (schedule.SteamPlaytimeGeneratorItemDefId > 0
                && schedule.SteamDropIntervalSeconds <= 0)
            {
                GD.PushError($"[BlindBox] Platform Schedule {schedule.Id} SteamDropIntervalSeconds must be positive.");
            }
            else if (schedule.SteamPlaytimeGeneratorItemDefId <= 0
                     && schedule.SteamDropIntervalSeconds != 0)
            {
                GD.PushError($"[BlindBox] Local Schedule {schedule.Id} SteamDropIntervalSeconds must be 0.");
            }
            if (schedule.IsLoopTrack)
            {
                if (schedule.StartSeconds != 0)
                    GD.PushError($"[BlindBox] Loop Schedule {schedule.Id} StartSeconds must be 0.");
            }

            if (box.IsPlatformInventoryRequired)
            {
                if (GetFallbackRefreshmentBox(schedule) == null)
                {
                    GD.PushError(
                        $"[BlindBox] Schedule {schedule.Id} references an invalid local Refreshment fallback "
                        + $"{schedule.FallbackBlindBoxId}.");
                }
            }
            else if (schedule.FallbackBlindBoxId != 0)
            {
                GD.PushError($"[BlindBox] Local box Schedule {schedule.Id} cannot configure a fallback.");
            }

            if (schedule.SteamCompletionReceiptItemDefId > 0)
            {
                var receipt = LubanData.Tables.TbSteamItemDef.GetOrDefault(
                    schedule.SteamCompletionReceiptItemDefId);
                if (schedule.IsLoopTrack)
                    GD.PushError($"[BlindBox] Loop Schedule {schedule.Id} cannot grant a completion receipt.");
                if (receipt is not
                    {
                        IsEnabled: true,
                        Type: ESteamItemDefType.Item,
                        PromoRule: "manual",
                        GrantedManually: true,
                        Tradable: false,
                        Marketable: false,
                        GameOnly: true,
                        StoreHidden: true,
                        AutoStack: false,
                    })
                {
                    GD.PushError(
                        $"[BlindBox] Schedule {schedule.Id} references an invalid completion receipt "
                        + $"{schedule.SteamCompletionReceiptItemDefId}.");
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

        foreach (var duplicate in enabledSchedules
                     .Where(schedule => schedule.SteamCompletionReceiptItemDefId > 0)
                     .GroupBy(schedule => schedule.SteamCompletionReceiptItemDefId)
                     .Where(group => group.Count() > 1))
        {
            GD.PushError(
                $"[BlindBox] Completion receipt {duplicate.Key} is shared by Schedules "
                + $"{string.Join(",", duplicate.Select(schedule => schedule.Id))}.");
        }

        ValidatePresentationScheduling();
        ValidateSteamPlaytimeScheduling();
#endif
    }

#if DEBUG
    private void ValidatePresentationScheduling()
    {
        var sequenceSchedules = GetSequenceSchedules();
        var loopSchedule = GetLoopSchedule();
        if (sequenceSchedules.Count == 0 || loopSchedule == null)
            return;

        var state = new BlindBoxRuntimeState
        {
            SequenceIndex = sequenceSchedules.Count,
            ScheduleSeconds = 100.0,
        };
        EnsureLoopStageInitialized(state);
        if (!state.LoopStageStarted
            || state.NextLoopPresentationSeconds <= state.ScheduleSeconds
            || state.NextLoopTriggerSeconds != state.ScheduleSeconds)
            GD.PushError("[BlindBox] Regression check failed: direct-reward loop initialization is incorrect.");

        if (sequenceSchedules.Count > 1)
        {
            var secondSchedule = sequenceSchedules[1];
            var sequentialState = new BlindBoxRuntimeState
            {
                SequenceIndex = 1,
                LastClaimSeconds = 100.0,
                ScheduleSeconds = 100.0 + secondSchedule.IntervalSeconds,
            };
            if (GetSequencePresentationRemainingSeconds(sequentialState, secondSchedule) > 0.001
                || sequentialState.ScheduleSeconds >= secondSchedule.StartSeconds)
            {
                GD.PushError(
                    "[BlindBox] Regression check setup is invalid or a later newcomer Schedule still depends on absolute StartSeconds.");
            }
        }
    }

    private void ValidateSteamPlaytimeScheduling()
    {
        var sequenceSchedules = GetSequenceSchedules();
        var firstSchedule = sequenceSchedules
            .FirstOrDefault(schedule => schedule.SteamPlaytimeGeneratorItemDefId > 0);
        if (firstSchedule == null)
            return;

        var before = new BlindBoxRuntimeState();
        if (!TryGetPreparationCandidate(before, out var schedule, out _)
            || schedule?.Id != firstSchedule.Id)
            GD.PushError("[BlindBox] Regression check failed: current direct reward candidate is unavailable.");

        var deferredRewardState = new BlindBoxRuntimeState
        {
            SequenceIndex = sequenceSchedules.Count,
            GeneratorActivation = new PlaytimeGeneratorActivationState
            {
                DeferredReward = new PreparedBlindBoxReward
                {
                    ScheduleId = GetLoopSchedule()?.Id ?? 0,
                    PlatformInstanceId = 1,
                },
            },
        };
        if (TryGetPreparationCandidate(deferredRewardState, out _, out _))
        {
            GD.PushError(
                "[BlindBox] Regression check failed: a deferred activation reward must block another preparation request.");
        }

        for (var index = 0; index < sequenceSchedules.Count; index++)
        {
            var current = sequenceSchedules[index];
            var expected = current.SteamPlaytimeGeneratorItemDefId > 0
                ? current
                : sequenceSchedules
                    .Skip(index + 1)
                    .FirstOrDefault(candidate => candidate.SteamPlaytimeGeneratorItemDefId > 0)
                  ?? GetLoopSchedule();
            var actual = GetGeneratorActivationOrder(new BlindBoxRuntimeState { SequenceIndex = index })
                .FirstOrDefault();
            if (actual?.Id != expected?.Id)
            {
                GD.PushError(
                    $"[BlindBox] Regression check failed: Schedule {current.Id} should prewarm "
                    + $"{expected?.Id.ToString() ?? "nothing"}, not {actual?.Id.ToString() ?? "nothing"}.");
            }
        }
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
