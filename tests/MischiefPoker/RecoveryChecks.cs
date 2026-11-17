using System.Text.Json;
using LuckyDogRise.Rooms.Poker;

internal static partial class NetworkChecks
{
    private static void RecoveryAndAutoplay()
    {
        SnapshotRoundTrips();
        IdleAutoplayAndReclaim();
        AutoplayPassAndPartialActivity();
        ManualActivityBreaksIdleStreak();
        DisconnectionAndReturningIdentity();
        DepartedBankrollIsReserved();
        MigrationUsesNewestSurvivingCopy();
        ConsecutiveMigrationAndSettlement();
        MissingRecoveryFailsClosed();
        ConflictingRecoveryFailsClosed();
        RecreatedLinkAndInvalidRecovery();
        MissingPeerAndSpectatorHost();
        Console.WriteLine("PASS full recovery copies, migration, autoplay, identity rebinding and original bankroll");
    }
    private static void Until(Hub hub, Func<bool> predicate, string label, double limit = 120)
    {
        for (double t = 0; !predicate() && t < limit; t += .1) hub.Step(.1);
        Check.That(predicate(), label);
    }
    private static Hub ThreePlayers()
    {
        var hub = new Hub("a", "b", "c");
        hub.Sessions["b"].SetReady(true); hub.Sessions["c"].SetReady(true); hub.Step();
        Check.That(hub.Sessions["a"].Start(), "start recovery test table"); hub.Step(3.6); return hub;
    }
    private static void FinishRemaining(Hub h)
    {
        Until(h, () =>
        {
            var owner = h.Sessions[h.Owner];
            if (owner.State?.Hand?.Public.Phase == PokerPhase.Settled) return true;
            var current = h.Sessions.Values.FirstOrDefault(s => s.State?.Hand is { } v && v.PlayerId == v.Public.ActivePlayerId);
            if (current?.Connected == true && !current.IsAutoplay) current.Act(PokerAction.EndTurn);
            return false;
        }, "remaining manual and delegated turns finish once");
    }
    private static void SnapshotRoundTrips()
    {
        var core = new MischiefPokerMatch([10, 20, 30], 10);
        core.StartHand(0, Enumerable.Range(0, 52).ToArray());
        for (int i = 0; i < 7; i++)
        {
            var snapshot = JsonSerializer.Deserialize<PokerMatchSnapshot>(JsonSerializer.Serialize(core.ExportRecovery()))!;
            Check.That(MischiefPokerMatch.TryRestore(snapshot, out var restored), "complete snapshot restores every phase");
            Check.Equal(JsonSerializer.Serialize(restored!.GetPublicView()), JsonSerializer.Serialize(core.GetPublicView()), "import preserves public state");
            foreach (int id in new[] { 10, 20, 30 }) Check.Sequence(restored.GetPlayerView(id).HoleCards, core.GetPlayerView(id).HoleCards, "hidden hands survive takeover");
            if (core.GetPublicView().Phase == PokerPhase.Acting)
            {
                var c = Check.Command(core, PokerAction.Flip, i % 5);
                core.Act(c, i * .2); restored.Act(c, i * .2);
            }
            else { core.ResolveHand(); restored.ResolveHand(); }
            Check.Equal(JsonSerializer.Serialize(restored.GetPublicView()), JsonSerializer.Serialize(core.GetPublicView()), "restored deck and awards produce identical continuation");
        }
        var valid = core.ExportRecovery();
        foreach (var bad in new[] { valid with { Deck = new int[52] }, valid with { DeckIndex = 0 },
            valid with { Clock = double.NaN }, valid with { Hands = [] }, valid with { Public = valid.Public with { CompletedTurns = [] } } })
            Check.That(!MischiefPokerMatch.TryRestore(bad, out _), "invalid complete snapshots fail closed");
        Check.Equal(core.ResolveHand(), PokerError.NoPendingDraw, "restored settlement cannot be paid twice");
    }
    private static void IdleAutoplayAndReclaim()
    {
        using var h = Started(); var a = h.Sessions["a"]; var b = h.Sessions["b"];
        for (int hand = 1; hand <= 2; hand++)
        {
            Until(h, () => b.State!.Hand!.Public.Phase == PokerPhase.Acting && b.State.Hand.Public.HandNumber == hand && b.State.DealRemaining == 0,
                "next timed-out hand begins");
            Until(h, () =>
            {
                if (a.State!.Hand!.Public.ActivePlayerId == a.State.Hand.PlayerId) a.Act(PokerAction.EndTurn);
                return b.State!.Hand!.Public.Phase == PokerPhase.Settled;
            }, "idle player reaches normal deadline");
            Check.Equal(b.IsAutoplay, hand == 2, "exactly two entirely idle timeouts enable autoplay");
        }
        Until(h, () => b.State!.Hand!.Public.HandNumber == 3 && b.State.DealRemaining == 0
            && b.State.Hand.Public.ActivePlayerId == b.State.Hand.PlayerId, "away player gets another turn");
        Check.That(!b.Act(PokerAction.Flip, 0) && b.CanReclaim, "autoplay blocks direct actions but offers explicit reclaim");
        h.Step(4.4);
        Check.Equal(b.State!.Hand!.Public.ActivePlayerId, b.State.Hand.PlayerId, "autoplay waits five seconds");
        Check.That(b.Reclaim(), "explicit cancel accepted before delegated PASS"); h.Step();
        Check.That(!b.IsAutoplay && b.Act(PokerAction.Flip, 0), "reclaim keeps remaining turn and permits manual choice"); h.Step();
        Check.Equal(b.State!.Hand!.Public.FlipsRemaining, 1, "no turn or flips reset on reclaim");
        Check.Equal(b.State.Players.Single(p => p.Key == "b").IdleTurns, 0, "manual flip clears idle streak");
    }
    private static void ManualActivityBreaksIdleStreak()
    {
        using var h = Started(); var a = h.Sessions["a"]; var b = h.Sessions["b"];
        h.Step(15.1); a.Act(PokerAction.EndTurn); h.Step();
        Check.Equal(a.State!.Players.Single(p => p.Key == "b").IdleTurns, 1, "first timeout counted");
        Until(h, () => b.State!.Hand!.Public.HandNumber == 2 && b.State.DealRemaining == 0, "second hand dealt");
        // Dealer can change; advance other turns until this peer acts.
        Until(h, () => { if (a.State!.Hand!.Public.ActivePlayerId == a.State.Hand.PlayerId) a.Act(PokerAction.EndTurn);
            return b.State!.Hand!.Public.ActivePlayerId == b.State.Hand.PlayerId; }, "manual PASS turn");
        b.Act(PokerAction.EndTurn); h.Step();
        Check.That(!b.IsAutoplay && a.State!.Players.Single(p => p.Key == "b").IdleTurns == 0,
            "deliberate zero-flip PASS is activity, never AFK");
    }
    private static void AutoplayPassAndPartialActivity()
    {
        using var h = Started(); var a = h.Sessions["a"]; var b = h.Sessions["b"];
        void ReachTurn(int hand) => Until(h, () =>
        {
            if (a.State!.Hand!.Public.ActivePlayerId == a.State.Hand.PlayerId) a.Act(PokerAction.EndTurn);
            return b.State!.Hand!.Public.HandNumber == hand && b.State.DealRemaining == 0
                && b.State.Hand.Public.ActivePlayerId == b.State.Hand.PlayerId;
        }, "reach idle player's next decision");
        ReachTurn(1); h.Step(15.1);
        ReachTurn(2);
        Check.That(b.Act(PokerAction.Flip, 0), "partial activity accepts first flip"); h.Step(15.1);
        Check.That(!b.IsAutoplay && b.State!.Players.Single(p => p.Key == "b").IdleTurns == 0,
            "one manual flip followed by timeout breaks the idle streak");
        ReachTurn(3); h.Step(15.1);
        ReachTurn(4); h.Step(15.1);
        Check.That(b.IsAutoplay, "two fresh idle turns enable autoplay after partial activity");
        ReachTurn(5); h.Step(4.3);
        Check.Equal(b.State!.Hand!.Public.ActivePlayerId, b.State.Hand.PlayerId, "delegated turn does not PASS early");
        h.Step(.9);
        var turn = b.State.Hand.Public.CompletedTurns.Single(t => t.PlayerId == b.State.Hand.PlayerId);
        Check.Equal(turn.FlipsUsed, 0, "delegation never flips cards");
        Check.That(b.State.Hand.Public.ActivePlayerId != b.State.Hand.PlayerId && b.IsAutoplay,
            "five-second PASS advances once and preserves match-wide AFK control");
    }
    private static void DisconnectionAndReturningIdentity()
    {
        using var h = ThreePlayers(); var a = h.Sessions["a"]; var b = h.Sessions["b"];
        Check.That(b.Act(PokerAction.Flip, 1), "return test active player can flip"); h.Step();
        var hand = b.State!.Hand!; var bank = hand.Public.Seats.Single(s => s.PlayerId == hand.PlayerId).Chips;
        h.Remove("b"); h.Step(.3);
        Check.That(a.State!.DepartedPlayers!.Any(p => p.Key == "b") && a.State.Hand!.Public.Board[1].PendingReplacement,
            "leaving preserves accepted flips and the current hand's seat");
        int departedDisplayId = PokerNetworkProtocol.DisplayId(a.State, a.Context!, hand.PlayerId);
        Check.That(departedDisplayId < 0 && a.IsAutoplayForDisplayId(departedDisplayId)
            && a.DisplayView!.Public.ActionOrder.Contains(departedDisplayId),
            "departed hand keeps a distinct display identity with an autoplay marker");
        h.Human("b"); h.Step(.3); b = h.Sessions["b"];
        Check.That(b.IsAutoplay && !b.IsReady && b.State!.Hand!.PlayerId == hand.PlayerId, "fresh membership ticket rebinds original delegated seat");
        Check.Sequence(b.State!.Hand!.HoleCards, hand.HoleCards, "returned owner sees their original two cards");
        Check.Equal(b.State.Hand.Public.Seats.Single(s => s.PlayerId == hand.PlayerId).Chips, bank, "return never refills or charges chips");
        Check.That(b.Reclaim(), "returned player must explicitly cancel autoplay"); h.Step();
        Check.That(b.IsReady && !b.IsAutoplay && b.State!.Hand!.Public.FlipsRemaining == 1, "explicit reclaim keeps used flip");
        Check.That(!a.IsAutoplayForDisplayId(departedDisplayId)
            && !a.DisplayView!.Public.ActionOrder.Contains(departedDisplayId), "rejoining replaces the departed display identity without duplication");
        // A real transport outage without Lobby removal also delegates after the heartbeat deadline.
        h.Links["b"].Disconnected = true; h.Step(10.2);
        Check.That(a.State!.Players.Single(p => p.Key == "b").Control == PokerControl.Disconnected, "missing heartbeat delegates a still-listed peer");
        h.Links["b"].Disconnected = false; h.Step();
        Check.That(b.IsAutoplay, "network returning alone never steals control back");
    }
    private static void DepartedBankrollIsReserved()
    {
        using var h = ThreePlayers(); var a = h.Sessions["a"];
        int id = h.Sessions["b"].State!.Hand!.PlayerId;
        h.Remove("b"); h.Step(); FinishRemaining(h);
        long chips = a.State!.Hand!.Public.Seats.Single(s => s.PlayerId == id).Chips;
        Until(h, () => a.State!.Hand!.Public.HandNumber == 2, "other players continue without departed seat");
        h.Human("b"); h.Step(); var b = h.Sessions["b"];
        var received = PokerNetworkProtocol.Decode(h.Sent.Last(p => p.To == "b" && PokerNetworkProtocol.Decode(p.Bytes)?.Kind == "state").Bytes)!;
        Check.That(PokerNetworkProtocol.ValidState(received.State!, h.Links["b"].Context!), "return projection validates");
        Check.That(MischiefPokerMatch.TryRestore(received.Recovery!.Match, out _), "return deck snapshot validates");
        Check.That(PokerNetworkProtocol.ValidRecovery(received.Recovery), "return recovery wrapper validates");
        Check.That(!b.IsInHand && !b.IsAutoplay && b.SetReady(true), $"after completed hand return uses sit-in (connected={b.Connected}, state={b.State?.Status}, inHand={b.IsInHand}, autoplay={b.IsAutoplay}, collect={b.IsCollecting})"); h.Step();
        FinishRemaining(h);
        Until(h, () => b.State!.Hand!.Public.HandNumber == 3, "returning spectator joins next hand");
        Check.Equal(b.State!.Hand!.Public.Seats.Single(s => s.PlayerId == id).Chips, chips - 100, "reserved account balance survives absence without a fresh 1000 grant");
    }
    private static void MigrationUsesNewestSurvivingCopy()
    {
        using var h = ThreePlayers(); var b = h.Sessions["b"]; var c = h.Sessions["c"];
        b.Act(PokerAction.Flip, 0); h.Step();
        var oldEpoch = b.State!.Epoch; var table = b.State.Table;
        h.Links["b"].DropStates = true;
        b.Act(PokerAction.Flip, 1); h.Step();
        Check.That(c.State!.Hand!.Public.Board[1].PendingReplacement && !b.State.Hand!.Public.Board[1].PendingReplacement,
            "deliberately create uneven replica delivery");
        var latest = c.State.Hand.Public;
        h.Remove("a"); h.Owner = "b"; h.Links["b"].DropStates = false; h.Step(.1);
        Check.That(b.IsMigrating && c.IsMigrating && !b.Act(PokerAction.Flip, 2), "owner change locks both UI endpoints and authority commands");
        h.Step(1.2);
        Check.That(b.Connected && c.Connected && b.State!.Table == table && b.State.RestartRequired,
            "new owner restores the same match and schedules Beta restart");
        Check.Equal(b.State!.Hand!.Public.Revision, latest.Revision, "new owner uses newer surviving peer's action revision");
        Check.Sequence(b.State.Hand.Public.Board, latest.Board, "confirmed flips do not roll back to new owner's stale replica");
        Check.That(b.State.Epoch != oldEpoch, "authority epoch changes independently from lobby ticket");
        var before = b.State.Hand.Public.Revision;
        var stale = h.Packet("c", "action", new(latest.SessionId, latest.HandNumber, latest.Revision,
            c.State!.Hand!.PlayerId, PokerAction.Flip, 4)) with { Epoch = oldEpoch };
        h.Links["b"].Enqueue("c", PokerNetworkProtocol.Encode(stale)); h.Step();
        Check.Equal(b.State.Hand.Public.Revision, before, "old-epoch commands cannot mutate recovered hand");
        FinishRemaining(h); h.Step(PokerNetworkSession.PayoutDuration(b.State.Hand.Public) + .3);
        Check.That(b.IsPaused && !b.CanResume && b.CanRestart, "Beta recovered hand ends with restart only");
        b.Restart(); h.Step(PokerNetworkSession.CollectionDuration + .3);
        Check.That(b.State.Table != table && b.State.Hand!.Public.Seats.All(s => s.Chips == 900)
            && b.State.Hand.Public.Dealer.PlayerId == b.State.Hand.PlayerId, "new host restarts with clean bankroll and dealer");
    }
    private static void ConsecutiveMigrationAndSettlement()
    {
        using var h = ThreePlayers(); var c = h.Sessions["c"];
        var hand = c.State!.Hand!.Public;
        h.Remove("a"); h.Owner = "b"; h.Step(.15);
        h.Remove("b"); h.Owner = "c"; h.Step(1.2);
        Check.That(c.Connected && c.State!.Hand!.Public.SessionId == hand.SessionId
            && c.State.DepartedPlayers!.Length == 2, "two consecutive hosts can vanish before appointing any backup");
        FinishRemaining(h); var settled = c.State!.Hand!.Public;
        Check.Equal(settled.Seats.Sum(s => s.Chips) + settled.TotalSystemTips, 3000L, "departed players remain eligible and money is conserved");
        // Transfer ownership at settlement without removing the previous host.
        using var second = ThreePlayers(); FinishRemaining(second);
        var result = second.Sessions["a"].State!.Hand!.Public;
        second.Owner = "b"; second.Step(1.2);
        Check.Sequence(second.Sessions["b"].State!.Hand!.Public.Seats, result.Seats, "settlement migration never pays awards a second time");
        Check.Equal(second.Sessions["b"].State!.Hand!.Public.HandNumber, result.HandNumber, "migration never silently starts a new hand");
    }
    private static void MissingRecoveryFailsClosed()
    {
        using var h = new Hub("a"); h.Human("b"); h.Links["b"].DropStates = true; h.Step();
        h.Remove("a"); h.Owner = "b"; h.Step(1.2);
        Check.That(h.Sessions["b"].State is { Status: PokerTableStatus.Interrupted, Notice: "recovery_failed", Hand: null },
            "no complete copy aborts the match without fabricating hidden cards");
        Check.That(h.Members.ContainsKey("b"), "failed hand recovery does not destroy the room");
    }
    private static void ConflictingRecoveryFailsClosed()
    {
        using var h = ThreePlayers();
        var copy = h.Sessions["c"].RecoveryCopy!;
        h.Sessions["c"].Dispose();
        h.Links["c"] = new(h, "c");
        h.Sessions["c"] = new(h.Links["c"], recovery: copy with { Notice = "conflicting-copy" }, recoveryLocalKey: "c");
        h.Remove("a"); h.Owner = "b"; h.Step(1.3);
        Check.That(h.Sessions["b"].State is { Status: PokerTableStatus.Interrupted, Notice: "recovery_failed", Hand: null },
            "equal-version divergent copies abort instead of choosing an arbitrary recovery");
        Check.That(h.Sessions["c"].Connected && !h.Sessions["c"].IsMigrating,
            "failed recovery result releases the other member's blocking state");
    }
    private static void RecreatedLinkAndInvalidRecovery()
    {
        using var h = ThreePlayers();
        var old = h.Sessions["b"];
        var copy = old.RecoveryCopy!;
        string key = old.Context!.LocalKey;
        old.Dispose();
        h.Members["b"] = (Guid.NewGuid().ToString("N"), false);
        h.Links["b"] = new(h, "b");
        h.Sessions["b"] = new(h.Links["b"], recovery: copy, recoveryLocalKey: key);
        h.Remove("a"); h.Owner = "b"; h.Step(1.3);
        var b = h.Sessions["b"]; var c = h.Sessions["c"];
        Check.That(b.State?.Hand?.Public.SessionId == copy.Match!.Public.SessionId && c.Connected,
            "recreated Steam link retains same-account recovery through a new membership ticket");
        Check.That(b.IsAutoplay && b.Reclaim(), "recreated host explicitly reclaims its old delegated seat"); h.Step();
        var before = c.State!.Version;
        var state = PokerNetworkProtocol.Decode(h.Sent.Last(p => p.To == "c" && PokerNetworkProtocol.Decode(p.Bytes)?.Kind == "state").Bytes)!;
        var invalid = state with { State = state.State! with { Version = before + 100 },
            Recovery = state.Recovery! with { Players = null!, Version = before + 100 } };
        h.Links["c"].Enqueue("b", PokerNetworkProtocol.Encode(invalid)); h.Step(.02);
        Check.Equal(c.State!.Version, before, "malformed backup is rejected before UI or LINQ can throw");
        // A delayed transfer request from before the resumed state must not reopen a permanent overlay.
        var late = state with { Kind = "recovery_request", Sequence = 1, Epoch = Guid.NewGuid(), State = null, Recovery = null };
        h.Links["c"].Enqueue("b", PokerNetworkProtocol.Encode(late)); h.Step(.02);
        Check.That(!c.IsMigrating && c.Connected, "obsolete recovery request cannot re-lock a restored table");
        Check.That(PokerNetworkProtocol.Encode(state).Length < PokerNetworkProtocol.MaxBytes, "complete replica fits bounded Steam message");
    }
    private static void MissingPeerAndSpectatorHost()
    {
        using var h = ThreePlayers();
        h.Human("d"); h.Step(.6);
        var id = h.Sessions["a"].State!.Hand!.Public.SessionId;
        h.Remove("a"); h.Owner = "d"; h.Step(1.3);
        Check.That(h.Sessions["d"].Connected && !h.Sessions["d"].IsParticipant
            && h.Sessions["d"].State!.Hand!.Public.SessionId == id,
            "Steam can elect a spectator; all-member copy restores the hand without adding a seat");
        using var missing = ThreePlayers();
        missing.Remove("a"); missing.Owner = "b"; missing.Links["c"].Disconnected = true;
        missing.Step(PokerNetworkSession.MigrationTimeoutSeconds + .4);
        Check.That(missing.Sessions["b"].State?.Notice == "recovery_failed",
            "unreachable listed replica times out rather than guessing an older confirmed action");
    }
}
