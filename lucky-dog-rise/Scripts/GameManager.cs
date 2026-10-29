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
    private static readonly PackedScene ChipRewardScene = GD.Load<PackedScene>("res://Scenes/Poker/ChipReward.tscn");
    private static readonly PackedScene BlindBoxRevealOverlayScene = GD.Load<PackedScene>("res://Scenes/Poker/BlindBoxRevealOverlay.tscn");

    [Signal] public delegate void BlindBoxRewardClaimRequestedEventHandler();
    [Signal] public delegate void InsufficientBetAttemptedEventHandler();
    [Signal] public delegate void CollectionProgressConfirmedEventHandler();
    [Signal] public delegate void DesktopModeRequestedEventHandler();
    [Signal] public delegate void PokerWelcomeConfirmedEventHandler();
    [Signal] public delegate void PokerQuickBarFunctionActivatedEventHandler();

    public bool IsPokerWelcomeVisible => _pokerWelcome?.IsOverlayVisible == true;
    public event Action<bool> PokerWelcomeVisibilityChanged;

    public bool IsCollectionProgressNoticeVisible => _collectionProgressDialog?.IsOverlayVisible == true;
    public bool IsTutorialOverlayVisible => _tutorial?.IsOverlayVisible == true;
    public bool IsPokerHandShowcaseVisible => _pokerHandShowcase?.IsOverlayVisible == true;

    public event Action<bool> TutorialOverlayVisibilityChanged;
    public event Action<bool> PokerHandShowcaseVisibilityChanged;

    public GameState State { get; private set; } = GameState.WaitingForBet;
    public bool HasDogGivenHint => _dogHint.HasGivenHint;
    public Node2D PendingReward => _pendingReward;
    private int _pendingPayout;
    private EHandRank _pendingRewardRank;
    private Node2D _pendingReward;
    private PlayerProgressSource _currentHandProgressSource = PlayerProgressSource.Gameplay;

    [Export] private float _drawStartDelayAfterFirstKnock = 0.03f+0.22f;
    [Export] private float _chipStackAppearanceDelayFromLastReplaceStart = 0.08f;

    private GameData _gameData = null!;
    public GameData GameData
    {
        get => _gameData;
        set
        {
            if (_gameData == value)
                return;

            DetachGameData();
            _gameData = value;
            AttachGameData();
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
    private TutorialManager _tutorial = null!;
    private ItemAreaController _itemArea = null!;
    private Marker2D _rewardSpawnPoint = null!;
    private BlindBoxRevealOverlayController _blindBoxOverlay = null!;
    [Export] private PokerHandShowcaseController _pokerHandShowcase = null!;
    [Export] private PokerQuickBarController _quickBar = null!;
    [Export] private PokerWelcomeDialogController _pokerWelcome = null!;
    [Export] private PersistentTipController _drawTip = null!;
    [Export] private PersistentTipController _helpBarTip = null!;
    public PersistentTipProgress PersistentTips { get; set; }
    private readonly PersistentTipProgress _sandboxTips = new();
    private PersistentTipProgress CurrentTips
    {
        get
        {
#if DEBUG
            if (_gameData == null || !_gameData.IsUsingLocalSave || _gameData.IsBlindBoxLocalTestMode
                || _gameData.IsSteamMockSimulationActive) return _sandboxTips;
#endif
            return PersistentTips;
        }
    }
    public bool IsPointOverPersistentTip(Vector2 position) => _drawTip?.ContainsPoint(position) == true
        || _helpBarTip?.ContainsPoint(position) == true;
    private PokerCollectionProgressDialogController _collectionProgressDialog = null!;
    private bool _isPokerModeActive;
    private SystemPanelController _settingsPanel;
    public SystemPanelController SettingsPanel
    {
        get => _settingsPanel;
        set
        {
            _settingsPanel = value;
        }
    }

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
        _interactionHints = GetNode<InteractionHintController>("InteractionHints");
        _tutorial = GetNode<TutorialManager>("TutorialManager");
        _interactionHints.EffectiveInteractionOccurred += OnEffectivePokerInteraction;
        _tutorial.OverlayVisibilityChanged += OnTutorialOverlayVisibilityChanged;
        _interactionHints.RegisterTarget(InteractionHintKeys.WaitingForBetBetStack, _chipStack);
        _interactionHints.RegisterTarget(InteractionHintKeys.WaitingForBetRefreshment, _itemArea);
        _interactionHints.RegisterTarget(InteractionHintKeys.RefreshmentUseConfirm, _itemArea);
        _interactionHints.RegisterTarget(InteractionHintKeys.DealtCardSelection, _cardTable);
        _interactionHints.RegisterTarget(InteractionHintKeys.DealtDogAdvice, _dogVisual);
        _interactionHints.RegisterTarget(InteractionHintKeys.DealtHandConfirm, _handArea);
        _interactionHints.RegisterTarget(InteractionHintKeys.HoldingHandConfirm, _handArea);
        _itemArea.InteractionActivated += _interactionHints.NotifyInteractionHandled;
        _itemArea.InteractionIgnored += _interactionHints.NotifyInteractionIgnored;
        _itemArea.HintContextChanged += RefreshInteractionHintTargets;
        _interactionHints.SetProactiveHintsEnabled(SettingsManager.LoadProactiveInteractionHints());
        SettingsManager.ProactiveInteractionHintsChanged += _interactionHints.SetProactiveHintsEnabled;
        _rewardSpawnPoint = GetNode<Marker2D>("RewardSpawnPoint");
        _rewardSpawnPoint.GetNode<Sprite2D>("PreviewSprite").Visible = false;
        _blindBoxOverlay = BlindBoxRevealOverlayScene.Instantiate<BlindBoxRevealOverlayController>();
        AddChild(_blindBoxOverlay);
        _blindBoxOverlay.RewardClaimRequested += () => EmitSignal(SignalName.BlindBoxRewardClaimRequested);
        _blindBoxOverlay.RevealStepChanged += step => _gameData.SetPendingBlindBoxRevealStep(step);
        _blindBoxOverlay.RewardShown += () => _gameData.MarkPendingBlindBoxRewardShown();
        _pokerHandShowcase.ShowcaseVisibilityChanged += OnPokerHandShowcaseVisibilityChanged;
        _quickBar.HelpRequested += OnQuickBarHelpRequested;
        _quickBar.TutorialRequested += OnQuickBarTutorialRequested;
        _quickBar.DesktopRequested += OnQuickBarDesktopRequested;
        _quickBar.InterfaceActivity += OnQuickBarInterfaceActivity;
        _drawTip.InterfaceActivity += OnQuickBarInterfaceActivity;
        _helpBarTip.InterfaceActivity += OnQuickBarInterfaceActivity;
        _quickBar.IdleReminderStarted += OnQuickBarIdleReminderStarted;
        _quickBar.FunctionActivated += OnQuickBarFunctionActivated;
        _pokerWelcome.OverlayVisibilityChanged += OnPokerWelcomeVisibilityChanged;
        _pokerWelcome.Confirmed += OnPokerWelcomeConfirmed;
        _collectionProgressDialog = GetNode<PokerCollectionProgressDialogController>("CollectionProgressDialog");
        _collectionProgressDialog.OverlayVisibilityChanged += OnCollectionProgressDialogVisibilityChanged;
        _collectionProgressDialog.Confirmed += OnCollectionProgressConfirmed;

        // 信号连接
        _dogVisual.DogClicked += OnDogClicked;
        _handArea.HandKnocked += OnDrawPressed;
        _handArea.FirstKnockLanded += OnFirstKnockLanded;
        _chipStack.BetRequested += OnBetRequested;
        _cardTable.CardClicked += OnCardClicked;
        _cardTable.LastReplacementStarted += OnLastReplacementStarted;
        RefreshInteractionHintTargets();

        AttachGameData();

        AudioManager.Instance.PlayRandomBgm();
    }

    public override void _ExitTree()
    {
        if (_interactionHints != null)
            _interactionHints.EffectiveInteractionOccurred -= OnEffectivePokerInteraction;
        _drawTip.InterfaceActivity -= OnQuickBarInterfaceActivity;
        _helpBarTip.InterfaceActivity -= OnQuickBarInterfaceActivity;
        SettingsPanel = null;
        SettingsManager.ProactiveInteractionHintsChanged -= _interactionHints.SetProactiveHintsEnabled;
        if (_tutorial != null)
            _tutorial.OverlayVisibilityChanged -= OnTutorialOverlayVisibilityChanged;
        if (_pokerHandShowcase != null)
            _pokerHandShowcase.ShowcaseVisibilityChanged -= OnPokerHandShowcaseVisibilityChanged;
        if (_quickBar != null)
        {
            _quickBar.HelpRequested -= OnQuickBarHelpRequested;
            _quickBar.TutorialRequested -= OnQuickBarTutorialRequested;
            _quickBar.DesktopRequested -= OnQuickBarDesktopRequested;
            _quickBar.InterfaceActivity -= OnQuickBarInterfaceActivity;
            _quickBar.IdleReminderStarted -= OnQuickBarIdleReminderStarted;
            _quickBar.FunctionActivated -= OnQuickBarFunctionActivated;
        }
        if (_pokerWelcome != null)
        {
            _pokerWelcome.OverlayVisibilityChanged -= OnPokerWelcomeVisibilityChanged;
            _pokerWelcome.Confirmed -= OnPokerWelcomeConfirmed;
        }
        if (_collectionProgressDialog != null)
        {
            _collectionProgressDialog.OverlayVisibilityChanged -= OnCollectionProgressDialogVisibilityChanged;
            _collectionProgressDialog.Confirmed -= OnCollectionProgressConfirmed;
        }
        if (_itemArea != null && _interactionHints != null)
        {
            _itemArea.InteractionActivated -= _interactionHints.NotifyInteractionHandled;
            _itemArea.InteractionIgnored -= _interactionHints.NotifyInteractionIgnored;
            _itemArea.HintContextChanged -= RefreshInteractionHintTargets;
        }
        DetachGameData();
    }

    public override void _Process(double delta)
    {
        if (_interactionHints == null || _blindBoxOverlay == null)
            return;

        RefreshOverlayInteractionContexts();
    }

    public void SetInteractionHintPokerModeActive(bool active)
    {
        _isPokerModeActive = active;
        if (!active)
            _pokerHandShowcase?.HideOverlay();
        RefreshOverlayInteractionContexts();
    }

    public void TogglePokerHandShowcase()
    {
        if (!_isPokerModeActive || _pokerHandShowcase == null || IsPokerWelcomeVisible)
            return;

        if (_pokerHandShowcase.IsOverlayVisible)
            _pokerHandShowcase.HideOverlay();
        else
            _pokerHandShowcase.ShowRanks();
        RefreshOverlayInteractionContexts();
    }

    public bool TryScrollPokerHandShowcase(Vector2 viewportPosition, double wheelSteps)
    {
        return _isPokerModeActive
            && _pokerHandShowcase?.TryScrollAt(viewportPosition, wheelSteps) == true;
    }

    public bool IsPointOverQuickBar(Vector2 viewportPosition) => _quickBar?.ContainsPoint(viewportPosition) == true;

    public void SetPokerQuickBarUsed(bool used)
    {
        _quickBar.SetHasUsedFunction(used);
        // Reconcile older accounts too, including notes that were never shown or left active.
        if (used) CompleteHelpBarTip();
    }

    private void OnQuickBarFunctionActivated()
    {
        // Close and acknowledge before opening help/controls or leaving poker.
        CompleteHelpBarTip();
        _pokerWelcome.ConfirmFromQuickBar();
        EmitSignal(SignalName.PokerQuickBarFunctionActivated);
    }

    private void CompleteHelpBarTip()
    {
        try { CurrentTips?.Complete(PersistentTipProgress.PokerHelpBar); }
        catch (Exception error) { GD.PushWarning($"[PersistentTip] Could not save help bar tip completion: {error.Message}"); }
        RefreshOverlayInteractionContexts();
    }

    public bool IsPointOverPokerWelcomeConfirmButton(Vector2 viewportPosition) =>
        _pokerWelcome?.IsPointOverConfirmButton(viewportPosition) == true;

    public void UpdateQuickBarPointer(bool overPoker, Vector2 viewportPosition, bool dragging, bool leftPressed, double delta)
    {
        _quickBar?.UpdatePointer(overPoker, viewportPosition, dragging, leftPressed, delta);
    }

    private void OnQuickBarInterfaceActivity()
    {
        _interactionHints.NotifyInteractionIgnored();
    }

    private void OnEffectivePokerInteraction() => _quickBar.NotifyEffectiveInteraction();

    private void OnQuickBarIdleReminderStarted()
    {
        try { CurrentTips?.Activate(PersistentTipProgress.PokerHelpBar); }
        catch (Exception error) { GD.PushWarning($"[PersistentTip] Could not save help bar tip activation: {error.Message}"); }
        RefreshOverlayInteractionContexts();
    }

    private void OnQuickBarHelpRequested() => _pokerHandShowcase.ShowBasics();
    private void OnQuickBarTutorialRequested() => _tutorial.ShowOnce();
    private void OnQuickBarDesktopRequested() => EmitSignal(SignalName.DesktopModeRequested);

    public void RequestPokerWelcome(bool required)
    {
        _pokerWelcome.SetRequested(required);
        RefreshOverlayInteractionContexts();
    }

    private void OnPokerWelcomeVisibilityChanged(bool visible)
    {
        PokerWelcomeVisibilityChanged?.Invoke(visible);
        RefreshOverlayInteractionContexts();
    }

    private void OnPokerWelcomeConfirmed(bool fromQuickBar)
    {
        _quickBar.NotifyEffectiveInteraction();
        if (!fromQuickBar) _quickBar.RevealAfterWelcome();
        EmitSignal(SignalName.PokerWelcomeConfirmed);
    }

    public void ShowPendingBlindBoxReward(PendingBlindBoxReward pending)
    {
        // Repeated open requests must not restart an active reveal animation.
        if (_blindBoxOverlay.Visible)
            return;

        // 盲盒表演覆盖牌桌时，不应让后台尚未落地的下注筹码继续播落地音效。
        _chipStack.CompleteAppearanceSilently();
        _blindBoxOverlay.ShowReward(pending, animateDrop: false);
        RefreshOverlayInteractionContexts();
    }

    public void HidePendingBlindBoxReward()
    {
        _blindBoxOverlay.HideOverlay();
        RefreshOverlayInteractionContexts();
    }

    public void ShowCollectionEventFirstDogProgressNotice()
    {
        if (!_isPokerModeActive || _collectionProgressDialog == null)
            return;

        _collectionProgressDialog.ShowNotice();
        RefreshOverlayInteractionContexts();
    }

    private void OnCollectionProgressConfirmed() => EmitSignal(SignalName.CollectionProgressConfirmed);

    // === 信号处理 ===

    private void OnBetRequested()
    {
        if (IsRefreshmentBalloonPointerCaptured()) return;
        if (State != GameState.WaitingForBet) return;
        if (!CanStartNewHand())
        {
            // 只在玩家主动点击下注筹码时通知 UI；自动结算或全局输入不应触发闪红。
            EmitSignal(SignalName.InsufficientBetAttempted);
            return;
        }

        // 只有通过余额校验后才允许筹码离场。
        _chipStack.PlayLeave();
        StartNewHand();
    }

    /// <summary>
    /// 下注入口的第一道校验。失败时保持下注筹码在桌面上，等待玩家通过全局输入补足余额。
    /// </summary>
    private bool CanStartNewHand()
    {
        if (State != GameState.WaitingForBet)
            return false;

        if (_gameData.CanAffordBet)
            return true;

        HandleInsufficientChips();
        return false;
    }

    /// <summary>
    /// 真正开始新局前再次校验，避免未来新增入口或异步流程绕过余额保护。
    /// </summary>
    private bool StartNewHand()
    {
        if (!CanStartNewHand())
            return false;

        _interactionHints.NotifyInteractionHandled();
#if DEBUG
        if (SettingsPanel != null && SettingsPanel.TryGetFixedSeed(out int fixedSeed))
        {
            _deck.SetFixedSeed(fixedSeed);
            _currentHandProgressSource = PlayerProgressSource.Debug;
        }
        else
        {
            _currentHandProgressSource = PlayerProgressSource.Gameplay;
        }
#else
        _currentHandProgressSource = PlayerProgressSource.Gameplay;
#endif
        DealNewHand();
        return true;
    }

    private void OnDrawPressed()
    {
        if (IsRefreshmentBalloonPointerCaptured()) return;
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
        if (IsRefreshmentBalloonPointerCaptured()) return;
        if (State != GameState.Dealt && State != GameState.Holding) return;
        _interactionHints.NotifyInteractionHandled();

        _held[index] = !_held[index];
        _cardTable.SetHeld(index, _held[index]);
        if (!_held[index])
        {
            try { CurrentTips?.Activate(PersistentTipProgress.PokerDraw); }
            catch (Exception error) { GD.PushWarning($"[PersistentTip] Could not save activation: {error.Message}"); }
        }
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
        if (IsRefreshmentBalloonPointerCaptured()) return;
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
                _gameData.RecordPlayerProgressEvent("DogHintRefused", _currentHandProgressSource);
            }
            return;
        }

        _interactionHints.NotifyInteractionHandled();

        var previewHand = _deck.PreviewFinalHand(_held);
        var previewRank = _dogHint.EvaluateHoldRank(_deck.CurrentHand, _held, previewHand);
        _dogVisual.ApplyReaction(GetSawReaction(previewRank));
        _dogHint.HasGivenHint = true;
        _gameData.RecordPlayerProgressEvent("DogHintAsked", _currentHandProgressSource);
        RefreshInteractionHintTargets();
        _hud.SetMessage("");
    }

    private void OnChipCollected()
    {
        _pendingReward = null;
        _gameData.ModifyChips(_pendingPayout, _currentHandProgressSource);
        _gameData.RecordPokerPayoutCollected(_pendingPayout, _currentHandProgressSource);
        _pendingPayout = 0;
        RefreshUI();
        SetState(GameState.WaitingForBet);
        if (SettingsManager.LoadQuickStartAfterWin())
            StartNewHand();
        else
            FinishCollectedHand();
    }

    private void FinishCollectedHand()
    {
        // 收奖才是赢牌局的结束点。保留信息面板结果，牌桌回到空桌待下注。
        _cardTable.ClearCards();
        _dogVisual.ApplyReaction(EDogReactionTrigger.WaitingForBet);
        _chipStack.ShowAfterRewardCollected();
    }

    // === 游戏逻辑 ===

    public void AddChips(int amount)
    {
        _gameData.ModifyChips(amount, PlayerProgressSource.Debug);
        RefreshUI();
    }

    private void DealNewHand()
    {
        if (!_gameData.CanAffordBet)
        {
            HandleInsufficientChips();
            return;
        }

        _gameData.ModifyChips(-_gameData.BetAmount, _currentHandProgressSource);
        _gameData.RecordPokerHandStarted(_gameData.BetAmount, _currentHandProgressSource);
        _gameData.EmitNewHandStarted();
        var hasLuckyDealBuff = _gameData.TryConsumeLuckyDealBuff(
            out var luckyDealMode,
            out var triggerChance);
        _deck.Deal(
            hasLuckyDealBuff ? luckyDealMode : null,
            hasLuckyDealBuff ? triggerChance : null);
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
        _gameData.RecordPokerHandResolved(rank, payout, _dogHint.HasGivenHint, _currentHandProgressSource);
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
        _interactionHints.RegisterTarget(InteractionHintKeys.SettledRewardStack, reward);
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

    private void OnTutorialOverlayVisibilityChanged(bool visible)
    {
        TutorialOverlayVisibilityChanged?.Invoke(visible);
        RefreshOverlayInteractionContexts();
    }

    private void OnPokerHandShowcaseVisibilityChanged(bool visible)
    {
        PokerHandShowcaseVisibilityChanged?.Invoke(visible);
        RefreshOverlayInteractionContexts();
    }

    private void OnCollectionProgressDialogVisibilityChanged(bool _)
    {
        RefreshOverlayInteractionContexts();
    }

    private void RefreshOverlayInteractionContexts()
    {
        if (_interactionHints == null
            || _blindBoxOverlay == null
            || _tutorial == null
            || _pokerHandShowcase == null
            || _collectionProgressDialog == null
            || _pokerWelcome == null)
            return;

        var blindBoxVisible = _blindBoxOverlay.Visible;
        var showcaseVisible = _pokerHandShowcase.IsOverlayVisible;
        var collectionProgressVisible = _collectionProgressDialog.IsOverlayVisible;
        _pokerWelcome.SetContext(_isPokerModeActive,
            blindBoxVisible || showcaseVisible || collectionProgressVisible);
        bool welcomeVisible = _pokerWelcome.IsOverlayVisible;
        _tutorial.SetPokerContext(
            _isPokerModeActive,
            blindBoxVisible || showcaseVisible || collectionProgressVisible || welcomeVisible);
        var pokerInputAvailable = _isPokerModeActive
            && !blindBoxVisible
            && !showcaseVisible
            && !collectionProgressVisible
            && !_tutorial.IsOverlayVisible;
        _drawTip.SetContext(CurrentTips, pokerInputAvailable && !welcomeVisible);
        _helpBarTip.SetContext(CurrentTips, pokerInputAvailable && !welcomeVisible);
        _quickBar?.SetWelcomeContext(welcomeVisible, _pokerWelcome.Layer);
        _quickBar?.SetAvailable(pokerInputAvailable);
        // The system panel may stay open throughout play; it does not suspend idle discovery.
        _quickBar?.SetReminderContext(pokerInputAvailable && !welcomeVisible);
        var pokerHintsAvailable = pokerInputAvailable && !welcomeVisible && _quickBar?.IsPointerInside != true;
        _interactionHints.SetInputContextActive(pokerHintsAvailable);
        _interactionHints.SetProactiveHintContextActive(pokerHintsAvailable);
    }

    private void RefreshInteractionHintTargets()
    {
        if (_interactionHints == null)
            return;

        if (_itemArea != null && _itemArea.IsUseBalloonOpen)
        {
            _interactionHints.SetAvailableKeys(InteractionHintKeys.RefreshmentUseConfirm);
            return;
        }

        switch (State)
        {
            case GameState.WaitingForBet:
                _interactionHints.SetAvailableKeys(
                    InteractionHintKeys.WaitingForBetRefreshment,
                    InteractionHintKeys.WaitingForBetBetStack);
                break;
            case GameState.Settled when _pendingReward != null && IsInstanceValid(_pendingReward):
                _interactionHints.SetAvailableKeys(InteractionHintKeys.SettledRewardStack);
                break;
            case GameState.Dealt:
            case GameState.Holding:
                _interactionHints.SetAvailableKeys(GetPlayDecisionHintKey());
                break;
            default:
                _interactionHints.SetAvailableKeys();
                break;
        }
    }

    private string GetPlayDecisionHintKey()
    {
        var faceUpCards = _deck.CurrentHand
            .Where((_, index) => _held[index])
            .ToArray();

        if (CanFaceUpCardsScore(faceUpCards))
            return State == GameState.Holding
                ? InteractionHintKeys.HoldingHandConfirm
                : InteractionHintKeys.DealtHandConfirm;

        if (!_held.Any(isHeld => !isHeld))
            return InteractionHintKeys.DealtCardSelection;

        return _dogHint.HasGivenHint
            ? InteractionHintKeys.HoldingHandConfirm
            : InteractionHintKeys.DealtDogAdvice;
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

        return groups.Any(group => group.Count >= 2);
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
        ApplyItemTexture(EItemType.BodyDecoration, (tex, name) => _handArea.SetBodyDecoration(tex, name), () => _handArea.SetBodyDecoration(null, ""));
        ApplyItemTexture(EItemType.Clothes, (tex, name) => _handArea.SetClothes(tex, name), () => _handArea.SetClothes(null, ""));
        ApplyItemTexture(EItemType.Accessory, (tex, name) => _handArea.SetAccessory(tex, name), () => _handArea.SetAccessory(null, ""));
        _dogVisual.RefreshEquippedHeadwear();
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

    private void OnRefreshmentStateChanged()
    {
        RefreshInteractionHintTargets();
    }

    private bool IsRefreshmentBalloonPointerCaptured()
    {
        return _itemArea != null
            && _itemArea.CapturesPointerAt(GetViewport().GetMousePosition());
    }

    private void AttachGameData()
    {
        if (_gameData == null || _hud == null || _chipStack == null || _itemArea == null)
            return;

        _dogVisual.GameData = _gameData;
        _itemArea.BindGameData(_gameData);
        _gameData.EquipmentChanged += ApplyEquippedVisuals;
        _gameData.RefreshmentStateChanged += OnRefreshmentStateChanged;
        _gameData.RefreshmentSelectionRefused += OnRefreshmentSelectionRefused;
        RefreshUI();
        ApplyEquippedVisuals();
        _hud.SetMessage("");
        _chipStack.ShowHint("Click to bet");
        RefreshInteractionHintTargets();
    }

    private void DetachGameData()
    {
        if (_gameData == null)
            return;

        _gameData.EquipmentChanged -= ApplyEquippedVisuals;
        _gameData.RefreshmentStateChanged -= OnRefreshmentStateChanged;
        _gameData.RefreshmentSelectionRefused -= OnRefreshmentSelectionRefused;
        if (_itemArea != null)
            _itemArea.BindGameData(null);
    }

    private void OnRefreshmentSelectionRefused()
    {
        _dogVisual.PlayTemporaryReaction(EDogReactionTrigger.RefuseRefreshment);
        _hud.SetMessage("");
    }

}
