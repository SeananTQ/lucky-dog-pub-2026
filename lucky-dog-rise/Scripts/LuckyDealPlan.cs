using System.Collections.Generic;

namespace LuckyDogRise;

/// <summary>
/// 幸运发牌在一局开始时确定的完整计划。
/// 自然发牌不会创建这个对象，仍使用普通牌堆流程。
/// </summary>
public sealed class LuckyDealPlan
{
    public int[] WinnerHand { get; }
    public int[] InitialVisibleHand { get; }
    public Queue<int> ReplacementQueue { get; }

    public LuckyDealPlan(int[] winnerHand, int[] initialVisibleHand, IEnumerable<int> replacementQueue)
    {
        WinnerHand = winnerHand;
        InitialVisibleHand = initialVisibleHand;
        ReplacementQueue = new Queue<int>(replacementQueue);
    }
}

/// <summary>可持久化的幸运发牌 Buff。持续局数按成功下注的新局扣除。</summary>
public sealed class LuckyDealBuffState
{
    public int RemainingHands { get; set; }
    public float TriggerChance { get; set; }
}
