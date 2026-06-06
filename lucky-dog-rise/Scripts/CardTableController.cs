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
    }

    private void OnCardClicked(int index)
    {
        AudioManager.Instance.PlaySfxByName("CardClick.wav");
        EmitSignal(SignalName.CardClicked, index);
    }

    public void DealCards(int[] hand)
    {
        float perCardDuration = 0.15f; // 每张牌间隔（小于总时长就有重叠）
        for (int i = 0; i < CardCount; i++)
        {
            _cards[i].SetCard(hand[i], i);
            _cards[i].ResetModulate();
            _cards[i].ShowBack();
            GD.Print($"[Deal] Card {i} delay = {i * perCardDuration}s");
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

    public void DimAll()
    {
        for (int i = 0; i < CardCount; i++)
            _cards[i].SetHeld(false);
    }

    public void BrightenAll()
    {
        for (int i = 0; i < CardCount; i++)
        {
            _cards[i].SetHeld(true);
            _cards[i].ResetModulate();
        }
    }

    public CardController GetCard(int index) => _cards[index];
}
