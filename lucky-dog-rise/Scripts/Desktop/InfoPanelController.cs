using Godot;

namespace LuckyDogRise;

public partial class InfoPanelController : CanvasLayer
{
    [Signal] public delegate void SettingsRequestedEventHandler();

    private PanelContainer _panel = null!;

    public override void _Ready()
    {
        _panel = GetNode<PanelContainer>("Panel");
        var btn = _panel.GetNode<Button>("RootVBox/SettingsBtn");
        btn.Pressed += () => EmitSignal(SignalName.SettingsRequested);
    }

    public void SetPanelPosition(Vector2 pos)
    {
        _panel.Position = pos;
    }
}
