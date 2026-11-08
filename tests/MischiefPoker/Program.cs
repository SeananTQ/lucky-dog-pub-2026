using System.Diagnostics;
using LuckyDogRise.Rooms.Poker;

try
{
    var watch = Stopwatch.StartNew();
    HandChecks.Run();
    Console.WriteLine($"PASS hand examples, kickers, best five and permutations ({Check.Count} checks)");
    MatchChecks.Run();
    PreviewChecks.Run();
    ShowdownChecks.Run();
    Console.WriteLine($"PASS game flow, deadlines, privacy, dealer changes and chip accounting ({Check.Count} checks)");
    HandChecks.ExhaustiveFiveCardCategories();
    Console.WriteLine($"MISCHIEF_POKER_PASS: {Check.Count} checks, all 2,598,960 five-card hands, {watch.Elapsed.TotalSeconds:F2}s");
}
catch (Exception error)
{
    // A failed assertion is a normal test failure, not an unhandled CLR crash/Windows error dialog.
    Console.Error.WriteLine("MISCHIEF_POKER_FAIL: " + error);
    Environment.ExitCode = 1;
}

internal static class Check
{
    public static int Count { get; private set; }
    public static void That(bool condition, string message)
    {
        Count++;
        if (!condition) throw new InvalidOperationException(message);
    }

    public static void Equal<T>(T actual, T expected, string message)
        => That(EqualityComparer<T>.Default.Equals(actual, expected), $"{message}: expected {expected}, got {actual}");
    public static void Sequence<T>(IEnumerable<T> actual, IEnumerable<T> expected, string message)
        => That(actual.SequenceEqual(expected), message);
    public static void Throws<T>(Action action, string message) where T : Exception
    {
        try { action(); } catch (T) { Count++; return; }
        throw new InvalidOperationException(message);
    }

    public static int[] Cards(string text) => text.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(token =>
    {
        int rank = "A23456789TJQK".IndexOf(token[0]);
        int suit = "cdhs".IndexOf(token[1]);
        if (token.Length != 2 || rank < 0 || suit < 0) throw new ArgumentException(text);
        return suit * 13 + rank;
    }).ToArray();

    public static int[] Deck(int[][] holes, string board, string replacements = "")
    {
        var prefix = Enumerable.Range(0, 2).SelectMany(pass => holes.Select(pair => pair[pass]))
            .Concat(Cards(board)).Concat(Cards(replacements)).ToArray();
        That(prefix.Distinct().Count() == prefix.Length, "test deck prefix must not duplicate cards");
        return prefix.Concat(Enumerable.Range(0, 52).Except(prefix)).ToArray();
    }

    public static PokerCommand Command(MischiefPokerMatch game, PokerAction action, int slot = -1)
    {
        var view = game.GetPublicView();
        return new(view.SessionId, view.HandNumber, view.Revision, view.ActivePlayerId!.Value, action, slot);
    }

    public static void PassAll(MischiefPokerMatch game, double now)
    {
        while (game.GetPublicView().Phase == PokerPhase.Acting)
            Equal(game.Act(Command(game, PokerAction.EndTurn), now), PokerError.None, "pass turn");
        Equal(game.GetPublicView().Phase, PokerPhase.AwaitingDraw, "all actions complete before drawing");
        Equal(game.ResolveHand(), PokerError.None, "settle hand");
    }
}
