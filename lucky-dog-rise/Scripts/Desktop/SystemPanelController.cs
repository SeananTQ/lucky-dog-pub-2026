using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using DataTables;

namespace LuckyDogRise;

public partial class SystemPanelController : CanvasLayer
{
    [Signal] public delegate void RandomizeRequestedEventHandler();
    [Signal] public delegate void RandomizeDogRequestedEventHandler();
    [Signal] public delegate void RandomAcquireItemRequestedEventHandler();
    [Signal] public delegate void DebugGrantChipsRequestedEventHandler();
    [Signal] public delegate void DogReactionRequestedEventHandler(int trigger);
    [Signal] public delegate void SwitchToPlayRequestedEventHandler();
    [Signal] public delegate void SwitchToBossKeyRequestedEventHandler();
    [Signal] public delegate void DebugBlindBoxCountdownBubbleVisibilityChangedEventHandler();

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
    private Button _debugTab = null!;

    // 页签内容容器
    private VBoxContainer _settingsContent = null!;
    private VBoxContainer _wardrobeContent = null!;
    private Control _linkTreeContent = null!;
    private VBoxContainer _debugContent = null!;
    private Control _settingsActionTopGap = null!;
    private Control _settingsActionRow = null!;
    private Control _settingsActionBottomGap = null!;
    private Control _settingsActionSep = null!;

    // Settings 页
    private CheckButton _audioToggle = null!;
    private OptionButton _displayOption = null!;
    private OptionButton _saveDataModeOption = null!;
    private ConfirmOverlayController _resetSaveConfirm = null!;

    // Debug 页
    private Label _seedLabel = null!;
    private Label _playTimeLabel = null!;
    private Button _blindBoxDebugToggle = null!;
    private Control _blindBoxDebugContent = null!;
    private Label _blindBoxDebugLabel = null!;
    private LineEdit _seedInput = null!;
    private OptionButton _reactionOption = null!;
    private int _currentSeed;
    private double _debugTimeRefreshTimer;

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

    private readonly Button[] _tabs = new Button[4];
    private readonly Dictionary<Button, TabGroup> _filterTabs = new();
    private readonly List<Button> _typeFilterButtons = new();
    private static readonly StringName PanelTopTabStyle = "PanelTopTab";
    private static readonly StringName PanelTopTabSelectedStyle = "PanelTopTabSelected";
    private static readonly StringName CategoryTabStyle = "CategoryTab";
    private static readonly StringName CategoryTabSelectedStyle = "CategoryTabSelected";
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
        _debugTab = GetNode<Button>("Panel/RootVBox/TitleRow/DebugTab");
        _tabs[0] = _wardrobeTab;
        _tabs[1] = _linkTreeTab;
        _tabs[2] = _settingsTab;
        _tabs[3] = _debugTab;

        _settingsContent = GetNode<VBoxContainer>("Panel/RootVBox/Scroll/ContentVBox/SettingsContent");
        _wardrobeContent = GetNode<VBoxContainer>("Panel/RootVBox/Scroll/ContentVBox/WardrobeContent");
        _linkTreeContent = GetNode<Control>("Panel/RootVBox/Scroll/ContentVBox/LinkTreeContent");
        _debugContent = GetNode<VBoxContainer>("Panel/RootVBox/Scroll/ContentVBox/DebugContent");
        _settingsActionTopGap = GetNode<Control>("Panel/RootVBox/ActionTopGap");
        _settingsActionRow = GetNode<Control>("Panel/RootVBox/SettingsActionRow");
        _settingsActionBottomGap = GetNode<Control>("Panel/RootVBox/ActionBottomGap");
        _settingsActionSep = GetNode<Control>("Panel/RootVBox/ActionSep");

        _wardrobeTab.Pressed += () => SwitchTab(0);
        _linkTreeTab.Pressed += () => SwitchTab(1);
        _settingsTab.Pressed += () => SwitchTab(2);
        _debugTab.Pressed += () => SwitchTab(3);
        SwitchTab(0);

        // === Settings 页 ===
        _audioToggle = GetNode<CheckButton>("Panel/RootVBox/Scroll/ContentVBox/SettingsContent/AudioRow/AudioToggle");
        _displayOption = GetNode<OptionButton>("Panel/RootVBox/Scroll/ContentVBox/SettingsContent/DisplayRow/DisplayOption");
        _saveDataModeOption = GetNode<OptionButton>("Panel/RootVBox/Scroll/ContentVBox/DebugContent/SaveDataModeRow/SaveDataModeOption");
        _resetSaveConfirm = GetNode<ConfirmOverlayController>("ResetSaveConfirm");
        var closeBtn = GetNode<Button>("Panel/RootVBox/TitleRow/CloseBtn");
        var quitBtn = GetNode<Button>("Panel/RootVBox/SettingsActionRow/QuitBtn");
        var resetSaveBtn = GetNode<Button>("Panel/RootVBox/Scroll/ContentVBox/SettingsContent/ResetSaveBtn");

        _displayOption.AddItem("Clock", 0);
        _displayOption.AddItem("Chips", 1);
        _displayOption.AddItem("Hidden", 2);
        _displayOption.Select((int)SettingsManager.LoadDisplayMode());

        _saveDataModeOption.AddItem("调试全道具", (int)SettingsManager.SaveDataMode.DebugAllItems);
        _saveDataModeOption.AddItem("本地存档", (int)SettingsManager.SaveDataMode.LocalSave);
        _saveDataModeOption.Select((int)SettingsManager.LoadSaveDataMode());

        _audioToggle.ButtonPressed = SettingsManager.LoadAudioEnabled();
        ApplyAudio(_audioToggle.ButtonPressed);

        var autoHideToggle = GetNode<CheckButton>("Panel/RootVBox/Scroll/ContentVBox/SettingsContent/AutoHideRow/AutoHideToggle");
        autoHideToggle.ButtonPressed = SettingsManager.LoadAutoHidePanel();
        autoHideToggle.Toggled += enabled => SettingsManager.SaveAutoHidePanel(enabled);

        var tongueImmediateToggle = GetNode<CheckButton>("Panel/RootVBox/Scroll/ContentVBox/SettingsContent/TongueImmediateRow/TongueImmediateToggle");
        tongueImmediateToggle.ButtonPressed = SettingsManager.LoadDesktopTongueImmediateMode();
        tongueImmediateToggle.Toggled += enabled => SettingsManager.SaveDesktopTongueImmediateMode(enabled);

        var showFullscreenToggle = GetNode<CheckButton>("Panel/RootVBox/Scroll/ContentVBox/SettingsContent/ShowFullscreenRow/ShowFullscreenToggle");
        showFullscreenToggle.ButtonPressed = SettingsManager.LoadShowOverFullscreenApps();
        showFullscreenToggle.Toggled += enabled => SettingsManager.SaveShowOverFullscreenApps(enabled);

        var enhancedTopmostToggle = GetNode<CheckButton>("Panel/RootVBox/Scroll/ContentVBox/SettingsContent/EnhancedTopmostRow/EnhancedTopmostToggle");
        enhancedTopmostToggle.ButtonPressed = SettingsManager.LoadEnhancedTopmostMode();
        enhancedTopmostToggle.Toggled += enabled => SettingsManager.SaveEnhancedTopmostMode(enabled);

        closeBtn.Pressed += Close;
        quitBtn.Pressed += () => GetTree().Quit();
        resetSaveBtn.Pressed += () =>
            _resetSaveConfirm.ShowConfirm(
                "Reset Save Data",
                "Reset local save data? This will clear chips, owned items, equipment, and create a fresh default save.",
                "Reset",
                "Cancel");
        _resetSaveConfirm.Confirmed += OnResetSaveConfirmed;

        var switchToPlayBtn = GetNode<Button>("Panel/RootVBox/SettingsActionRow/SwitchToPlayBtn");
        var switchToBossKeyBtn = GetNode<Button>("Panel/RootVBox/SettingsActionRow/SwitchToBossKeyBtn");
        switchToPlayBtn.Pressed += () => EmitSignal(SignalName.SwitchToPlayRequested);
        switchToBossKeyBtn.Pressed += () => EmitSignal(SignalName.SwitchToBossKeyRequested);

        _audioToggle.Toggled += OnAudioToggled;
        _displayOption.ItemSelected += OnDisplayModeChanged;
        _saveDataModeOption.ItemSelected += OnSaveDataModeChanged;

        // === Debug 页 ===
        _seedLabel = GetNode<Label>("Panel/RootVBox/Scroll/ContentVBox/DebugContent/SeedRow/SeedLabel");
        _playTimeLabel = GetNode<Label>("Panel/RootVBox/Scroll/ContentVBox/DebugContent/PlayTimeLabel");
        _blindBoxDebugToggle = GetNode<Button>("Panel/RootVBox/Scroll/ContentVBox/DebugContent/BlindBoxDebugToggle");
        _blindBoxDebugContent = GetNode<Control>("Panel/RootVBox/Scroll/ContentVBox/DebugContent/BlindBoxDebugContent");
        _blindBoxDebugLabel = GetNode<Label>("Panel/RootVBox/Scroll/ContentVBox/DebugContent/BlindBoxDebugContent/BlindBoxDebugLabel");
        var seedCopyBtn = GetNode<Button>("Panel/RootVBox/Scroll/ContentVBox/DebugContent/SeedRow/SeedCopyBtn");
        _seedInput = GetNode<LineEdit>("Panel/RootVBox/Scroll/ContentVBox/DebugContent/SeedInput");
        var grantChipsBtn = GetNode<Button>("Panel/RootVBox/Scroll/ContentVBox/DebugContent/GrantChipsBtn");
        var randomizeSceneBtn = GetNode<Button>("Panel/RootVBox/Scroll/ContentVBox/DebugContent/RandomizeSceneBtn");
        var randomizeDogBtn = GetNode<Button>("Panel/RootVBox/Scroll/ContentVBox/DebugContent/RandomizeDogBtn");
        var randomAcquireItemBtn = GetNode<Button>("Panel/RootVBox/Scroll/ContentVBox/DebugContent/RandomAcquireItemBtn");
        var hideDebugTabBtn = GetNode<Button>("Panel/RootVBox/Scroll/ContentVBox/DebugContent/HideDebugTabBtn");
        var hideCountdownBubbleToggle = GetNode<CheckButton>("Panel/RootVBox/Scroll/ContentVBox/DebugContent/HideCountdownBubbleRow/HideCountdownBubbleToggle");
        _reactionOption = GetNode<OptionButton>("Panel/RootVBox/Scroll/ContentVBox/DebugContent/ReactionRow/ReactionOption");
        var playReactionBtn = GetNode<Button>("Panel/RootVBox/Scroll/ContentVBox/DebugContent/ReactionRow/PlayReactionBtn");

        hideCountdownBubbleToggle.ButtonPressed = SettingsManager.LoadDebugHideBlindBoxCountdownBubble();
        hideCountdownBubbleToggle.Toggled += enabled =>
        {
            SettingsManager.SaveDebugHideBlindBoxCountdownBubble(enabled);
            EmitSignal(SignalName.DebugBlindBoxCountdownBubbleVisibilityChanged);
        };
        seedCopyBtn.Pressed += () => DisplayServer.ClipboardSet(_currentSeed.ToString());
        grantChipsBtn.Pressed += () => EmitSignal(SignalName.DebugGrantChipsRequested);
        _blindBoxDebugToggle.Pressed += ToggleBlindBoxDebug;
        randomizeSceneBtn.Pressed += () => EmitSignal(SignalName.RandomizeRequested);
        randomizeDogBtn.Pressed += () => EmitSignal(SignalName.RandomizeDogRequested);
        randomAcquireItemBtn.Pressed += () => EmitSignal(SignalName.RandomAcquireItemRequested);
        hideDebugTabBtn.Pressed += HideDebugTabForSession;
        BuildReactionOptions();
        playReactionBtn.Pressed += () =>
            EmitSignal(SignalName.DogReactionRequested, _reactionOption.GetSelectedId());

        // === Wardrobe 页 ===
        _wardrobeGrid = GetNode<GridContainer>("Panel/RootVBox/Scroll/ContentVBox/WardrobeContent/WardrobeScroll/WardrobeGrid");
        _typeFilterRow = GetNode<HBoxContainer>("Panel/RootVBox/Scroll/ContentVBox/WardrobeContent/TypeFilterRow");
        _emptyWardrobeCenter = GetNode<Control>("Panel/RootVBox/Scroll/ContentVBox/WardrobeContent/WardrobeScroll/EmptyWardrobeCenter");
        _emptyWardrobeLabel = GetNode<Label>("Panel/RootVBox/Scroll/ContentVBox/WardrobeContent/WardrobeScroll/EmptyWardrobeCenter/EmptyWardrobeLabel");

        _panel.Visible = false;
        RefreshDebugPlayTime();
    }

    public override void _Process(double delta)
    {
        if (_gameData == null || !_debugContent.Visible)
            return;

        _debugTimeRefreshTimer -= delta;
        if (_debugTimeRefreshTimer > 0)
            return;

        _debugTimeRefreshTimer = 1.0;
        RefreshDebugPlayTime();
    }

    private void SwitchTab(int index)
    {
        var contents = new Control[] { _wardrobeContent, _linkTreeContent, _settingsContent, _debugContent };
        for (int i = 0; i < _tabs.Length; i++)
        {
            contents[i].Visible = i == index;
            _tabs[i].ThemeTypeVariation = i == index ? PanelTopTabSelectedStyle : PanelTopTabStyle;
        }
        _settingsActionTopGap.Visible = index == 2;
        _settingsActionRow.Visible = index == 2;
        _settingsActionBottomGap.Visible = index == 2;
        _settingsActionSep.Visible = index == 2;
        if (index == 0 && _gameData != null)
            BuildWardrobe();
        if (index == 3)
            RefreshDebugPlayTime();
    }

    private void EnsureCurrentTabReady()
    {
        if (_gameData == null)
            return;

        if (_wardrobeContent?.Visible == true)
            BuildWardrobe();
        if (_debugContent?.Visible == true)
            RefreshDebugPlayTime();
    }

    private void RefreshDebugPlayTime()
    {
        if (_playTimeLabel == null || _gameData == null)
            return;

        var total = TimeSpan.FromSeconds(_gameData.TotalPlaySeconds);
        _playTimeLabel.Text = $"Play Time: {total:hh\\:mm\\:ss} ({_gameData.TotalPlaySeconds:0.0}s)";
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
            .OrderByDescending(item => _gameData.Inventory.IsNew(item.Id))
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

    public bool ContainsPoint(Vector2 windowPos)
    {
        if (!_panel.Visible) return false;
        return new Rect2(_panel.Position, PanelSize).HasPoint(windowPos)
            || (_resetSaveConfirm.Visible && new Rect2(_resetSaveConfirm.Position, _resetSaveConfirm.Size).HasPoint(windowPos))
            || PopupContainsPoint(_displayOption.GetPopup(), windowPos)
            || PopupContainsPoint(_reactionOption.GetPopup(), windowPos);
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

    // ===== 设置回调 =====

    private void OnAudioToggled(bool enabled)
    {
        SettingsManager.SaveAudioEnabled(enabled);
        ApplyAudio(enabled);
    }

    private void OnDisplayModeChanged(long index)
    {
        SettingsManager.SaveDisplayMode((SettingsManager.DisplayMode)(int)index);
    }

    private void OnSaveDataModeChanged(long index)
    {
        _gameData.SetSaveDataMode((SettingsManager.SaveDataMode)(int)index);
        _wardrobeBuilt = false;
        if (_wardrobeContent.Visible)
            BuildWardrobe();
    }

    private void OnResetSaveConfirmed()
    {
        _gameData.ResetLocalSave();
        _wardrobeBuilt = false;
        if (_wardrobeContent.Visible)
            BuildWardrobe();
    }

    private static void ApplyAudio(bool enabled)
    {
        AudioManager.Instance.SetSfxVolume(enabled ? 1f : 0f);
        AudioManager.Instance.SetBgmVolume(enabled ? 0.7f : 0f);
    }
}
