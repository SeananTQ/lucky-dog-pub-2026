using System.Text.Json;
using LuckyDogRise.Rooms.Poker;

internal static class NetworkChecks
{
    public static void Run()
    {
        ReadinessAndPrivacy();
        AuthenticationAndOrdering();
        AdmissionAndPresentationBarrier();
        MembershipAndRecovery();
        MalformedMessages();
        AtomicCoreAdmission();
        Console.WriteLine("PASS serialized multiplayer readiness, private hands, permissions, retries, admission and interruption");
    }

    private static void ReadinessAndPrivacy()
    {
        using var h = new Hub("a", "b");
        var a = h.Sessions["a"]; var b = h.Sessions["b"];
        b.Propose(); h.Step();
        Check.Equal(a.State!.Status, PokerTableStatus.Idle, "guest cannot propose on the host's behalf");
        a.Propose(); h.Step();
        Check.That(a.IsReady && !b.IsReady && !a.CanStart, "host auto-ready; humans explicitly consent");
        a.Start(); h.Step();
        Check.Equal(a.State!.Status, PokerTableStatus.Preparing, "one ready player cannot start");
        b.SetReady(true);
        int sentReady = h.Sent.Count;
        b.SetReady(true);
        Check.That(b.IsReady && h.Sent.Count == sentReady, "pending readiness reserves input without flooding duplicate commands");
        h.Step();
        Check.That(a.CanStart, "two ready players permit host start");
        b.Start(); h.Step();
        Check.Equal(a.State.Status, PokerTableStatus.Preparing, "guest cannot start");
        a.Start(); h.Step();
        Check.That(!b.Act(PokerAction.Flip, 0), "deal interval accepts no input");
        h.Step(3.6);
        var av = a.State!.Hand!; var bv = b.State!.Hand!;
        Check.That(av.HoleCards.Count == 2 && bv.HoleCards.Count == 2 && !av.HoleCards.Intersect(bv.HoleCards).Any(), "each recipient has only its own hole cards");
        Check.That(bv.Public.Settlement == null, "no remote private cards before showdown");
        Check.Equal(b.DisplayView!.PlayerId, 1, "guest has local dog id 1 despite a different canonical seat");
        Check.Equal(b.DisplayView.Public.ActivePlayerId, 1, "current actor remaps to the local dog");
        Check.That(b.DisplayView.Public.Seats.Single(s => s.SeatNumber == 1).PlayerId != 1, "host remains a different dog on guest screen");
        foreach (var sent in h.Sent.Where(m => m.To == "b"))
        {
            var packet = PokerNetworkProtocol.Decode(sent.Bytes)!;
            if (packet.State?.Hand is not { } v) continue;
            Check.That(v.Public.Settlement == null && v.HoleCards.SequenceEqual(bv.HoleCards), "wire snapshots never contain another player's secret hand");
            Check.That(PokerNetworkProtocol.ValidState(packet.State, h.Links["b"].Context!), "legitimate snapshot passes strict validation");
        }
        a.Stop(); h.Step();
        h.Bot("dog"); a.Propose(); h.Step(0.5);
        Check.That(!a.State!.Players.Single(p => p.Companion).Ready, "companion readiness has a visible delay");
        h.Step(0.7);
        Check.That(a.CanStart && a.State!.Players.Single(p => p.Companion).Ready, "companions automatically participate after one second");
        a.Start(); h.Step();
        Check.That(b.State!.Hand is { PlayerId: 0, HoleCards.Count: 0 } && !b.IsParticipant, "unready guest receives no private cards");
        Check.That(!b.Act(PokerAction.EndTurn), "spectators cannot operate the table");
        h.Step(7);
        Check.That(a.State!.Hand!.Public.ActivePlayerId == a.State.Hand.PlayerId
            && a.State.Hand.Public.CompletedTurns.Count == 1, "authority finishes a companion's legal turn automatically");
        a.Act(PokerAction.EndTurn); h.Step(); a.FinishPresentation(); h.Step();
        Check.Equal(a.State!.Hand!.Public.HandNumber, 2L, "companions and spectators do not block the human's presentation barrier");
    }

    private static void AuthenticationAndOrdering()
    {
        using var h = Started();
        var a = h.Sessions["a"]; var b = h.Sessions["b"];
        var old = b.State!.Hand!;
        h.Links["a"].Hold = true;
        Check.That(b.Act(PokerAction.Flip, 0), "first click enqueues a command");
        Check.That(b.ActionPending && !b.Act(PokerAction.Flip, 1), "a second click waits for acknowledgement");
        var action = h.Sent.Last(m => m.From == "b" && PokerNetworkProtocol.Decode(m.Bytes)!.Kind == "action");
        h.Links["a"].Hold = false; h.Step();
        Check.That(!b.ActionPending && b.State!.Hand!.Public.Board[0].PendingReplacement, "ack clears pending input and applies first flip");
        long revision = a.State!.Hand!.Public.Revision;
        h.Links["a"].Enqueue("b", action.Bytes); h.Step();
        Check.Equal(a.State!.Hand!.Public.Revision, revision, "duplicate transport message cannot toggle again");

        // The sender can forge the command's PlayerId, but can only act as its authenticated seat.
        var c = b.State!.Hand!;
        var forged = h.Packet("b", "action", command: new(c.Public.SessionId, c.Public.HandNumber, c.Public.Revision,
            a.State!.Hand!.PlayerId, PokerAction.Flip, 1));
        h.Links["a"].Enqueue("b", PokerNetworkProtocol.Encode(forged)); h.Step();
        Check.That(a.State!.Hand!.Public.Board[1].PendingReplacement, "authenticated sender overrides forged player id");
        Check.Equal(a.State.Hand.Public.ActivePlayerId, a.State.Hand.PlayerId, "two accepted flips finish the guest's own turn");
        revision = a.State.Hand.Public.Revision;
        h.Inject("b", "action", command: new(old.Public.SessionId, old.Public.HandNumber, old.Public.Revision,
            a.State.Hand.PlayerId, PokerAction.EndTurn)); h.Step();
        Check.Equal(a.State!.Hand!.Public.Revision, revision, "stale or nonacting client cannot finish host turn");
        h.Inject("b", "stop"); h.Step();
        Check.Equal(a.State!.Status, PokerTableStatus.Playing, "only host can end table");

        var bad = h.Packet("b", "action", command: new(a.State.Hand!.Public.SessionId, 1, revision,
            a.State.Hand.PlayerId, PokerAction.EndTurn));
        foreach (var p in new[] { bad with { Room = "wrong" }, bad with { SenderTicket = "old-membership" },
                     bad with { RecipientTicket = "old-host" }, bad with { Table = Guid.NewGuid() } })
            h.Links["a"].Enqueue("b", PokerNetworkProtocol.Encode(p));
        h.Step();
        Check.Equal(a.State!.Hand!.Public.Revision, revision, "wrong room, ticket and table are ignored");
    }

    private static void AdmissionAndPresentationBarrier()
    {
        using var h = Started();
        var a = h.Sessions["a"]; var b = h.Sessions["b"];
        h.Human("c"); h.Step(); var c = h.Sessions["c"];
        Check.That(c.State!.Hand!.HoleCards.Count == 0 && !c.IsParticipant, "new member waits without a hand");
        c.SetReady(true); h.Step();
        Check.That(!c.IsParticipant && c.IsReady, "readying does not enter the current hand");
        PassHand(h);
        var settled = a.State!.Hand!.Public;
        Check.That(settled.Settlement != null && b.State!.Hand!.Public.Settlement?.Players.Count == 2, "showdown reveals only settled participants");
        Check.That(PokerNetworkProtocol.ValidState(b.State!, h.Links["b"].Context!), "settled snapshot is valid");
        a.FinishPresentation(); h.Step(0.6);
        Check.Equal(a.State!.Hand!.Public.HandNumber, settled.HandNumber, "next hand waits for every participating human");
        c.FinishPresentation(); h.Step();
        Check.Equal(a.State!.Hand!.Public.HandNumber, settled.HandNumber, "spectator acknowledgement cannot replace a participant");
        b.FinishPresentation(); h.Step();
        var next = a.State!.Hand!.Public;
        Check.Equal(next.HandNumber, settled.HandNumber + 1, "all presentations finish before a new hand");
        Check.That(c.IsParticipant && c.State!.Hand!.HoleCards.Count == 2, "ready newcomer is admitted at next deal");
        Check.Equal(next.Seats.Count, 3, "next hand adds one stable seat");
        foreach (var seat in settled.Seats)
            Check.Equal(next.Seats.Single(s => s.PlayerId == seat.PlayerId).Chips, seat.Chips - 100, "old balance preserved with one new ante");
        Check.Equal(next.Seats.Single(s => s.SeatNumber == 3).Chips, 900L, "new participant starts at 1000 minus ante");
        h.Inject("b", "finished", hand: settled.HandNumber); h.Step();
        Check.Equal(a.State!.Hand!.Public.HandNumber, next.HandNumber, "late acknowledgement cannot start another hand");
    }

    private static void MembershipAndRecovery()
    {
        using (var h = Started())
        {
            var b = h.Sessions["b"];
            h.Links["b"].Hold = true; h.Step(11);
            Check.That(!b.Connected && !b.Act(PokerAction.EndTurn), "stale state disables client input");
            h.Links["b"].Hold = false; h.Step();
            Check.That(b.Connected, "heartbeat snapshots resume after a transient delivery pause");
            h.Owner = "b"; h.Step();
            Check.Equal(h.Sessions["b"].State!.Status, PokerTableStatus.Interrupted, "new host does not reconstruct hidden deck or continue old game");
            b.Propose(); h.Step();
            Check.Equal(b.State!.Status, PokerTableStatus.Preparing, "new host can propose a fresh table after interruption");
        }
        using (var h = Started())
        {
            h.Remove("b"); h.Step();
            Check.Equal(h.Sessions["a"].State!.Status, PokerTableStatus.Interrupted, "participant removal stops the fixed hand safely");
        }
        using (var h = new Hub("a", "b"))
        {
            var a = h.Sessions["a"]; a.Propose(); h.Step();
            h.Sessions["b"].SetReady(true); h.Step();
            h.Remove("b"); h.Step(); h.Human("b"); h.Step();
            Check.That(!h.Sessions["b"].IsReady, "new membership does not inherit consent");
            h.Sessions["b"].SetReady(true); h.Step();
            Check.That(a.CanStart, "new membership has an independent command sequence");
            a.Start(); h.Step(3.6); PassHand(h); h.Step(46);
            Check.Equal(a.State!.Status, PokerTableStatus.Interrupted, "unresponsive presentation cannot block a table forever");
        }
    }

    private static void MalformedMessages()
    {
        using var h = Started();
        var valid = h.Sessions["b"].State!;
        foreach (var bytes in new[] { Array.Empty<byte>(), new byte[PokerNetworkProtocol.MaxBytes + 1], "{}"u8.ToArray(), "{bad}"u8.ToArray() })
            Check.That(PokerNetworkProtocol.Decode(bytes) == null, "malformed or oversized message is rejected");
        foreach (var state in new[] { valid with { Players = null! }, valid with { DealRemaining = double.NaN },
                     valid with { Players = [valid.Players[0], valid.Players[0]] },
                     valid with { Hand = valid.Hand! with { HoleCards = [0, 0] } },
                     valid with { Hand = valid.Hand! with { Public = null! } },
                     valid with { Hand = valid.Hand! with { Public = valid.Hand.Public with { Seats = null! } } } })
            Check.That(!PokerNetworkProtocol.ValidState(state, h.Links["b"].Context!), "malformed projection rejected before UI");
        long version = h.Sessions["a"].State!.Version;
        h.Links["a"].Enqueue("b", PokerNetworkProtocol.Encode(h.Packet("b", "state") with { State = valid with { Version = version + 10000 } }));
        h.Step();
        Check.That(h.Sessions["a"].State!.Version < version + 10000, "guest cannot publish authority snapshots");
    }

    private static void AtomicCoreAdmission()
    {
        var m = new MischiefPokerMatch([1, 2], 1);
        m.StartHand(0); Check.PassAll(m, 0); m.ResolveHand();
        string before = JsonSerializer.Serialize(m.GetPublicView());
        Check.Equal(m.StartHand(1, [0], [3]), PokerError.InvalidDeck, "bad deck prevents admission");
        Check.Equal(m.StartHand(1, newPlayers: [1]), PokerError.InvalidRoster, "duplicate seat prevents admission");
        Check.Equal(JsonSerializer.Serialize(m.GetPublicView()), before, "rejected admission preserves the last result atomically");
        Check.Equal(m.StartHand(1, newPlayers: [3]), PokerError.None, "admission and deal commit together");
        Check.Equal(m.GetPlayerView(3).HoleCards.Count, 2, "new seat cannot exist without its new hand");
    }

    private static Hub Started()
    {
        var h = new Hub("a", "b");
        h.Sessions["a"].Propose(); h.Step(); h.Sessions["b"].SetReady(true); h.Step();
        h.Sessions["a"].Start(); h.Step(3.6);
        Check.Equal(h.Sessions["b"].State!.Status, PokerTableStatus.Playing, "two endpoints receive started table");
        return h;
    }
    private static void PassHand(Hub h)
    {
        for (int i = 0; i < 8 && h.Sessions[h.Owner].State!.Hand!.Public.Phase == PokerPhase.Acting; i++)
        {
            var s = h.Sessions.Values.First(s => s.IsParticipant && s.State!.Hand!.PlayerId == s.State.Hand.Public.ActivePlayerId);
            Check.That(s.Act(PokerAction.EndTurn), "active endpoint can pass"); h.Step();
        }
        Check.Equal(h.Sessions[h.Owner].State!.Hand!.Public.Phase, PokerPhase.Settled, "all participants pass into settlement");
    }

    private sealed class Hub : IDisposable
    {
        public string Owner = "a";
        public readonly Dictionary<string, (string Ticket, bool Bot)> Members = new();
        public readonly Dictionary<string, Link> Links = new();
        public readonly Dictionary<string, PokerNetworkSession> Sessions = new();
        public readonly List<(string From, string To, byte[] Bytes)> Sent = new();
        private long _injected = 100000;
        public Hub(params string[] humans) { foreach (string id in humans) Human(id); Step(); }
        public void Human(string key)
        {
            Members[key] = (Guid.NewGuid().ToString("N"), false);
            Links[key] = new(this, key); Sessions[key] = new(Links[key]);
        }
        public void Bot(string key) => Members[key] = (Guid.NewGuid().ToString("N"), true);
        public void Remove(string key) { Sessions.Remove(key, out var session); session?.Dispose(); Links.Remove(key); Members.Remove(key); }
        public void Step(double seconds = 0.15)
        { for (double t = 0; t < seconds; t += 0.02) foreach (var s in Sessions.Values.ToArray()) s.Tick(0.02); }
        public PokerPacket Packet(string sender, string kind, PokerCommand? command = null, long hand = 0)
            => new(PokerNetworkProtocol.Version, "room:social", Members[sender].Ticket, Members[Owner].Ticket,
                kind, ++_injected, Sessions[Owner].State!.Table, Command: command, FinishedHand: hand);
        public void Inject(string sender, string kind, PokerCommand? command = null, long hand = 0)
            => Links[Owner].Enqueue(sender, PokerNetworkProtocol.Encode(Packet(sender, kind, command, hand)));
        public void Dispose() { foreach (var s in Sessions.Values) s.Dispose(); }
    }
    private sealed class Link(Hub hub, string key) : IPokerLink
    {
        private readonly Queue<(string Sender, byte[] Bytes)> _queue = new();
        public bool Hold;
        public event Action<string, byte[]> Received = delegate { };
        public PokerRoomContext? Context => hub.Members.ContainsKey(key) ? new("room:social", key, hub.Owner,
            hub.Members.Select((m, i) => new PokerPeer(m.Key, m.Value.Ticket, m.Key == key ? 1 : i + 20, m.Key, m.Value.Bot)).ToArray()) : null;
        public bool Send(string recipient, byte[] bytes)
        {
            hub.Sent.Add((key, recipient, (byte[])bytes.Clone()));
            if (!hub.Links.TryGetValue(recipient, out var link)) return false;
            link.Enqueue(key, bytes); return true;
        }
        public void Enqueue(string sender, byte[] bytes) => _queue.Enqueue((sender, (byte[])bytes.Clone()));
        public void Poll() { if (!Hold) while (_queue.TryDequeue(out var m)) Received(m.Sender, m.Bytes); }
        public void Dispose() => _queue.Clear();
    }
}
