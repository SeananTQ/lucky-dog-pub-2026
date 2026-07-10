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
}

public partial class GameManager : Node2D
{
    private static readonly PackedScene ChipRewardScene = GD.Load<PackedScene>("res://Scenes/ChipReward.tscn");
    private static readonly PackedScene BlindBoxRevealOverlayScene = GD.Load<PackedScene>("res://Scenes/BlindBoxRevealOverlay.tscn");

    [Signal] public delegate void BlindBoxRewardClaimRequestedEventHandler();

    public GameState State { get; private set; } = GameState.WaitingForBet;
    public bool HasDogGivenHint => _dogHint.HasGivenHint;
    public Node2D PendingReward => _pendingReward;
    private int _pendingPayout;
    private EHandRank _pendingRewardRank;
    private Node2D _pendingReward;

    [Export] private float _drawStartDelayAfterFirstKnock = 0.03f+0.22f;
    [Export] private float _rewardSpawnDelayFromLastReplaceStart = 0.08f;

    private GameData _gameData = null!;
    public GameData GameData
    {
        get => _gameData;
        set
        {
            _gameData = value;
            if (_hud != null && _chipStack != null)
            {
                _dogVisual.GameData = _gameData;
                RefreshUI();
                ApplyEquippedVisuals();
                _gameData.EquipmentChanged += ApplyEquippedVisuals;
                _hud.SetMessage("");
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
    private ItemAreaController _itemArea = null!;
    private Marker2D _rewardSpawnPoint = null!;
    private BlindBoxRevealOverlayController _blindBoxOverlay = null!;
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
        _itemArea = GetNode<ItemAreaController>("ItemArea");
        _rewardSpawnPoint = GetNode<Marker2D>("RewardSpawnPoint");
        _rewardSpawnPoint.GetNode<Sprite2D>("PreviewSprite").Visible = false;
        _blindBoxOverlay = BlindBoxRevealOverlayScene.Instantiate<BlindBoxRevealOverlayController>();
        AddChild(_blindBoxOverlay);
        _blindBoxOverlay.RewardClaimRequested += () => EmitSignal(SignalName.BlindBoxRewardClaimRequested);
        _blindBoxOverlay.RevealStepChanged += step => _gameData.SetPendingBlindBoxRevealStep(step);
        _blindBoxOverlay.RewardShown += () => _gameData.MarkPendingBlindBoxRewardShown();

        // 信号连接
        _dogVisual.DogClicked += OnDogClicked;
        _handArea.HandKnocked += OnDrawPressed;
        _handArea.FirstKnockLanded += OnFirstKnockLanded;
        _chipStack.BetPlaced += OnBetPlaced;
        _cardTable.CardClicked += OnCardClicked;
        _cardTable.LastReplacementStarted += OnLastReplacementStarted;

        if (_gameData != null)
        {
            RefreshUI();
            _hud.SetMessage("");
            _chipStack.ShowHint("Click to bet");
        }

        AudioManager.Instance.PlayBgmByName("MainTheme.ogg");
    }

    public void ShowPendingBlindBoxReward(PendingBlindBoxReward pending)
    {
        _blindBoxOverlay.ShowReward(pending, animateDrop: false);
    }

    public void HidePendingBlindBoxReward()
    {
        _blindBoxOverlay.HideOverlay();
    }

    // === 信号处理 ===

    private void OnBetPlaced()
    {
        if (State != GameState.WaitingForBet) return;
        if (!_gameData.CanAffordBet) { HandleInsufficientChips(); return; }
        if (SettingsPanel != null && SettingsPanel.TryGetFixedSeed(out int fixedSeed))
            _deck.SetFixedSeed(fixedSeed);
        DealNewHand();
    }

    private void OnDrawPressed()
    {
        // HandAreaController 会在第一下落地时通知，补牌不在点击瞬间启动。
    }

    private void OnFirstKnockLanded()
    {
        if (State != GameState.Dealt && State != GameState.Holding) return;
        GetTree().CreateTimer(_drawStartDelayAfterFirstKnock).Timeout += () =>
        {
            if (State == GameState.Dealt || State == GameState.Holding)
                DoDraw();
        };
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
            _dogVisual.ApplyReaction(EDogReactionTrigger.HintExhausted);
            _hud.SetMessage("");
        }
        else if (!_dogHint.HasGivenHint)
        {
            _dogVisual.ApplyReaction(EDogReactionTrigger.WaitingForHint);
        }
    }

    private void OnDogClicked()
    {
        if (State != GameState.Dealt && State != GameState.Holding) return;

        if (_dogHint.HasGivenHint)
        {
            _dogVisual.ApplyReaction(EDogReactionTrigger.RefuseHint);
            _hud.SetMessage("");
            return;
        }

        var previewHand = _deck.PreviewFinalHand(_held);
        var previewRank = _dogHint.EvaluateHoldRank(_deck.CurrentHand, _held, previewHand);
        _dogVisual.ApplyReaction(GetSawReaction(previewRank));
        _dogHint.HasGivenHint = true;
        _hud.SetMessage("");
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
        _dogVisual.ApplyReaction(EDogReactionTrigger.Dealt);
        _handArea.Enabled = false;  // 发牌动画期间禁止敲桌
        GetTree().CreateTimer(1.1f).Timeout += () => _handArea.Enabled = true;
        _hud.SetMessage("");
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
        _dogVisual.ApplyReaction(GetSawReaction(rank));
        if (payout > 0)
        {
            _pendingPayout = payout;
            _pendingRewardRank = rank;
            _hud.SetMessage("");
            State = GameState.Settled;
        }
        else
        {
            _hud.SetMessage("");
            State = GameState.WaitingForBet;
            _chipStack.ShowHint("Click to bet");
            if (!_gameData.CanAffordBet) { HandleInsufficientChips(); return; }
        }
        if (_gameData.Progression.CheckRankUp())
            _hud.ShowOverlay($"Rank Up: {_gameData.Progression.CurrentRank}!");
        RefreshUI();
    }

    private void HandleInsufficientChips()
    {
        State = GameState.WaitingForBet;
        _hud.HideOverlay();
        _hud.SetMessage("");
        _chipStack.ShowHint("Click to bet");
        RefreshUI();
    }

    private void SpawnChipReward(int amount, EHandRank rank)
    {
        var reward = ChipRewardScene.Instantiate<ChipRewardController>();
        _rewardSpawnPoint.AddChild(reward);
        reward.Position = Vector2.Zero;
        reward.Setup(amount, rank);
        reward.Collected += OnChipCollected;
        _pendingReward = reward;
    }

    private void OnLastReplacementStarted()
    {
        if (_pendingPayout <= 0) return;

        GetTree().CreateTimer(Mathf.Max(0f, _rewardSpawnDelayFromLastReplaceStart)).Timeout += () =>
        {
            if (_pendingPayout <= 0 || IsQueuedForDeletion()) return;
            int payout = _pendingPayout;
            EHandRank rank = _pendingRewardRank;
            SpawnChipReward(payout, rank);
        };
    }

    private void RefreshUI()
    {
        SettingsPanel?.UpdateSeed(_deck.LastSeed);
    }

    private static EDogReactionTrigger GetSawReaction(EHandRank rank)
    {
        if (rank == EHandRank.Nothing)
            return EDogReactionTrigger.SawNothing;

        var payTable = LubanData.Tables.TbPayTable.DataList.FirstOrDefault(row => row.HandRank == rank);
        if (payTable == null)
            return EDogReactionTrigger.SawNothing;

        return (EDogReactionTrigger)(3000 + (int)payTable.HandRank);
    }

    private void ApplyEquippedVisuals()
    {
        _dogVisual.RefreshEquippedVisuals();
        ApplyItemTexture(EItemType.Background, (tex, _) => GetNode<TextureRect>("Background").Texture = tex);
        ApplyItemTexture(EItemType.Table, (tex, _) => GetNode<TextureRect>("Table").Texture = tex);
        ApplyItemTexture(EItemType.Arm, (tex, name) => _handArea.SetArm(tex, name));
        ApplyItemTexture(EItemType.Clothes, (tex, name) => _handArea.SetClothes(tex, name), () => _handArea.SetClothes(null, ""));
        ApplyItemTexture(EItemType.Accessory, (tex, name) => _handArea.SetAccessory(tex, name), () => _handArea.SetAccessory(null, ""));
        ApplyItemTexture(EItemType.Refreshment, (tex, name) => _itemArea.SetTreat(tex, name), _itemArea.ClearTreat);
        _dogVisual.RefreshEquippedHeadwear();
        _dogVisual.RefreshEquippedEyewear();
    }

    private void ApplyItemTexture(EItemType type, System.Action<Texture2D, string> apply, Action clear = null)
    {
        var item = _gameData.Inventory.GetEquipped(type);
        if (item == null || item.AssetPathList.Count == 0)
        {
            clear?.Invoke();
            return;
        }
        var tex = GD.Load<Texture2D>(PlayerInventory.ToResPath(item.AssetPathList[0]));
        if (tex == null)
        {
            clear?.Invoke();
            return;
        }
        var fileName = item.AssetPathList[0].Split('\\').Last();
        apply(tex, fileName);
    }

    public void OnPlayDogReaction(int trigger)
    {
        _dogVisual.ApplyReaction((EDogReactionTrigger)trigger);
        _hud.SetMessage("");
    }
}
