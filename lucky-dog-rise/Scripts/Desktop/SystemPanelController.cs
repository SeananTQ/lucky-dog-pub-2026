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
    private Button _debugTab = null!;

    // 页签内容容器
    private VBoxContainer _settingsContent = null!;
    private VBoxContainer _wardrobeContent = null!;
    private VBoxContainer _debugContent = null!;

    // Settings 页
    private CheckButton _audioToggle = null!;
    private OptionButton _displayOption = null!;
    private OptionButton _saveDataModeOption = null!;
    private ConfirmOverlayController _resetSaveConfirm = null!;

    // Debug 页
    private Label _seedLabel = null!;
    private Label _playTimeLabel = null!;
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
        }
    }

    private readonly Button[] _tabs = new Button[3];
    private readonly Dictionary<Button, TabGroup> _filterTabs = new();
    private readonly List<Button> _typeFilterButtons = new();

    public override void _Ready()
    {
        _panel = GetNode<PanelContainer>("Panel");

        _settingsTab = GetNode<Button>("Panel/Scroll/RootVBox/TitleRow/SettingsTab");
        _wardrobeTab = GetNode<Button>("Panel/Scroll/RootVBox/TitleRow/WardrobeTab");
        _debugTab = GetNode<Button>("Panel/Scroll/RootVBox/TitleRow/DebugTab");
        _tabs[0] = _settingsTab;
        _tabs[1] = _wardrobeTab;
        _tabs[2] = _debugTab;

        _settingsContent = GetNode<VBoxContainer>("Panel/Scroll/RootVBox/SettingsContent");
        _wardrobeContent = GetNode<VBoxContainer>("Panel/Scroll/RootVBox/WardrobeContent");
        _debugContent = GetNode<VBoxContainer>("Panel/Scroll/RootVBox/DebugContent");

        _settingsTab.Pressed += () => SwitchTab(0);
        _wardrobeTab.Pressed += () => SwitchTab(1);
        _debugTab.Pressed += () => SwitchTab(2);
        SwitchTab(0);

        // === Settings 页 ===
        _audioToggle = GetNode<CheckButton>("Panel/Scroll/RootVBox/SettingsContent/AudioRow/AudioToggle");
        _displayOption = GetNode<OptionButton>("Panel/Scroll/RootVBox/SettingsContent/DisplayRow/DisplayOption");
        _saveDataModeOption = GetNode<OptionButton>("Panel/Scroll/RootVBox/DebugContent/SaveDataModeRow/SaveDataModeOption");
        _resetSaveConfirm = GetNode<ConfirmOverlayController>("ResetSaveConfirm");
        var closeBtn = GetNode<Button>("Panel/Scroll/RootVBox/TitleRow/CloseBtn");
        var quitBtn = GetNode<Button>("Panel/Scroll/RootVBox/SettingsContent/QuitBtn");
        var resetSaveBtn = GetNode<Button>("Panel/Scroll/RootVBox/SettingsContent/ResetSaveBtn");

        _displayOption.AddItem("Clock", 0);
        _displayOption.AddItem("Chips", 1);
        _displayOption.AddItem("Hidden", 2);
        _displayOption.Select((int)SettingsManager.LoadDisplayMode());

        _saveDataModeOption.AddItem("调试全道具", (int)SettingsManager.SaveDataMode.DebugAllItems);
        _saveDataModeOption.AddItem("本地存档", (int)SettingsManager.SaveDataMode.LocalSave);
        _saveDataModeOption.Select((int)SettingsManager.LoadSaveDataMode());

        _audioToggle.ButtonPressed = SettingsManager.LoadAudioEnabled();
        ApplyAudio(_audioToggle.ButtonPressed);

        var autoHideToggle = GetNode<CheckButton>("Panel/Scroll/RootVBox/SettingsContent/AutoHideRow/AutoHideToggle");
        autoHideToggle.ButtonPressed = SettingsManager.LoadAutoHidePanel();
        autoHideToggle.Toggled += enabled => SettingsManager.SaveAutoHidePanel(enabled);

        var tongueImmediateToggle = GetNode<CheckButton>("Panel/Scroll/RootVBox/SettingsContent/TongueImmediateRow/TongueImmediateToggle");
        tongueImmediateToggle.ButtonPressed = SettingsManager.LoadDesktopTongueImmediateMode();
        tongueImmediateToggle.Toggled += enabled => SettingsManager.SaveDesktopTongueImmediateMode(enabled);

        var showFullscreenToggle = GetNode<CheckButton>("Panel/Scroll/RootVBox/SettingsContent/ShowFullscreenRow/ShowFullscreenToggle");
        showFullscreenToggle.ButtonPressed = SettingsManager.LoadShowOverFullscreenApps();
        showFullscreenToggle.Toggled += enabled => SettingsManager.SaveShowOverFullscreenApps(enabled);

        var enhancedTopmostToggle = GetNode<CheckButton>("Panel/Scroll/RootVBox/SettingsContent/EnhancedTopmostRow/EnhancedTopmostToggle");
        enhancedTopmostToggle.ButtonPressed = SettingsManager.LoadEnhancedTopmostMode();
        enhancedTopmostToggle.Toggled += enabled => SettingsManager.SaveEnhancedTopmostMode(enabled);

        closeBtn.Pressed += Close;
        quitBtn.Pressed += () => GetTree().Quit();
        resetSaveBtn.Pressed += () =>
            _resetSaveConfirm.ShowConfirm(
                "重置存档",
                "确定要重置本地存档吗？此操作会清空筹码、背包拥有状态和装备状态，并创建一份新的默认存档。",
                "重置",
                "取消");
        _resetSaveConfirm.Confirmed += OnResetSaveConfirmed;

        var switchToPlayBtn = GetNode<Button>("Panel/Scroll/RootVBox/SettingsContent/SwitchToPlayBtn");
        var switchToBossKeyBtn = GetNode<Button>("Panel/Scroll/RootVBox/SettingsContent/SwitchToBossKeyBtn");
        switchToPlayBtn.Pressed += () => EmitSignal(SignalName.SwitchToPlayRequested);
        switchToBossKeyBtn.Pressed += () => EmitSignal(SignalName.SwitchToBossKeyRequested);

        _audioToggle.Toggled += OnAudioToggled;
        _displayOption.ItemSelected += OnDisplayModeChanged;
        _saveDataModeOption.ItemSelected += OnSaveDataModeChanged;

        // === Debug 页 ===
        _seedLabel = GetNode<Label>("Panel/Scroll/RootVBox/DebugContent/SeedRow/SeedLabel");
        _playTimeLabel = GetNode<Label>("Panel/Scroll/RootVBox/DebugContent/PlayTimeLabel");
        var seedCopyBtn = GetNode<Button>("Panel/Scroll/RootVBox/DebugContent/SeedRow/SeedCopyBtn");
        _seedInput = GetNode<LineEdit>("Panel/Scroll/RootVBox/DebugContent/SeedInput");
        var grantChipsBtn = GetNode<Button>("Panel/Scroll/RootVBox/DebugContent/GrantChipsBtn");
        var randomizeSceneBtn = GetNode<Button>("Panel/Scroll/RootVBox/DebugContent/RandomizeSceneBtn");
        var randomizeDogBtn = GetNode<Button>("Panel/Scroll/RootVBox/DebugContent/RandomizeDogBtn");
        var randomAcquireItemBtn = GetNode<Button>("Panel/Scroll/RootVBox/DebugContent/RandomAcquireItemBtn");
        _reactionOption = GetNode<OptionButton>("Panel/Scroll/RootVBox/DebugContent/ReactionRow/ReactionOption");
        var playReactionBtn = GetNode<Button>("Panel/Scroll/RootVBox/DebugContent/ReactionRow/PlayReactionBtn");

        seedCopyBtn.Pressed += () => DisplayServer.ClipboardSet(_currentSeed.ToString());
        grantChipsBtn.Pressed += () => EmitSignal(SignalName.DebugGrantChipsRequested);
        randomizeSceneBtn.Pressed += () => EmitSignal(SignalName.RandomizeRequested);
        randomizeDogBtn.Pressed += () => EmitSignal(SignalName.RandomizeDogRequested);
        randomAcquireItemBtn.Pressed += () => EmitSignal(SignalName.RandomAcquireItemRequested);
        BuildReactionOptions();
        playReactionBtn.Pressed += () =>
            EmitSignal(SignalName.DogReactionRequested, _reactionOption.GetSelectedId());

        // === Wardrobe 页 ===
        _wardrobeGrid = GetNode<GridContainer>("Panel/Scroll/RootVBox/WardrobeContent/WardrobeScroll/WardrobeGrid");
        _typeFilterRow = GetNode<HBoxContainer>("Panel/Scroll/RootVBox/WardrobeContent/TypeFilterRow");
        _emptyWardrobeCenter = GetNode<Control>("Panel/Scroll/RootVBox/WardrobeContent/WardrobeScroll/EmptyWardrobeCenter");
        _emptyWardrobeLabel = GetNode<Label>("Panel/Scroll/RootVBox/WardrobeContent/WardrobeScroll/EmptyWardrobeCenter/EmptyWardrobeLabel");

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
        var contents = new[] { _settingsContent, _wardrobeContent, _debugContent };
        for (int i = 0; i < _tabs.Length; i++)
        {
            contents[i].Visible = i == index;
            _tabs[i].Modulate = i == index ? Colors.White : new Color(0.5f, 0.5f, 0.5f);
        }
        if (index == 1 && _gameData != null)
            BuildWardrobe();
        if (index == 2)
            RefreshDebugPlayTime();
    }

    private void RefreshDebugPlayTime()
    {
        if (_playTimeLabel == null || _gameData == null)
            return;

        var total = TimeSpan.FromSeconds(_gameData.TotalPlaySeconds);
        _playTimeLabel.Text = $"Play Time: {total:hh\\:mm\\:ss} ({_gameData.TotalPlaySeconds:0.0}s)";
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
            btn.Text = tab.TabName;
            btn.AddThemeFontSizeOverride("font_size", 13);
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
            btn.Modulate = tab.Id == activeTabId ? Colors.White : new Color(0.5f, 0.5f, 0.5f);
    }

    private void PopulateWardrobeGrid(TabGroup tab)
    {
        foreach (var child in _wardrobeGrid.GetChildren())
        {
            child.QueueFree();
        }

        var items = tab.TabItemTypeList
            .SelectMany(type => _gameData.Inventory.GetOwnedOfType(type))
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
