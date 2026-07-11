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
    private const float SequentialPitchStartMinimum = 0.98f;
    private const float SequentialPitchStartMaximum = 1.0f;
    private const float SequentialPitchStep = 0.015f;

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
        EmitSignal(SignalName.CardClicked, index);
    }

    public void DealCards(int[] hand)
    {
        SetCardsVisible(true);
        float perCardDuration = 0.2f;
        var handPitchStart = GetSequentialPitchStart();
        for (int i = 0; i < CardCount; i++)
        {
            _cards[i].SetCard(hand[i], i);
            _cards[i].ResetModulate();
            _cards[i].ShowBack();
            _cards[i].AnimateDeal(i * perCardDuration, GetSequentialPitch(handPitchStart, i), 0f);
        }
    }

    public void ReplaceCards(int[] finalHand, bool[] held)
    {
        float perCardDelay = 0.2f;
        int replacementCount = 0;
        for (int i = 0; i < CardCount; i++)
            if (!held[i]) replacementCount++;
        var handPitchStart = GetSequentialPitchStart();
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
                    _cards[idx].AnimateReplace(GetSequentialPitch(handPitchStart, scheduledIndex), 0f);
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

    private static float GetSequentialPitchStart()
    {
        return Mathf.Lerp(SequentialPitchStartMinimum, SequentialPitchStartMaximum, (float)GD.Randf());
    }

    private static float GetSequentialPitch(float start, int sequenceIndex)
    {
        return start + SequentialPitchStep * sequenceIndex;
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
    /// 从左向右传递的错拍轻抬，表达整组卡牌可供选择，而不暗示具体哪张该弃。
    /// </summary>
    public void PlayInteractionHint()
    {
        if (!CanPlayInteractionHint)
            return;

        AudioManager.Instance.PlaySfx("Card_PokerHandHint");
        var order = new[] { 0, 1, 2, 3, 4 };
        var delays = new[] { 0.0, 0.05, 0.1, 0.15, 0.2 };
        var lifts = new[] { 8f, 9f, 10f, 11f, 12f };
        var rotations = new[] { 0.026f, 0.03f, 0.034f, 0.038f, 0.042f };

        for (var sequenceIndex = 0; sequenceIndex < order.Length; sequenceIndex++)
        {
            var cardIndex = order[sequenceIndex];
            _cards[cardIndex].PlayInteractionHint(
                delays[sequenceIndex], lifts[sequenceIndex], rotations[sequenceIndex]);
        }
    }

    public CardController GetCard(int index) => _cards[index];
}
