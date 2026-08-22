using Godot;

namespace LuckyDogRise;

public partial class TutorialManager : CanvasLayer
{
    [Signal] public delegate void OverlayVisibilityChangedEventHandler(bool visible);

    [Export] private Control _overlayInput = null!;

    private const double AutomaticShowIdleSeconds = 20.0;
    private const double DismissLockSeconds = 2.0;

    private GameData _gameData = null!;
    private InteractionHintController _interactionHints = null!;
    private bool _pokerModeActive;
    private bool _blockedByHigherOverlay;
    private bool _manualShowPending;
    private double _secondsSinceEffectiveInteraction;
    private double _dismissLockRemaining;

    public bool IsOverlayVisible => Visible;

    public override void _Ready()
    {
        Visible = false;
        _overlayInput.GuiInput += OnOverlayGuiInput;
        SettingsManager.PokerGuideOverlayEnabledChanged += OnPokerGuideOverlayEnabledChanged;
    }

    public override void _ExitTree()
    {
        SettingsManager.PokerGuideOverlayEnabledChanged -= OnPokerGuideOverlayEnabledChanged;
        BindInteractionHints(null);
        BindGameData(null);
    }

    public override void _Process(double delta)
    {
        if (Visible)
        {
            _dismissLockRemaining = Mathf.Max(0.0, _dismissLockRemaining - delta);
            return;
        }

        if (!_pokerModeActive
            || _blockedByHigherOverlay
            || _gameData == null
            || !_gameData.NeedsPokerBasicsGuidance
            || !SettingsManager.LoadPokerGuideOverlayEnabled())
            return;

        _secondsSinceEffectiveInteraction += delta;
        if (_secondsSinceEffectiveInteraction >= AutomaticShowIdleSeconds)
            ShowOverlay();
    }

    public void BindGameData(GameData gameData)
    {
        if (_gameData == gameData)
            return;

        if (_gameData != null)
            _gameData.PokerBasicsGuidanceChanged -= OnPokerBasicsGuidanceChanged;

        _gameData = gameData;
        if (_gameData == null)
            return;

        _gameData.PokerBasicsGuidanceChanged += OnPokerBasicsGuidanceChanged;
        SettingsManager.InitializePokerGuideOverlaySetting(_gameData.NeedsPokerBasicsGuidance);
    }

    public void BindInteractionHints(InteractionHintController interactionHints)
    {
        if (_interactionHints == interactionHints)
            return;

        if (_interactionHints != null)
            _interactionHints.EffectiveInteractionOccurred -= OnEffectiveInteractionOccurred;

        _interactionHints = interactionHints;
        if (_interactionHints != null)
            _interactionHints.EffectiveInteractionOccurred += OnEffectiveInteractionOccurred;
    }

    public void SetPokerContext(bool pokerModeActive, bool blockedByHigherOverlay)
    {
        var enteredPokerMode = pokerModeActive && !_pokerModeActive;
        _pokerModeActive = pokerModeActive;
        _blockedByHigherOverlay = blockedByHigherOverlay;

        if (!_pokerModeActive)
            return;

        if (_manualShowPending)
        {
            _manualShowPending = false;
            ShowOverlay();
            return;
        }

        // 新档第一次进入扑克模式时立即展示；临时关闭后切走再回来也视为一次新的展示机会。
        if (enteredPokerMode
            && _gameData?.NeedsPokerBasicsGuidance == true
            && SettingsManager.LoadPokerGuideOverlayEnabled())
            ShowOverlay();
    }

    private void OnEffectiveInteractionOccurred()
    {
        _secondsSinceEffectiveInteraction = 0.0;
    }

    private void OnPokerBasicsGuidanceChanged(bool _)
    {
        _secondsSinceEffectiveInteraction = 0.0;
    }

    private void OnPokerGuideOverlayEnabledChanged(bool enabled)
    {
        if (!enabled)
        {
            _manualShowPending = false;
            HideOverlay();
            return;
        }

        if (_pokerModeActive)
            ShowOverlay();
        else
            _manualShowPending = true;
    }

    private void ShowOverlay()
    {
        if (Visible || !SettingsManager.LoadPokerGuideOverlayEnabled())
            return;

        Visible = true;
        _dismissLockRemaining = DismissLockSeconds;
        EmitSignal(SignalName.OverlayVisibilityChanged, true);
    }

    private void HideOverlay()
    {
        if (!Visible)
            return;

        Visible = false;
        _dismissLockRemaining = 0.0;
        _secondsSinceEffectiveInteraction = 0.0;
        EmitSignal(SignalName.OverlayVisibilityChanged, false);
    }

    private void OnOverlayGuiInput(InputEvent @event)
    {
        if (@event is not InputEventMouseButton
            {
                ButtonIndex: MouseButton.Left,
                Pressed: true,
            })
            return;

        // 两秒锁定期内仍然吃掉输入，避免点穿牌桌，但不关闭教程。
        _overlayInput.AcceptEvent();
        if (_dismissLockRemaining > 0.0)
            return;

        if (_gameData?.NeedsPokerBasicsGuidance == true)
        {
            // 新手仅关闭本次；设置保持开启，之后再次无有效操作 20 秒会重新出现。
            HideOverlay();
            return;
        }

        // 已完成扑克基础阶段的玩家，点击教程即永久关闭设置。
        SettingsManager.SavePokerGuideOverlayEnabled(false);
    }
}
