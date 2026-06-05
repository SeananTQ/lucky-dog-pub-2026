using Godot;
using System;

namespace LuckyDogRise;

public enum GameState
{
    Idle,
    Dealt,
    Holding,
    Drawing,
    Settled,
    GameOver
}

public partial class GameManager : Node2D
{
    private const int BetAmount = 5;
    private const int StartingChips = 100;
    private static readonly Color DimColor = new(0.6f, 0.6f, 0.6f, 1f);
    private const float DrawAnimDuration = 0.3f;

    public GameState State { get; private set; } = GameState.Idle;
    public int Chips { get; private set; }

    private DeckManager _deck = null!;
    private DogHintSystem _dogHint = null!;
    private ProgressionManager _progression = null!;

    private TextureRect[] _cardNodes = new TextureRect[5];
    private bool[] _held = new bool[5];
    private Button _dealButton = null!;
    private Button _drawButton = null!;
    private Label _chipLabel = null!;
    private Label _rankLabel = null!;
    private Label _betLabel = null!;
    private Label _messageLabel = null!;
    private CanvasLayer _overlay = null!;
    private Label _centerLabel = null!;

    private DogVisual _dogVisual = null!;
    private Button _dogButton = null!;

    public override void _Ready()
    {
        _deck = new DeckManager();
        _dogHint = new DogHintSystem();
        _progression = new ProgressionManager();
        Chips = StartingChips;

        for (int i = 0; i < 5; i++)
            _cardNodes[i] = GetNode<TextureRect>($"CardArea/Card{i}");

        _dealButton = GetNode<Button>("HUD/DealButton");
        _drawButton = GetNode<Button>("HUD/DrawButton");
        _chipLabel = GetNode<Label>("HUD/InfoPanel/MarginContainer/HBox/VBox/ChipLabel");
        _rankLabel = GetNode<Label>("HUD/RankPanel/RankLabel");
        _betLabel = GetNode<Label>("HUD/InfoPanel/MarginContainer/HBox/VBox/BetLabel");
        _messageLabel = GetNode<Label>("HUD/MessagePanel/MessageLabel");
        _overlay = GetNode<CanvasLayer>("Overlay");
        _centerLabel = GetNode<Label>("Overlay/OverlayPanel/OverlayVBox/CenterLabel");
        _dogVisual = GetNode<DogVisual>("DogArea");
        _dogButton = GetNode<Button>("HUD/DogButton");
        var handArea = GetNode<HandAreaController>("HandArea");

        _dealButton.Pressed += OnDealPressed;
        _drawButton.Pressed += OnDrawPressed;
        _dogButton.Pressed += OnDogClicked;
        handArea.HandKnocked += OnDrawPressed;

        for (int i = 0; i < 5; i++)
        {
            int index = i;
            _cardNodes[i].GuiInput += (e) => OnCardInput(e, index);
        }

        UpdateButtonStates();
        UpdateUI();
        SetMessage("Click DEAL to start!");
    }

    private void OnDealPressed()
    {
        switch (State)
        {
            case GameState.Idle:
            case GameState.Settled:
                StartNewHand();
                break;
            case GameState.GameOver:
                ResetGame();
                break;
        }
    }

    private void OnDrawPressed()
    {
        if (State == GameState.Dealt || State == GameState.Holding)
            DoDraw();
    }

    private void StartNewHand()
    {
        if (Chips < BetAmount)
        {
            TriggerGameOver();
            return;
        }

        Chips -= BetAmount;
        _deck.Deal();
        _held = new bool[5];
        _dogHint.ResetForNewHand();

        for (int i = 0; i < 5; i++)
        {
            _cardNodes[i].Modulate = DimColor;
            SetCardTexture(i, _deck.CurrentHand[i]);
        }

        State = GameState.Dealt;
        _dogVisual.ResetAppearance();
        SetMessage("Click cards to HOLD, then DRAW");
        UpdateButtonStates();
        UpdateUI();
    }

    private void OnCardInput(InputEvent e, int index)
    {
        if (State != GameState.Dealt && State != GameState.Holding) return;
        if (e is not InputEventMouseButton mb || !mb.Pressed) return;

        _held[index] = !_held[index];

        // Visual: held = normal, un-held = dimmed
        _cardNodes[index].Modulate = _held[index] ? Colors.White : DimColor;

        State = GameState.Holding;
        SetMessage(_held[index] ? $"Card {index + 1} HELD" : $"Card {index + 1} discarded");
    }

    private void OnDogClicked()
    {
        if (State != GameState.Dealt && State != GameState.Holding) return;

        if (_dogHint.HasGivenHint)
        {
            _dogVisual.ShowSunglasses();
            SetMessage("Dog: *puts on sunglasses* No more hints!");
            return;
        }

        var signal = _dogHint.EvaluateHold(_deck.CurrentHand, _held, _deck.PredeterminedRank);
        _dogVisual.ShowSignal(signal);
        _dogHint.HasGivenHint = true;
        SetMessage($"Dog: {GetSignalMessage(signal)}");
    }

    private void DoDraw()
    {
        State = GameState.Drawing;
        UpdateButtonStates();

        // Clear all hold visuals
        for (int i = 0; i < 5; i++)
            _cardNodes[i].Modulate = Colors.White;

        var finalHand = _deck.DrawReplacements(_held);

        // Animate new cards sliding in from below
        for (int i = 0; i < 5; i++)
        {
            if (!_held[i])
            {
                SetCardTexture(i, finalHand[i]);
                AnimateCardIn(i);
            }
        }

        var rank = CardEvaluator.Evaluate(finalHand);
        int payout = CardEvaluator.GetPayout(finalHand, BetAmount);

        if (payout > 0)
        {
            Chips += payout;
            SetMessage($"{rank}! Won {payout} chips!");
        }
        else
        {
            SetMessage("No win this hand.");
        }

        _progression.UpdateHighScore(Chips);
        State = GameState.Settled;

        if (_progression.CheckRankUp())
            ShowRankUp(_progression.CurrentRank);
        else if (Chips < BetAmount)
            TriggerGameOver();

        UpdateButtonStates();
        UpdateUI();
    }

    private void AnimateCardIn(int index)
    {
        var card = _cardNodes[index];
        card.Modulate = new Color(1, 1, 1, 0);

        var tween = CreateTween();
        tween.TweenProperty(card, "modulate:a", 1f, DrawAnimDuration)
            .SetEase(Tween.EaseType.Out);
    }

    private void TriggerGameOver()
    {
        State = GameState.GameOver;
        _overlay.Visible = true;
        _centerLabel.Text = DogProverbs.GetRandom();
        SetMessage("Game Over");
        UpdateButtonStates();
    }

    private void ShowRankUp(PlayerRank rank)
    {
        _overlay.Visible = true;
        _centerLabel.Text = $"Rank Up: {rank}!";
    }

    private void ResetForNextHand()
    {
        State = GameState.Idle;
        _held = new bool[5];
        for (int i = 0; i < 5; i++)
            _cardNodes[i].Modulate = Colors.White;
        _dogVisual.ResetAppearance();
        _overlay.Visible = false;
        SetMessage("Click DEAL to start!");
        UpdateButtonStates();
        UpdateUI();
    }

    private void ResetGame()
    {
        Chips = StartingChips;
        _progression.Reset();
        _overlay.Visible = false;
        ResetForNextHand();
    }

    private void UpdateButtonStates()
    {
        switch (State)
        {
            case GameState.Idle:
            case GameState.Settled:
                _dealButton.Disabled = false;
                _drawButton.Disabled = true;
                _dealButton.Text = State == GameState.Settled ? "NEXT HAND" : "DEAL";
                break;

            case GameState.Dealt:
            case GameState.Holding:
                _dealButton.Disabled = true;
                _drawButton.Disabled = false;
                break;

            case GameState.Drawing:
                _dealButton.Disabled = true;
                _drawButton.Disabled = true;
                break;

            case GameState.GameOver:
                _dealButton.Disabled = false;
                _dealButton.Text = "RESTART";
                _drawButton.Disabled = true;
                break;
        }
    }

    private void SetCardTexture(int index, int card)
    {
        var path = DeckManager.CardToAssetPath(card);
        var tex = GD.Load<Texture2D>(path);
        if (tex != null)
            _cardNodes[index].Texture = tex;
        else
            GD.PrintErr($"[Card] FAILED to load: {path}");
    }

    private void SetMessage(string msg)
    {
        _messageLabel.Text = msg;
    }

    private void UpdateUI()
    {
        _chipLabel.Text = $"Chips: {Chips}";
        _rankLabel.Text = $"Rank: {_progression.CurrentRank}";
        _betLabel.Text = $"Bet: {BetAmount}";
    }

    private static string GetSignalMessage(DogSignal signal)
    {
        return signal switch
        {
            DogSignal.Bored => "*yawns* Not looking great...",
            DogSignal.Happy => "*wags tail* Feeling good!",
            DogSignal.LuckyEye => "*eyes light up* Something big!",
            DogSignal.TopTier => "*ears perk up* INCREDIBLE!",
            _ => "...",
        };
    }
}
