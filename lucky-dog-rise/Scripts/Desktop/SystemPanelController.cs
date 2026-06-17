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

    // Debug 页
    private Label _seedLabel = null!;
    private LineEdit _seedInput = null!;
    private int _currentSeed;

    // Wardrobe 页
    private GridContainer _wardrobeGrid = null!;
    private HBoxContainer _typeFilterRow = null!;
    private EItemType _selectedType;
    private GameData _gameData = null!;
    public GameData GameData { get => _gameData; set { _gameData = value; _gameData.EquipmentChanged += RefreshWardrobeGrid; } }

    private readonly Button[] _tabs = new Button[3];
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
        var closeBtn = GetNode<Button>("Panel/Scroll/RootVBox/TitleRow/CloseBtn");
        var quitBtn = GetNode<Button>("Panel/Scroll/RootVBox/SettingsContent/QuitBtn");

        _displayOption.AddItem("Clock", 0);
        _displayOption.AddItem("Chips", 1);
        _displayOption.AddItem("Hidden", 2);
        _displayOption.Select((int)SettingsManager.LoadDisplayMode());

        _audioToggle.ButtonPressed = SettingsManager.LoadAudioEnabled();
        ApplyAudio(_audioToggle.ButtonPressed);

        var autoHideToggle = GetNode<CheckButton>("Panel/Scroll/RootVBox/SettingsContent/AutoHideRow/AutoHideToggle");
        autoHideToggle.ButtonPressed = SettingsManager.LoadAutoHidePanel();
        autoHideToggle.Toggled += enabled => SettingsManager.SaveAutoHidePanel(enabled);

        closeBtn.Pressed += Close;
        quitBtn.Pressed += () => GetTree().Quit();

        var switchToPlayBtn = GetNode<Button>("Panel/Scroll/RootVBox/SettingsContent/SwitchToPlayBtn");
        var switchToBossKeyBtn = GetNode<Button>("Panel/Scroll/RootVBox/SettingsContent/SwitchToBossKeyBtn");
        switchToPlayBtn.Pressed += () => EmitSignal(SignalName.SwitchToPlayRequested);
        switchToBossKeyBtn.Pressed += () => EmitSignal(SignalName.SwitchToBossKeyRequested);

        _audioToggle.Toggled += OnAudioToggled;
        _displayOption.ItemSelected += OnDisplayModeChanged;

        // === Debug 页 ===
        _seedLabel = GetNode<Label>("Panel/Scroll/RootVBox/DebugContent/SeedRow/SeedLabel");
        var seedCopyBtn = GetNode<Button>("Panel/Scroll/RootVBox/DebugContent/SeedRow/SeedCopyBtn");
        _seedInput = GetNode<LineEdit>("Panel/Scroll/RootVBox/DebugContent/SeedInput");
        var randomizeSceneBtn = GetNode<Button>("Panel/Scroll/RootVBox/DebugContent/RandomizeSceneBtn");
        var randomizeDogBtn = GetNode<Button>("Panel/Scroll/RootVBox/DebugContent/RandomizeDogBtn");

        seedCopyBtn.Pressed += () => DisplayServer.ClipboardSet(_currentSeed.ToString());
        randomizeSceneBtn.Pressed += () => EmitSignal(SignalName.RandomizeRequested);
        randomizeDogBtn.Pressed += () => EmitSignal(SignalName.RandomizeDogRequested);

        // === Wardrobe 页 ===
        _wardrobeGrid = GetNode<GridContainer>("Panel/Scroll/RootVBox/WardrobeContent/WardrobeScroll/WardrobeGrid");
        _typeFilterRow = GetNode<HBoxContainer>("Panel/Scroll/RootVBox/WardrobeContent/TypeFilterRow");

        _panel.Visible = false;
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
        PopulateWardrobeGrid(_selectedType);
    }

    private void BuildTypeFilters()
    {
        foreach (var child in _typeFilterRow.GetChildren())
            child.QueueFree();
        _typeFilterButtons.Clear();

        var types = System.Enum.GetValues(typeof(EItemType)).Cast<EItemType>();
        foreach (var type in types)
        {
            var items = _gameData.Inventory.GetOwnedOfType(type).ToList();
            if (items.Count == 0) continue;

            var btn = new Button();
            btn.Text = TypeLabel(type);
            btn.AddThemeFontSizeOverride("font_size", 13);
            btn.Pressed += () =>
            {
                _selectedType = type;
                UpdateFilterButtonStyles(type);
                PopulateWardrobeGrid(type);
            };
            _typeFilterRow.AddChild(btn);
            _typeFilterButtons.Add(btn);
        }

        if (_typeFilterButtons.Count > 0)
        {
            _selectedType = types.First(t => _gameData.Inventory.GetOwnedOfType(t).Any());
            UpdateFilterButtonStyles(_selectedType);
        }
    }

    private void UpdateFilterButtonStyles(EItemType active)
    {
        foreach (var btn in _typeFilterButtons)
            btn.Modulate = btn.Text == TypeLabel(active) ? Colors.White : new Color(0.5f, 0.5f, 0.5f);
    }

    private void PopulateWardrobeGrid(EItemType type)
    {
        foreach (var child in _wardrobeGrid.GetChildren())
            child.QueueFree();

        var items = _gameData.Inventory.GetOwnedOfType(type).ToList();
        foreach (var item in items)
            _wardrobeGrid.AddChild(CreateItemCell(item));
    }

    private void RefreshWardrobeGrid()
    {
        if (_wardrobeContent.Visible)
            PopulateWardrobeGrid(_selectedType);
    }

    private static readonly PackedScene ItemCellScene = GD.Load<PackedScene>("res://Scenes/Prefabs/ItemCell.tscn");

    private Node CreateItemCell(Item item)
    {
        var cell = ItemCellScene.Instantiate<ItemCellController>();
        cell.Setup(item, _gameData.Inventory.IsEquipped(item.Id));
        cell.Pressed += () => _gameData.EquipItem(item.Id);
        return cell;
    }

    private static string TypeLabel(EItemType type) => type switch
    {
        EItemType.Dog => "Dog",
        EItemType.Headwear => "Hat",
        EItemType.Eyewear => "Eye",
        EItemType.Arm => "Arm",
        EItemType.Clothes => "Top",
        EItemType.Table => "Table",
        EItemType.Background => "BG",
        EItemType.Accessory => "Acc",
        EItemType.Treat => "Treat",
        EItemType.CardBack => "CardB",
        EItemType.CardFace => "CardF",
        _ => type.ToString(),
    };

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
    }

    public void Close()
    {
        if (_tween != null && _tween.IsRunning()) _tween.Kill();
        _tween = CreateTween();
        _tween.TweenProperty(_panel, "modulate:a", 0f, 0.1f).SetEase(Tween.EaseType.In);
        _tween.TweenCallback(Callable.From(() => _panel.Visible = false));
    }

    public void CloseImmediate()
    {
        if (_tween != null && _tween.IsRunning()) _tween.Kill();
        _panel.Modulate = Colors.White with { A = 0f };
        _panel.Visible = false;
    }

    public bool ContainsPoint(Vector2 windowPos)
    {
        if (!_panel.Visible) return false;
        return new Rect2(_panel.Position, PanelSize).HasPoint(windowPos);
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

    private static void ApplyAudio(bool enabled)
    {
        AudioManager.Instance.SetSfxVolume(enabled ? 1f : 0f);
        AudioManager.Instance.SetBgmVolume(enabled ? 0.7f : 0f);
    }
}
