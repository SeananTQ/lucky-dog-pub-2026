using LuckyDogRise.Rooms.Poker;

internal static class RoundPresentationChecks
{
    public static void Run()
    {
        var match = new MischiefPokerMatch(new[] { 1, 2, 3 }, 1);
        Check.Equal(match.StartHand(0), PokerError.None, "round fixture starts");
        var round = new PokerRoundPresentation();
        var before = match.GetPlayerView(1);
        round.Update(before, 0);
        int original = before.Public.Board[0].Card;
        Check.Equal(match.Act(Check.Command(match, PokerAction.Flip, 0), 0), PokerError.None, "pending replacement fixture");
        while (match.GetPublicView().Phase == PokerPhase.Acting)
            match.Act(Check.Command(match, PokerAction.EndTurn), 0);
        var awaiting = match.GetPlayerView(1);
        round.Update(awaiting, 0);
        Check.Equal(round.Stage, PokerRoundStage.FinalAction, "last actor gets a visual hold before draw");
        for (int i = 0; i < 20; i++) round.Update(awaiting, 0.1, cardsMoving: true);
        Check.Equal(round.StageSeconds, 0d, "last flip must finish before final feedback hold starts");
        Advance(round, awaiting, PokerRoundStage.RetractingResults);
        for (int i = 0; i < 30; i++) round.Update(awaiting, 0.1);
        Check.Equal(round.Stage, PokerRoundStage.RetractingResults, "slow authority never fabricates a replacement");
        Check.Equal(match.ResolveHand(), PokerError.None, "fixture settles");
        var settled = match.GetPlayerView(1);
        // A newly received settled snapshot must also walk the full reveal, not jump to winners.
        round = new PokerRoundPresentation();
        round.Update(settled, 0);
        Check.Equal(round.DisplayView!.Public.Board[0], new PokerBoardCard(original, true), "late result preserves the old back during final action");
        Check.That(round.DisplayView.Public.Settlement == null && round.DisplayView.Public.Seats.All(s => s.Chips == 900),
            "no future hole cards, rank, winners or payouts leak into pre-reveal UI");
        Check.That(!round.RequestNext() && !round.TakeNextHandRequest(), "early next cannot skip ceremony or charge an ante");
        Advance(round, settled, PokerRoundStage.Replacing);
        Check.That(round.DisplayView!.Public.Board[0].Card != original && round.DisplayView.Public.Settlement == null,
            "replacement may change the board before opponents' cards are revealed");
        Advance(round, settled, PokerRoundStage.Straightening);
        Check.That(!round.HandsRevealed && !round.RanksVisible && !round.WinnersVisible, "straightening still conceals results");
        Advance(round, settled, PokerRoundStage.RevealingHands);
        Check.That(round.HandsRevealed && !round.RanksVisible && !round.WinnersVisible, "faces flip before rank drawers and crowns");
        for (int i = 0; i < 20; i++) round.Update(settled, 0.1, cardsMoving: true);
        Check.Equal(round.Stage, PokerRoundStage.RevealingHands, "all remote flips must finish before rank display");
        Advance(round, settled, PokerRoundStage.Showdown);
        Check.That(round.RanksVisible && !round.WinnersVisible, "ranks precede winner presentation");
        Advance(round, settled, PokerRoundStage.AwaitingNext);
        Check.Sequence(round.DisplayView!.Public.Seats, settled.Public.Seats, "all payouts end at authoritative balances");
        long cue = round.Showdown.Cue;
        for (int i = 0; i < 100; i++) round.Update(settled, 0.1);
        Check.Equal(round.Stage, PokerRoundStage.AwaitingNext, "developer hold does not auto-continue");
        Check.Equal(round.Showdown.Cue, cue, "held result cannot replay awards");
        Check.That(round.RequestNext() && !round.RequestNext(), "exactly one next request starts collection");
        Check.That(!round.WinnersVisible && !round.RanksVisible && !round.TakeNextHandRequest(), "collection hides result drawers and waits before a new hand");
        Advance(round, settled, PokerRoundStage.AwaitingNewHand);
        Check.That(round.TakeNextHandRequest() && !round.TakeNextHandRequest(), "the authoritative next-hand request is single use");
        round.NextHandRejected();
        Check.Equal(round.Stage, PokerRoundStage.AwaitingNext, "failed next request returns to the result");
        Check.Equal(match.StartHand(2), PokerError.None, "fixture accepts new hand");
        round.Update(match.GetPlayerView(1), 0);
        Check.That(round.Stage == PokerRoundStage.Acting && round.Showdown.Current == null && !round.HandsRevealed,
            "new hand resets all old reveal and winner state");

        var tieMatch = new MischiefPokerMatch(new[] { 1, 2, 3 }, 1);
        tieMatch.StartHand(0, Check.Deck(new[] { Check.Cards("2h 3d"), Check.Cards("4s 5h"), Check.Cards("6s 7d") }, "Ac Tc Jc Qc Kc"));
        Check.PassAll(tieMatch, 0);
        var tie = tieMatch.GetPlayerView(1);
        var tieRound = new PokerRoundPresentation();
        tieRound.Update(tie, 0);
        Advance(tieRound, tie, PokerRoundStage.Replacing);
        double noReplacementTime = 0;
        while (tieRound.Stage == PokerRoundStage.Replacing) { tieRound.Update(tie, 0.04); noReplacementTime += 0.04; }
        Check.That(noReplacementTime >= PokerRoundPresentation.FlipSeconds && noReplacementTime <= 0.20,
            "all-front board waits a flip interval without changing cards");
        Check.Sequence(tieRound.DisplayView!.Public.Board, tie.Public.Board, "all-front ceremony preserves actual card values");
        Advance(tieRound, tie, PokerRoundStage.Showdown);
        while (!tieRound.Showdown.Revealed) tieRound.Update(tie, 0.1);
        Check.That(!tieRound.CanRequestNext && !tieRound.RequestNext(), "first tied payout cannot unlock next");
        Advance(tieRound, tie, PokerRoundStage.AwaitingNext);
        tieRound.RequestNext();
        Advance(tieRound, tie, PokerRoundStage.MovingDealer);
        Check.That(tieRound.DealerId == null && !tieRound.TakeNextHandRequest(), "moving dealer has no duplicate static badge and cannot deal early");
        Advance(tieRound, tie, PokerRoundStage.AwaitingNewHand);
        Check.Equal(tieRound.DealerId, tie.Public.Settlement!.NextDealer.PlayerId, "badge arrives at rules-selected dealer, including reversal");

        var low = tie with { Public = tie.Public with { Seats = tie.Public.Seats.Select((s, i) => s with { Chips = i == 0 ? 99 : s.Chips }).ToArray() } };
        var auto = new PokerRoundPresentation { PauseAfterPayout = false };
        auto.Update(low, 0); Advance(auto, low, PokerRoundStage.AwaitingNext);
        for (int i = 0; i < 30; i++) auto.Update(low, 0.1);
        Check.That(!auto.CanAffordNext && !auto.RequestNext() && auto.Stage == PokerRoundStage.AwaitingNext,
            "1-99 chips stop both automatic and manual continuation without top-up");
        auto.Update(tie, 0);
        Check.Equal(auto.Stage, PokerRoundStage.AwaitingNext, "hidden or zero-time presentation does not start automatic collection");
        auto.Update(tie, 0.1);
        Check.Equal(auto.Stage, PokerRoundStage.Collecting, "normal automatic path uses the same collection sequence when affordable");
        var frozen = auto.StageSeconds;
        auto.Update(tie, double.NaN); auto.Update(tie, double.PositiveInfinity); auto.Update(tie, -2);
        Check.Equal(auto.StageSeconds, frozen, "invalid elapsed time cannot skip transitions");
        auto.Update(tie with { Public = tie.Public with { SessionId = Guid.NewGuid() } }, 0);
        Check.Equal(auto.Stage, PokerRoundStage.FinalAction, "another session cannot inherit old collection or dealer flight");
        Console.WriteLine("PASS staged round disclosure, feedback hold, tie payouts, collection, dealer transfer and continuation guards");
    }

    private static void Advance(PokerRoundPresentation round, PokerPlayerView view, PokerRoundStage target)
    {
        for (int i = 0; i < 400 && round.Stage != target; i++) round.Update(view, 0.1);
        Check.Equal(round.Stage, target, "presentation reaches " + target);
    }
}
