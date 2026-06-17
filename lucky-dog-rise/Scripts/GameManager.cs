using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using DataTables;

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
    private static readonly PackedScene ChipRewardScene = GD.Load<PackedScene>("res://Scenes/ChipReward.tscn");

    public GameState State { get; private set; } = GameState.WaitingForBet;
    public bool HasDogGivenHint => _dogHint.HasGivenHint;
    public Node2D PendingReward => _pendingReward;
    private int _pendingPayout;
    private Node2D _pendingReward;

    private GameData _gameData = null!;
    public GameData GameData
    {
        get => _gameData;
        set
        {
            _gameData = value;
            if (_hud != null && _chipStack != null)
            {
                RefreshUI();
                ApplyEquippedVisuals();
                _dogVisual.GameData = _gameData;
                _gameData.EquipmentChanged += ApplyEquippedVisuals;
                _hud.SetMessage("Click the chips to place your bet");
                _chipStack.ShowHint("Click to bet");
            }
        }
    }

    private DeckManager _deck = null!;
    private DogHintSystem _dogHint = null!;
    private bool[] _held = [true, true, true, true, true];

    private HUDController _hud = null!;
    private CardTableController _cardTable = null!;
    private DogVisual _dogVisual = null!;
    private ChipStackController _chipStack = null!;
    private HandAreaController _handArea = null!;
    private Marker2D _rewardSpawnPoint = null!;
    public SystemPanelController SettingsPanel { get; set; } = null!;

    public override void _Ready()
    {
        _deck = new DeckManager();
        _dogHint = new DogHintSystem();

        _hud = GetNode<HUDController>("HUD");
        _cardTable = GetNode<CardTableController>("CardArea");
        _dogVisual = GetNode<DogVisual>("DogArea");
        _chipStack = GetNode<ChipStackController>("ChipStack");
        _handArea = GetNode<HandAreaController>("HandArea");
        _rewardSpawnPoint = GetNode<Marker2D>("RewardSpawnPoint");
        _rewardSpawnPoint.GetNode<Sprite2D>("PreviewSprite").Visible = false;

        // 信号连接
        _dogVisual.DogClicked += OnDogClicked;
        _handArea.HandKnocked += OnDrawPressed;
        _chipStack.BetPlaced += OnBetPlaced;
        _cardTable.CardClicked += OnCardClicked;

        if (_gameData != null)
        {
            RefreshUI();
            _hud.SetMessage("Click the chips to place your bet");
            _chipStack.ShowHint("Click to bet");
        }

        AudioManager.Instance.PlayBgmByName("MainTheme.ogg");
    }

    // === 信号处理 ===

    private void OnBetPlaced()
    {
        if (State != GameState.WaitingForBet) return;
        if (!_gameData.CanAffordBet) { TriggerGameOver(); return; }
        if (SettingsPanel != null && SettingsPanel.TryGetFixedSeed(out int fixedSeed))
            _deck.SetFixedSeed(fixedSeed);
        DealNewHand();
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
        _pendingReward = null;
        _gameData.ModifyChips(_pendingPayout);
        _pendingPayout = 0;
        RefreshUI();
        State = GameState.WaitingForBet;
        OnBetPlaced();
    }

    // === 游戏逻辑 ===

    public void AddChips(int amount)
    {
        _gameData.ModifyChips(amount);
        RefreshUI();
    }

    private void DealNewHand()
    {
        _gameData.ModifyChips(-_gameData.BetAmount);
        _gameData.EmitNewHandStarted();
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

    private void DoDraw()
    {
        State = GameState.Drawing;
        _handArea.Enabled = false;
        RefreshUI();
        _cardTable.BrightenAll();
        var finalHand = _deck.DrawReplacements(_held);
        _cardTable.ReplaceCards(finalHand, _held);
        var rank = CardEvaluator.Evaluate(finalHand);
        int payout = CardEvaluator.GetPayout(finalHand, _gameData.BetAmount);
        _gameData.EmitHandResolved(rank, payout);
        if (payout > 0)
        {
            _pendingPayout = payout;
            _hud.SetMessage($"{rank}! Won {payout} chips! Click chips to collect.");
            SpawnChipReward(payout);
            State = GameState.Settled;
        }
        else
        {
            _hud.SetMessage("No win. Click chips to bet again.");
            State = GameState.WaitingForBet;
            _chipStack.ShowHint("Click to bet");
            if (!_gameData.CanAffordBet) { TriggerGameOver(); return; }
        }
        if (_gameData.Progression.CheckRankUp())
            _hud.ShowOverlay($"Rank Up: {_gameData.Progression.CurrentRank}!");
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
        SettingsPanel?.UpdateSeed(_deck.LastSeed);
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

    private void ApplyEquippedVisuals()
    {
        ApplyItemTexture(EItemType.Background, tex => GetNode<TextureRect>("Background").Texture = tex);
        ApplyItemTexture(EItemType.Table, tex => GetNode<TextureRect>("Table").Texture = tex);
        ApplyItemTexture(EItemType.Clothes, tex => _handArea.SetClothes(tex));
        ApplyItemTexture(EItemType.Accessory, tex => _handArea.SetAccessory(tex));
    }

    private void ApplyItemTexture(EItemType type, System.Action<Texture2D> apply)
    {
        var item = _gameData.Inventory.GetEquipped(type);
        if (item == null || item.AssetPathList.Count == 0) return;
        var tex = GD.Load<Texture2D>(PlayerInventory.ToResPath(item.AssetPathList[0]));
        if (tex != null) apply(tex);
    }

    public void OnRandomizeScene()
    {
        var rng = new Random();

        ApplyRandomFromInventory(EItemType.Background, rng,
            tex => GetNode<TextureRect>("Background").Texture = tex);
        ApplyRandomFromInventory(EItemType.Table, rng,
            tex => GetNode<TextureRect>("Table").Texture = tex);
        ApplyRandomFromInventory(EItemType.Clothes, rng,
            tex => _handArea.SetClothes(tex));
        ApplyRandomFromInventory(EItemType.Accessory, rng,
            tex => _handArea.SetAccessory(tex));
    }

    private void ApplyRandomFromInventory(EItemType type, Random rng, System.Action<Texture2D> apply)
    {
        var items = _gameData.Inventory.GetOwnedOfType(type).ToList();
        if (items.Count == 0) return;
        var picked = items[rng.Next(items.Count)];
        if (picked.AssetPathList.Count == 0) return;
        var tex = GD.Load<Texture2D>(PlayerInventory.ToResPath(picked.AssetPathList[0]));
        if (tex != null) apply(tex);
    }

    public void OnRandomizeDog()
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
