using Godot;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using DataTables;

namespace LuckyDogRise;

public partial class SystemPanelController : CanvasLayer
{
#if DEBUG
    [Signal] public delegate void RandomizeRequestedEventHandler();
    [Signal] public delegate void RandomizeDogRequestedEventHandler();
    [Signal] public delegate void RandomAcquireItemRequestedEventHandler();
    [Signal] public delegate void DebugGrantChipsRequestedEventHandler();
    [Signal] public delegate void DebugGrantLuckyDealsRequestedEventHandler();
    [Signal] public delegate void DogReactionRequestedEventHandler(int trigger);
    [Signal] public delegate void GlobalMouseListeningDisabledChangedEventHandler(bool disabled);
    [Signal] public delegate void SteamMockPanelVisibilityChangedEventHandler(bool visible);
#endif
    [Signal] public delegate void SwitchToPlayRequestedEventHandler();
    [Signal] public delegate void SwitchToBossKeyRequestedEventHandler();
    [Signal] public delegate void QuitRequestedEventHandler();
    [Signal] public delegate void DesktopBgmPlaybackChangedEventHandler(bool enabled);
    [Signal] public delegate void BlindBoxBubbleVisibilityChangedEventHandler();
    [Signal] public delegate void CounterLayoutChangedEventHandler();

    [Export] private Label _buildVersionLabel = null!;
    [Export] private Label _globalInputChipsEarnedLabel = null!;
    [Export] private OptionButton _armAppearanceOption = null!;
    [Export] private OptionButton _pokerFrameRateOption = null!;
    [Export] private CheckButton _vsyncToggle = null!;
    [Export] private CheckButton _disableGlobalMouseListeningToggle = null!;
    [Export] private Button _exportDiagnosticsButton = null!;
    [Export] private Label _exportDiagnosticsStatusLabel = null!;
    [Export] private PackedScene _linkTreeBannerScene = null!;

    public bool IsOpen => _panel.Visible;

    public Vector2 PanelSize
    {
        get
        {
            var s = _panel.Size;
            var min = _panel.CustomMinimumSize;
            return new Vector2(Mathf.Max(s.X, min.X), Mathf.Max(s.Y, min.Y));
        }
    }

    private PanelContainer _panel = null!;
    private ScrollContainer _panelScroll = null!;
    private Tween _tween = null!;
    private const float PanelScrollDragThreshold = 8f;
    private bool _panelScrollDragPotential;
    private bool _panelScrollDragging;
    private Vector2 _panelScrollDragStartPosition;
    private int _panelScrollDragStartValue;
    private BaseButton _panelScrollPressedButton;

    // 页签按钮
    private Button _settingsTab = null!;
    private Button _wardrobeTab = null!;
    private Button _linkTreeTab = null!;
#if DEBUG
    private Button _debugTab = null!;
#endif

    // 页签内容容器
    private VBoxContainer _settingsContent = null!;
    private VBoxContainer _wardrobeContent = null!;
    private VBoxContainer _linkTreeContent = null!;
    private Control _linkTreeStatusCenter = null!;
    private Label _linkTreeStatusLabel = null!;
#if DEBUG
    private VBoxContainer _debugContent = null!;
#endif
    private Control _settingsActionTopGap = null!;
    private Control _settingsActionRow = null!;
    private Control _settingsActionBottomGap = null!;
    private Control _settingsActionSep = null!;
    private Button _switchToPlayBtn = null!;
    private Button _switchToBossKeyBtn = null!;

    // Settings 页
    private HSlider _sfxVolumeSlider = null!;
    private HSlider _bgmVolumeSlider = null!;
    private Label _sfxVolumeValueLabel = null!;
    private Label _bgmVolumeValueLabel = null!;
    private CheckButton _desktopBgmToggle = null!;
    private CheckButton _rightClickQuickModeSwitchToggle = null!;
    private CheckButton _preventAccidentalDragToggle = null!;
    private OptionButton _languageOption = null!;
    private OptionButton _displayOption = null!;
#if DEBUG
    private OptionButton _saveDataModeOption = null!;
#endif
    private CheckButton _blindBoxBubbleToggle = null!;
    private CheckButton _pokerGuideOverlayToggle = null!;
    private CheckButton _autoEquipToggle = null!;
    private CheckButton _taskbarSnapToggle = null!;
    private bool _refreshingArmAppearanceOption;

#if DEBUG
    // Debug 页
    private ConfirmOverlayController _resetSaveConfirm = null!;
    private Label _seedLabel = null!;
    private Label _playTimeLabel = null!;
    private Label _luckyDealBuffLabel = null!;
    private Button _blindBoxDebugToggle = null!;
    private Control _blindBoxDebugContent = null!;
    private Label _blindBoxDebugLabel = null!;
    private CheckButton _blindBoxPaymentSourceLabelsToggle = null!;
    private CheckButton _blindBoxLocalTestModeToggle = null!;
    private Label _blindBoxLocalTestPreparedRewardCount = null!;
    private Button _blindBoxLocalTestPreparedRewardDecrease = null!;
    private Button _blindBoxLocalTestPreparedRewardIncrease = null!;
    private Button _blindBoxLocalTestAdvance = null!;
    private Button _blindBoxLocalTestClear = null!;
    private Label _playerProgressDebugLabel = null!;
    private OptionButton _playerProgressMultiplierOption = null!;
    private LineEdit _seedInput = null!;
    private OptionButton _reactionOption = null!;
    private int _currentSeed;
    private double _debugTimeRefreshTimer;
    private bool _resetPlayerProgressPending;
    private bool _steamMockActive;
    private CheckButton _showDeveloperLauncherNextStartupToggle = null!;
#endif

    // Wardrobe 页
    private GridContainer _wardrobeGrid = null!;
    private Control _emptyWardrobeCenter = null!;
    private HBoxContainer _typeFilterRow = null!;
    private TabGroup _selectedTab = null!;
    private Label _emptyWardrobeLabel = null!;
    private GameData _gameData = null!;
    private IGamePlatformService _platformService = null!;
    private IPlatformInventoryService _inventoryService;
    private IRecoverablePlatformService _recoverablePlatformService;
    private LinkTreePageState _linkTreePageState = LinkTreePageState.Loading;
    private double _linkTreeLoadingElapsedSeconds;
    private bool _linkTreeShowRecoveryAdvice;
    private bool _refreshLinkTreeSelectionOnNextInventorySnapshot = true;
    private double _linkTreeInventoryRecheckRemainingSeconds = -1.0;
    private int _linkTreeInventoryRecheckAttemptCount;
    public GameData GameData
    {
        get => _gameData;
        set
        {
            if (_gameData != null)
            {
                _gameData.EquipmentChanged -= RefreshWardrobeGrid;
                _gameData.InventoryChanged -= RefreshWardrobeGrid;
                _gameData.RefreshmentStateChanged -= RefreshWardrobeGrid;
                _gameData.EquipmentChanged -= RefreshArmAppearanceSelection;
                _gameData.InventoryChanged -= BuildArmAppearanceOptions;
                _gameData.ChipsChanged -= OnChipsChangedForGlobalInputLabel;
                _gameData.BlindBoxStateChanged -= OnSharedInventoryTransactionStateChanged;
            }

            _gameData = value;
            SettingsManager.InitializePokerGuideOverlaySetting(_gameData.NeedsPokerBasicsGuidance);
            _pokerGuideOverlayToggle?.SetPressedNoSignal(
                SettingsManager.LoadPokerGuideOverlayEnabled());
            _gameData.EquipmentChanged += RefreshWardrobeGrid;
            _gameData.InventoryChanged += RefreshWardrobeGrid;
            _gameData.RefreshmentStateChanged += RefreshWardrobeGrid;
            _gameData.EquipmentChanged += RefreshArmAppearanceSelection;
            _gameData.InventoryChanged += BuildArmAppearanceOptions;
            _gameData.ChipsChanged += OnChipsChangedForGlobalInputLabel;
            _gameData.BlindBoxStateChanged += OnSharedInventoryTransactionStateChanged;
            EnsureCurrentTabReady();
            if (IsNodeReady())
            {
                BuildArmAppearanceOptions();
                RefreshGlobalInputChipsEarnedLabel();
#if DEBUG
                RefreshBlindBoxLocalTestControls();
#endif
            }
        }
    }

    public IGamePlatformService PlatformService
    {
        get => _platformService;
        set
        {
            if (_inventoryService != null)
            {
                _inventoryService.InventorySnapshotChanged -= OnPlatformInventorySnapshotChanged;
                _inventoryService.PromoItemGrantCompleted -= OnPlatformPromoItemGrantCompleted;
            }
            if (_recoverablePlatformService != null)
            {
                _recoverablePlatformService.ConnectionStateChanged -= OnPlatformConnectionStateChanged;
                _recoverablePlatformService.InventoryTrustStateChanged -= OnPlatformInventoryTrustStateChanged;
            }

            _platformService = value;
            _inventoryService = value as IPlatformInventoryService;
            _recoverablePlatformService = value as IRecoverablePlatformService;
            if (_inventoryService != null)
            {
                _inventoryService.InventorySnapshotChanged += OnPlatformInventorySnapshotChanged;
                _inventoryService.PromoItemGrantCompleted += OnPlatformPromoItemGrantCompleted;
            }
            if (_recoverablePlatformService != null)
            {
                _recoverablePlatformService.ConnectionStateChanged += OnPlatformConnectionStateChanged;
                _recoverablePlatformService.InventoryTrustStateChanged += OnPlatformInventoryTrustStateChanged;
            }

            if (IsNodeReady())
            {
#if DEBUG
                ConfigureSteamMockLinkTreeGrants();
#endif
                InitializeLinkTreeInventory();
            }
        }
    }

    private readonly List<Button> _tabs = new();
    private readonly List<Control> _tabContents = new();
    private readonly List<LinkTreeRewardEntry> _linkTreeRewardEntries = new();
    private readonly Dictionary<Button, TabGroup> _filterTabs = new();
    private readonly List<Button> _typeFilterButtons = new();
    private static readonly StringName PanelTopTabStyle = "PanelTopTab";
    private static readonly StringName PanelTopTabSelectedStyle = "PanelTopTabSelected";
    private static readonly StringName CategoryTabStyle = "CategoryTab";
    private static readonly StringName CategoryTabSelectedStyle = "CategoryTabSelected";
    private static readonly string[] LocaleOptions =
    [
        L10n.SystemLocale,
        L10n.EnglishLocale,
        L10n.SimplifiedChineseLocale,
        L10n.TraditionalChineseLocale,
        L10n.JapaneseLocale,
        L10n.KoreanLocale,
        L10n.SpanishSpainLocale,
        L10n.SpanishLatinAmericaLocale,
        L10n.PortugueseBrazilLocale,
        L10n.PortuguesePortugalLocale,
        L10n.FrenchLocale,
        L10n.GermanLocale,
        L10n.DanishLocale,
        L10n.IndonesianLocale,
        L10n.NorwegianLocale,
        L10n.SwedishLocale,
        L10n.DutchLocale,
        L10n.VietnameseLocale,
        L10n.MalayLocale,
    ];
    private static readonly int[] PokerFrameRateOptions = [60, 30, 20, 15];
    private static readonly Color LinkTreeGiftLockedColor = new(0.6039216f, 0.70980394f, 0.7411765f, 1f);
    private static readonly Color LinkTreeGiftReadyColor = new(0f, 0.78039217f, 0.40392157f, 1f);
    private static readonly Color LinkTreeGiftClaimedColor = new(1f, 1f, 1f, 0f);
    private static readonly Color LinkTreeBusyImageModulate = new(0.4f, 0.4f, 0.4f, 1f);
    private const double LinkTreeRecoveryAdviceDelaySeconds = 10.0;
    private static readonly double[] LinkTreeInventoryRecheckDelaysSeconds = [1.0, 2.0, 4.0, 8.0, 8.0];
    private static readonly Vector2 LinkTreeRewardFeedbackStartScale = new(0.62f, 0.62f);
    private static readonly Vector2 LinkTreeRewardFeedbackRestScale = Vector2.One;
    private static readonly Vector2 LinkTreeRewardFeedbackEndScale = new(0.72f, 0.72f);
    private const double LinkTreeRewardFeedbackHoldSeconds = 0.5;
    private static readonly IReadOnlyDictionary<int, Texture2D> TabIconsByGroupId = new Dictionary<int, Texture2D>
    {
        [1001] = GD.Load<Texture2D>("res://Assets/UI/Icon/TabIcon_Dog.svg"),
        [1002] = GD.Load<Texture2D>("res://Assets/UI/Icon/TabIcon_Headwear.svg"),
        [1003] = GD.Load<Texture2D>("res://Assets/UI/Icon/TabIcon_Eyewear.svg"),
        [1004] = GD.Load<Texture2D>("res://Assets/UI/Icon/TabIcon_Player.svg"),
        [1005] = GD.Load<Texture2D>("res://Assets/UI/Icon/TabIcon_Theme.svg"),
        [1006] = GD.Load<Texture2D>("res://Assets/UI/Icon/TabIcon_Refreshment.svg"),
    };
    private const string ArmColorChipPathPrefix = "res://Assets/UI/Icon/Arm_ColorChip_";

    public override void _Ready()
    {
        _panel = GetNode<PanelContainer>("Panel");
        _panelScroll = GetNode<ScrollContainer>("Panel/RootVBox/Scroll");

        _settingsTab = GetNode<Button>("Panel/RootVBox/TitleRow/SettingsTab");
        _wardrobeTab = GetNode<Button>("Panel/RootVBox/TitleRow/WardrobeTab");
        _linkTreeTab = GetNode<Button>("Panel/RootVBox/TitleRow/LinkTreeTab");
        _tabs.Add(_wardrobeTab);
        _tabs.Add(_linkTreeTab);
        _tabs.Add(_settingsTab);

        _settingsContent = GetNode<VBoxContainer>("Panel/RootVBox/Scroll/ContentVBox/SettingsContent");
        _wardrobeContent = GetNode<VBoxContainer>("Panel/RootVBox/Scroll/ContentVBox/WardrobeContent");
        _linkTreeContent = GetNode<VBoxContainer>("Panel/RootVBox/Scroll/ContentVBox/LinkTreeContent");
        _linkTreeStatusCenter = GetNode<Control>("Panel/RootVBox/Scroll/ContentVBox/LinkTreeContent/LinkTreeStatusCenter");
        _linkTreeStatusLabel = GetNode<Label>("Panel/RootVBox/Scroll/ContentVBox/LinkTreeContent/LinkTreeStatusCenter/StatusVBox/LinkTreeStatusLabel");
        _tabContents.Add(_wardrobeContent);
        _tabContents.Add(_linkTreeContent);
        _tabContents.Add(_settingsContent);
        _settingsActionTopGap = GetNode<Control>("Panel/RootVBox/ActionTopGap");
        _settingsActionRow = GetNode<Control>("Panel/RootVBox/SettingsActionRow");
        _settingsActionBottomGap = GetNode<Control>("Panel/RootVBox/ActionBottomGap");
        _settingsActionSep = GetNode<Control>("Panel/RootVBox/ActionSep");

        _wardrobeTab.Pressed += () => SwitchTab(0);
        _linkTreeTab.Pressed += () => SwitchTab(1);
        _settingsTab.Pressed += () => SwitchTab(2);
        BuildLinkTree();
        SetLinkTreePageState(LinkTreePageState.Loading);
        InitializeLinkTreeInventory();
#if DEBUG
        _debugTab = GetNode<Button>("Panel/RootVBox/TitleRow/DebugTab");
        _debugContent = GetNode<VBoxContainer>("Panel/RootVBox/Scroll/ContentVBox/DebugContent");
        _tabs.Add(_debugTab);
        _tabContents.Add(_debugContent);
        _debugTab.Pressed += () => SwitchTab(3);
#else
        GetNode("Panel/RootVBox/TitleRow/DebugTab").Free();
        GetNode("Panel/RootVBox/Scroll/ContentVBox/DebugContent").Free();
        GetNode("ResetSaveConfirm").Free();
#endif
        SwitchTab(0);
        _buildVersionLabel.Text = BuildInfo.DisplayVersion;
        RefreshGlobalInputChipsEarnedLabel();

        // === Settings 页 ===
        _sfxVolumeSlider = GetNode<HSlider>("Panel/RootVBox/Scroll/ContentVBox/SettingsContent/SfxVolumeRow/SfxVolumeSlider");
        _bgmVolumeSlider = GetNode<HSlider>("Panel/RootVBox/Scroll/ContentVBox/SettingsContent/BgmVolumeRow/BgmVolumeSlider");
        _sfxVolumeValueLabel = GetNode<Label>("Panel/RootVBox/Scroll/ContentVBox/SettingsContent/SfxVolumeRow/SfxVolumeValueLabel");
        _bgmVolumeValueLabel = GetNode<Label>("Panel/RootVBox/Scroll/ContentVBox/SettingsContent/BgmVolumeRow/BgmVolumeValueLabel");
        _desktopBgmToggle = GetNode<CheckButton>("Panel/RootVBox/Scroll/ContentVBox/SettingsContent/DesktopBgmRow/DesktopBgmToggle");
        _rightClickQuickModeSwitchToggle = GetNode<CheckButton>("Panel/RootVBox/Scroll/ContentVBox/SettingsContent/RightClickQuickModeSwitchRow/RightClickQuickModeSwitchToggle");
        _preventAccidentalDragToggle = GetNode<CheckButton>("Panel/RootVBox/Scroll/ContentVBox/SettingsContent/PreventAccidentalDragRow/PreventAccidentalDragToggle");
        _languageOption = GetNode<OptionButton>("Panel/RootVBox/Scroll/ContentVBox/SettingsContent/LanguageRow/LanguageOption");
        _displayOption = GetNode<OptionButton>("Panel/RootVBox/Scroll/ContentVBox/SettingsContent/DisplayRow/DisplayOption");
        _armAppearanceOption.GetPopup().AddThemeConstantOverride("icon_max_width", 32);
        var closeBtn = GetNode<Button>("Panel/RootVBox/TitleRow/CloseBtn");
        var quitBtn = GetNode<Button>("Panel/RootVBox/SettingsActionRow/QuitBtn");
        var restartBtn = GetNode<Button>("Panel/RootVBox/SettingsActionRow/RestartBtn");

        BuildLanguageOptions();

        BuildDisplayOptions();
        BuildPokerFrameRateOptions();
        _vsyncToggle.ButtonPressed = SettingsManager.LoadVsyncEnabled();

#if DEBUG
        _saveDataModeOption = GetNode<OptionButton>("Panel/RootVBox/Scroll/ContentVBox/DebugContent/SaveDataModeRow/SaveDataModeOption");
        _saveDataModeOption.AddItem("调试全道具", (int)SettingsManager.SaveDataMode.DebugAllItems);
        _saveDataModeOption.AddItem("本地存档", (int)SettingsManager.SaveDataMode.LocalSave);
        _saveDataModeOption.Select((int)SettingsManager.LoadSaveDataMode());
#endif

        RefreshAudioControlsFromStorage();

        var autoHideToggle = GetNode<CheckButton>("Panel/RootVBox/Scroll/ContentVBox/SettingsContent/AutoHideRow/AutoHideToggle");
        autoHideToggle.ButtonPressed = SettingsManager.LoadAutoHidePanel();
        autoHideToggle.Toggled += enabled => SettingsManager.SaveAutoHidePanel(enabled);

        var tongueImmediateToggle = GetNode<CheckButton>("Panel/RootVBox/Scroll/ContentVBox/SettingsContent/TongueImmediateRow/TongueImmediateToggle");
        tongueImmediateToggle.ButtonPressed = SettingsManager.LoadDesktopTongueImmediateMode();
        tongueImmediateToggle.Toggled += enabled => SettingsManager.SaveDesktopTongueImmediateMode(enabled);

        _blindBoxBubbleToggle = GetNode<CheckButton>("Panel/RootVBox/Scroll/ContentVBox/SettingsContent/BlindBoxBubbleRow/BlindBoxBubbleToggle");
        _blindBoxBubbleToggle.ButtonPressed = SettingsManager.LoadAlwaysShowBlindBoxBubble();
        _blindBoxBubbleToggle.Toggled += OnAlwaysShowBlindBoxBubbleToggled;

        var showFullscreenToggle = GetNode<CheckButton>("Panel/RootVBox/Scroll/ContentVBox/SettingsContent/ShowFullscreenRow/ShowFullscreenToggle");
        showFullscreenToggle.ButtonPressed = SettingsManager.LoadShowOverFullscreenApps();
        showFullscreenToggle.Toggled += enabled => SettingsManager.SaveShowOverFullscreenApps(enabled);

        var enhancedTopmostToggle = GetNode<CheckButton>("Panel/RootVBox/Scroll/ContentVBox/SettingsContent/EnhancedTopmostRow/EnhancedTopmostToggle");
        enhancedTopmostToggle.ButtonPressed = SettingsManager.LoadEnhancedTopmostMode();
        enhancedTopmostToggle.Toggled += enabled => SettingsManager.SaveEnhancedTopmostMode(enabled);

        var proactiveInteractionHintsToggle = GetNode<CheckButton>("Panel/RootVBox/Scroll/ContentVBox/SettingsContent/ProactiveInteractionHintsRow/ProactiveInteractionHintsToggle");
        proactiveInteractionHintsToggle.ButtonPressed = SettingsManager.LoadProactiveInteractionHints();
        proactiveInteractionHintsToggle.Toggled += enabled => SettingsManager.SaveProactiveInteractionHints(enabled);

        _pokerGuideOverlayToggle = GetNode<CheckButton>("Panel/RootVBox/Scroll/ContentVBox/SettingsContent/PokerGuideOverlayRow/PokerGuideOverlayToggle");
        _pokerGuideOverlayToggle.ButtonPressed = SettingsManager.LoadPokerGuideOverlayEnabled();
        _pokerGuideOverlayToggle.Toggled += SettingsManager.SavePokerGuideOverlayEnabled;
        SettingsManager.PokerGuideOverlayEnabledChanged += OnPokerGuideOverlayEnabledChanged;

        var avoidObscuringDogEyesToggle = GetNode<CheckButton>("Panel/RootVBox/Scroll/ContentVBox/SettingsContent/AvoidObscuringDogEyesRow/AvoidObscuringDogEyesToggle");
        avoidObscuringDogEyesToggle.ButtonPressed = SettingsManager.LoadAvoidObscuringDogEyes();
        avoidObscuringDogEyesToggle.Toggled += enabled => SettingsManager.SaveAvoidObscuringDogEyes(enabled);

        _autoEquipToggle = GetNode<CheckButton>("Panel/RootVBox/Scroll/ContentVBox/SettingsContent/AutoEquipRow/AutoEquipToggle");
        _autoEquipToggle.ButtonPressed = SettingsManager.LoadAutoEquipNewOutfits();
        _autoEquipToggle.Toggled += enabled => SettingsManager.SaveAutoEquipNewOutfits(enabled);

        _taskbarSnapToggle = GetNode<CheckButton>("Panel/RootVBox/Scroll/ContentVBox/SettingsContent/TaskbarSnapRow/TaskbarSnapToggle");
        _taskbarSnapToggle.ButtonPressed = SettingsManager.LoadSnapToWindowsTaskbar();
        _taskbarSnapToggle.Toggled += enabled => SettingsManager.SaveSnapToWindowsTaskbar(enabled);

        var streamerSafeToggle = GetNode<CheckButton>("Panel/RootVBox/Scroll/ContentVBox/SettingsContent/StreamerSafeRow/StreamerSafeToggle");
        streamerSafeToggle.ButtonPressed = SettingsManager.LoadStreamerSafeMode();
        streamerSafeToggle.Toggled += enabled =>
        {
            SettingsManager.SaveStreamerSafeMode(enabled);
            L10n.SetSafeMode(enabled);
            RefreshLocalizedOptionText();
        };

        var counterCenterToggle = GetNode<CheckButton>("Panel/RootVBox/Scroll/ContentVBox/SettingsContent/CounterCenterRow/CounterCenterToggle");
        counterCenterToggle.ButtonPressed = SettingsManager.LoadCenterCounterOnTaskbar();
        counterCenterToggle.Toggled += enabled =>
        {
            SettingsManager.SaveCenterCounterOnTaskbar(enabled);
            EmitSignal(SignalName.CounterLayoutChanged);
        };

        var autoHideCounterToggle = GetNode<CheckButton>("Panel/RootVBox/Scroll/ContentVBox/SettingsContent/AutoHideCounterRow/AutoHideCounterToggle");
        autoHideCounterToggle.ButtonPressed = SettingsManager.LoadAutoHideCounter();
        autoHideCounterToggle.Toggled += enabled => SettingsManager.SaveAutoHideCounter(enabled);

        closeBtn.Pressed += Close;
        quitBtn.Pressed += () => EmitSignal(SignalName.QuitRequested);
        restartBtn.Pressed += RestartGame;
        _exportDiagnosticsButton.Pressed += () => ExportDiagnostics();

        _switchToPlayBtn = GetNode<Button>("Panel/RootVBox/SettingsActionRow/SwitchToPlayBtn");
        _switchToBossKeyBtn = GetNode<Button>("Panel/RootVBox/SettingsActionRow/SwitchToBossKeyBtn");
        _switchToPlayBtn.Pressed += () => EmitSignal(SignalName.SwitchToPlayRequested);
        _switchToBossKeyBtn.Pressed += () => EmitSignal(SignalName.SwitchToBossKeyRequested);
        RefreshModeButtonText();

        _sfxVolumeSlider.ValueChanged += OnSfxVolumeChanged;
        _bgmVolumeSlider.ValueChanged += OnBgmVolumeChanged;
        _desktopBgmToggle.Toggled += OnDesktopBgmToggled;
        _rightClickQuickModeSwitchToggle.ButtonPressed = SettingsManager.LoadRightClickQuickModeSwitch();
        _rightClickQuickModeSwitchToggle.Toggled += enabled => SettingsManager.SaveRightClickQuickModeSwitch(enabled);
        _preventAccidentalDragToggle.ButtonPressed = SettingsManager.LoadPreventAccidentalDrag();
        _preventAccidentalDragToggle.Toggled += enabled => SettingsManager.SavePreventAccidentalDrag(enabled);
        _languageOption.ItemSelected += OnLanguageSelected;
        _displayOption.ItemSelected += OnDisplayModeChanged;
        _pokerFrameRateOption.ItemSelected += OnPokerFrameRateSelected;
        _vsyncToggle.Toggled += SettingsManager.SaveVsyncEnabled;
        _armAppearanceOption.ItemSelected += OnArmAppearanceSelected;
#if DEBUG
        _saveDataModeOption.ItemSelected += OnSaveDataModeChanged;
#endif
        L10n.Changed += RefreshLocalizedOptionText;

#if DEBUG
        // === Debug 页 ===
        _seedLabel = GetNode<Label>("Panel/RootVBox/Scroll/ContentVBox/DebugContent/SeedRow/SeedLabel");
        _playTimeLabel = GetNode<Label>("Panel/RootVBox/Scroll/ContentVBox/DebugContent/PlayTimeLabel");
        _luckyDealBuffLabel = GetNode<Label>("Panel/RootVBox/Scroll/ContentVBox/DebugContent/LuckyDealBuffLabel");
        _blindBoxDebugToggle = GetNode<Button>("Panel/RootVBox/Scroll/ContentVBox/DebugContent/BlindBoxDebugToggle");
        _blindBoxDebugContent = GetNode<Control>("Panel/RootVBox/Scroll/ContentVBox/DebugContent/BlindBoxDebugContent");
        _blindBoxDebugLabel = GetNode<Label>("Panel/RootVBox/Scroll/ContentVBox/DebugContent/BlindBoxDebugContent/BlindBoxDebugLabel");
        _blindBoxPaymentSourceLabelsToggle = GetNode<CheckButton>("Panel/RootVBox/Scroll/ContentVBox/DebugContent/BlindBoxDebugContent/BlindBoxPaymentSourceLabelsRow/BlindBoxPaymentSourceLabelsToggle");
        _blindBoxLocalTestModeToggle = GetNode<CheckButton>("Panel/RootVBox/Scroll/ContentVBox/DebugContent/BlindBoxDebugContent/BlindBoxLocalTestModeRow/BlindBoxLocalTestModeToggle");
        _blindBoxLocalTestPreparedRewardCount = GetNode<Label>("Panel/RootVBox/Scroll/ContentVBox/DebugContent/BlindBoxDebugContent/BlindBoxLocalTestPreparedRewardRow/BlindBoxLocalTestPreparedRewardCount");
        _blindBoxLocalTestPreparedRewardDecrease = GetNode<Button>("Panel/RootVBox/Scroll/ContentVBox/DebugContent/BlindBoxDebugContent/BlindBoxLocalTestPreparedRewardRow/BlindBoxLocalTestPreparedRewardDecrease");
        _blindBoxLocalTestPreparedRewardIncrease = GetNode<Button>("Panel/RootVBox/Scroll/ContentVBox/DebugContent/BlindBoxDebugContent/BlindBoxLocalTestPreparedRewardRow/BlindBoxLocalTestPreparedRewardIncrease");
        _blindBoxLocalTestAdvance = GetNode<Button>("Panel/RootVBox/Scroll/ContentVBox/DebugContent/BlindBoxDebugContent/BlindBoxLocalTestActionRow/BlindBoxLocalTestAdvance");
        _blindBoxLocalTestClear = GetNode<Button>("Panel/RootVBox/Scroll/ContentVBox/DebugContent/BlindBoxDebugContent/BlindBoxLocalTestActionRow/BlindBoxLocalTestClear");
        _playerProgressDebugLabel = GetNode<Label>("Panel/RootVBox/Scroll/ContentVBox/DebugContent/PlayerProgressDebugLabel");
        _playerProgressMultiplierOption = GetNode<OptionButton>("Panel/RootVBox/Scroll/ContentVBox/DebugContent/PlayerProgressMultiplierRow/PlayerProgressMultiplierOption");
        var seedCopyBtn = GetNode<Button>("Panel/RootVBox/Scroll/ContentVBox/DebugContent/SeedRow/SeedCopyBtn");
        _seedInput = GetNode<LineEdit>("Panel/RootVBox/Scroll/ContentVBox/DebugContent/SeedInput");
        var grantChipsBtn = GetNode<Button>("Panel/RootVBox/Scroll/ContentVBox/DebugContent/GrantChipsBtn");
        var grantLuckyDealsBtn = GetNode<Button>("Panel/RootVBox/Scroll/ContentVBox/DebugContent/GrantLuckyDealsBtn");
        var steamMockPanelToggle = GetNode<CheckButton>("Panel/RootVBox/Scroll/ContentVBox/DebugContent/LinkTreeSyncSimulationRow/LinkTreeSyncSimulationToggle");
        _showDeveloperLauncherNextStartupToggle = GetNode<CheckButton>("Panel/RootVBox/Scroll/ContentVBox/DebugContent/DeveloperLauncherRow/DeveloperLauncherToggle");
        var resetSettingsBtn = GetNode<Button>("Panel/RootVBox/Scroll/ContentVBox/DebugContent/ResetSettingsBtn");
        var markPokerBeginnerBtn = GetNode<Button>("Panel/RootVBox/Scroll/ContentVBox/DebugContent/MarkPokerBeginnerBtn");
        var resetSaveBtn = GetNode<Button>("Panel/RootVBox/Scroll/ContentVBox/DebugContent/ResetSaveBtn");
        var resetPlayerProgressBtn = GetNode<Button>("Panel/RootVBox/Scroll/ContentVBox/DebugContent/ResetPlayerProgressBtn");
        var randomizeSceneBtn = GetNode<Button>("Panel/RootVBox/Scroll/ContentVBox/DebugContent/RandomizeSceneBtn");
        var randomizeDogBtn = GetNode<Button>("Panel/RootVBox/Scroll/ContentVBox/DebugContent/RandomizeDogBtn");
        var randomAcquireItemBtn = GetNode<Button>("Panel/RootVBox/Scroll/ContentVBox/DebugContent/RandomAcquireItemBtn");
        var hideDebugTabBtn = GetNode<Button>("Panel/RootVBox/Scroll/ContentVBox/DebugContent/HideDebugTabBtn");
        _reactionOption = GetNode<OptionButton>("Panel/RootVBox/Scroll/ContentVBox/DebugContent/ReactionRow/ReactionOption");
        var playReactionBtn = GetNode<Button>("Panel/RootVBox/Scroll/ContentVBox/DebugContent/ReactionRow/PlayReactionBtn");

        seedCopyBtn.Pressed += () => DisplayServer.ClipboardSet(_currentSeed.ToString());
        grantChipsBtn.Pressed += () => EmitSignal(SignalName.DebugGrantChipsRequested);
        grantLuckyDealsBtn.Pressed += () =>
        {
            EmitSignal(SignalName.DebugGrantLuckyDealsRequested);
            RefreshDebugPlayTime();
        };
        steamMockPanelToggle.SetPressedNoSignal(false);
        steamMockPanelToggle.Toggled += visible =>
            EmitSignal(SignalName.SteamMockPanelVisibilityChanged, visible);
        _showDeveloperLauncherNextStartupToggle.ButtonPressed = SettingsManager.LoadShowDeveloperLauncherOnStartup();
        _showDeveloperLauncherNextStartupToggle.Toggled += SettingsManager.SaveShowDeveloperLauncherOnStartup;
        _disableGlobalMouseListeningToggle.SetPressedNoSignal(false);
        _disableGlobalMouseListeningToggle.Toggled += disabled =>
            EmitSignal(SignalName.GlobalMouseListeningDisabledChanged, disabled);
        resetSettingsBtn.Pressed += ResetSettingsToDefaults;
        markPokerBeginnerBtn.Pressed += () => _gameData.MarkPokerBasicsGuidanceNeededForDebug();
        _resetSaveConfirm = GetNode<ConfirmOverlayController>("ResetSaveConfirm");
        resetSaveBtn.Pressed += ConfirmResetSave;
        _resetSaveConfirm.Confirmed += OnResetConfirmed;
        _playerProgressMultiplierOption.AddItem("统计 x1", 1);
        _playerProgressMultiplierOption.AddItem("统计 x10", 10);
        _playerProgressMultiplierOption.AddItem("统计 x100", 100);
        _playerProgressMultiplierOption.AddItem("统计 x1000", 1000);
        _playerProgressMultiplierOption.Select(0);
        _playerProgressMultiplierOption.ItemSelected += _ =>
            _gameData.SetPlayerProgressDebugMultiplier(_playerProgressMultiplierOption.GetSelectedId());
        resetPlayerProgressBtn.Pressed += ConfirmResetPlayerProgress;
        _blindBoxDebugToggle.Pressed += ToggleBlindBoxDebug;
        RefreshBlindBoxLocalTestControls();
        _blindBoxLocalTestModeToggle.Toggled += enabled =>
        {
            if (_gameData == null)
                return;

            if (!_gameData.SetBlindBoxLocalTestMode(enabled))
                _blindBoxLocalTestModeToggle.SetPressedNoSignal(_gameData.IsBlindBoxLocalTestMode);
            RefreshBlindBoxLocalTestControls();
            RefreshBlindBoxDebugStatus();
        };
        _blindBoxLocalTestPreparedRewardDecrease.Pressed += () =>
        {
            _gameData?.AdjustBlindBoxLocalTestPreparedRewardCount(-1);
            RefreshBlindBoxLocalTestControls();
            RefreshBlindBoxDebugStatus();
        };
        _blindBoxLocalTestPreparedRewardIncrease.Pressed += () =>
        {
            _gameData?.AdjustBlindBoxLocalTestPreparedRewardCount(1);
            RefreshBlindBoxLocalTestControls();
            RefreshBlindBoxDebugStatus();
        };
        _blindBoxLocalTestAdvance.Pressed += () =>
        {
            _gameData?.AdvanceBlindBoxLocalTestPresentation();
            RefreshBlindBoxDebugStatus();
        };
        _blindBoxLocalTestClear.Pressed += () =>
        {
            _gameData?.ClearBlindBoxLocalTestPresentation();
            RefreshBlindBoxDebugStatus();
        };
        BalloonHintController.ShowPaymentSourceLabels = false;
        _blindBoxPaymentSourceLabelsToggle.SetPressedNoSignal(false);
        _blindBoxPaymentSourceLabelsToggle.Toggled += enabled =>
        {
            BalloonHintController.ShowPaymentSourceLabels = enabled;
            EmitSignal(SignalName.BlindBoxBubbleVisibilityChanged);
        };
        randomizeSceneBtn.Pressed += () => EmitSignal(SignalName.RandomizeRequested);
        randomizeDogBtn.Pressed += () => EmitSignal(SignalName.RandomizeDogRequested);
        randomAcquireItemBtn.Pressed += () => EmitSignal(SignalName.RandomAcquireItemRequested);
        hideDebugTabBtn.Pressed += HideDebugTabForSession;
        BuildReactionOptions();
        playReactionBtn.Pressed += () =>
            EmitSignal(SignalName.DogReactionRequested, _reactionOption.GetSelectedId());
#endif

        // === Wardrobe 页 ===
        _wardrobeGrid = GetNode<GridContainer>("Panel/RootVBox/Scroll/ContentVBox/WardrobeContent/WardrobeScroll/WardrobeGrid");
        _typeFilterRow = GetNode<HBoxContainer>("Panel/RootVBox/Scroll/ContentVBox/WardrobeContent/TypeFilterRow");
        _emptyWardrobeCenter = GetNode<Control>("Panel/RootVBox/Scroll/ContentVBox/WardrobeContent/WardrobeScroll/EmptyWardrobeCenter");
        _emptyWardrobeLabel = GetNode<Label>("Panel/RootVBox/Scroll/ContentVBox/WardrobeContent/WardrobeScroll/EmptyWardrobeCenter/EmptyWardrobeLabel");

        _panel.Visible = false;
#if DEBUG
        RefreshDebugPlayTime();
#endif
    }

#if DEBUG
    public void SetSteamMockPanelToggle(bool visible)
    {
        if (!IsNodeReady())
            return;
        var toggle = GetNode<CheckButton>("Panel/RootVBox/Scroll/ContentVBox/DebugContent/LinkTreeSyncSimulationRow/LinkTreeSyncSimulationToggle");
        toggle.SetPressedNoSignal(visible);
    }

    public void SetSteamMockActive(bool active)
    {
        _steamMockActive = active;
        _saveDataModeOption.Disabled = active;
        _saveDataModeOption.Select(active
            ? (int)SettingsManager.SaveDataMode.LocalSave
            : (int)SettingsManager.LoadSaveDataMode());
        foreach (var entry in _linkTreeRewardEntries)
            entry.DebugIdLabel.Visible = active;
        if (active)
            ResetSteamMockLinkTreeState();
        RefreshLinkTreePagePresentation();
        RefreshBlindBoxLocalTestControls();
        RefreshBlindBoxDebugStatus();
    }

    public void ResetSteamMockLinkTreeState()
    {
        if (!IsNodeReady())
            return;

        foreach (var entry in _linkTreeRewardEntries)
        {
            entry.RewardFeedbackTween?.Kill();
            entry.RewardFeedbackPending = false;
            entry.RewardFeedbackPlaying = false;
            entry.ClaimPending = false;
            entry.State = LinkTreeRewardState.Unopened;
            entry.RewardVisualRoot.Visible = false;
            RefreshLinkTreeRewardEntry(entry);
        }
        _refreshLinkTreeSelectionOnNextInventorySnapshot = true;
        RefreshLinkTreeVisibleEntries();
        RefreshLinkTreePageFromPlatformState();
    }

    private void ConfigureSteamMockLinkTreeGrants()
    {
        if (_platformService is not IDebugSteamMockController controller || _linkTreeRewardEntries.Count == 0)
            return;

        var grants = _linkTreeRewardEntries.Select(entry =>
        {
            var rewardItemDefId = entry.Data.RewardType == ELinkTreeRewardType.FixedItem
                ? LubanData.Tables.TbItem.GetOrDefault(entry.Data.RewardItemId)?.SteamItemDefId ?? 0
                : 0;
            return new DebugSteamLinkTreeGrant(
                entry.Data.SteamClaimBundleItemDefId,
                entry.Data.SteamReceiptItemDefId,
                rewardItemDefId);
        }).ToArray();
        controller.ConfigureLinkTreeGrants(grants);
    }
#endif

    public override void _ExitTree()
    {
        SettingsManager.PokerGuideOverlayEnabledChanged -= OnPokerGuideOverlayEnabledChanged;
    }

    public override void _Process(double delta)
    {
        if (_panelScrollDragPotential && !Input.IsMouseButtonPressed(MouseButton.Left))
            ResetPanelScrollDrag();

        ProcessLinkTreeInventoryRecheck(delta);

        if (_linkTreePageState == LinkTreePageState.Loading && !_linkTreeShowRecoveryAdvice)
        {
            _linkTreeLoadingElapsedSeconds += delta;
            if (_linkTreeLoadingElapsedSeconds >= LinkTreeRecoveryAdviceDelaySeconds
                && _recoverablePlatformService?.ConnectionState == PlatformConnectionState.Unavailable)
            {
                _linkTreeShowRecoveryAdvice = true;
                if (_linkTreeContent.Visible)
                    RefreshLinkTreePagePresentation();
            }
        }

#if DEBUG
        if (_gameData == null || !_debugContent.Visible)
            return;

        _debugTimeRefreshTimer -= delta;
        if (_debugTimeRefreshTimer > 0)
            return;

        _debugTimeRefreshTimer = 1.0;
        RefreshDebugPlayTime();
#endif
    }

    public override void _Input(InputEvent @event)
    {
        if (!_panel.Visible)
        {
            ResetPanelScrollDrag();
            return;
        }

        if (@event is InputEventMouseButton { ButtonIndex: MouseButton.Left } mouseButton)
        {
            if (mouseButton.Pressed)
                BeginPanelScrollDrag(mouseButton.Position);
            else
                EndPanelScrollDrag();
            return;
        }

        if (@event is not InputEventMouseMotion mouseMotion || !_panelScrollDragPotential)
            return;

        var delta = mouseMotion.Position - _panelScrollDragStartPosition;
        if (!_panelScrollDragging)
        {
            if (Mathf.Abs(delta.Y) < PanelScrollDragThreshold
                || Mathf.Abs(delta.Y) <= Mathf.Abs(delta.X))
            {
                return;
            }

            _panelScrollDragging = true;
            if (_panelScrollPressedButton != null)
                _panelScrollPressedButton.Disabled = true;
        }

        _panelScroll.ScrollVertical = _panelScrollDragStartValue - Mathf.RoundToInt(delta.Y);
        GetViewport().SetInputAsHandled();
    }

    private void BeginPanelScrollDrag(Vector2 mousePosition)
    {
        if (!_panelScroll.GetGlobalRect().HasPoint(mousePosition))
            return;

        var verticalScrollBar = _panelScroll.GetVScrollBar();
        if (verticalScrollBar.MaxValue <= verticalScrollBar.Page)
            return;

        var hoveredControl = GetViewport().GuiGetHoveredControl();
        if (FindControlAncestor<Godot.Range>(hoveredControl, _panelScroll) != null
            || FindControlAncestor<LineEdit>(hoveredControl, _panelScroll) != null)
        {
            return;
        }

        _panelScrollDragPotential = true;
        _panelScrollDragging = false;
        _panelScrollDragStartPosition = mousePosition;
        _panelScrollDragStartValue = _panelScroll.ScrollVertical;
        _panelScrollPressedButton = FindControlAncestor<BaseButton>(hoveredControl, _panelScroll);
        if (_panelScrollPressedButton?.Disabled == true)
            _panelScrollPressedButton = null;
    }

    private void EndPanelScrollDrag()
    {
        if (!_panelScrollDragPotential)
            return;

        if (_panelScrollDragging)
            GetViewport().SetInputAsHandled();
        ResetPanelScrollDrag();
    }

    private void ResetPanelScrollDrag()
    {
        if (_panelScrollPressedButton != null)
            _panelScrollPressedButton.Disabled = false;

        _panelScrollPressedButton = null;
        _panelScrollDragPotential = false;
        _panelScrollDragging = false;
    }

    private static T FindControlAncestor<T>(Control control, Control boundary) where T : Control
    {
        for (var current = control; current != null && current != boundary; current = current.GetParent() as Control)
        {
            if (current is T match)
                return match;
        }

        return null;
    }

    private void SwitchTab(int index)
    {
        for (int i = 0; i < _tabs.Count; i++)
        {
            _tabContents[i].Visible = i == index;
            _tabs[i].ThemeTypeVariation = i == index ? PanelTopTabSelectedStyle : PanelTopTabStyle;
        }
        _settingsActionTopGap.Visible = index == 2;
        _settingsActionRow.Visible = index == 2;
        _settingsActionBottomGap.Visible = index == 2;
        _settingsActionSep.Visible = index == 2;
        if (index == 0 && _gameData != null)
            BuildWardrobe();
        if (index == 1)
        {
            RefreshLinkTreeVisibleEntries();
            _refreshLinkTreeSelectionOnNextInventorySnapshot = true;
            RefreshLinkTreePageFromPlatformState();
            _recoverablePlatformService?.RequestReconnect();
            TryPlayPendingLinkTreeRewardFeedbacks();
        }
        #if DEBUG
        if (index == 3)
            RefreshDebugPlayTime();
        #endif
    }

    private void EnsureCurrentTabReady()
    {
        if (_gameData == null)
            return;

        if (_wardrobeContent?.Visible == true)
            BuildWardrobe();
#if DEBUG
        if (_debugContent?.Visible == true)
            RefreshDebugPlayTime();
#endif
    }

    private void OnChipsChangedForGlobalInputLabel(int _)
    {
        // GlobalInputTracker records the progress statistic immediately after changing
        // the chip balance, so refresh after the current call stack has completed.
        Callable.From(RefreshGlobalInputChipsEarnedLabel).CallDeferred();
    }

    private void OnSharedInventoryTransactionStateChanged()
    {
        if (_linkTreeContent?.Visible == true)
            RefreshLinkTreePageFromPlatformState();
    }

    private void RefreshGlobalInputChipsEarnedLabel()
    {
        if (_globalInputChipsEarnedLabel == null || _gameData == null)
            return;

        _globalInputChipsEarnedLabel.Text = _gameData.GlobalInputChipsEarned.ToString("N0", CultureInfo.InvariantCulture);
    }

    private enum LinkTreeRewardState
    {
        Unopened,
        OpenedAwaitingReturn,
        ReadyToClaim,
        Claimed,
    }

    private enum LinkTreePageState
    {
        Loading,
        Ready,
        Unavailable,
    }

    private sealed class LinkTreeRewardEntry
    {
        public Button Banner = null!;
        public TextureRect BannerImage = null!;
        public TextureRect GiftBadge = null!;
        public LoadingIndicatorController LoadingIndicator = null!;
        public Control RewardVisualRoot = null!;
        public TextureRect RewardCellShadow = null!;
        public ItemCellController RewardCell = null!;
        public Label RewardAmountLabel = null!;
        public Label DebugIdLabel = null!;
        public LinkTree Data = null!;
        public LinkTreeRewardState State;
        public bool SelectedForDisplay;
        public bool ClaimPending;
        public bool RewardFeedbackPending;
        public bool RewardFeedbackPlaying;
        public Tween RewardFeedbackTween = null!;
    }

    private void BuildLinkTree()
    {
        foreach (var entry in _linkTreeRewardEntries)
            entry.RewardFeedbackTween?.Kill();

        foreach (var child in _linkTreeContent.GetChildren())
        {
            if (child.Name != "LinkTreeTopGap" && child.Name != "LinkTreeStatusCenter")
                child.QueueFree();
        }

        _linkTreeRewardEntries.Clear();

        var entries = LubanData.Tables.TbLinkTree.DataList
            .Where(entry => entry.IsEnabled)
            .OrderBy(entry => entry.SortOrder)
            .ThenBy(entry => entry.Id);

        foreach (var data in entries)
            AddLinkTreeBanner(data);

#if DEBUG
        ConfigureSteamMockLinkTreeGrants();
#endif
        RefreshLinkTreeVisibleEntries();
    }

    private void AddLinkTreeBanner(LinkTree data)
    {
        var banner = _linkTreeBannerScene.Instantiate<Button>();
        banner.Name = string.IsNullOrWhiteSpace(data.Key) ? $"LinkTree_{data.Id}" : data.Key;
        banner.TooltipText = ResolveLinkTreeTooltip(data);
        ApplyLinkTreeTexture(banner.GetNode<TextureRect>("BannerImage"), data.BannerTexturePath);
        ApplyLinkTreeTexture(banner.GetNode<TextureRect>("GiftBadge"), data.BadgeTexturePath);
        _linkTreeContent.AddChild(banner);

        var entry = new LinkTreeRewardEntry
        {
            Banner = banner,
            BannerImage = banner.GetNode<TextureRect>("BannerImage"),
            GiftBadge = banner.GetNode<TextureRect>("GiftBadge"),
            LoadingIndicator = banner.GetNode<LoadingIndicatorController>("LoadingIndicator"),
            RewardVisualRoot = banner.GetNode<Control>("RewardVisualRoot"),
            RewardCellShadow = banner.GetNode<TextureRect>("RewardVisualRoot/RewardCellShadow"),
            RewardCell = banner.GetNode<ItemCellController>("RewardVisualRoot/RewardCell"),
            RewardAmountLabel = banner.GetNode<Label>("RewardVisualRoot/RewardAmountLabel"),
            DebugIdLabel = banner.GetNode<Label>("DebugIdLabel"),
            Data = data,
            State = LinkTreeRewardState.Unopened,
        };
        entry.DebugIdLabel.Text = data.Id.ToString();
#if DEBUG
        entry.DebugIdLabel.Visible = _steamMockActive;
#endif
        _linkTreeRewardEntries.Add(entry);
        banner.Visible = false;
        banner.Pressed += () => OnLinkTreeBannerPressed(entry);
        RefreshLinkTreeRewardEntry(entry);
    }

    private void OnLinkTreeBannerPressed(LinkTreeRewardEntry entry)
    {
        if (entry.State == LinkTreeRewardState.Claimed)
        {
            OpenLinkTreeUrl(entry.Data.Key, entry.Data.PostClaimUrl);
            return;
        }

        if (entry.State == LinkTreeRewardState.ReadyToClaim)
        {
            ClaimLinkTreeReward(entry);
            return;
        }

        var result = OpenLinkTreeUrl(entry.Data.Key, entry.Data.PreClaimUrl);
        if (IsLinkTreeOpenCheckPassed(entry.Data.OpenCheckType, result))
        {
            if (entry.State == LinkTreeRewardState.Unopened)
            {
                entry.State = LinkTreeRewardState.OpenedAwaitingReturn;
                RefreshLinkTreeRewardEntry(entry);
            }
        }
    }

    public void OnGlobalMousePressed(Vector2I screenPosition, bool clickedOutsideInteractiveContent)
    {
        if (!clickedOutsideInteractiveContent || !HasLinkTreeRewardsAwaitingExternalClick())
            return;

        MarkOpenedLinkTreeRewardsReadyToClaim();
    }

    private bool HasLinkTreeRewardsAwaitingExternalClick()
    {
        return _linkTreeRewardEntries
            .Any(entry => entry.State == LinkTreeRewardState.OpenedAwaitingReturn);
    }

    private void MarkOpenedLinkTreeRewardsReadyToClaim()
    {
        foreach (var entry in _linkTreeRewardEntries)
        {
            if (entry.State != LinkTreeRewardState.OpenedAwaitingReturn)
                continue;

            entry.State = LinkTreeRewardState.ReadyToClaim;
            RefreshLinkTreeRewardEntry(entry);
        }
    }

    private static bool IsLinkTreeOpenCheckPassed(ELinkTreeOpenCheckType checkType, Error shellOpenResult)
    {
        return checkType switch
        {
            ELinkTreeOpenCheckType.None => true,
            ELinkTreeOpenCheckType.ShellOpenOk => shellOpenResult == Error.Ok,
            ELinkTreeOpenCheckType.SteamClientOk => shellOpenResult == Error.Ok,
            ELinkTreeOpenCheckType.BrowserProcessOk => shellOpenResult == Error.Ok,
            _ => false,
        };
    }

    private static Error OpenLinkTreeUrl(string bannerId, string url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            GD.Print($"[LinkTree] {bannerId} clicked, but no URL has been configured yet.");
            return Error.Unconfigured;
        }

        var result = OS.ShellOpen(url);
        if (result == Error.Ok)
            GD.Print($"[LinkTree] Opened {bannerId}: {url}");
        else
            GD.PushWarning($"[LinkTree] Failed to open {bannerId}: {url} ({result}).");

        return result;
    }

    private void ClaimLinkTreeReward(LinkTreeRewardEntry entry)
    {
        if (entry.ClaimPending)
            return;

        if (entry.State == LinkTreeRewardState.Claimed)
        {
            return;
        }

        if (_inventoryService != null)
        {
            entry.ClaimPending = true;
            RefreshLinkTreeInteractionPresentation();
            ClaimSteamLinkTreeReward(entry);
            return;
        }

        GD.PushWarning($"[LinkTree] Steam Inventory unavailable; refusing reward claim for {entry.Data.Key}.");
    }

    private void InitializeLinkTreeInventory()
    {
        if (!IsNodeReady())
            return;
        if (_platformService == null)
        {
            SetLinkTreePageState(LinkTreePageState.Loading);
            return;
        }

        if (_inventoryService == null)
        {
            SetLinkTreePageState(LinkTreePageState.Unavailable);
            return;
        }

        SetLinkTreePageState(LinkTreePageState.Loading);
        _inventoryService.StartInventorySynchronization();
    }

    private void SetLinkTreePageState(LinkTreePageState state)
    {
        if (!IsNodeReady())
            return;

        if (_linkTreePageState != state)
        {
            _linkTreeLoadingElapsedSeconds = 0.0;
            _linkTreeShowRecoveryAdvice = false;
        }
        _linkTreePageState = state;
        RefreshLinkTreePagePresentation();
    }

    private void RefreshLinkTreePagePresentation()
    {
        var state = _linkTreePageState;
        var showBanners = state == LinkTreePageState.Ready;
        var unavailableText = L10n.Tr(L10nKey.LinkTree_RewardsUnavailable);
        var syncingText = GetLinkTreeSyncStatusText(_linkTreeShowRecoveryAdvice);
        _linkTreeStatusCenter.Visible = !showBanners;
        _linkTreeStatusLabel.Text = state switch
        {
            LinkTreePageState.Loading => syncingText,
            LinkTreePageState.Unavailable => unavailableText,
            _ => string.Empty,
        };
        foreach (var entry in _linkTreeRewardEntries)
        {
            RefreshLinkTreeRewardEntry(entry);
            entry.Banner.Visible = showBanners && entry.SelectedForDisplay;
        }

        TryPlayPendingLinkTreeRewardFeedbacks();
    }

    private static string GetLinkTreeSyncStatusText(bool includeRecoveryAdvice)
    {
        var text = L10n.Tr(L10nKey.LinkTree_SyncingRewards);
        if (includeRecoveryAdvice)
            return text;

        var paragraphBreak = text.IndexOf("\n\n", StringComparison.Ordinal);
        return paragraphBreak >= 0 ? text[..paragraphBreak] : text;
    }

    private bool IsSharedInventoryRevalidationPending()
    {
        if (_inventoryService == null || _recoverablePlatformService == null)
            return false;

        return _recoverablePlatformService.ConnectionState != PlatformConnectionState.Ready
               || _recoverablePlatformService.InventoryTrustState != PlatformInventoryTrustState.Trusted;
    }

    private bool IsSharedInventoryWritePending()
    {
        return _linkTreeRewardEntries.Any(entry => entry.ClaimPending)
               || _gameData?.ActiveBlindBoxPreparationPending == true
               || _gameData?.PendingLinkTreeClaim != null
               || _inventoryService?.IsPromoGrantPending == true
               || _inventoryService?.IsPlaytimeDropPending == true;
    }

    private void RefreshLinkTreePageFromPlatformState()
    {
        if (_inventoryService == null || _recoverablePlatformService == null)
        {
            SetLinkTreePageState(LinkTreePageState.Unavailable);
            return;
        }

        SetLinkTreePageState(IsSharedInventoryRevalidationPending()
            ? LinkTreePageState.Loading
            : LinkTreePageState.Ready);
    }

    private void RefreshLinkTreeVisibleEntries()
    {
        if (_linkTreeRewardEntries.Count == 0)
            return;

        var configuredCount = LubanData.Tables.TbGameDevelopConfig.DataList
            .FirstOrDefault()?.LinkTreeVisibleBannerCount ?? 0;
        var visibleCount = configuredCount > 0 ? configuredCount : _linkTreeRewardEntries.Count;
        if (configuredCount <= 0)
            GD.PushWarning("[LinkTree] LinkTreeVisibleBannerCount must be positive; showing all enabled banners.");

        var selected = new HashSet<LinkTreeRewardEntry>();
        foreach (var entry in _linkTreeRewardEntries.Where(entry => entry.Data.IsPinned))
            selected.Add(entry);

        if (selected.Count > visibleCount)
        {
            GD.PushWarning(
                $"[LinkTree] {selected.Count} pinned banners exceed LinkTreeVisibleBannerCount={visibleCount}; " +
                "showing all pinned banners.");
        }

        AddLinkTreeEntriesUntilFull(
            selected,
            _linkTreeRewardEntries.Where(entry =>
                !entry.Data.IsPinned && (entry.RewardFeedbackPending || entry.RewardFeedbackPlaying)),
            visibleCount);
        AddLinkTreeEntriesUntilFull(
            selected,
            _linkTreeRewardEntries.Where(entry => !entry.Data.IsPinned && entry.State != LinkTreeRewardState.Claimed),
            visibleCount);
        AddLinkTreeEntriesUntilFull(
            selected,
            _linkTreeRewardEntries.Where(entry => !entry.Data.IsPinned && entry.State == LinkTreeRewardState.Claimed),
            visibleCount);

        foreach (var entry in _linkTreeRewardEntries)
            entry.SelectedForDisplay = selected.Contains(entry);

        RefreshLinkTreePagePresentation();
    }

    private static void AddLinkTreeEntriesUntilFull(
        HashSet<LinkTreeRewardEntry> selected,
        IEnumerable<LinkTreeRewardEntry> candidates,
        int visibleCount)
    {
        foreach (var entry in candidates)
        {
            if (selected.Count >= visibleCount)
                return;

            selected.Add(entry);
        }
    }

    private void ClaimSteamLinkTreeReward(LinkTreeRewardEntry entry)
    {
        var bundleItemDefId = entry.Data.SteamClaimBundleItemDefId;
        var receiptItemDefId = entry.Data.SteamReceiptItemDefId;
        var bundleItemDef = LubanData.Tables.TbSteamItemDef.GetOrDefault(bundleItemDefId);
        var receiptItemDef = LubanData.Tables.TbSteamItemDef.GetOrDefault(receiptItemDefId);
        if (bundleItemDefId <= 0 || receiptItemDefId <= 0
            || bundleItemDef is not { IsEnabled: true, Type: ESteamItemDefType.Bundle }
            || receiptItemDef is not { IsEnabled: true, Type: ESteamItemDefType.Item })
        {
            entry.ClaimPending = false;
            RefreshLinkTreeInteractionPresentation();
            GD.PushWarning(
                $"[LinkTree] Invalid Steam Bundle/receipt for {entry.Data.Key}: " +
                $"Bundle={bundleItemDefId}, Receipt={receiptItemDefId}.");
            return;
        }
        if (!_inventoryService!.IsInventoryReady)
        {
            _recoverablePlatformService?.RequestReconnect();
            GD.Print($"[LinkTree] Steam inventory is not ready; waiting to claim {entry.Data.Key}.");
            return;
        }
        if (_gameData?.IsUsingLocalSave != true)
        {
            entry.ClaimPending = false;
            RefreshLinkTreeInteractionPresentation();
            GD.PushWarning("[LinkTree] Steam-backed rewards require LocalSave mode so the claim transaction can be recovered.");
            return;
        }
        if (_gameData == null || !_gameData.TryBeginLinkTreeClaim(
                entry.Data.Id,
                bundleItemDefId,
                receiptItemDefId))
        {
            entry.ClaimPending = false;
            RefreshLinkTreeInteractionPresentation();
            GD.PushWarning($"[LinkTree] Shared Steam inventory transaction is busy; refusing {entry.Data.Key}.");
            return;
        }
        if (!_inventoryService.TryGrantPromoItem(bundleItemDefId, receiptItemDefId, out var message))
        {
            _gameData.ClearPendingLinkTreeClaim();
            entry.ClaimPending = false;
            ResetLinkTreeInventoryRecheck();
            RefreshLinkTreeInteractionPresentation();
            GD.PushWarning($"[LinkTree] {message}");
            return;
        }

        ResetLinkTreeInventoryRecheck();
        GD.Print($"[LinkTree] {message}");
    }

    private void OnPlatformInventorySnapshotChanged(PlatformInventorySnapshot snapshot)
    {
        DiagnosticLog.Record("platform_inventory_snapshot", new Dictionary<string, object>
        {
            ["succeeded"] = snapshot.Succeeded,
            ["itemTypeCount"] = snapshot.OwnedItemDefIds.Count,
            ["itemStackCount"] = snapshot.Items.Count,
            ["pendingLinkTreeClaim"] = _gameData?.PendingLinkTreeClaim != null,
        });
        if (!snapshot.Succeeded)
        {
            RefreshLinkTreePageFromPlatformState();
            GD.PushWarning($"[SteamInventory] {snapshot.Message}");
            return;
        }

        if (_gameData?.LinkTreeRewardLedgerInitialized == false)
        {
            var pendingLinkTreeId = _gameData.PendingLinkTreeClaim?.LinkTreeId;
            var baselineIds = _linkTreeRewardEntries
                .Where(entry => snapshot.OwnedItemDefIds.Contains(entry.Data.SteamReceiptItemDefId))
                // A persisted in-flight claim is not historical. If its receipt appeared
                // while the game was closed, the recovery block below must still grant it.
                .Where(entry => entry.Data.Id != pendingLinkTreeId)
                .Select(entry => entry.Data.Id)
                .ToArray();
            _gameData.InitializeLinkTreeRewardLedgerBaseline(baselineIds);
        }

        var pending = _gameData?.PendingLinkTreeClaim;
        var pendingEntry = pending == null
            ? null
            : _linkTreeRewardEntries.FirstOrDefault(entry =>
                entry.Data.Id == pending.LinkTreeId
                && entry.Data.SteamClaimBundleItemDefId == pending.SteamClaimBundleItemDefId
                && entry.Data.SteamReceiptItemDefId == pending.SteamReceiptItemDefId);
        if (pending != null && pendingEntry == null)
        {
            GD.PushWarning($"[LinkTree] Pending claim does not match current LinkTree data: LinkTreeId={pending.LinkTreeId}.");
            _gameData?.ClearPendingLinkTreeClaim();
            ResetLinkTreeInventoryRecheck();
        }
        else if (pendingEntry != null)
        {
            pendingEntry.ClaimPending = true;
            if (snapshot.OwnedItemDefIds.Contains(pending!.SteamReceiptItemDefId)
                && IsLinkTreeRewardConfirmedBySnapshot(pendingEntry.Data, snapshot))
            {
                CompleteSteamLinkTreeClaim(pendingEntry, recovered: true);
            }
            else if (_inventoryService?.IsPromoGrantPending != true)
            {
                ScheduleLinkTreeInventoryRecheck(pendingEntry);
            }
        }

        foreach (var entry in _linkTreeRewardEntries)
        {
            if (!snapshot.OwnedItemDefIds.Contains(entry.Data.SteamReceiptItemDefId))
                continue;

            if (!IsLinkTreeRewardConfirmedBySnapshot(entry.Data, snapshot))
                continue;

            CompleteSteamLinkTreeClaim(entry, recovered: true);
        }

        ResumeWaitingLinkTreeClaims();

        if (_refreshLinkTreeSelectionOnNextInventorySnapshot)
        {
            RefreshLinkTreeVisibleEntries();
            _refreshLinkTreeSelectionOnNextInventorySnapshot = false;
        }
        RefreshLinkTreePageFromPlatformState();
        GD.Print($"[SteamInventory] {snapshot.Message}");
    }

    private void OnPlatformConnectionStateChanged(PlatformConnectionState state)
    {
        RefreshLinkTreePageFromPlatformState();
        if (state == PlatformConnectionState.Ready)
            ResumeWaitingLinkTreeClaims();
    }

    private void OnPlatformInventoryTrustStateChanged(PlatformInventoryTrustState _) =>
        RefreshLinkTreePageFromPlatformState();

    private void OnPlatformPromoItemGrantCompleted(PlatformPromoItemGrantResult result)
    {
        var entry = _linkTreeRewardEntries.FirstOrDefault(candidate =>
            candidate.Data.SteamClaimBundleItemDefId == result.PromoItemDefId
            && candidate.Data.SteamReceiptItemDefId == result.ReceiptItemDefId);
        if (entry == null)
        {
            // Other inventory-backed features share AddPromoItem with LinkTree.
            return;
        }

        entry.ClaimPending = false;
        if (!result.Succeeded || !result.ReceiptOwned)
        {
            _gameData?.ClearPendingLinkTreeClaim();
            ResetLinkTreeInventoryRecheck();
            RefreshLinkTreeInteractionPresentation();
            GD.PushWarning($"[LinkTree] {result.Message}");
            return;
        }

        // Enter the authoritative full-inventory synchronization state before queuing
        // reward feedback. Otherwise the tween starts on the visible Banner and then
        // immediately continues behind the full-page loading state.
        _inventoryService?.StartInventorySynchronization();

        if (IsLinkTreeRewardConfirmedByItems(entry.Data, result.ChangedItems))
        {
            CompleteSteamLinkTreeClaim(entry, recovered: false);
        }
        else
        {
            entry.ClaimPending = true;
            ScheduleLinkTreeInventoryRecheck(entry);
            RefreshLinkTreeInteractionPresentation();
        }

        GD.Print($"[LinkTree] {result.Message}");
    }

    private void CompleteSteamLinkTreeClaim(LinkTreeRewardEntry entry, bool recovered)
    {
        if (_gameData?.PendingLinkTreeClaim is { } pending
            && (pending.LinkTreeId != entry.Data.Id
                || pending.SteamClaimBundleItemDefId != entry.Data.SteamClaimBundleItemDefId
                || pending.SteamReceiptItemDefId != entry.Data.SteamReceiptItemDefId))
        {
            GD.PushWarning($"[LinkTree] Refusing mismatched pending reward completion for {entry.Data.Key}.");
            return;
        }

        bool wasAlreadyApplied = _gameData?.HasAppliedLinkTreeReward(entry.Data.Id) == true;
        if (wasAlreadyApplied)
        {
            if (entry.State != LinkTreeRewardState.Claimed
                || entry.ClaimPending
                || _gameData?.PendingLinkTreeClaim?.LinkTreeId == entry.Data.Id)
            {
                MarkLinkTreeEntryClaimed(entry);
            }
            return;
        }

        if (!TryGrantLinkTreeReward(entry.Data))
        {
            GD.PushError($"[LinkTree] Steam receipt exists but local reward failed for {entry.Data.Key}.");
            return;
        }

        MarkLinkTreeEntryClaimed(entry);
        SetupLinkTreeRewardPreview(entry);
        QueueLinkTreeRewardFeedback(entry);
        GD.Print($"[LinkTree] {(recovered ? "Recovered" : "Completed")} Steam-backed reward for {entry.Data.Key} ({entry.Data.Id}).");
        DiagnosticLog.Record("linktree_reward_completed", new Dictionary<string, object>
        {
            ["linkTreeId"] = entry.Data.Id,
            ["rewardType"] = entry.Data.RewardType.ToString(),
            ["rewardChips"] = entry.Data.RewardChips,
            ["rewardItemId"] = entry.Data.RewardItemId,
            ["recovered"] = recovered,
            ["wasAlreadyApplied"] = wasAlreadyApplied,
            ["chips"] = _gameData.Chips,
        });
    }

    private bool TryGrantLinkTreeReward(LinkTree data)
    {
        switch (data.RewardType)
        {
            case ELinkTreeRewardType.None:
                return true;

            case ELinkTreeRewardType.FixedItem:
                if (data.RewardItemId <= 0 || LubanData.Tables.TbItem.GetOrDefault(data.RewardItemId) == null)
                {
                    GD.PushWarning($"[LinkTree] Reward item is missing for {data.Key} ({data.Id}): {data.RewardItemId}.");
                    return false;
                }
                // Full snapshot confirmation happens before this method. Record the local
                // application ledger without fabricating another copy of the Steam item.
#if DEBUG
                if (_steamMockActive && _gameData?.HasAppliedLinkTreeReward(data.Id) != true)
                    _gameData?.AddItem(data.RewardItemId, source: PlayerProgressSource.Debug);
#endif
                return _gameData?.TryApplyLinkTreeRewardOnce(data.Id, 0) == true;

            case ELinkTreeRewardType.FixedChips:
                if (_gameData == null)
                    return false;

                return _gameData.TryApplyLinkTreeRewardOnce(data.Id, data.RewardChips);

            case ELinkTreeRewardType.SequentialPack:
                GD.PushWarning($"[LinkTree] SequentialPack reward is not implemented yet: {data.Key} ({data.Id}).");
                return false;

            default:
                GD.PushWarning($"[LinkTree] Unknown reward type for {data.Key} ({data.Id}): {data.RewardType}.");
                return false;
        }
    }

    private static bool IsLinkTreeRewardConfirmedBySnapshot(
        LinkTree data,
        PlatformInventorySnapshot snapshot)
    {
        if (data.RewardType != ELinkTreeRewardType.FixedItem)
            return true;

        var item = LubanData.Tables.TbItem.GetOrDefault(data.RewardItemId);
        if (item == null || item.SteamItemDefId <= 0)
            return false;

        return snapshot.OwnedItemDefIds.Contains(item.SteamItemDefId);
    }

    private static bool IsLinkTreeRewardConfirmedByItems(
        LinkTree data,
        IReadOnlyList<PlatformInventoryItem> items)
    {
        if (data.RewardType != ELinkTreeRewardType.FixedItem)
            return true;

        var item = LubanData.Tables.TbItem.GetOrDefault(data.RewardItemId);
        if (item == null || item.SteamItemDefId <= 0)
            return false;

        return items.Any(changedItem =>
            changedItem.ItemDefId == item.SteamItemDefId && changedItem.Quantity > 0);
    }

    private void MarkLinkTreeEntryClaimed(LinkTreeRewardEntry entry)
    {
        var presentationChanged = entry.ClaimPending || entry.State != LinkTreeRewardState.Claimed;
        entry.ClaimPending = false;
        entry.State = LinkTreeRewardState.Claimed;
        if (_gameData?.PendingLinkTreeClaim?.LinkTreeId == entry.Data.Id)
        {
            _gameData.ClearPendingLinkTreeClaim();
            ResetLinkTreeInventoryRecheck();
        }
        if (presentationChanged)
            RefreshLinkTreeInteractionPresentation();
    }

    private void ScheduleLinkTreeInventoryRecheck(LinkTreeRewardEntry entry)
    {
        if (_linkTreeInventoryRecheckRemainingSeconds >= 0.0)
            return;

        if (_linkTreeInventoryRecheckAttemptCount >= LinkTreeInventoryRecheckDelaysSeconds.Length)
        {
            entry.ClaimPending = false;
            _gameData?.ClearPendingLinkTreeClaim();
            ResetLinkTreeInventoryRecheck();
            RefreshLinkTreeInteractionPresentation();
            GD.PushWarning(
                $"[LinkTree] Steam inventory did not confirm {entry.Data.Key} after bounded rechecks; "
                + "stopped recovery without resubmitting the Bundle. The player may retry manually.");
            return;
        }

        _linkTreeInventoryRecheckRemainingSeconds =
            LinkTreeInventoryRecheckDelaysSeconds[_linkTreeInventoryRecheckAttemptCount];
    }

    private void ProcessLinkTreeInventoryRecheck(double delta)
    {
        if (_linkTreeInventoryRecheckRemainingSeconds < 0.0)
            return;

        if (_gameData?.PendingLinkTreeClaim == null)
        {
            ResetLinkTreeInventoryRecheck();
            return;
        }

        _linkTreeInventoryRecheckRemainingSeconds -= delta;
        if (_linkTreeInventoryRecheckRemainingSeconds > 0.0)
            return;

        _linkTreeInventoryRecheckRemainingSeconds = -1.0;
        _linkTreeInventoryRecheckAttemptCount++;
        _inventoryService?.StartInventorySynchronization();
    }

    private void ResetLinkTreeInventoryRecheck()
    {
        _linkTreeInventoryRecheckRemainingSeconds = -1.0;
        _linkTreeInventoryRecheckAttemptCount = 0;
    }

    private void ResumeWaitingLinkTreeClaims()
    {
        if (_inventoryService?.IsInventoryReady != true || _gameData?.PendingLinkTreeClaim != null)
            return;

        var waitingEntry = _linkTreeRewardEntries.FirstOrDefault(entry =>
            entry.ClaimPending && entry.State == LinkTreeRewardState.ReadyToClaim);
        if (waitingEntry != null)
            ClaimSteamLinkTreeReward(waitingEntry);
    }

    private void RefreshLinkTreeInteractionPresentation()
    {
        foreach (var entry in _linkTreeRewardEntries)
            RefreshLinkTreeRewardEntry(entry);
    }

    private void RefreshLinkTreeRewardEntry(LinkTreeRewardEntry entry)
    {
        var showLoading = entry.ClaimPending && entry.State != LinkTreeRewardState.Claimed;
        var inventoryWritePending = IsSharedInventoryWritePending();
        entry.Banner.Disabled = inventoryWritePending;
        entry.BannerImage.Modulate = inventoryWritePending ? LinkTreeBusyImageModulate : Colors.White;
        entry.LoadingIndicator.SetLoading(showLoading);
        entry.GiftBadge.Visible = entry.State != LinkTreeRewardState.Claimed && !showLoading;
        var giftBadgeColor = entry.State switch
        {
            LinkTreeRewardState.Unopened => LinkTreeGiftLockedColor,
            LinkTreeRewardState.OpenedAwaitingReturn => LinkTreeGiftLockedColor,
            LinkTreeRewardState.ReadyToClaim => LinkTreeGiftReadyColor,
            _ => LinkTreeGiftClaimedColor,
        };
        entry.GiftBadge.Modulate = inventoryWritePending
            ? new Color(
                giftBadgeColor.R * LinkTreeBusyImageModulate.R,
                giftBadgeColor.G * LinkTreeBusyImageModulate.G,
                giftBadgeColor.B * LinkTreeBusyImageModulate.B,
                giftBadgeColor.A)
            : giftBadgeColor;
    }

    private static void SetupLinkTreeRewardPreview(LinkTreeRewardEntry entry)
    {
        switch (entry.Data.RewardType)
        {
            case ELinkTreeRewardType.FixedItem:
                var item = LubanData.Tables.TbItem.GetOrDefault(entry.Data.RewardItemId);
                if (item != null)
                {
                    entry.RewardCell.Visible = true;
                    entry.RewardCellShadow.Visible = true;
                    entry.RewardAmountLabel.Visible = false;
                    entry.RewardCell.Setup(item, isEquipped: false, count: 1, isNew: false);
                }
                break;

            case ELinkTreeRewardType.FixedChips:
                entry.RewardCell.Visible = false;
                entry.RewardCellShadow.Visible = false;
                entry.RewardAmountLabel.Text = FormatSignedLinkTreeRewardAmount(entry.Data.RewardChips);
                entry.RewardAmountLabel.Visible = true;
                break;

            default:
                entry.RewardCell.Visible = false;
                entry.RewardCellShadow.Visible = false;
                entry.RewardAmountLabel.Visible = false;
                break;
        }
    }

    private void PlayLinkTreeRewardFeedback(LinkTreeRewardEntry entry)
    {
        entry.RewardFeedbackPending = false;
        entry.RewardFeedbackPlaying = true;
        entry.RewardFeedbackTween?.Kill();

        entry.RewardVisualRoot.Visible = true;
        entry.RewardVisualRoot.Scale = LinkTreeRewardFeedbackStartScale;
        entry.RewardVisualRoot.Modulate = new Color(1f, 1f, 1f, 0f);

        entry.RewardFeedbackTween = CreateTween();
        entry.RewardFeedbackTween.TweenProperty(entry.RewardVisualRoot, "scale", LinkTreeRewardFeedbackRestScale, 0.18)
            .SetTrans(Tween.TransitionType.Back)
            .SetEase(Tween.EaseType.Out);
        entry.RewardFeedbackTween.Parallel().TweenProperty(entry.RewardVisualRoot, "modulate:a", 1f, 0.12)
            .SetTrans(Tween.TransitionType.Quad)
            .SetEase(Tween.EaseType.Out);

        entry.RewardFeedbackTween.TweenInterval(LinkTreeRewardFeedbackHoldSeconds);

        entry.RewardFeedbackTween.TweenProperty(entry.RewardVisualRoot, "scale", LinkTreeRewardFeedbackEndScale, 0.14)
            .SetTrans(Tween.TransitionType.Quad)
            .SetEase(Tween.EaseType.In);
        entry.RewardFeedbackTween.Parallel().TweenProperty(entry.RewardVisualRoot, "modulate:a", 0f, 0.14)
            .SetTrans(Tween.TransitionType.Quad)
            .SetEase(Tween.EaseType.In);

        entry.RewardFeedbackTween.TweenCallback(Callable.From(() =>
        {
            entry.RewardFeedbackPlaying = false;
            entry.RewardVisualRoot.Visible = false;
            RefreshLinkTreeVisibleEntries();
        }));
    }

    private void QueueLinkTreeRewardFeedback(LinkTreeRewardEntry entry)
    {
        entry.RewardFeedbackPending = true;
        TryPlayPendingLinkTreeRewardFeedback(entry);
    }

    private void TryPlayPendingLinkTreeRewardFeedbacks()
    {
        foreach (var entry in _linkTreeRewardEntries)
            TryPlayPendingLinkTreeRewardFeedback(entry);
    }

    private void TryPlayPendingLinkTreeRewardFeedback(LinkTreeRewardEntry entry)
    {
        if (!entry.RewardFeedbackPending
            || _linkTreePageState != LinkTreePageState.Ready
            || !_linkTreeContent.Visible
            || !entry.SelectedForDisplay
            || !entry.Banner.IsVisibleInTree())
        {
            return;
        }

        PlayLinkTreeRewardFeedback(entry);
    }

    private static string FormatSignedLinkTreeRewardAmount(int amount)
    {
        return amount >= 0
            ? $"+ {amount}"
            : $"- {Math.Abs(amount)}";
    }

    private static void ApplyLinkTreeTexture(TextureRect rect, string tablePath)
    {
        var path = ToLinkTreeResPath(tablePath);
        rect.Texture = !string.IsNullOrWhiteSpace(path) && ResourceLoader.Exists(path)
            ? GD.Load<Texture2D>(path)
            : null;
    }

    private static string ToLinkTreeResPath(string tablePath)
    {
        if (string.IsNullOrWhiteSpace(tablePath))
            return string.Empty;

        var normalized = tablePath.Replace('\\', '/');
        if (normalized.StartsWith("res://", StringComparison.OrdinalIgnoreCase))
            return normalized;

        return normalized.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase)
            ? $"res://{normalized}"
            : $"res://Assets/{normalized}";
    }

    private static string ResolveLinkTreeTooltip(LinkTree data)
    {
        if (string.IsNullOrWhiteSpace(data.TooltipKey))
            return data.Key;

        var translated = L10n.Tr(data.TooltipKey);
        return translated == data.TooltipKey ? data.TooltipKey : translated;
    }

#if DEBUG
    private void RefreshDebugPlayTime()
    {
        if (_playTimeLabel == null || _gameData == null)
            return;

        var total = TimeSpan.FromSeconds(_gameData.TotalPlaySeconds);
        _playTimeLabel.Text = $"Play Time: {total:hh\\:mm\\:ss} ({_gameData.TotalPlaySeconds:0.0}s)";
        _luckyDealBuffLabel.Text = $"Lucky Deal Buff: {_gameData.LuckyDealRemainingHands} hands remaining";
        _playerProgressDebugLabel.Text = _gameData.GetPlayerProgressDebugStatus();
        RefreshBlindBoxLocalTestControls();
        RefreshBlindBoxDebugStatus();
    }

    private void ToggleBlindBoxDebug()
    {
        _blindBoxDebugContent.Visible = !_blindBoxDebugContent.Visible;
        _blindBoxDebugToggle.Text = _blindBoxDebugContent.Visible
            ? "▼ BlindBox Debug"
            : "▶ BlindBox Debug";
        RefreshBlindBoxDebugStatus();
    }

    private void HideDebugTabForSession()
    {
        // 仅用于录制前清理界面：不写入设置或存档，重启游戏后调试页签会自然恢复。
        _debugTab.Visible = false;
        SwitchTab(0);
    }

    private void RefreshBlindBoxDebugStatus()
    {
        if (_blindBoxDebugLabel == null || _gameData == null)
            return;

        _blindBoxDebugLabel.Text = _gameData.GetBlindBoxDebugStatus();
    }

    private void RefreshBlindBoxLocalTestControls()
    {
        if (_blindBoxLocalTestModeToggle == null || _gameData == null)
            return;

        var mockActive = _gameData.IsSteamMockSimulationActive;
        var enabled = _gameData.IsBlindBoxLocalTestMode;
        _blindBoxLocalTestModeToggle.SetPressedNoSignal(enabled);
        _blindBoxLocalTestModeToggle.Disabled = mockActive;
        _blindBoxLocalTestModeToggle.TooltipText = mockActive
            ? "Steam Mock 已启用；准备奖励和网络阶段请使用上方面板。"
            : string.Empty;
        _blindBoxLocalTestPreparedRewardCount.Text = mockActive
            ? "—"
            : _gameData.BlindBoxLocalTestPreparedRewardCount.ToString();
        _blindBoxLocalTestPreparedRewardDecrease.Disabled = mockActive || !enabled;
        _blindBoxLocalTestPreparedRewardIncrease.Disabled = mockActive || !enabled;
        var sandboxActive = mockActive || enabled;
        _blindBoxLocalTestAdvance.Disabled = !sandboxActive
                                             || _gameData.SteamMockPresentationAdvancePending;
        _blindBoxLocalTestAdvance.TooltipText = _gameData.SteamMockPresentationAdvancePending
            ? "正在等待当前 Schedule 的 Steam Mock 奖励准备完成。"
            : string.Empty;
        _blindBoxLocalTestClear.Disabled = !sandboxActive;
    }
#endif

    // ===== Wardrobe 页 =====

    private bool _wardrobeBuilt;

    private void BuildWardrobe()
    {
        if (!_wardrobeBuilt)
        {
            BuildTypeFilters();
            _wardrobeBuilt = true;
        }
        if (_selectedTab != null)
            PopulateWardrobeGrid(_selectedTab);
    }

    private void BuildTypeFilters()
    {
        foreach (var child in _typeFilterRow.GetChildren())
            child.QueueFree();
        _filterTabs.Clear();
        _typeFilterButtons.Clear();

        var tabs = LubanData.Tables.TbTabGroup.DataList
            .OrderBy(t => t.SortOrder);

        var selectedTabId = _selectedTab?.Id;
        foreach (var tab in tabs)
        {
            var btn = new Button();
            btn.CustomMinimumSize = new Vector2(0, 28);
            btn.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            btn.TooltipText = GetWardrobeTabTooltip(tab);
            if (TabIconsByGroupId.TryGetValue(tab.Id, out var icon))
                btn.Icon = icon;
            btn.IconAlignment = HorizontalAlignment.Center;
            btn.VerticalIconAlignment = VerticalAlignment.Center;
            btn.ThemeTypeVariation = CategoryTabStyle;
            btn.Pressed += () =>
            {
                _selectedTab = tab;
                UpdateFilterButtonStyles(tab.Id);
                PopulateWardrobeGrid(tab);
            };
            _filterTabs[btn] = tab;
            _typeFilterRow.AddChild(btn);
            _typeFilterButtons.Add(btn);
        }

        if (_typeFilterButtons.Count > 0)
        {
            _selectedTab = tabs.FirstOrDefault(t => t.Id == selectedTabId) ?? tabs.First();
            UpdateFilterButtonStyles(_selectedTab.Id);
        }
    }

    private void UpdateFilterButtonStyles(int activeTabId)
    {
        foreach (var (btn, tab) in _filterTabs)
            btn.ThemeTypeVariation = tab.Id == activeTabId ? CategoryTabSelectedStyle : CategoryTabStyle;
    }

    private void PopulateWardrobeGrid(TabGroup tab)
    {
        foreach (var child in _wardrobeGrid.GetChildren())
        {
            child.QueueFree();
        }

        var items = tab.TabItemTypeList
            .SelectMany(type => _gameData.Inventory.GetOwnedOfType(type))
            .Where(item => !item.IsHiddenInBag)
            .OrderBy(item => (int)item.ItemRarity)
            .ThenBy(item => (int)item.ItemType)
            .ThenByDescending(item => item.SortOrder)
            .ThenBy(item => item.Id)
            .ToList();

        _wardrobeGrid.Visible = items.Count > 0;
        _emptyWardrobeCenter.Visible = items.Count == 0;
        if (items.Count == 0)
            return;

        foreach (var item in items)
            _wardrobeGrid.AddChild(CreateItemCell(item));
    }

    private static string GetWardrobeTabTooltip(TabGroup tab)
    {
        var key = tab.Id switch
        {
            1001 => L10nKey.Wardrobe_Tab_Dog,
            1002 => L10nKey.Wardrobe_Tab_Hat,
            1003 => L10nKey.Wardrobe_Tab_Eyewear,
            1004 => L10nKey.Wardrobe_Tab_Player,
            1005 => L10nKey.Wardrobe_Tab_Theme,
            1006 => L10nKey.Wardrobe_Tab_Refreshment,
            _ => null,
        };

        return key == null ? tab.TabName : L10n.Tr(key);
    }

    private void RefreshWardrobeGrid()
    {
        if (_wardrobeContent.Visible && _selectedTab != null)
            PopulateWardrobeGrid(_selectedTab);
    }

    private static readonly PackedScene ItemCellScene = GD.Load<PackedScene>("res://Scenes/Prefabs/ItemCell.tscn");

    private Node CreateItemCell(Item item)
    {
        var cell = ItemCellScene.Instantiate<ItemCellController>();
        var isSelected = item.ItemType == EItemType.Refreshment
            ? _gameData.IsTableRefreshmentSelected(item.Id)
            : _gameData.Inventory.IsEquipped(item.Id);
        cell.Setup(
            item,
            isSelected,
            _gameData.Inventory.GetCount(item.Id),
            _gameData.Inventory.IsNew(item.Id));
        cell.Pressed += () => _gameData.ToggleEquipItem(item.Id);
        return cell;
    }

    // ===== 公共 API =====

    public void Toggle() { if (_panel.Visible) Close(); else Open(); }

    public void SetCurrentMode(bool isBossKeyMode)
    {
        if (_switchToPlayBtn == null || _switchToBossKeyBtn == null)
            return;

        _switchToPlayBtn.Visible = isBossKeyMode;
        _switchToBossKeyBtn.Visible = !isBossKeyMode;
    }

    public void Open()
    {
        if (_tween != null && _tween.IsRunning()) _tween.Kill();
        EnsureCurrentTabReady();
        _panel.Modulate = Colors.White with { A = 0f };
        _panel.Visible = true;
        Callable.From(TryPlayPendingLinkTreeRewardFeedbacks).CallDeferred();
        _tween = CreateTween();
        _tween.TweenProperty(_panel, "modulate:a", 1f, 0.15f).SetEase(Tween.EaseType.Out);
    }

    public void SetPanelPosition(Vector2 pos)
    {
        _panel.Position = pos;
#if DEBUG
        _resetSaveConfirm?.SetOverlayRect(pos, PanelSize);
#endif
    }

    public void Close()
    {
#if DEBUG
        if (_resetSaveConfirm != null)
            _resetSaveConfirm.Visible = false;
#endif
        if (_tween != null && _tween.IsRunning()) _tween.Kill();
        _tween = CreateTween();
        _tween.TweenProperty(_panel, "modulate:a", 0f, 0.1f).SetEase(Tween.EaseType.In);
        _tween.TweenCallback(Callable.From(() => _panel.Visible = false));
    }

    public void CloseImmediate()
    {
#if DEBUG
        if (_resetSaveConfirm != null)
            _resetSaveConfirm.Visible = false;
#endif
        if (_tween != null && _tween.IsRunning()) _tween.Kill();
        _panel.Modulate = Colors.White with { A = 0f };
        _panel.Visible = false;
    }

    private void RestartGame()
    {
        _gameData?.SaveImmediatelyIfUsingLocalSave();
        SingleInstanceGuard.ReleaseForRestart();
        var processId = OS.CreateInstance(OS.GetCmdlineArgs());
        if (processId < 0)
        {
            SingleInstanceGuard.ReacquireAfterFailedRestart();
            OS.Alert("Failed to restart Lucky Dog Rise. The current game will remain open.", "Lucky Dog Rise");
            return;
        }
        GetTree().Quit();
    }

    public string ExportDiagnostics(bool revealInExplorer = true)
    {
        try
        {
            _exportDiagnosticsButton.Disabled = true;
            _exportDiagnosticsStatusLabel.Text = string.Empty;
            _exportDiagnosticsStatusLabel.Visible = false;
            var path = DiagnosticLog.ExportPackage(_gameData, _platformService);
            _exportDiagnosticsStatusLabel.Modulate = new Color(0.24f, 0.68f, 0.42f);
            _exportDiagnosticsStatusLabel.Text = L10n.Format(L10nKey.Settings_ExportDiagnosticsSuccess, path);
            _exportDiagnosticsStatusLabel.TooltipText = path;
            _exportDiagnosticsStatusLabel.Visible = true;
            if (revealInExplorer)
            {
                try
                {
                    DiagnosticLog.RevealInExplorer(path);
                }
                catch (Exception exception)
                {
                    GD.PushWarning($"[Diagnostics] Export succeeded, but Explorer could not reveal the file: {exception.Message}");
                }
            }
            GD.Print($"[Diagnostics] Exported diagnostic package: {path}");
            return path;
        }
        catch (Exception exception)
        {
            GD.PushError($"[Diagnostics] Export failed: {exception}");
            _exportDiagnosticsStatusLabel.Modulate = new Color(0.82f, 0.25f, 0.25f);
            _exportDiagnosticsStatusLabel.Text = L10n.Tr(L10nKey.Settings_ExportDiagnosticsFailed);
            _exportDiagnosticsStatusLabel.TooltipText = exception.Message;
            _exportDiagnosticsStatusLabel.Visible = true;
            return string.Empty;
        }
        finally
        {
            _exportDiagnosticsButton.Disabled = false;
        }
    }

    public bool ContainsPoint(Vector2 windowPos)
    {
        if (!_panel.Visible) return false;
        return new Rect2(_panel.Position, PanelSize).HasPoint(windowPos)
#if DEBUG
            || (_resetSaveConfirm.Visible && new Rect2(_resetSaveConfirm.Position, _resetSaveConfirm.Size).HasPoint(windowPos))
#endif
            || PopupContainsPoint(_languageOption.GetPopup(), windowPos)
            || PopupContainsPoint(_displayOption.GetPopup(), windowPos)
            || PopupContainsPoint(_armAppearanceOption.GetPopup(), windowPos)
#if DEBUG
            || PopupContainsPoint(_reactionOption.GetPopup(), windowPos)
            || PopupContainsPoint(_playerProgressMultiplierOption.GetPopup(), windowPos)
#endif
            ;
    }

    private static bool PopupContainsPoint(PopupMenu popup, Vector2 windowPos)
    {
        if (popup == null || !popup.Visible)
            return false;

        var popupRect = new Rect2(popup.Position, popup.Size);
        if (popupRect.HasPoint(windowPos))
            return true;

        var screenRelativePosition = popup.Position - DisplayServer.WindowGetPosition();
        return new Rect2(screenRelativePosition, popup.Size).HasPoint(windowPos);
    }

#if DEBUG
    public void UpdateSeed(int seed)
    {
        _currentSeed = seed;
        _seedLabel.Text = $"Seed: {seed}";
    }

    public bool TryGetFixedSeed(out int seed)
    {
        seed = 0;
        return _seedInput.Text.Length > 0 && int.TryParse(_seedInput.Text, out seed);
    }

    private void BuildReactionOptions()
    {
        _reactionOption.Clear();

        var triggers = LubanData.Tables.TbDogReaction.DataList
            .Select(reaction => reaction.DogReactionTrigger)
            .Where(trigger => trigger != EDogReactionTrigger.None && trigger != EDogReactionTrigger.Bespoke)
            .Distinct()
            .OrderBy(trigger => (int)trigger);

        foreach (var trigger in triggers)
            _reactionOption.AddItem($"{trigger} ({(int)trigger})", (int)trigger);

        if (_reactionOption.ItemCount > 0)
            _reactionOption.Select(0);
    }
#endif

    // ===== 设置回调 =====

    private void BuildLanguageOptions()
    {
        _languageOption.Clear();
        var savedLocale = SettingsManager.LoadLocale();
        var selectedIndex = 0;
        for (int i = 0; i < LocaleOptions.Length; i++)
        {
            var locale = LocaleOptions[i];
            _languageOption.AddItem(L10n.GetDisplayName(locale), i);
            if (locale == savedLocale)
                selectedIndex = i;
        }

        _languageOption.Select(selectedIndex);
    }

    private void BuildDisplayOptions()
    {
        _displayOption.Clear();
        _displayOption.AddItem(L10n.Tr(L10nKey.Settings_CounterDisplay_Clock), (int)SettingsManager.DisplayMode.Clock);
        _displayOption.AddItem(L10n.Tr(L10nKey.Settings_CounterDisplay_Chips), (int)SettingsManager.DisplayMode.Chips);
        _displayOption.AddItem(L10n.Tr(L10nKey.Settings_CounterDisplay_Nickname), (int)SettingsManager.DisplayMode.Nickname);
        _displayOption.AddItem(L10n.Tr(L10nKey.Settings_CounterDisplay_Hidden), (int)SettingsManager.DisplayMode.Hidden);
        SelectDisplayModeOption(SettingsManager.LoadDisplayMode());
    }

    private void SelectDisplayModeOption(SettingsManager.DisplayMode mode)
    {
        for (var i = 0; i < _displayOption.ItemCount; i++)
        {
            if (_displayOption.GetItemId(i) != (int)mode)
                continue;

            _displayOption.Select(i);
            return;
        }

        _displayOption.Select(0);
    }

    private void BuildPokerFrameRateOptions()
    {
        _pokerFrameRateOption.Clear();
        var savedFrameRate = SettingsManager.LoadPokerFrameRate();
        var selectedIndex = 0;
        for (int i = 0; i < PokerFrameRateOptions.Length; i++)
        {
            var frameRate = PokerFrameRateOptions[i];
            _pokerFrameRateOption.AddItem($"{frameRate} FPS", frameRate);
            if (frameRate == savedFrameRate)
                selectedIndex = i;
        }

        _pokerFrameRateOption.Select(selectedIndex);
    }

    private void BuildArmAppearanceOptions()
    {
        if (_armAppearanceOption == null || _gameData == null)
            return;

        _refreshingArmAppearanceOption = true;
        _armAppearanceOption.Clear();

        var armItems = LubanData.Tables.TbItem.DataList
            .Where(item => item.ItemType == EItemType.Arm
                && item.AcquisitionType == EAcquisitionType.Initial
                && _gameData.Inventory.Owns(item.Id))
            .OrderBy(item => item.Id)
            .ToList();

        _armAppearanceOption.Disabled = armItems.Count == 0;

        for (int i = 0; i < armItems.Count; i++)
        {
            var item = armItems[i];
            var label = L10n.Format(L10nKey.Settings_ArmAppearance_Tone, i + 1);
            var icon = LoadArmAppearanceIcon(item);
            if (icon != null)
                _armAppearanceOption.AddIconItem(icon, label, item.Id);
            else
            {
                _armAppearanceOption.AddItem(label, item.Id);
            }
        }

        RefreshArmAppearanceSelection();
        _refreshingArmAppearanceOption = false;
    }

    private static Texture2D LoadArmAppearanceIcon(Item item)
    {
        if (item.AssetPathList.Count > 0)
        {
            var normalizedAssetPath = item.AssetPathList[0].Replace('\\', '/');
            var fileName = normalizedAssetPath.Split('/').Last();
            var extensionIndex = fileName.LastIndexOf('.');
            var assetName = extensionIndex > 0 ? fileName[..extensionIndex] : fileName;
            var colorChipPath = $"{ArmColorChipPathPrefix}{assetName}.svg";
            if (ResourceLoader.Exists(colorChipPath))
                return GD.Load<Texture2D>(colorChipPath);

            GD.PushWarning(
                $"[Settings] Arm color-chip icon could not be loaded: item {item.Id}, {colorChipPath}; " +
                "falling back to the generic item icon.");
        }

        return PlayerInventory.LoadItemIconOrFallback(item.IconPath);
    }

    private void RefreshArmAppearanceSelection()
    {
        if (_armAppearanceOption == null || _gameData == null)
            return;

        var equippedId = _gameData.Inventory.GetEquipped(EItemType.Arm)?.Id ?? 0;
        for (int i = 0; i < _armAppearanceOption.ItemCount; i++)
        {
            if (_armAppearanceOption.GetItemId(i) != equippedId)
                continue;

            _armAppearanceOption.Select(i);
            return;
        }
    }

    private void RefreshLocalizedOptionText()
    {
        if (_languageOption == null)
            return;

        SetLinkTreePageState(_linkTreePageState);

        var selected = _languageOption.GetSelectedId();
        for (int i = 0; i < LocaleOptions.Length; i++)
            _languageOption.SetItemText(i, L10n.GetDisplayName(LocaleOptions[i]));
        if (selected >= 0)
            _languageOption.Select(selected);

        if (_displayOption != null)
        {
            for (var i = 0; i < _displayOption.ItemCount; i++)
            {
                var mode = (SettingsManager.DisplayMode)_displayOption.GetItemId(i);
                _displayOption.SetItemText(i, mode switch
                {
                    SettingsManager.DisplayMode.Clock => L10n.Tr(L10nKey.Settings_CounterDisplay_Clock),
                    SettingsManager.DisplayMode.Chips => L10n.Tr(L10nKey.Settings_CounterDisplay_Chips),
                    SettingsManager.DisplayMode.Nickname => L10n.Tr(L10nKey.Settings_CounterDisplay_Nickname),
                    SettingsManager.DisplayMode.Hidden => L10n.Tr(L10nKey.Settings_CounterDisplay_Hidden),
                    _ => mode.ToString(),
                });
            }
        }

        if (_armAppearanceOption != null)
        {
            for (int i = 0; i < _armAppearanceOption.ItemCount; i++)
                _armAppearanceOption.SetItemText(i, L10n.Format(L10nKey.Settings_ArmAppearance_Tone, i + 1));
        }

        RefreshModeButtonText();

        foreach (var (button, tab) in _filterTabs)
            button.TooltipText = GetWardrobeTabTooltip(tab);
    }

    private void RefreshModeButtonText()
    {
        if (_switchToPlayBtn == null || _switchToBossKeyBtn == null)
            return;

        RefreshModeButtonText(_switchToPlayBtn, L10nKey.Common_Play);
        RefreshModeButtonText(_switchToBossKeyBtn, L10nKey.Common_Desktop);
    }

    private static void RefreshModeButtonText(Button button, string key)
    {
        var showText = L10n.CurrentLocale == L10n.SimplifiedChineseLocale;
        button.Text = showText ? L10n.Tr(key) : string.Empty;
        button.IconAlignment = showText ? HorizontalAlignment.Left : HorizontalAlignment.Center;
    }

    private void OnLanguageSelected(long index)
    {
        var i = Mathf.Clamp((int)index, 0, LocaleOptions.Length - 1);
        L10n.SetLocale(LocaleOptions[i]);
        RefreshLocalizedOptionText();
    }

    private void OnSfxVolumeChanged(double value)
    {
        var volume = (float)value;
        SettingsManager.SaveSfxVolume(volume);
        AudioManager.Instance.SetSfxVolume(volume);
        RefreshVolumeLabel(_sfxVolumeValueLabel, volume);
    }

    private void OnBgmVolumeChanged(double value)
    {
        var volume = (float)value;
        SettingsManager.SaveBgmVolume(volume);
        AudioManager.Instance.SetBgmVolume(volume);
        RefreshVolumeLabel(_bgmVolumeValueLabel, volume);
    }

    private void OnDesktopBgmToggled(bool enabled)
    {
        SettingsManager.SavePlayBgmInDesktop(enabled);
        EmitSignal(SignalName.DesktopBgmPlaybackChanged, enabled);
    }

    private void OnAlwaysShowBlindBoxBubbleToggled(bool enabled)
    {
        SettingsManager.SaveAlwaysShowBlindBoxBubble(enabled);
        EmitSignal(SignalName.BlindBoxBubbleVisibilityChanged);
    }

    private void OnDisplayModeChanged(long index)
    {
        var selectedId = _displayOption.GetSelectedId();
        if (selectedId >= 0)
            SettingsManager.SaveDisplayMode((SettingsManager.DisplayMode)selectedId);
    }

    private void OnPokerFrameRateSelected(long index)
    {
        var itemIndex = (int)index;
        if (itemIndex < 0 || itemIndex >= _pokerFrameRateOption.ItemCount)
            return;

        SettingsManager.SavePokerFrameRate(_pokerFrameRateOption.GetItemId(itemIndex));
    }

    private void OnArmAppearanceSelected(long index)
    {
        if (_refreshingArmAppearanceOption || _gameData == null)
            return;

        var itemIndex = (int)index;
        if (itemIndex < 0 || itemIndex >= _armAppearanceOption.ItemCount)
            return;

        _gameData.EquipItem(_armAppearanceOption.GetItemId(itemIndex));
    }

#if DEBUG
    private void OnSaveDataModeChanged(long index)
    {
        _gameData.SetSaveDataMode((SettingsManager.SaveDataMode)(int)index);
        _wardrobeBuilt = false;
        if (_wardrobeContent.Visible)
            BuildWardrobe();
    }
#endif

#if DEBUG
    private void ConfirmResetSave()
    {
        _resetPlayerProgressPending = false;
        _resetSaveConfirm.ShowConfirmKey(
            L10nKey.Settings_ResetSaveData,
            L10nKey.Settings_ResetSaveMessage,
            L10nKey.Settings_ResetSaveConfirm,
            L10nKey.Common_Cancel);
    }

    private void OnResetSaveConfirmed()
    {
        _gameData.ResetLocalSave();
        _wardrobeBuilt = false;
        if (_wardrobeContent.Visible)
            BuildWardrobe();
    }
#endif

#if DEBUG
    private void ConfirmResetPlayerProgress()
    {
        _resetPlayerProgressPending = true;
        _resetSaveConfirm.ShowConfirm(
            "重置本地成就与统计？",
            "仅清空当前帐号目录中的 player_progress_0.json。不会影响筹码、背包、装备或游戏存档。",
            "重置",
            "取消");
    }
#endif

#if DEBUG
    private void OnResetConfirmed()
    {
        if (_resetPlayerProgressPending)
        {
            _resetPlayerProgressPending = false;
            _gameData.ResetPlayerProgress();
            RefreshGlobalInputChipsEarnedLabel();
            RefreshDebugPlayTime();
            return;
        }
        OnResetSaveConfirmed();
    }
#endif

#if DEBUG
    private void ResetSettingsToDefaults()
    {
        SettingsManager.ResetToDefaults();
        L10n.SetSafeMode(SettingsManager.LoadStreamerSafeMode(), notify: false);
        L10n.SetLocale(SettingsManager.LoadLocale(), save: false);

        RefreshSettingsControlsFromStorage();
        RefreshAudioControlsFromStorage();
        EmitSignal(SignalName.DesktopBgmPlaybackChanged, _desktopBgmToggle.ButtonPressed);
        _gameData.SetSaveDataMode(SettingsManager.LoadSaveDataMode());
        EmitSignal(SignalName.BlindBoxBubbleVisibilityChanged);
        EmitSignal(SignalName.CounterLayoutChanged);
    }
#endif

    private void RefreshSettingsControlsFromStorage()
    {
        RefreshAudioControlsFromStorage();
        GetNode<CheckButton>("Panel/RootVBox/Scroll/ContentVBox/SettingsContent/AutoHideRow/AutoHideToggle")
            .SetPressedNoSignal(SettingsManager.LoadAutoHidePanel());
        GetNode<CheckButton>("Panel/RootVBox/Scroll/ContentVBox/SettingsContent/TongueImmediateRow/TongueImmediateToggle")
            .SetPressedNoSignal(SettingsManager.LoadDesktopTongueImmediateMode());
        _blindBoxBubbleToggle.SetPressedNoSignal(SettingsManager.LoadAlwaysShowBlindBoxBubble());
        GetNode<CheckButton>("Panel/RootVBox/Scroll/ContentVBox/SettingsContent/ShowFullscreenRow/ShowFullscreenToggle")
            .SetPressedNoSignal(SettingsManager.LoadShowOverFullscreenApps());
        GetNode<CheckButton>("Panel/RootVBox/Scroll/ContentVBox/SettingsContent/EnhancedTopmostRow/EnhancedTopmostToggle")
            .SetPressedNoSignal(SettingsManager.LoadEnhancedTopmostMode());
        GetNode<CheckButton>("Panel/RootVBox/Scroll/ContentVBox/SettingsContent/ProactiveInteractionHintsRow/ProactiveInteractionHintsToggle")
            .SetPressedNoSignal(SettingsManager.LoadProactiveInteractionHints());
        _pokerGuideOverlayToggle.SetPressedNoSignal(SettingsManager.LoadPokerGuideOverlayEnabled());
        GetNode<CheckButton>("Panel/RootVBox/Scroll/ContentVBox/SettingsContent/AvoidObscuringDogEyesRow/AvoidObscuringDogEyesToggle")
            .SetPressedNoSignal(SettingsManager.LoadAvoidObscuringDogEyes());
        _rightClickQuickModeSwitchToggle.SetPressedNoSignal(SettingsManager.LoadRightClickQuickModeSwitch());
        _autoEquipToggle.SetPressedNoSignal(SettingsManager.LoadAutoEquipNewOutfits());
        _taskbarSnapToggle.SetPressedNoSignal(SettingsManager.LoadSnapToWindowsTaskbar());
        GetNode<CheckButton>("Panel/RootVBox/Scroll/ContentVBox/SettingsContent/StreamerSafeRow/StreamerSafeToggle")
            .SetPressedNoSignal(SettingsManager.LoadStreamerSafeMode());
        GetNode<CheckButton>("Panel/RootVBox/Scroll/ContentVBox/SettingsContent/CounterCenterRow/CounterCenterToggle")
            .SetPressedNoSignal(SettingsManager.LoadCenterCounterOnTaskbar());
        GetNode<CheckButton>("Panel/RootVBox/Scroll/ContentVBox/SettingsContent/AutoHideCounterRow/AutoHideCounterToggle")
            .SetPressedNoSignal(SettingsManager.LoadAutoHideCounter());
        BuildPokerFrameRateOptions();
        _vsyncToggle.SetPressedNoSignal(SettingsManager.LoadVsyncEnabled());

        BuildLanguageOptions();
        BuildDisplayOptions();
#if DEBUG
        _saveDataModeOption.Select((int)SettingsManager.LoadSaveDataMode());
        _showDeveloperLauncherNextStartupToggle.SetPressedNoSignal(
            SettingsManager.LoadShowDeveloperLauncherOnStartup());
#endif
        RefreshLocalizedOptionText();
    }

    private void OnPokerGuideOverlayEnabledChanged(bool enabled)
    {
        _pokerGuideOverlayToggle?.SetPressedNoSignal(enabled);
    }

    private void RefreshAudioControlsFromStorage()
    {
        var sfxVolume = SettingsManager.LoadSfxVolume();
        var bgmVolume = SettingsManager.LoadBgmVolume();
        _sfxVolumeSlider.SetValueNoSignal(sfxVolume);
        _bgmVolumeSlider.SetValueNoSignal(bgmVolume);
        _desktopBgmToggle.SetPressedNoSignal(SettingsManager.LoadPlayBgmInDesktop());
        RefreshVolumeLabel(_sfxVolumeValueLabel, sfxVolume);
        RefreshVolumeLabel(_bgmVolumeValueLabel, bgmVolume);
        AudioManager.Instance.SetSfxVolume(sfxVolume);
        AudioManager.Instance.SetBgmVolume(bgmVolume);
    }

    private static void RefreshVolumeLabel(Label label, float volume)
    {
        label.Text = $"{Mathf.RoundToInt(Mathf.Clamp(volume, 0f, 1f) * 100f)}%";
    }
}
