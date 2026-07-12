using System;
using DataTables;
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
    public EHandRank PredeterminedRank { get; private set; }
    public int LastSeed { get; private set; }
    public LuckyDealPlan LuckyDealPlan { get; private set; }

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

    public void Deal(float? luckyTriggerChance = null)
    {
        // 每手用新种子，方便复现
        int seed = _fixedSeed ?? new Random().Next();
        LastSeed = seed;
        _rng = new Random(seed);
        _fixedSeed = null; // 固定种子只用一次

        // Shuffle full deck
        Shuffle(_fullDeck);
        _dealIndex = 0;

        LuckyDealPlan = null;
        if (luckyTriggerChance.HasValue && _rng.NextDouble() < luckyTriggerChance.Value)
        {
            LuckyDealPlan = CreateLuckyDealPlan();
            PredeterminedRank = CardEvaluator.Evaluate(LuckyDealPlan.WinnerHand);
            CurrentHand = (int[])LuckyDealPlan.InitialVisibleHand.Clone();
            FinalHand = (int[])CurrentHand.Clone();
            return;
        }

        // 自然发牌不创建幸运计划：直接从洗好的 NormalDeck 取前五张，
        // 后续补牌会从第六张继续抽取。
        CurrentHand = _fullDeck.Take(5).ToArray();
        _dealIndex = 5;
        PredeterminedRank = CardEvaluator.Evaluate(CurrentHand);
        FinalHand = (int[])CurrentHand.Clone();
    }

    public int[] DrawReplacements(bool[] held)
    {
        // Replace non-held cards from remaining deck
        for (int i = 0; i < 5; i++)
        {
            if (!held[i])
            {
                FinalHand[i] = LuckyDealPlan is { ReplacementQueue.Count: > 0 }
                    ? LuckyDealPlan.ReplacementQueue.Dequeue()
                    : DrawNextCard();
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
        var usedCards = GetReservedCards();
        int peekIndex = _dealIndex;
        var replacementQueue = LuckyDealPlan == null
            ? null
            : new Queue<int>(LuckyDealPlan.ReplacementQueue);

        for (int i = 0; i < 5; i++)
        {
            if (!held[i])
            {
                if (replacementQueue is { Count: > 0 })
                {
                    preview[i] = replacementQueue.Dequeue();
                    continue;
                }

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

    private LuckyDealPlan CreateLuckyDealPlan()
    {
        var luckyRows = LubanData.Tables.TbPayTable.DataList
            .Where(row => row.LuckyWeight > 0)
            .ToArray();
        int totalWeight = luckyRows.Sum(row => row.LuckyWeight);
        if (totalWeight <= 0)
            throw new InvalidOperationException("LuckyWeight must contain at least one positive entry.");

        int roll = _rng.Next(totalWeight);
        var selected = luckyRows[0];
        foreach (var row in luckyRows)
        {
            if (roll < row.LuckyWeight)
            {
                selected = row;
                break;
            }
            roll -= row.LuckyWeight;
        }

        var winnerHand = GenerateHandForRank(selected.HandRank);
        int hiddenCount = RollHiddenCount();
        var hiddenIndices = Enumerable.Range(0, 5)
            .OrderBy(_ => _rng.Next())
            .Take(hiddenCount)
            .ToArray();
        var initialVisibleHand = (int[])winnerHand.Clone();
        var reservedCards = new HashSet<int>(winnerHand);

        foreach (int index in hiddenIndices)
        {
            int bait = DrawRandomUnreservedCard(reservedCards);
            initialVisibleHand[index] = bait;
            reservedCards.Add(bait);
        }

        Shuffle(initialVisibleHand);
        var replacementQueue = hiddenIndices
            .Select(index => winnerHand[index])
            .OrderBy(_ => _rng.Next())
            .ToArray();
        return new LuckyDealPlan(winnerHand, initialVisibleHand, replacementQueue);
    }

    private int RollHiddenCount()
    {
        int roll = _rng.Next(100);
        return roll < 30 ? 1 : roll < 90 ? 2 : 3;
    }

    private int DrawRandomUnreservedCard(HashSet<int> reservedCards)
    {
        var candidates = Enumerable.Range(0, 52)
            .Where(card => !reservedCards.Contains(card))
            .ToArray();
        return candidates[_rng.Next(candidates.Length)];
    }

    private int[] GenerateHandForRank(EHandRank rank)
    {
        return rank switch
        {
            EHandRank.RoyalFlush => GenerateRoyalFlush(),
            EHandRank.StraightFlush => GenerateStraightFlush(),
            EHandRank.FourOfAKind => GenerateFourOfAKind(),
            EHandRank.FullHouse => GenerateFullHouse(),
            EHandRank.Flush => GenerateFlush(),
            EHandRank.Straight => GenerateStraight(),
            EHandRank.ThreeOfAKind => GenerateThreeOfAKind(),
            EHandRank.TwoPair => GenerateTwoPair(),
            EHandRank.JacksOrBetter => GenerateJacksOrBetter(),
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
        var suits = Enumerable.Range(0, 4).OrderBy(_ => _rng.Next()).ToArray();
        int kickerRank = (rank + 1 + _rng.Next(12)) % 13;
        int kickerSuit = _rng.Next(4);
        return new[] { rank + suits[0] * 13, rank + suits[1] * 13, rank + suits[2] * 13, rank + suits[3] * 13, kickerRank + kickerSuit * 13 };
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
        if (CardEvaluator.Evaluate(result) != EHandRank.Nothing)
            return GenerateNothing(); // Retry
        return result;
    }

    private int DrawNextCard()
    {
        // Draw from deck, skipping cards already in hand
        var usedCards = GetReservedCards();
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

    private HashSet<int> GetReservedCards()
    {
        var usedCards = new HashSet<int>(CurrentHand.Concat(FinalHand));
        if (LuckyDealPlan != null)
        {
            // 隐藏赢家牌和首轮诱饵牌都不能从 NormalDeck 再次抽到。
            usedCards.UnionWith(LuckyDealPlan.WinnerHand);
            usedCards.UnionWith(LuckyDealPlan.InitialVisibleHand);
            usedCards.UnionWith(LuckyDealPlan.ReplacementQueue);
        }
        return usedCards;
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

    private void Shuffle(int[] array)
    {
        for (int i = array.Length - 1; i > 0; i--)
        {
            int j = _rng.Next(i + 1);
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
        return $"res://Assets/v1/CardFace/Classic/{CardToString(card)}.png";
    }
}
