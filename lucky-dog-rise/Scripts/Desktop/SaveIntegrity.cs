#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace LuckyDogRise;

internal static class SaveIntegrity
{
    public const int CurrentVersion = 3;

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
        if (profile.IntegrityVersion is not (1 or 2 or CurrentVersion)
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
        var actual = hmac.ComputeHash(GetCanonicalBytes(profile, profile.IntegrityVersion));
        return expected.Length == actual.Length
            && CryptographicOperations.FixedTimeEquals(expected, actual);
    }

    private static byte[] GetCanonicalBytes(SaveProfile profile, int integrityVersion = CurrentVersion)
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
            BlindBoxClaimedCountsBySchedule = SortDictionary(profile.BlindBoxClaimedCountsBySchedule),
            BlindBoxRuntimeState = CanonicalizeRuntimeState(profile.BlindBoxRuntimeState, integrityVersion),
            PendingBlindBoxReward = CanonicalizePendingReward(profile.PendingBlindBoxReward),
            // v1 存档的签名没有这个字段；保持 null 并由 JsonIgnore 省略，兼容旧 HMAC。
            LuckyDealBuffState = integrityVersion >= 2
                ? CanonicalizeLuckyDealBuff(profile.LuckyDealBuffState)
                : null,
            CreatedAt = profile.CreatedAt ?? string.Empty,
            UpdatedAt = profile.UpdatedAt ?? string.Empty,
        };

        return Encoding.UTF8.GetBytes(JsonSerializer.Serialize(canonical, CanonicalJsonOptions));
    }

    private static LuckyDealBuffState CanonicalizeLuckyDealBuff(LuckyDealBuffState? state)
    {
        state ??= new LuckyDealBuffState();
        return new LuckyDealBuffState
        {
            RemainingHands = Math.Max(0, state.RemainingHands),
            TriggerChance = Math.Clamp(state.TriggerChance, 0f, 1f),
        };
    }

    private static Dictionary<int, int> SortDictionary(Dictionary<int, int>? source)
    {
        return (source ?? new Dictionary<int, int>())
            .OrderBy(pair => pair.Key)
            .ToDictionary(pair => pair.Key, pair => pair.Value);
    }

    private static BlindBoxRuntimeState CanonicalizeRuntimeState(BlindBoxRuntimeState? state, int integrityVersion)
    {
        state ??= new BlindBoxRuntimeState();
        return new BlindBoxRuntimeState
        {
            SequenceIndex = state.SequenceIndex,
            LastClaimSeconds = state.LastClaimSeconds,
            // v1/v2 存档没有本地展示门槛；默认值会由 JsonIgnore 省略，保持旧签名兼容。
            NextLoopPresentationSeconds = integrityVersion >= 3
                ? state.NextLoopPresentationSeconds
                : 0.0,
            LoopTrackStates = (state.LoopTrackStates ?? new Dictionary<int, BlindBoxScheduleState>())
                .OrderBy(pair => pair.Key)
                .ToDictionary(
                    pair => pair.Key,
                    pair => new BlindBoxScheduleState
                    {
                        PendingCount = pair.Value?.PendingCount ?? 0,
                        ProcessedGrantCount = pair.Value?.ProcessedGrantCount ?? 0,
                    }),
        };
    }

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
            TotalPlaySeconds = pending.TotalPlaySeconds,
            DebugText = pending.DebugText ?? string.Empty,
        };
    }
}
