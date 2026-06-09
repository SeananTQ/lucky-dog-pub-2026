using Godot;
using System;
using System.Collections.Generic;

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
    private WindowManager _windowManager = null!;
    private DragHandler _dragHandler = null!;
    private TestSettingPanelController _settingsPanel = null!;
    private GlobalInputTracker _globalInput = null!;
    private TaskbarSnap _taskbarSnap = null!;
    private BossKeyController _bossKey = null!;

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
        _debugHud.RandomizeDogRequested += OnRandomizeDog;

        RefreshUI();
        _hud.SetMessage("Click the chips to place your bet");
        _chipStack.ShowHint("Click to bet");

        // 启动BGM
        AudioManager.Instance.PlayBgmByName("MainTheme.ogg");

        // === 桌宠宿主窗口初始化 ===
        SetupDesktopMode();
    }

    private void SetupDesktopMode()
    {
        Position = new Vector2(WindowManager.GameViewOffsetX, WindowManager.GameViewOffsetY);
        _hud.Offset = new Vector2(WindowManager.GameViewOffsetX, WindowManager.GameViewOffsetY);

        _windowManager = new WindowManager();
        _windowManager.Name = "WindowManager";
        AddChild(_windowManager);

        _dragHandler = new DragHandler();
        _dragHandler.Name = "DragHandler";
        AddChild(_dragHandler);
        _dragHandler.DragEnded += () => { };

        _settingsPanel = GD.Load<PackedScene>("res://Scenes/TestSettingPanel.tscn").Instantiate<TestSettingPanelController>();
        _settingsPanel.Name = "SettingsPanel";
        _settingsPanel.Layer = 100;
        AddChild(_settingsPanel);

        CreateSettingsButton();

        // 全局输入钩子（打字统计）
        _globalInput = new GlobalInputTracker();
        _globalInput.Name = "GlobalInputTracker";
        AddChild(_globalInput);

        // 任务栏吸附
        _taskbarSnap = new TaskbarSnap();
        _taskbarSnap.Name = "TaskbarSnap";
        AddChild(_taskbarSnap);
    }

    private void CreateSettingsButton()
    {
        var btn = new Button();
        btn.Text = "⚙";
        btn.Flat = true;
        btn.SetPosition(new Vector2(1150, 10));
        btn.SetSize(new Vector2(40, 40));
        btn.AddThemeFontSizeOverride("font_size", 24);
        btn.Pressed += () => _settingsPanel.Toggle();
        _hud.AddChild(btn);
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

    public void AddChips(int amount)
    {
        Chips += amount;
        _progression.UpdateHighScore(Chips);
        RefreshUI();
    }

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

    private void OnRandomizeDog()
    {
        var (eyewears, headwears) = LoadAccessoryEntries();
        if (eyewears.Count == 0 || headwears.Count == 0) return;

        var rng = new Random();
        var eye = eyewears[rng.Next(eyewears.Count)];
        var head = headwears[rng.Next(headwears.Count)];

        _dogVisual.ResetAppearance();
        _dogVisual.ShowClawPalm();
        _dogVisual.SetEyewear(eye.file, eye.scenePos);
        _dogVisual.SetHeadwear(head.file, head.scenePos);
        _hud.SetMessage($"Dog: {eye.name} + {head.name}");
    }

    private record struct AssetEntry(string name, string file, Vector2 scenePos)
    {
        // centerX = x + w/2, centerY = y + h/2
        // Eyewear: scenePos = (centerX - 586, centerY - 677)
        // Headwear: scenePos = (centerX - 585, centerY - 683)
        public static AssetEntry FromJson(Godot.Collections.Dictionary d, bool isHeadwear)
        {
            var name = d["name"].AsString();
            var file = d["file"].AsString().Split('/')[^1]; // "Eyewear/EyePatch.png" → "EyePatch.png"
            var cx = (float)d["x"].AsDouble() + (float)d["w"].AsDouble() / 2f;
            var cy = (float)d["y"].AsDouble() + (float)d["h"].AsDouble() / 2f;
            var scenePos = isHeadwear
                ? new Vector2(cx - 585f, cy - 683f)
                : new Vector2(cx - 586f, cy - 677f);
            return new AssetEntry(name, file, scenePos);
        }
    }

    private static (List<AssetEntry> eyewear, List<AssetEntry> headwear) LoadAccessoryEntries()
    {
        var eyewear = new List<AssetEntry>();
        var headwear = new List<AssetEntry>();

        using var file = FileAccess.Open("res://Assets/layer_index.json", FileAccess.ModeFlags.Read);
        if (file == null) return (eyewear, headwear);

        var json = new Json();
        if (json.Parse(file.GetAsText()) != Error.Ok) return (eyewear, headwear);

        var layers = json.Data.AsGodotDictionary()["layers"].AsGodotArray();
        foreach (var layer in layers)
        {
            var d = layer.AsGodotDictionary();
            var name = d["name"].AsString();
            if (name.StartsWith("Eyewear/"))
                eyewear.Add(AssetEntry.FromJson(d, false));
            else if (name.StartsWith("Headwear/"))
                headwear.Add(AssetEntry.FromJson(d, true));
        }

        return (eyewear, headwear);
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
