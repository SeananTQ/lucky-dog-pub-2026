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
    public bool IsLocked { get; set; }

    public void ResetForNewHand()
    {
        HasGivenHint = false;
        IsLocked = false;
    }

    public DogSignal EvaluateHold(int[] currentHand, bool[] held, int[] finalHand)
    {
        // 狗知道补牌后的实际结果，根据最终手牌质量给信号
        var rank = CardEvaluator.Evaluate(finalHand);
        return RankToSignal(rank);
    }

    private static DogSignal RankToSignal(HandRank rank)
    {
        return rank switch
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
