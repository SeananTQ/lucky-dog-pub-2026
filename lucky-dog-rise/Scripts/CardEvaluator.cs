using System;
using DataTables;
using System.Collections.Generic;
using System.Linq;

namespace LuckyDogRise;

public static class CardEvaluator
{
    // 卡牌编码：0-51 的整数
    // suit = card / 13 → 0=Club, 1=Diamond, 2=Heart, 3=Spade
    // rank = card % 13 → 0=Ace, 1=2, 2=3, ..., 9=10, 10=Jack, 11=Queen, 12=King
    // 注意：rank 是 0-based，美术资源文件名是 1-based（CardToString 会 +1）
    public static int GetSuit(int card) => card / 13;
    public static int GetRank(int card) => card % 13;

    public static EHandRank Evaluate(int[] cards)
    {
        if (cards.Length != 5) return EHandRank.Nothing;

        var ranks = cards.Select(GetRank).OrderBy(r => r).ToArray();
        var suits = cards.Select(GetSuit).ToArray();

        bool isFlush = suits.Distinct().Count() == 1;
        bool isStraight = IsStraight(ranks, out bool isRoyal);

        if (isFlush && isStraight && isRoyal) return EHandRank.RoyalFlush;
        if (isFlush && isStraight) return EHandRank.StraightFlush;

        var groups = ranks.GroupBy(r => r).OrderByDescending(g => g.Count()).ToArray();
        int maxCount = groups[0].Count();

        if (maxCount == 4) return EHandRank.FourOfAKind;
        if (maxCount == 3 && groups.Length == 2) return EHandRank.FullHouse;
        if (isFlush) return EHandRank.Flush;
        if (isStraight) return EHandRank.Straight;
        if (maxCount == 3) return EHandRank.ThreeOfAKind;
        if (maxCount == 2 && groups.Count(g => g.Count() == 2) == 2) return EHandRank.TwoPair;
        if (maxCount == 2) return EHandRank.OnePair;

        return EHandRank.Nothing;
    }

    public static int GetPayout(int[] cards, int bet)
    {
        var rank = Evaluate(cards);
        if (rank == EHandRank.Nothing)
            return 0;

        var payTableRow = LubanData.Tables.TbPayTable.DataList
            .FirstOrDefault(row => row.HandRank == rank);
        return payTableRow == null ? 0 : payTableRow.PayoutMultiplier * bet;
    }

    private static bool IsStraight(int[] sortedRanks, out bool isRoyal)
    {
        isRoyal = false;

        // 10-J-Q-K-A (royal): sorted = [0,9,10,11,12]
        if (sortedRanks.SequenceEqual(new[] { 0, 9, 10, 11, 12 }))
        {
            isRoyal = true;
            return true;
        }

        // 普通顺子：连续递增
        for (int i = 1; i < 5; i++)
        {
            if (sortedRanks[i] != sortedRanks[i - 1] + 1)
                return false;
        }

        return true;
    }

    public static int[] GetOptimalHold(int[] cards)
    {
        // Returns indices (0-4) of cards to hold for best expected outcome
        EHandRank currentRank = Evaluate(cards);
        var held = new List<int>();

        var ranks = cards.Select(GetRank).ToArray();
        var suits = cards.Select(GetSuit).ToArray();
        var groups = ranks.Select((r, i) => new { Rank = r, Index = i })
                          .GroupBy(x => x.Rank)
                          .OrderByDescending(g => g.Count())
                          .ToArray();

        // Always hold made hands (except maybe breaking a low pair)
        if (currentRank >= EHandRank.Straight)
        {
            return Enumerable.Range(0, 5).ToArray(); // Hold all
        }

        // Four of a kind - hold all four
        if (groups[0].Count() == 4)
            return groups[0].Select(x => x.Index).ToArray();

        // Full house - hold all
        if (groups[0].Count() == 3 && groups.Length == 2)
            return Enumerable.Range(0, 5).ToArray();

        // Three of a kind - hold the three
        if (groups[0].Count() == 3)
            return groups[0].Select(x => x.Index).ToArray();

        // Two pair - hold both pairs
        if (groups[0].Count() == 2 && groups[1].Count() == 2)
            return groups.Take(2).SelectMany(g => g.Select(x => x.Index)).ToArray();

        // Any pair is currently a winning hand. Keep high and low pairs explicit
        // so future hold strategy or reward rules can differentiate them again.
        if (groups[0].Count() == 2)
        {
            int pairRank = groups[0].Key;
            // High pair (J, Q, K, A): currently held the same as a low pair.
            if (pairRank == 0 || pairRank >= 10)
                return groups[0].Select(x => x.Index).ToArray();
            // Low pair: also held under the current all-pair Playtest rule.
            return groups[0].Select(x => x.Index).ToArray();
        }

        // No pair - hold high cards (J, Q, K, A)
        for (int i = 0; i < 5; i++)
        {
            if (ranks[i] == 0 || ranks[i] >= 10)
                held.Add(i);
        }

        // Check for 4 to a flush
        var suitGroups = suits.Select((s, i) => new { Suit = s, Index = i })
                              .GroupBy(x => x.Suit)
                              .OrderByDescending(g => g.Count())
                              .ToArray();
        if (suitGroups[0].Count() == 4)
            return suitGroups[0].Select(x => x.Index).ToArray();

        // Check for 4 to a straight
        var sortedByRank = ranks.Select((r, i) => new { Rank = r, Index = i })
                                .OrderBy(x => x.Rank).ToArray();
        for (int start = 0; start <= 1; start++)
        {
            var window = sortedByRank.Skip(start).Take(4).ToArray();
            if (CanCompleteStraight(window.Select(x => x.Rank)))
                return window.Select(x => x.Index).ToArray();
        }

        return held.ToArray();
    }

    private static bool CanCompleteStraight(IEnumerable<int> ranks)
    {
        var uniqueRanks = ranks.Distinct().ToArray();
        if (uniqueRanks.Length != 4)
            return false;

        // A-2-3-4-5 through 9-10-J-Q-K are contiguous in this project's
        // A=0 rank encoding.
        for (int startRank = 0; startRank <= 8; startRank++)
        {
            if (uniqueRanks.All(rank => rank >= startRank && rank <= startRank + 4))
                return true;
        }

        // 10-J-Q-K-A is the only legal sequence that crosses the rank boundary.
        return uniqueRanks.All(rank => rank == 0 || rank >= 9);
    }
}
