using Godot;

namespace LuckyDogRise;

public partial class TestSettingPanelController : CanvasLayer
{
    public bool IsOpen => _panel.Visible;
    internal const float PanelWidth = 300f;
    internal const float PanelHeight = 420f;

    private PanelContainer _panel = null!;
    private CheckButton _audioToggle = null!;
    private Tween _tween = null!;

    public override void _Ready()
    {
        _panel = GetNode<PanelContainer>("Panel");
        _audioToggle = GetNode<CheckButton>("Panel/Scroll/RootVBox/AudioRow/AudioToggle");
        var closeBtn = GetNode<Button>("Panel/Scroll/RootVBox/TitleRow/CloseBtn");
        var quitBtn = GetNode<Button>("Panel/Scroll/RootVBox/QuitBtn");

        _panel.Visible = false;
        _audioToggle.ButtonPressed = SettingsManager.LoadAudioEnabled();
        ApplyAudio(_audioToggle.ButtonPressed);

        closeBtn.Pressed += Close;
        quitBtn.Pressed += () => GetTree().Quit();
        _audioToggle.Toggled += OnAudioToggled;
    }

    public void Toggle() { if (_panel.Visible) Close(); else Open(); }

    public void Open()
    {
        var ws = DisplayServer.WindowGetSize();
        _panel.Position = new Vector2((ws.X - PanelWidth) / 2, (ws.Y - PanelHeight) / 2);
        if (_tween != null && _tween.IsRunning()) _tween.Kill();
        _panel.Modulate = Colors.White with { A = 0f };
        _panel.Visible = true;
        _tween = CreateTween();
        _tween.TweenProperty(_panel, "modulate:a", 1f, 0.15f).SetEase(Tween.EaseType.Out);
    }

    public void Close()
    {
        if (_tween != null && _tween.IsRunning()) _tween.Kill();
        _tween = CreateTween();
        _tween.TweenProperty(_panel, "modulate:a", 0f, 0.1f).SetEase(Tween.EaseType.In);
        _tween.TweenCallback(Callable.From(() => _panel.Visible = false));
    }

    public void SetTargetPosition(Vector2 pos)
    {
        _panel.OffsetLeft = pos.X;
        _panel.OffsetTop = pos.Y;
    }

    private void OnAudioToggled(bool enabled)
    {
        SettingsManager.SaveAudioEnabled(enabled);
        ApplyAudio(enabled);
    }

    private static void ApplyAudio(bool enabled)
    {
        AudioManager.Instance.SetSfxVolume(enabled ? 1f : 0f);
        AudioManager.Instance.SetBgmVolume(enabled ? 0.7f : 0f);
    }
}
