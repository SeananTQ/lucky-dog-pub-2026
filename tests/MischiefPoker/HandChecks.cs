using DataTables;
using LuckyDogRise.Rooms.Poker;

internal static class HandChecks
{
    public static void Run()
    {
        string[] categories = ["As Kd 9h 5c 3s", "As Ah Kc 9d 3s", "As Ah Kc Kd 3s", "As Ah Ac Kd 3s",
            "2c 3d 4s 5h 6c", "As Js 9s 5s 3s", "As Ah Ac Kd Ks", "As Ah Ac Ad Ks",
            "5s 6s 7s 8s 9s", "Ts Js Qs Ks As"];
        PokerHandValue previous = default;
        for (int i = 0; i < categories.Length; i++)
        {
            var value = Five(categories[i]);
            Check.Equal((int)value.Rank, i + 1, "each hand category");
            Check.That(value.CompareTo(previous) > 0, "category precedence");
            previous = value;
        }

        // Exercise each comparison component, not just a category difference.
        (string High, string Low)[] comparisons =
        [
            ("As Ah Ac Ad Ks", "As Ah Ac Ad Qs"),
            ("Ks Kh Kc Qd Qh", "Qs Qh Qc Ad Ah"),
            ("As Ah Ac Kd Kh", "As Ah Ac Qd Qh"),
            ("As Ah Ac Kd 9h", "As Ah Ac Kd 8h"),
            ("As Ah Kd Kh 9c", "As Ah Qd Qh Kc"),
            ("As Ah Kd Kh 9c", "As Ah Kd Kh 8c"),
            ("As Ah Kc Qd 9s", "As Ah Kc Qd 8s"),
            ("As Ah Kc Qd 9s", "As Ah Kc Jd Ts"),
            ("As Ah Kc Qd 9s", "As Ah Qc Jd Ts"),
            ("As Js 9s 5s 4s", "As Js 9s 5s 3s"),
            ("As Kd 9h 5c 4s", "As Kd 9h 5c 3s"),
            ("2c 3d 4h 5s 6c", "Ac 2d 3h 4s 5c"),
            ("Ts Js Qs Ks As", "9s Ts Js Qs Ks")
        ];
        foreach (var pair in comparisons)
        {
            Check.That(Five(pair.High).CompareTo(Five(pair.Low)) > 0, $"comparison: {pair.High} > {pair.Low}");
            Check.That(Five(pair.Low).CompareTo(Five(pair.High)) < 0, "comparison antisymmetry");
        }
        Check.Equal(Five("As Js 9s 5s 3s"), Five("Ah Jh 9h 5h 3h"), "suits do not break ties");
        Check.Equal(Five("Qc Kd Ah 2s 3c").Rank, EHandRank.Nothing, "no wraparound straight");
        Check.Equal(Five("Ac 2d 3h 4s 5c").Rank, EHandRank.Straight, "wheel straight");

        Best("2c 3d Th Jh Qh Kh Ah", "Th Jh Qh Kh Ah", "play the board with zero hole cards");
        Best("Ks 3d As Ad Ac Ah 2s", "As Ad Ac Ah Ks", "one hole card as kicker");
        Best("As Ac Ah Kd 8c 5s 2d", "As Ac Ah Kd 8c", "two hole cards");
        Best("As Ah Ad Ks Kh Kd 2c", "As Ah Ad Ks Kh", "two trips choose stronger full house");
        Best("As Ah Ks Kh Qs Qh 2d", "As Ah Ks Kh Qs", "three pairs choose top two plus kicker");
        Best("As Js 9s 6s 4s 2s Kh", "As Js 9s 6s 4s", "six-card flush chooses highest five");
        Best("As 2h 3d 4c 5h 8d Kc", "As 2h 3d 4c 5h", "wheel among seven cards");
        Check.Equal(Seven("As Ah Kc Kd Qh 9s 3d").Value, Seven("Ac Ad Kh Ks Qd 8h 2c").Value,
            "sixth and seventh cards do not break an equal best-five tie");

        Check.Throws<ArgumentException>(() => PokerHandEvaluator.EvaluateFive(new int[5]), "duplicate rejected");
        Check.Throws<ArgumentException>(() => PokerHandEvaluator.EvaluateSeven(Check.Cards("As Ks")), "wrong count rejected");
        Check.Throws<ArgumentException>(() => PokerHandEvaluator.EvaluateSeven(Check.Cards("As As Ks Qs Js Ts 9s")), "seven-card duplicate rejected");
        Check.Throws<ArgumentOutOfRangeException>(() => PokerHandEvaluator.EvaluateFive([-1, 1, 2, 3, 4]), "negative rejected");
        Check.Throws<ArgumentOutOfRangeException>(() => PokerHandEvaluator.EvaluateFive([52, 1, 2, 3, 4]), "out of deck rejected");

        var random = new Random(73103);
        for (int i = 0; i < 1000; i++)
        {
            int[] cards = Enumerable.Range(0, 52).OrderBy(_ => random.Next()).Take(7).ToArray();
            var hand = PokerHandEvaluator.EvaluateSeven(cards);
            Check.That(hand.Cards.Count == 5 && hand.Cards.Distinct().Count() == 5
                && hand.Cards.All(cards.Contains), "best-five cards are a valid input subset");
            Check.Equal(PokerHandEvaluator.EvaluateFive(hand.Cards.ToArray()), hand.Value, "returned five match returned value");
            Check.Equal(PokerHandEvaluator.EvaluateSeven(cards.Reverse().ToArray()).Value, hand.Value,
                "card order cannot change comparison");
        }
    }

    public static void ExhaustiveFiveCardCategories()
    {
        long[] counts = new long[11];
        var strengths = Enumerable.Range(0, 11).Select(_ => new HashSet<int>()).ToArray();
        Span<int> hand = stackalloc int[5];
        for (int a = 0; a < 48; a++)
        for (int b = a + 1; b < 49; b++)
        for (int c = b + 1; c < 50; c++)
        for (int d = c + 1; d < 51; d++)
        for (int e = d + 1; e < 52; e++)
        {
            hand[0] = a; hand[1] = b; hand[2] = c; hand[3] = d; hand[4] = e;
            var value = PokerHandEvaluator.EvaluateFive(hand);
            counts[(int)value.Rank]++;
            strengths[(int)value.Rank].Add(value.TieBreak);
        }
        // Exact combinatorial distribution of all C(52,5) hands; royal flush separated from straight flush.
        long[] expected = [0, 1302540, 1098240, 123552, 54912, 10200, 5108, 3744, 624, 36, 4];
        Check.Sequence(counts, expected, "exhaustive category frequencies");
        Check.Equal(counts.Sum(), 2598960L, "all five-card combinations enumerated");
        // Independent counts of distinct strengths detect dropped kickers and suit-based tie breakers.
        int[] expectedStrengths = [0, 1277, 2860, 858, 858, 10, 1277, 156, 156, 9, 1];
        Check.Sequence(strengths.Select(values => values.Count), expectedStrengths, "7,462 distinct poker strengths");
    }

    private static PokerHandValue Five(string cards) => PokerHandEvaluator.EvaluateFive(Check.Cards(cards));
    private static PokerBestHand Seven(string cards) => PokerHandEvaluator.EvaluateSeven(Check.Cards(cards));
    private static void Best(string seven, string five, string message) => Check.Equal(Seven(seven).Value, Five(five), message);
}
