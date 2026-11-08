using LuckyDogRise.Rooms;
using LuckyDogRise.Rooms.Poker;

internal static class PreviewChecks
{
    public static void Run()
    {
        for (int count = 2; count <= 6; count++)
        for (int preset = 0; preset <= 8; preset++)
        {
            var players = Enumerable.Range(1, count).ToArray();
            var session = new MischiefPokerPreviewSession(players, 2, 1) { NextDealPreset = preset };
            session.NextHand();
            Check.Equal(session.LastError, PokerError.None, "preview preset accepts a valid 52-card deck");
            Check.Equal(session.View.Public.Pot, count * 100L, "preview ante");
            session.Paused = true;
            var before = session.View.Public;
            session.Tick(100);
            Check.Equal(session.View.Public.SecondsRemaining, before.SecondsRemaining, "paused clock remains stopped");
            Check.Equal(session.View.Public.Revision, before.Revision, "paused AI does not act");
            session.Paused = false;
            Finish(session, preset == 0);
            var result = session.View.Public.Settlement!;
            if (preset == 1) Check.Equal(result.Winners.Count, count, "royal-flush board preset ties all players");
            if (preset == 2) Check.Sequence(result.Winners, new[] { 2 }, "dealer preset wins without changing board");
            if (preset == 3) Check.Sequence(result.Winners, new[] { 1 }, "other-winner preset transfers dealer");
            if (preset is 4 or 5 or 6) Check.Sequence(result.Winners, new[] { 2 }, "explanation fixture has the expected remote winner");
            if (preset == 7) Check.Sequence(result.Winners.Order(), players.Skip(1).Take(2), "other winners tie while the local player loses");
            if (preset == 8) Check.Sequence(result.Winners, new[] { 1 }, "local sole winner fixture");
            Check.Equal(session.View.Public.Seats.Sum(s => s.Chips) + session.View.Public.TotalSystemTips, count * 1000L,
                "preview conserved balance including system tips");
            session.NextHand();
            Check.Equal(session.View.Public.HandNumber, 2L, "next hand preserves session");
            int expired = session.View.Public.ActivePlayerId!.Value;
            session.ExpireTurn();
            Check.That(session.View.Public.CompletedTurns.Any(t => t.PlayerId == expired && t.Reason == PokerTurnEnd.TimedOut),
                "debug time advancement uses normal timeout path");
        }
        var streak = new MischiefPokerPreviewSession(new[] { 1, 2, 3 }, 2, 1) { NextDealPreset = 2 };
        for (int i = 0; i < 4; i++) { streak.NextHand(); Finish(streak, false); }
        Check.Equal(streak.View.Public.Settlement!.DealerChange, PokerDealerChange.StreakLimit, "dealer preset reaches forced rotation");
        for (int i = 0; i < 50; i++)
        {
            var session = new MischiefPokerPreviewSession(new[] { 1, 2, 3, 4, 5, 6 }, 1, 1);
            session.NextHand();
            Finish(session, true);
            Check.That(session.View.Public.Settlement != null, "automatic bots complete full table");
        }
        Console.WriteLine("PASS local preview driver: presets, 2..6 seats, pause, automatic bots, timeout and dealer rotation");
    }

    private static void Finish(MischiefPokerPreviewSession session, bool automatic)
    {
        for (int steps = 0; session.View.Public.Phase != PokerPhase.Settled && steps < 50; steps++)
            if (session.View.Public.ActivePlayerId == 1) session.EndTurn();
            else if (automatic) session.Tick(1.3);
            else session.Step();
        Check.Equal(session.View.Public.Phase, PokerPhase.Settled, "preview hand finishes in bounded steps");
    }
}
