using System;
using DataTables;
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

    public EHandRank EvaluateHoldRank(int[] currentHand, bool[] held, int[] finalHand)
    {
        return CardEvaluator.Evaluate(finalHand);
    }

    private static DogSignal RankToSignal(EHandRank rank)
    {
        return rank switch
        {
            EHandRank.Nothing => DogSignal.Bored,
            EHandRank.JacksOrBetter => DogSignal.Happy,
            EHandRank.TwoPair => DogSignal.Happy,
            EHandRank.ThreeOfAKind => DogSignal.Happy,
            EHandRank.Straight => DogSignal.LuckyEye,
            EHandRank.Flush => DogSignal.LuckyEye,
            EHandRank.FullHouse => DogSignal.LuckyEye,
            EHandRank.FourOfAKind => DogSignal.TopTier,
            EHandRank.StraightFlush => DogSignal.TopTier,
            EHandRank.RoyalFlush => DogSignal.TopTier,
            _ => DogSignal.Bored
        };
    }
}
