namespace LuckyDogRise;

public static class InteractionHintKeys
{
    /// <summary>准备下注：提示点击桌面消耗品。</summary>
    public const string WaitingForBetRefreshment = "Poker.WaitingForBet.Refreshment";

    /// <summary>准备下注：提示点击下注筹码。</summary>
    public const string WaitingForBetBetStack = "Poker.WaitingForBet.BetStack";

    /// <summary>消耗品气球已打开：提示点击对号或酒杯。</summary>
    public const string RefreshmentUseConfirm = "Poker.Refreshment.UseConfirm";

    /// <summary>发牌后：提示选择需要替换的牌。</summary>
    public const string DealtCardSelection = "Poker.Dealt.CardSelection";

    /// <summary>发牌后：提示点击小狗询问建议。</summary>
    public const string DealtDogAdvice = "Poker.Dealt.DogAdvice";

    /// <summary>发牌后：提示敲桌确认。</summary>
    public const string DealtHandConfirm = "Poker.Dealt.HandConfirm";

    /// <summary>调整保留牌后：提示敲桌确认。</summary>
    public const string HoldingHandConfirm = "Poker.Holding.HandConfirm";

    /// <summary>结算后：提示领取奖励筹码。</summary>
    public const string SettledRewardStack = "Poker.Settled.RewardStack";
}
