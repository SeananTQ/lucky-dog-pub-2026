using System;
using System.Collections.Generic;
using System.Linq;
using DataTables;
using Godot;

namespace LuckyDogRise;

public partial class ModeManager : Control
{
    public enum Mode { BossKey, Play, Immersive }
    public Mode CurrentMode { get; private set; } = Mode.BossKey;

    private SystemPanelController _settingsPanel = null!;
    public SystemPanelController SettingsPanelObj => _settingsPanel;
    private Node2D _bossKeyContent = null!;
    private DogVisual _bossDogVisual = null!;
    private DesktopRiseIntroController _bossRiseIntro = null!;
    private BalloonHintController _bossBlindBoxHint = null!;
    private BlindBoxRevealOverlayController _bossBlindBoxOverlay = null!;
    private Marker2D _bossBlindBoxRevealAnchor = null!;
    private Marker2D _bossTaskBarAnchor = null!;
    private PanelContainer _bossStatusPanel = null!;
    private Button _bossModeButton = null!;
    private Button _bossSystemButton = null!;
    private Vector2 _bossStatusPanelBasePosition;
    private Vector2 _bossStatusPanelBaseSize;
    private StyleBoxFlat _bossStatusPanelStyle = null!;
    private float _bossStatusPanelBaseMarginTop;
    private float _bossStatusPanelBaseMarginBottom;
    private bool _bossStatusBarInteractable = true;
    private bool _bossRiseIntroSuppressesBlindBoxHint;
    private GameManager _gameManager = null!;
    private Label _mainText = null!;
    private Vector2 _windowBaseSize;
    private Vector2 _panelSize;
    private Vector2 _contentOffset;

    private bool _isDragging, _potentialDrag, _isClickThrough = true;
    private Vector2I _mouseScreenStart, _windowPosStart;
    private const float DragThreshold = 5f;
    private const int PlayInfoPanelWidth = 246;
    private const int PlayGameWidth = 600;
    private const int PlayGameSettingsGap = 1;

    private bool _taskbarSnapped;
    private const int SnapThreshold = 15;
    private const int BreakawayThreshold = 30;

    private Rect2 _dogHitRect;
    private Rect2 _btnHitRect;
    private Texture2D _blindBoxIcon = null!;

    private GameData _gameData = null!;
    public GameData GameDataObj => _gameData;

    private static readonly EItemType[] DebugDogItemTypes =
    [
        EItemType.Dog,
        EItemType.Headwear,
        EItemType.Eyewear,
    ];

    private static readonly EItemType[] DebugSceneItemTypes = Enum.GetValues<EItemType>()
        .Where(type => !DebugDogItemTypes.Contains(type))
        .ToArray();

    // 面板避让九宫优先级：伪装模式。按数组顺序尝试，数字对应键盘九宫格：
    // 789 / 456 / 123
    private static readonly int[] BossKeyPanelSlotPriority =
    [
        8, 9, 7, 6, 4, 2, 3, 1,
    ];

    // 面板避让九宫优先级：扑克模式。需要调整扑克模式面板位置时，只改这个数组顺序。
    // 789 / 456 / 123
    private static readonly int[] PlayPanelSlotPriority =
    [
        6, 8, 9, 7, 4, 2, 3, 1,
    ];

    private readonly Random _debugRandom = new();
    private readonly Dictionary<EItemType, ShuffleBag<int>> _debugItemBags = new();
    private readonly Queue<(double time, int count)> _desktopInputEvents = new();
    private const double DesktopActivitySampleSeconds = 10.0;
    private DesktopActivityState _currentDesktopActivityState;
    private DesktopActivityState _candidateDesktopActivityState;
    private double _candidateDesktopActivitySeconds;
    private double _desktopActivityCooldownSeconds;
    private bool _desktopTongueFeedbackEnabled = true;
    private double _fullscreenCheckTimer;
    private bool _hiddenByFullscreenApp;
    private double _enhancedTopmostTimer;
    private double _enhancedTopmostBoostTimer;
    private double _enhancedTopmostDelayedBoostTimer;
    private bool _waitingForWinMenuDismiss;
    private double _recoverTopmostOnNextMousePressTimer;
    private double _settingsPanelOpenedAtSeconds;
    private const double RecoverTopmostOnNextMousePressSeconds = 5.0;
    private const double SettingsPanelAutoHideOpenGraceSeconds = 0.2;
    private const float BossCounterTongueClearance = 2f;
    private const float BossCounterMinimumHeight = 22f;

    public override void _Ready()
    {
        L10n.ApplySavedOrSystemLocale();

        _gameData = new GameData();
        _gameData.Name = "GameData";
        AddChild(_gameData);

        _bossKeyContent = GD.Load<PackedScene>("res://Scenes/BossKeyContent.tscn").Instantiate<Node2D>();
        _bossKeyContent.Name = "BossKeyContent";
        AddChild(_bossKeyContent);
        _bossDogVisual = _bossKeyContent.GetNode<DogVisual>("ContentA/DogArea");
        _bossBlindBoxHint = _bossKeyContent.GetNode<BalloonHintController>("CanvasLayer/BlindBoxHint");
        _bossBlindBoxRevealAnchor = _bossKeyContent.GetNode<Marker2D>("ContentA/DesktopBlindBoxRevealAnchor");
        _bossTaskBarAnchor = _bossKeyContent.GetNode<Marker2D>("ContentA/TaskBar");
        _bossDogVisual.ShowEquippedEyewearByDefault = true;
        _bossDogVisual.GameData = _gameData;
        RefreshBossDogVisuals();
        _gameData.EquipmentChanged += RefreshBossDogVisuals;
        _gameData.BlindBoxStateChanged += RefreshBossBlindBoxHint;
        _gameData.ChipsChanged += _ => RefreshBossBlindBoxHint();
        _mainText = _bossKeyContent.GetNode<Label>("CanvasLayer/Panel/HBoxContainer/MainText");
        _bossStatusPanel = _bossKeyContent.GetNode<PanelContainer>("CanvasLayer/Panel");
        _bossStatusPanelBasePosition = _bossStatusPanel.Position;
        _bossStatusPanelBaseSize = _bossStatusPanel.Size;
        CaptureBossStatusPanelStyle();
        _blindBoxIcon = GD.Load<Texture2D>("res://Assets/UI/BlindBox/BlindBox_Common_Closed.png");
        _bossModeButton = _bossKeyContent.GetNode<Button>("CanvasLayer/Panel/HBoxContainer/ModeSwitch");
        _bossSystemButton = _bossKeyContent.GetNode<Button>("CanvasLayer/Panel/HBoxContainer/SystemButton");
        _bossModeButton.Pressed += OnBossModeButtonPressed;
        _bossSystemButton.Pressed += OnBossSystemButtonPressed;
        _bossBlindBoxHint.Pressed += OnBossBlindBoxHintPressed;
        RefreshBossBlindBoxHint();

        // 先实例化面板以读取实际尺寸
        _settingsPanel = GD.Load<PackedScene>("res://Scenes/SystemPanel.tscn").Instantiate<SystemPanelController>();
        _settingsPanel.Name = "SettingsPanel";
        _settingsPanel.Layer = 100;
        AddChild(_settingsPanel);
        _settingsPanel.GameData = _gameData;
        _settingsPanel.SwitchToPlayRequested += SwitchToPlay;
        _settingsPanel.SwitchToBossKeyRequested += SwitchToBossKey;
        _settingsPanel.RandomizeRequested += OnRandomizeScene;
        _settingsPanel.RandomizeDogRequested += OnRandomizeDog;
        _settingsPanel.RandomAcquireItemRequested += OnRandomAcquireItem;
        _settingsPanel.DebugGrantChipsRequested += OnDebugGrantChips;
        _settingsPanel.DogReactionRequested += OnDogReactionRequested;
        _settingsPanel.BlindBoxBubbleVisibilityChanged += OnBlindBoxBubbleVisibilityChanged;
        _settingsPanel.CounterLayoutChanged += ApplyBossCounterLayout;
        RefreshSettingsPanelModeActions();

        _panelSize = _settingsPanel.PanelSize;
        _contentOffset = _panelSize;

        _bossBlindBoxOverlay = GD.Load<PackedScene>("res://Scenes/DesktopBlindBoxRevealOverlay.tscn")
            .Instantiate<BlindBoxRevealOverlayController>();
        _bossBlindBoxOverlay.Name = "DesktopBlindBoxRevealOverlay";
        _bossBlindBoxOverlay.RewardClaimRequested += OnBossBlindBoxRewardClaimRequested;
        _bossBlindBoxOverlay.RevealStepChanged += step => _gameData.SetPendingBlindBoxRevealStep(step);
        _bossBlindBoxOverlay.RewardShown += () => _gameData.MarkPendingBlindBoxRewardShown();
        _bossKeyContent.AddChild(_bossBlindBoxOverlay);

        _bossRiseIntro = GD.Load<PackedScene>("res://Scenes/DesktopRiseIntro.tscn")
            .Instantiate<DesktopRiseIntroController>();
        _bossRiseIntro.Name = "DesktopRiseIntro";
        _bossRiseIntro.StatusBarRevealRequested += OnBossRiseIntroStatusBarRevealRequested;
        _bossRiseIntro.Finished += OnBossRiseIntroFinished;
        _bossKeyContent.AddChild(_bossRiseIntro);
        _bossRiseIntro.BindGameData(_gameData);

        _dogHitRect = new Rect2(60 + _contentOffset.X, 90 + _contentOffset.Y, 180, 180);
        _btnHitRect = new Rect2(50 + _contentOffset.X, 265 + _contentOffset.Y, 240, 40);

        _windowBaseSize = _bossKeyContent.GetNode<Marker2D>("ContentA/WindowSize").Position;
        var bossContentA = _bossKeyContent.GetNode<Node2D>("ContentA");
        bossContentA.Position = _contentOffset;
        ConfigureBossRiseIntro();
        UpdateBossBlindBoxOverlayPosition();
        SetupFatWindow();
        SetWindowAboveTaskbar();
        DisplayServer.WindowSetPosition(DisplayServer.WindowGetPosition());
        EnableLayeredWindow();

        _bossKeyContent.GetNode<CanvasLayer>("CanvasLayer").Offset = _contentOffset;
        ApplyBossCounterLayout();
        _bossKeyContent.GetNode<CanvasLayer>("Bubble").Offset = _contentOffset;
        _bossKeyContent.GetNode<CanvasLayer>("Bubble").Visible = false;
        RestoreBossBlindBoxRewardIfNeeded();
        CallDeferred(MethodName.RestoreBossBlindBoxRewardIfNeeded);

        var tracker = new GlobalInputTracker();
        tracker.Name = "GlobalInputTracker";
        tracker.GameData = _gameData;
        tracker.TypingInputOccurred += OnTypingInputOccurred;
        tracker.GlobalMousePressed += OnGlobalMousePressed;
        tracker.GlobalWinKeyPressed += OnGlobalWinKeyPressed;
        tracker.GlobalEscapeKeyPressed += OnGlobalEscapeKeyPressed;
        AddChild(tracker);

        CallDeferred(MethodName.PlayBossRiseIntro);
    }

    private double _displayTimer;
    private SettingsManager.DisplayMode _lastMode = (SettingsManager.DisplayMode)(-1);

    public override void _Process(double _)
    {
        UpdateFullscreenVisibility(_);
        UpdateEnhancedTopmost(_);
        UpdateDesktopActivityState(_);

        if (_hiddenByFullscreenApp)
            return;

        var mode = SettingsManager.CurrentDisplayMode;
        if (mode != _lastMode)
        {
            _lastMode = mode;
            _mainText.Text = mode switch
            {
                SettingsManager.DisplayMode.Clock => DateTime.Now.ToString("HH:mm"),
                SettingsManager.DisplayMode.Hidden => "",
                _ => "0"
            };
        }
        if (mode == SettingsManager.DisplayMode.Clock)
            _mainText.Text = DateTime.Now.ToString("HH:mm");
        else if (mode == SettingsManager.DisplayMode.Chips)
            _mainText.Text = _gameData.Chips.ToString();

        var localPos = DisplayServer.MouseGetPosition() - DisplayServer.WindowGetPosition();
        bool over = _settingsPanel.ContainsPoint(localPos);

        if (CurrentMode == Mode.BossKey)
        {
            over |= _dogHitRect.HasPoint(localPos) || _btnHitRect.HasPoint(localPos);
            over |= GetBossStatusPanelRect().HasPoint(localPos);
            if (_bossBlindBoxHint != null && _bossBlindBoxHint.Visible && _bossBlindBoxHint.MouseFilter != Control.MouseFilterEnum.Ignore)
                over |= GetBossBlindBoxHintRect().HasPoint(localPos);
            if (_bossBlindBoxOverlay != null && _bossBlindBoxOverlay.Visible)
                over |= GetBossBlindBoxOverlayRect().HasPoint(localPos);
        }
        else if (CurrentMode == Mode.Play)
        {
            if (_playViewport != null)
            {
                var gameRect = new Rect2(_playViewport.Position,
                    _playViewport.Size * _playViewport.Scale);
                over |= gameRect.HasPoint(localPos);
            }
            if (_infoPanel != null && _infoPanel.Visible)
            {
                int infoX = _infoPanelOnRight ? PlayInfoPanelWidth + PlayGameWidth + PlayGameSettingsGap : 0;
                over |= new Rect2(infoX, _contentOffset.Y, PlayInfoPanelWidth, 600).HasPoint(localPos);
            }
        }

        if (_isClickThrough && over) SetClickThrough(false);
        else if (!_isClickThrough && !over && !_isDragging) SetClickThrough(true);
    }

    public override void _Notification(int what)
    {
        if (what == NotificationWMWindowFocusOut && _settingsPanel.IsOpen
            && SettingsManager.LoadAutoHidePanel())
        {
            var mouse = DisplayServer.MouseGetPosition();
            var wp = DisplayServer.WindowGetPosition();
            var ws = DisplayServer.WindowGetSize();
            if (mouse.X < wp.X || mouse.X > wp.X + ws.X ||
                mouse.Y < wp.Y || mouse.Y > wp.Y + ws.Y)
                _settingsPanel.CloseImmediate();
        }
    }

    // ===== 模式切换 =====

    private Node2D _playRoot = null!;
    private SubViewportContainer _playViewport = null!;
    private InfoPanelController _infoPanel = null!;
    // 游玩模式布局状态：false=信息面板在左(默认), true=信息面板在右
    private bool _infoPanelOnRight;

    private void SwitchToPlay()
    {
        if (CurrentMode == Mode.Play) return;
        if (_settingsPanel.IsOpen) _settingsPanel.CloseImmediate();

        HideBossKeyContent();

        if (_playRoot == null)
        {
            _playRoot = GD.Load<PackedScene>("res://Scenes/PlayContent.tscn").Instantiate<Node2D>();
            _playRoot.Name = "PlayRoot";
            AddChild(_playRoot);
            _playViewport = _playRoot.GetNode<SubViewportContainer>("SubViewportContainer");

            // 信息面板由 ModeManager 直接管理（需要动态定位+避让）
            _infoPanel = GD.Load<PackedScene>("res://Scenes/InfoPanel.tscn").Instantiate<InfoPanelController>();
            _infoPanel.Name = "InfoPanel";
            AddChild(_infoPanel);
            _infoPanel.SettingsRequested += ToggleSettingsPanel;
            _infoPanel.BlindBoxRequested += OnBlindBoxRequested;

            // 连接 Main 中的 GameManager 信号
            _gameManager = _playRoot.GetNode<GameManager>("SubViewportContainer/SubViewport/Main");
            _gameManager.GameData = _gameData;
            _gameManager.SettingsPanel = _settingsPanel;
            _gameManager.BlindBoxRewardClaimRequested += OnBlindBoxRewardClaimRequested;

            // InfoPanel 绑定 GameData
            _infoPanel.Bind(_gameData);
            if (_gameData.PendingBlindBoxReward != null)
                _gameManager.ShowPendingBlindBoxReward(_gameData.PendingBlindBoxReward);
        }

        // 切换游玩模式的胖窗口尺寸（左信息面板 + 视觉缝隙 + 600×600 游戏内容 + 420 缓冲）
        SetupPlayFatWindow();
        KeepPlayContentWithinScreen();
        SetClickThrough(false);
        UpdatePlayLayout();
        _playRoot.Visible = true;
        _infoPanel.Visible = true;
        CurrentMode = Mode.Play;
        RefreshSettingsPanelModeActions();
    }

    private void UpdatePlayLayout()
    {
        var scrSize = DisplayServer.ScreenGetSize();
        var winPos = DisplayServer.WindowGetPosition();
        int baseY = (int)_contentOffset.Y;
        const int pad = 5;
        int gameX = PlayInfoPanelWidth;

        // 信息面板在左侧（默认）：屏幕范围 winPos.X ~ winPos.X + PlayInfoPanelWidth
        bool leftOk = winPos.X >= -pad && winPos.X + PlayInfoPanelWidth <= scrSize.X + pad;

        _infoPanelOnRight = !leftOk;

        // 游戏面板位置固定，信息面板自己绕到右侧
        _playViewport.Position = new Vector2(gameX, baseY);
        _infoPanel.SetPanelPosition(new Vector2(_infoPanelOnRight ? gameX + PlayGameWidth + PlayGameSettingsGap : 0, baseY));
    }

    private void SwitchToBossKey()
    {
        if (CurrentMode == Mode.BossKey) return;
        if (_settingsPanel.IsOpen) _settingsPanel.CloseImmediate();

        if (_playRoot != null)
            _playRoot.Visible = false;
        if (_infoPanel != null)
            _infoPanel.Visible = false;

        ShowBossKeyContent();
        SetupFatWindow();
        SetClickThrough(true);
        CurrentMode = Mode.BossKey;
        RefreshSettingsPanelModeActions();
    }

    private void HideBossKeyContent()
    {
        _bossRiseIntro?.HideImmediate();
        _bossRiseIntroSuppressesBlindBoxHint = false;
        if (_bossDogVisual != null)
            _bossDogVisual.Visible = true;
        if (_bossStatusPanel != null)
            _bossStatusPanel.Visible = true;
        SetBossStatusBarInteractable(true);
        _bossKeyContent.Visible = false;
        // CanvasLayer 不继承 Node2D 的 Visible，需单独隐藏
        _bossKeyContent.GetNode<CanvasLayer>("CanvasLayer").Visible = false;
        _bossBlindBoxOverlay?.HideOverlay();
        _bossKeyContent.GetNode<CanvasLayer>("Bubble").Visible = false;
    }

    private void ShowBossKeyContent()
    {
        _bossKeyContent.Visible = true;
        _bossKeyContent.GetNode<CanvasLayer>("CanvasLayer").Visible = true;
        ApplyBossCounterLayout();
        UpdateBossBlindBoxOverlayPosition();
        RefreshBossDogVisuals();
        RefreshBossBlindBoxHint();
        RestoreBossBlindBoxRewardIfNeeded();
    }

    private void RefreshBossDogVisuals()
    {
        _bossDogVisual.RefreshEquippedDisguiseVisuals();
        _bossDogVisual.RefreshEquippedEyewear(showIfEquipped: true);
        _bossRiseIntro?.RefreshVisuals();
    }

    private void ConfigureBossRiseIntro()
    {
        if (_bossRiseIntro == null || _bossDogVisual == null)
            return;

        _bossRiseIntro.Configure(_contentOffset, _bossDogVisual.Position, _bossDogVisual.Scale, _bossTaskBarAnchor.Position.Y);
    }

    private void PlayBossRiseIntro()
    {
        if (_bossRiseIntro == null || _hiddenByFullscreenApp || CurrentMode != Mode.BossKey)
            return;

        ConfigureBossRiseIntro();
        _bossDogVisual.Visible = false;
        _bossStatusPanel.Visible = false;
        SetBossStatusBarInteractable(false);
        _bossRiseIntroSuppressesBlindBoxHint = true;
        RefreshBossBlindBoxHint();
        _bossRiseIntro.Play();
    }

    private void OnBossRiseIntroStatusBarRevealRequested()
    {
        if (CurrentMode != Mode.BossKey || _hiddenByFullscreenApp)
            return;

        _bossStatusPanel.Visible = true;
        SetBossStatusBarInteractable(false);
    }

    private void OnBossRiseIntroFinished()
    {
        _bossRiseIntroSuppressesBlindBoxHint = false;
        if (CurrentMode != Mode.BossKey || _hiddenByFullscreenApp)
            return;

        _bossDogVisual.Visible = true;
        _bossStatusPanel.Visible = true;
        SetBossStatusBarInteractable(true);
        RefreshBossDogVisuals();
        RefreshBossBlindBoxHint();
    }

    private void SetBossStatusBarInteractable(bool interactable)
    {
        if (_bossModeButton == null || _bossSystemButton == null)
            return;

        _bossStatusBarInteractable = interactable;
        _bossModeButton.Disabled = false;
        _bossSystemButton.Disabled = false;
        _bossModeButton.MouseFilter = Control.MouseFilterEnum.Stop;
        _bossSystemButton.MouseFilter = Control.MouseFilterEnum.Stop;
    }

    private void ApplyBossCounterLayout()
    {
        if (_bossStatusPanel == null || _bossTaskBarAnchor == null)
            return;

        if (!SettingsManager.LoadCenterCounterOnTaskbar())
        {
            ApplyBossStatusPanelHeight(_bossStatusPanelBaseSize.Y);
            _bossStatusPanel.Position = _bossStatusPanelBasePosition;
            return;
        }

        var taskbarHeight = GetBottomTaskbarHeightAtWindow();
        if (taskbarHeight <= 0)
        {
            ApplyBossStatusPanelHeight(_bossStatusPanelBaseSize.Y);
            _bossStatusPanel.Position = _bossStatusPanelBasePosition;
            return;
        }

        var panelHeight = _bossStatusPanelBaseSize.Y > 0
            ? _bossStatusPanelBaseSize.Y
            : _bossStatusPanel.Size.Y;
        if (panelHeight <= 0)
            panelHeight = 29f;

        var taskbarTop = _bossTaskBarAnchor.Position.Y;
        var taskbarBottom = taskbarTop + taskbarHeight;
        var desiredTop = taskbarTop + (taskbarHeight - panelHeight) / 2f;
        var tongueLimitTop = GetBossTongueClearTopY();
        if (desiredTop < tongueLimitTop)
            desiredTop = tongueLimitTop;

        var availableHeight = taskbarBottom - desiredTop;
        var finalHeight = Mathf.Clamp(availableHeight, BossCounterMinimumHeight, panelHeight);
        ApplyBossStatusPanelHeight(finalHeight);

        _bossStatusPanel.Position = new Vector2(_bossStatusPanelBasePosition.X, desiredTop);
    }

    private void CaptureBossStatusPanelStyle()
    {
        if (_bossStatusPanel.GetThemeStylebox("panel") is not StyleBoxFlat style)
            return;

        _bossStatusPanelStyle = (StyleBoxFlat)style.Duplicate();
        _bossStatusPanel.AddThemeStyleboxOverride("panel", _bossStatusPanelStyle);
        _bossStatusPanelBaseMarginTop = _bossStatusPanelStyle.GetContentMargin(Side.Top);
        _bossStatusPanelBaseMarginBottom = _bossStatusPanelStyle.GetContentMargin(Side.Bottom);
    }

    private void ApplyBossStatusPanelHeight(float height)
    {
        if (_bossStatusPanel == null)
            return;

        var baseHeight = _bossStatusPanelBaseSize.Y > 0 ? _bossStatusPanelBaseSize.Y : _bossStatusPanel.Size.Y;
        if (baseHeight <= 0)
            baseHeight = height;

        var clampedHeight = Mathf.Clamp(height, BossCounterMinimumHeight, baseHeight);
        if (_bossStatusPanelStyle != null)
        {
            var shrink = Mathf.Max(0f, baseHeight - clampedHeight);
            _bossStatusPanelStyle.SetContentMargin(Side.Top, Mathf.Max(0f, _bossStatusPanelBaseMarginTop - shrink / 2f));
            _bossStatusPanelStyle.SetContentMargin(Side.Bottom, Mathf.Max(0f, _bossStatusPanelBaseMarginBottom - shrink / 2f));
        }

        _bossStatusPanel.Size = new Vector2(_bossStatusPanelBaseSize.X, clampedHeight);
    }

    private float GetBossTongueClearTopY()
    {
        var tongue = _bossDogVisual.GetNodeOrNull<Sprite2D>("HeadRoot/Tonghe");
        if (tongue?.Texture == null)
            return float.NegativeInfinity;

        var tongueHalfHeight = tongue.Texture.GetHeight() * Mathf.Abs(tongue.Scale.Y) * 0.5f;
        var dogScaleY = Mathf.Abs(_bossDogVisual.Scale.Y);
        return _bossDogVisual.Position.Y + (tongue.Position.Y + tongueHalfHeight) * dogScaleY + BossCounterTongueClearance;
    }

    private void OnBossModeButtonPressed()
    {
        if (!_bossStatusBarInteractable)
            return;

        SwitchToPlay();
    }

    private void OnBossSystemButtonPressed()
    {
        if (!_bossStatusBarInteractable)
            return;

        ToggleSettingsPanel();
    }

    private void RefreshBossBlindBoxHint()
    {
        if (_bossBlindBoxHint == null || _gameData == null)
            return;

        if (_bossRiseIntroSuppressesBlindBoxHint)
        {
            SetBossBlindBoxHintDisplayVisible(false);
            return;
        }

        if (_bossBlindBoxOverlay != null && _bossBlindBoxOverlay.Visible)
        {
            SetBossBlindBoxHintDisplayVisible(false);
            return;
        }

        var state = _gameData.GetBlindBoxHintState();
        var hideWaitingBubble = state.Status == BlindBoxHintStatus.Waiting
            && !SettingsManager.LoadAlwaysShowBlindBoxBubble();
        SetBossBlindBoxHintDisplayVisible(state.Status != BlindBoxHintStatus.PendingReward && !hideWaitingBubble);

        switch (state.Status)
        {
            case BlindBoxHintStatus.PendingReward:
                break;
            case BlindBoxHintStatus.Ready:
            case BlindBoxHintStatus.NotEnoughChips:
                _bossBlindBoxHint.ShowCost(_blindBoxIcon, state.Cost);
                break;
            default:
                _bossBlindBoxHint.ShowCountdown(TimeSpan.FromSeconds(state.RemainingSeconds));
                break;
        }
    }

    private void OnBlindBoxBubbleVisibilityChanged()
    {
        RefreshBossBlindBoxHint();
        _infoPanel?.RefreshBlindBoxButton();
    }

    private void OnBossBlindBoxHintPressed()
    {
        if (_gameData == null)
            return;

        var state = _gameData.GetBlindBoxHintState();
        GD.Print($"[BossKey BlindBoxHint] pressed, status={state.Status}, cost={state.Cost}, remaining={state.RemainingSeconds:0.0}");

        switch (state.Status)
        {
            case BlindBoxHintStatus.PendingReward:
                if (_gameData.PendingBlindBoxReward != null)
                    ShowBossBlindBoxReward(_gameData.PendingBlindBoxReward);
                break;
            case BlindBoxHintStatus.Ready:
                var pending = _gameData.TryOpenBlindBox();
                if (pending != null)
                    ShowBossBlindBoxReward(pending);
                break;
            case BlindBoxHintStatus.NotEnoughChips:
                _bossBlindBoxHint.FlashTextRed();
                break;
        }
    }

    private void SetBossBlindBoxHintDisplayVisible(bool visible)
    {
        _bossBlindBoxHint.SetDisplayVisible(visible);
    }

    private Rect2 GetBossBlindBoxHintRect()
    {
        return new Rect2(
            _contentOffset + _bossBlindBoxHint.Position,
            _bossBlindBoxHint.Size
        );
    }

    private Rect2 GetBossStatusPanelRect()
    {
        return new Rect2(
            _contentOffset + _bossStatusPanel.Position,
            _bossStatusPanel.Size
        );
    }

    private Rect2 GetBossBlindBoxOverlayRect()
    {
        var root = _bossBlindBoxOverlay.GetNodeOrNull<Control>("RevealRoot")
            ?? _bossBlindBoxOverlay.GetNodeOrNull<Control>("RewardRoot");
        if (root == null)
        {
            return new Rect2(
                _bossBlindBoxRevealAnchor.GlobalPosition + new Vector2(-150f, -302f),
                new Vector2(300f, 332f)
            );
        }

        var rect = new Rect2(_bossBlindBoxOverlay.Offset + root.Position, root.Size);
        // The speech-bubble tail extends below the 300x300 panel.
        rect.Size += new Vector2(0f, 32f);
        return rect;
    }

    private void ShowBossBlindBoxReward(PendingBlindBoxReward pending)
    {
        UpdateBossBlindBoxOverlayPosition();
        SetBossBlindBoxHintDisplayVisible(false);
        SetClickThrough(false);
        _bossBlindBoxOverlay.ShowReward(pending, animateDrop: !pending.RewardShown);
    }

    private void RestoreBossBlindBoxRewardIfNeeded()
    {
        if (CurrentMode != Mode.BossKey || _gameData.PendingBlindBoxReward == null)
            return;
        if (_bossBlindBoxOverlay != null && _bossBlindBoxOverlay.Visible)
            return;

        ShowBossBlindBoxReward(_gameData.PendingBlindBoxReward);
    }

    private void UpdateBossBlindBoxOverlayPosition()
    {
        if (_bossBlindBoxOverlay == null || _bossBlindBoxRevealAnchor == null)
            return;

        _bossBlindBoxOverlay.Offset = _bossBlindBoxRevealAnchor.GlobalPosition;
    }

    private void OnBossBlindBoxRewardClaimRequested()
    {
        _gameData.ClaimPendingBlindBoxReward();
        _bossBlindBoxOverlay.HideOverlay();
        RefreshBossBlindBoxHint();
        if (CurrentMode == Mode.BossKey)
            SetClickThrough(true);
    }

    private void OnRandomizeScene()
    {
        ApplyRandomEquipment(DebugSceneItemTypes);
    }

    private void OnRandomizeDog()
    {
        ApplyRandomEquipment(DebugDogItemTypes);
    }

    private void OnDogReactionRequested(int trigger)
    {
        if (CurrentMode == Mode.BossKey)
            _bossDogVisual.ApplyReaction((EDogReactionTrigger)trigger);
        else
            _gameManager?.OnPlayDogReaction(trigger);
    }

    private void OnRandomAcquireItem()
    {
        var items = LubanData.Tables.TbItem.DataList
            .Where(item => !item.IsUnique || !_gameData.Inventory.Owns(item.Id))
            .ToList();
        if (items.Count == 0)
            return;

        var item = items[_debugRandom.Next(items.Count)];
        _gameData.AddItem(item.Id, count: 1, markNew: true);
    }

    private void OnDebugGrantChips()
    {
        _gameData.ModifyChips(8000);
    }

    private void OnBlindBoxRequested()
    {
        if (_infoPanel == null)
            return;

        var pending = _gameData.TryOpenBlindBox();
        if (pending != null)
            _gameManager?.ShowPendingBlindBoxReward(pending);
    }

    private void OnBlindBoxRewardClaimRequested()
    {
        _gameData.ClaimPendingBlindBoxReward();
        _gameManager?.HidePendingBlindBoxReward();
    }

    private void OnTypingInputOccurred(int count)
    {
        _desktopInputEvents.Enqueue((Time.GetTicksMsec() / 1000.0, count));

        if (CurrentMode == Mode.BossKey && !_hiddenByFullscreenApp && _desktopTongueFeedbackEnabled)
            _bossDogVisual.PlayDesktopTongueTap(count);
    }

    private void OnGlobalMousePressed(Vector2I screenPosition)
    {
        AutoHideSettingsPanelIfClickedOutside(screenPosition);

        if (!SettingsManager.LoadEnhancedTopmostMode())
        {
            _waitingForWinMenuDismiss = false;
            _enhancedTopmostDelayedBoostTimer = 0.0;
            _recoverTopmostOnNextMousePressTimer = 0.0;
            return;
        }

        if (_recoverTopmostOnNextMousePressTimer > 0.0)
        {
            _recoverTopmostOnNextMousePressTimer = 0.0;
            StartEnhancedTopmostBoost();
            return;
        }

        if (_waitingForWinMenuDismiss)
        {
            _waitingForWinMenuDismiss = false;
            StartEnhancedTopmostBoostAfterShellDismiss();
            return;
        }

        if (IsPointInTaskbarArea(screenPosition))
        {
            StartEnhancedTopmostBoost();
        }
    }

    private void AutoHideSettingsPanelIfClickedOutside(Vector2I screenPosition)
    {
        if (_settingsPanel == null
            || !_settingsPanel.IsOpen
            || !SettingsManager.LoadAutoHidePanel())
            return;

        var now = Time.GetTicksMsec() / 1000.0;
        if (now - _settingsPanelOpenedAtSeconds < SettingsPanelAutoHideOpenGraceSeconds)
            return;

        var windowLocalPosition = screenPosition - DisplayServer.WindowGetPosition();
        if (_settingsPanel.ContainsPoint(windowLocalPosition))
            return;

        _settingsPanel.CloseImmediate();
    }

    private void OnGlobalWinKeyPressed()
    {
        if (!SettingsManager.LoadEnhancedTopmostMode())
        {
            _waitingForWinMenuDismiss = false;
            _enhancedTopmostDelayedBoostTimer = 0.0;
            _recoverTopmostOnNextMousePressTimer = 0.0;
            return;
        }

        if (_waitingForWinMenuDismiss)
        {
            _waitingForWinMenuDismiss = false;
            StartEnhancedTopmostBoostAfterShellDismiss();
            return;
        }

        _waitingForWinMenuDismiss = true;
    }

    private void OnGlobalEscapeKeyPressed()
    {
        if (!SettingsManager.LoadEnhancedTopmostMode())
        {
            _waitingForWinMenuDismiss = false;
            _enhancedTopmostDelayedBoostTimer = 0.0;
            _recoverTopmostOnNextMousePressTimer = 0.0;
            return;
        }

        if (!_waitingForWinMenuDismiss)
            return;

        _waitingForWinMenuDismiss = false;
        StartEnhancedTopmostBoostAfterShellDismiss();
    }

    private void StartEnhancedTopmostBoostAfterShellDismiss()
    {
        StartEnhancedTopmostBoost();
        _enhancedTopmostDelayedBoostTimer = 0.08;
        _recoverTopmostOnNextMousePressTimer = RecoverTopmostOnNextMousePressSeconds;
    }

    private void StartEnhancedTopmostBoost()
    {
        _enhancedTopmostBoostTimer = 0.5;
        _enhancedTopmostTimer = 0.0;
        ReassertTopmostNoActivate();
    }

    private void UpdateFullscreenVisibility(double delta)
    {
        _fullscreenCheckTimer -= delta;
        if (_fullscreenCheckTimer > 0.0)
            return;

        _fullscreenCheckTimer = 0.5;
        var shouldHide = !SettingsManager.LoadShowOverFullscreenApps() && IsOtherAppFullscreen();
        if (shouldHide == _hiddenByFullscreenApp)
            return;

        SetHiddenByFullscreenApp(shouldHide);
        if (!shouldHide)
        {
            SetClickThrough(CurrentMode == Mode.BossKey);
            if (CurrentMode == Mode.Play)
                UpdatePlayLayout();
        }
    }

    private void UpdateEnhancedTopmost(double delta)
    {
        if (CurrentMode != Mode.BossKey || _hiddenByFullscreenApp || !SettingsManager.LoadEnhancedTopmostMode())
            return;

        if (_enhancedTopmostDelayedBoostTimer > 0.0)
        {
            _enhancedTopmostDelayedBoostTimer -= delta;
            if (_enhancedTopmostDelayedBoostTimer <= 0.0)
                StartEnhancedTopmostBoost();
        }

        if (_recoverTopmostOnNextMousePressTimer > 0.0)
            _recoverTopmostOnNextMousePressTimer -= delta;

        if (_enhancedTopmostBoostTimer <= 0.0)
            return;

        if (_enhancedTopmostBoostTimer > 0.0)
            _enhancedTopmostBoostTimer -= delta;

        _enhancedTopmostTimer -= delta;
        if (_enhancedTopmostTimer > 0.0)
            return;

        _enhancedTopmostTimer = 0.016;
        ReassertTopmostNoActivate();
    }

    private static bool IsPointInTaskbarArea(Vector2I screenPosition)
    {
        for (int i = 0; i < DisplayServer.GetScreenCount(); i++)
        {
            var screen = new Rect2I(DisplayServer.ScreenGetPosition(i), DisplayServer.ScreenGetSize(i));
            if (!screen.HasPoint(screenPosition)) continue;

            var usable = DisplayServer.ScreenGetUsableRect(i);
            return !usable.HasPoint(screenPosition);
        }

        return false;
    }

    private void SetHiddenByFullscreenApp(bool hidden)
    {
        _hiddenByFullscreenApp = hidden;
        if (hidden)
        {
            if (_settingsPanel.IsOpen)
                _settingsPanel.CloseImmediate();
            HideBossKeyContent();
            if (_playRoot != null)
                _playRoot.Visible = false;
            if (_infoPanel != null)
                _infoPanel.Visible = false;
            return;
        }

        if (CurrentMode == Mode.BossKey)
            ShowBossKeyContent();
        else if (CurrentMode == Mode.Play)
        {
            if (_playRoot != null)
                _playRoot.Visible = true;
            if (_infoPanel != null)
                _infoPanel.Visible = true;
        }
    }

    private static bool IsOtherAppFullscreen()
    {
        var foreground = WindowNative.GetForegroundWindow();
        var ownWindow = (IntPtr)DisplayServer.WindowGetNativeHandle(DisplayServer.HandleType.WindowHandle);
        if (foreground == IntPtr.Zero || foreground == ownWindow)
            return false;

        if (!WindowNative.GetWindowRect(foreground, out var rect))
            return false;

        var windowRect = new Rect2I(
            rect.Left,
            rect.Top,
            Math.Max(0, rect.Right - rect.Left),
            Math.Max(0, rect.Bottom - rect.Top)
        );

        for (int i = 0; i < DisplayServer.GetScreenCount(); i++)
        {
            var screen = new Rect2I(DisplayServer.ScreenGetPosition(i), DisplayServer.ScreenGetSize(i));
            if (CoversScreen(windowRect, screen))
                return true;
        }

        return false;
    }

    private static bool CoversScreen(Rect2I windowRect, Rect2I screenRect)
    {
        const int tolerance = 2;
        return windowRect.Position.X <= screenRect.Position.X + tolerance
            && windowRect.Position.Y <= screenRect.Position.Y + tolerance
            && windowRect.End.X >= screenRect.End.X - tolerance
            && windowRect.End.Y >= screenRect.End.Y - tolerance;
    }

    private void UpdateDesktopActivityState(double delta)
    {
        var now = Time.GetTicksMsec() / 1000.0;
        while (_desktopInputEvents.Count > 0 && now - _desktopInputEvents.Peek().time > DesktopActivitySampleSeconds)
            _desktopInputEvents.Dequeue();

        if (CurrentMode != Mode.BossKey)
        {
            ResetDesktopActivityCandidate();
            return;
        }

        var state = ResolveDesktopActivityState(GetDesktopInputEventsPerMinute(now));
        if (state == null)
        {
            ResetDesktopActivityCandidate();
            return;
        }

        if (_desktopActivityCooldownSeconds > 0.0)
        {
            _desktopActivityCooldownSeconds -= delta;
            return;
        }

        if (_currentDesktopActivityState != null && _currentDesktopActivityState.Id == state.Id)
        {
            ResetDesktopActivityCandidate();
            return;
        }

        if (_candidateDesktopActivityState == null || _candidateDesktopActivityState.Id != state.Id)
        {
            _candidateDesktopActivityState = state;
            _candidateDesktopActivitySeconds = 0.0;
            return;
        }

        _candidateDesktopActivitySeconds += delta;
        if (_candidateDesktopActivitySeconds < state.MinDurationSeconds)
            return;

        _currentDesktopActivityState = state;
        _candidateDesktopActivityState = null;
        _candidateDesktopActivitySeconds = 0.0;
        _desktopActivityCooldownSeconds = state.CooldownSeconds;
        _desktopTongueFeedbackEnabled = state.EnableTongueFeedback;
        _bossDogVisual.ApplyReaction(state.DogReactionTrigger);
    }

    private double GetDesktopInputEventsPerMinute(double now)
    {
        var count = _desktopInputEvents.Sum(item => item.count);
        var elapsed = _desktopInputEvents.Count == 0
            ? DesktopActivitySampleSeconds
            : Math.Min(DesktopActivitySampleSeconds, Math.Max(1.0, now - _desktopInputEvents.Peek().time));
        return count / elapsed * 60.0;
    }

    private static DesktopActivityState ResolveDesktopActivityState(double inputEventsPerMinute)
    {
        return LubanData.Tables.TbDesktopActivityState.DataList
            .Where(state => inputEventsPerMinute >= state.MinInputEventsPerMinute
                && (state.MaxInputEventsPerMinute == 0 || inputEventsPerMinute <= state.MaxInputEventsPerMinute))
            .OrderByDescending(state => state.Priority)
            .ThenBy(state => state.Id)
            .FirstOrDefault();
    }

    private void ResetDesktopActivityCandidate()
    {
        _candidateDesktopActivityState = null;
        _candidateDesktopActivitySeconds = 0.0;
    }

    private void ApplyRandomEquipment(IEnumerable<EItemType> types)
    {
        foreach (var type in types)
        {
            var items = _gameData.Inventory.GetOwnedOfType(type)
                .OrderBy(item => item.Id)
                .ToList();
            if (items.Count == 0) continue;

            if (!_debugItemBags.TryGetValue(type, out var bag))
            {
                bag = new ShuffleBag<int>();
                _debugItemBags[type] = bag;
            }

            var equippedId = _gameData.Inventory.GetEquipped(type)?.Id ?? -1;
            var pickedId = bag.Pick(items.Select(item => item.Id).ToList(), _debugRandom, equippedId);
            _gameData.EquipItem(pickedId);
        }
    }

    // ===== 面板切换 =====

    private void ToggleSettingsPanel()
    {
        if (_settingsPanel.IsOpen)
        {
            _settingsPanel.Close();
            return;
        }
        RefreshSettingsPanelModeActions();
        PositionPanelInBestSlot();
        _settingsPanel.Open();
        _settingsPanelOpenedAtSeconds = Time.GetTicksMsec() / 1000.0;
    }

    private void RefreshSettingsPanelModeActions()
    {
        _settingsPanel?.SetCurrentMode(CurrentMode == Mode.BossKey);
    }

    private void PositionPanelInBestSlot()
    {
        var winPos = DisplayServer.WindowGetPosition();
        var scrSize = DisplayServer.ScreenGetSize();
        int pw = (int)_panelSize.X;
        int ph = (int)_panelSize.Y;
        const int pad = 5;

        // 根据模式取 A 区位置和尺寸
        float aX, aY; int aw, ah;
        if (CurrentMode == Mode.Play && _playViewport != null)
        {
            aX = _playViewport.Position.X;
            aY = _playViewport.Position.Y;
            aw = (int)(_playViewport.Size.X * _playViewport.Scale.X);
            ah = (int)(_playViewport.Size.Y * _playViewport.Scale.Y);
        }
        else
        {
            aX = _contentOffset.X;
            aY = _contentOffset.Y;
            aw = (int)_windowBaseSize.X;
            ah = (int)_windowBaseSize.Y;
        }

        bool Fits(int sx, int sy)
        {
            if (sx < -pad || sx + pw > scrSize.X + pad ||
                sy < -pad || sy + ph > scrSize.Y + pad)
                return false;
            // 游玩模式下避开信息面板区域
            if (CurrentMode == Mode.Play && _infoPanel != null && _infoPanel.Visible)
            {
                int infoX = _infoPanelOnRight
                    ? (int)aX + aw + PlayGameSettingsGap : 0;
                int infoY = (int)aY;
                var setRect = new Rect2(sx, sy, pw, ph);
                // info 坐标是窗口空间，Fits 是屏幕空间，需加 winPos
                var infoRect = new Rect2(winPos.X + infoX, winPos.Y + infoY, PlayInfoPanelWidth, 600);
                if (setRect.Intersects(infoRect))
                    return false;
            }
            return true;
        }

        float bY = aY + ah - ph;
        float centerX = aX + aw / 2f - pw / 2f;

        var slotPriority = CurrentMode == Mode.Play ? PlayPanelSlotPriority : BossKeyPanelSlotPriority;

        foreach (var slot in slotPriority)
        {
            var (wx, wy) = GetPanelSlotPosition(slot, aX, aY, aw, ah, pw, ph, bY, centerX);
            if (CurrentMode == Mode.Play && (slot == 6 || slot == 9 || slot == 3))
                wx += PlayGameSettingsGap;
            if (Fits(winPos.X + wx, winPos.Y + wy))
            {
                _settingsPanel.SetPanelPosition(new Vector2(wx, wy));
                return;
            }
        }
        // 兜底：覆盖在 A 区中央
        _settingsPanel.SetPanelPosition(new Vector2(aX + aw / 2f - pw / 2f, aY + ah / 2f - ph / 2f));
    }

    private static (int wx, int wy) GetPanelSlotPosition(
        int slot, float aX, float aY, int aw, int ah, int pw, int ph, float bY, float centerX)
    {
        return slot switch
        {
            9 => ((int)aX + aw, 0),
            8 => ((int)centerX, 0),
            7 => (0, 0),
            6 => ((int)aX + aw, (int)bY),
            4 => (0, (int)bY),
            3 => ((int)aX + aw, (int)aY + ah),
            2 => ((int)centerX, (int)aY + ah),
            1 => (0, (int)aY + ah),
            _ => ((int)(aX + aw / 2f - pw / 2f), (int)(aY + ah / 2f - ph / 2f)),
        };
    }

    // ===== 窗口管理 =====

    private void SetupFatWindow()
    {
        DisplayServer.WindowSetFlag(DisplayServer.WindowFlags.Transparent, true);
        DisplayServer.WindowSetFlag(DisplayServer.WindowFlags.AlwaysOnTop, true);

        int winW = (int)_windowBaseSize.X + (int)_panelSize.X * 2;
        int winH = (int)_windowBaseSize.Y + (int)_panelSize.Y * 2;
        var pos = DisplayServer.WindowGetPosition();
        DisplayServer.WindowSetSize(new Vector2I(winW, winH));
        DisplayServer.WindowSetPosition(pos);
        RenderingServer.SetDefaultClearColor(new Color(0, 0, 0, 0));
    }

    private void ApplyTaskbarSnap(ref Vector2I newPos)
    {
        if (!SettingsManager.LoadSnapToWindowsTaskbar())
        {
            _taskbarSnapped = false;
            return;
        }

        var scrRect = DisplayServer.ScreenGetUsableRect();
        int taskbarTop = scrRect.Position.Y + scrRect.Size.Y;
        var anchor = _bossKeyContent.GetNode<Marker2D>("ContentA/TaskBar").Position;
        int anchorY = (int)(_contentOffset.Y + anchor.Y);
        int snappedY = taskbarTop - anchorY;

        int dist = Math.Abs(newPos.Y - snappedY);

        if (_taskbarSnapped)
        {
            if (newPos.Y < snappedY - BreakawayThreshold)
                _taskbarSnapped = false;
            else
                newPos.Y = snappedY;
        }
        else if (dist < SnapThreshold)
        {
            _taskbarSnapped = true;
            newPos.Y = snappedY;
        }
    }

    private void SetupPlayFatWindow()
    {
        int pw = (int)_panelSize.X;
        int ph = (int)_panelSize.Y;
        int contentW = PlayInfoPanelWidth + PlayGameWidth;
        int contentH = 600;
        int winW = contentW + pw * 2;
        int winH = Math.Max(contentH, ph) + ph * 2;
        // 只 resize，保留窗口当前位置，不让内容跳位
        var pos = DisplayServer.WindowGetPosition();
        DisplayServer.WindowSetSize(new Vector2I(winW, winH));
        DisplayServer.WindowSetPosition(pos);
    }

    private void KeepPlayContentWithinScreen()
    {
        const int contentW = PlayInfoPanelWidth + PlayGameWidth;
        const int contentH = 600;
        const int pad = 5;

        var pos = DisplayServer.WindowGetPosition();
        var contentTopLeft = new Vector2I(pos.X, pos.Y + (int)_contentOffset.Y);
        var contentRect = new Rect2I(contentTopLeft, new Vector2I(contentW, contentH));
        var screen = GetBestScreenUsableRect(contentRect);

        int newX = pos.X;
        int newY = pos.Y;

        if (contentRect.Position.X < screen.Position.X + pad)
            newX += screen.Position.X + pad - contentRect.Position.X;
        else if (contentRect.End.X > screen.End.X - pad)
            newX -= contentRect.End.X - (screen.End.X - pad);

        if (contentRect.Position.Y < screen.Position.Y + pad)
            newY += screen.Position.Y + pad - contentRect.Position.Y;
        else if (contentRect.End.Y > screen.End.Y - pad)
            newY -= contentRect.End.Y - (screen.End.Y - pad);

        DisplayServer.WindowSetPosition(new Vector2I(newX, newY));
    }

    private static Rect2I GetBestScreenUsableRect(Rect2I targetRect)
    {
        var targetCenter = targetRect.Position + targetRect.Size / 2;
        Rect2I best = DisplayServer.ScreenGetUsableRect();
        long bestDistance = long.MaxValue;

        for (int i = 0; i < DisplayServer.GetScreenCount(); i++)
        {
            var screen = DisplayServer.ScreenGetUsableRect(i);
            if (screen.Intersects(targetRect))
                return screen;

            var center = screen.Position + screen.Size / 2;
            long dx = center.X - targetCenter.X;
            long dy = center.Y - targetCenter.Y;
            long distance = dx * dx + dy * dy;
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = screen;
            }
        }

        return best;
    }

    private static int GetBottomTaskbarHeightAtWindow()
    {
        var windowRect = new Rect2I(DisplayServer.WindowGetPosition(), DisplayServer.WindowGetSize());
        var windowCenter = windowRect.Position + windowRect.Size / 2;
        int fallback = 0;

        for (int i = 0; i < DisplayServer.GetScreenCount(); i++)
        {
            var screen = new Rect2I(DisplayServer.ScreenGetPosition(i), DisplayServer.ScreenGetSize(i));
            var usable = DisplayServer.ScreenGetUsableRect(i);
            var bottomHeight = Math.Max(0, screen.End.Y - usable.End.Y);
            if (bottomHeight > 0 && fallback == 0)
                fallback = bottomHeight;

            if (screen.HasPoint(windowCenter) || screen.Intersects(windowRect))
                return bottomHeight;
        }

        return fallback;
    }

    private void SetWindowAboveTaskbar()
    {
        var scrRect = DisplayServer.ScreenGetUsableRect();
        int taskbarTop = (int)(scrRect.Position.Y + scrRect.Size.Y);
        int winW = DisplayServer.WindowGetSize().X;
        var anchor = _bossKeyContent.GetNode<Marker2D>("ContentA/TaskBar").Position;
        int anchorY = (int)(_contentOffset.Y + anchor.Y);
        int x = (int)(scrRect.Position.X + (scrRect.Size.X - winW) / 2);
        int y = taskbarTop - anchorY;
        DisplayServer.WindowSetPosition(new Vector2I(x, y));
        ApplyBossCounterLayout();
    }

    private void EnableLayeredWindow()
    {
        ApplyNativeWindowStyles(clickThrough: true);
    }

    private void SetClickThrough(bool enabled)
    {
        _isClickThrough = enabled;
        ApplyNativeWindowStyles(enabled);
    }

    private static void ApplyNativeWindowStyles(bool clickThrough)
    {
        var hWnd = (IntPtr)DisplayServer.WindowGetNativeHandle(DisplayServer.HandleType.WindowHandle);
        if (hWnd == IntPtr.Zero) return;

        var style = WindowNative.GetWindowLong(hWnd, WindowNative.GWL_EXSTYLE);
        style |= WindowNative.WS_EX_LAYERED;
        if (clickThrough)
            style |= WindowNative.WS_EX_TRANSPARENT;
        else
            style &= ~WindowNative.WS_EX_TRANSPARENT;

        WindowNative.SetWindowLong(hWnd, WindowNative.GWL_EXSTYLE, style);
        WindowNative.SetWindowPos(hWnd, WindowNative.HWND_TOPMOST, 0, 0, 0, 0,
            WindowNative.SWP_NOMOVE | WindowNative.SWP_NOSIZE | WindowNative.SWP_SHOWWINDOW);
    }

    private static void ReassertTopmostNoActivate()
    {
        var hWnd = (IntPtr)DisplayServer.WindowGetNativeHandle(DisplayServer.HandleType.WindowHandle);
        if (hWnd == IntPtr.Zero) return;

        WindowNative.SetWindowPos(hWnd, WindowNative.HWND_TOPMOST, 0, 0, 0, 0,
            WindowNative.SWP_NOMOVE
            | WindowNative.SWP_NOSIZE
            | WindowNative.SWP_NOACTIVATE
            | WindowNative.SWP_SHOWWINDOW);
    }

    public override void _Input(InputEvent @event)
    {
        if (@event is InputEventMouseButton mb && mb.ButtonIndex == MouseButton.Left)
        {
            if (mb.Pressed)
            {
                var localPos = DisplayServer.MouseGetPosition() - DisplayServer.WindowGetPosition();
                if (_settingsPanel.ContainsPoint(localPos)) return;
                if (_infoPanel != null && _infoPanel.Visible)
                {
                    int infoX = _infoPanelOnRight ? PlayInfoPanelWidth + PlayGameWidth + PlayGameSettingsGap : 0;
                    if (new Rect2(infoX, _contentOffset.Y, PlayInfoPanelWidth, 600).HasPoint(localPos)) return;
                }

                _mouseScreenStart = DisplayServer.MouseGetPosition();
                _windowPosStart = DisplayServer.WindowGetPosition();
                _potentialDrag = true;
            }
            else
            {
                if (_isDragging) GetViewport().SetInputAsHandled();
                _isDragging = false; _potentialDrag = false;
                _taskbarSnapped = false;
            }
        }
        else if (@event is InputEventMouseMotion && _potentialDrag)
        {
            var d = DisplayServer.MouseGetPosition() - _mouseScreenStart;
            if (Mathf.Abs(d.X) > DragThreshold || Mathf.Abs(d.Y) > DragThreshold)
            { _isDragging = true; SetClickThrough(false); }
            if (_isDragging)
            {
                var newPos = _windowPosStart + d;
                ApplyTaskbarSnap(ref newPos);
                DisplayServer.WindowSetPosition(newPos);
                if (CurrentMode == Mode.BossKey)
                    ApplyBossCounterLayout();
                if (_settingsPanel.IsOpen)
                    PositionPanelInBestSlot();
                if (CurrentMode == Mode.Play)
                    UpdatePlayLayout();
                GetViewport().SetInputAsHandled();
            }
        }
    }

    private sealed class ShuffleBag<T>
    {
        private readonly Queue<T> _queue = new();
        private List<T> _lastCandidates = new();
        private T _lastPicked;
        private bool _hasLastPicked;

        public T Pick(IReadOnlyList<T> candidates, Random rng, T avoid)
        {
            if (candidates.Count == 0)
                throw new InvalidOperationException("ShuffleBag needs at least one candidate.");

            if (_queue.Count == 0 || !HasSameCandidates(candidates))
                Refill(candidates, rng, avoid);

            var picked = _queue.Dequeue();
            _lastPicked = picked;
            _hasLastPicked = true;
            return picked;
        }

        private void Refill(IReadOnlyList<T> candidates, Random rng, T avoid)
        {
            _lastCandidates = candidates.ToList();
            var shuffled = candidates.ToList();
            for (int i = shuffled.Count - 1; i > 0; i--)
            {
                int j = rng.Next(i + 1);
                (shuffled[i], shuffled[j]) = (shuffled[j], shuffled[i]);
            }

            MoveFirstRepeatBack(shuffled, avoid);
            if (_hasLastPicked)
                MoveFirstRepeatBack(shuffled, _lastPicked);

            _queue.Clear();
            foreach (var item in shuffled)
                _queue.Enqueue(item);
        }

        private static void MoveFirstRepeatBack(List<T> shuffled, T avoid)
        {
            if (shuffled.Count <= 1 || !EqualityComparer<T>.Default.Equals(shuffled[0], avoid))
                return;

            for (int i = 1; i < shuffled.Count; i++)
            {
                if (!EqualityComparer<T>.Default.Equals(shuffled[i], avoid))
                {
                    (shuffled[0], shuffled[i]) = (shuffled[i], shuffled[0]);
                    return;
                }
            }
        }

        private bool HasSameCandidates(IReadOnlyList<T> candidates)
        {
            return _lastCandidates.Count == candidates.Count
                && !_lastCandidates.Except(candidates).Any()
                && !candidates.Except(_lastCandidates).Any();
        }
    }
}
