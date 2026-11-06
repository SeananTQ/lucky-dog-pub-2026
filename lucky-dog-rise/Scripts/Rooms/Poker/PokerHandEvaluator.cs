#if !DEMO_BUILD && !RECORDING_BUILD
using System;
using System.Collections.Generic;
using DataTables;

namespace LuckyDogRise.Rooms.Poker;

// Nothing means high card here; the single-player payout evaluator is intentionally unchanged.
public readonly record struct PokerHandValue(EHandRank Rank, int TieBreak) : IComparable<PokerHandValue>
{
    public int CompareTo(PokerHandValue other)
    {
        int category = Rank.CompareTo(other.Rank);
        return category != 0 ? category : TieBreak.CompareTo(other.TieBreak);
    }
}

public sealed record PokerBestHand(PokerHandValue Value, IReadOnlyList<int> Cards);

/// <summary>Standard hold'em comparison using the existing 0..51 card encoding. No payout or save access.</summary>
public static class PokerHandEvaluator
{
    public static PokerHandValue EvaluateFive(ReadOnlySpan<int> cards)
    {
        Validate(cards, 5);
        return ScoreFive(cards);
    }

    public static PokerBestHand EvaluateSeven(ReadOnlySpan<int> cards)
    {
        Validate(cards, 7);
        Span<int> candidate = stackalloc int[5];
        int[] bestCards = new int[5];
        PokerHandValue best = default;
        // All 21 subsets: neither hole card is required, and unused cards never break a tie.
        for (int omitA = 0; omitA < 6; omitA++)
        for (int omitB = omitA + 1; omitB < 7; omitB++)
        {
            int index = 0;
            for (int i = 0; i < 7; i++)
                if (i != omitA && i != omitB) candidate[index++] = cards[i];
            var value = ScoreFive(candidate);
            if (value.CompareTo(best) <= 0) continue;
            best = value;
            candidate.CopyTo(bestCards);
        }
        return new PokerBestHand(best, Array.AsReadOnly(bestCards));
    }

    private static void Validate(ReadOnlySpan<int> cards, int count)
    {
        if (cards.Length != count) throw new ArgumentException($"Exactly {count} cards required.", nameof(cards));
        ulong seen = 0;
        foreach (int card in cards)
        {
            if ((uint)card >= 52) throw new ArgumentOutOfRangeException(nameof(cards));
            ulong bit = 1UL << card;
            if ((seen & bit) != 0) throw new ArgumentException("Duplicate card.", nameof(cards));
            seen |= bit;
        }
    }

    private static PokerHandValue ScoreFive(ReadOnlySpan<int> cards)
    {
        Span<int> counts = stackalloc int[15];
        counts.Clear();
        bool flush = true;
        int suit = cards[0] / 13;
        foreach (int card in cards)
        {
            int rank = card % 13;
            counts[rank == 0 ? 14 : rank + 1]++;
            flush &= card / 13 == suit;
        }

        int straightHigh = 0;
        int run = 0;
        for (int rank = 14; rank >= 2; rank--)
        {
            run = counts[rank] != 0 ? run + 1 : 0;
            if (run == 5) { straightHigh = rank + 4; break; }
        }
        if (straightHigh == 0 && counts[14] != 0 && counts[5] != 0
            && counts[4] != 0 && counts[3] != 0 && counts[2] != 0) straightHigh = 5;
        if (flush && straightHigh != 0)
            return Pack(straightHigh == 14 ? EHandRank.RoyalFlush : EHandRank.StraightFlush, straightHigh);

        int four = 0, three = 0, pairHigh = 0, pairLow = 0;
        Span<int> singles = stackalloc int[5];
        int singleCount = 0;
        for (int rank = 14; rank >= 2; rank--)
        {
            switch (counts[rank])
            {
                case 4: four = rank; break;
                case 3: three = rank; break;
                case 2:
                    if (pairHigh == 0) pairHigh = rank; else pairLow = rank;
                    break;
                case 1: singles[singleCount++] = rank; break;
            }
        }
        if (four != 0) return Pack(EHandRank.FourOfAKind, four, singles[0]);
        if (three != 0 && pairHigh != 0) return Pack(EHandRank.FullHouse, three, pairHigh);
        if (flush) return Pack(EHandRank.Flush, singles[0], singles[1], singles[2], singles[3], singles[4]);
        if (straightHigh != 0) return Pack(EHandRank.Straight, straightHigh);
        if (three != 0) return Pack(EHandRank.ThreeOfAKind, three, singles[0], singles[1]);
        if (pairLow != 0) return Pack(EHandRank.TwoPair, pairHigh, pairLow, singles[0]);
        if (pairHigh != 0) return Pack(EHandRank.OnePair, pairHigh, singles[0], singles[1], singles[2]);
        return Pack(EHandRank.Nothing, singles[0], singles[1], singles[2], singles[3], singles[4]);
    }

    private static PokerHandValue Pack(EHandRank rank, int a, int b = 0, int c = 0, int d = 0, int e = 0)
        => new(rank, ((((a * 15) + b) * 15 + c) * 15 + d) * 15 + e);
}
#endif
