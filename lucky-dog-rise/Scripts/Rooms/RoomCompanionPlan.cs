#if !DEMO_BUILD && !RECORDING_BUILD
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;

namespace LuckyDogRise.Rooms;

public sealed record RoomCompanionAppearance(int SkinId, int HeadwearId);

// A room-owned cosmetic plan, not fake platform accounts or persistent inventory.
// Only the fixed plan and retirements cross the network. Everyone derives the same
// sparse activity stages from the shared server clock, including after host migration.
public sealed class RoomCompanionPlan
{
    public const int Count = 3;
    public const int MaxWireLength = 128;
    public const int MaxActivityCycleSeconds = 75;
    private static readonly string[] Names = { "熬夜的程序员", "没洗头的美术", "打喷嚏的策划" };
    private readonly ReadOnlyCollection<RoomCompanionAppearance> _appearances;
    public int Seed { get; }
    public long StartedAt { get; }
    public int RetiredMask { get; }
    public int RemainingCount => Remaining(RetiredMask);
    public IReadOnlyList<RoomCompanionAppearance> Appearances => _appearances;

    private RoomCompanionPlan(int seed, long startedAt, int retiredMask,
        IEnumerable<RoomCompanionAppearance> appearances)
    {
        Seed = seed;
        StartedAt = startedAt;
        RetiredMask = retiredMask;
        _appearances = Array.AsReadOnly(appearances.ToArray());
    }

    public static RoomCompanionPlan Create(int[] skins, int[] hats, long startedAt, int? seed = null)
    {
        if (startedAt < 0) throw new ArgumentOutOfRangeException(nameof(startedAt));
        if (seed.HasValue && seed.Value <= 0) throw new ArgumentOutOfRangeException(nameof(seed));
        if (skins == null || skins.Length == 0 || skins.Any(id => id <= 0))
            throw new ArgumentException("Companions need valid positive skin ids.", nameof(skins));
        if (hats != null && hats.Any(id => id < 0))
            throw new ArgumentException("Headwear ids must be zero or positive.", nameof(hats));
        int fixedSeed = seed ?? Random.Shared.Next(1, int.MaxValue);
        var random = new Random(fixedSeed);
        var candidates = hats is { Length: > 0 } ? hats : new[] { 0 };
        var appearances = new RoomCompanionAppearance[Count];
        for (int slot = 0; slot < Count; slot++)
            appearances[slot] = new RoomCompanionAppearance(skins[random.Next(skins.Length)],
                candidates[random.Next(candidates.Length)]);
        return new RoomCompanionPlan(fixedSeed, startedAt, 0, appearances);
    }

    public RoomCompanionPlan WithHumanCount(int humanCount)
    {
        if (humanCount is < 0 or > RoomRules.Capacity)
            throw new ArgumentOutOfRangeException(nameof(humanCount));
        // No human means the room has ended. A temporary decrease never brings
        // retired companions back, and real players always have capacity priority.
        int remaining = humanCount == 0 ? 0 : Math.Max(0, 4 - humanCount);
        int mask = RetiredMask;
        for (int slot = 0; slot < Count && Remaining(mask) > remaining; slot++)
            mask |= 1 << slot;
        return mask == RetiredMask ? this : new RoomCompanionPlan(Seed, StartedAt, mask, _appearances);
    }

    private static int Remaining(int mask)
    {
        int count = 0;
        for (int slot = 0; slot < Count; slot++)
            if ((mask & (1 << slot)) == 0) count++;
        return count;
    }

    public bool TryRetire(int memberId, long presence, out RoomCompanionPlan retired)
    {
        retired = this;
        if (presence != Seed || memberId >= 0 || memberId < -Count) return false;
        int bit = 1 << (-memberId - 1);
        if ((RetiredMask & bit) != 0) return false;
        retired = new RoomCompanionPlan(Seed, StartedAt, RetiredMask | bit, _appearances);
        return true;
    }

    public bool SameGeneration(RoomCompanionPlan other) => other != null
        && Seed == other.Seed && StartedAt == other.StartedAt
        && _appearances.SequenceEqual(other._appearances);

    public RoomCompanionPlan MergeRetirements(RoomCompanionPlan previous)
    {
        if (!SameGeneration(previous)) return this;
        int mask = RetiredMask | previous.RetiredMask;
        return mask == RetiredMask ? this : new RoomCompanionPlan(Seed, StartedAt, mask, _appearances);
    }

    public string ToWire() => string.Create(CultureInfo.InvariantCulture,
        $"1:{Seed}:{StartedAt}:{RetiredMask}:{_appearances[0].SkinId}:{_appearances[0].HeadwearId}:{_appearances[1].SkinId}:{_appearances[1].HeadwearId}:{_appearances[2].SkinId}:{_appearances[2].HeadwearId}");

    public static bool TryRead(string value, out RoomCompanionPlan plan)
    {
        plan = null;
        if (string.IsNullOrEmpty(value) || value.Length > MaxWireLength) return false;
        var fields = value.Split(':');
        if (fields.Length != 10 || fields[0] != "1"
            || !TryNumber(fields[1], out long seed) || seed is <= 0 or > int.MaxValue
            || !TryNumber(fields[2], out long startedAt)
            || !TryNumber(fields[3], out long mask) || mask > 7) return false;
        var appearances = new RoomCompanionAppearance[Count];
        for (int slot = 0; slot < Count; slot++)
        {
            if (!TryNumber(fields[4 + slot * 2], out long skin) || skin is <= 0 or > int.MaxValue
                || !TryNumber(fields[5 + slot * 2], out long hat) || hat > int.MaxValue) return false;
            appearances[slot] = new RoomCompanionAppearance((int)skin, (int)hat);
        }
        plan = new RoomCompanionPlan((int)seed, startedAt, (int)mask, appearances);
        return true;
    }

    private static bool TryNumber(string value, out long result)
    {
        result = 0;
        if (value.Length == 0 || value.Length > 19 || value.Length > 1 && value[0] == '0') return false;
        foreach (char character in value)
            if (character is < '0' or > '9') return false;
        return long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out result);
    }

    public RoomMember[] MembersAt(long serverTime)
    {
        var members = new List<RoomMember>(Count);
        bool beforeStart = serverTime < StartedAt;
        // Both operands are nonnegative on the subtraction path, so even a
        // malformed/rolled-back clock cannot overflow elapsed or ActivitySequence.
        long elapsed = beforeStart ? 0 : serverTime - StartedAt;
        for (int slot = 0; slot < Count; slot++)
        {
            if ((RetiredMask & (1 << slot)) != 0) continue;
            var appearance = _appearances[slot];
            var activity = beforeStart ? (1001, false, 0L) : ActivityAt(slot, elapsed);
            members.Add(new RoomMember(-slot - 1, Names[slot], appearance.SkinId,
                appearance.HeadwearId, activity.Item1, Seed, activity.Item3, activity.Item2, true));
        }
        return members.ToArray();
    }

    private readonly record struct ActivityStage(int Seconds, int Reaction, bool Tongue);

    private (int Reaction, bool Tongue, long Sequence) ActivityAt(int slot, long elapsed)
    {
        uint mixed = Mix(unchecked((uint)Seed + (uint)(slot + 1) * 0x9E3779B9u));
        // Like the real desktop activity flow, input feedback comes first and a
        // sustained burst then raises the mood. These are cosmetic input bouts,
        // never global keystrokes, counters or rewards. The return to idle follows
        // stopped input: excited -> focused -> casual -> bored, without tongue taps.
        Span<ActivityStage> stages = stackalloc ActivityStage[]
        {
            new(10 + (int)(mixed % 7), 1003, false),
            new(1, 1003, true),
            new(1, 1001, true),
            new(3 + (int)((mixed >> 16) % 4), 1001, false),
            new(1, 1001, true),
            new(2, 1001, false),
            new(2, 1001, true),
            new(1, 1001, false),
            new(1, 1001, true),
            new(1, 1005, true),
            new(1, 1005, false),
            new(2, 1005, true),
            new(1, 1005, false),
            new(2, 1005, true),
            new(1, 1005, false),
            new(2, 1005, true),
            new(1, 1005, false),
            new(1, 1005, true),
            new(1, 1006, true),
            new(1, 1006, false),
            new(2, 1006, true),
            new(1, 1006, false),
            new(2, 1006, true),
            new(1, 1006, false),
            new(2, 1006, true),
            new(5 + (int)((mixed >> 24) % 3), 1006, false),
            new(5, 1005, false),
            new(10, 1001, false),
        };
        int period = 0;
        foreach (var stage in stages) period += stage.Seconds;
        int offset = (int)((mixed >> 8) % (uint)period);
        int phase = (int)((elapsed % period + offset) % period);
        int stageStart = 0;
        foreach (var stage in stages)
        {
            if (phase < stageStart + stage.Seconds)
            {
                long began = Math.Max(0, elapsed - (phase - stageStart));
                // Stable inside a stage: no per-second UI rebuild or network heartbeat.
                // Even across a mood rise, adjacent active stages total <= 2 seconds,
                // so the unchanged activity lease always covers a complete input bout.
                return (stage.Reaction, stage.Tongue,
                    began == long.MaxValue ? long.MaxValue : began + 1);
            }
            stageStart += stage.Seconds;
        }
        throw new InvalidOperationException("Companion activity phase is outside its cycle.");
    }

    private static uint Mix(uint value)
    {
        unchecked
        {
            value ^= value >> 16;
            value *= 0x7FEB352Du;
            value ^= value >> 15;
            value *= 0x846CA68Bu;
            return value ^ (value >> 16);
        }
    }
}
#endif
