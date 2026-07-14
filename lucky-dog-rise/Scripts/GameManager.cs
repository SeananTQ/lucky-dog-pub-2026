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
    [Export] private float _chipStackAppearanceDelayFromLastReplaceStart = 0.08f;

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
    private InteractionHintController _interactionHints = null!;
    private ItemAreaController _itemArea = null!;
    private Marker2D _rewardSpawnPoint = null!;
    private BlindBoxRevealOverlayController _blindBoxOverlay = null!;
    private bool _isPokerModeActive;
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
        _interactionHints = GetNode<InteractionHintController>("InteractionHints");
        _interactionHints.RegisterTarget(InteractionHintTargetId.BetStack, _chipStack);
        _interactionHints.RegisterTarget(InteractionHintTargetId.HandConfirm, _handArea);
        _interactionHints.RegisterTarget(InteractionHintTargetId.CardSelection, _cardTable);
        _interactionHints.RegisterTarget(InteractionHintTargetId.DogAdvice, _dogVisual);
        _interactionHints.SetProactiveHintsEnabled(SettingsManager.LoadProactiveInteractionHints());
        SettingsManager.ProactiveInteractionHintsChanged += _interactionHints.SetProactiveHintsEnabled;
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
        RefreshInteractionHintTargets();

        if (_gameData != null)
        {
            RefreshUI();
            _hud.SetMessage("");
            _chipStack.ShowHint("Click to bet");
        }

        AudioManager.Instance.PlayRandomBgm();
    }

    public override void _ExitTree()
    {
        SettingsManager.ProactiveInteractionHintsChanged -= _interactionHints.SetProactiveHintsEnabled;
    }

    public override void _Process(double delta)
    {
        if (_interactionHints == null || _blindBoxOverlay == null)
            return;

        var isPokerInputAvailable = _isPokerModeActive && !_blindBoxOverlay.Visible;
        _interactionHints.SetInputContextActive(isPokerInputAvailable);
        _interactionHints.SetProactiveHintContextActive(isPokerInputAvailable);
    }

    public void SetInteractionHintPokerModeActive(bool active)
    {
        _isPokerModeActive = active;
        if (_interactionHints != null && _blindBoxOverlay != null)
        {
            var isPokerInputAvailable = active && !_blindBoxOverlay.Visible;
            _interactionHints.SetInputContextActive(isPokerInputAvailable);
            _interactionHints.SetProactiveHintContextActive(isPokerInputAvailable);
        }
    }

    public void ShowPendingBlindBoxReward(PendingBlindBoxReward pending)
    {
        // 盲盒表演覆盖牌桌时，不应让后台尚未落地的下注筹码继续播落地音效。
        _chipStack.CompleteAppearanceSilently();
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
        _interactionHints.NotifyInteractionHandled();
        if (!_gameData.CanAffordBet) { HandleInsufficientChips(); return; }
#if DEBUG
        if (SettingsPanel != null && SettingsPanel.TryGetFixedSeed(out int fixedSeed))
            _deck.SetFixedSeed(fixedSeed);
#endif
        DealNewHand();
    }

    private void OnDrawPressed()
    {
        // HandAreaController 会在第一下落地时通知，补牌不在点击瞬间启动。
        if (State == GameState.Dealt || State == GameState.Holding)
            _interactionHints.NotifyInteractionHandled();
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
        _interactionHints.NotifyInteractionHandled();

        _held[index] = !_held[index];
        _cardTable.SetHeld(index, _held[index]);
        SetState(GameState.Holding);
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
            // 已展示过询问结果：狗头本身无效。
            // 但如果玩家随后改过牌，第一次再点狗头需要展示拒绝状态。
            if (_dogHint.IsLocked && !_dogHint.HasRefusedAfterLock)
            {
                _dogHint.HasRefusedAfterLock = true;
                _interactionHints.NotifyInteractionHandled();
                _dogVisual.ApplyReaction(EDogReactionTrigger.RefuseHint);
            }
            return;
        }

        _interactionHints.NotifyInteractionHandled();

        var previewHand = _deck.PreviewFinalHand(_held);
        var previewRank = _dogHint.EvaluateHoldRank(_deck.CurrentHand, _held, previewHand);
        _dogVisual.ApplyReaction(GetSawReaction(previewRank));
        _dogHint.HasGivenHint = true;
        RefreshInteractionHintTargets();
        _hud.SetMessage("");
    }

    private void OnChipCollected()
    {
        _pendingReward = null;
        _gameData.ModifyChips(_pendingPayout);
        _pendingPayout = 0;
        RefreshUI();
        SetState(GameState.WaitingForBet);
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
        _deck.Deal(_gameData.TryConsumeLuckyDealBuff(out float triggerChance) ? triggerChance : null);
        _held = [true, true, true, true, true];
        _dogHint.ResetForNewHand();
        _chipStack.HideHint();
        _cardTable.DealCards(_deck.CurrentHand);
        SetState(GameState.Dealt);
        _dogVisual.ApplyReaction(EDogReactionTrigger.Dealt);
        _handArea.Enabled = false;  // 发牌动画期间禁止敲桌
        GetTree().CreateTimer(1.1f).Timeout += () => _handArea.Enabled = true;
        _hud.SetMessage("");
        RefreshUI();
    }

    private void DoDraw()
    {
        SetState(GameState.Drawing);
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
            SetState(GameState.Settled);
        }
        else
        {
            _hud.SetMessage("");
            // 等最后一张补牌开始翻面后，再与奖励筹码共用同一时序显示下注筹码。
            // 在此之前保持 Drawing，避免下注筹码提前出现。
        }
        if (_gameData.Progression.CheckRankUp())
            _hud.ShowOverlay($"Rank Up: {_gameData.Progression.CurrentRank}!");
        RefreshUI();
    }

    private void HandleInsufficientChips()
    {
        SetState(GameState.WaitingForBet);
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
        reward.InteractionActivated += _interactionHints.NotifyInteractionHandled;
        _pendingReward = reward;
        _interactionHints.RegisterTarget(InteractionHintTargetId.RewardStack, reward);
        RefreshInteractionHintTargets();
    }

    private void OnLastReplacementStarted(bool hasReplacement)
    {
        void ShowNextChipState()
        {
            if (IsQueuedForDeletion()) return;

            if (_pendingPayout > 0)
            {
                int payout = _pendingPayout;
                EHandRank rank = _pendingRewardRank;
                SpawnChipReward(payout, rank);
                return;
            }

            if (State != GameState.Drawing) return;
            SetState(GameState.WaitingForBet);
            _chipStack.ShowHint("Click to bet");
            if (!_gameData.CanAffordBet)
                HandleInsufficientChips();
            RefreshUI();
        }

        if (!hasReplacement)
        {
            ShowNextChipState();
            return;
        }

        GetTree().CreateTimer(Mathf.Max(0f, _chipStackAppearanceDelayFromLastReplaceStart)).Timeout += ShowNextChipState;
    }

    private void RefreshUI()
    {
#if DEBUG
        SettingsPanel?.UpdateSeed(_deck.LastSeed);
#endif
    }

    private void SetState(GameState state)
    {
        State = state;
        RefreshInteractionHintTargets();
    }

    private void RefreshInteractionHintTargets()
    {
        if (_interactionHints == null)
            return;

        switch (State)
        {
            case GameState.WaitingForBet:
                _interactionHints.SetAvailableTargets(InteractionHintTargetId.BetStack);
                break;
            case GameState.Settled when _pendingReward != null && IsInstanceValid(_pendingReward):
                _interactionHints.SetAvailableTargets(InteractionHintTargetId.RewardStack);
                break;
            case GameState.Dealt:
            case GameState.Holding:
                _interactionHints.SetAvailableTargets(GetPlayDecisionHintTarget());
                break;
            default:
                _interactionHints.SetAvailableTargets();
                break;
        }
    }

    private InteractionHintTargetId GetPlayDecisionHintTarget()
    {
        var faceUpCards = _deck.CurrentHand
            .Where((_, index) => _held[index])
            .ToArray();

        if (CanFaceUpCardsScore(faceUpCards))
            return InteractionHintTargetId.HandConfirm;

        if (!_held.Any(isHeld => !isHeld))
            return InteractionHintTargetId.CardSelection;

        return _dogHint.HasGivenHint
            ? InteractionHintTargetId.HandConfirm
            : InteractionHintTargetId.DogAdvice;
    }

    /// <summary>
    /// 正面牌不足五张时，不把未出现的补牌纳入判断；只识别当前已经形成的可计分组合。
    /// </summary>
    private static bool CanFaceUpCardsScore(int[] faceUpCards)
    {
        if (faceUpCards.Length == 5)
            return CardEvaluator.Evaluate(faceUpCards) != EHandRank.Nothing;

        var groups = faceUpCards
            .Select(CardEvaluator.GetRank)
            .GroupBy(rank => rank)
            .Select(group => new { Rank = group.Key, Count = group.Count() })
            .OrderByDescending(group => group.Count)
            .ToArray();

        if (groups.Length == 0)
            return false;

        if (groups.Any(group => group.Count >= 3))
            return true;

        if (groups.Count(group => group.Count >= 2) >= 2)
            return true;

        return groups.Any(group => group.Count >= 2 && IsJacksOrBetter(group.Rank));
    }

    private static bool IsJacksOrBetter(int rank) => rank == 0 || rank >= 10;

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
