#if DEBUG && !RECORDING_BUILD
using System;
using System.Linq;

namespace LuckyDogRise.Rooms;

// Exercises state transitions and malformed/stale messages without Godot UI or Steam.
internal static class RoomSandboxChecks
{
    public static string Run()
    {
        CheckActivity();
        CheckKicking();
        var server = new RoomSandbox();
        var a = server.AddClient(1, "A", 1012);
        var b = server.AddClient(2, "B", 1012);
        var c = server.AddClient(3, "C", 1012);
        Check(server.Create(a, "Room") == "", "create");
        var code = a.JoinedCode;
        server.Join(b, code);
        server.Join(c, code);
        Check(a.View == null && b.View == null, "replicas wait for messages");
        server.Tick(0);
        Check(a.View.Members.Length == 3 && c.View.Members.Length == 3, "three replicas");
        Check(!ReferenceEquals(a.View.Members, b.View.Members), "independent replica storage");
        a.LocalScale = 2;
        a.HiddenMembers.Add(2);
        Check(b.LocalScale == 1 && b.HiddenMembers.Count == 0, "local preferences isolated");

        server.Settings(b).Latency = 1;
        a.SetAppearance(1013, 0, 1006);
        server.Tick(0);
        Check(a.View.Members[0].Reaction == 1006 && b.View.Members[0].Reaction == 1001, "actual delayed replica");
        // Rapid coalescing cannot starve a delayed recipient indefinitely.
        for (var i = 0; i < 12; i++) { a.SetAppearance(1013, 0, 1005); server.Tick(0.1); }
        Check(b.View.Members[0].Reaction == 1005, "continuous updates still arrive");
        server.SetPaused(c, true);
        a.SetAppearance(1012, 0, 1007);
        server.Tick(2);
        Check(c.View.Members[0].Reaction == 1005, "paused receive retains old replica");
        server.SetPaused(c, false);
        server.Tick(0);
        Check(c.View.Members[0].Reaction == 1007, "resume resynchronizes");

        Check(server.SetGame(b, "test") != "", "non-owner metadata rejected");
        Check(server.SetGame(a, "test") == "", "owner metadata updated");
        Check(server.Search().Single().GameId == "test", "directory reflects metadata");
        Check(server.SendChat(a, "hello") == "", "chat accepted");
        Check(server.SendChat(a, "flood") != "", "chat rate limited");
        Check(server.SendChat(c, new string('x', 121)) != "", "oversized text rejected");
        Check(server.SendChat(c, "\ncontrol") != "", "control characters rejected");
        server.Tick(0);
        Check(a.Bubbles.Count == 1 && c.Bubbles.Count == 1, "bubble delivered");
        var senderPresence = a.View.Members.Single(m => m.Id == a.Id).Presence;
        c.Receive(new RoomChat(100, code, a.Id, senderPresence, "inbound flood", server.Now + 6), server.Now);
        Check(c.Bubbles[a.Id].Text == "hello", "receiver independently rate limits");
        server.Tick(7);
        Check(a.Bubbles.Count == 0 && b.Bubbles.Count == 0, "bubble expires across clients");
        server.Settings(b).Latency = 8;
        server.SendChat(a, "expires in transit");
        server.Tick(8);
        Check(b.Bubbles.Count == 0, "expired message never displayed");

        var oldSession = b.Session;
        a.SetAppearance(1012, 0, 1004);
        server.Leave(b);
        server.Join(b, code);
        Check(b.Session > oldSession, "new membership epoch");
        server.Tick(8);
        var formerPresence = b.View.Members.Single(m => m.Id == 1).Presence;
        server.Leave(a);
        server.Join(a, code);
        server.Tick(8);
        b.Receive(new RoomChat(999, code, 1, formerPresence, "stale", server.Now + 6), server.Now);
        Check(b.Bubbles.Count == 0, "old member incarnation cannot inject chat");
        Check(b.View.OwnerId == 2, "host departure selects surviving owner");
        Check(server.SetGame(a, "forbidden") != "", "old owner lost authority");

        for (var id = 4; id <= 6; id++) server.Join(server.AddClient(id, $"P{id}", 1012), code);
        var extra = server.AddClient(7, "seventh", 1012);
        Check(server.Join(extra, code) != "" && extra.JoinedCode == "", "six-player capacity enforced");
        Check(server.Join(extra, "invalid") != "", "invalid room handled");
        server.Settings(b).Latency = 10;
        for (var i = 0; i < 1000; i++) c.SetAppearance(1012, 0, i % 2 == 0 ? 1001 : 1005);
        Check(server.PendingCount <= 6, "snapshot queue bounded");
        server.Create(b, "Other");
        server.Tick(10);
        Check(b.View.Name == "Other" && b.View.Members.Length == 1, "room switch drops old deliveries");
        var other = b.JoinedCode;
        server.Leave(b);
        Check(!server.Search().Any(r => r.Code == other), "last departure destroys room");
        return "ROOM_SANDBOX_PASS: isolated replicas, latency, bounded queues, reconnect, capacity, owner transfer, metadata, chat validation/expiry, local settings, host removal and room-scoped bans.\n"
            + RoomRequestChecks.Run();
    }
    private static void Check(bool passed, string message)
    {
        if (!passed) throw new InvalidOperationException("Room sandbox regression: " + message);
    }

    private static void CheckActivity()
    {
        var server = new RoomSandbox();
        using var a = server.AddClient(1, "A", 1012);
        using var b = server.AddClient(2, "B", 1012);
        using var c = server.AddClient(3, "C", 1012);
        server.Create(a, "Activity");
        server.Join(b, a.JoinedCode);
        server.Join(c, a.JoinedCode);
        server.Tick(0);
        foreach (var reaction in new[] { 1005, 1006, 1003 })
        {
            b.SetReaction(reaction);
            server.Tick(0);
            Check(a.View.Members.Single(m => m.Id == b.Id).Reaction == reaction
                && c.View.Members.Single(m => m.Id == b.Id).Reaction == reaction, "all observers see activity expressions");
        }
        b.NotifyInputActivity();
        server.Tick(0);
        Check(a.IsTongueActive(2) && !a.IsTongueActive(1) && !a.IsTongueActive(3), "activity belongs only to sender");
        for (int i = 0; i < 60; i++) { b.NotifyInputActivity(); server.Tick(0.1); }
        Check(a.IsTongueActive(2) && c.IsTongueActive(2), "sustained activity renews its lease");
        server.Tick(RoomRules.InputActivityHold + 0.01);
        Check(!a.IsTongueActive(2) && !c.IsTongueActive(2), "stopping input publishes idle");

        b.NotifyInputActivity();
        server.Tick(0);
        server.SetSendingPaused(b, true);
        for (int i = 0; i < 40; i++)
        {
            b.NotifyInputActivity();
            a.SetAppearance(1012, 0, i % 2 == 0 ? 1001 : 1005);
            server.Tick(0.1);
        }
        Check(b.TongueActive && !a.IsTongueActive(2), "unrelated snapshots cannot extend a disconnected sender lease");
        server.SetSendingPaused(b, false);
        server.Tick(0);
        Check(a.IsTongueActive(2), "resuming sender renews active state");
        b.Leave();
        server.Tick(0);
        Check(!a.IsTongueActive(2), "departure drops activity immediately");
        server.Join(b, a.JoinedCode);
        server.Tick(0);
        Check(!b.TongueActive && !a.IsTongueActive(2), "rejoin cannot inherit old activity");

        server.Settings(a).Latency = 0.3;
        b.NotifyInputActivity();
        server.Tick(0.1);
        Check(!a.IsTongueActive(2) && c.IsTongueActive(2), "receiver delay affects only that observer");
        server.Tick(0.21);
        Check(a.IsTongueActive(2), "delayed activity reaches observer");
        b.StopInputActivity();
        server.Tick(0.31);
        Check(!a.IsTongueActive(2), "delayed stop reaches observer");
    }

    private static void CheckKicking()
    {
        var server = new RoomSandbox();
        using var owner = server.AddClient(1, "Owner", 1012);
        using var target = server.AddClient(2, "Target", 1012);
        using var successor = server.AddClient(3, "Successor", 1012);
        using var otherOwner = server.AddClient(4, "Other owner", 1012);
        server.Create(owner, "Moderated room");
        var code = owner.JoinedCode;
        server.Join(target, code);
        server.Join(successor, code);
        server.Tick(0);
        var targetPresence = owner.View.Members.Single(m => m.Id == target.Id).Presence;
        Check(target.Kick(successor.Id, owner.View.Members.Single(m => m.Id == successor.Id).Presence)
            == "Rooms_KickNotOwner", "non-owner cannot remove a member");
        Check(owner.Kick(owner.Id, owner.View.Members.Single(m => m.Id == owner.Id).Presence)
            == "Rooms_KickInvalidTarget", "owner cannot remove self");
        Check(owner.Kick(999, 1) == "Rooms_KickInvalidTarget", "missing target rejected");

        var oldSuccessorPresence = owner.View.Members.Single(m => m.Id == successor.Id).Presence;
        successor.Leave();
        server.Join(successor, code);
        server.Tick(0);
        Check(owner.Kick(successor.Id, oldSuccessorPresence) == "Rooms_KickInvalidTarget"
            && successor.JoinedCode == code, "stale confirmation cannot target rejoined member");

        server.SendChat(target, "Before removal");
        target.NotifyInputActivity();
        server.Tick(0);
        var staleSnapshot = target.View;
        server.Settings(target).Latency = 10;
        owner.SetAppearance(1013, 0, 1005);
        server.Settings(target).RequestDelay = 2;
        Check(target.FindAndJoin(), "target has pending automatic join before removal");
        var removedNotifications = 0;
        var reentrantRequestStarted = false;
        target.Changed += () =>
        {
            if (target.Failure != RoomFailure.Removed) return;
            removedNotifications++;
            Check(target.JoinedCode == "" && target.View == null && !target.IsBusy,
                "removal notification observes fully cleared membership");
            reentrantRequestStarted |= target.FindAndJoin();
        };
        Check(owner.Kick(target.Id, targetPresence) == "", "owner can remove current member");
        Check(target.Failure == RoomFailure.Removed && target.RequestState == RoomRequestState.Failed
            && target.Operation == RoomOperation.None && target.Bubbles.Count == 0 && !target.TongueActive
            && removedNotifications == 1 && !reentrantRequestStarted && server.PendingRequestCount == 0,
            "removal atomically clears activity, chat and pending auto-join");
        target.Receive(staleSnapshot with { Revision = staleSnapshot.Revision + 100 });
        server.Tick(12);
        Check(target.JoinedCode == "" && target.View == null && target.Failure == RoomFailure.Removed
            && owner.View.Members.Length == 2 && successor.View.Members.Length == 2,
            "queued and stale snapshots cannot resurrect kicked member");

        server.Settings(target).RequestDelay = 0;
        target.Join(code);
        server.Tick(0);
        Check(target.Failure == RoomFailure.Banned && target.JoinedCode == "", "room code cannot bypass ban");
        server.Create(target, "Target's own room");
        var retained = target.JoinedCode;
        target.Join(code);
        server.Tick(0);
        Check(target.Failure == RoomFailure.Banned && target.JoinedCode == retained,
            "failed banned join preserves current room");
        target.Search();
        server.Tick(0);
        Check(target.Listings.All(r => r.Code != code), "banned room excluded from player's directory");
        target.FindAndJoin();
        server.Tick(0);
        Check(target.Failure == RoomFailure.NoMatchingRoom && target.JoinedCode == retained,
            "random join cannot select banned room");

        server.Create(otherOwner, "Allowed room");
        target.FindAndJoin();
        server.Tick(0);
        server.Tick(0);
        Check(target.JoinedCode == otherOwner.JoinedCode, "random join can still choose another allowed room");
        owner.Leave();
        server.Tick(0);
        Check(successor.View.OwnerId == successor.Id, "surviving member becomes owner");
        target.Join(code);
        server.Tick(0);
        Check(target.Failure == RoomFailure.Banned && target.JoinedCode == otherOwner.JoinedCode,
            "ban survives host migration");
        Check(owner.Kick(successor.Id, successor.View.Members.Single().Presence) == "Rooms_KickUnavailable",
            "departed owner cannot remove former members");
        server.Create(owner, "Same owner's new room");
        target.Join(owner.JoinedCode);
        server.Tick(0);
        Check(target.RequestState == RoomRequestState.Succeeded && target.JoinedCode == owner.JoinedCode
            && owner.JoinedCode != code, "new room does not inherit same owner's old ban list");
        successor.Leave();
        Check(server.Search().All(r => r.Code != code), "last member's departure destroys old banned room");
    }
}
#endif
