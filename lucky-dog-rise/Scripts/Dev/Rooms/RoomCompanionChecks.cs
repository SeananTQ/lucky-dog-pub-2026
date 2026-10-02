#if DEBUG && !DEMO_BUILD && !RECORDING_BUILD
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace LuckyDogRise.Rooms;

// Pure room/model checks. No Steam session, Godot nodes, global input or saves.
internal static class RoomCompanionChecks
{
    public static string Run()
    {
        CheckCreationAndWire();
        CheckRetirements();
        CheckActivity();
        return "Companion plans: fixed appearances, bounded codec, monotonic retirements, input-led mood rises and quiet cooldowns passed.";
    }

    private static void CheckCreationAndWire()
    {
        int[] skins = { 1012, 1013, 1014 };
        int[] hats = { 0, 2201, 2202 };
        var plan = RoomCompanionPlan.Create(skins, hats, 1000, 12345);
        string wire = plan.ToWire();
        Check(RoomCompanionPlan.Create(skins, hats, 1000, 12345).ToWire() == wire,
            "same creation seed fixes the same appearance choices");
        Check(plan.Appearances.All(a => skins.Contains(a.SkinId) && hats.Contains(a.HeadwearId)),
            "creation samples only the supplied legal visual assets");
        skins[0] = hats[0] = 999999;
        Check(plan.ToWire() == wire, "caller arrays cannot change an existing room plan");
        var members = plan.MembersAt(1000);
        members[0] = members[0] with { SkinId = 999999 };
        Check(plan.ToWire() == wire && plan.MembersAt(1000)[0].SkinId != 999999,
            "returned member arrays cannot mutate the plan");
        Check(plan.Appearances is not RoomCompanionAppearance[], "appearance collection is read-only");
        Check(RoomCompanionPlan.TryRead(wire, out var copy) && copy.ToWire() == wire
            && plan.SameGeneration(copy), "wire round trip preserves the room generation");
        Check(RoomCompanionPlan.Create(new[] { 1012 }, Array.Empty<int>(), 0, 1)
            .MembersAt(0).All(m => m.HeadwearId == 0), "empty headwear pool is supported without a hat");
        Check(RoomCompanionPlan.Create(new[] { 1012 }, null, 0, int.MaxValue).Seed == int.MaxValue,
            "largest explicit seed remains valid");
        Check(RoomCompanionPlan.TryRead("1:1:0:0:1012:0:1012:0:1012:0", out _),
            "mock rooms may start at zero");

        var oldCulture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("ar-SA");
            Check(plan.ToWire() == wire && RoomCompanionPlan.TryRead(wire, out _),
                "wire numbers are culture-independent");
        }
        finally { CultureInfo.CurrentCulture = oldCulture; }

        foreach (var bad in new[]
        {
            null, "", new string('1', RoomCompanionPlan.MaxWireLength + 1),
            "2:1:0:0:1012:0:1012:0:1012:0", "1:0:0:0:1012:0:1012:0:1012:0",
            "1:-1:0:0:1012:0:1012:0:1012:0", "1:2147483648:0:0:1012:0:1012:0:1012:0",
            "1:1:-1:0:1012:0:1012:0:1012:0", "1:1:9223372036854775808:0:1012:0:1012:0:1012:0",
            "1:1:0:8:1012:0:1012:0:1012:0", "1:1:0:-1:1012:0:1012:0:1012:0",
            "1:1:0:0:0:0:1012:0:1012:0", "1:1:0:0:1012:-1:1012:0:1012:0",
            "1:1:0:0:1012:2147483648:1012:0:1012:0", "1:1:0:0:1012:0:1012:0",
            "1:1:0:0:1012:0:1012:0:1012:0:0", "1:+1:0:0:1012:0:1012:0:1012:0",
            "1:01:0:0:1012:0:1012:0:1012:0", "1:1:0:0:1012:0:1012:0:1012:0 ",
            "1:１:0:0:1012:0:1012:0:1012:0", "1:1:0:0:1012:0:1012:0:1012:\n0"
        })
            Check(!RoomCompanionPlan.TryRead(bad, out var rejected) && rejected == null,
                "malformed or noncanonical plans are rejected");
        Throws(() => RoomCompanionPlan.Create(Array.Empty<int>(), null, 0), "empty skin pool rejected");
        Throws(() => RoomCompanionPlan.Create(new[] { 0 }, null, 0), "invalid skin rejected");
        Throws(() => RoomCompanionPlan.Create(new[] { 1 }, new[] { -1 }, 0), "invalid hat rejected");
        Throws(() => RoomCompanionPlan.Create(new[] { 1 }, null, -1), "negative creation time rejected");
        Throws(() => RoomCompanionPlan.Create(new[] { 1 }, null, 0, 0), "zero seed rejected");
    }

    private static void CheckRetirements()
    {
        var original = RoomCompanionPlan.Create(new[] { 1012 }, new[] { 0 }, 100, 99);
        var current = original;
        for (int humans = 1; humans <= RoomRules.Capacity; humans++)
        {
            current = current.WithHumanCount(humans);
            var members = current.MembersAt(200);
            Check(members.Length == Math.Max(0, 4 - humans), "real members replace companions one by one");
            Check(members.All(m => m.IsCompanion && m.Id < 0 && m.Presence == original.Seed),
                "companions have room-local identities separate from platform members");
            Check(original.SameGeneration(current), "retirement never changes names, generation or appearance");
            if (humans <= 3)
                Check(members[0].Id == -humans, "retirement order is -1 then -2 then -3");
        }
        Check(current.WithHumanCount(1).MembersAt(201).Length == 0, "departures never respawn retired companions");
        Check(original.RetiredMask == 0, "retirement methods do not mutate older snapshots");
        var two = original.WithHumanCount(2);
        Check(two.WithHumanCount(1).RetiredMask == two.RetiredMask, "partly retired plan also never refills");
        Check(original.MergeRetirements(two).RetiredMask == 1, "late metadata cannot resurrect a retired companion");
        Check(two.MergeRetirements(original).RetiredMask == 1, "retirement merge is monotonic in either direction");
        string[] fields = original.ToWire().Split(':');
        fields[3] = "4";
        Check(RoomCompanionPlan.TryRead(string.Join(":", fields), out var third), "all valid retirement masks decode");
        Check(third.MergeRetirements(two).RetiredMask == 5, "independently observed retirements are combined");
        Check(original.WithHumanCount(0).MembersAt(200).Length == 0, "no companions survive the last real member");
        var differentSeed = RoomCompanionPlan.Create(new[] { 1012 }, new[] { 0 }, 100, 100);
        var differentStart = RoomCompanionPlan.Create(new[] { 1012 }, new[] { 0 }, 101, 99);
        var differentLook = RoomCompanionPlan.Create(new[] { 1013 }, new[] { 0 }, 100, 99);
        foreach (var nextRoom in new[] { differentSeed, differentStart, differentLook })
            Check(!original.SameGeneration(nextRoom) && nextRoom.MergeRetirements(current).RetiredMask == 0,
                "a different room generation cannot inherit old retirements");
        Check(!original.SameGeneration(null) && ReferenceEquals(original.MergeRetirements(null), original),
            "missing prior plan is harmless");
        Throws(() => original.WithHumanCount(-1), "negative real-member count rejected");
        Throws(() => original.WithHumanCount(RoomRules.Capacity + 1), "over-capacity real-member count rejected");
    }

    private static void CheckActivity()
    {
        var allowed = new HashSet<int> { 1001, 1003, 1005, 1006 };
        for (int seed = 1; seed <= 32; seed++)
        {
            var plan = RoomCompanionPlan.Create(new[] { 1012, 1013 }, new[] { 0, 2201 }, 1000, seed);
            Check(RoomCompanionPlan.TryRead(plan.ToWire(), out var other), "second viewer reads the shared plan");
            var previous = plan.MembersAt(1000);
            var pulses = new int[RoomCompanionPlan.Count];
            var lastRise = new long[RoomCompanionPlan.Count];
            var upward = new HashSet<(int, int)>();
            var downward = new HashSet<(int, int)>();
            var seen = new HashSet<int>();
            bool sawTongue = false;
            bool sawStable = false;
            for (long time = 1000; time <= 1240; time++)
            {
                var members = plan.MembersAt(time);
                Check(members.SequenceEqual(other.MembersAt(time)), "viewers derive identical activity at the same server time");
                for (int slot = 0; slot < members.Length; slot++)
                {
                    var member = members[slot];
                    var prior = previous[slot];
                    Check(allowed.Contains(member.Reaction) && member.ActivitySequence > 0,
                        "only safe base reactions and positive activity sequences are produced");
                    Check(member.ActivitySequence >= prior.ActivitySequence, "activity stages have monotonic sequences");
                    bool changed = member.Reaction != prior.Reaction || member.TongueActive != prior.TongueActive;
                    if (changed) Check(member.ActivitySequence > prior.ActivitySequence,
                        "reaction and tongue transitions both advance the sequence");
                    else
                    {
                        Check(member == prior, "unchanged stages do not cause per-second presentation snapshots");
                        sawStable = true;
                    }
                    int rank = ActivityRank(member.Reaction);
                    int priorRank = ActivityRank(prior.Reaction);
                    if (rank > priorRank)
                    {
                        Check(rank == priorRank + 1, "rising activity does not skip an intermediate mood");
                        Check(prior.TongueActive && member.TongueActive,
                            "input visibly starts before every mood rise and bridges the change");
                        if (lastRise[slot] > 0 && rank >= 2)
                            Check(time - lastRise[slot] >= (rank == 2 ? 5 : 9),
                                "focused and excited moods require a sustained build-up");
                        lastRise[slot] = time;
                        upward.Add((prior.Reaction, member.Reaction));
                    }
                    else if (rank < priorRank)
                    {
                        Check(rank == priorRank - 1, "cooling activity descends through each intermediate mood");
                        Check(!prior.TongueActive && !member.TongueActive,
                            "mood reductions follow quiet input rather than new tongue feedback");
                        downward.Add((prior.Reaction, member.Reaction));
                    }
                    Check(member.SkinId == prior.SkinId && member.HeadwearId == prior.HeadwearId
                        && member.Name == prior.Name && member.Presence == prior.Presence,
                        "activity never changes the fixed appearance or identity");
                    pulses[slot] = member.TongueActive ? pulses[slot] + 1 : 0;
                    Check(pulses[slot] <= 2, "a tongue pulse never outlives the three-second activity lease");
                    sawTongue |= member.TongueActive;
                    seen.Add(member.Reaction);
                }
                previous = members;
            }
            Check(sawTongue && sawStable && seen.SetEquals(allowed), "the shared script contains sparse activity and all base moods");
            Check(upward.SetEquals(new[] { (1003, 1001), (1001, 1005), (1005, 1006) })
                && downward.SetEquals(new[] { (1006, 1005), (1005, 1001), (1001, 1003) }),
                "every cycle has input-led progressive rises and quiet progressive cooldowns");
            for (int slot = 0; slot < RoomCompanionPlan.Count; slot++)
            {
                var transitions = new HashSet<(int, int)>();
                var before = plan.MembersAt(1000)[slot];
                for (long time = 1001; time <= 1000 + RoomCompanionPlan.MaxActivityCycleSeconds; time++)
                {
                    var after = plan.MembersAt(time)[slot];
                    if (before.Reaction != after.Reaction) transitions.Add((before.Reaction, after.Reaction));
                    before = after;
                }
                Check(transitions.Count == 6, "each companion completes all rise and cooldown transitions within the advertised cycle bound");
            }
            Check(plan.MembersAt(1100).SequenceEqual(other.MembersAt(1100)), "a later join derives the current stage without replay");
            Check(plan.MembersAt(long.MinValue).All(m => !m.TongueActive && m.Reaction == 1001 && m.ActivitySequence == 0),
                "a clock before creation stays idle without arithmetic underflow");
            Check(plan.MembersAt(1005).SequenceEqual(other.MembersAt(1005)), "a backwards clock read cannot mutate the shared plan");
        }
        var zero = RoomCompanionPlan.Create(new[] { 1012 }, null, 0, 1);
        Check(zero.MembersAt(long.MaxValue).All(m => m.ActivitySequence > 0), "extreme future time cannot overflow the sequence");
        var late = RoomCompanionPlan.Create(new[] { 1012 }, null, long.MaxValue, int.MaxValue);
        Check(late.MembersAt(long.MaxValue).Length == 3 && late.MembersAt(0).All(m => !m.TongueActive),
            "large absolute creation time remains safe");
    }

    private static int ActivityRank(int reaction) => reaction switch
    {
        1003 => 0,
        1001 => 1,
        1005 => 2,
        1006 => 3,
        _ => throw new InvalidOperationException("Unexpected companion reaction."),
    };

    private static void Throws(Action action, string message)
    {
        try { action(); }
        catch (ArgumentException) { return; }
        throw new InvalidOperationException("Room companion check failed: " + message);
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("Room companion check failed: " + message);
    }
}
#endif
