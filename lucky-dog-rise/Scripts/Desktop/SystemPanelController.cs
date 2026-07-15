using Godot;
using System;
using System.Collections.Generic;
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
#endif
    [Signal] public delegate void SwitchToPlayRequestedEventHandler();
    [Signal] public delegate void SwitchToBossKeyRequestedEventHandler();
    [Signal] public delegate void DesktopBgmPlaybackChangedEventHandler(bool enabled);
    [Signal] public delegate void BlindBoxBubbleVisibilityChangedEventHandler();
    [Signal] public delegate void CounterLayoutChangedEventHandler();

    [Export] private Label _buildVersionLabel = null!;

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
    private Tween _tween = null!;

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
    private Control _linkTreeContent = null!;
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
    private CheckButton _autoEquipToggle = null!;
    private CheckButton _taskbarSnapToggle = null!;
    private ConfirmOverlayController _resetSaveConfirm = null!;

#if DEBUG
    // Debug 页
    private Label _seedLabel = null!;
    private Label _playTimeLabel = null!;
    private Label _luckyDealBuffLabel = null!;
    private Button _blindBoxDebugToggle = null!;
    private Control _blindBoxDebugContent = null!;
    private Label _blindBoxDebugLabel = null!;
    private LineEdit _seedInput = null!;
    private OptionButton _reactionOption = null!;
    private int _currentSeed;
    private double _debugTimeRefreshTimer;
#endif

    // Wardrobe 页
    private GridContainer _wardrobeGrid = null!;
    private Control _emptyWardrobeCenter = null!;
    private HBoxContainer _typeFilterRow = null!;
    private TabGroup _selectedTab = null!;
    private Label _emptyWardrobeLabel = null!;
    private GameData _gameData = null!;
    public GameData GameData
    {
        get => _gameData;
        set
        {
            _gameData = value;
            _gameData.EquipmentChanged += RefreshWardrobeGrid;
            _gameData.InventoryChanged += RefreshWardrobeGrid;
            EnsureCurrentTabReady();
        }
    }

    private readonly List<Button> _tabs = new();
    private readonly List<Control> _tabContents = new();
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
    ];
    private static readonly IReadOnlyDictionary<int, Texture2D> TabIconsByGroupId = new Dictionary<int, Texture2D>
    {
        [1001] = GD.Load<Texture2D>("res://Assets/UI/Icon/TabIcon_Dog.svg"),
        [1002] = GD.Load<Texture2D>("res://Assets/UI/Icon/TabIcon_Headwear.svg"),
        [1003] = GD.Load<Texture2D>("res://Assets/UI/Icon/TabIcon_Eyewear.svg"),
        [1004] = GD.Load<Texture2D>("res://Assets/UI/Icon/TabIcon_Player.svg"),
        [1005] = GD.Load<Texture2D>("res://Assets/UI/Icon/TabIcon_Theme.svg"),
        [1006] = GD.Load<Texture2D>("res://Assets/UI/Icon/TabIcon_Refreshment.svg"),
    };

    public override void _Ready()
    {
        _panel = GetNode<PanelContainer>("Panel");

        _settingsTab = GetNode<Button>("Panel/RootVBox/TitleRow/SettingsTab");
        _wardrobeTab = GetNode<Button>("Panel/RootVBox/TitleRow/WardrobeTab");
        _linkTreeTab = GetNode<Button>("Panel/RootVBox/TitleRow/LinkTreeTab");
        _tabs.Add(_wardrobeTab);
        _tabs.Add(_linkTreeTab);
        _tabs.Add(_settingsTab);

        _settingsContent = GetNode<VBoxContainer>("Panel/RootVBox/Scroll/ContentVBox/SettingsContent");
        _wardrobeContent = GetNode<VBoxContainer>("Panel/RootVBox/Scroll/ContentVBox/WardrobeContent");
        _linkTreeContent = GetNode<Control>("Panel/RootVBox/Scroll/ContentVBox/LinkTreeContent");
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
#if DEBUG
        _debugTab = GetNode<Button>("Panel/RootVBox/TitleRow/DebugTab");
        _debugContent = GetNode<VBoxContainer>("Panel/RootVBox/Scroll/ContentVBox/DebugContent");
        _tabs.Add(_debugTab);
        _tabContents.Add(_debugContent);
        _debugTab.Pressed += () => SwitchTab(3);
#else
        GetNode("Panel/RootVBox/TitleRow/DebugTab").Free();
        GetNode("Panel/RootVBox/Scroll/ContentVBox/DebugContent").Free();
#endif
        SwitchTab(0);
        _buildVersionLabel.Text = BuildInfo.DisplayVersion;

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
        _resetSaveConfirm = GetNode<ConfirmOverlayController>("ResetSaveConfirm");
        var closeBtn = GetNode<Button>("Panel/RootVBox/TitleRow/CloseBtn");
        var quitBtn = GetNode<Button>("Panel/RootVBox/SettingsActionRow/QuitBtn");
        var restartBtn = GetNode<Button>("Panel/RootVBox/SettingsActionRow/RestartBtn");
        var resetSaveBtn = GetNode<Button>("Panel/RootVBox/Scroll/ContentVBox/SettingsContent/ResetSaveBtn");

        BuildLanguageOptions();

        BuildDisplayOptions();

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

        closeBtn.Pressed += Close;
        quitBtn.Pressed += () => GetTree().Quit();
        restartBtn.Pressed += RestartGame;
        resetSaveBtn.Pressed += () =>
            _resetSaveConfirm.ShowConfirmKey(
                L10nKey.Settings_ResetSaveData,
                L10nKey.Settings_ResetSaveMessage,
                L10nKey.Settings_ResetSaveConfirm,
                L10nKey.Common_Cancel);
        _resetSaveConfirm.Confirmed += OnResetSaveConfirmed;

        _switchToPlayBtn = GetNode<Button>("Panel/RootVBox/SettingsActionRow/SwitchToPlayBtn");
        _switchToBossKeyBtn = GetNode<Button>("Panel/RootVBox/SettingsActionRow/SwitchToBossKeyBtn");
        _switchToPlayBtn.Pressed += () => EmitSignal(SignalName.SwitchToPlayRequested);
        _switchToBossKeyBtn.Pressed += () => EmitSignal(SignalName.SwitchToBossKeyRequested);
        RefreshModeButtonText();

        _sfxVolumeSlider.ValueChanged += OnSfxVolumeChanged;
        _bgmVolumeSlider.ValueChanged += OnBgmVolumeChanged;
        _desktopBgmToggle.Toggled += OnDesktopBgmToggled;
        _rightClickQuickModeSwitchToggle.Toggled += enabled => SettingsManager.SaveRightClickQuickModeSwitch(enabled);
        _preventAccidentalDragToggle.ButtonPressed = SettingsManager.LoadPreventAccidentalDrag();
        _preventAccidentalDragToggle.Toggled += enabled => SettingsManager.SavePreventAccidentalDrag(enabled);
        _languageOption.ItemSelected += OnLanguageSelected;
        _displayOption.ItemSelected += OnDisplayModeChanged;
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
        var seedCopyBtn = GetNode<Button>("Panel/RootVBox/Scroll/ContentVBox/DebugContent/SeedRow/SeedCopyBtn");
        _seedInput = GetNode<LineEdit>("Panel/RootVBox/Scroll/ContentVBox/DebugContent/SeedInput");
        var grantChipsBtn = GetNode<Button>("Panel/RootVBox/Scroll/ContentVBox/DebugContent/GrantChipsBtn");
        var grantLuckyDealsBtn = GetNode<Button>("Panel/RootVBox/Scroll/ContentVBox/DebugContent/GrantLuckyDealsBtn");
        var resetSettingsBtn = GetNode<Button>("Panel/RootVBox/Scroll/ContentVBox/DebugContent/ResetSettingsBtn");
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
        resetSettingsBtn.Pressed += ResetSettingsToDefaults;
        _blindBoxDebugToggle.Pressed += ToggleBlindBoxDebug;
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

    public override void _Process(double delta)
    {
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

#if DEBUG
    private void RefreshDebugPlayTime()
    {
        if (_playTimeLabel == null || _gameData == null)
            return;

        var total = TimeSpan.FromSeconds(_gameData.TotalPlaySeconds);
        _playTimeLabel.Text = $"Play Time: {total:hh\\:mm\\:ss} ({_gameData.TotalPlaySeconds:0.0}s)";
        _luckyDealBuffLabel.Text = $"Lucky Deal Buff: {_gameData.LuckyDealRemainingHands} hands remaining";
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
            btn.TooltipText = tab.TabName;
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

    private void RefreshWardrobeGrid()
    {
        if (_wardrobeContent.Visible && _selectedTab != null)
            PopulateWardrobeGrid(_selectedTab);
    }

    private static readonly PackedScene ItemCellScene = GD.Load<PackedScene>("res://Scenes/Prefabs/ItemCell.tscn");

    private Node CreateItemCell(Item item)
    {
        var cell = ItemCellScene.Instantiate<ItemCellController>();
        cell.Setup(
            item,
            _gameData.Inventory.IsEquipped(item.Id),
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
        _tween = CreateTween();
        _tween.TweenProperty(_panel, "modulate:a", 1f, 0.15f).SetEase(Tween.EaseType.Out);
    }

    public void SetPanelPosition(Vector2 pos)
    {
        _panel.Position = pos;
        _resetSaveConfirm?.SetOverlayRect(pos, PanelSize);
    }

    public void Close()
    {
        if (_resetSaveConfirm != null)
            _resetSaveConfirm.Visible = false;
        if (_tween != null && _tween.IsRunning()) _tween.Kill();
        _tween = CreateTween();
        _tween.TweenProperty(_panel, "modulate:a", 0f, 0.1f).SetEase(Tween.EaseType.In);
        _tween.TweenCallback(Callable.From(() => _panel.Visible = false));
    }

    public void CloseImmediate()
    {
        if (_resetSaveConfirm != null)
            _resetSaveConfirm.Visible = false;
        if (_tween != null && _tween.IsRunning()) _tween.Kill();
        _panel.Modulate = Colors.White with { A = 0f };
        _panel.Visible = false;
    }

    private void RestartGame()
    {
        _gameData?.SaveImmediatelyIfUsingLocalSave();
        OS.CreateInstance(OS.GetCmdlineArgs());
        GetTree().Quit();
    }

    public bool ContainsPoint(Vector2 windowPos)
    {
        if (!_panel.Visible) return false;
        return new Rect2(_panel.Position, PanelSize).HasPoint(windowPos)
            || (_resetSaveConfirm.Visible && new Rect2(_resetSaveConfirm.Position, _resetSaveConfirm.Size).HasPoint(windowPos))
            || PopupContainsPoint(_languageOption.GetPopup(), windowPos)
            || PopupContainsPoint(_displayOption.GetPopup(), windowPos)
#if DEBUG
            || PopupContainsPoint(_reactionOption.GetPopup(), windowPos)
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
        _displayOption.AddItem(L10n.Tr(L10nKey.Settings_CounterDisplay_Hidden), (int)SettingsManager.DisplayMode.Hidden);
        _displayOption.Select((int)SettingsManager.LoadDisplayMode());
    }

    private void RefreshLocalizedOptionText()
    {
        if (_languageOption == null)
            return;

        var selected = _languageOption.GetSelectedId();
        for (int i = 0; i < LocaleOptions.Length; i++)
            _languageOption.SetItemText(i, L10n.GetDisplayName(LocaleOptions[i]));
        if (selected >= 0)
            _languageOption.Select(selected);

        if (_displayOption != null && _displayOption.ItemCount >= 3)
        {
            _displayOption.SetItemText(0, L10n.Tr(L10nKey.Settings_CounterDisplay_Clock));
            _displayOption.SetItemText(1, L10n.Tr(L10nKey.Settings_CounterDisplay_Chips));
            _displayOption.SetItemText(2, L10n.Tr(L10nKey.Settings_CounterDisplay_Hidden));
        }

        RefreshModeButtonText();
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
        SettingsManager.SaveDisplayMode((SettingsManager.DisplayMode)(int)index);
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

    private void OnResetSaveConfirmed()
    {
        _gameData.ResetLocalSave();
        _wardrobeBuilt = false;
        if (_wardrobeContent.Visible)
            BuildWardrobe();
    }

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
        _rightClickQuickModeSwitchToggle.SetPressedNoSignal(SettingsManager.LoadRightClickQuickModeSwitch());
        _autoEquipToggle.SetPressedNoSignal(SettingsManager.LoadAutoEquipNewOutfits());
        _taskbarSnapToggle.SetPressedNoSignal(SettingsManager.LoadSnapToWindowsTaskbar());
        GetNode<CheckButton>("Panel/RootVBox/Scroll/ContentVBox/SettingsContent/StreamerSafeRow/StreamerSafeToggle")
            .SetPressedNoSignal(SettingsManager.LoadStreamerSafeMode());
        GetNode<CheckButton>("Panel/RootVBox/Scroll/ContentVBox/SettingsContent/CounterCenterRow/CounterCenterToggle")
            .SetPressedNoSignal(SettingsManager.LoadCenterCounterOnTaskbar());

        BuildLanguageOptions();
        BuildDisplayOptions();
#if DEBUG
        _saveDataModeOption.Select((int)SettingsManager.LoadSaveDataMode());
#endif
        RefreshLocalizedOptionText();
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
