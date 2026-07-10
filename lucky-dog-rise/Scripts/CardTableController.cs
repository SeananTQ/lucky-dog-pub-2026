using Godot;
using System.Linq;

namespace LuckyDogRise;

public partial class CardTableController : Node2D, IInteractionHintTarget
{
    [Signal]
    public delegate void CardClickedEventHandler(int index);

    [Signal]
    public delegate void LastReplacementStartedEventHandler(bool hasReplacement);

    private static readonly PackedScene CardScene = GD.Load<PackedScene>("res://Scenes/Prefabs/Card.tscn");
    private const int CardCount = 5;
    private const float CardWidth = 120f;
    private const float CardGap = 12f;

    private CardController[] _cards = new CardController[5];

    public bool CanPlayInteractionHint => _cards.All(card => card != null && card.Visible);
    public bool IsInteractionHintPlaying => _cards.Any(card => card != null && card.IsInteractionHintPlaying);

    public override void _Ready()
    {
        float totalWidth = CardCount * CardWidth + (CardCount - 1) * CardGap;
        float startX = -totalWidth / 2f + CardWidth / 2f;

        for (int i = 0; i < CardCount; i++)
        {
            var card = CardScene.Instantiate<CardController>();
            card.CardIndex = i;
            card.Clicked += OnCardClicked;
            AddChild(card);
            card.Position = new Vector2(startX + i * (CardWidth + CardGap), 0);
            _cards[i] = card;
        }

        SetCardsVisible(false);
    }

    private void SetCardsVisible(bool visible)
    {
        for (int i = 0; i < CardCount; i++)
            _cards[i].Visible = visible;
    }

    private void OnCardClicked(int index)
    {
        AudioManager.Instance.PlaySfx("Card_PokerHandSelect");
        EmitSignal(SignalName.CardClicked, index);
    }

    public void DealCards(int[] hand)
    {
        SetCardsVisible(true);
        float perCardDuration = 0.2f;
        for (int i = 0; i < CardCount; i++)
        {
            _cards[i].SetCard(hand[i], i);
            _cards[i].ResetModulate();
            _cards[i].ShowBack();
            _cards[i].AnimateDeal(i * perCardDuration);
        }
    }

    public void ReplaceCards(int[] finalHand, bool[] held)
    {
        float perCardDelay = 0.2f;
        int replacementCount = 0;
        for (int i = 0; i < CardCount; i++)
            if (!held[i]) replacementCount++;
        int replaced = 0;
        for (int i = 0; i < CardCount; i++)
        {
            if (!held[i])
            {
                int idx = i;
                _cards[idx].SetCard(finalHand[idx], idx);
                int scheduledIndex = replaced;
                float delay = scheduledIndex * perCardDelay;
                GetTree().CreateTimer(delay).Timeout += () =>
                {
                    if (scheduledIndex == replacementCount - 1)
                        EmitSignal(SignalName.LastReplacementStarted, true);
                    _cards[idx].AnimateReplace();
                };
                replaced++;
            }
        }

        if (replacementCount == 0)
            CallDeferred(nameof(EmitLastReplacementStarted));
    }

    private void EmitLastReplacementStarted()
    {
        EmitSignal(SignalName.LastReplacementStarted, false);
    }

    public void SetHeld(int index, bool held)
    {
        _cards[index].SetHeld(held);
    }

    public void BrightenAll()
    {
        for (int i = 0; i < CardCount; i++)
            _cards[i].ResetModulate();
    }

    /// <summary>
    /// 从中间向两侧扩散的错拍轻抬，表达整组卡牌可供选择，而不暗示具体哪张该弃。
    /// </summary>
    public void PlayInteractionHint()
    {
        if (!CanPlayInteractionHint)
            return;

        var order = new[] { 2, 1, 3, 0, 4 };
        var delays = new[] { 0.0, 0.042, 0.072, 0.118, 0.146 };
        var lifts = new[] { 9f, 7f, 8f, 6f, 6.5f };
        var rotations = new[] { 0.028f, -0.022f, 0.032f, -0.018f, 0.024f };

        for (var sequenceIndex = 0; sequenceIndex < order.Length; sequenceIndex++)
        {
            var cardIndex = order[sequenceIndex];
            _cards[cardIndex].PlayInteractionHint(
                delays[sequenceIndex], lifts[sequenceIndex], rotations[sequenceIndex]);
        }
    }

    public CardController GetCard(int index) => _cards[index];
}
