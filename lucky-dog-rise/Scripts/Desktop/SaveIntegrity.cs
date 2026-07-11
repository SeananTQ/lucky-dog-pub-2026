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
    public const int CurrentVersion = 1;

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
            BlindBoxClaimedCountsBySchedule = SortDictionary(profile.BlindBoxClaimedCountsBySchedule),
            BlindBoxRuntimeState = CanonicalizeRuntimeState(profile.BlindBoxRuntimeState),
            PendingBlindBoxReward = CanonicalizePendingReward(profile.PendingBlindBoxReward),
            CreatedAt = profile.CreatedAt ?? string.Empty,
            UpdatedAt = profile.UpdatedAt ?? string.Empty,
        };

        return Encoding.UTF8.GetBytes(JsonSerializer.Serialize(canonical, CanonicalJsonOptions));
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
            LastClaimSeconds = state.LastClaimSeconds,
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
