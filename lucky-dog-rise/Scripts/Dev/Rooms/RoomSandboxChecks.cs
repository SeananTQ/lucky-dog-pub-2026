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
        return "ROOM_SANDBOX_PASS: isolated replicas, latency, bounded queues, reconnect, capacity, owner transfer, metadata, chat validation/expiry, local settings.\n"
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
}
#endif
