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
    private static readonly PackedScene ChipRewardScene = GD.Load<PackedScene>("res://Scenes/ChipReward.tscn");

    public GameState State { get; private set; } = GameState.WaitingForBet;
    public int Chips { get; private set; }
    public bool DebugMode { get; set; } = true;
    public bool HasDogGivenHint => _dogHint.HasGivenHint;
    public Node2D PendingReward => _pendingReward;
    private int _pendingPayout;
    private Node2D _pendingReward;

    private DeckManager _deck = null!;
    private DogHintSystem _dogHint = null!;
    private ProgressionManager _progression = null!;
    private bool[] _held = [true, true, true, true, true];

    private HUDController _hud = null!;
    private DebugHUDController _debugHud = null!;
    private CardTableController _cardTable = null!;
    private DogVisual _dogVisual = null!;
    private ChipStackController _chipStack = null!;
    private HandAreaController _handArea = null!;
    private Marker2D _rewardSpawnPoint = null!;

    public override void _Ready()
    {
        _deck = new DeckManager();
        _dogHint = new DogHintSystem();
        _progression = new ProgressionManager();
        Chips = StartingChips;

        _hud = GetNode<HUDController>("HUD");
        _debugHud = GetNode<DebugHUDController>("HUD/DebugPanel");
        _cardTable = GetNode<CardTableController>("CardArea");
        _dogVisual = GetNode<DogVisual>("DogArea");
        _chipStack = GetNode<ChipStackController>("ChipStack");
        _handArea = GetNode<HandAreaController>("HandArea");
        _rewardSpawnPoint = GetNode<Marker2D>("RewardSpawnPoint");
        _rewardSpawnPoint.GetNode<Sprite2D>("PreviewSprite").Visible = false;

        // 信号连接
        _hud.ConnectDeal(this, nameof(OnDealPressed));
        _hud.ConnectDraw(this, nameof(OnDrawPressed));
        _dogVisual.DogClicked += OnDogClicked;
        _handArea.HandKnocked += OnDrawPressed;
        _chipStack.BetPlaced += OnBetPlaced;
        _cardTable.CardClicked += OnCardClicked;
        _debugHud.RandomizeRequested += OnRandomizeScene;

        RefreshUI();
        _hud.SetMessage("Click the chips to place your bet");
        _chipStack.ShowHint("Click to bet");

        // 启动BGM
        AudioManager.Instance.PlayBgmByName("MainTheme.ogg");
    }

    // === 信号处理 ===

    private void OnBetPlaced()
    {
        if (State != GameState.WaitingForBet) return;
        if (Chips < BetAmount) { TriggerGameOver(); return; }
        if (_debugHud.TryGetFixedSeed(out int fixedSeed))
            _deck.SetFixedSeed(fixedSeed);
        DealNewHand();
    }

    private void OnDealPressed()
    {
        switch (State)
        {
            case GameState.WaitingForBet: OnBetPlaced(); break;
            case GameState.Settled: StartNextHand(); break;
            case GameState.GameOver: ResetGame(); break;
        }
    }

    private void OnDrawPressed()
    {
        if (State == GameState.Dealt || State == GameState.Holding)
            CallDeferred(nameof(DoDraw));
    }

    private void OnCardClicked(int index)
    {
        if (State != GameState.Dealt && State != GameState.Holding) return;

        _held[index] = !_held[index];
        _cardTable.SetHeld(index, _held[index]);
        State = GameState.Holding;
        _hud.SetMessage(_held[index] ? $"Card {index + 1} HELD" : $"Card {index + 1} discarded");

        if (_dogHint.HasGivenHint && !_dogHint.IsLocked)
        {
            _dogHint.IsLocked = true;
            _dogVisual.ShowSunglasses();
            _hud.SetMessage("Dog: *puts on sunglasses* Locked in!");
        }
    }

    private void OnDogClicked()
    {
        if (State != GameState.Dealt && State != GameState.Holding) return;

        if (_dogHint.HasGivenHint)
        {
            _dogVisual.ShakePaw();
            _hud.SetMessage("Dog: *shakes paw* No more hints!");
            return;
        }

        var previewHand = _deck.PreviewFinalHand(_held);
        var signal = _dogHint.EvaluateHold(_deck.CurrentHand, _held, previewHand);
        _dogVisual.ShowSignal(signal);
        _dogHint.HasGivenHint = true;
        _hud.SetMessage($"Dog: {GetSignalMessage(signal)}");
    }

    private void OnChipCollected()
    {
        _hud.SetDealButtonVisible(true);
        _pendingReward = null;
        Chips += _pendingPayout;
        _progression.UpdateHighScore(Chips);
        _pendingPayout = 0;
        RefreshUI();
        State = GameState.WaitingForBet;
        OnBetPlaced();
    }

    // === 游戏逻辑 ===

    private void DealNewHand()
    {
        Chips -= BetAmount;
        _deck.Deal();
        _held = [true, true, true, true, true];
        _dogHint.ResetForNewHand();
        _chipStack.HideHint();
        _cardTable.DealCards(_deck.CurrentHand);
        State = GameState.Dealt;
        _dogVisual.ResetAppearance();
        _handArea.Enabled = false;  // 发牌动画期间禁止敲桌
        GetTree().CreateTimer(1.1f).Timeout += () => _handArea.Enabled = true;
        _hud.SetMessage("Click cards to HOLD, then knock to draw");
        RefreshUI();
    }

    private void StartNextHand()
    {
        if (Chips < BetAmount) { TriggerGameOver(); return; }
        DealNewHand();
    }

    private void DoDraw()
    {
        State = GameState.Drawing;
        _handArea.Enabled = false;
        RefreshUI();
        _cardTable.BrightenAll();
        var finalHand = _deck.DrawReplacements(_held);
        _cardTable.ReplaceCards(finalHand, _held);
        var rank = CardEvaluator.Evaluate(finalHand);
        int payout = CardEvaluator.GetPayout(finalHand, BetAmount);
        if (payout > 0)
        {
            _pendingPayout = payout;
            _hud.SetMessage($"{rank}! Won {payout} chips! Click chips to collect.");
            SpawnChipReward(payout);
            State = GameState.Settled;
            _hud.SetDealButtonVisible(false);
        }
        else
        {
            _hud.SetMessage("No win. Click chips to bet again.");
            State = GameState.WaitingForBet;
            _chipStack.ShowHint("Click to bet");
            if (Chips < BetAmount) { TriggerGameOver(); return; }
        }
        if (_progression.CheckRankUp())
            _hud.ShowOverlay($"Rank Up: {_progression.CurrentRank}!");
        RefreshUI();
    }

    private void TriggerGameOver()
    {
        State = GameState.GameOver;
        _hud.ShowOverlay(DogProverbs.GetRandom());
        _hud.SetMessage("Game Over");
        _chipStack.HideHint();
        RefreshUI();
    }

    private void ResetForNextHand()
    {
        State = GameState.WaitingForBet;
        _held = [true, true, true, true, true];
        _cardTable.BrightenAll();
        _dogVisual.ResetAppearance();
        _hud.HideOverlay();
        _hud.SetDealButtonVisible(true);
        _chipStack.ShowHint("Click to bet");
        _hud.SetMessage("Click the chips to place your bet");
        RefreshUI();
    }

    private void ResetGame()
    {
        Chips = StartingChips;
        _progression.Reset();
        _hud.HideOverlay();
        ResetForNextHand();
    }

    private void SpawnChipReward(int amount)
    {
        var reward = ChipRewardScene.Instantiate<ChipRewardController>();
        _rewardSpawnPoint.AddChild(reward);
        reward.Position = Vector2.Zero;
        reward.Setup(amount);
        reward.Collected += OnChipCollected;
        _pendingReward = reward;
    }

    private void RefreshUI()
    {
        _hud.UpdateButtons(State, DebugMode);
        _hud.UpdateInfo(Chips, _progression.CurrentRank, BetAmount);
        _debugHud.DebugEnabled = DebugMode;
        _debugHud.UpdateSeed(_deck.LastSeed);
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

    private void OnRandomizeScene()
    {
        var rng = new Random();

        var bg = GetRandomTexture("res://Assets/Background/", rng);
        if (bg != null) GetNode<TextureRect>("Background").Texture = bg;

        var table = GetRandomTexture("res://Assets/Table/", rng);
        if (table != null) GetNode<TextureRect>("Table").Texture = table;

        var clothes = GetRandomTexture("res://Assets/Clothes/", rng);
        if (clothes != null) _handArea.SetClothes(clothes);

        var accessory = GetRandomTexture("res://Assets/Accessory/", rng);
        if (accessory != null) _handArea.SetAccessory(accessory);
    }

    private static Texture2D GetRandomTexture(string dirPath, Random rng)
    {
        var dir = DirAccess.Open(dirPath);
        if (dir == null) return null;

        var files = new System.Collections.Generic.List<string>();
        dir.ListDirBegin();
        string fileName = dir.GetNext();
        while (fileName != "")
        {
            if (fileName.EndsWith(".png"))
                files.Add(fileName);
            fileName = dir.GetNext();
        }
        dir.ListDirEnd();

        if (files.Count == 0) return null;
        return GD.Load<Texture2D>(dirPath + files[rng.Next(files.Count)]);
    }
}
