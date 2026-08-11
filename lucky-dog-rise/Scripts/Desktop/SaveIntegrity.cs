#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DataTables;

namespace LuckyDogRise;

internal static class SaveIntegrity
{
    public const int CurrentVersion = 15;

    private static readonly JsonSerializerOptions CanonicalJsonOptions = new()
    {
        WriteIndented = false,
    };

    public static string Sign(SaveProfile profile)
    {
        if (!BuildInfo.TryGetSaveHmacKey(out var key))
            throw new InvalidOperationException("Save HMAC key is unavailable.");

        using var hmac = new HMACSHA256(key);
        return Convert.ToHexString(hmac.ComputeHash(GetCanonicalBytes(profile)));
    }

    public static bool Verify(SaveProfile profile)
    {
        if (profile.IntegrityVersion != CurrentVersion
            || string.IsNullOrWhiteSpace(profile.IntegrityTag)
            || !BuildInfo.TryGetSaveHmacKey(out var key))
            return false;

        byte[] expected;
        try
        {
            expected = Convert.FromHexString(profile.IntegrityTag);
        }
        catch (FormatException)
        {
            return false;
        }

        using var hmac = new HMACSHA256(key);
        var actual = hmac.ComputeHash(GetCanonicalBytes(profile));
        return expected.Length == actual.Length
            && CryptographicOperations.FixedTimeEquals(expected, actual);
    }

    private static byte[] GetCanonicalBytes(SaveProfile profile)
    {
        var canonical = new SaveProfile
        {
            Version = profile.Version,
            IntegrityVersion = profile.IntegrityVersion,
            IntegrityTag = string.Empty,
            Chips = profile.Chips,
            TotalPlaySeconds = profile.TotalPlaySeconds,
            OwnedItemIds = (profile.OwnedItemIds ?? []).OrderBy(id => id).ToList(),
            OwnedItemCounts = SortDictionary(profile.OwnedItemCounts),
            EquippedItemIdsByType = (profile.EquippedItemIdsByType ?? new Dictionary<string, int>())
                .OrderBy(pair => pair.Key, StringComparer.Ordinal)
                .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal),
            NewItemIds = (profile.NewItemIds ?? []).OrderBy(id => id).ToList(),
            AppliedLinkTreeRewardIds = (profile.AppliedLinkTreeRewardIds ?? []).OrderBy(id => id).ToList(),
            LinkTreeRewardLedgerInitialized = profile.LinkTreeRewardLedgerInitialized ?? false,
            BlindBoxRuntimeState = CanonicalizeRuntimeState(profile.BlindBoxRuntimeState),
            PendingBlindBoxReward = CanonicalizePendingReward(profile.PendingBlindBoxReward),
            PendingLinkTreeClaim = CanonicalizePendingLinkTreeClaim(profile.PendingLinkTreeClaim),
            LuckyDealBuffState = CanonicalizeLuckyDealBuff(profile.LuckyDealBuffState, includeLuckyDealMode: true),
            RefreshmentRuntimeState = CanonicalizeRefreshmentRuntimeState(profile.RefreshmentRuntimeState),
            CreatedAt = profile.CreatedAt ?? string.Empty,
            UpdatedAt = profile.UpdatedAt ?? string.Empty,
        };

        return Encoding.UTF8.GetBytes(JsonSerializer.Serialize(canonical, CanonicalJsonOptions));
    }

    private static LuckyDealBuffState CanonicalizeLuckyDealBuff(
        LuckyDealBuffState? state,
        bool includeLuckyDealMode)
    {
        state ??= new LuckyDealBuffState();
        var luckyDealMode = Enum.IsDefined(state.LuckyDealMode) && state.LuckyDealMode != 0
            ? state.LuckyDealMode
            : ELuckyDealMode.GuidedDraw;
        return new LuckyDealBuffState
        {
            RemainingHands = Math.Max(0, state.RemainingHands),
            TriggerChance = Math.Clamp(state.TriggerChance, 0f, 1f),
            LuckyDealMode = includeLuckyDealMode ? luckyDealMode : 0,
        };
    }

    private static RefreshmentRuntimeState CanonicalizeRefreshmentRuntimeState(RefreshmentRuntimeState? state)
    {
        state ??= new RefreshmentRuntimeState();
        return new RefreshmentRuntimeState
        {
            CurrentItemId = Math.Max(0, state.CurrentItemId),
            Status = Enum.IsDefined(typeof(TableRefreshmentStatus), state.Status)
                ? state.Status
                : TableRefreshmentStatus.Empty,
            BuffSourceItemId = Math.Max(0, state.BuffSourceItemId),
            BuffTotalHands = Math.Max(0, state.BuffTotalHands),
        };
    }

    private static Dictionary<int, int> SortDictionary(Dictionary<int, int>? source)
    {
        return (source ?? new Dictionary<int, int>())
            .OrderBy(pair => pair.Key)
            .ToDictionary(pair => pair.Key, pair => pair.Value);
    }

    private static BlindBoxRuntimeState CanonicalizeRuntimeState(BlindBoxRuntimeState? state)
    {
        state ??= new BlindBoxRuntimeState();
        return new BlindBoxRuntimeState
        {
            SequenceIndex = state.SequenceIndex,
            ScheduleSeconds = state.ScheduleSeconds,
            LastClaimSeconds = state.LastClaimSeconds,
            NextLoopPresentationSeconds = state.NextLoopPresentationSeconds,
            NextLoopTriggerSeconds = state.NextLoopTriggerSeconds,
            LoopStageStarted = state.LoopStageStarted,
            PendingPreparation = CanonicalizePreparation(state.PendingPreparation),
            PreparedReward = CanonicalizePreparedReward(state.PreparedReward),
            LockedPresentation = CanonicalizeLockedPresentation(state.LockedPresentation),
        };
    }

    private static PendingBlindBoxPreparation? CanonicalizePreparation(PendingBlindBoxPreparation? pending) =>
        pending == null ? null : new PendingBlindBoxPreparation
        {
            ScheduleId = pending.ScheduleId,
            BlindBoxId = pending.BlindBoxId,
            GeneratorItemDefId = pending.GeneratorItemDefId,
            Phase = pending.Phase,
            IsLate = pending.IsLate,
            SubmittedAtTotalPlaySeconds = pending.SubmittedAtTotalPlaySeconds,
            RetryNotBeforeTotalPlaySeconds = pending.RetryNotBeforeTotalPlaySeconds,
            InventoryQuantitiesBeforeRequest = (pending.InventoryQuantitiesBeforeRequest ?? new Dictionary<ulong, uint>())
                .OrderBy(pair => pair.Key)
                .ToDictionary(pair => pair.Key, pair => pair.Value),
        };

    private static PreparedBlindBoxReward? CanonicalizePreparedReward(PreparedBlindBoxReward? reward) =>
        reward == null ? null : new PreparedBlindBoxReward
        {
            ScheduleId = reward.ScheduleId,
            BlindBoxId = reward.BlindBoxId,
            PlatformInstanceId = reward.PlatformInstanceId,
            SteamItemDefId = reward.SteamItemDefId,
            ItemId = reward.ItemId,
            IsLate = reward.IsLate,
        };

    private static LockedBlindBoxPresentation? CanonicalizeLockedPresentation(LockedBlindBoxPresentation? value) =>
        value == null ? null : new LockedBlindBoxPresentation
        {
            ScheduleId = value.ScheduleId,
            BlindBoxId = value.BlindBoxId,
            Kind = value.Kind,
            PreparedPlatformInstanceId = value.PreparedPlatformInstanceId,
        };

    private static PendingBlindBoxReward? CanonicalizePendingReward(PendingBlindBoxReward? pending)
    {
        if (pending == null)
            return null;

        return new PendingBlindBoxReward
        {
            BlindBoxId = pending.BlindBoxId,
            ScheduleId = pending.ScheduleId,
            ItemId = pending.ItemId,
            RevealPathId = pending.RevealPathId,
            RevealStep = pending.RevealStep,
            RewardShown = pending.RewardShown,
            IsPlatformInventoryReward = pending.IsPlatformInventoryReward,
            PlatformInstanceId = pending.PlatformInstanceId,
            CompletesSchedule = pending.CompletesSchedule,
            TotalPlaySeconds = pending.TotalPlaySeconds,
            DebugText = pending.DebugText ?? string.Empty,
        };
    }

    private static PendingLinkTreeClaim? CanonicalizePendingLinkTreeClaim(PendingLinkTreeClaim? pending)
    {
        if (pending == null)
            return null;

        return new PendingLinkTreeClaim
        {
            LinkTreeId = pending.LinkTreeId,
            SteamPromoItemDefId = pending.SteamPromoItemDefId,
            SteamClaimBundleItemDefId = pending.SteamClaimBundleItemDefId,
            SteamReceiptItemDefId = pending.SteamReceiptItemDefId,
        };
    }

}
