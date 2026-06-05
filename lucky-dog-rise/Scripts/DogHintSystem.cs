using System;
using System.Linq;

namespace LuckyDogRise;

public enum DogSignal
{
    Bored,      // Bad hand
    Happy,      // Decent (pair/two pair)
    LuckyEye,   // Big hand (flush+)
    TopTier      // Top tier (straight flush/royal)
}

public class DogHintSystem
{
    public bool HasGivenHint { get; set; }

    public void ResetForNewHand()
    {
        HasGivenHint = false;
    }

    public DogSignal EvaluateHold(int[] cards, bool[] held, HandRank predeterminedRank)
    {
        // The dog knows the final outcome (predetermined rank)
        // But it gives imprecise signals based on general quality
        return predeterminedRank switch
        {
            HandRank.Nothing => DogSignal.Bored,
            HandRank.JacksOrBetter => DogSignal.Happy,
            HandRank.TwoPair => DogSignal.Happy,
            HandRank.ThreeOfAKind => DogSignal.Happy,
            HandRank.Straight => DogSignal.LuckyEye,
            HandRank.Flush => DogSignal.LuckyEye,
            HandRank.FullHouse => DogSignal.LuckyEye,
            HandRank.FourOfAKind => DogSignal.TopTier,
            HandRank.StraightFlush => DogSignal.TopTier,
            HandRank.RoyalFlush => DogSignal.TopTier,
            _ => DogSignal.Bored
        };
    }
}
