using Godot;

namespace LuckyDogRise;

public partial class CardTableController : Node2D
{
    [Signal]
    public delegate void CardClickedEventHandler(int index);

    private static readonly PackedScene CardScene = GD.Load<PackedScene>("res://Scenes/Prefabs/Card.tscn");
    private const int CardCount = 5;
    private const float CardWidth = 120f;
    private const float CardGap = 12f;

    private CardController[] _cards = new CardController[5];

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
        AudioManager.Instance.PlaySfxByName("CardClick.wav");
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
        for (int i = 0; i < CardCount; i++)
        {
            if (!held[i])
            {
                _cards[i].SetCard(finalHand[i], i);
                _cards[i].AnimateReplace();
            }
        }
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

    public CardController GetCard(int index) => _cards[index];
}
