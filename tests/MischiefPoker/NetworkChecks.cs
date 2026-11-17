using System.Text.Json;
using LuckyDogRise.Rooms.Poker;

internal static class NetworkChecks
{
    public static void Run()
    {
        ReadinessAndPrivacy();
        AuthenticationAndOrdering();
        AdmissionAndTimedContinuation();
        SpectatorsJoinPausedTable();
        FailedAdmissionPreservesBalances();
        GameEntryConsent();
        PauseAndResume();
        GameplayVoteKeepsPausedTable();
        SitOutAndReturn();
        RestartDepletedTable();
        MembershipAndRecovery();
        MalformedMessages();
        AtomicCoreAdmission();
        CompanionRetirementBoundary();
        CompanionWinningsRemainValid();
        FlipHistory();
        Console.WriteLine("PASS serialized multiplayer readiness, private hands, permissions, retries, admission and interruption");
    }

    private static void ReadinessAndPrivacy()
    {
        using var h = new Hub("a", "b");
        var a = h.Sessions["a"]; var b = h.Sessions["b"];
        Check.Equal(a.State!.Status, PokerTableStatus.Preparing, "poker room automatically enters preparation");
        Check.That(!b.Propose() && !a.SetReady(false), "guest cannot create a table; host has no ready toggle");
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
        a.Act(PokerAction.EndTurn); h.Step();
        h.Step(PokerNetworkSession.PayoutDuration(a.State!.Hand!.Public) + PokerNetworkSession.CollectionDuration + .2);
        Check.Equal(a.State!.Hand!.Public.HandNumber, 2L, "authority advances without any presentation acknowledgement");
    }

    private static void FlipHistory()
    {
        using var h = new Hub("a", "b");
        var a = h.Sessions["a"]; var b = h.Sessions["b"];
        b.SetReady(true); h.Step(); a.Start(); h.Step(4.1);
        Check.That(b.Act(PokerAction.Flip, 0), "first remote flip accepted"); h.Step();
        Check.That(b.Act(PokerAction.Flip, 0), "second remote flip accepted"); h.Step();
        var hand = a.State!.Hand!.Public;
        Check.That(hand.Flips.Count == 2 && !hand.Board[0].PendingReplacement, "both actions survive identical final board state");
        Check.Sequence(hand.Flips.Select(f => f.Number), new[] { 1, 2 }, "left then right are per-turn action numbers");
        Check.Sequence(hand.Flips.Select(f => f.FaceUp), new[] { false, true }, "flip history preserves face changes");
        Check.That(a.DisplayView!.Public.Flips.All(f => f.PlayerId == a.Context!.Members.Single(m => m.Key == "b").LocalId)
            && b.DisplayView!.Public.Flips.All(f => f.PlayerId == 1), "each viewer rebinds acting dog identity");
        var state = a.State;
        var room = a.Context!;
        bool Valid(params PokerFlip[] flips) => PokerNetworkProtocol.ValidState(state with
        { Hand = state.Hand! with { Public = hand with { Flips = flips } } }, room);
        Check.That(Valid(hand.Flips.ToArray()), "complete public history survives serialized snapshot");
        Check.That(!Valid(hand.Flips[0], hand.Flips[0]), "duplicate action revision rejected");
        Check.That(!Valid(hand.Flips[0] with { PlayerId = 6 }, hand.Flips[1]), "unknown actor rejected");
        Check.That(!Valid(hand.Flips[0] with { Slot = 5 }, hand.Flips[1]), "out-of-range card rejected");
        Check.That(!Valid(hand.Flips[0] with { Number = 2 }, hand.Flips[1]), "missing first action rejected");
        Check.That(!Valid(hand.Flips[0], hand.Flips[1] with { FaceUp = false }), "non-toggle history rejected");
        Check.That(!Valid(hand.Flips[0]), "incomplete history rejected against completed turn");
        var saved = hand.Flips.ToArray();
        a.Act(PokerAction.EndTurn); h.Step();
        Check.Sequence(a.State!.Hand!.Public.Flips, saved, "system replacement/settlement adds no paw action");
        h.Step(PokerNetworkSession.PayoutDuration(a.State.Hand.Public) + 5);
        Check.That(a.State.Hand!.Public.HandNumber == 2 && a.State.Hand.Public.Flips.Count == 0, "new hand clears bounded flip history");
    }

    private static void GameEntryConsent()
    {
        using var h = new Hub("a", "b", "c");
        h.Game = "social"; h.Step();
        Check.Equal(h.Sessions["a"].State!.Status, PokerTableStatus.Idle, "chat rooms have no poker preparation");
        Check.That(!h.Sessions["a"].Propose(), "poker cannot be started outside its room game");
        h.Accepted.Add("b"); h.Bot("dog"); h.Game = "mischief"; h.Step(.2);
        var a = h.Sessions["a"]; var b = h.Sessions["b"]; var c = h.Sessions["c"];
        Check.That(a.IsReady && b.IsReady && !c.IsReady, "only accepted humans receive initial readiness");
        Check.That(!a.State!.Players.Single(p => p.Companion).Ready, "companion waits before visibly readying");
        b.SetReady(false); h.Step(1.1);
        Check.That(!b.IsReady && a.State!.Players.Single(p => p.Companion).Ready, "cancelled consent is not reapplied each tick");
        a.Start(); h.Step(3.5);
        Check.That(!b.IsParticipant && !c.IsParticipant && c.State!.Hand!.HoleCards.Count == 0, "declined and cancelled players wait without being charged");
    }

    private static void PauseAndResume()
    {
        using var h = Started();
        var a = h.Sessions["a"]; var b = h.Sessions["b"];
        Check.That(!b.SetPauseRequested(true), "guest has no pause command");
        Check.That(a.SetPauseRequested(true), "host can request pause while acting");
        Check.Equal(a.State!.Continuation, PokerContinuation.PauseRequested, "pause is pending, not an immediate stop");
        Check.That(a.SetPauseRequested(false), "host may undo pause before collection");
        Check.Equal(a.State!.Continuation, PokerContinuation.Automatic, "undo restores automatic continuation");
        a.SetPauseRequested(true); PassHand(h);
        var settled = a.State!.Hand!.Public;
        var table = a.State.Table;
        h.Step(1);
        Check.Equal(a.State!.Continuation, PokerContinuation.PauseRequested, "settlement remains visible while payout finishes");
        h.Step(PokerNetworkSession.PayoutDuration(settled));
        Check.Equal(a.State!.Continuation, PokerContinuation.Paused, "pause waits until all winner payouts complete");
        h.Step(25);
        Check.Equal(a.State!.Hand!.Public.HandNumber, settled.HandNumber, "paused table charges no further ante");
        Check.That(a.State.Hand.Public.Seats.SequenceEqual(settled.Seats) && a.CanResume, "paused balances and result remain intact");
        Check.That(!b.Resume(), "guest cannot resume");
        Check.That(a.Resume(), "host resumes the same table");
        Check.That(!a.Resume(), "duplicate resume cannot charge a second ante");
        Check.Equal(a.State!.Continuation, PokerContinuation.Collecting, "resume starts card collection first");
        h.Step(PokerNetworkSession.CollectionDuration + .2);
        var next = a.State!.Hand!.Public;
        Check.That(a.State.Table == table && next.SessionId == settled.SessionId && next.HandNumber == settled.HandNumber + 1,
            "resume preserves table and match identity");
        Check.Equal(next.Dealer, settled.Settlement!.NextDealer, "resume uses the previously computed next dealer");
        foreach (var seat in settled.Seats)
            Check.Equal(next.Seats.Single(s => s.PlayerId == seat.PlayerId).Chips, seat.Chips - 100, "resume keeps balance and deducts exactly one ante");
        var stale = h.Packet("a", "pause", hand: settled.HandNumber) with { Ready = true };
        h.Links["a"].Enqueue("a", PokerNetworkProtocol.Encode(stale)); h.Step();
        Check.Equal(a.State!.Continuation, PokerContinuation.Automatic, "previous hand pause cannot affect the new hand");
        h.Inject("b", "pause", hand: next.HandNumber); h.Step();
        Check.Equal(a.State!.Continuation, PokerContinuation.Automatic, "forged guest pause is ignored");
    }

    private static void GameplayVoteKeepsPausedTable()
    {
        using var h = new Hub("a", "b");
        var a = h.Sessions["a"]; var b = h.Sessions["b"];
        b.SetReady(true); h.Step();
        h.GameChangePending = true;
        Check.That(!a.CanStart && !a.Start(), "vote blocks start even before the next session tick");
        h.Inject("a", "start"); h.Step();
        Check.Equal(a.State!.Status, PokerTableStatus.Preparing, "queued start cannot bypass the gameplay vote");
        h.GameChangePending = false;
        Check.That(a.Start(), "cancelled vote restores preparation without requiring readiness again");
        h.Step(3.6); a.SetPauseRequested(true); PassHand(h);
        h.Step(PokerNetworkSession.PayoutDuration(a.State!.Hand!.Public) + .2);
        var table = a.State!.Table;
        var hand = a.State.Hand!.Public;
        string result = JsonSerializer.Serialize(hand);
        h.GameChangePending = true;
        Check.That(!a.CanResume && !a.CanRestart && !a.Resume() && !a.Restart(), "vote locks resume and restart immediately");
        h.Inject("a", "resume", hand: hand.HandNumber);
        h.Inject("a", "restart", hand: hand.HandNumber);
        h.Step(61);
        Check.That(a.State!.Table == table && a.State.Continuation == PokerContinuation.Paused
            && JsonSerializer.Serialize(a.State.Hand!.Public) == result, "vote keeps cards, chips, seats and dealer intact without another ante");
        h.GameChangePending = false;
        Check.That(a.CanResume && a.CanRestart, "expiry or cancellation releases both controls");
        Check.That(JsonSerializer.Serialize(a.State.Hand!.Public) == result, "releasing a vote never starts a hand automatically");
        h.GameChangePending = true; h.Step();
        h.Game = "social"; h.GameChangePending = false; h.Step();
        Check.That(a.State!.Hand == null && b.State!.Hand == null && !a.CanResume && !a.CanRestart,
            "successful gameplay change clears both endpoints' old table projection");
        h.Inject("a", "resume", hand: hand.HandNumber); h.Step();
        Check.That(a.State!.Hand == null, "late continuation cannot revive a table in chat gameplay");
        h.Accepted.Add("b"); h.Game = "mischief"; h.Step();
        Check.That(a.State!.Status == PokerTableStatus.Preparing && a.State.Table != table && a.CanStart,
            "returning to poker prepares a new table with fresh consent");
        a.Start(); h.Step();
        Check.That(a.State!.Hand!.Public.HandNumber == 1 && a.State.Hand.Public.Dealer.PlayerId == a.State.Hand.PlayerId
            && a.State.Hand.Public.Seats.All(s => s.Chips == 900), "new gameplay session starts from host dealer and initial chips");
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

    private static void AdmissionAndTimedContinuation()
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
        Check.Equal(a.State!.Hand!.Public.HandNumber, settled.HandNumber, "payout interval does not end early on an acknowledgement");
        c.FinishPresentation(); h.Step();
        Check.Equal(a.State!.Hand!.Public.HandNumber, settled.HandNumber, "spectator acknowledgement cannot replace a participant");
        // Neither an obscured host view nor an absent guest acknowledgement blocks the authority.
        h.Step(PokerNetworkSession.PayoutDuration(settled));
        Check.Equal(a.State!.Continuation, PokerContinuation.Collecting, "collection follows timed payout");
        h.Step(PokerNetworkSession.CollectionDuration);
        var next = a.State!.Hand!.Public;
        Check.Equal(next.HandNumber, settled.HandNumber + 1, "authority starts a new hand after timed payout and collection");
        Check.That(c.IsParticipant && c.State!.Hand!.HoleCards.Count == 2, "requested newcomer joins at automatic next deal");
        Check.Equal(next.Seats.Count, 3, "next hand appends the new seat");
        Check.Equal(next.SessionId, settled.SessionId, "admission keeps the existing match");
        Check.Equal(next.Dealer, settled.Settlement!.NextDealer, "admission does not replace the scheduled dealer or direction");
        Check.Equal(c.State!.Hand!.Public.Seats.Single(s => s.PlayerId == c.State.Hand.PlayerId).Chips, 900L,
            "first admission issues 1000 chips and charges exactly one ante");
        Check.That(PokerNetworkProtocol.ValidState(c.State, h.Links["c"].Context!), "new participant receives a valid private projection");
        foreach (var seat in settled.Seats)
            Check.Equal(next.Seats.Single(s => s.PlayerId == seat.PlayerId).Chips, seat.Chips - 100, "old balance preserved with one new ante");
        h.Inject("b", "finished", hand: settled.HandNumber); h.Step();
        Check.Equal(a.State!.Hand!.Public.HandNumber, next.HandNumber, "obsolete acknowledgement cannot start another hand");
        // Do not use b's real command sequence after the forged high-sequence packet.
        a.SetPauseRequested(true); h.Step(55);
        h.Step(PokerNetworkSession.PayoutDuration(a.State!.Hand!.Public));
        Check.That(a.CanResume && a.CanRestart, "paused table offers both continuation and restart");
        Guid oldTable = a.State.Table;
        var oldRestart = h.Packet("a", "restart", hand: a.State.Hand!.Public.HandNumber);
        Check.That(!b.Restart() && a.Restart() && !a.Restart(), "only host restarts once");
        h.Step();
        Check.That(!c.SetReady(false), "admission choice is frozen during restart collection");
        h.Step(PokerNetworkSession.CollectionDuration + .2);
        Check.That(c.IsParticipant && c.State!.Hand!.HoleCards.Count == 2, "restart includes all willing participants");
        Check.That(a.State!.Table != oldTable && a.State.Hand!.Public.HandNumber == 1, "restart invalidates the previous table identity");
        Check.That(a.State.Hand.Public.Seats.All(s => s.Chips == 900) && a.State.Hand.Public.Pot == 300,
            "all restarted participants get exactly 1000 and pay one ante");
        Check.Equal(a.State.Hand.Public.Dealer, new PokerDealer(a.State.Hand.PlayerId, 1, PokerDirection.Forward), "restart privileges the host as first dealer");
        h.Links["a"].Enqueue("a", PokerNetworkProtocol.Encode(oldRestart)); h.Step();
        Check.Equal(a.State.Hand.Public.HandNumber, 1L, "stale restart cannot reset the new hand");
    }

    private static void SpectatorsJoinPausedTable()
    {
        using var h = new Hub("a", "b", "c");
        var a = h.Sessions["a"]; var b = h.Sessions["b"]; var c = h.Sessions["c"];
        b.SetReady(true); h.Step();
        Check.That(a.CanStart && a.Start(), "minimum ready count permits start without every member");
        h.Step(3.6);
        Check.That(!c.IsReady && !c.IsParticipant && c.State!.Hand!.HoleCards.Count == 0,
            "unready member becomes a spectator without a seat, ante or private cards");
        Check.That(!c.Act(PokerAction.Flip, 0) && !c.Act(PokerAction.EndTurn), "spectator cannot act");
        a.SetPauseRequested(true); h.Step();
        Check.That(!b.IsPaused && !c.IsPaused, "reservation is not actual pause for members");
        PassHand(h); h.Step(PokerNetworkSession.PayoutDuration(a.State!.Hand!.Public));
        Check.That(a.IsPaused && b.IsPaused && c.IsPaused, "all members observe actual pause after payouts");
        var previous = a.State!.Hand!.Public;
        var table = a.State.Table;
        b.SetReady(false); c.SetReady(true); h.Step();
        Check.That(a.CanResume, "pending first-time admission can satisfy the minimum count on resume");
        c.SetReady(false); h.Step();
        Check.That(!a.CanResume && !c.IsParticipant, "cancelled admission neither reserves a seat nor issues chips");
        h.Human("d"); h.Step(); var d = h.Sessions["d"];
        Check.That(d.IsPaused && !d.IsParticipant && d.State!.Hand!.HoleCards.Count == 0,
            "late member sees the same paused spectator state");
        d.SetReady(true); h.Step();
        Check.That(a.CanResume && a.Resume(), "host resumes with a late spectator who requested a seat");
        h.Step();
        Check.That(!d.SetReady(false), "seat requests are frozen during collection");
        h.Step(PokerNetworkSession.CollectionDuration + .2);
        var next = a.State!.Hand!.Public;
        Check.That(a.State.Table == table && next.SessionId == previous.SessionId && next.HandNumber == previous.HandNumber + 1,
            "resume with a new seat continues the match instead of restarting");
        Check.That(!c.IsParticipant && c.State!.Hand!.HoleCards.Count == 0, "cancelled spectator remains outside the resumed hand");
        Check.That(b.IsParticipant && !b.IsInHand && b.State!.Hand!.HoleCards.Count == 0, "old sit-out retains its seat without cards");
        Check.Equal(next.Seats.Single(s => s.PlayerId == b.State!.Hand!.PlayerId),
            previous.Seats.Single(s => s.PlayerId == b.State!.Hand!.PlayerId), "sitting out during pause does not change chips or seat");
        var added = next.Seats.Single(s => s.PlayerId == d.State!.Hand!.PlayerId);
        Check.That(added.SeatNumber == 3 && added.Chips == 900 && next.Pot == 200,
            "new seat appends after existing seats, not after spectator member IDs");
        Check.That(d.IsInHand && d.State!.Hand!.HoleCards.Count == 2
            && PokerNetworkProtocol.ValidState(d.State, h.Links["d"].Context!), "late admission receives exactly its own valid hand");
        Check.Equal(d.DisplayView!.PlayerId, 1, "new member's local dog mapping is independent of its seat number");
        h.Links["a"].Enqueue("c", PokerNetworkProtocol.Encode(h.Packet("c", "ready", hand: previous.HandNumber) with { Ready = true }));
        h.Step();
        Check.That(!c.IsReady && !c.IsParticipant, "late previous-hand sit-in request cannot enter the new deal");
    }

    private static void FailedAdmissionPreservesBalances()
    {
        using var h = Started(); var a = h.Sessions["a"]; var b = h.Sessions["b"];
        var match = new MischiefPokerMatch([1, 2], 1, new() { InitialChips = 100 });
        match.StartHand(0, Check.Deck([Check.Cards("As Ah"), Check.Cards("2c 3d")], "Kc Jd 8h 6s 4c"));
        Check.PassAll(match, 0);
        typeof(PokerNetworkSession).GetFields(System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
            .Single(f => f.FieldType == typeof(MischiefPokerMatch)).SetValue(a, match);
        h.Step(12);
        h.Human("c"); h.Step(); var c = h.Sessions["c"];
        c.SetReady(true); h.Step();
        var before = a.State!.Hand!.Public;
        Check.That(!a.CanResume && !a.Resume() && !c.IsParticipant, "newcomer does not bypass another willing player's insufficient chips");
        Check.That(a.State.Hand.Public.Seats.SequenceEqual(before.Seats), "rejected resume leaves all existing balances untouched");
        b.SetReady(false); h.Step();
        Check.That(a.CanResume && a.Resume(), "depleted player may spectate while two funded players continue");
        h.Step(PokerNetworkSession.CollectionDuration + 3.6);
        Check.That(!b.IsInHand && c.IsInHand, "only the consenting funded roster receives cards");
        Check.Equal(b.State!.Hand!.Public.Seats.Single(s => s.PlayerId == b.State.Hand.PlayerId).Chips, 0L,
            "sitting out never refills depleted chips");
        b.SetReady(true); h.Step(); PassHand(h);
        h.Step(PokerNetworkSession.PayoutDuration(a.State!.Hand!.Public));
        Check.That(a.IsPaused && !a.CanResume && !b.IsInHand, "depleted return request cannot bypass the ante check");
    }

    private static void SitOutAndReturn()
    {
        using var h = new Hub("a", "b", "c");
        var a = h.Sessions["a"]; var b = h.Sessions["b"]; var c = h.Sessions["c"];
        b.SetReady(true); c.SetReady(true); h.Step(); a.Start(); h.Step(3.6);
        b.SetReady(false); h.Step();
        Check.That(b.IsInHand && !b.IsReady && b.State!.Hand!.HoleCards.Count == 2, "sit-out request keeps the current hand and private cards");
        b.SetReady(true); h.Step(); Check.That(b.IsReady, "pending sit-out can be cancelled");
        b.SetReady(false); h.Step(); PassHand(h);
        int id = b.State!.Hand!.PlayerId;
        var settled = b.State.Hand.Public;
        var seat = settled.Seats.Single(s => s.PlayerId == id);
        h.Step(PokerNetworkSession.PayoutDuration(settled) + PokerNetworkSession.CollectionDuration + 3.6);
        Check.That(b.IsParticipant && !b.IsInHand && b.State!.Hand!.HoleCards.Count == 0, "sitting out reserves the seat without receiving private cards");
        Check.Equal(b.State.Hand.Public.Seats.Single(s => s.PlayerId == id), seat, "sitting out preserves both balance and seat number");
        Check.That(PokerNetworkProtocol.ValidState(b.State, h.Links["b"].Context!), "sitting-out projection validates");
        Check.That(!b.Act(PokerAction.EndTurn) && !b.Act(PokerAction.Flip, 0), "sitting-out player cannot act");
        b.SetReady(true); h.Step(); Check.That(!b.IsInHand, "return does not retroactively join an already dealt hand");
        PassHand(h); h.Step(PokerNetworkSession.PayoutDuration(a.State!.Hand!.Public) + PokerNetworkSession.CollectionDuration + .2);
        Check.That(b.IsInHand && b.State!.Hand!.HoleCards.Count == 2, "return admits the original seat at the next deal");
        Check.Equal(b.State.Hand.Public.Seats.Single(s => s.PlayerId == id).Chips, seat.Chips - 100, "return deducts one ante without refilling");
        Check.Equal(b.State.Hand.Public.Seats.Single(s => s.PlayerId == id).SeatNumber, seat.SeatNumber, "return never renumbers seats");
        h.Step(3.6); b.SetReady(false); c.SetReady(false); h.Step(); PassHand(h);
        h.Step(PokerNetworkSession.PayoutDuration(a.State!.Hand!.Public) + 1);
        Check.That(a.State!.Continuation == PokerContinuation.Paused && !a.CanResume && !a.CanRestart,
            "fewer than two willing players pauses without charging another ante");
        b.SetReady(true); h.Step(); Check.That(a.CanResume, "returning original player re-enables resume");
    }

    private static void RestartDepletedTable()
    {
        using var h = Started(); var a = h.Sessions["a"];
        // Trusted authority fixture: one final affordable hand, not a public client command.
        var match = new MischiefPokerMatch([1, 2], 1, new() { InitialChips = 100 });
        match.StartHand(0, Check.Deck([Check.Cards("As Ah"), Check.Cards("2c 3d")], "Kc Jd 8h 6s 4c"));
        Check.PassAll(match, 0);
        typeof(PokerNetworkSession).GetFields(System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
            .Single(f => f.FieldType == typeof(MischiefPokerMatch)).SetValue(a, match);
        h.Step(12);
        Check.That(!a.CanResume && a.CanRestart && a.State!.Continuation == PokerContinuation.Paused,
            "depleted table disables resume but allows restart");
        a.Restart(); h.Step(PokerNetworkSession.CollectionDuration + .2);
        Check.That(a.State!.Hand!.Public.Seats.All(s => s.Chips == 900) && a.State.Hand.Public.Dealer.PlayerId == a.State.Hand.PlayerId,
            "depleted table restarts with fresh test balances and the host dealer");
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
            a.Start(); h.Step(3.6); PassHand(h);
            h.Step(PokerNetworkSession.PayoutDuration(a.State!.Hand!.Public) + PokerNetworkSession.CollectionDuration + .2);
            Check.Equal(a.State!.Hand!.Public.HandNumber, 2L, "missing presentation acknowledgements do not interrupt a healthy room");
        }
    }

    private static void MalformedMessages()
    {
        using var h = Started();
        var valid = h.Sessions["b"].State!;
        foreach (var bytes in new[] { Array.Empty<byte>(), new byte[PokerNetworkProtocol.MaxBytes + 1], "{}"u8.ToArray(), "{bad}"u8.ToArray() })
            Check.That(PokerNetworkProtocol.Decode(bytes) == null, "malformed or oversized message is rejected");
        foreach (var state in new[] { valid with { Players = null! }, valid with { DealRemaining = double.NaN },
                     valid with { SettlementElapsed = double.NaN }, valid with { Continuation = (PokerContinuation)99 },
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

    private static void CompanionWinningsRemainValid()
    {
        var match = new MischiefPokerMatch([1, 2, 3, 4], 1);
        int[][] holes = [Check.Cards("As Ks"), Check.Cards("2c 3c"), Check.Cards("4c 5c"),
            Check.Cards("6c 7c"), Check.Cards("8c 9c"), Check.Cards("Tc Jc")];
        const string board = "Qs Js Ts 6d 7d";
        for (int hand = 0; hand < 10; hand++)
        {
            Check.Equal(match.StartHand(hand, Check.Deck(holes.Take(4).ToArray(), board)), PokerError.None, "original companions fund ten hands");
            Check.PassAll(match, hand);
        }
        Check.Equal(match.GetPublicView().Seats.Single(s => s.PlayerId == 1).Chips, 4000L, "human legitimately wins all original companion chips");
        int[] humans = [1, 5, 6, 7, 8, 9];
        for (int hand = 10; hand < 20; hand++)
        {
            Check.Equal(match.StartHand(hand, Check.Deck(holes, board), newPlayers: hand == 10 ? humans.Skip(1).ToArray() : null,
                activePlayers: humans, retiredPlayers: hand == 10 ? [2, 3, 4] : null), PokerError.None, "fresh human bankrolls coexist with retained companion winnings");
            Check.PassAll(match, hand);
        }
        var view = match.GetPlayerView(1);
        Check.Equal(view.Public.Seats.Single(s => s.PlayerId == 1).Chips, 9000L, "total funding includes six humans and the three retired original companions");
        var peers = humans.Select(id => new PokerPeer(id.ToString(), "t" + id, id, "player", false)).ToArray();
        var room = new PokerRoomContext("room", "1", "1", peers);
        var state = new PokerNetworkState(match.SessionId, 1, PokerTableStatus.Playing,
            humans.Select(id => new PokerParticipant(id.ToString(), "t" + id, id, true, true, false)).ToArray(), view, 0, "");
        var packet = new PokerPacket(PokerNetworkProtocol.Version, "room", "t1", "t1", "state", 1, match.SessionId, State: state);
        Check.That(PokerNetworkProtocol.ValidState(PokerNetworkProtocol.Decode(PokerNetworkProtocol.Encode(packet))!.State!, room),
            "serialized results accept legal accumulated companion winnings above the old six-player starting total");
        var impossible = view with { Public = view.Public with { Seats = view.Public.Seats.Select(s => s.PlayerId == 1 ? s with { Chips = 9001 } : s).ToArray() } };
        Check.That(!PokerNetworkProtocol.ValidState(state with { Hand = impossible }, room), "funding validation remains bounded after allowing companion winnings");
    }

    private static void CompanionRetirementBoundary()
    {
        using var h = new Hub("a");
        h.Bot("bot1"); h.Bot("bot2"); h.AutomaticRetirement = true; h.Step(1.2);
        var a = h.Sessions["a"];
        h.CapacityReady = false;
        Check.That(!a.CanStart && !a.Start(), "initial deal waits for native capacity protection");
        h.CapacityReady = true;
        Check.That(a.Start(), "owner starts with real companions"); h.Step(3.2);
        h.Human("b"); h.Human("c"); h.Human("d"); h.Step();
        Check.Equal(h.Members.Count, 6, "current hand may temporarily fill all six visible slots");
        Check.That(a.State!.Players.Count(p => p.Companion) == 2 && a.State.Players.Count(p => p.Ready) == 3,
            "three spectators neither replace bots mid-hand nor count as willing players");
        a.SetPauseRequested(true);
        for (int i = 0; i < 500 && a.State.Hand!.Public.Phase != PokerPhase.Settled; i++)
        {
            if (a.State.Hand!.Public.ActivePlayerId == a.State.Hand.PlayerId) a.Act(PokerAction.EndTurn);
            h.Step(.1);
        }
        var settled = a.State.Hand!.Public;
        Check.Equal(settled.Phase, PokerPhase.Settled, "companions complete their own current hand");
        long balance = settled.Seats.Single(s => s.PlayerId == a.State.Hand.PlayerId).Chips;
        h.Step(Math.Max(0, PokerNetworkSession.PayoutDuration(settled) - a.State.SettlementElapsed - .15));
        Check.Equal(h.Members.Count, 6, "payout is not cut short by bot retirement");
        h.Step(.3);
        Check.That(a.IsPaused && h.Members.Count == 5 && h.Members.Count(m => m.Value.Bot) == 1,
            "after payout reserve an open slot while retaining one opponent");
        Check.Equal(a.State.RetiredCompanions!.Length, 1, "historical result retains retired companion's paid hand");
        Check.That(h.Sessions["b"].State!.RetiredCompanions!.Length == 1,
            "serialized recipient accepts historical result after lobby membership removal");
        Check.Equal(a.State.Hand!.Public.Seats.Single(s => s.PlayerId == a.State.Hand.PlayerId).Chips, balance,
            "retirement neither refunds nor resets the human balance");
        var table = a.State.Table;
        h.CapacityReady = false;
        Check.That(!a.CanResume && !a.CanRestart && !a.Resume() && !a.Restart(), "native capacity retry blocks both pause exits");
        h.Inject("a", "restart", hand: settled.HandNumber); h.Step();
        Check.That(a.State.Table == table && a.IsPaused, "forged restart cannot reset a table while capacity is unsafe");
        h.CapacityReady = true;
        h.Human("e"); h.Step();
        Check.That(h.Members.Count == 5 && h.Members.All(m => !m.Value.Bot) && !a.CanResume,
            "five unwilling humans force the final bot out; insufficient participation pauses safely");
        h.Sessions["b"].SetReady(true); h.Step();
        Check.That(a.CanResume && a.Resume(), "a spectator's own seat request makes continuation possible");
        h.Step(PokerNetworkSession.CollectionDuration + .3);
        var b = h.Sessions["b"];
        Check.Equal(b.State!.Hand!.HoleCards.Count, 2, "admitted spectator receives their own two private cards");
        Check.Equal(b.State.Hand.Public.Seats.Single(s => s.PlayerId == b.State.Hand.PlayerId).Chips, 900L,
            "new human starts with own 1000 balance, not retired companion's balance");
        Check.Equal(a.State!.Hand!.Public.Seats.Single(s => s.PlayerId == a.State.Hand.PlayerId).SeatNumber, 1,
            "remaining player's seat number is stable across companion retirement");
        Check.That(a.State.RetiredCompanions!.Length == 0 && a.State.Hand.Public.Seats.Count == 2,
            "next deal discards retired ledger seats and historical projections atomically");
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
        public string Game = "mischief";
        public bool GameChangePending;
        public bool AutomaticRetirement;
        public bool CapacityReady = true;
        public string[] Reconcile(int minimum, string[] preferred)
        {
            if (!AutomaticRetirement) return [];
            int humans = Members.Count(m => !m.Value.Bot);
            int keep = Math.Min(Math.Max(0, 5 - humans), Math.Max(minimum, 3 - humans));
            var retired = Members.Where(m => m.Value.Bot).OrderBy(m => preferred.Contains(m.Key) ? 1 : 0)
                .Take(Math.Max(0, Members.Count(m => m.Value.Bot) - keep)).Select(m => m.Key).ToArray();
            foreach (var key in retired) Members.Remove(key);
            return retired;
        }
        public readonly HashSet<string> Accepted = new();
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
            => new(PokerNetworkProtocol.Version, "room:" + Game, Members[sender].Ticket, Members[Owner].Ticket,
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
        public PokerRoomContext? Context => hub.Members.ContainsKey(key) ? new("room:" + hub.Game, key, hub.Owner,
            hub.Members.Select((m, i) => new PokerPeer(m.Key, m.Value.Ticket, m.Key == key ? 1 : i + 20, m.Key, m.Value.Bot, hub.Accepted.Contains(m.Key))).ToArray(), hub.Game, hub.GameChangePending, hub.Reconcile, hub.CapacityReady) : null;
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
