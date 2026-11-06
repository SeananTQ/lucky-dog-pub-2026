using System.Text.Json;
using LuckyDogRise.Rooms.Poker;

internal static class MatchChecks
{
    private static readonly int[] Ids = [17, -3, 42, -9, 81, 55];

    public static void Run()
    {
        ConstructionAndInvalidStarts();
        DealingAndPrivateViews();
        FlipsDrawAndSettlement();
        DeadlinesAndBadCommands();
        DealerTransitions();
        ChipsAndSystemTips();
        RandomizedSessions();
    }

    private static MischiefPokerMatch Make(int count = 4, MischiefPokerRules? rules = null, int firstDealerSeat = 0)
        => new(Ids.Take(count).ToArray(), Ids[firstDealerSeat], rules);

    private static int[] WinningDeck(int count = 4, int winnerSeat = 0)
    {
        var others = Check.Cards("2c 3d 2d 3h 2h 3s 2s 3c 5c 9d");
        int cursor = 0;
        var holes = Enumerable.Range(0, count).Select(seat => seat == winnerSeat
            ? Check.Cards("As Ah") : new[] { others[cursor++], others[cursor++] }).ToArray();
        return Check.Deck(holes, "Kc Jd 8h 6s 4c");
    }

    private static void ConstructionAndInvalidStarts()
    {
        Check.Throws<ArgumentException>(() => new MischiefPokerMatch([1], 1), "one participant unsupported by core");
        Check.Throws<ArgumentException>(() => new MischiefPokerMatch([1, 1], 1), "duplicate identity rejected");
        Check.Throws<ArgumentException>(() => new MischiefPokerMatch([1, 2], 3), "nonparticipating first dealer rejected");
        Check.Throws<ArgumentException>(() => new MischiefPokerMatch([1, 2, 3, 4, 5, 6, 7], 1), "room capacity respected");
        foreach (var invalid in new MischiefPokerRules[]
        {
            new() { InitialChips = 0 }, new() { InitialChips = long.MaxValue },
            new() { Ante = 0 }, new() { Ante = 1001 }, new() { TurnSeconds = double.NaN },
            new() { TurnSeconds = 0 }, new() { TurnSeconds = double.PositiveInfinity },
            new() { MaxConsecutiveDealerHands = 0 }
        }) Check.Throws<ArgumentOutOfRangeException>(() => Make(rules: invalid), "invalid tuning rejected");

        var game = Make();
        string before = JsonSerializer.Serialize(game.GetPublicView());
        foreach (var deck in new[] { Array.Empty<int>(), new int[52], Enumerable.Range(1, 52).ToArray() })
            Check.Equal(game.StartHand(0, deck), PokerError.InvalidDeck, "bad deck rejected before debiting");
        foreach (var time in new[] { -1, double.NaN, double.PositiveInfinity, double.MaxValue })
            Check.Equal(game.StartHand(time), PokerError.InvalidTime, "invalid start clock");
        Check.Equal(JsonSerializer.Serialize(game.GetPublicView()), before, "failed starts preserve all state");
        Check.Equal(game.StartHand(0), PokerError.None, "normal cryptographic shuffle starts");
        var publicView = game.GetPublicView();
        Check.Equal(game.StartHand(1), PokerError.HandInProgress, "cannot charge another ante mid-hand");
        Check.Equal(game.GetPublicView().Revision, publicView.Revision, "rejected start leaves revision unchanged");
    }

    private static void DealingAndPrivateViews()
    {
        int[] seats = Ids.Take(4).ToArray();
        var game = new MischiefPokerMatch(seats, seats[2]);
        seats[0] = 999;
        Check.Equal(game.GetPublicView().Seats[0].PlayerId, Ids[0], "constructor snapshots seat order");
        Check.Equal(game.GetPlayerView(Ids[0]).HoleCards.Count, 0, "no private cards before deal");
        var deck = Enumerable.Range(0, 52).ToArray();
        Check.Equal(game.StartHand(10, deck), PokerError.None, "start fixed deck");
        Array.Fill(deck, 51);
        var view = game.GetPublicView();
        Check.Sequence(view.ActionOrder, new[] { Ids[3], Ids[0], Ids[1], Ids[2] }, "owner can occupy any fixed seat");
        Check.Sequence(view.Board.Select(card => card.Card), Enumerable.Range(8, 5), "five community cards start face up");
        Check.That(view.Board.All(card => !card.PendingReplacement), "no initial pending replacement");
        Check.Equal(view.Dealer.ConsecutiveHands, 1, "first hand counts as first dealer hand");
        Check.Equal(view.SecondsRemaining, 15d, "initial turn duration");
        Check.Equal(view.Pot, 400L, "all antes collected once");
        Check.That(view.Seats.All(seat => seat.Chips == 900), "initial balance minus ante");
        Check.That(view.Settlement == null && !JsonSerializer.Serialize(view).Contains("HoleCards"),
            "public projection contains no private hand before showdown");
        for (int seat = 0; seat < 4; seat++)
        {
            var playerView = game.GetPlayerView(Ids[seat]);
            Check.Sequence(playerView.HoleCards, new[] { seat, seat + 4 }, "each recipient receives only its own two cards");
            Check.Throws<NotSupportedException>(() => ((IList<int>)playerView.HoleCards)[0] = 51,
                "private projection cannot mutate authority state");
        }
        Check.Throws<ArgumentException>(() => game.GetPlayerView(999), "unknown recipient cannot request a hand");
        Check.Throws<NotSupportedException>(() => ((IList<int>)view.ActionOrder)[0] = 999, "public arrays are read-only");
        Check.Equal(game.Act(Check.Command(game, PokerAction.Flip, 0), 11), PokerError.None, "flip through command path");
        Check.That(!view.Board[0].PendingReplacement && game.GetPublicView().Board[0].PendingReplacement,
            "old snapshots do not mutate when authority progresses");
    }

    private static void FlipsDrawAndSettlement()
    {
        var game = Make();
        var deck = Enumerable.Range(0, 52).ToArray();
        Check.Equal(game.StartHand(0, deck), PokerError.None, "start flip test");
        var original = game.GetPublicView();
        var flip = Check.Command(game, PokerAction.Flip, 0);
        Check.Equal(game.Act(flip, 1), PokerError.None, "first flip");
        Check.Equal(game.Act(flip, 1), PokerError.StaleRevision, "duplicate cannot flip back or consume a second move");
        Check.Equal(game.GetPublicView().FlipsRemaining, 1, "duplicate does not consume action");
        Check.Equal(game.GetPublicView().Board[0].Card, original.Board[0].Card, "face-down keeps original card");
        Check.Equal(game.Act(Check.Command(game, PokerAction.Flip, 0), 2), PokerError.None, "same slot twice is legal");
        Check.That(!game.GetPublicView().Board[0].PendingReplacement, "flip back restores original");
        Check.Equal(game.GetPublicView().ActivePlayerId, Ids[2], "two flips advance exactly one player");
        Check.Equal(game.GetPublicView().CompletedTurns[0].Reason, PokerTurnEnd.TwoFlipsUsed, "two-flip completion recorded");
        Check.Equal(game.Act(Check.Command(game, PokerAction.Flip, 1), 3), PokerError.None, "second player marks replacement");
        Check.Equal(game.Act(Check.Command(game, PokerAction.EndTurn), 4), PokerError.None, "one flip then end");
        Check.Equal(game.GetPublicView().CompletedTurns[1].FlipsUsed, 1, "unused opportunity forfeited");
        Check.Equal(game.Act(Check.Command(game, PokerAction.Flip, 1), 5), PokerError.None, "later player undoes predecessor");
        Check.Equal(game.Act(Check.Command(game, PokerAction.EndTurn), 6), PokerError.None, "end remaining move");
        Check.Equal(game.GetPublicView().ActivePlayerId, Ids[0], "dealer acts last");
        Check.Equal(game.Act(Check.Command(game, PokerAction.Flip, 2), 7), PokerError.None, "dealer first flip");
        Check.Equal(game.Act(Check.Command(game, PokerAction.Flip, 4), 8), PokerError.None, "dealer second flip");
        Check.Equal(game.GetPublicView().Phase, PokerPhase.AwaitingDraw, "last action precedes draw presentation");
        Check.Sequence(game.GetPublicView().Board.Select(card => card.Card), original.Board.Select(card => card.Card),
            "no new card is drawn during action turns");
        Check.Equal(game.ResolveHand(), PokerError.None, "uniform final draw");
        var settled = game.GetPublicView();
        Check.Sequence(settled.Settlement!.Replacements.Select(item => item.Slot), new[] { 2, 4 }, "only final face-down slots replaced");
        Check.Sequence(settled.Board.Select(card => card.Card), new[] { 8, 9, 13, 11, 14 }, "draws fresh undealt cards in slot order");
        Check.That(settled.Board.All(card => !card.PendingReplacement), "all cards face up at showdown");
        Check.That(settled.Settlement.Players.All(player => player.HoleCards.Count == 2), "hands reveal only after settlement");
        Check.Sequence(game.GetPlayerView(Ids[0]).HoleCards, new[] { 0, 4 }, "hole cards remain unchanged");
        string result = JsonSerializer.Serialize(settled);
        Check.Equal(game.ResolveHand(), PokerError.NoPendingDraw, "duplicate settlement rejected");
        Check.Equal(JsonSerializer.Serialize(game.GetPublicView()), result, "duplicate cannot pay winners twice");

        var unchanged = Make();
        Check.Equal(unchanged.StartHand(0, WinningDeck()), PokerError.None, "start pass-only hand");
        Check.PassAll(unchanged, 0);
        Check.Equal(unchanged.GetPublicView().Settlement!.Replacements.Count, 0, "all face up means no replacement");
        Check.That(unchanged.GetPublicView().CompletedTurns.All(turn => turn.FlipsUsed == 0), "passing both moves is allowed");

        var allFive = Make(3);
        Check.Equal(allFive.StartHand(0, Enumerable.Range(0, 52).ToArray()), PokerError.None, "all-five replacement scenario");
        for (int slot = 0; slot < 5; slot++)
            Check.Equal(allFive.Act(Check.Command(allFive, PokerAction.Flip, slot), 0), PokerError.None, "mark a distinct slot");
        Check.PassAll(allFive, 0);
        Check.Equal(allFive.GetPublicView().Settlement!.Replacements.Count, 5, "all five can be replaced at settlement");
        Check.Sequence(allFive.GetPublicView().Board.Select(card => card.Card), Enumerable.Range(11, 5), "no old card reused in full redraw");
    }

    private static void DeadlinesAndBadCommands()
    {
        var game = Make();
        Check.Equal(game.StartHand(0, WinningDeck()), PokerError.None, "start deadline test");
        var correct = Check.Command(game, PokerAction.Flip, 0);
        (PokerCommand Command, PokerError Error)[] invalid =
        [
            (correct with { SessionId = Guid.NewGuid() }, PokerError.WrongSession),
            (correct with { HandNumber = 9 }, PokerError.StaleHand),
            (correct with { ExpectedRevision = 99 }, PokerError.StaleRevision),
            (correct with { PlayerId = Ids[0] }, PokerError.NotYourTurn),
            (correct with { PlayerId = 999 }, PokerError.NotYourTurn),
            (correct with { Slot = -1 }, PokerError.InvalidSlot),
            (correct with { Slot = 5 }, PokerError.InvalidSlot),
            (correct with { Action = (PokerAction)99 }, PokerError.InvalidAction)
        ];
        string before = JsonSerializer.Serialize(game.GetPublicView());
        foreach (var bad in invalid) Check.Equal(game.Act(bad.Command, 0), bad.Error, "reject invalid action");
        Check.Equal(JsonSerializer.Serialize(game.GetPublicView()), before, "bad commands have no gameplay effects");
        Check.Equal(game.Act(correct, 14), PokerError.None, "first flip just before deadline");
        Check.Equal(game.GetPublicView().TurnDeadline, 15d, "first move never refreshes deadline");
        var late = Check.Command(game, PokerAction.Flip, 1);
        Check.Equal(game.Act(late, 15), PokerError.StaleRevision, "exact deadline expires before action");
        Check.Equal(game.GetPublicView().ActivePlayerId, Ids[2], "next player after timeout");
        Check.That(game.GetPublicView().Board[0].PendingReplacement && !game.GetPublicView().Board[1].PendingReplacement,
            "timeout preserves earlier flip without applying late flip");
        Check.Equal(game.GetPublicView().CompletedTurns[0].Reason, PokerTurnEnd.TimedOut, "timeout reason retained");
        Check.Equal(game.AdvanceTo(1000), PokerError.None, "large time jump consumes overdue turns");
        Check.Equal(game.GetPublicView().Phase, PokerPhase.AwaitingDraw, "large jump finishes all turns");
        Check.Equal(game.GetPublicView().CompletedTurns.Count, 4, "every player acted or timed out once");
        foreach (var time in new[] { -1, 999, double.NaN, double.NegativeInfinity, double.PositiveInfinity })
            Check.Equal(game.AdvanceTo(time), PokerError.InvalidTime, "bad or backwards clock rejected");
        Check.Equal(game.ResolveHand(), PokerError.None, "timeouts retain eligibility for settlement");
        Check.Equal(game.StartHand(1000, WinningDeck()), PokerError.None, "next hand after settlement");
        Check.Equal(game.Act(late, 1000), PokerError.StaleHand, "previous-hand action cannot change next hand");
        Check.Equal(game.GetPublicView().SecondsRemaining, 15d, "new hand starts a fresh deadline");
        var earlyFinish = Make(2);
        Check.Equal(earlyFinish.StartHand(0), PokerError.None, "early finish timing scenario");
        Check.Equal(earlyFinish.Act(Check.Command(earlyFinish, PokerAction.EndTurn), 2), PokerError.None, "early turn end");
        Check.Equal(earlyFinish.GetPublicView().TurnDeadline, 17d, "next player's full 15 seconds starts at early end");
        Check.Equal(earlyFinish.ResolveHand(), PokerError.NoPendingDraw, "cannot expose cards while any player is still acting");

        var six = Make(6);
        Check.Equal(six.StartHand(0), PokerError.None, "six-player hand");
        Check.Equal(six.AdvanceTo(89.999), PokerError.None, "just before six timeouts");
        Check.Equal(six.GetPublicView().ActivePlayerId, Ids[0], "sixth player is dealer");
        Check.Equal(six.AdvanceTo(90), PokerError.None, "exact 90-second action budget");
        Check.Equal(six.GetPublicView().Phase, PokerPhase.AwaitingDraw, "six 15-second turns finish at 90");
    }

    private static void DealerTransitions()
    {
        var game = Make();
        for (int hand = 1; hand <= 4; hand++)
        {
            Check.Equal(game.StartHand(0, WinningDeck()), PokerError.None, "consecutive winning hand");
            Check.Equal(game.GetPublicView().Dealer.ConsecutiveHands, hand, "dealer count includes first hand");
            Check.PassAll(game, 0);
            var result = game.GetPublicView().Settlement!;
            Check.Equal(result.DealerChange, hand < 4 ? PokerDealerChange.Continued : PokerDealerChange.StreakLimit,
                "only fourth consecutive dealer win triggers forced change");
        }
        var next = game.GetPublicView().Settlement!.NextDealer;
        Check.Equal(next, new PokerDealer(Ids[1], 1, PokerDirection.Reverse), "old-next seat before reverse");
        Check.Equal(game.StartHand(0, WinningDeck()), PokerError.None, "start after forced change");
        Check.Sequence(game.GetPublicView().ActionOrder, new[] { Ids[0], Ids[3], Ids[2], Ids[1] },
            "forced-out dealer becomes first actor");

        var lostFourth = Make();
        for (int hand = 1; hand <= 4; hand++)
        {
            Check.Equal(lostFourth.StartHand(0, WinningDeck(winnerSeat: hand == 4 ? 2 : 0)), PokerError.None, "fourth-hand loss scenario");
            Check.PassAll(lostFourth, 0);
        }
        Check.Equal(lostFourth.GetPublicView().Settlement!.DealerChange, PokerDealerChange.Winner, "fourth-hand other winner takes precedence");
        Check.Equal(lostFourth.GetPublicView().Settlement!.NextDealer,
            new PokerDealer(Ids[2], 1, PokerDirection.Forward), "ordinary winner does not reverse direction");

        var tied = Make(3);
        int[] tieDeck = Check.Deck([Check.Cards("2c 3c"), Check.Cards("4c 5c"), Check.Cards("6c 7c")], "Th Jh Qh Kh Ah");
        for (int hand = 0; hand < 3; hand++)
        {
            Check.Equal(tied.StartHand(0, tieDeck), PokerError.None, "repeated tie hand");
            if (hand > 0) Check.Equal(tied.GetPublicView().ActivePlayerId, Ids[(hand - 1) % 2], "old dealer is first even after reverse-direction transfer");
            Check.PassAll(tied, 0);
            Check.Equal(tied.GetPublicView().Settlement!.DealerChange, PokerDealerChange.Tie, "tie forces change before streak limit");
            Check.Equal(tied.GetPublicView().Settlement!.NextDealer.PlayerId, Ids[hand % 2 == 0 ? 1 : 0],
                "repeated forced transfers follow agreed adjacent-seat behavior");
        }
        var tunable = Make(2, new() { MaxConsecutiveDealerHands = 1 });
        Check.Equal(tunable.StartHand(0, WinningDeck(2)), PokerError.None, "custom streak limit");
        Check.PassAll(tunable, 0);
        Check.Equal(tunable.GetPublicView().Settlement!.DealerChange, PokerDealerChange.StreakLimit, "limit is configurable");

        // Exhaust every starting seat and both directions, independent of IDs or the dog's visual position.
        for (int count = 2; count <= 6; count++)
        for (int first = 0; first < count; first++)
        {
            var forced = Make(count, new() { MaxConsecutiveDealerHands = 1 }, first);
            int dealerSeat = first;
            int direction = 1;
            for (int hand = 0; hand < 2; hand++)
            {
                Check.Equal(forced.StartHand(0, WinningDeck(count, dealerSeat)), PokerError.None, "forced transfer at arbitrary seat");
                Check.PassAll(forced, 0);
                int expectedNext = (dealerSeat + direction + count) % count;
                Check.Equal(forced.GetPublicView().Settlement!.NextDealer.PlayerId, Ids[expectedNext], "successor uses old direction");
                direction = -direction;
                Check.Equal((int)forced.GetPublicView().Settlement!.NextDealer.Direction, direction, "direction reverses after passing");
                int previous = dealerSeat;
                dealerSeat = expectedNext;
                Check.Equal((dealerSeat + direction + count) % count, previous, "previous dealer is first actor after transfer");
            }
        }
    }

    private static void ChipsAndSystemTips()
    {
        var game = Make(5, firstDealerSeat: 4);
        var deck = Check.Deck([Check.Cards("Tc 3d"), Check.Cards("Td 4h"), Check.Cards("Th 5c"),
            Check.Cards("6d 7h"), Check.Cards("8c 9d")], "Ac Kd Qh Js 2c");
        Check.Equal(game.StartHand(0, deck), PokerError.None, "500-chip pot");
        Check.PassAll(game, 0);
        var result = game.GetPublicView().Settlement!;
        Check.Sequence(result.Winners, Ids.Take(3), "only three highest players split pot");
        Check.That(result.Players.Take(3).All(player => player.Award == 166), "each tied winner gets integer share");
        Check.That(result.Players.Skip(3).All(player => player.Award == 0), "losers receive no share");
        Check.Equal(result.SystemTip, 2L, "remainder goes to system as dealer tip");
        Check.Equal(game.GetPublicView().TotalSystemTips, 2L, "system tip tracked separately");
        Check.Equal(game.GetPublicView().Seats.Sum(seat => seat.Chips) + game.GetPublicView().TotalSystemTips, 5000L,
            "player chips plus system collection conserve total");
        Check.Equal(result.NextDealer, new PokerDealer(Ids[0], 1, PokerDirection.Reverse), "tie forces losing dealer off too");
        Check.Equal(game.StartHand(0, deck), PokerError.None, "balances continue into next hand");
        Check.Equal(game.GetPublicView().Seats[0].Chips, 966L, "no initial stack refill on next hand");
        Check.PassAll(game, 0);
        Check.Equal(game.GetPublicView().TotalSystemTips, 4L, "tip totals accumulate across hands");

        var depleted = Make(2, new() { InitialChips = 100, Ante = 100 });
        Check.Equal(depleted.StartHand(0, WinningDeck(2)), PokerError.None, "exactly one ante available");
        Check.PassAll(depleted, 0);
        string before = JsonSerializer.Serialize(depleted.GetPublicView());
        Check.Equal(depleted.StartHand(0, WinningDeck(2)), PokerError.InsufficientChips, "insufficient ante blocks whole next hand");
        Check.Equal(JsonSerializer.Serialize(depleted.GetPublicView()), before, "no partial charges, refill, elimination or new hand");
        Check.That(depleted.GetPublicView().Seats.All(seat => seat.Chips >= 0), "balances never go negative");
    }

    private static void RandomizedSessions()
    {
        var random = new Random(20261107);
        int hands = 0;
        for (int count = 2; count <= 6; count++)
        for (int session = 0; session < 20; session++)
        {
            var game = Make(count);
            double now = 0;
            for (int round = 0; round < 20; round++)
            {
                int[] deck = Enumerable.Range(0, 52).OrderBy(_ => random.Next()).ToArray();
                var start = game.StartHand(now, deck);
                if (start == PokerError.InsufficientChips) break;
                Check.Equal(start, PokerError.None, "random hand starts");
                var before = game.GetPublicView();
                var originalHoles = Ids.Take(count).Select(id => game.GetPlayerView(id).HoleCards.ToArray()).ToArray();
                while (game.GetPublicView().Phase == PokerPhase.Acting)
                {
                    var view = game.GetPublicView();
                    Check.Equal(view.Seats.Sum(seat => seat.Chips) + view.Pot + view.TotalSystemTips, count * 1000L,
                        "conservation during actions");
                    if (random.Next(5) == 0)
                    {
                        now = view.TurnDeadline!.Value;
                        Check.Equal(game.AdvanceTo(now), PokerError.None, "random timeout");
                    }
                    else
                    {
                        now += Math.Min(.1, view.SecondsRemaining / 2);
                        var action = random.Next(3) == 0 ? PokerAction.EndTurn : PokerAction.Flip;
                        Check.Equal(game.Act(Check.Command(game, action, random.Next(5)), now), PokerError.None, "random legal action");
                    }
                    Check.Sequence(game.GetPublicView().Board.Select(card => card.Card), before.Board.Select(card => card.Card),
                        "random action never deals a replacement early");
                }
                var pending = game.GetPublicView().Board.Select(card => card.PendingReplacement).ToArray();
                Check.Equal(game.ResolveHand(), PokerError.None, "random settlement");
                var settled = game.GetPublicView();
                var result = settled.Settlement!;
                Check.Equal(settled.Seats.Sum(seat => seat.Chips) + settled.TotalSystemTips, count * 1000L,
                    "conservation after awards plus system tip");
                Check.Equal(result.Players.Sum(player => player.Award) + result.SystemTip, result.Pot, "pot fully accounted for");
                Check.Sequence(settled.CompletedTurns.Select(turn => turn.PlayerId), settled.ActionOrder, "each player has exactly one turn");
                Check.That(settled.CompletedTurns.All(turn => turn.FlipsUsed is >= 0 and <= 2), "two-move cap");
                var used = result.Players.SelectMany(player => player.HoleCards).Concat(settled.Board.Select(card => card.Card))
                    .Concat(result.Replacements.Select(replace => replace.OldCard)).ToArray();
                Check.Equal(used.Distinct().Count(), used.Length, "no duplicate or recycled card in hand");
                for (int seat = 0; seat < count; seat++)
                    Check.Sequence(game.GetPlayerView(Ids[seat]).HoleCards, originalHoles[seat], "private hand unchanged through round");
                for (int slot = 0; slot < 5; slot++)
                    Check.Equal(settled.Board[slot].Card != before.Board[slot].Card, pending[slot], "replace exactly pending slots");
                hands++;
            }
        }
        Check.That(hands > 1000, "long-run randomized coverage");
        Console.WriteLine($"Randomized completed hands: {hands} (2..6 players)");
    }
}
