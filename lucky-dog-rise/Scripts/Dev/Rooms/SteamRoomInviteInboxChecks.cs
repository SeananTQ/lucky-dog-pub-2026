#if DEBUG && !DEMO_BUILD && !RECORDING_BUILD
using System;

namespace LuckyDogRise.Rooms;

// Managed-only checks: no Godot scene, Steam initialization or player data needed.
public static class SteamRoomInviteInboxChecks
{
    public static void Run()
    {
        const ulong first = 109775243348102719;
        const ulong second = first + 1;
        var inbox = SteamRoomInviteInbox.FromCommandLine(
            ["--path", "game", "+connect_lobby", first.ToString(System.Globalization.CultureInfo.InvariantCulture)]);
        Assert(inbox.HasPending, "cold-launch intent is discoverable before room UI exists");
        Assert(inbox.TryTake(out var taken) && taken == first, "cold launch captures Steam lobby argument");
        Assert(!inbox.HasPending && !inbox.TryTake(out taken) && taken == 0,
            "consumed startup invitation never repeats");

        Assert(inbox.Queue(first) && inbox.Queue(first) && inbox.Queue(second), "valid lobby IDs are accepted");
        Assert(inbox.TryTake(out taken) && taken == second && !inbox.TryTake(out _),
            "duplicate and competing invitations coalesce to one latest choice");
        Assert(inbox.Queue(first), "a later explicit acceptance may target the same lobby again");
        inbox.Clear();
        Assert(!inbox.TryTake(out _), "explicit clear discards pending invitations");

        foreach (var invalid in new ulong[] { 0, 1, 76561198000000000, ulong.MaxValue })
            Assert(!inbox.Queue(invalid), "non-lobby Steam IDs are rejected before joining");
        Assert(!inbox.TryTake(out _), "invalid callback IDs never become pending invitations");
        foreach (var invalid in new[] { "", "0", "-1", "+109775243348102719", "18446744073709551616",
                     "76561198000000000", "LD-031G0028EAKHZ", " 109775243348102719", "garbage" })
        {
            var bad = SteamRoomInviteInbox.FromCommandLine(["+connect_lobby", invalid]);
            Assert(!bad.TryTake(out _), "malformed cold-launch values are ignored");
        }

        Assert(!SteamRoomInviteInbox.FromCommandLine(null).TryTake(out _), "null argument sequence is empty");
        Assert(!SteamRoomInviteInbox.FromCommandLine(["+connect_lobby"]).TryTake(out _),
            "missing lobby value is ignored");
        Assert(!SteamRoomInviteInbox.FromCommandLine(["109775243348102719", "--connect_lobby", "109775243348102719"])
            .TryTake(out _), "unrelated numeric arguments and similar switches cannot trigger a join");
        inbox = SteamRoomInviteInbox.FromCommandLine(["+connect_lobby", "109775243348102719",
            "+connect_lobby", "109775243348102720", "+connect_lobby", "invalid"]);
        Assert(inbox.TryTake(out taken) && taken == second, "last valid startup invitation wins");

        inbox.Queue(first);
        Assert(!inbox.Queue(1) && inbox.TryTake(out taken) && taken == first,
            "an invalid callback cannot erase an already accepted valid invitation");
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("Room invitation inbox: " + message);
    }
}
#endif
