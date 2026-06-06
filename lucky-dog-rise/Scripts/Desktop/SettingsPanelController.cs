using Godot;

namespace LuckyDogRise;

public partial class SettingsPanelController : CanvasLayer
{
    private PanelContainer _panel = null!;
    private CheckButton _audioToggle = null!;
    private WindowManager _windowManager = null!;
    private Tween _tween;

    private const float PanelWidth = 280f;
    private const float PanelHeight = 140f;

    public override void _Ready()
    {
        _windowManager = GetNode<WindowManager>("../WindowManager");
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
        var gameRect = _windowManager.GameViewScreenRect;
        var screenSize = DisplayServer.ScreenGetSize();
        var (panelScreenPos, _) = PanelPositioner.Calculate(screenSize, gameRect, new Vector2(PanelWidth, PanelHeight));
        var hostPos = DisplayServer.WindowGetPosition();
        _panel.Position = panelScreenPos - hostPos;

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

    public void Reposition()
    {
        if (!_panel.Visible) return;

        var gameRect = _windowManager.GameViewScreenRect;
        var screenSize = DisplayServer.ScreenGetSize();
        var (panelScreenPos, _) = PanelPositioner.Calculate(screenSize, gameRect, new Vector2(PanelWidth, PanelHeight));
        var hostPos = DisplayServer.WindowGetPosition();
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
        bgStyle.ContentMarginLeft = 16;
        bgStyle.ContentMarginTop = 12;
        bgStyle.ContentMarginRight = 16;
        bgStyle.ContentMarginBottom = 12;

        var borderStyle = new StyleBoxFlat();
        borderStyle.BgColor = Colors.Transparent;
        borderStyle.BorderWidthLeft = 1;
        borderStyle.BorderWidthTop = 1;
        borderStyle.BorderWidthRight = 1;
        borderStyle.BorderWidthBottom = 1;
        borderStyle.BorderColor = new Color(1, 1, 1, 0.15f);
        borderStyle.CornerRadiusTopLeft = 12;
        borderStyle.CornerRadiusTopRight = 12;
        borderStyle.CornerRadiusBottomRight = 12;
        borderStyle.CornerRadiusBottomLeft = 12;
        bgStyle.DrawCenter = true;

        _panel.AddThemeStyleboxOverride("panel", bgStyle);

        // 根 VBox
        var rootVBox = new VBoxContainer();
        rootVBox.AddThemeConstantOverride("separation", 12);

        // 标题行：Label + X 按钮
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

        // 分隔线
        var sep = new HSeparator();
        rootVBox.AddChild(sep);

        // 音效行
        var audioRow = new HBoxContainer();
        var audioLabel = new Label();
        audioLabel.Text = "Sound Effects";
        audioLabel.AddThemeFontSizeOverride("font_size", 16);
        audioLabel.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        audioRow.AddChild(audioLabel);

        _audioToggle = new CheckButton();
        _audioToggle.Toggled += OnAudioToggled;
        audioRow.AddChild(_audioToggle);
        rootVBox.AddChild(audioRow);

        _panel.AddChild(rootVBox);
        AddChild(_panel);
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
