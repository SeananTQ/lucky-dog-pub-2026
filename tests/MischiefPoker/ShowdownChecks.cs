using LuckyDogRise.Rooms.Poker;

internal static class ShowdownChecks
{
    public static void Run()
    {
        var bear = Settle("Ts 5s 9d 9s 5c", "Th 2h", "Td Kc", "7h 8d");
        var losing = Explain(bear, 1);
        Check.Sequence(losing.LocalCards.Order(), Check.Cards("Th Ts 9d 9s 5s").Order(), "local green includes left public 5, not hole 2");
        Check.Sequence(losing.WinnerCards.Order(), Check.Cards("Td Ts 9d 9s Kc").Order(), "bear's full two pair plus decisive K");
        Check.Equal(losing.DecisiveCard, (int?)Check.Cards("Kc")[0], "winner's K animates");
        var own = Explain(bear, 2);
        Check.Equal(own.DecisiveCard, null, "winning client does not compare losers");
        Check.Equal(own.WinnerCards.Count, 4, "sole winning client sees only the two-pair core");
        Check.That(own.WinnerCards.SetEquals(own.LocalCards), "own bright and green agree");
        var spectator = Explain(bear, 100);
        Check.Equal(spectator.LocalCards.Count, 0, "spectator has no green mask");
        Check.Equal(spectator.DecisiveCard, null, "spectator does not compare kickers");

        var pair = Settle("Ah As 9d 7s 3c", "Kc Jh", "Kd Tc", "Qd 8c");
        Check.Equal(Explain(pair, 2).DecisiveCard, (int?)Check.Cards("Jh")[0], "second-place viewer compares J vs T");
        Check.Equal(Explain(pair, 3).DecisiveCard, (int?)Check.Cards("Kc")[0], "third-place viewer compares K vs Q");
        Check.Equal(Explain(pair, 3).WinnerCards.Count, 3, "pair core plus one necessary kicker");
        var twoPair = Settle("Ah As Kc 9d 3s", "Kd Jh", "Kh Tc", "Qc Qd");
        Check.Equal(Explain(twoPair, 2).DecisiveCard, (int?)Check.Cards("Jh")[0], "same two pairs compare kicker");
        Check.Equal(Explain(twoPair, 3).DecisiveCard, null, "different lower pair suppresses misleading smaller kicker");
        Check.Equal(Explain(twoPair, 3).LocalCards.Count, 4, "different two pair outlines core only");

        var trips = Settle("Ah As Ad 9c 3s", "Kc Jh", "Kh Tc");
        Check.Equal(Explain(trips, 2).DecisiveCard, (int?)Check.Cards("Jh")[0], "triples compare second kicker when first ties");
        var quads = Settle("Ah As Ad Ac 3s", "Kc Jh", "Qh Tc");
        Check.Equal(Explain(quads, 2).DecisiveCard, (int?)Check.Cards("Kc")[0], "quads compare their kicker");
        var straight = Settle("5h 6s 7d 9c Ks", "8c 2d", "3h 4s");
        Check.Equal(Explain(straight, 2).DecisiveCard, null, "different straights have no bounce or shrink");
        Check.Equal(Explain(straight, 2).WinnerCards.Count, 5, "whole straight shown");
        var high = Settle("Ah 3s 7d 9c Js", "Kc 2d", "Qh 4s");
        Check.Equal(Explain(high, 2).DecisiveCard, null, "high card does not bounce");
        Check.Equal(Explain(high, 2).LocalCards.Count, 5, "high card shows full five");
        var category = Settle("5h 6s 7d 9c Ks", "8c 2d", "Kh 4s");
        Check.Equal(Explain(category, 2).DecisiveCard, null, "different categories do not compare isolated cards");

        var tie = Settle("Ah As Jc 9h 3d", "Kc 2c", "Kd 4d", "Qh 8d");
        Check.Equal(tie.Settlement!.Winners.Count, 2, "two equal winners fixture");
        foreach (int winner in tie.Settlement.Winners)
        {
            var tiedLocal = PokerShowdownPresentation.Create(tie, 1, winner)!;
            Check.Equal(tiedLocal.LocalCards.Count, 5, "tied local sees full best five");
            Check.Equal(tiedLocal.WinnerCards.Count, 5, "tied local sees current winner's full five");
            Check.Equal(tiedLocal.DecisiveCard, null, "tie never invents a deciding card");
            var tiedOther = PokerShowdownPresentation.Create(tie, 3, winner)!;
            Check.That(tiedOther.DecisiveCard != null, "loser still gets explanation for each tied winner");
        }
        var boardTie = Settle("Ac Tc Jc Qc Kc", "2h 3d", "4s 5h", "6s 7d");
        Check.That(Explain(boardTie, 1).LocalCards.SetEquals(boardTie.Board.Select(c => c.Card)), "board-only tie leaves both hole cards unused");
        Check.Equal(PokerShowdownPresentation.Create(bear with { Phase = PokerPhase.Acting, Settlement = null }, 1, 2), null,
            "no masks before public settlement");

        var sequence = new PokerShowdownSequence();
        var view = new PokerPlayerView(tie, 3, Check.Cards("Qh 8d"));
        sequence.Update(view, 0);
        long cue = sequence.Cue;
        Check.Equal(sequence.Current!.WinnerId, tie.ActionOrder.First(tie.Settlement.Winners.Contains), "winner order follows fixed action order");
        for (int i = 0; i < 50; i++) sequence.Update(view, 0);
        Check.Equal(sequence.Cue, cue, "repeated/paused snapshots cannot replay payout");
        Check.That(!sequence.Revealed, "reveal delay is preserved while paused");
        for (int i = 0; i < 29; i++) sequence.Update(view, 0.1);
        Check.Equal(sequence.Index, 1, "second tied winner follows first animation and dwell");
        for (int i = 0; i < 80; i++) sequence.Update(view, 0.1);
        Check.That(sequence.Finished, "sequence finishes and holds final winner without looping");
        long completeCue = sequence.Cue;
        sequence.Update(view, double.NaN);
        sequence.Update(view, double.PositiveInfinity);
        sequence.Update(view, 60);
        Check.Equal(sequence.Cue, completeCue, "invalid deltas or long stalls cannot replay a completed payout");
        sequence.Update(view with { Public = tie with { HandNumber = tie.HandNumber + 1, Phase = PokerPhase.Acting, Settlement = null } }, 0);
        Check.Equal(sequence.Current, null, "new hand clears settled emphasis");
        sequence.Update(view with { Public = tie with { SessionId = Guid.NewGuid() } }, 0);
        Check.Equal(sequence.Index, 0, "new session restarts even with the same hand number");

        var random = new Random(724);
        for (int i = 0; i < 1000; i++)
        {
            var seven = Enumerable.Range(0, 52).OrderBy(_ => random.Next()).Take(7).ToArray();
            var best = PokerShowdownPresentation.SelectBest(seven.Take(5).Select(c => new PokerBoardCard(c, false)).ToArray(), seven.Skip(5).ToArray());
            Check.Equal(PokerHandEvaluator.EvaluateFive(best), PokerHandEvaluator.EvaluateSeven(seven).Value, "visual choice preserves authoritative best-five strength");
        }
        Console.WriteLine("PASS per-viewer showdown masks, deterministic public slots, kickers, ties and presentation lifecycle");
    }

    private static PokerShowdownEmphasis Explain(PokerPublicView state, int viewer)
        => PokerShowdownPresentation.Create(state, viewer, state.Settlement!.Winners[0])!;
    private static PokerPublicView Settle(string board, params string[] holes)
    {
        var match = new MischiefPokerMatch(Enumerable.Range(1, holes.Length).ToArray(), 1);
        Check.Equal(match.StartHand(0, Check.Deck(holes.Select(Check.Cards).ToArray(), board)), PokerError.None, "showdown fixture starts");
        Check.PassAll(match, 0);
        return match.GetPublicView();
    }
}
