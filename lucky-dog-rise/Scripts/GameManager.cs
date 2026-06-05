using Godot;
using System;

namespace LuckyDogRise;

public enum GameState
{
    WaitingForBet,
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
    private static readonly PackedScene ChipRewardScene = GD.Load<PackedScene>("res://Scenes/ChipReward.tscn");

    public GameState State { get; private set; } = GameState.WaitingForBet;
    public int Chips { get; private set; }
    public bool GuideEnabled { get; set; } = true;
    private int _pendingPayout;
    private Node2D _pendingReward;

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
    private ChipStackController _chipStack = null!;

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
        _chipStack = GetNode<ChipStackController>("ChipStack");

        _dealButton.Pressed += OnDealPressed;
        _drawButton.Pressed += OnDrawPressed;
        _dogButton.Pressed += OnDogClicked;
        handArea.HandKnocked += OnDrawPressed;
        _chipStack.BetPlaced += OnBetPlaced;

        for (int i = 0; i < 5; i++)
        {
            int index = i;
            _cardNodes[i].GuiInput += (e) => OnCardInput(e, index);
        }

        UpdateButtonStates();
        UpdateUI();
        SetMessage("Click the chips to place your bet");
        _chipStack.ShowHint("Click to bet");
    }

    private void OnBetPlaced()
    {
        if (State != GameState.WaitingForBet) return;
        if (Chips < BetAmount)
        {
            TriggerGameOver();
            return;
        }

        Chips -= BetAmount;
        _deck.Deal();
        _held = new bool[5];
        _dogHint.ResetForNewHand();
        _chipStack.HideHint();

        for (int i = 0; i < 5; i++)
        {
            _cardNodes[i].Modulate = DimColor;
            SetCardTexture(i, _deck.CurrentHand[i]);
        }

        State = GameState.Dealt;
        _dogVisual.ResetAppearance();
        SetMessage("Click cards to HOLD, then knock to draw");
        UpdateButtonStates();
        UpdateUI();
    }

    private void OnDealPressed()
    {
        switch (State)
        {
            case GameState.WaitingForBet:
                OnBetPlaced();
                break;
            case GameState.Settled:
                StartNextHand();
                break;
            case GameState.GameOver:
                ResetGame();
                break;
        }
    }

    private void OnDrawPressed()
    {
        if (State == GameState.Dealt || State == GameState.Holding)
            CallDeferred(nameof(DoDraw));
    }

    private void StartNextHand()
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
        _chipStack.HideHint();
        SetMessage("Click cards to HOLD, then knock to draw");
        UpdateButtonStates();
        UpdateUI();
    }

    private void OnCardInput(InputEvent e, int index)
    {
        if (State != GameState.Dealt && State != GameState.Holding) return;
        if (e is not InputEventMouseButton mb || !mb.Pressed) return;

        GetViewport().SetInputAsHandled();
        _held[index] = !_held[index];
        _cardNodes[index].Modulate = _held[index] ? Colors.White : DimColor;

        State = GameState.Holding;
        SetMessage(_held[index] ? $"Card {index + 1} HELD" : $"Card {index + 1} discarded");

        // 狗给过提示后，玩家改了保留牌 → 自动戴墨镜
        if (_dogHint.HasGivenHint && !_dogHint.IsLocked)
        {
            _dogHint.IsLocked = true;
            _dogVisual.ShowSunglasses();
            SetMessage("Dog: *puts on sunglasses* Locked in!");
        }
    }

    private void OnDogClicked()
    {
        if (State != GameState.Dealt && State != GameState.Holding) return;
        if (_dogHint.HasGivenHint) return;

        var previewHand = _deck.PreviewFinalHand(_held);
        var signal = _dogHint.EvaluateHold(_deck.CurrentHand, _held, previewHand);
        _dogVisual.ShowSignal(signal);
        _dogHint.HasGivenHint = true;
        SetMessage($"Dog: {GetSignalMessage(signal)}");
    }

    private void DoDraw()
    {
        State = GameState.Drawing;
        UpdateButtonStates();

        for (int i = 0; i < 5; i++)
            _cardNodes[i].Modulate = Colors.White;

        var finalHand = _deck.DrawReplacements(_held);

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
            _pendingPayout = payout;
            SetMessage($"{rank}! Won {payout} chips! Click chips to collect.");
            SpawnChipReward(payout);
            State = GameState.Settled;
            _dealButton.Visible = false;
        }
        else
        {
            SetMessage("No win. Click chips to bet again.");
            State = GameState.WaitingForBet;
            _chipStack.ShowHint("Click to bet");

            if (Chips < BetAmount)
            {
                TriggerGameOver();
                return;
            }
        }

        if (_progression.CheckRankUp())
            ShowRankUp(_progression.CurrentRank);

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

    private void SpawnChipReward(int amount)
    {
        var reward = ChipRewardScene.Instantiate<ChipRewardController>();
        AddChild(reward);
        reward.Position = new Vector2(600, 900);
        reward.Setup(amount);
        reward.Collected += OnChipCollected;
        _pendingReward = reward;
    }

    private void OnChipCollected()
    {
        _dealButton.Visible = true;
        _pendingReward = null;
        Chips += _pendingPayout;
        _progression.UpdateHighScore(Chips);
        _pendingPayout = 0;
        UpdateUI();
        State = GameState.WaitingForBet;
        OnBetPlaced();
    }

    private void TriggerGameOver()
    {
        State = GameState.GameOver;
        _overlay.Visible = true;
        _centerLabel.Text = DogProverbs.GetRandom();
        SetMessage("Game Over");
        _chipStack.HideHint();
        UpdateButtonStates();
    }

    private void ShowRankUp(PlayerRank rank)
    {
        _overlay.Visible = true;
        _centerLabel.Text = $"Rank Up: {rank}!";
    }

    private void ResetForNextHand()
    {
        State = GameState.WaitingForBet;
        _held = new bool[5];
        for (int i = 0; i < 5; i++)
            _cardNodes[i].Modulate = Colors.White;
        _dogVisual.ResetAppearance();
        _overlay.Visible = false;
        _dealButton.Visible = true;
        _chipStack.ShowHint("Click to bet");
        SetMessage("Click the chips to place your bet");
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
            case GameState.WaitingForBet:
                _dealButton.Disabled = false;
                _dealButton.Text = "DEAL";
                _drawButton.Disabled = true;
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

            case GameState.Settled:
                _dealButton.Disabled = false;
                _dealButton.Text = "NEXT HAND";
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

    // 新手引导：点击空白处时，正确的交互元素会弹跳提示
    public override void _UnhandledInput(InputEvent @event)
    {
        if (!GuideEnabled) return;
        if (@event is not InputEventMouseButton mb || !mb.Pressed) return;

        switch (State)
        {
            case GameState.WaitingForBet:
                BounceNode(_chipStack);
                break;

            case GameState.Dealt:
            case GameState.Holding:
                if (!_dogHint.HasGivenHint)
                    BounceNode(_dogVisual);
                else
                    BounceNode(GetNode<Node2D>("HandArea"));
                break;

            case GameState.Settled:
                if (_pendingReward != null && IsInstanceValid(_pendingReward))
                    BounceNode(_pendingReward);
                break;
        }
    }

    private void BounceNode(Node2D node)
    {
        var tween = CreateTween();
        var origY = node.Position.Y;
        tween.TweenProperty(node, "position:y", origY - 12, 0.08)
            .SetEase(Tween.EaseType.Out)
            .SetTrans(Tween.TransitionType.Quad);
        tween.TweenProperty(node, "position:y", origY, 0.1)
            .SetEase(Tween.EaseType.Out)
            .SetTrans(Tween.TransitionType.Bounce);
    }
}
