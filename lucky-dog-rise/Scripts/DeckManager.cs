using System;
using System.Collections.Generic;
using System.Linq;

namespace LuckyDogRise;

public class DeckManager
{
    private Random _rng;
    private int[] _fullDeck;
    private int _dealIndex;

    public int[] CurrentHand { get; private set; } = new int[5];
    public int[] FinalHand { get; private set; } = new int[5];
    public HandRank PredeterminedRank { get; private set; }
    public int LastSeed { get; private set; }

    private int? _fixedSeed;

    public DeckManager(int? seed = null)
    {
        _fixedSeed = seed;
        _rng = seed.HasValue ? new Random(seed.Value) : new Random();
        _fullDeck = Enumerable.Range(0, 52).ToArray();
    }

    public void SetFixedSeed(int? seed)
    {
        _fixedSeed = seed;
    }

    public void Deal()
    {
        // 每手用新种子，方便复现
        int seed = _fixedSeed ?? new Random().Next();
        LastSeed = seed;
        _rng = new Random(seed);
        _fixedSeed = null; // 固定种子只用一次

        // Shuffle full deck
        Shuffle(_fullDeck);
        _dealIndex = 0;

        // Decide outcome distribution (biased toward near-miss excitement)
        PredeterminedRank = RollOutcome();

        // Generate hand based on predetermined outcome
        CurrentHand = GenerateHandForRank(PredeterminedRank);
        FinalHand = (int[])CurrentHand.Clone();
    }

    public int[] DrawReplacements(bool[] held)
    {
        // Replace non-held cards from remaining deck
        for (int i = 0; i < 5; i++)
        {
            if (!held[i])
            {
                FinalHand[i] = DrawNextCard();
            }
        }
        return FinalHand;
    }

    public int[] GetOptimalHold()
    {
        return CardEvaluator.GetOptimalHold(CurrentHand);
    }

    public int[] PreviewFinalHand(bool[] held)
    {
        // 预览补牌结果，不修改内部状态
        var preview = (int[])FinalHand.Clone();
        var usedCards = new HashSet<int>(CurrentHand.Concat(FinalHand));
        int peekIndex = _dealIndex;

        for (int i = 0; i < 5; i++)
        {
            if (!held[i])
            {
                while (peekIndex < 52)
                {
                    int card = _fullDeck[peekIndex++];
                    if (!usedCards.Contains(card))
                    {
                        preview[i] = card;
                        usedCards.Add(card);
                        break;
                    }
                }
            }
        }
        return preview;
    }

    private HandRank RollOutcome()
    {
        // Weighted distribution: biased toward exciting near-miss outcomes
        // Total weight: 1000
        // Nothing: 250, JacksOrBetter: 300, TwoPair: 180, ThreeOfAKind: 120,
        // Straight: 60, Flush: 40, FullHouse: 30, FourOfAKind: 12, StraightFlush: 6, RoyalFlush: 2
        int roll = _rng.Next(1000);

        if (roll < 250) return HandRank.Nothing;          // 25%
        if (roll < 550) return HandRank.JacksOrBetter;    // 30%
        if (roll < 730) return HandRank.TwoPair;          // 18%
        if (roll < 850) return HandRank.ThreeOfAKind;     // 12%
        if (roll < 910) return HandRank.Straight;          // 6%
        if (roll < 950) return HandRank.Flush;             // 4%
        if (roll < 980) return HandRank.FullHouse;         // 3%
        if (roll < 992) return HandRank.FourOfAKind;       // 1.2%
        if (roll < 998) return HandRank.StraightFlush;     // 0.6%
        return HandRank.RoyalFlush;                        // 0.2%
    }

    private int[] GenerateHandForRank(HandRank rank)
    {
        return rank switch
        {
            HandRank.RoyalFlush => GenerateRoyalFlush(),
            HandRank.StraightFlush => GenerateStraightFlush(),
            HandRank.FourOfAKind => GenerateFourOfAKind(),
            HandRank.FullHouse => GenerateFullHouse(),
            HandRank.Flush => GenerateFlush(),
            HandRank.Straight => GenerateStraight(),
            HandRank.ThreeOfAKind => GenerateThreeOfAKind(),
            HandRank.TwoPair => GenerateTwoPair(),
            HandRank.JacksOrBetter => GenerateJacksOrBetter(),
            _ => GenerateNothing(),
        };
    }

    private int[] GenerateRoyalFlush()
    {
        int suit = _rng.Next(4);
        return new[] { suit * 13 + 0, suit * 13 + 9, suit * 13 + 10, suit * 13 + 11, suit * 13 + 12 };
    }

    private int[] GenerateStraightFlush()
    {
        int suit = _rng.Next(4);
        int startRank = _rng.Next(9); // 0-8 for A-5 through 9-K
        if (startRank == 8) // 9-K straight flush
            return new[] { suit * 13 + 9, suit * 13 + 10, suit * 13 + 11, suit * 13 + 12, suit * 13 + 0 };
        return Enumerable.Range(startRank, 5).Select(r => suit * 13 + r).ToArray();
    }

    private int[] GenerateFourOfAKind()
    {
        int rank = _rng.Next(13);
        int suit1 = _rng.Next(4);
        int suit2 = (suit1 + 1 + _rng.Next(3)) % 4;
        int suit3 = (suit1 + 2 + _rng.Next(2)) % 4;
        int suit4 = (suit1 + 3) % 4;
        int kickerRank = (rank + 1 + _rng.Next(12)) % 13;
        int kickerSuit = _rng.Next(4);
        return new[] { rank + suit1 * 13, rank + suit2 * 13, rank + suit3 * 13, rank + suit4 * 13, kickerRank + kickerSuit * 13 };
    }

    private int[] GenerateFullHouse()
    {
        int tripRank = _rng.Next(13);
        int pairRank = (tripRank + 1 + _rng.Next(12)) % 13;
        var suits = Enumerable.Range(0, 4).OrderBy(_ => _rng.Next()).ToArray();
        return new[]
        {
            tripRank + suits[0] * 13, tripRank + suits[1] * 13, tripRank + suits[2] * 13,
            pairRank + suits[0] * 13, pairRank + suits[1] * 13
        };
    }

    private int[] GenerateFlush()
    {
        int suit = _rng.Next(4);
        var ranks = Enumerable.Range(0, 13).OrderBy(_ => _rng.Next()).Take(5).OrderBy(r => r).ToArray();
        // Make sure it's not a straight
        while (IsConsecutive(ranks))
        {
            ranks = Enumerable.Range(0, 13).OrderBy(_ => _rng.Next()).Take(5).OrderBy(r => r).ToArray();
        }
        return ranks.Select(r => suit * 13 + r).ToArray();
    }

    private int[] GenerateStraight()
    {
        int startRank = _rng.Next(9);
        var ranks = startRank == 8
            ? new[] { 9, 10, 11, 12, 0 }
            : Enumerable.Range(startRank, 5).ToArray();
        // Assign different suits
        var suits = Enumerable.Range(0, 4).OrderBy(_ => _rng.Next()).ToArray();
        return ranks.Select((r, i) => r + suits[i % 4] * 13).ToArray();
    }

    private int[] GenerateThreeOfAKind()
    {
        int rank = _rng.Next(13);
        var suits = Enumerable.Range(0, 4).OrderBy(_ => _rng.Next()).Take(3).ToArray();
        var otherRanks = Enumerable.Range(0, 13).Where(r => r != rank).OrderBy(_ => _rng.Next()).Take(2).ToArray();
        return new[]
        {
            rank + suits[0] * 13, rank + suits[1] * 13, rank + suits[2] * 13,
            otherRanks[0] + _rng.Next(4) * 13, otherRanks[1] + _rng.Next(4) * 13
        };
    }

    private int[] GenerateTwoPair()
    {
        var pairRanks = Enumerable.Range(0, 13).OrderBy(_ => _rng.Next()).Take(2).ToArray();
        int kickerRank = Enumerable.Range(0, 13).Where(r => r != pairRanks[0] && r != pairRanks[1]).OrderBy(_ => _rng.Next()).First();
        var suits = Enumerable.Range(0, 4).OrderBy(_ => _rng.Next()).ToArray();
        return new[]
        {
            pairRanks[0] + suits[0] * 13, pairRanks[0] + suits[1] * 13,
            pairRanks[1] + suits[2] * 13, pairRanks[1] + suits[3] * 13,
            kickerRank + _rng.Next(4) * 13
        };
    }

    private int[] GenerateJacksOrBetter()
    {
        // Pair of J, Q, K, or A
        int[] highRanks = { 0, 10, 11, 12 }; // A, J, Q, K
        int rank = highRanks[_rng.Next(4)];
        var suits = Enumerable.Range(0, 4).OrderBy(_ => _rng.Next()).Take(2).ToArray();
        var otherRanks = Enumerable.Range(0, 13).Where(r => r != rank).OrderBy(_ => _rng.Next()).Take(3).ToArray();
        return new[]
        {
            rank + suits[0] * 13, rank + suits[1] * 13,
            otherRanks[0] + _rng.Next(4) * 13, otherRanks[1] + _rng.Next(4) * 13, otherRanks[2] + _rng.Next(4) * 13
        };
    }

    private int[] GenerateNothing()
    {
        // No pair, no straight, no flush
        var cards = new List<int>();
        var usedRanks = new HashSet<int>();
        while (cards.Count < 5)
        {
            int card = _rng.Next(52);
            int rank = CardEvaluator.GetRank(card);
            int suit = CardEvaluator.GetSuit(card);
            if (cards.Any(c => CardEvaluator.GetRank(c) == rank)) continue;
            if (cards.Count(c => CardEvaluator.GetSuit(c) == suit) >= 4) continue;
            cards.Add(card);
            usedRanks.Add(rank);
        }
        // Verify it's actually nothing
        var result = cards.ToArray();
        if (CardEvaluator.Evaluate(result) != HandRank.Nothing)
            return GenerateNothing(); // Retry
        return result;
    }

    private int DrawNextCard()
    {
        // Draw from deck, skipping cards already in hand
        var usedCards = new HashSet<int>(CurrentHand.Concat(FinalHand));
        while (_dealIndex < 52)
        {
            int card = _fullDeck[_dealIndex++];
            if (!usedCards.Contains(card))
                return card;
        }
        // Fallback: generate random unused card
        int fallback;
        do { fallback = _rng.Next(52); } while (usedCards.Contains(fallback));
        return fallback;
    }

    private static bool IsConsecutive(int[] sortedRanks)
    {
        if (sortedRanks.Contains(0) && sortedRanks.Contains(12))
        {
            // Check A-low straight possibility
            var alt = sortedRanks.Select(r => r == 0 ? 13 : r).OrderBy(r => r).ToArray();
            if (alt[4] - alt[0] == 4) return true;
        }
        for (int i = 1; i < sortedRanks.Length; i++)
            if (sortedRanks[i] != sortedRanks[i - 1] + 1) return false;
        return true;
    }

    private static void Shuffle(int[] array)
    {
        var rng = new Random();
        for (int i = array.Length - 1; i > 0; i--)
        {
            int j = rng.Next(i + 1);
            (array[i], array[j]) = (array[j], array[i]);
        }
    }

    public static string CardToString(int card)
    {
        string[] suits = { "Club", "Diamond", "Heart", "Spade" };
        // rank 0=Ace→1, 1=2→2, ..., 9=10→10, 10=Jack→11, 11=Queen→12, 12=King→13
        int rankNum = CardEvaluator.GetRank(card) + 1;
        return $"{suits[CardEvaluator.GetSuit(card)]}{rankNum}";
    }

    public static string CardToAssetPath(int card)
    {
        return $"res://Assets/Card/{CardToString(card)}.png";
    }
}
