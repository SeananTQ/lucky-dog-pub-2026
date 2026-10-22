using System;
using System.Linq;
using DataTables;

namespace LuckyDogRise;

/// <summary>Presentation-only samples; never consumes the game's deck or random state.</summary>
public static class PokerHandExamples
{
    public static int VisibleCardCount(EHandRank rank) => rank switch
    {
        EHandRank.FourOfAKind or EHandRank.TwoPair => 4,
        EHandRank.ThreeOfAKind => 3,
        EHandRank.OnePair => 2,
        EHandRank.RoyalFlush or EHandRank.StraightFlush or EHandRank.FullHouse
            or EHandRank.Flush or EHandRank.Straight => 5,
        _ => throw new ArgumentOutOfRangeException(nameof(rank))
    };

    public static int[] Next(EHandRank rank, Random random, int[] previous = null)
    {
        int visibleCount = VisibleCardCount(rank);
        for (int attempt = 0; attempt < 128; attempt++)
        {
            var cards = Generate(rank, random);
            // Also reject a straight flush in the flush row, or five equal suits in a straight.
            if (CardEvaluator.Evaluate(cards) != rank)
                continue;
            if (previous != null && cards.Take(visibleCount).SequenceEqual(previous.Take(visibleCount)))
                continue;
            return cards;
        }
        throw new InvalidOperationException($"Could not generate a new example for {rank}.");
    }

    private static int[] Generate(EHandRank rank, Random random)
    {
        if (rank is EHandRank.RoyalFlush or EHandRank.StraightFlush or EHandRank.Straight)
        {
            int start = rank == EHandRank.RoyalFlush ? 9 : random.Next(rank == EHandRank.Straight ? 10 : 9);
            int suit = random.Next(4);
            return Enumerable.Range(0, 5)
                .Select(i => (rank == EHandRank.Straight ? random.Next(4) : suit) * 13 + (start + i) % 13)
                .ToArray();
        }

        int[] ranks = Enumerable.Range(0, 13).ToArray();
        random.Shuffle(ranks);
        if (rank == EHandRank.Flush)
        {
            int suit = random.Next(4);
            return ranks.Take(5).OrderBy(value => value == 0 ? 13 : value)
                .Select(value => suit * 13 + value).ToArray();
        }

        var cards = new int[5];
        int firstGroup = rank switch
        {
            EHandRank.FourOfAKind => 4,
            EHandRank.FullHouse or EHandRank.ThreeOfAKind => 3,
            _ => 2
        };
        int[] suits = [0, 1, 2, 3];
        random.Shuffle(suits);
        for (int i = 0; i < firstGroup; i++)
            cards[i] = suits[i] * 13 + ranks[0];

        if (rank is EHandRank.FullHouse or EHandRank.TwoPair)
        {
            random.Shuffle(suits);
            cards[firstGroup] = suits[0] * 13 + ranks[1];
            cards[firstGroup + 1] = suits[1] * 13 + ranks[1];
            if (rank == EHandRank.TwoPair)
                cards[4] = random.Next(4) * 13 + ranks[2];
        }
        else
        {
            for (int i = firstGroup; i < 5; i++)
                cards[i] = random.Next(4) * 13 + ranks[i - firstGroup + 1];
        }
        return cards;
    }
}
