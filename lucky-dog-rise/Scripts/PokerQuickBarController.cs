using Godot;

namespace LuckyDogRise;

/// <summary>扑克顶部快捷入口；指针位置由宿主窗口传入，避免 SubViewport 离开后的旧坐标。</summary>
public partial class PokerQuickBarController : CanvasLayer
{
    [Signal] public delegate void HelpRequestedEventHandler();
    [Signal] public delegate void TutorialRequestedEventHandler();
    [Signal] public delegate void DesktopRequestedEventHandler();
    [Signal] public delegate void InterfaceActivityEventHandler();
    [Signal] public delegate void FunctionActivatedEventHandler();

    [Export] private Control _hotZone = null!;
    [Export] private Control _clip = null!;
    [Export] private PanelContainer _bar = null!;
    [Export] private Button _helpButton = null!;
    [Export] private Button _tutorialButton = null!;
    [Export] private Button _desktopButton = null!;

    private const double HideDelaySeconds = 0.5;
    private const double WelcomeRevealSeconds = 5.0;
    private const double ReminderIdleSeconds = 20.0;
    private bool _available;
    private bool _waitForPointerExit;
    private double _outsideSeconds;
    private double _welcomeRevealRemaining;
    private int _normalLayer;
    private bool _welcomeVisible;
    private Tween _slide;
    private float _discoveryHotZoneHeight;
    private bool _hasUsedFunction;
    private bool _reminderContextActive;
    private double _idleSeconds;

    public bool IsIdleReminderActive { get; private set; }

    public bool IsExpanded { get; private set; }
    public bool IsPointerInside { get; private set; }

    public override void _Ready()
    {
        _normalLayer = Layer;
        _discoveryHotZoneHeight = _hotZone.Size.Y;
        _bar.Resized += RefreshHotZoneHeight;
        _helpButton.Pressed += () => Activate(SignalName.HelpRequested);
        _tutorialButton.Pressed += () => Activate(SignalName.TutorialRequested);
        _desktopButton.Pressed += () => Activate(SignalName.DesktopRequested);
        foreach (var control in new Control[] { _hotZone, _bar, _helpButton, _tutorialButton, _desktopButton })
            control.GuiInput += OnGuiInput;
        L10n.Changed += RefreshText;
        RefreshText();
        _hotZone.Visible = false;
        CollapseImmediately();
    }

    public override void _ExitTree()
    {
        L10n.Changed -= RefreshText;
        _bar.Resized -= RefreshHotZoneHeight;
        _slide?.Kill();
    }

    public void SetHasUsedFunction(bool used)
    {
        _hasUsedFunction = used;
        ClearIdleReminder();
        RefreshHotZoneHeight();
    }

    public void SetReminderContext(bool active)
    {
        if (_reminderContextActive == active) return;
        _reminderContextActive = active;
        ClearIdleReminder();
    }

    public void NotifyEffectiveInteraction() => ClearIdleReminder();

    private void ClearIdleReminder()
    {
        _idleSeconds = 0;
        IsIdleReminderActive = false;
        _outsideSeconds = 0;
    }

    private void RefreshHotZoneHeight()
    {
        // Match the visible bar body, not its animated position or the shadow below it.
        _hotZone.Size = new Vector2(_hotZone.Size.X,
            _hasUsedFunction ? _bar.Size.Y : _discoveryHotZoneHeight);
    }

    public void SetAvailable(bool available)
    {
        if (_available == available) return;
        _available = available;
        _hotZone.Visible = available;
        if (!available)
        {
            IsPointerInside = false;
            CollapseImmediately();
        }
    }

    public void SetWelcomeContext(bool visible, int welcomeLayer)
    {
        if (_welcomeVisible == visible) return;
        _welcomeVisible = visible;
        // Let only the toolbar sit above the welcome shield; the rest of the table stays modal.
        Layer = visible ? welcomeLayer + 1 : _normalLayer;
        if (visible) CollapseImmediately();
    }

    public void RevealAfterWelcome()
    {
        if (!_available) return;
        _waitForPointerExit = false;
        _outsideSeconds = 0;
        _welcomeRevealRemaining = WelcomeRevealSeconds;
        SetExpanded(true);
    }

    public bool ContainsPoint(Vector2 viewportPosition) => _available
        && _hotZone.GetGlobalRect().HasPoint(viewportPosition);

    public void UpdatePointer(bool overPoker, Vector2 viewportPosition, bool dragging, bool leftPressed, double delta)
    {
        IsPointerInside = overPoker && ContainsPoint(viewportPosition);
        if (!_available) return;
        _welcomeRevealRemaining = Mathf.Max(0.0, _welcomeRevealRemaining - delta);
        if (_reminderContextActive && !_hasUsedFunction && !IsIdleReminderActive)
        {
            _idleSeconds += delta;
            // Blank clicks do not count as effective play; defer revealing until the gesture ends.
            if (_idleSeconds >= ReminderIdleSeconds && !dragging && !leftPressed)
            {
                IsIdleReminderActive = true;
                _waitForPointerExit = false;
            }
        }
        if (IsIdleReminderActive)
        {
            _outsideSeconds = 0;
            SetExpanded(true);
            return;
        }
        if (!IsPointerInside) _waitForPointerExit = false;
        if (dragging)
        {
            _waitForPointerExit = IsPointerInside;
            CollapseImmediately();
            return;
        }
        if (IsPointerInside)
        {
            _outsideSeconds = 0;
            EmitSignal(SignalName.InterfaceActivity);
            if (!_waitForPointerExit && !leftPressed) SetExpanded(true);
        }
        else if (IsExpanded)
        {
            _outsideSeconds += delta;
            // 按住按钮移出时保留完整的释放事件，再允许收起。
            if (_outsideSeconds >= HideDelaySeconds && _welcomeRevealRemaining <= 0 && !leftPressed)
                SetExpanded(false);
        }
    }

    public void Dismiss()
    {
        _waitForPointerExit = true;
        CollapseImmediately();
    }

    private void Activate(StringName signal)
    {
        SetHasUsedFunction(true);
        // Record before the action can hide poker or open another overlay.
        EmitSignal(SignalName.FunctionActivated);
        Dismiss();
        EmitSignal(SignalName.InterfaceActivity);
        EmitSignal(signal);
    }

    private void SetExpanded(bool expanded)
    {
        if (IsExpanded == expanded) return;
        IsExpanded = expanded;
        _slide?.Kill();
        _bar.Visible = true;
        _slide = CreateTween().SetTrans(Tween.TransitionType.Cubic).SetEase(Tween.EaseType.Out);
        _slide.TweenProperty(_bar, "position:y", expanded ? 0f : -_clip.Size.Y, 0.18);
        if (!expanded) _slide.TweenCallback(Callable.From(() => _bar.Visible = false));
    }

    private void CollapseImmediately()
    {
        _slide?.Kill();
        _slide = null;
        IsExpanded = false;
        _outsideSeconds = 0;
        _welcomeRevealRemaining = 0;
        _bar.Position = new Vector2(0, -_clip.Size.Y);
        _bar.Visible = false;
    }

    private void OnGuiInput(InputEvent @event)
    {
        if (@event is InputEventMouseButton { ButtonIndex: MouseButton.Left })
            EmitSignal(SignalName.InterfaceActivity);
    }

    private void RefreshText()
    {
        _helpButton.Text = L10n.Tr(L10nKey.PokerToolbar_Help);
        _tutorialButton.Text = L10n.Tr(L10nKey.PokerToolbar_Tutorial);
        _desktopButton.Text = L10n.Tr(L10nKey.PokerToolbar_DesktopPet);
    }
}
