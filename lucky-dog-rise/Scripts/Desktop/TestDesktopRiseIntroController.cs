using Godot;

namespace LuckyDogRise;

public partial class TestDesktopRiseIntroController : Node2D
{
    [Export] private DesktopRiseIntroController _riseIntro = null!;
    [Export] private Button _playButton = null!;
    [Export] private Label _statusLabel = null!;

    public override void _Ready()
    {
        _playButton.Pressed += PlayIntro;
        _riseIntro.StatusBarRevealRequested += () => _statusLabel.Text = "Counter reveal requested";
        _riseIntro.Finished += () => _statusLabel.Text = "Finished - click Play Rise again";
        // Keep this preview aligned with BossKeyContent.tscn's hand-authored anchors.
        _riseIntro.Configure(Vector2.Zero, new Vector2(137f, 184f), new Vector2(0.25f, 0.25f), 184f);
        PlayIntro();
    }

    public override void _UnhandledKeyInput(InputEvent @event)
    {
        if (@event is InputEventKey { Pressed: true, Echo: false, Keycode: Key.Space })
            PlayIntro();
    }

    private void PlayIntro()
    {
        _statusLabel.Text = "Playing - press Space repeatedly to test re-entry";
        _riseIntro.Play();
    }
}
