#if DEBUG && !RECORDING_BUILD
using System;
using System.Linq;

namespace LuckyDogRise.Rooms;

// Pure state-transition checks: no Godot, Steam account, disk save or inventory.
internal static class RoomGameChangeChecks
{
    public static string Run()
    {
        CheckMajorityAndManualCommit();
        CheckCompanionVotes();
        CheckProposalLifetimeAndFaults();
        CheckRosterChanges();
        CheckWorkChat();
        CheckChatAcrossDelayedModeChanges();
        CheckRoomClock();
        return "ROOM_GAME_CHANGE_PASS: all-member majority, manual commit, companion delay, timeout/cancel, stale proposals, roster changes, work chat and room age.";
    }

    private static (RoomSandbox Server, RoomClient[] Players) CreateRoom(int humans, bool companions = false)
    {
        var server = new RoomSandbox();
        if (companions)
            server.CompanionFactory = now => RoomCompanionPlan.Create([1012], [0], now, 1234);
        var players = Enumerable.Range(1, humans)
            .Select(id => server.AddClient(id, $"Player {id}", 1012)).ToArray();
        server.Create(players[0], "Vote test");
        foreach (var player in players.Skip(1)) server.Join(player, players[0].JoinedCode);
        server.Tick(0);
        return (server, players);
    }

    private static string Propose(RoomSandbox server, RoomClient host, string target = "work")
    {
        Check(host.ProposeGameChange(target) == "ok", "host can propose a different supported mode");
        server.Tick(0);
        return host.View.GameChange.Id;
    }

    private static void CheckMajorityAndManualCommit()
    {
        for (int total = 1; total <= RoomRules.Capacity; total++)
        {
            var (server, players) = CreateRoom(total);
            var host = players[0];
            string code = host.JoinedCode;
            string id = Propose(server, host);
            var change = host.View.GameChange;
            int required = total / 2 + 1;
            Check(change.ElectorateCount == total && change.AcceptedCount == 1
                && change.RequiredCount == required && change.Votes.All(v => v.CountsForMajority),
                "every real member including the host counts, with strictly more than half required");
            if (required > 1)
                Check(host.ConfirmGameChange(id) == "Rooms_GameChangeNoMajority",
                    "host cannot confirm before strictly greater than half accepts");
            foreach (var player in players.Skip(1).Take(required - 1))
                Check(player.RespondGameChange(id, true) == "ok", "eligible member accepts");
            if (players.Length > required)
                Check(players[required].RespondGameChange(id, false) == "ok", "opposition is retained in the same roster");
            server.Tick(0);
            Check(host.CanConfirmGameChange && host.View.GameChange.HasMajority
                && players.All(p => p.View.GameId == "social"), "a reached majority never automatically switches mode");
            if (total > 1)
                Check(players[1].ConfirmGameChange(id) == "Rooms_GameChangeNotOwner", "only the host commits");
            Check(host.ConfirmGameChange(id) == "ok", "host manually commits an accepted proposal");
            server.Tick(0);
            Check(players.All(p => p.JoinedCode == code && p.View.GameId == "work" && p.View.GameChange == null)
                && server.Search().Single().GameId == "work", "opposed and unanswered members follow the commit without leaving");
            Check(!host.CanConfirmGameChange, "commit consumes the proposal exactly once");
        }
    }

    private static void CheckCompanionVotes()
    {
        var (server, players) = CreateRoom(1, companions: true);
        var host = players[0];
        string id = Propose(server, host);
        var plan = host.View.GameChange;
        Check(host.RespondGameChange(id, true) == "ok"
            && host.RespondGameChange(id, false) == "Rooms_GameChangeInvalidProposal",
            "the proposing host retains its initial yes and uses cancellation to withdraw");
        Check(plan.Votes.Length == 4 && plan.RequiredCount == 3 && plan.AcceptedCount == 1
            && plan.Votes.Count(v => v.MemberId < 0 && v.State == RoomGameVoteState.Pending) == 3,
            "three companions are voting members and start pending");
        server.Tick(RoomRules.CompanionVoteDelay - 0.01);
        Check(host.View.GameChange.AcceptedCount == 1 && !host.CanConfirmGameChange,
            "companions do not reply before their one-second delay");
        server.Tick(0.01);
        Check(host.View.GameChange.AcceptedCount == 4 && host.CanConfirmGameChange
            && host.View.GameId == "social", "companions accept after one second without committing for the host");
        Check(host.CancelGameChange(id) == "ok", "host can cancel after reaching majority");
        server.Tick(0);
        Check(host.View.GameChange == null && host.View.GameId == "social", "cancellation keeps previous mode");
    }

    private static void CheckProposalLifetimeAndFaults()
    {
        var (server, players) = CreateRoom(3);
        var host = players[0];
        var guest = players[1];
        Check(guest.ProposeGameChange("work") == "Rooms_GameChangeNotOwner", "guest cannot initiate a mode switch");
        foreach (string invalid in new[] { "", "WORK", "poker", "social" })
            Check(host.ProposeGameChange(invalid) == "Rooms_GameChangeInvalidMode", "unsupported or unchanged mode rejected");
        server.Settings(host).NextFailure = MockRoomFailure.Unavailable;
        Check(host.ProposeGameChange("work") == "Rooms_GameChangeUpdateFailed" && host.View.GameChange == null,
            "failed proposal does not create a visible vote");
        string id = Propose(server, host);
        Check(host.GameChangeSecondsRemaining == 60 && host.ProposeGameChange("work") == "Rooms_GameChangeAlreadyPending",
            "one sixty-second proposal at a time");
        Check(guest.CancelGameChange(id) == "Rooms_GameChangeNotOwner", "guest cannot cancel another member's proposal");
        Check(guest.RespondGameChange("stale", true) == "Rooms_GameChangeInvalidProposal", "wrong proposal id cannot vote");
        server.Settings(guest).NextFailure = MockRoomFailure.Unavailable;
        Check(guest.RespondGameChange(id, true) == "Rooms_GameChangeUpdateFailed", "failed vote is reported");
        server.Tick(0);
        Check(host.View.GameChange.AcceptedCount == 1, "failed vote does not count");
        Check(guest.RespondGameChange(id, true) == "ok", "vote can be retried");
        server.Tick(0);
        long revision = host.View.Revision;
        Check(guest.RespondGameChange(id, true) == "ok", "same vote is idempotent");
        server.Tick(0);
        Check(host.View.Revision == revision, "idempotent vote does not broadcast");
        Check(guest.RespondGameChange(id, false) == "ok", "same member can revise an answer without gaining another vote");
        server.Tick(0);
        Check(host.View.GameChange.AcceptedCount == 1 && !host.CanConfirmGameChange, "a changed answer removes the previous acceptance");
        guest.RespondGameChange(id, true); server.Tick(0);
        server.Settings(host).NextFailure = MockRoomFailure.Unavailable;
        Check(host.ConfirmGameChange(id) == "Rooms_GameChangeUpdateFailed", "failed commit is reported");
        server.Tick(0);
        Check(host.View.GameId == "social" && host.View.GameChange.Id == id, "failed commit retains both prior mode and valid proposal");
        server.Settings(host).NextFailure = MockRoomFailure.Unavailable;
        Check(host.CancelGameChange(id) == "Rooms_GameChangeUpdateFailed", "failed cancel is reported");
        server.Tick(0);
        Check(host.View.GameChange.Id == id, "failed cancellation retains the proposal");
        Check(host.CancelGameChange(id) == "ok", "host cancellation succeeds on retry");
        server.Tick(0);
        string next = Propose(server, host);
        Check(next != id && guest.RespondGameChange(id, true) == "Rooms_GameChangeInvalidProposal"
            && host.ConfirmGameChange(id) == "Rooms_GameChangeInvalidProposal"
            && host.CancelGameChange(id) == "Rooms_GameChangeInvalidProposal", "old commands cannot affect the replacement proposal");
        server.Tick(59.99);
        Check(host.View.GameChange?.Id == next && host.GameChangeSecondsRemaining > 0,
            "proposal remains live before its deadline");
        server.Tick(0.01);
        Check(players.All(p => p.View.GameChange == null && p.View.GameId == "social")
            && host.GameChangeSecondsRemaining == 0, "exact deadline cancels all replicas without switching");
        Check(host.ConfirmGameChange(next) != "ok", "expired proposal cannot be committed later");
        using var forged = new RoomClient(server, host.Id, "forged", 1012);
        Check(server.ProposeGameChange(forged, "work") == "Rooms_GameChangeUnavailable",
            "same numerical id from an unrelated client cannot control this room");
    }

    private static void CheckRosterChanges()
    {
        foreach (string action in new[] { "join", "leave", "rejoin", "kick-human", "kick-companion", "owner-leaves" })
        {
            var (server, players) = CreateRoom(2, companions: true);
            var host = players[0];
            var guest = players[1];
            string code = host.JoinedCode;
            string id = Propose(server, host);
            if (action == "join") server.Join(server.AddClient(3, "newcomer", 1012), code);
            else if (action == "leave") guest.Leave();
            else if (action == "rejoin") { guest.Leave(); server.Join(guest, code); }
            else if (action == "owner-leaves") host.Leave();
            else
            {
                var target = action == "kick-human" ? host.View.Members.Single(m => m.Id == guest.Id)
                    : host.View.Members.First(m => m.IsCompanion);
                Check(host.Kick(target.Id, target.Presence) == "", "host can modify membership during a pending proposal");
            }
            server.Tick(0);
            var survivor = players.First(player => player.JoinedCode == code);
            Check(survivor.View.GameChange == null && survivor.View.GameId == "social",
                $"{action} invalidates the fixed electorate without changing mode");
            Check(survivor.RespondGameChange(id, true) != "ok", "commands for an invalidated roster reject");
        }

        var (stableServer, stablePlayers) = CreateRoom(2);
        var owner = stablePlayers[0];
        var member = stablePlayers[1];
        string stable = Propose(stableServer, owner);
        member.SetAppearance(1013, 0, 1005);
        member.NotifyInputActivity();
        owner.SetName("Updated title");
        owner.SetAccess(RoomAccess.FriendsOnly);
        stableServer.Tick(0.1);
        Check(owner.View.GameChange?.Id == stable, "appearance, activity, title and permission changes preserve the electorate");
        Check(!ReferenceEquals(owner.View.GameChange.Votes, member.View.GameChange.Votes), "vote replicas have independent storage");
        member.View.GameChange.Votes[0] = member.View.GameChange.Votes[0] with { State = RoomGameVoteState.Declined };
        Check(owner.View.GameChange.AcceptedCount == 1, "changing one replica cannot change the host's displayed votes");
        Check(owner.ConfirmGameChange(stable) == "Rooms_GameChangeNoMajority", "replica mutation never changes authoritative votes");
    }

    private static void CheckWorkChat()
    {
        var (server, players) = CreateRoom(2);
        var host = players[0];
        var guest = players[1];
        Check(host.ChatAllowed && host.SendChat("before work") == "", "chat is available before the switch");
        server.Tick(0);
        Check(host.Bubbles.Count == 1 && guest.Bubbles.Count == 1, "social bubbles are visible before commitment");
        string id = Propose(server, host);
        guest.RespondGameChange(id, true); server.Tick(0);
        Check(host.ChatAllowed, "pending work proposal does not disable the current chatting mode");
        Check(host.ConfirmGameChange(id) == "ok", "work mode committed");
        server.Tick(0);
        Check(players.All(p => !p.ChatAllowed && p.Bubbles.Count == 0), "committing work clears old visible bubbles on all clients");
        Check(host.SendChat("client rejects") == "Rooms_ChatDisabled"
            && server.SendChat(guest, "service rejects") == "Rooms_ChatDisabled", "work blocks chat at client and service boundaries");
        var member = host.View.Members.Single(m => m.Id == guest.Id);
        host.Receive(new RoomChat(90000, host.JoinedCode, member.Id, member.Presence, "in-flight", server.Now + 6), server.Now);
        Check(host.Bubbles.Count == 0, "work rejects inbound in-flight chat");
        id = Propose(server, host, "social");
        guest.RespondGameChange(id, true); server.Tick(0);
        host.ConfirmGameChange(id); server.Tick(0);
        server.Tick(RoomRules.ChatCooldown);
        Check(players.All(p => p.ChatAllowed) && host.SendChat("back to chat") == "", "approved return to social restores chatting");
        server.Tick(0);
        Check(guest.Bubbles[host.Id].Text == "back to chat", "social chat works after returning from work");
    }

    private static void CheckRoomClock()
    {
        var (server, players) = CreateRoom(1);
        var host = players[0];
        Check(host.View.CreatedAt == 0 && host.View.ClockNow == 0 && host.RoomAgeSeconds == 0,
            "a room created at mock time zero has a valid creation timestamp");
        long revision = host.View.Revision;
        server.Tick(17.5);
        Check(Math.Abs(host.RoomAgeSeconds - 17.5) < 0.001 && host.View.Revision == revision,
            "room age advances locally without a timer-only snapshot broadcast");
        var newcomer = server.AddClient(2, "late join", 1012);
        server.Join(newcomer, host.JoinedCode); server.Tick(0);
        Check(newcomer.View.CreatedAt == 0 && newcomer.RoomAgeSeconds >= 17,
            "late joiner learns the room age rather than starting a personal room timer");
        host.Leave(); server.Tick(0);
        Check(newcomer.View.OwnerId == newcomer.Id && newcomer.View.CreatedAt == 0 && newcomer.RoomAgeSeconds >= 17,
            "host succession retains the original creation timestamp");
        server.Create(host, "new clock"); server.Tick(0);
        Check(host.View.CreatedAt == 17 && host.RoomAgeSeconds == 0, "a new room has a separate creation timestamp");
    }

    private static void CheckChatAcrossDelayedModeChanges()
    {
        var (server, players) = CreateRoom(2);
        var host = players[0];
        var delayed = players[1];
        server.Settings(delayed).Latency = 4;
        Check(host.SendChat("queued before work") == "", "a social message can be pending for a delayed recipient");
        server.Tick(0);
        string work = Propose(server, host);
        // Use the authoritative id while this deliberately delayed replica still
        // displays its previous snapshot, as a received vote notification would.
        delayed.RespondGameChange(work, true); server.Tick(0);
        host.ConfirmGameChange(work); server.Tick(0);
        Check(delayed.View.GameId == "social" && delayed.ChatAllowed
            && delayed.SendChat("stale local permission") == "Rooms_ChatDisabled",
            "the service denies work chat even while a delayed client still displays social mode");
        string social = Propose(server, host, "social");
        delayed.RespondGameChange(social, true); server.Tick(0);
        host.ConfirmGameChange(social); server.Tick(0);
        server.Tick(4);
        Check(delayed.View.GameId == "social" && delayed.Bubbles.Count == 0,
            "coalesced work-to-social snapshots cannot resurrect a queued pre-work message");
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("Room game-change regression: " + message);
    }
}
#endif
