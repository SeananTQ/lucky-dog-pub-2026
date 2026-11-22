using System.Collections.Generic;
using System.Linq;
using DataTables;

namespace LuckyDogRise;

internal static class PokerHandComposition
{
    public static int GetMatchingRankCount(EHandRank rank) => rank switch
    {
        EHandRank.OnePair or EHandRank.TwoPair => 2,
        EHandRank.ThreeOfAKind => 3,
        EHandRank.FourOfAKind => 4,
        _ => 0,
    };

    public static HashSet<int> GetCoreCards(int[] cards, EHandRank rank)
    {
        int matchingCount = GetMatchingRankCount(rank);
        if (matchingCount == 0)
            return cards.ToHashSet();

        return cards.GroupBy(card => card % 13)
            .Where(group => group.Count() == matchingCount)
            .SelectMany(group => group)
            .ToHashSet();
    }
}
