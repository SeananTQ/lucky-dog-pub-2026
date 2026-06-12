using Godot;

namespace LuckyDogRise;

public partial class TestSettingPanelController : CanvasLayer
{
    [Signal] public delegate void RandomizeRequestedEventHandler();
    [Signal] public delegate void RandomizeDogRequestedEventHandler();

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

    // 设置页
    private VBoxContainer _settingsContent = null!;
    private CheckButton _audioToggle = null!;
    private OptionButton _displayOption = null!;

    // Debug 页
    private VBoxContainer _debugContent = null!;
    private Label _seedLabel = null!;
    private LineEdit _seedInput = null!;

    // 分页按钮
    private Button _settingsTab = null!;
    private Button _debugTab = null!;

    private int _currentSeed;

    public override void _Ready()
    {
        _panel = GetNode<PanelContainer>("Panel");

        // 分页切换
        _settingsTab = GetNode<Button>("Panel/Scroll/RootVBox/TitleRow/SettingsTab");
        _debugTab = GetNode<Button>("Panel/Scroll/RootVBox/TitleRow/DebugTab");
        _settingsContent = GetNode<VBoxContainer>("Panel/Scroll/RootVBox/SettingsContent");
        _debugContent = GetNode<VBoxContainer>("Panel/Scroll/RootVBox/DebugContent");

        _settingsTab.Pressed += () => SwitchTab(true);
        _debugTab.Pressed += () => SwitchTab(false);

        // 设置页
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

        closeBtn.Pressed += Close;
        quitBtn.Pressed += () => GetTree().Quit();
        _audioToggle.Toggled += OnAudioToggled;
        _displayOption.ItemSelected += OnDisplayModeChanged;

        // Debug 页
        _seedLabel = GetNode<Label>("Panel/Scroll/RootVBox/DebugContent/SeedRow/SeedLabel");
        var seedCopyBtn = GetNode<Button>("Panel/Scroll/RootVBox/DebugContent/SeedRow/SeedCopyBtn");
        _seedInput = GetNode<LineEdit>("Panel/Scroll/RootVBox/DebugContent/SeedInput");
        var randomizeSceneBtn = GetNode<Button>("Panel/Scroll/RootVBox/DebugContent/RandomizeSceneBtn");
        var randomizeDogBtn = GetNode<Button>("Panel/Scroll/RootVBox/DebugContent/RandomizeDogBtn");

        seedCopyBtn.Pressed += () => DisplayServer.ClipboardSet(_currentSeed.ToString());
        randomizeSceneBtn.Pressed += () => EmitSignal(SignalName.RandomizeRequested);
        randomizeDogBtn.Pressed += () => EmitSignal(SignalName.RandomizeDogRequested);

        _panel.Visible = false;
    }

    private void SwitchTab(bool settings)
    {
        _settingsContent.Visible = settings;
        _debugContent.Visible = !settings;
        _settingsTab.Modulate = settings ? Colors.White : new Color(0.5f, 0.5f, 0.5f);
        _debugTab.Modulate = !settings ? Colors.White : new Color(0.5f, 0.5f, 0.5f);
    }

    // ===== 公共 API（设置页） =====

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

    // ===== 公共 API（Debug 页） =====

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
