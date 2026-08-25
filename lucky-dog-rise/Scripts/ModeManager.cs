using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using DataTables;
using Godot;

namespace LuckyDogRise;

public partial class ModeManager : Control
{
#if DEBUG
    private const string AccountIdentityProbeArgument = "--account-identity-probe";
    private const string SingleInstanceSmokeArgument = "--single-instance-smoke";
    private const string BlindBoxRegressionSmokeArgument = "--blindbox-regression-smoke";
#endif
    private enum StartupState
    {
        HiddenBootstrap,
        AccountIdentityWaiting,
        LocalInitializing,
        PlatformWaiting,
        ReadyToReveal,
        IntroPlaying,
        Interactive,
        Failed,
    }

    private const double StartupPlatformWaitSeconds = 10.0;
    private const double BlindBoxLoadingMinimumSeconds = 0.5;
    private static readonly Vector2I StartupAccountGateWindowSize = new(900, 560);
    public enum Mode { BossKey, Play, Immersive }
    public Mode CurrentMode { get; private set; } = Mode.BossKey;

    private SystemPanelController _settingsPanel = null!;
    private GlobalInputTracker _globalInputTracker = null!;
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
    private float _bossStatusPanelBaseAntiAliasingSize = 1f;
    private bool _bossStatusBarInteractable = true;
    private bool _bossStatusPanelBaseVisible = true;
    private bool _bossCounterAutoHidden;
    private double _bossCounterAutoHideRemainingSeconds;
    private bool _bossRiseIntroSuppressesBlindBoxHint;
    private GameManager _gameManager = null!;
    private Label _mainText = null!;
    private Font _bossCounterSourceFont = null!;
    private readonly Dictionary<int, Font> _bossCounterFontsByOversampling = new();
    private string _lastCounterPersonaName = string.Empty;
    private Vector2 _windowBaseSize;
    private Vector2 _windowBaseDesignSize;
    private Vector2 _panelSize;
    private Vector2 _contentOffset;
    private Vector2 _bossContentOffset;
    private readonly IPanelAvoidanceStrategy _panelAvoidanceStrategy = new LegacyRealtimeGridStrategy();
    private Node2D _bossContentA = null!;
    private CanvasLayer _bossCanvasLayer = null!;
    private CanvasLayer _bossBubbleLayer = null!;
    private int _desktopPetScaleStep = SettingsManager.DefaultDesktopPetScaleStep;
    private float _desktopPetScaleFactor = 1f;
    private bool _desktopPetScalePreviewActive;
    private int _desktopPetScalePreviewOriginalStep;
    private Vector2I _desktopPetScalePreviewOriginalWindowPosition;
    private bool _desktopPetScalePreviewOriginalTaskbarSnapped;
    private int _otherUiScaleStep = SettingsManager.DefaultOtherUiScaleStep;
    private float _otherUiScaleFactor = 1f;
    private bool _otherUiScalePreviewActive;
    private int _otherUiScalePreviewOriginalStep;
    private Vector2I _otherUiScalePreviewOriginalWindowPosition;
    // 延迟到桌宠已绘制一帧后才移动窗口；用于让过期的延迟移动失效。
    private int _modeSwitchRevision;

    private bool _isDragging, _potentialDrag, _isClickThrough = true;
    private Vector2I _mouseScreenStart, _windowPosStart;
    private ulong _dragPressStartedAtMsec;
    private const float DefaultDragThreshold = 5f;
    private const float ProtectedPlayDragThreshold = 6f;
    private const ulong ProtectedPlayDragHoldDelayMsec = 70;
    private const int PlayInfoPanelBaseWidth = 246;
    private const int PlayGameBaseSize = 600;
    private const int PlayGameSettingsGap = 0;

    private bool _taskbarSnapped;
    private const int SnapThreshold = 15;
    private const int BreakawayThreshold = 30;
    private bool _bossWorkAreaSnapshotReady;
    private int _bossWorkAreaScreen = -1;
    private Rect2I _bossLastScreenRect;
    private Rect2I _bossLastUsableRect;
    private Vector2I _bossLastTaskbarAnchorScreenPosition;

    private Rect2 _dogHitRect;
    private static readonly Rect2 BossDogHitRectDesign = new(60, 90, 180, 180);
    private Texture2D _blindBoxIcon = null!;
    private bool _blindBoxOpeningUiActive;
    private double _blindBoxOpeningUiElapsedSeconds;
    private double _blindBoxOpeningUiMinimumSeconds;
    private PendingBlindBoxReward _blindBoxOpeningResolvedReward = null!;
#if DEBUG
    private SteamMockPanelController _steamMockPanel = null!;
    private IDebugSteamMockController _steamMockController = null!;
    private DeveloperLauncherController _developerLauncher = null!;
    private bool _steamMockPanelRequestedVisible;
    private bool _lastSteamMockActive;
#endif

    private GameData _gameData = null!;
    private IGamePlatformService _platformService = null!;
    private PlatformAchievementSynchronizer _achievementSynchronizer = null!;
    private AccountStorageContext _storageContext = null!;
    private AccountStateManager _accountStateManager = null!;
    private StartupAccountGateController _startupAccountGate = null!;
    private bool _startInSteamMock;
    private bool _shutdownRequested;
    private bool _platformDisposed;
    private bool _duplicateLaunch;
    private bool _startupInitialized;
    private StartupState _startupState = StartupState.HiddenBootstrap;
    private double _startupPlatformWaitRemaining;
    private bool _startupFocusRequested;
    private Rect2I _startupScreen;
    private bool _startupUseInitialMeetingPosition;
    private int _pokerFrameRate = 60;
    private double _pokerRenderAccumulator;
    private bool _pokerViewportWasActive;
    public GameData GameDataObj => _gameData;
    public IGamePlatformService PlatformService => _platformService;

#if DEBUG
    private static readonly EItemType[] DebugGrantItemTypes = Enum.GetValues<EItemType>()
        .Where(type => type != EItemType.Dog)
        .ToArray();
    private const int DebugEmptyEquipmentWeight = 3;

    private enum DebugEquipmentSource
    {
        AllCatalog,
        Owned,
    }
#endif

#if DEBUG
    private readonly Random _debugRandom = new();
    private readonly Dictionary<(DebugEquipmentSource source, EItemType type), ShuffleBag<int>> _debugEquipmentBags = new();
    private int _debugGrantItemTypeIndex;
#endif
    private readonly Queue<(double time, int count)> _desktopInputEvents = new();
    private const double DesktopActivitySampleSeconds = 10.0;
    private DesktopActivityState _currentDesktopActivityState;
    private DesktopActivityState _candidateDesktopActivityState;
    private double _candidateDesktopActivitySeconds;
    private double _desktopActivityCooldownSeconds;
    private bool _desktopTongueFeedbackEnabled = true;
    private double _fullscreenCheckTimer;
    private bool _hiddenByFullscreenApp;
    private SingleInstanceGuard _singleInstanceGuard;
    private bool _activationRequestedBeforeReady;
    private double _enhancedTopmostTimer;
    private double _enhancedTopmostBoostTimer;
    private double _enhancedTopmostDelayedBoostTimer;
    private bool _waitingForWinMenuDismiss;
    private double _recoverTopmostOnNextMousePressTimer;
    private double _settingsPanelOpenedAtSeconds;
    private const double RecoverTopmostOnNextMousePressSeconds = 5.0;
    private const double SettingsPanelAutoHideOpenGraceSeconds = 0.2;
    private const double BossCounterAutoHideDelaySeconds = 1.0;
    private const float BossCounterTongueClearance = 2f;
    private const float BossCounterMinimumHeight = 22f;

    public override void _EnterTree()
    {
        GetTree().AutoAcceptQuit = false;
        SetNativeMainWindowVisible(false);
        _startupState = StartupState.LocalInitializing;
        _singleInstanceGuard = GetNodeOrNull<SingleInstanceGuard>("/root/SingleInstanceGuard");
        if (_singleInstanceGuard is { IsPrimaryInstance: false })
        {
            _duplicateLaunch = true;
            SetProcess(false);
            return;
        }
        DiagnosticLog.Initialize();
        if (_singleInstanceGuard != null)
            _singleInstanceGuard.ActivationRequested += OnExternalActivationRequested;
    }

    public override void _Ready()
    {
        if (_duplicateLaunch)
        {
            HandleDuplicateLaunch();
            return;
        }

        SettingsManager.ApplyDisplayPerformanceSettings();

        if (!BuildInfo.ValidateCurrentBuild())
        {
            OS.Alert(BuildInfo.ValidationError, "Lucky Dog Rise Playtest");
            GetTree().Quit(2);
            return;
        }

        _pokerFrameRate = SettingsManager.LoadPokerFrameRate();
        SettingsManager.PokerFrameRateChanged += OnPokerFrameRateChanged;
        SettingsManager.AutoHideCounterChanged += OnAutoHideCounterChanged;

        L10n.ApplySavedOrSystemLocale();
#if DEBUG
        if (OS.GetCmdlineUserArgs().Any(argument =>
                string.Equals(argument, BlindBoxRegressionSmokeArgument, StringComparison.OrdinalIgnoreCase)))
        {
            try
            {
                BlindBoxRegressionSmoke.Run();
                GetTree().Quit();
            }
            catch (Exception exception)
            {
                GD.PushError($"[BlindBoxRegressionSmoke] Failed: {exception}");
                GetTree().Quit(1);
            }
            return;
        }

        var forceLauncher = OS.GetCmdlineUserArgs().Any(argument =>
            string.Equals(argument, "--dev-launcher", StringComparison.OrdinalIgnoreCase));
        var automatedSmoke = OS.GetCmdlineUserArgs().Any(argument =>
            string.Equals(argument, "--diagnostics-export-smoke", StringComparison.OrdinalIgnoreCase)
            || string.Equals(argument, AccountIdentityProbeArgument, StringComparison.OrdinalIgnoreCase)
            || string.Equals(argument, SingleInstanceSmokeArgument, StringComparison.OrdinalIgnoreCase)
            || string.Equals(argument, "--identity-unavailable-smoke", StringComparison.OrdinalIgnoreCase));
        var launcherRequestedBySetting = SettingsManager.LoadShowDeveloperLauncherOnStartup();
        if (!automatedSmoke && (forceLauncher || launcherRequestedBySetting))
        {
            ShowDeveloperLauncher();
            return;
        }

        ContinueStartup(new DebugLaunchSelection(
            DebugRuntimeEnvironment.IntegratedDebug,
            DebugSteamScenario.NormalSuccess));
#else
        ContinueStartup();
#endif
    }

#if DEBUG
    private void ShowDeveloperLauncher()
    {
        _developerLauncher = GD.Load<PackedScene>("res://Scenes/Debug/DeveloperLauncher.tscn")
            .Instantiate<DeveloperLauncherController>();
        _developerLauncher.Name = "DeveloperLauncher";
        _developerLauncher.LaunchRequested += OnDeveloperLaunchRequested;
        AddChild(_developerLauncher);

        var launcherSize = new Vector2I(620, 430);
        DisplayServer.WindowSetSize(launcherSize);
        var screen = DisplayServer.WindowGetCurrentScreen();
        var usable = DisplayServer.ScreenGetUsableRect(screen);
        DisplayServer.WindowSetPosition(usable.Position + (usable.Size - launcherSize) / 2);
        SetClickThrough(false);
        SetNativeMainWindowVisible(true);
        _startupState = StartupState.HiddenBootstrap;
        GD.Print("[Startup] Developer launcher is waiting for a runtime environment selection.");
    }

    private void OnDeveloperLaunchRequested(int environment, int scenario)
    {
        var selection = new DebugLaunchSelection(
            (DebugRuntimeEnvironment)environment,
            (DebugSteamScenario)scenario);
        _developerLauncher.LaunchRequested -= OnDeveloperLaunchRequested;
        _developerLauncher.QueueFree();
        _developerLauncher = null!;
        SetNativeMainWindowVisible(false);
        ContinueStartup(selection);
    }

    private void ContinueStartup(DebugLaunchSelection selection)
    {
        _platformService = GamePlatformServiceFactory.Create(selection);
        StartAccountAwareStartup(selection.Environment == DebugRuntimeEnvironment.SteamMock);
    }
#else
    private void ContinueStartup()
    {
        _platformService = GamePlatformServiceFactory.Create();
        StartAccountAwareStartup(startInSteamMock: false);
    }
#endif

    private void StartAccountAwareStartup(bool startInSteamMock)
    {
        _startInSteamMock = startInSteamMock;
        if (_platformService is IRecoverablePlatformService recoverable)
            recoverable.AccountIdentityConflictDetected += OnAccountIdentityConflictDetected;

        if (TryResolveAccountStorage(out var storageContext))
        {
#if DEBUG
            if (TryCompleteAccountIdentityProbe(storageContext))
                return;
#endif
            AuthorizeDeveloperAccountAndCompleteStartup(startInSteamMock, storageContext);
            return;
        }

        ShowAccountIdentityGate();
    }

    private void AuthorizeDeveloperAccountAndCompleteStartup(
        bool startInSteamMock,
        AccountStorageContext storageContext)
    {
#if DEBUG
        if (!startInSteamMock
            && string.Equals(storageContext.Provider, "steam", StringComparison.Ordinal))
        {
            var access = DeveloperSteamAccountAllowlist.Check(storageContext.AccountId);
            if (!access.Allowed)
            {
                ShowDeveloperAccountDenied(storageContext, access);
                return;
            }

            GD.Print(
                $"[DeveloperAccountAllowlist] Authorized SteamID64={storageContext.AccountId}, Note={access.Note}");
        }
#endif
        CompleteStartup(startInSteamMock, storageContext);
    }

    private void CompleteStartup(bool startInSteamMock, AccountStorageContext storageContext)
    {
        _storageContext = storageContext;
        _singleInstanceGuard?.PublishAccountIdentity(storageContext.Provider, storageContext.AccountId);
        if (!LegacyAccountStorageMigration.Prepare(storageContext))
        {
            OS.Alert(
                "The explicit local account migration did not complete. No player data will be loaded in this launch. " +
                "Review the AccountMigration log and retry after resolving the cause.",
                "Account Migration Failed");
            QuitBeforeAccountStartup();
            return;
        }
        if (_startupAccountGate != null)
        {
            _startupAccountGate.QueueFree();
            _startupAccountGate = null!;
            SetNativeMainWindowVisible(false);
        }
        GD.Print(_platformService.IsAvailable
            ? $"[Platform] {_platformService.ProviderName} ready. AppID={_platformService.AppId}, Persona={_platformService.PersonaName}, Account={storageContext}"
            : string.Equals(storageContext.Provider, "steam", StringComparison.Ordinal)
                ? $"[Platform] Steam recovery active. {_platformService.StatusMessage}"
                : $"[Platform] Offline fallback. {_platformService.StatusMessage}");
        DiagnosticLog.Record("platform_service_created", new Dictionary<string, object>
        {
            ["provider"] = _platformService.ProviderName,
            ["available"] = _platformService.IsAvailable,
            ["state"] = (_platformService as IRecoverablePlatformService)?.ConnectionState.ToString(),
            ["accountProvider"] = storageContext.Provider,
            ["accountId"] = storageContext.AccountId,
#if DEBUG
            ["debugEnvironment"] = startInSteamMock ? "SteamMock" : "IntegratedDebug",
#endif
        });

        _accountStateManager = new AccountStateManager(storageContext);
        var initialMeetingState = _accountStateManager.LoadInitialMeetingStateForStartup();

        _gameData = new GameData();
        _gameData.Name = "GameData";
        _gameData.ConfigureStorage(storageContext);
#if DEBUG
        _gameData.StartInSteamMockSimulation = startInSteamMock;
#endif
        AddChild(_gameData);
        _gameData.BindPlatformInventoryService(_platformService);
        _achievementSynchronizer = new PlatformAchievementSynchronizer(_platformService, _gameData.PlayerProgress);

        _bossKeyContent = GD.Load<PackedScene>("res://Scenes/BossKeyContent.tscn").Instantiate<Node2D>();
        _bossKeyContent.Name = "BossKeyContent";
        AddChild(_bossKeyContent);
        _bossContentA = _bossKeyContent.GetNode<Node2D>("ContentA");
        _bossCanvasLayer = _bossKeyContent.GetNode<CanvasLayer>("CanvasLayer");
        _bossBubbleLayer = _bossKeyContent.GetNode<CanvasLayer>("Bubble");
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
        _bossCounterSourceFont = _mainText.GetThemeFont("font");
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
        _otherUiScaleStep = SettingsManager.LoadOtherUiScaleStep();
        _otherUiScaleFactor = SettingsManager.GetOtherUiScaleFactor(_otherUiScaleStep);
        GetViewport().OversamplingOverride = Mathf.Max(1f, _otherUiScaleFactor);
        _settingsPanel.SetRenderScale(_otherUiScaleFactor);
        _settingsPanel.GameData = _gameData;
        _settingsPanel.PlatformService = _platformService;
        _settingsPanel.SwitchToPlayRequested += SwitchToPlay;
        _settingsPanel.SwitchToBossKeyRequested += SwitchToBossKey;
        _settingsPanel.QuitRequested += RequestGracefulQuit;
        _settingsPanel.DesktopBgmPlaybackChanged += OnDesktopBgmPlaybackChanged;
#if DEBUG
        _settingsPanel.RandomizeRequested += OnRandomizeScene;
        _settingsPanel.RandomizeDogRequested += OnRandomizeDog;
        _settingsPanel.RandomAcquireItemRequested += OnRandomAcquireItem;
        _settingsPanel.DebugGrantChipsRequested += OnDebugGrantChips;
        _settingsPanel.DebugGrantLuckyDealsRequested += OnDebugGrantLuckyDeals;
        _settingsPanel.DogReactionRequested += OnDogReactionRequested;
        _settingsPanel.GlobalMouseListeningDisabledChanged += OnGlobalMouseListeningDisabledChanged;
        _settingsPanel.SteamMockPanelVisibilityChanged += OnSteamMockPanelVisibilityChanged;
        _steamMockPanelRequestedVisible = startInSteamMock;
        _settingsPanel.SetSteamMockPanelToggle(_steamMockPanelRequestedVisible);
#endif
        _settingsPanel.BlindBoxBubbleVisibilityChanged += OnBlindBoxBubbleVisibilityChanged;
        _settingsPanel.CounterLayoutChanged += ApplyBossCounterLayout;
        _settingsPanel.DesktopPetScalePreviewRequested += OnDesktopPetScalePreviewRequested;
        _settingsPanel.DesktopPetScaleConfirmed += OnDesktopPetScaleConfirmed;
        _settingsPanel.DesktopPetScaleCanceled += OnDesktopPetScaleCanceled;
        _settingsPanel.OtherUiScalePreviewRequested += OnOtherUiScalePreviewRequested;
        _settingsPanel.OtherUiScaleConfirmed += OnOtherUiScaleConfirmed;
        _settingsPanel.OtherUiScaleCanceled += OnOtherUiScaleCanceled;
        RefreshSettingsPanelModeActions();

        if (OS.GetCmdlineUserArgs().Contains("--diagnostics-export-smoke"))
        {
            RunDiagnosticsExportSmoke();
            return;
        }

        _panelSize = _settingsPanel.PanelSize;
        _contentOffset = _panelSize;
#if DEBUG
        _steamMockController = _platformService as IDebugSteamMockController;
        if (_steamMockController != null)
        {
            _steamMockPanel = GD.Load<PackedScene>("res://Scenes/Debug/SteamMockPanel.tscn")
                .Instantiate<SteamMockPanelController>();
            _steamMockPanel.Name = "SteamMockPanel";
            AddChild(_steamMockPanel);
            _steamMockPanel.Bind(_platformService, _gameData);
            _steamMockPanel.SetPanelBottom(_contentOffset.Y);
            _steamMockPanel.CloseRequested += OnSteamMockPanelCloseRequested;
            _steamMockPanel.SimulationReset += OnSteamMockSimulationReset;
        }
#endif

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

        _windowBaseDesignSize = _bossKeyContent.GetNode<Marker2D>("ContentA/WindowSize").Position;
        _desktopPetScaleStep = SettingsManager.LoadDesktopPetScaleStep();
        ApplyDesktopPetScaleGeometry(_desktopPetScaleStep);
        ConfigureBossRiseIntro();
        UpdateBossBlindBoxOverlayPosition();
        SetupFatWindow();
        if (initialMeetingState == AccountStateManager.TutorialStepState.NotStarted)
        {
            // A：初次见面，保持当前居中出现的位置，为后续右侧新手引导预留空间。
            SetWindowAboveTaskbar();
            _startupUseInitialMeetingPosition = true;
            _accountStateManager.SaveTutorialStepState(
                AccountStateManager.InitialMeetingTutorialId,
                AccountStateManager.TutorialStepState.Shown);
        }
        else
        {
            // B：非初次见面的启动位置，与 C：从扑克切回桌宠使用同一套右侧面板预留公式。
            _startupScreen = GetBestScreenUsableRect(new Rect2I(
                DisplayServer.WindowGetPosition(),
                new Vector2I((int)_windowBaseSize.X, (int)_windowBaseSize.Y)));
        }
        HideBossKeyContent();
        DisplayServer.WindowSetPosition(DisplayServer.WindowGetPosition());
        EnableLayeredWindow();

        _bossCanvasLayer.Offset = _bossContentOffset;
        ApplyBossCounterLayout();
        _bossBubbleLayer.Offset = _bossContentOffset;
        _bossBubbleLayer.Visible = false;
        RestoreBossBlindBoxRewardIfNeeded();
        CallDeferred(MethodName.RestoreBossBlindBoxRewardIfNeeded);

        _globalInputTracker = new GlobalInputTracker();
        _globalInputTracker.Name = "GlobalInputTracker";
        _globalInputTracker.GameData = _gameData;
        _globalInputTracker.TypingInputOccurred += OnTypingInputOccurred;
        _globalInputTracker.GlobalMousePressed += OnGlobalMousePressed;
        _globalInputTracker.GlobalWinKeyPressed += OnGlobalWinKeyPressed;
        _globalInputTracker.GlobalEscapeKeyPressed += OnGlobalEscapeKeyPressed;
        AddChild(_globalInputTracker);

        _startupPlatformWaitRemaining = StartupPlatformWaitSeconds;
        _startupState = _platformService is IRecoverablePlatformService recoverable
            && recoverable.ConnectionState != PlatformConnectionState.Ready
                ? StartupState.PlatformWaiting
                : StartupState.ReadyToReveal;
        _startupInitialized = true;
    }

    private bool TryResolveAccountStorage(out AccountStorageContext storageContext)
    {
        storageContext = null!;
        if (_platformService == null
            || string.IsNullOrWhiteSpace(_platformService.AccountProvider)
            || string.IsNullOrWhiteSpace(_platformService.AccountId))
            return false;

        try
        {
            storageContext = string.Equals(_platformService.AccountProvider, "steam", StringComparison.Ordinal)
                ? AccountStorageContext.ForSteam(_platformService.AccountId)
                : (BuildInfo.IsDevelopment || OS.GetCmdlineUserArgs().Contains("--diagnostics-export-smoke"))
                  && string.Equals(_platformService.AccountProvider, "dev", StringComparison.Ordinal)
                    ? AccountStorageContext.ForDevelopment(_platformService.AccountId)
                    : null!;
            return storageContext != null;
        }
        catch (Exception exception)
        {
            GD.PushError($"[AccountStorage] Invalid platform identity: {exception.Message}");
            return false;
        }
    }

#if DEBUG
    private bool TryCompleteAccountIdentityProbe(AccountStorageContext storageContext)
    {
        if (!OS.GetCmdlineUserArgs().Any(argument =>
                string.Equals(argument, AccountIdentityProbeArgument, StringComparison.OrdinalIgnoreCase)))
            return false;

        GD.Print($"[AccountIdentityProbe] Provider={storageContext.Provider}, AccountId={storageContext.AccountId}");
        _singleInstanceGuard?.PublishAccountIdentity(storageContext.Provider, storageContext.AccountId);
        DisposePlatformService();
        GetTree().Quit();
        return true;
    }
#endif

    private void ShowAccountIdentityGate()
    {
        _startupAccountGate = GD.Load<PackedScene>("res://Scenes/StartupAccountGate.tscn")
            .Instantiate<StartupAccountGateController>();
        _startupAccountGate.Name = "StartupAccountGate";
        _startupAccountGate.RetryRequested += OnAccountIdentityRetryRequested;
        _startupAccountGate.QuitRequested += QuitBeforeAccountStartup;
        AddChild(_startupAccountGate);
        SetNativeMainWindowVisible(false);
        ApplyStartupAccountGateWindowLayout();
        SetClickThrough(false);
        _startupAccountGate.SetStatus(
            $"Steam account verification is required before loading player data.\nStart Steam and sign in, then click \"Retry\". The game may close and relaunch through Steam.\nNo player save has been loaded or modified.\n\nStatus: {GetStartupPlatformStatusMessage()}",
            retryEnabled: true);
        _startupState = StartupState.AccountIdentityWaiting;
        _startupInitialized = true;
        RevealStartupAccountGateAfterLayout();
    }

#if DEBUG
    private void ShowDeveloperAccountDenied(
        AccountStorageContext storageContext,
        DeveloperSteamAccountAccessResult access)
    {
        var persona = string.IsNullOrWhiteSpace(_platformService.PersonaName)
            ? "Unknown"
            : _platformService.PersonaName;
        var message = access.ConfigurationValid
            ? $"当前 Steam 帐号未获准用于编辑器开发版本。\n\n" +
              $"帐号昵称：{persona}\nSteamID64：{storageContext.AccountId}\n\n" +
              $"如果这是获准的开发帐号，请将该 SteamID64 添加到 Build/Developer/steam-account-allowlist.json。\n" +
              $"本次启动未读取或修改玩家存档。"
            : $"开发用 Steam 帐号白名单配置无效。\n\n" +
              $"{access.ErrorMessage}\n\n" +
              $"帐号昵称：{persona}\nSteamID64：{storageContext.AccountId}\n\n" +
              $"本次启动未读取或修改玩家存档。";

        GD.PushWarning($"[DeveloperAccountAllowlist] Access denied. Persona={persona}, SteamID64={storageContext.AccountId}, ConfigurationValid={access.ConfigurationValid}, Error={access.ErrorMessage}");
        DiagnosticLog.Record("developer_steam_account_access_denied", new Dictionary<string, object>
        {
            ["persona"] = persona,
            ["steamId64"] = storageContext.AccountId,
            ["configurationValid"] = access.ConfigurationValid,
            ["configurationError"] = access.ErrorMessage,
        });
        OS.Alert(
            message,
            access.ConfigurationValid
                ? "Steam 帐号未获准"
                : "Steam 帐号白名单配置错误");
        QuitBeforeAccountStartup();
    }
#endif

    private void OnAccountIdentityRetryRequested()
    {
        _startupAccountGate?.SetStatus(
            $"Checking your Steam account...\nIf necessary, the game will close and relaunch through Steam.\nNo player save has been loaded or modified.\n\nStatus: {GetStartupPlatformStatusMessage()}",
            retryEnabled: false);

        if (_platformService is not IRecoverablePlatformService recoverable)
            return;

        recoverable.RequestReconnect();
        if (TryResolveAccountStorage(out var storageContext))
        {
#if DEBUG
            if (TryCompleteAccountIdentityProbe(storageContext))
                return;
#endif
            AuthorizeDeveloperAccountAndCompleteStartup(_startInSteamMock, storageContext);
            return;
        }

        if (recoverable.CanRequestClientRelaunch)
        {
            SingleInstanceGuard.ReleaseForRestart();
            if (recoverable.TryRequestClientRelaunch(out var relaunchMessage))
            {
                GD.Print($"[Startup] {relaunchMessage}");
                DiagnosticLog.Record("startup_steam_client_relaunch_requested", new Dictionary<string, object>
                {
                    ["appId"] = BuildInfo.ExpectedSteamAppId,
                });
                _startupAccountGate?.SetStatus(
                    "Steam is relaunching Lucky Dog Rise. This window will close automatically.\nNo player save has been loaded or modified.",
                    retryEnabled: false);
                QuitBeforeAccountStartup();
                return;
            }

            GD.PushWarning($"[Startup] {relaunchMessage}");

            if (!SingleInstanceGuard.ReacquireAfterFailedRestart(markInteractive: false))
            {
                GD.PushError("[Startup] Failed to reacquire the single-instance guard after Steam declined the relaunch request.");
                QuitBeforeAccountStartup();
                return;
            }
        }

        _startupAccountGate?.SetStatus(
            $"Steam account verification is still unavailable.\nMake sure Steam is fully running and signed in, then click \"Retry\" again.\nNo player save has been loaded or modified.\n\nStatus: {GetStartupPlatformStatusMessage()}",
            retryEnabled: true);
    }

    private void ApplyStartupAccountGateWindowLayout()
    {
        DisplayServer.WindowSetSize(StartupAccountGateWindowSize);
        var usable = DisplayServer.ScreenGetUsableRect(DisplayServer.WindowGetCurrentScreen());
        DisplayServer.WindowSetPosition(
            usable.Position + (usable.Size - StartupAccountGateWindowSize) / 2);
        _startupAccountGate?.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
    }

    private async void RevealStartupAccountGateAfterLayout()
    {
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        if (_startupState != StartupState.AccountIdentityWaiting || _startupAccountGate == null)
            return;

        ApplyStartupAccountGateWindowLayout();
        SetNativeMainWindowVisible(true);
    }

    private string GetStartupPlatformStatusMessage()
    {
        var status = _platformService?.StatusMessage ?? string.Empty;
        return status switch
        {
            "Steam 尚未连接。" => "Steam is not connected.",
            "Steam 客户端未运行，未执行 SteamAPI.Init()。" => "The Steam client is not running.",
            "SteamAPI.Init() 返回 false。Steam 已运行，但当前 AppID 尚未被客户端接受。" =>
                "SteamAPI.Init() failed. Steam is running, but the current AppID was not accepted by the client.",
            "Steamworks 初始化成功。" => "Steamworks initialized successfully.",
            _ when status.StartsWith("Steamworks 初始化异常：", StringComparison.Ordinal) =>
                $"Steamworks initialization error: {status["Steamworks 初始化异常：".Length..]}",
            _ when status.StartsWith("Steam 回调异常：", StringComparison.Ordinal) =>
                $"Steam callback error: {status["Steam 回调异常：".Length..]}",
            _ when status.StartsWith("Steam 连接已中断", StringComparison.Ordinal) => "The Steam connection was interrupted.",
            _ when string.IsNullOrWhiteSpace(status) => "No additional status information is available.",
            _ when status.Contains('：', StringComparison.Ordinal) => "Steam reported a connection error.",
            _ => status
        };
    }

    private void QuitBeforeAccountStartup()
    {
        _shutdownRequested = true;
        _singleInstanceGuard?.BeginShutdown();
        DisposePlatformService();
        GetTree().Quit();
    }

    private double _displayTimer;
    private SettingsManager.DisplayMode _lastMode = (SettingsManager.DisplayMode)(-1);

    public override void _Process(double _)
    {
        if (!_startupInitialized)
            return;
        _platformService?.RunCallbacks();
        _achievementSynchronizer?.Tick(_);
        UpdateStartup(_);
        UpdateBlindBoxOpeningUi(_);
#if DEBUG
        UpdateSteamMockPresentation();
#endif

        if (_startupState is not (StartupState.IntroPlaying or StartupState.Interactive))
            return;

        if (CurrentMode == Mode.BossKey)
            _gameData?.RecordDesktopModeSeconds(_, visible: !_hiddenByFullscreenApp);
        else if (CurrentMode == Mode.Play || CurrentMode == Mode.Immersive)
            _gameData?.RecordPokerModeSeconds(_);

        UpdateFullscreenVisibility(_);
        UpdatePokerViewportRendering(_);
        UpdateEnhancedTopmost(_);
        UpdateDesktopActivityState(_);
        UpdateBossWorkAreaTracking();

        if (_hiddenByFullscreenApp)
            return;

        var mode = SettingsManager.CurrentDisplayMode;
        var personaName = _platformService?.PersonaName ?? string.Empty;
        if (mode != _lastMode
            || mode == SettingsManager.DisplayMode.Nickname
                && !string.Equals(personaName, _lastCounterPersonaName, StringComparison.Ordinal))
        {
            _lastMode = mode;
            _lastCounterPersonaName = personaName;
            _mainText.Text = mode switch
            {
                SettingsManager.DisplayMode.Clock => DateTime.Now.ToString("HH:mm"),
                SettingsManager.DisplayMode.Nickname => personaName,
                SettingsManager.DisplayMode.Hidden => "",
                _ => "0"
            };
            _mainText.TooltipText = mode == SettingsManager.DisplayMode.Nickname
                ? personaName
                : string.Empty;
        }
        if (mode == SettingsManager.DisplayMode.Clock)
            _mainText.Text = DateTime.Now.ToString("HH:mm");
        else if (mode == SettingsManager.DisplayMode.Chips)
            _mainText.Text = _gameData.Chips.ToString();

        UpdateBossCounterAutoHide(_);
        bool over = IsScreenPointOverInteractiveContent(DisplayServer.MouseGetPosition());

        if (_isClickThrough && over) SetClickThrough(false);
        else if (!_isClickThrough && !over && !_isDragging) SetClickThrough(true);
    }

    public override void _Notification(int what)
    {
        if (_duplicateLaunch)
            return;

        if (what == NotificationWMCloseRequest)
        {
            RequestGracefulQuit();
            return;
        }

        if (what == NotificationApplicationFocusIn)
            (_platformService as IRecoverablePlatformService)?.RequestReconnect();

        if (what == NotificationWMWindowFocusOut && _settingsPanel != null && _settingsPanel.IsOpen
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
    private SubViewport _playSubViewport = null!;
    private InfoPanelController _infoPanel = null!;
    // 游玩模式布局状态：false=信息面板在左(默认), true=信息面板在右
    private bool _infoPanelOnRight;

    private void SwitchToPlay()
    {
        if (CurrentMode == Mode.Play) return;
        _modeSwitchRevision++;
        _settingsPanel.CancelPendingDesktopPetScaleChange();
        _settingsPanel.CancelPendingOtherUiScaleChange();
        if (_settingsPanel.IsOpen) _settingsPanel.CloseImmediate();

        HideBossKeyContent();

        if (_playRoot == null)
        {
            _playRoot = GD.Load<PackedScene>("res://Scenes/PlayContent.tscn").Instantiate<Node2D>();
            _playRoot.Name = "PlayRoot";
            AddChild(_playRoot);
            _playViewport = _playRoot.GetNode<SubViewportContainer>("SubViewportContainer");
            _playSubViewport = _playRoot.GetNode<SubViewport>("SubViewportContainer/SubViewport");
            _playViewport.Scale = Vector2.One * (0.5f * _otherUiScaleFactor);

            // 信息面板由 ModeManager 直接管理（需要动态定位+避让）
            _infoPanel = GD.Load<PackedScene>("res://Scenes/InfoPanel.tscn").Instantiate<InfoPanelController>();
            _infoPanel.Name = "InfoPanel";
            AddChild(_infoPanel);
            _infoPanel.SetRenderScale(_otherUiScaleFactor);
            _infoPanel.SettingsRequested += ToggleSettingsPanel;
            _infoPanel.BlindBoxRequested += OnBlindBoxRequested;

            // 连接 Main 中的 GameManager 信号
            _gameManager = _playRoot.GetNode<GameManager>("SubViewportContainer/SubViewport/Main");
            _gameManager.GameData = _gameData;
            _gameManager.SettingsPanel = _settingsPanel;
            _infoPanel.PaytableRequested += _gameManager.TogglePokerHandShowcase;
            _gameManager.BlindBoxRewardClaimRequested += OnBlindBoxRewardClaimRequested;
            _gameManager.InsufficientBetAttempted += _infoPanel.FlashInsufficientBet;

            // InfoPanel 绑定 GameData
            _infoPanel.Bind(_gameData);
            _infoPanel.SetBlindBoxOpeningLoading(_blindBoxOpeningUiActive);
        }

        // 扑克根节点会在模式切换时复用；每次回来都要以共享进度重建盲盒外壳，
        // 否则它会保留离开扑克模式前的旧盲盒贴图。
        if (_gameData.PendingBlindBoxReward != null && !_blindBoxOpeningUiActive)
            _gameManager.ShowPendingBlindBoxReward(_gameData.PendingBlindBoxReward);

        // 切换游玩模式的胖窗口尺寸（左信息面板 + 视觉缝隙 + 600×600 游戏内容 + 420 缓冲）
        SetupPlayFatWindow();
        KeepPlayContentWithinScreen();
        SetClickThrough(false);
        UpdatePlayLayout();
        _playRoot.Visible = true;
        _infoPanel.Visible = true;
        CurrentMode = Mode.Play;
#if DEBUG
        RefreshSteamMockPanelVisibility();
#endif
        ResetPokerViewportRendering();
        AudioManager.Instance.SetBgmPaused(false);
        _gameManager.SetInteractionHintPokerModeActive(true);
        RefreshSettingsPanelModeActions();
    }

    private void UpdatePlayLayout()
    {
        var scrSize = DisplayServer.ScreenGetSize();
        var winPos = DisplayServer.WindowGetPosition();
        int baseY = (int)_contentOffset.Y;
        const int pad = 5;
        int infoWidth = GetPlayInfoPanelWidth();
        int gameSize = GetPlayGameSize();
        int gameX = infoWidth;

        // 信息面板在左侧（默认）：屏幕范围 winPos.X ~ winPos.X + PlayInfoPanelWidth
        bool leftOk = winPos.X >= -pad && winPos.X + infoWidth <= scrSize.X + pad;

        _infoPanelOnRight = !leftOk;

        // 游戏面板位置固定，信息面板自己绕到右侧
        _playViewport.Position = new Vector2(gameX, baseY);
        _infoPanel.SetPanelPosition(new Vector2(_infoPanelOnRight ? gameX + gameSize + PlayGameSettingsGap : 0, baseY));
    }

    private void SwitchToBossKey()
    {
        if (CurrentMode == Mode.BossKey) return;
        int switchRevision = ++_modeSwitchRevision;
        var playScreen = GetBestScreenUsableRect(GetPlayGameScreenRect());
        CancelWindowDrag();
        _settingsPanel.CancelPendingDesktopPetScaleChange();
        _settingsPanel.CancelPendingOtherUiScaleChange();
        if (_settingsPanel.IsOpen) _settingsPanel.CloseImmediate();

        // DWM Cloak 会让窗口对玩家不可见，但仍允许 Godot 在后台继续 resize、移动和绘制。
        // 因此窗口移动时不会再携带合成器中残留的扑克画面。
        bool windowCloaked = SetNativeWindowCloaked(true);

        if (_playRoot != null)
            _playRoot.Visible = false;
        if (_infoPanel != null)
            _infoPanel.Visible = false;
        _gameManager?.SetInteractionHintPokerModeActive(false);
        AudioManager.Instance.SetBgmPaused(!SettingsManager.LoadPlayBgmInDesktop());

        // 先把扑克和桌宠都隐藏，进入透明交接；移动过程不会带着扑克矩形。
        HideBossKeyContent();
        SetupFatWindow();
        SetClickThrough(true);
        CurrentMode = Mode.BossKey;
#if DEBUG
        RefreshSteamMockPanelVisibility();
#endif
        ResetPokerViewportRendering();
        RefreshSettingsPanelModeActions();

        RevealBossKeyAfterTransparentHandoff(playScreen, switchRevision, windowCloaked);
    }

    public override void _ExitTree()
    {
        if (_singleInstanceGuard != null)
            _singleInstanceGuard.ActivationRequested -= OnExternalActivationRequested;
        SettingsManager.PokerFrameRateChanged -= OnPokerFrameRateChanged;
        SettingsManager.AutoHideCounterChanged -= OnAutoHideCounterChanged;
        DisposePlatformService();
    }

    private void HandleDuplicateLaunch()
    {
#if DEBUG
        _platformService = GamePlatformServiceFactory.Create(new DebugLaunchSelection(
            DebugRuntimeEnvironment.IntegratedDebug,
            DebugSteamScenario.NormalSuccess));
#else
        _platformService = GamePlatformServiceFactory.Create();
#endif
        var existingAccountId = string.Empty;
        var result = _singleInstanceGuard == null
            ? SingleInstanceGuard.DuplicateLaunchResult.ExistingUnresponsive
            : _singleInstanceGuard.ResolveDuplicateLaunch(
                _platformService.AccountProvider,
                _platformService.AccountId,
                out existingAccountId);

        switch (result)
        {
            case SingleInstanceGuard.DuplicateLaunchResult.ExistingActivated:
                GD.Print("[SingleInstance] Existing game instance for the same account was activated; closing duplicate launch.");
                break;
            case SingleInstanceGuard.DuplicateLaunchResult.AccountConflict:
#if DEBUG
                if (IsSingleInstanceSmoke())
                {
                    GD.Print($"[SingleInstanceSmoke] Account conflict. Existing={existingAccountId}, Current={_platformService.AccountId}");
                    break;
                }
#endif
                OS.Alert(
                    $"Lucky Dog Rise is already running for Steam account {existingAccountId}.\n\n" +
                    $"The current Steam account is {_platformService.AccountId}. Close the existing game completely, then launch again.",
                    "Steam Account Conflict");
                break;
            case SingleInstanceGuard.DuplicateLaunchResult.IdentityUnavailable:
#if DEBUG
                if (IsSingleInstanceSmoke())
                {
                    GD.Print("[SingleInstanceSmoke] Current account identity is unavailable.");
                    break;
                }
#endif
                OS.Alert(
                    "Lucky Dog Rise is already running, and the current Steam account could not be verified. " +
                    "Close the existing game completely, confirm Steam is online, then launch again.",
                    "Steam Account Verification Required");
                break;
            default:
#if DEBUG
                if (IsSingleInstanceSmoke())
                {
                    GD.Print("[SingleInstanceSmoke] Existing instance was unresponsive.");
                    break;
                }
#endif
                OS.Alert(
                    "Lucky Dog Rise is already running, but its account identity or window did not respond.\n\n" +
                    "Stop the game from Steam. If Steam cannot stop it, end LuckyDogRise from Windows Task Manager.",
                    "Lucky Dog Rise");
                break;
        }

        DisposePlatformService();
        GetTree().Quit(result == SingleInstanceGuard.DuplicateLaunchResult.ExistingActivated ? 0 : 3);
    }

#if DEBUG
    private static bool IsSingleInstanceSmoke() => OS.GetCmdlineUserArgs().Any(argument =>
        string.Equals(argument, SingleInstanceSmokeArgument, StringComparison.OrdinalIgnoreCase));
#endif

    private void OnAccountIdentityConflictDetected(string expectedAccountId, string actualAccountId)
    {
        if (_shutdownRequested)
            return;
        var reason = $"Steam account changed from {expectedAccountId} to {actualAccountId} while the game was running.";
        _gameData?.FreezeAccountStorage(reason);
        _achievementSynchronizer = null!;
        OS.Alert(
            "The active Steam account changed while Lucky Dog Rise was running. " +
            "Local saves and platform writes have been stopped. The game will now exit; launch it again from the intended Steam account.",
            "Steam Account Changed");
        RequestGracefulQuit();
    }

    private bool OnExternalActivationRequested()
    {
        if (_shutdownRequested)
            return false;

        if (!IsNodeReady() || _startupState < StartupState.IntroPlaying)
        {
            _startupFocusRequested = true;
            return true;
        }

        SetNativeWindowCloaked(false);
        if (_hiddenByFullscreenApp)
            SetHiddenByFullscreenApp(false);

        var hWnd = (IntPtr)DisplayServer.WindowGetNativeHandle(DisplayServer.HandleType.WindowHandle);
        if (hWnd != IntPtr.Zero)
        {
            WindowNative.ShowWindow(hWnd, WindowNative.SW_RESTORE);
            WindowNative.SetForegroundWindow(hWnd);
        }
        ReassertTopmostNoActivate();
        (_platformService as IRecoverablePlatformService)?.RequestReconnect();
        GD.Print("[SingleInstance] Existing game window was revealed after another launch request.");
        return true;
    }

    private void OnPokerFrameRateChanged(int frameRate)
    {
        _pokerFrameRate = frameRate;
        ResetPokerViewportRendering();
    }

    private void ResetPokerViewportRendering()
    {
        _pokerRenderAccumulator = 0;
        _pokerViewportWasActive = false;
        if (_playSubViewport != null)
            _playSubViewport.RenderTargetUpdateMode = SubViewport.UpdateMode.Disabled;
    }

    private void UpdatePokerViewportRendering(double delta)
    {
        if (_playSubViewport == null)
            return;

        var active = CurrentMode == Mode.Play
            && !_hiddenByFullscreenApp
            && _playRoot.Visible;
        if (!active)
        {
            if (_pokerViewportWasActive)
                _playSubViewport.RenderTargetUpdateMode = SubViewport.UpdateMode.Disabled;
            _pokerViewportWasActive = false;
            _pokerRenderAccumulator = 0;
            return;
        }

        if (_pokerFrameRate >= 60)
        {
            _playSubViewport.RenderTargetUpdateMode = SubViewport.UpdateMode.Always;
            _pokerViewportWasActive = true;
            _pokerRenderAccumulator = 0;
            return;
        }

        var interval = 1.0 / _pokerFrameRate;
        if (!_pokerViewportWasActive)
        {
            _playSubViewport.RenderTargetUpdateMode = SubViewport.UpdateMode.Once;
            _pokerViewportWasActive = true;
            _pokerRenderAccumulator = 0;
            return;
        }

        _pokerRenderAccumulator += delta;
        if (_pokerRenderAccumulator < interval)
            return;

        _pokerRenderAccumulator %= interval;
        _playSubViewport.RenderTargetUpdateMode = SubViewport.UpdateMode.Once;
    }

    private void RunDiagnosticsExportSmoke()
    {
        var exportDirectory = System.Environment.GetEnvironmentVariable("LUCKYDOG_DIAGNOSTICS_SMOKE_DIR");
        if (string.IsNullOrWhiteSpace(exportDirectory))
        {
            GD.PushError("[DiagnosticsSmoke] Export failed: LUCKYDOG_DIAGNOSTICS_SMOKE_DIR is missing.");
            GetTree().Quit(3);
            return;
        }

        try
        {
            var path = DiagnosticLog.ExportPackage(_gameData, _platformService, exportDirectory);
            GD.Print($"[DiagnosticsSmoke] Export passed: {path}");
            GetTree().Quit();
        }
        catch (Exception exception)
        {
            GD.PushError($"[DiagnosticsSmoke] Export failed: {exception}");
            GetTree().Quit(3);
        }
    }

    private void RequestGracefulQuit()
    {
        if (_shutdownRequested)
            return;

        _settingsPanel?.CancelPendingDesktopPetScaleChange();
        _settingsPanel?.CancelPendingOtherUiScaleChange();
        _shutdownRequested = true;
        _singleInstanceGuard?.BeginShutdown();
        var totalStopwatch = Stopwatch.StartNew();

        var cloakStopwatch = Stopwatch.StartNew();
        bool windowCloaked = SetNativeWindowCloaked(true);
        if (!windowCloaked)
            SetNativeMainWindowVisible(false);
        GD.Print($"[Shutdown] Window hidden in {cloakStopwatch.ElapsedMilliseconds} ms (DWM cloak: {windowCloaked}).");

        try
        {
            _gameData?.FlushForShutdown();
        }
        catch (Exception exception)
        {
            GD.PushError($"[Shutdown] Failed to flush local data: {exception}");
        }

        var platformStopwatch = Stopwatch.StartNew();
        try
        {
            DisposePlatformService();
        }
        catch (Exception exception)
        {
            GD.PushError($"[Shutdown] Failed to dispose platform service: {exception}");
        }
        GD.Print($"[Shutdown] Platform cleanup completed in {platformStopwatch.ElapsedMilliseconds} ms.");
        GD.Print($"[Shutdown] Explicit shutdown preparation completed in {totalStopwatch.ElapsedMilliseconds} ms; quitting scene tree.");
        GetTree().Quit();
    }

    private void DisposePlatformService()
    {
        if (_platformDisposed)
            return;

        if (_platformService is IRecoverablePlatformService recoverable)
            recoverable.AccountIdentityConflictDetected -= OnAccountIdentityConflictDetected;
        _platformService?.Dispose();
        _platformDisposed = true;
    }

    private async void RevealBossKeyAfterTransparentHandoff(Rect2I screen, int switchRevision, bool windowCloaked)
    {
        // 此时扑克和桌宠均不可见。连续等待两次完整绘制，确保 Windows 合成器
        // 已用透明画面替换掉此前正在显示的扑克帧，再移动宿主窗口。
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);

        // 若这一帧内已经切回扑克模式，不能再移动宿主窗口。
        if (switchRevision != _modeSwitchRevision || CurrentMode != Mode.BossKey)
        {
            if (windowCloaked)
                SetNativeWindowCloaked(false);
            return;
        }

        PositionBossKeyForRightPlayPanel(screen);
        ShowBossKeyContent();

        // 让桌宠画面在 Cloak 状态下真正进入交换链，再交还给 DWM 显示。
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);

        if (windowCloaked)
            SetNativeWindowCloaked(false);
    }

    private void UpdateStartup(double delta)
    {
        if (_startupState == StartupState.AccountIdentityWaiting)
        {
            if (TryResolveAccountStorage(out var storageContext))
            {
#if DEBUG
                if (TryCompleteAccountIdentityProbe(storageContext))
                    return;
#endif
                AuthorizeDeveloperAccountAndCompleteStartup(_startInSteamMock, storageContext);
            }
            else if (_startupAccountGate != null)
                _startupAccountGate.SetStatus(
                    $"Steam account verification is required before loading player data.\nStart Steam and sign in, then click \"Retry\". The game may close and relaunch through Steam.\nNo player save has been loaded or modified.\n\nStatus: {GetStartupPlatformStatusMessage()}",
                    retryEnabled: true);
            return;
        }

        if (_startupState == StartupState.PlatformWaiting)
        {
            _startupPlatformWaitRemaining -= delta;
            if ((_platformService as IRecoverablePlatformService)?.ConnectionState == PlatformConnectionState.Ready)
            {
                GD.Print("[Startup] Steam inventory is ready; revealing desktop pet.");
                DiagnosticLog.Record("startup_reveal_ready", new Dictionary<string, object> { ["reason"] = "platform_ready" });
                _startupState = StartupState.ReadyToReveal;
            }
            else if (_startupPlatformWaitRemaining <= 0)
            {
                GD.Print("[Startup] Steam inventory wait reached 10 seconds; revealing with background recovery enabled.");
                DiagnosticLog.Record("startup_reveal_ready", new Dictionary<string, object> { ["reason"] = "platform_timeout" });
                _startupState = StartupState.ReadyToReveal;
            }
        }

        if (_startupState == StartupState.ReadyToReveal)
        {
            _startupState = StartupState.IntroPlaying;
            RevealBossStartup();
        }
    }

    private async void RevealBossStartup()
    {
        bool windowCloaked = SetNativeWindowCloaked(true);

        if (!_startupUseInitialMeetingPosition)
            PositionBossKeyForRightPlayPanel(_startupScreen);

        ShowBossKeyContent();
        PlayBossRiseIntro();
        SetNativeMainWindowVisible(true);

        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);

        if (windowCloaked)
            SetNativeWindowCloaked(false);

        _singleInstanceGuard?.MarkInteractive();
        if (_startupFocusRequested)
        {
            _startupFocusRequested = false;
            OnExternalActivationRequested();
        }
    }

    private void OnDesktopBgmPlaybackChanged(bool enabled)
    {
        if (CurrentMode == Mode.BossKey)
            AudioManager.Instance.SetBgmPaused(!enabled);
    }

    private void HideBossKeyContent()
    {
        _bossRiseIntro?.HideImmediate();
        _bossRiseIntroSuppressesBlindBoxHint = false;
        if (_bossDogVisual != null)
            _bossDogVisual.Visible = true;
        SetBossStatusPanelBaseVisible(true);
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
        RefreshBossCounterVisibilityAfterContentShown();
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

        _bossRiseIntro.Configure(
            _bossContentOffset,
            _bossDogVisual.Position,
            _bossDogVisual.Scale,
            _bossTaskBarAnchor.Position.Y,
            _desktopPetScaleFactor);
    }

    private void OnDesktopPetScalePreviewRequested(int step)
    {
        if (!_desktopPetScalePreviewActive)
        {
            _desktopPetScalePreviewActive = true;
            _desktopPetScalePreviewOriginalStep = _desktopPetScaleStep;
            _desktopPetScalePreviewOriginalWindowPosition = DisplayServer.WindowGetPosition();
            _desktopPetScalePreviewOriginalTaskbarSnapped = _taskbarSnapped;
        }

        ApplyDesktopPetScaleStep(step, preserveTaskbarAnchor: true);
    }

    private void OnDesktopPetScaleConfirmed(int step)
    {
        _desktopPetScaleStep = Mathf.Clamp(
            step,
            SettingsManager.DesktopPetScaleStepMin,
            SettingsManager.DesktopPetScaleStepMax);
        _desktopPetScalePreviewActive = false;
    }

    private void OnDesktopPetScaleCanceled()
    {
        if (!_desktopPetScalePreviewActive)
            return;

        var originalPosition = _desktopPetScalePreviewOriginalWindowPosition;
        var originalTaskbarSnapped = _desktopPetScalePreviewOriginalTaskbarSnapped;
        var originalStep = _desktopPetScalePreviewOriginalStep;
        _desktopPetScalePreviewActive = false;
        ApplyDesktopPetScaleStep(originalStep, preserveTaskbarAnchor: false);
        DisplayServer.WindowSetPosition(originalPosition);
        _taskbarSnapped = originalTaskbarSnapped;
        ApplyBossCounterLayout();
        UpdateBossBlindBoxOverlayPosition();
        if (_settingsPanel?.IsOpen == true)
            PositionPanelInBestSlot();
    }

    private void OnOtherUiScalePreviewRequested(int step)
    {
        if (!_otherUiScalePreviewActive)
        {
            _otherUiScalePreviewActive = true;
            _otherUiScalePreviewOriginalStep = _otherUiScaleStep;
            _otherUiScalePreviewOriginalWindowPosition = DisplayServer.WindowGetPosition();
        }

        ApplyOtherUiScaleStep(step, preserveContentAnchor: true);
    }

    private void OnOtherUiScaleConfirmed(int step)
    {
        _otherUiScaleStep = Mathf.Clamp(
            step,
            SettingsManager.OtherUiScaleStepMin,
            SettingsManager.OtherUiScaleStepMax);
        _otherUiScalePreviewActive = false;
    }

    private void OnOtherUiScaleCanceled()
    {
        if (!_otherUiScalePreviewActive)
            return;

        var originalStep = _otherUiScalePreviewOriginalStep;
        var originalPosition = _otherUiScalePreviewOriginalWindowPosition;
        _otherUiScalePreviewActive = false;
        ApplyOtherUiScaleStep(originalStep, preserveContentAnchor: false);
        DisplayServer.WindowSetPosition(originalPosition);
        if (CurrentMode == Mode.BossKey)
        {
            ApplyBossCounterLayout();
            UpdateBossBlindBoxOverlayPosition();
        }
        else if (CurrentMode == Mode.Play)
        {
            UpdatePlayLayout();
        }

        if (_settingsPanel?.IsOpen == true)
            PositionPanelInBestSlot();
    }

    private void ApplyOtherUiScaleStep(int step, bool preserveContentAnchor)
    {
        var oldBossAnchor = DisplayServer.WindowGetPosition()
            + RoundToVector2I(GetBossTaskbarAnchorWindowPosition());
        var oldPlayAnchor = _playViewport == null
            ? DisplayServer.WindowGetPosition()
            : DisplayServer.WindowGetPosition() + RoundToVector2I(_playViewport.Position);

        _otherUiScaleStep = Mathf.Clamp(
            step,
            SettingsManager.OtherUiScaleStepMin,
            SettingsManager.OtherUiScaleStepMax);
        _otherUiScaleFactor = SettingsManager.GetOtherUiScaleFactor(_otherUiScaleStep);
        GetViewport().OversamplingOverride = Mathf.Max(1f, _otherUiScaleFactor);
        _settingsPanel.SetRenderScale(_otherUiScaleFactor);
        _panelSize = _settingsPanel.PanelSize;
        _contentOffset = _panelSize;
#if DEBUG
        _steamMockPanel?.SetPanelBottom(_contentOffset.Y);
#endif
        ApplyDesktopPetScaleGeometry(_desktopPetScaleStep);

        if (_playViewport != null)
            _playViewport.Scale = Vector2.One * (0.5f * _otherUiScaleFactor);
        _infoPanel?.SetRenderScale(_otherUiScaleFactor);

        if (CurrentMode == Mode.BossKey)
        {
            SetupFatWindow();
            if (preserveContentAnchor)
            {
                DisplayServer.WindowSetPosition(
                    oldBossAnchor - RoundToVector2I(GetBossTaskbarAnchorWindowPosition()));
            }
            ApplyBossCounterLayout();
            UpdateBossBlindBoxOverlayPosition();
        }
        else if (CurrentMode == Mode.Play)
        {
            SetupPlayFatWindow();
            UpdatePlayLayout();
            if (preserveContentAnchor)
            {
                DisplayServer.WindowSetPosition(
                    oldPlayAnchor - RoundToVector2I(_playViewport.Position));
            }
            KeepPlayContentWithinScreen();
            UpdatePlayLayout();
        }

        if (_settingsPanel.IsOpen)
            PositionPanelInBestSlot();
    }

    private int GetPlayInfoPanelWidth() => Mathf.CeilToInt(PlayInfoPanelBaseWidth * _otherUiScaleFactor);
    private int GetPlayGameSize() => Mathf.CeilToInt(PlayGameBaseSize * _otherUiScaleFactor);

    public void ApplyDesktopPetScaleStep(int step) =>
        ApplyDesktopPetScaleStep(step, preserveTaskbarAnchor: true);

    private void ApplyDesktopPetScaleStep(int step, bool preserveTaskbarAnchor)
    {
        var oldAnchorScreenPosition = DisplayServer.WindowGetPosition()
            + RoundToVector2I(GetBossTaskbarAnchorWindowPosition());

        ApplyDesktopPetScaleGeometry(step);
        if (CurrentMode != Mode.BossKey)
            return;

        SetupFatWindow();
        if (preserveTaskbarAnchor)
        {
            var newWindowPosition = oldAnchorScreenPosition
                - RoundToVector2I(GetBossTaskbarAnchorWindowPosition());
            DisplayServer.WindowSetPosition(newWindowPosition);
        }

        ApplyBossCounterLayout();
        UpdateBossBlindBoxOverlayPosition();
        if (_settingsPanel?.IsOpen == true)
            PositionPanelInBestSlot();
    }

    private void ApplyDesktopPetScaleGeometry(int step)
    {
        _desktopPetScaleStep = Mathf.Clamp(
            step,
            SettingsManager.DesktopPetScaleStepMin,
            SettingsManager.DesktopPetScaleStepMax);
        _desktopPetScaleFactor = SettingsManager.GetDesktopPetScaleFactor(_desktopPetScaleStep);

        _bossContentOffset = new Vector2(
            _panelSize.X,
            CalculateBossTopBufferHeight());

        _bossContentA.Position = _bossContentOffset;
        _bossContentA.Scale = Vector2.One * _desktopPetScaleFactor;
        _bossCanvasLayer.Offset = _bossContentOffset;
        _bossCanvasLayer.Scale = Vector2.One * _desktopPetScaleFactor;
        _bossBubbleLayer.Offset = _bossContentOffset;
        _bossBubbleLayer.Scale = Vector2.One * _desktopPetScaleFactor;
        _bossBlindBoxHint?.SetRenderScale(_desktopPetScaleFactor);
        ApplyBossCounterTextRenderScale();
        ApplyBossCounterStyleRenderScale();
        if (_bossBlindBoxOverlay != null)
            _bossBlindBoxOverlay.Scale = Vector2.One * _desktopPetScaleFactor;

        _windowBaseSize = new Vector2(
            Mathf.Ceil(_windowBaseDesignSize.X * _desktopPetScaleFactor),
            Mathf.Ceil(_windowBaseDesignSize.Y * _desktopPetScaleFactor));
        UpdateBossInteractionRects();
        ConfigureBossRiseIntro();
        UpdateBossBlindBoxOverlayPosition();
    }

    private float CalculateBossTopBufferHeight()
    {
        var revealTop = -302f;
        if (_bossBlindBoxOverlay != null)
        {
            var revealRoot = _bossBlindBoxOverlay.GetNodeOrNull<Control>("RevealRoot");
            var rewardRoot = _bossBlindBoxOverlay.GetNodeOrNull<Control>("RewardRoot");
            if (revealRoot != null)
                revealTop = Mathf.Min(revealTop, revealRoot.Position.Y);
            if (rewardRoot != null)
                revealTop = Mathf.Min(revealTop, rewardRoot.Position.Y);
        }

        var revealTopRelativeToA = _bossBlindBoxRevealAnchor.Position.Y + revealTop;
        var requiredRevealBuffer = Mathf.Ceil(-revealTopRelativeToA * _desktopPetScaleFactor);
        return Mathf.Max(_panelSize.Y, requiredRevealBuffer);
    }

    private Vector2 GetBossTaskbarAnchorWindowPosition() =>
        _bossContentOffset + _bossTaskBarAnchor.Position * _desktopPetScaleFactor;

    private void ApplyBossCounterTextRenderScale()
    {
        if (_mainText == null || _bossCounterSourceFont == null)
            return;

        var oversampling = Math.Max(1, Mathf.CeilToInt(_desktopPetScaleFactor));
        if (!_bossCounterFontsByOversampling.TryGetValue(oversampling, out var font))
        {
            font = (Font)_bossCounterSourceFont.Duplicate();
            if (font is FontFile fontFile)
                fontFile.Oversampling = oversampling;
            else if (font is SystemFont systemFont)
                systemFont.Oversampling = oversampling;
            _bossCounterFontsByOversampling[oversampling] = font;
        }

        _mainText.AddThemeFontOverride("font", font);
    }

    private static Vector2I RoundToVector2I(Vector2 value) =>
        new(Mathf.RoundToInt(value.X), Mathf.RoundToInt(value.Y));

    private Rect2 ScaleBossDesignRect(Rect2 designRect) => new(
        _bossContentOffset + designRect.Position * _desktopPetScaleFactor,
        designRect.Size * _desktopPetScaleFactor);

    private void UpdateBossInteractionRects()
    {
        // The legacy dog rectangle extends 35 design pixels below A. At high scale this
        // becomes a large invisible strip below the counter that still captures dragging.
        var dogRectInsideA = BossDogHitRectDesign.Intersection(
            new Rect2(Vector2.Zero, _windowBaseDesignSize));
        _dogHitRect = ScaleBossDesignRect(dogRectInsideA);
    }

    private void PlayBossRiseIntro()
    {
        if (_bossRiseIntro == null || _hiddenByFullscreenApp || CurrentMode != Mode.BossKey)
            return;

        ConfigureBossRiseIntro();
        _bossDogVisual.Visible = false;
        SetBossStatusPanelBaseVisible(false);
        SetBossStatusBarInteractable(false);
        _bossRiseIntroSuppressesBlindBoxHint = true;
        RefreshBossBlindBoxHint();
        _bossRiseIntro.Play();
    }

    private void OnBossRiseIntroStatusBarRevealRequested()
    {
        if (CurrentMode != Mode.BossKey || _hiddenByFullscreenApp)
            return;

        SetBossStatusPanelBaseVisible(true);
        SetBossStatusBarInteractable(false);
    }

    private void OnBossRiseIntroFinished()
    {
        if (_startupState == StartupState.IntroPlaying)
            _startupState = StartupState.Interactive;
        _bossRiseIntroSuppressesBlindBoxHint = false;
        if (CurrentMode != Mode.BossKey || _hiddenByFullscreenApp)
            return;

        _bossDogVisual.Visible = true;
        SetBossStatusPanelBaseVisible(true);
        SetBossStatusBarInteractable(true);
        RefreshBossCounterVisibilityAfterContentShown();
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

    private void SetBossStatusPanelBaseVisible(bool visible)
    {
        _bossStatusPanelBaseVisible = visible;
        ApplyBossStatusPanelVisibility();
    }

    private void ApplyBossStatusPanelVisibility()
    {
        if (_bossStatusPanel == null)
            return;

        _bossStatusPanel.Visible = _bossStatusPanelBaseVisible && !_bossCounterAutoHidden;
    }

    private void OnAutoHideCounterChanged(bool enabled)
    {
        _bossCounterAutoHideRemainingSeconds = 0.0;
        _bossCounterAutoHidden = enabled
            && !IsMouseOverBossCounterAutoHideRegion(DisplayServer.MouseGetPosition());
        ApplyBossStatusPanelVisibility();
    }

    private void RefreshBossCounterVisibilityAfterContentShown()
    {
        if (!SettingsManager.LoadAutoHideCounter())
        {
            _bossCounterAutoHidden = false;
            _bossCounterAutoHideRemainingSeconds = 0.0;
            ApplyBossStatusPanelVisibility();
            return;
        }

        _bossCounterAutoHidden = !IsMouseOverBossCounterAutoHideRegion(DisplayServer.MouseGetPosition());
        _bossCounterAutoHideRemainingSeconds = _bossCounterAutoHidden
            ? 0.0
            : BossCounterAutoHideDelaySeconds;
        ApplyBossStatusPanelVisibility();
    }

    private void UpdateBossCounterAutoHide(double delta)
    {
        if (_bossStatusPanel == null
            || CurrentMode != Mode.BossKey
            || _hiddenByFullscreenApp
            || _startupState != StartupState.Interactive
            || !_bossStatusPanelBaseVisible
            || !SettingsManager.LoadAutoHideCounter())
            return;

        if (IsMouseOverBossCounterAutoHideRegion(DisplayServer.MouseGetPosition()))
        {
            _bossCounterAutoHideRemainingSeconds = BossCounterAutoHideDelaySeconds;
            if (_bossCounterAutoHidden)
            {
                _bossCounterAutoHidden = false;
                ApplyBossStatusPanelVisibility();
            }
            return;
        }

        if (_bossCounterAutoHidden)
            return;

        _bossCounterAutoHideRemainingSeconds -= delta;
        if (_bossCounterAutoHideRemainingSeconds > 0.0)
            return;

        _bossCounterAutoHideRemainingSeconds = 0.0;
        _bossCounterAutoHidden = true;
        ApplyBossStatusPanelVisibility();
    }

    private bool IsMouseOverBossCounterAutoHideRegion(Vector2I screenPosition)
    {
        if (_bossStatusPanel == null)
            return false;

        var localPos = screenPosition - DisplayServer.WindowGetPosition();
        return _dogHitRect.HasPoint(localPos)
            || GetBossStatusPanelRect().HasPoint(localPos);
    }

    private void ApplyBossCounterLayout()
    {
        if (_bossStatusPanel == null || _bossTaskBarAnchor == null)
            return;

        if (!SettingsManager.LoadCenterCounterOnTaskbar()
            || !SettingsManager.LoadSnapToWindowsTaskbar()
            || !_taskbarSnapped)
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
        var taskbarHeightInDesignCoordinates = taskbarHeight / _desktopPetScaleFactor;
        var desiredTop = taskbarTop + (taskbarHeightInDesignCoordinates - panelHeight) / 2f;
        var tongueLimitTop = GetBossTongueClearTopY();
        if (desiredTop < tongueLimitTop)
            desiredTop = tongueLimitTop;

        // 桌宠倍率优先：Counter 保持完整设计高度，只通过移动避让舌头。
        ApplyBossStatusPanelHeight(panelHeight);
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
        _bossStatusPanelBaseAntiAliasingSize = _bossStatusPanelStyle.AntiAliasingSize;
    }

    private void ApplyBossCounterStyleRenderScale()
    {
        if (_bossStatusPanelStyle == null)
            return;

        // StyleBoxFlat recommends an antialiasing ring of 1 px at final display scale.
        // Compensate for the CanvasLayer scale so the ring does not become visibly blurry.
        _bossStatusPanelStyle.AntiAliasingSize =
            _bossStatusPanelBaseAntiAliasingSize / Mathf.Max(0.01f, _desktopPetScaleFactor);
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

        if (_blindBoxOpeningUiActive)
        {
            SetBossBlindBoxHintDisplayVisible(true);
            _bossBlindBoxHint.ShowLoading();
            return;
        }

        var state = _gameData.GetBlindBoxHintState();
        var hideWaitingBubble = state.Status == BlindBoxHintStatus.Waiting
            && !SettingsManager.LoadAlwaysShowBlindBoxBubble();
        var hideForTransition = state.Status is BlindBoxHintStatus.PendingReward or BlindBoxHintStatus.Opening;
        SetBossBlindBoxHintDisplayVisible(!hideForTransition && !hideWaitingBubble);

        switch (state.Status)
        {
            case BlindBoxHintStatus.PendingReward:
            case BlindBoxHintStatus.Opening:
                break;
            case BlindBoxHintStatus.Ready:
            case BlindBoxHintStatus.NotEnoughChips:
                _bossBlindBoxHint.ShowValueFromAssetPath(
                    state.Box?.HintIconPath,
                    _blindBoxIcon,
                    state.ValueMode,
                    state.DisplayValue,
                    _gameData.Chips,
                    state.PaymentSource,
                    state.StrikeThrough);
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
                BeginBlindBoxOpen(state);
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
            _bossContentOffset + _bossBlindBoxHint.Position * _desktopPetScaleFactor,
            _bossBlindBoxHint.Size * _desktopPetScaleFactor
        );
    }

    private Rect2 GetBossStatusPanelRect()
    {
        return new Rect2(
            _bossContentOffset + _bossStatusPanel.Position * _desktopPetScaleFactor,
            _bossStatusPanel.Size * _desktopPetScaleFactor
        );
    }

    private Rect2 GetBossBlindBoxOverlayRect()
    {
        var root = _bossBlindBoxOverlay.GetNodeOrNull<Control>("RevealRoot")
            ?? _bossBlindBoxOverlay.GetNodeOrNull<Control>("RewardRoot");
        if (root == null)
        {
            return new Rect2(
                _bossBlindBoxRevealAnchor.GlobalPosition
                    + new Vector2(-150f, -302f) * _desktopPetScaleFactor,
                new Vector2(300f, 332f) * _desktopPetScaleFactor
            );
        }

        var rect = new Rect2(
            _bossBlindBoxOverlay.Offset + root.Position * _desktopPetScaleFactor,
            root.Size * _desktopPetScaleFactor);
        // The speech-bubble tail extends below the 300x300 panel.
        rect.Size += new Vector2(0f, 32f) * _desktopPetScaleFactor;
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
        if (_blindBoxOpeningUiActive
            || CurrentMode != Mode.BossKey
            || _gameData.PendingBlindBoxReward == null)
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

#if DEBUG
    private void OnRandomizeScene()
    {
        ApplyRandomEquipment(DebugEquipmentSource.AllCatalog);
    }

    private void OnRandomizeDog()
    {
        ApplyRandomEquipment(DebugEquipmentSource.Owned);
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
        var allCandidates = LubanData.Tables.TbItem.DataList
            .Where(item => item.ItemType != EItemType.Dog && !item.IsHiddenInBag)
            .ToList();
        if (allCandidates.Count == 0)
            return;

        // 未集齐时只发未拥有物品；集齐后允许重复发放，以便录制时继续补数量。
        bool hasUnownedItem = allCandidates.Any(item => !_gameData.Inventory.Owns(item.Id));
        for (int attempt = 0; attempt < DebugGrantItemTypes.Length; attempt++)
        {
            var type = DebugGrantItemTypes[_debugGrantItemTypeIndex];
            _debugGrantItemTypeIndex = (_debugGrantItemTypeIndex + 1) % DebugGrantItemTypes.Length;

            var candidates = allCandidates
                .Where(item => item.ItemType == type)
                .Where(item => !hasUnownedItem || !_gameData.Inventory.Owns(item.Id))
                .ToList();
            if (candidates.Count == 0)
                continue;

            var item = candidates[_debugRandom.Next(candidates.Count)];
            _gameData.AddItem(item.Id, count: 1, markNew: false, source: PlayerProgressSource.Debug);
            return;
        }
    }

    private void OnDebugGrantChips()
    {
        _gameData.ModifyChips(8000);
    }

    private void OnDebugGrantLuckyDeals()
    {
        _gameData.GrantLuckyDealBuff(10, 0.75f);
    }
#endif

    private void OnBlindBoxRequested()
    {
        if (_infoPanel == null)
            return;

        var state = _gameData.GetBlindBoxHintState();
        if (state.Status == BlindBoxHintStatus.Ready)
            BeginBlindBoxOpen(state);
        else if (state.Status == BlindBoxHintStatus.PendingReward
                 && _gameData.PendingBlindBoxReward != null)
            _gameManager?.ShowPendingBlindBoxReward(_gameData.PendingBlindBoxReward);
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
        _settingsPanel?.OnGlobalMousePressed(screenPosition, !IsScreenPointOverInteractiveContent(screenPosition));
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

#if DEBUG
    private void OnGlobalMouseListeningDisabledChanged(bool disabled)
    {
        _globalInputTracker.SetGlobalMouseListeningEnabled(!disabled);
    }
#endif

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

    private void BeginBlindBoxOpen(BlindBoxHintState state)
    {
        if (_blindBoxOpeningUiActive)
            return;

        StartBlindBoxOpeningUi(state.PaymentSource);
        var pending = _gameData.TryOpenBlindBox();
        if (pending != null)
        {
            ResolveBlindBoxOpeningUi(pending);
            return;
        }

        CancelBlindBoxOpeningUi();
    }

    private void StartBlindBoxOpeningUi(BlindBoxPaymentSource paymentSource)
    {
        _blindBoxOpeningUiActive = true;
        _blindBoxOpeningUiElapsedSeconds = 0.0;
        _blindBoxOpeningUiMinimumSeconds = BlindBoxLoadingMinimumSeconds;
        _blindBoxOpeningResolvedReward = null!;
        _infoPanel?.SetBlindBoxOpeningLoading(true);
        RefreshBossBlindBoxHint();
        DiagnosticLog.Record("blindbox_loading_started", new Dictionary<string, object>
        {
            ["paymentSource"] = paymentSource.ToString(),
            ["minimumSeconds"] = _blindBoxOpeningUiMinimumSeconds,
        });
    }

    private void ResolveBlindBoxOpeningUi(PendingBlindBoxReward pending)
    {
        if (!_blindBoxOpeningUiActive)
        {
            PresentBlindBoxReward(pending);
            return;
        }

        _blindBoxOpeningResolvedReward = pending;
        TryPresentResolvedBlindBoxReward();
    }

    private void UpdateBlindBoxOpeningUi(double delta)
    {
        if (!_blindBoxOpeningUiActive)
            return;

        _blindBoxOpeningUiElapsedSeconds += delta;
        TryPresentResolvedBlindBoxReward();
    }

    private void TryPresentResolvedBlindBoxReward()
    {
        if (!_blindBoxOpeningUiActive
            || _blindBoxOpeningResolvedReward == null
            || _blindBoxOpeningUiElapsedSeconds < _blindBoxOpeningUiMinimumSeconds)
        {
            return;
        }

        var pending = _blindBoxOpeningResolvedReward;
        var elapsedSeconds = _blindBoxOpeningUiElapsedSeconds;
        _blindBoxOpeningUiActive = false;
        _blindBoxOpeningResolvedReward = null!;
        _infoPanel?.SetBlindBoxOpeningLoading(false);
        RefreshBossBlindBoxHint();
        DiagnosticLog.Record("blindbox_loading_completed", new Dictionary<string, object>
        {
            ["elapsedSeconds"] = elapsedSeconds,
            ["blindBoxId"] = pending.BlindBoxId,
            ["rewardItemId"] = pending.ItemId,
        });
        PresentBlindBoxReward(pending);
    }

    private void PresentBlindBoxReward(PendingBlindBoxReward pending)
    {
        if (CurrentMode == Mode.BossKey)
            ShowBossBlindBoxReward(pending);
        else if (CurrentMode == Mode.Play)
            _gameManager?.ShowPendingBlindBoxReward(pending);
    }

    private void CancelBlindBoxOpeningUi()
    {
        _blindBoxOpeningUiActive = false;
        _blindBoxOpeningResolvedReward = null!;
        _infoPanel?.SetBlindBoxOpeningLoading(false);
        RefreshBossBlindBoxHint();
    }

#if DEBUG
    private void OnSteamMockPanelVisibilityChanged(bool visible)
    {
        _steamMockPanelRequestedVisible = visible;
        RefreshSteamMockPanelVisibility();
    }

    private void OnSteamMockPanelCloseRequested()
    {
        _steamMockPanelRequestedVisible = false;
        _settingsPanel.SetSteamMockPanelToggle(false);
        RefreshSteamMockPanelVisibility();
    }

    private void OnSteamMockSimulationReset()
    {
        CancelBlindBoxOpeningUi();
        _gameManager?.HidePendingBlindBoxReward();
        _bossBlindBoxOverlay?.HideOverlay();
        _settingsPanel?.ResetSteamMockLinkTreeState();
        RefreshBossBlindBoxHint();
        _infoPanel?.RefreshBlindBoxButton();
    }

    private void RefreshSteamMockPanelVisibility()
    {
        _steamMockPanel?.SetPanelVisible(
            _steamMockPanelRequestedVisible && CurrentMode == Mode.Play);
    }

    private void UpdateSteamMockPresentation()
    {
        if (_steamMockController == null)
            return;
        var active = _steamMockController.IsMockActive;
        if (_lastSteamMockActive == active)
            return;
        _lastSteamMockActive = active;
        _settingsPanel.SetSteamMockActive(active);
        RefreshBossBlindBoxHint();
    }
#endif

    private bool IsScreenPointOverInteractiveContent(Vector2I screenPosition)
    {
        var localPos = screenPosition - DisplayServer.WindowGetPosition();
        bool over = _settingsPanel != null && _settingsPanel.ContainsPoint(localPos);
#if DEBUG
        over |= _steamMockPanel != null && _steamMockPanel.ContainsPoint(localPos);
#endif

        if (CurrentMode == Mode.BossKey)
        {
            over |= _dogHitRect.HasPoint(localPos);
            // Keep the counter geometry as a hover target even when the panel is hidden by auto-hide.
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
                int infoX = _infoPanelOnRight ? GetPlayInfoPanelWidth() + GetPlayGameSize() + PlayGameSettingsGap : 0;
                over |= new Rect2(infoX, _contentOffset.Y, GetPlayInfoPanelWidth(), GetPlayGameSize()).HasPoint(localPos);
            }
        }

        return over;
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
        if (state.DogReactionTrigger == EDogReactionTrigger.Starstruck)
            _gameData.RecordPlayerProgressEvent("DesktopStarstruckEntered");
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

#if DEBUG
    private void ApplyRandomEquipment(DebugEquipmentSource source)
    {
        var selections = new Dictionary<EItemType, int?>();
        foreach (var type in PlayerInventory.GetEquipmentTypes())
        {
            var candidates = (source == DebugEquipmentSource.AllCatalog
                    ? LubanData.Tables.TbItem.DataList.Where(item => item.ItemType == type)
                    : _gameData.Inventory.GetOwnedOfType(type))
                // Special2 暂作为录制避让标记：不参与 Debug 快速随机穿戴。
                .Where(item => item.ItemRarity != ERarity.Special2)
                .Select(item => item.Id)
                .OrderBy(id => id)
                .ToList();
            if (_gameData.Inventory.CanUnequip(type))
            {
                for (int i = 0; i < DebugEmptyEquipmentWeight; i++)
                    candidates.Add(0); // 0 不对应物品，代表该可空装备位留空。
            }
            if (candidates.Count == 0)
                continue;

            var key = (source, type);
            if (!_debugEquipmentBags.TryGetValue(key, out var bag))
            {
                bag = new ShuffleBag<int>();
                _debugEquipmentBags[key] = bag;
            }

            var equippedId = _gameData.Inventory.GetEquipped(type)?.Id ?? 0;
            var pickedId = bag.Pick(candidates, _debugRandom, equippedId);
            selections[type] = pickedId == 0 ? null : pickedId;
        }

        // 全图鉴模式只做临时视觉预览，绝不把未拥有物品写进背包或存档。
        _gameData.Inventory.SetDebugPreviewEquipment(selections);
    }
#endif

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
        var screen = CurrentMode == Mode.BossKey
            ? GetBossKeyUsableScreenRect()
            : GetBestScreenUsableRect(GetPlayGameScreenRect());
        int pw = (int)_panelSize.X;
        int ph = (int)_panelSize.Y;

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
            aX = _bossContentOffset.X;
            aY = _bossContentOffset.Y;
            aw = (int)_windowBaseSize.X;
            ah = (int)_windowBaseSize.Y;
        }

        Rect2? infoPanelRect = null;
        if (CurrentMode == Mode.Play && _infoPanel != null && _infoPanel.Visible)
        {
            int infoX = _infoPanelOnRight
                ? (int)aX + aw + PlayGameSettingsGap
                : 0;
            infoPanelRect = new Rect2(
                infoX,
                (int)aY,
                GetPlayInfoPanelWidth(),
                GetPlayGameSize());
        }

        // 桌宠模式的 4/6 宫以 TaskBar 锚点（小狗吸附任务栏的下沿）作为底边。
        // 不能使用整个 A 区底边，否则倍率越大，侧边面板会被额外向下推并挤出屏幕。
        float sidePanelBottomY = CurrentMode == Mode.BossKey
            ? GetBossTaskbarAnchorWindowPosition().Y
            : aY + ah;

        var placement = _panelAvoidanceStrategy.CalculatePanelPlacement(
            new PanelPlacementContext(
                CurrentMode == Mode.Play ? PanelHostMode.Play : PanelHostMode.BossKey,
                winPos,
                DisplayServer.WindowGetSize(),
                screen,
                new Vector2(aX, aY),
                new Vector2I(aw, ah),
                new Vector2I(pw, ph),
                sidePanelBottomY,
                infoPanelRect,
                Mathf.CeilToInt(_settingsPanel.TopActionAreaHeight),
                PlayGameSettingsGap));
        _settingsPanel.SetPanelPosition(placement.PanelPosition);
    }

    // ===== 窗口管理 =====

    private void SetupFatWindow()
    {
        DisplayServer.WindowSetFlag(DisplayServer.WindowFlags.Transparent, true);
        DisplayServer.WindowSetFlag(DisplayServer.WindowFlags.AlwaysOnTop, true);

        var layout = _panelAvoidanceStrategy.CalculateHostLayout(
            new HostLayoutContext(
                PanelHostMode.BossKey,
                _bossContentOffset,
                _windowBaseSize,
                _panelSize,
                0,
                0));
        _bossContentOffset = layout.MainContentOrigin;
        var pos = DisplayServer.WindowGetPosition();
        DisplayServer.WindowSetSize(layout.WindowSize);
        DisplayServer.WindowSetPosition(pos);
        RenderingServer.SetDefaultClearColor(new Color(0, 0, 0, 0));
    }

    /// <summary>
    /// 反推桌宠窗口位置，使切回扑克后 Main 右侧能完整展开运行时尺寸的系统设置面板。
    /// 同时夹紧桌宠 A 区，避免桌宠跑出目标显示器工作区。
    /// </summary>
    private void PositionBossKeyForRightPlayPanel(Rect2I screen)
    {
        const int pad = 5;

        int anchorY = Mathf.RoundToInt(GetBossTaskbarAnchorWindowPosition().Y);
        int taskbarTop = screen.End.Y;

        // _playViewport.Position.X 是扑克内容左侧信息面板的实际宽度；
        // _panelSize 则直接来自系统设置面板的运行时尺寸，避免未来改面板宽度后坐标公式失效。
        int playContentWidth = _playViewport == null
            ? GetPlayInfoPanelWidth() + GetPlayGameSize()
            : Mathf.CeilToInt(_playViewport.Position.X
                + _playViewport.Size.X * _playViewport.Scale.X);
        int requiredRightSpace = playContentWidth + PlayGameSettingsGap + Mathf.CeilToInt(_panelSize.X);
        int desiredWindowX = screen.End.X - pad - requiredRightSpace;

        int minWindowX = screen.Position.X + pad - (int)_bossContentOffset.X;
        int maxWindowX = screen.End.X - pad - (int)_bossContentOffset.X - (int)_windowBaseSize.X;
        if (maxWindowX < minWindowX)
            maxWindowX = minWindowX;

        var windowPosition = new Vector2I(
            Math.Clamp(desiredWindowX, minWindowX, maxWindowX),
            taskbarTop - anchorY);
        DisplayServer.WindowSetPosition(windowPosition);
        _taskbarSnapped = SettingsManager.LoadSnapToWindowsTaskbar();
        ApplyBossCounterLayout();
    }

    private void ApplyTaskbarSnap(ref Vector2I newPos)
    {
        if (CurrentMode != Mode.BossKey || !SettingsManager.LoadSnapToWindowsTaskbar())
        {
            _taskbarSnapped = false;
            return;
        }

        var anchorWindowPosition = RoundToVector2I(GetBossTaskbarAnchorWindowPosition());
        var scrRect = GetUsableScreenRectAtPoint(newPos + anchorWindowPosition);
        int taskbarTop = scrRect.Position.Y + scrRect.Size.Y;
        int anchorY = anchorWindowPosition.Y;
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

    private void UpdateBossWorkAreaTracking()
    {
        if (CurrentMode != Mode.BossKey)
        {
            _bossWorkAreaSnapshotReady = false;
            _bossWorkAreaScreen = -1;
            return;
        }

        var anchorWindowPosition = RoundToVector2I(GetBossTaskbarAnchorWindowPosition());
        var currentAnchorScreenPosition = DisplayServer.WindowGetPosition() + anchorWindowPosition;
        int screenIndex = _bossWorkAreaSnapshotReady && !_isDragging
            ? _bossWorkAreaScreen
            : FindScreenIndexAtPoint(currentAnchorScreenPosition);
        if (screenIndex < 0)
            screenIndex = DisplayServer.WindowGetCurrentScreen();

        var screenRect = new Rect2I(
            DisplayServer.ScreenGetPosition(screenIndex),
            DisplayServer.ScreenGetSize(screenIndex));
        var usableRect = DisplayServer.ScreenGetUsableRect(screenIndex);

        if (!_bossWorkAreaSnapshotReady || screenIndex != _bossWorkAreaScreen)
        {
            if (SettingsManager.LoadSnapToWindowsTaskbar()
                && Math.Abs(currentAnchorScreenPosition.Y - usableRect.End.Y) <= SnapThreshold)
                _taskbarSnapped = true;
            CaptureBossWorkAreaSnapshot(
                screenIndex,
                screenRect,
                usableRect,
                currentAnchorScreenPosition);
            return;
        }

        bool workAreaChanged = screenRect != _bossLastScreenRect
            || usableRect != _bossLastUsableRect;
        bool snapEnabled = SettingsManager.LoadSnapToWindowsTaskbar();
        bool currentlyAttachedToTaskbar =
            Math.Abs(currentAnchorScreenPosition.Y - usableRect.End.Y) <= SnapThreshold;

        if (workAreaChanged && !_isDragging)
        {
            bool wasAttachedToTaskbar = snapEnabled
                && (_taskbarSnapped
                    || Math.Abs(
                        _bossLastTaskbarAnchorScreenPosition.Y - _bossLastUsableRect.End.Y)
                        <= SnapThreshold);

            if (wasAttachedToTaskbar)
            {
                // Windows may reposition this oversized transparent host window when its work
                // area changes. Restore the previous horizontal anchor and attach it to the new
                // taskbar top instead of trusting that OS-adjusted window position.
                var desiredAnchorScreenPosition = new Vector2I(
                    _bossLastTaskbarAnchorScreenPosition.X,
                    usableRect.End.Y);
                DisplayServer.WindowSetPosition(desiredAnchorScreenPosition - anchorWindowPosition);
                currentAnchorScreenPosition = desiredAnchorScreenPosition;
                _taskbarSnapped = true;
                ApplyBossCounterLayout();
                if (_settingsPanel?.IsOpen == true)
                    PositionPanelInBestSlot();
            }
            else
            {
                // 未吸附时，任务栏尺寸变化不应改变玩家放置的桌宠位置。
                // Windows 会先重排超大的透明宿主窗口，再更新 Godot 暴露的工作区；
                // 因此要恢复最后一次可信锚点，而不是接受系统重排后的位置。
                DisplayServer.WindowSetPosition(
                    _bossLastTaskbarAnchorScreenPosition - anchorWindowPosition);
                currentAnchorScreenPosition = _bossLastTaskbarAnchorScreenPosition;
                _taskbarSnapped = false;
                ApplyBossCounterLayout();
                if (_settingsPanel?.IsOpen == true)
                    PositionPanelInBestSlot();
            }
        }
        else if (!_isDragging && snapEnabled && _taskbarSnapped && !currentlyAttachedToTaskbar)
        {
            // The native window can be moved before Godot exposes the new work-area rect.
            // Keep the last trusted horizontal anchor and immediately recover taskbar contact.
            var desiredAnchorScreenPosition = new Vector2I(
                _bossLastTaskbarAnchorScreenPosition.X,
                usableRect.End.Y);
            DisplayServer.WindowSetPosition(desiredAnchorScreenPosition - anchorWindowPosition);
            currentAnchorScreenPosition = desiredAnchorScreenPosition;
            ApplyBossCounterLayout();
        }
        else if (!_isDragging
            && !_taskbarSnapped
            && screenRect == _bossLastScreenRect
            && currentAnchorScreenPosition != _bossLastTaskbarAnchorScreenPosition)
        {
            // 工作区通知到达前，Windows 可能已经移动了胖窗口。未吸附状态下
            // 玩家只能通过本类的拖拽流程移动窗口，所以此处可安全恢复自由锚点。
            DisplayServer.WindowSetPosition(
                _bossLastTaskbarAnchorScreenPosition - anchorWindowPosition);
            currentAnchorScreenPosition = _bossLastTaskbarAnchorScreenPosition;
            ApplyBossCounterLayout();
            if (_settingsPanel?.IsOpen == true)
                PositionPanelInBestSlot();
        }
        else if (!snapEnabled)
        {
            _taskbarSnapped = false;
        }

        CaptureBossWorkAreaSnapshot(
            screenIndex,
            screenRect,
            usableRect,
            currentAnchorScreenPosition,
            updateAnchor: _isDragging || workAreaChanged || _taskbarSnapped);
    }

    private void CaptureBossWorkAreaSnapshot(
        int screenIndex,
        Rect2I screenRect,
        Rect2I usableRect,
        Vector2I anchorScreenPosition,
        bool updateAnchor = true)
    {
        _bossWorkAreaSnapshotReady = true;
        _bossWorkAreaScreen = screenIndex;
        _bossLastScreenRect = screenRect;
        _bossLastUsableRect = usableRect;
        if (updateAnchor)
            _bossLastTaskbarAnchorScreenPosition = anchorScreenPosition;
    }

    private static int FindScreenIndexAtPoint(Vector2I screenPoint)
    {
        for (int i = 0; i < DisplayServer.GetScreenCount(); i++)
        {
            var screen = new Rect2I(
                DisplayServer.ScreenGetPosition(i),
                DisplayServer.ScreenGetSize(i));
            if (screen.HasPoint(screenPoint))
                return i;
        }

        return -1;
    }

    private void SetupPlayFatWindow()
    {
        var layout = _panelAvoidanceStrategy.CalculateHostLayout(
            new HostLayoutContext(
                PanelHostMode.Play,
                _contentOffset,
                new Vector2(
                    GetPlayInfoPanelWidth() + GetPlayGameSize(),
                    GetPlayGameSize()),
                _panelSize,
                GetPlayInfoPanelWidth(),
                GetPlayGameSize()));
        _contentOffset = layout.MainContentOrigin;
        // 只 resize，保留窗口当前位置，不让内容跳位
        var pos = DisplayServer.WindowGetPosition();
        DisplayServer.WindowSetSize(layout.WindowSize);
        DisplayServer.WindowSetPosition(pos);
    }

    private void KeepPlayContentWithinScreen()
    {
        int contentW = GetPlayInfoPanelWidth() + GetPlayGameSize();
        int contentH = GetPlayGameSize();
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

    private Rect2I GetPlayGameScreenRect()
    {
        var windowPosition = DisplayServer.WindowGetPosition();
        if (_playViewport == null)
            return new Rect2I(windowPosition, DisplayServer.WindowGetSize());

        return new Rect2I(
            windowPosition + (Vector2I)_playViewport.Position,
            (Vector2I)(_playViewport.Size * _playViewport.Scale));
    }

    private Rect2I GetBossKeyUsableScreenRect()
    {
        var anchorScreenPosition = DisplayServer.WindowGetPosition()
            + RoundToVector2I(GetBossTaskbarAnchorWindowPosition());
        return GetUsableScreenRectAtPoint(anchorScreenPosition);
    }

    private static Rect2I GetUsableScreenRectAtPoint(Vector2I screenPoint)
    {
        for (int i = 0; i < DisplayServer.GetScreenCount(); i++)
        {
            var screen = new Rect2I(DisplayServer.ScreenGetPosition(i), DisplayServer.ScreenGetSize(i));
            if (screen.HasPoint(screenPoint))
                return DisplayServer.ScreenGetUsableRect(i);
        }

        return GetBestScreenUsableRect(new Rect2I(screenPoint, Vector2I.One));
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

    private int GetBottomTaskbarHeightAtWindow()
    {
        var anchorScreenPosition = DisplayServer.WindowGetPosition()
            + RoundToVector2I(GetBossTaskbarAnchorWindowPosition());
        int fallback = 0;

        for (int i = 0; i < DisplayServer.GetScreenCount(); i++)
        {
            var screen = new Rect2I(DisplayServer.ScreenGetPosition(i), DisplayServer.ScreenGetSize(i));
            var usable = DisplayServer.ScreenGetUsableRect(i);
            var bottomHeight = Math.Max(0, screen.End.Y - usable.End.Y);
            if (bottomHeight > 0 && fallback == 0)
                fallback = bottomHeight;

            if (screen.HasPoint(anchorScreenPosition))
                return bottomHeight;
        }

        return fallback;
    }

    private void SetWindowAboveTaskbar()
    {
        var scrRect = GetBossKeyUsableScreenRect();
        int taskbarTop = (int)(scrRect.Position.Y + scrRect.Size.Y);
        int winW = DisplayServer.WindowGetSize().X;
        int anchorY = Mathf.RoundToInt(GetBossTaskbarAnchorWindowPosition().Y);
        int x = (int)(scrRect.Position.X + (scrRect.Size.X - winW) / 2);
        int y = taskbarTop - anchorY;
        DisplayServer.WindowSetPosition(new Vector2I(x, y));
        _taskbarSnapped = SettingsManager.LoadSnapToWindowsTaskbar();
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
            WindowNative.SWP_NOMOVE | WindowNative.SWP_NOSIZE | WindowNative.SWP_NOACTIVATE);
    }

    private static bool SetNativeMainWindowVisible(bool visible)
    {
        if (!OperatingSystem.IsWindows())
            return false;

        var hWnd = (IntPtr)DisplayServer.WindowGetNativeHandle(DisplayServer.HandleType.WindowHandle);
        if (hWnd == IntPtr.Zero)
            return false;

        WindowNative.ShowWindow(
            hWnd,
            visible ? WindowNative.SW_SHOWNOACTIVATE : WindowNative.SW_HIDE);
        return true;
    }

    private static bool SetNativeWindowCloaked(bool cloaked)
    {
        if (!OperatingSystem.IsWindows())
            return false;

        var hWnd = (IntPtr)DisplayServer.WindowGetNativeHandle(DisplayServer.HandleType.WindowHandle);
        if (hWnd == IntPtr.Zero)
            return false;

        int value = cloaked ? 1 : 0;
        int result = WindowNative.DwmSetWindowAttribute(
            hWnd,
            WindowNative.DWMWA_CLOAK,
            ref value,
            sizeof(int));
        if (result >= 0)
            return true;

        GD.PushWarning($"DwmSetWindowAttribute(DWMWA_CLOAK) failed: 0x{result:X8}");
        return false;
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
        if (!_startupInitialized || _startupState < StartupState.IntroPlaying)
            return;
#if DEBUG
        if (@event is InputEventKey { Pressed: true, Echo: false } key
            && !_settingsPanel.IsOpen)
        {
            if (key.Keycode == Key.F2)
            {
                ApplyRandomEquipment(DebugEquipmentSource.AllCatalog);
                GetViewport().SetInputAsHandled();
                return;
            }

            if (key.Keycode == Key.F3)
            {
                ApplyRandomEquipment(DebugEquipmentSource.Owned);
                GetViewport().SetInputAsHandled();
                return;
            }
        }
#endif

        if (@event is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Right }
            && CurrentMode == Mode.Play
            && (_potentialDrag || _isDragging))
        {
            CancelWindowDrag();
            GetViewport().SetInputAsHandled();
            return;
        }

        if (@event is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Right }
            && CurrentMode == Mode.Play
            && SettingsManager.LoadRightClickQuickModeSwitch()
            && _playViewport != null)
        {
            var localPos = DisplayServer.MouseGetPosition() - DisplayServer.WindowGetPosition();
            var gameRect = new Rect2(_playViewport.Position, _playViewport.Size * _playViewport.Scale);
            if (gameRect.HasPoint(localPos))
            {
                SwitchToBossKey();
                GetViewport().SetInputAsHandled();
                return;
            }
        }

        if (@event is InputEventMouseButton mb && mb.ButtonIndex == MouseButton.Left)
        {
            if (mb.Pressed)
            {
                var localPos = DisplayServer.MouseGetPosition() - DisplayServer.WindowGetPosition();
                if (_settingsPanel.ContainsPoint(localPos)) return;
#if DEBUG
                if (_steamMockPanel != null && _steamMockPanel.ContainsPoint(localPos)) return;
#endif
                if (CurrentMode == Mode.BossKey)
                {
                    if (_bossBlindBoxHint != null
                        && _bossBlindBoxHint.Visible
                        && _bossBlindBoxHint.MouseFilter != Control.MouseFilterEnum.Ignore
                        && GetBossBlindBoxHintRect().HasPoint(localPos)) return;
                    if (_bossBlindBoxOverlay != null
                        && _bossBlindBoxOverlay.Visible
                        && GetBossBlindBoxOverlayRect().HasPoint(localPos)) return;
                }
                if (_infoPanel != null && _infoPanel.Visible)
                {
                    int infoX = _infoPanelOnRight ? GetPlayInfoPanelWidth() + GetPlayGameSize() + PlayGameSettingsGap : 0;
                    if (new Rect2(infoX, _contentOffset.Y, GetPlayInfoPanelWidth(), GetPlayGameSize()).HasPoint(localPos)) return;
                }
                _mouseScreenStart = DisplayServer.MouseGetPosition();
                _windowPosStart = DisplayServer.WindowGetPosition();
                _dragPressStartedAtMsec = Time.GetTicksMsec();
                _potentialDrag = true;
            }
            else
            {
                if (_isDragging) GetViewport().SetInputAsHandled();
                _isDragging = false; _potentialDrag = false;
                if (CurrentMode != Mode.BossKey || !SettingsManager.LoadSnapToWindowsTaskbar())
                    _taskbarSnapped = false;
            }
        }
        else if (@event is InputEventMouseMotion && _potentialDrag)
        {
            var mouseScreenPosition = DisplayServer.MouseGetPosition();
            var d = mouseScreenPosition - _mouseScreenStart;
            bool useAccidentalDragProtection = CurrentMode == Mode.Play
                && SettingsManager.LoadPreventAccidentalDrag();
            float dragThreshold = useAccidentalDragProtection
                ? ProtectedPlayDragThreshold
                : DefaultDragThreshold;
            bool movedFarEnough = d.LengthSquared() >= dragThreshold * dragThreshold;
            bool heldLongEnough = !useAccidentalDragProtection
                || Time.GetTicksMsec() - _dragPressStartedAtMsec >= ProtectedPlayDragHoldDelayMsec;
            if (!_isDragging && movedFarEnough && heldLongEnough)
            {
                // 确认是拖拽意图后才重新取锚点，避免窗口补跳此前点击抖动产生的距离。
                _isDragging = true;
                _mouseScreenStart = mouseScreenPosition;
                _windowPosStart = DisplayServer.WindowGetPosition();
                d = Vector2I.Zero;
                SetClickThrough(false);
            }
            if (_isDragging)
            {
                var newPos = _windowPosStart + d;
                ApplyTaskbarSnap(ref newPos);
                DisplayServer.WindowSetPosition(newPos);
                if (CurrentMode == Mode.BossKey)
                    ApplyBossCounterLayout();
                if (_settingsPanel.IsOpen && _panelAvoidanceStrategy.ReflowWhileDragging)
                    PositionPanelInBestSlot();
                if (CurrentMode == Mode.Play)
                    UpdatePlayLayout();
                GetViewport().SetInputAsHandled();
            }
        }
    }

    private void CancelWindowDrag()
    {
        _isDragging = false;
        _potentialDrag = false;
        _taskbarSnapped = false;
    }

#if DEBUG
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
#endif
}
