using Godot;

namespace LuckyDogRise;

public partial class SettingsPanelController : CanvasLayer
{
    public bool IsOpen => _panel.Visible;

    private PanelContainer _panel = null!;
    private CheckButton _audioToggle = null!;
    private CheckButton _alwaysOnTopToggle = null!;
    private CheckButton _taskbarIconToggle = null!;
    private WindowManager _windowManager = null!;
    private Tween _tween;

    internal const float PanelWidth = 300f;
    internal const float PanelHeight = 420f;

    public override void _Ready()
    {
        _windowManager = GetNodeOrNull<WindowManager>("../WindowManager");
        BuildUI();
        _panel.Visible = false;

        var enabled = SettingsManager.LoadAudioEnabled();
        _audioToggle.ButtonPressed = enabled;
        ApplyAudio(enabled);
    }

    public void Toggle()
    {
        if (_panel.Visible)
            Close();
        else
            Open();
    }

    public void Open()
    {
        if (_windowManager != null)
        {
            var gameRect = _windowManager.GameViewScreenRect;
            var screenSize = DisplayServer.ScreenGetSize();
            var (panelScreenPos, _) = PanelPositioner.Calculate(screenSize, gameRect, new Vector2(PanelWidth, PanelHeight));
            _panel.Position = panelScreenPos - DisplayServer.WindowGetPosition();
        }
        else
        {
            var windowSize = DisplayServer.WindowGetSize();
            _panel.Position = new Vector2(
                (windowSize.X - PanelWidth) / 2,
                (windowSize.Y - PanelHeight) / 2
            );
        }

        if (_tween != null && _tween.IsRunning())
            _tween.Kill();

        _panel.Modulate = Colors.White with { A = 0f };
        _panel.Visible = true;
        _tween = CreateTween();
        _tween.TweenProperty(_panel, "modulate:a", 1f, 0.15f).SetEase(Tween.EaseType.Out);
    }

    public void Close()
    {
        if (_tween != null && _tween.IsRunning())
            _tween.Kill();

        _tween = CreateTween();
        _tween.TweenProperty(_panel, "modulate:a", 0f, 0.1f).SetEase(Tween.EaseType.In);
        _tween.TweenCallback(Callable.From(() => _panel.Visible = false));
    }

    /// <summary>外部设置面板在宿主窗口内的位置（窗口扩展后调用）</summary>
    public void SetTargetPosition(Vector2 pos)
    {
        _panel.Position = pos;
    }

    public void Reposition()
    {
        if (!_panel.Visible || _windowManager == null) return;

        var hostPos = DisplayServer.WindowGetPosition();
        var windowSize = DisplayServer.WindowGetSize();
        var gameRect = _windowManager?.GameViewScreenRect
            ?? new Rect2(hostPos.X, hostPos.Y, windowSize.X, windowSize.Y);
        var screenSize = DisplayServer.ScreenGetSize();
        var (panelScreenPos, _) = PanelPositioner.Calculate(screenSize, gameRect, new Vector2(PanelWidth, PanelHeight));
        _panel.Position = panelScreenPos - hostPos;
    }

    private void BuildUI()
    {
        // 主面板容器
        _panel = new PanelContainer();
        _panel.SetSize(new Vector2(PanelWidth, PanelHeight));

        var bgStyle = new StyleBoxFlat();
        bgStyle.BgColor = new Color(0.08f, 0.08f, 0.08f, 0.92f);
        bgStyle.CornerRadiusTopLeft = 12;
        bgStyle.CornerRadiusTopRight = 12;
        bgStyle.CornerRadiusBottomRight = 12;
        bgStyle.CornerRadiusBottomLeft = 12;
        bgStyle.ContentMarginLeft = 12;
        bgStyle.ContentMarginTop = 8;
        bgStyle.ContentMarginRight = 12;
        bgStyle.ContentMarginBottom = 8;
        _panel.AddThemeStyleboxOverride("panel", bgStyle);

        // ScrollContainer 防止内容溢出
        var scroll = new ScrollContainer();
        scroll.SizeFlagsVertical = Control.SizeFlags.ExpandFill;

        // 根 VBox
        var rootVBox = new VBoxContainer();
        rootVBox.AddThemeConstantOverride("separation", 4);

        // 标题行
        var titleRow = new HBoxContainer();
        var titleLabel = new Label();
        titleLabel.Text = "Settings";
        titleLabel.AddThemeFontSizeOverride("font_size", 20);
        titleRow.AddChild(titleLabel);

        var spacer = new Control();
        spacer.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        titleRow.AddChild(spacer);

        var closeBtn = new Button();
        closeBtn.Text = "X";
        closeBtn.Flat = true;
        closeBtn.AddThemeFontSizeOverride("font_size", 18);
        closeBtn.Pressed += Close;
        titleRow.AddChild(closeBtn);
        rootVBox.AddChild(titleRow);

        var sep = new HSeparator();
        rootVBox.AddChild(sep);

        // === 分组：音频 ===
        var audioRow = new HBoxContainer();
        var audioLabel = new Label();
        audioLabel.Text = "Sound Effects";
        audioLabel.AddThemeFontSizeOverride("font_size", 15);
        audioLabel.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        audioRow.AddChild(audioLabel);

        _audioToggle = new CheckButton();
        _audioToggle.Toggled += OnAudioToggled;
        audioRow.AddChild(_audioToggle);
        rootVBox.AddChild(audioRow);

        // === 折叠组：必要设置 ===
        BuildCollapsibleGroup(rootVBox, "▾ General", true, (content) =>
        {
            // 多语言（预留）
            AddPrefRow(content, "Language", new OptionButton());
            // 肤色（预留）
            AddPrefRow(content, "Skin Tone", new OptionButton());
            // 多显示器（预留）
            AddPrefRow(content, "Monitor", new OptionButton());
        });

        // === 折叠组：显示设置 ===
        BuildCollapsibleGroup(rootVBox, "▾ Display", false, (content) =>
        {
            AddPrefRow(content, "Game Scale", new OptionButton());
            AddPrefRow(content, "Panel Scale", new OptionButton());
        });

        // === 折叠组：系统设置 ===
        BuildCollapsibleGroup(rootVBox, "▾ System", false, (content) =>
        {
            _alwaysOnTopToggle = new CheckButton();
            AddPrefRow(content, "Always on Top", _alwaysOnTopToggle);
            _alwaysOnTopToggle.Toggled += (on) =>
            {
                if (_windowManager != null)
                    _windowManager.SetAlwaysOnTop(on);
                else
                    DisplayServer.WindowSetFlag(DisplayServer.WindowFlags.AlwaysOnTop, on);
            };

            _taskbarIconToggle = new CheckButton();
            AddPrefRow(content, "Show Taskbar Icon", _taskbarIconToggle);
        });

        // 分隔线 + 退出
        rootVBox.AddChild(new HSeparator());
        var quitBtn = new Button();
        quitBtn.Text = "Quit Game";
        quitBtn.AddThemeFontSizeOverride("font_size", 14);
        quitBtn.Pressed += () => GetTree().Quit();
        rootVBox.AddChild(quitBtn);

        scroll.AddChild(rootVBox);
        _panel.AddChild(scroll);
        AddChild(_panel);
    }

    private void BuildCollapsibleGroup(VBoxContainer parent, string headerText,
        bool defaultExpanded, System.Action<VBoxContainer> populate)
    {
        var header = new Button();
        header.Text = headerText;
        header.Flat = true;
        header.AddThemeFontSizeOverride("font_size", 15);
        header.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;

        var content = new VBoxContainer();
        content.AddThemeConstantOverride("separation", 4);
        content.Visible = defaultExpanded;
        content.Modulate = new Color(1, 1, 1, 0.8f);
        populate(content);

        header.Pressed += () =>
        {
            content.Visible = !content.Visible;
            header.Text = content.Visible
                ? headerText.Replace("▸", "▾")
                : headerText.Replace("▾", "▸");
        };

        parent.AddChild(header);
        parent.AddChild(content);
    }

    private static void AddPrefRow(VBoxContainer parent, string label, Control widget)
    {
        var row = new HBoxContainer();
        var lbl = new Label();
        lbl.Text = label;
        lbl.AddThemeFontSizeOverride("font_size", 13);
        lbl.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        row.AddChild(lbl);
        row.AddChild(widget);
        parent.AddChild(row);
    }

    private void OnAudioToggled(bool enabled)
    {
        SettingsManager.SaveAudioEnabled(enabled);
        ApplyAudio(enabled);
    }

    private static void ApplyAudio(bool enabled)
    {
        var volume = enabled ? 1f : 0f;
        AudioManager.Instance.SetSfxVolume(volume);
        AudioManager.Instance.SetBgmVolume(enabled ? 0.7f : 0f);
    }
}
