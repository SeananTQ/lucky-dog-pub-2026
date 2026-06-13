using Godot;

namespace LuckyDogRise;

public partial class InfoPanelController : CanvasLayer
{
    [Signal] public delegate void SettingsRequestedEventHandler();

    [Export] private Button  _settingsBtn =null;
    private PanelContainer _panel = null!;

    public override void _Ready()
    {
        _panel = GetNode<PanelContainer>("Panel");
        //var btn = GetNode<Button>("Panel/Scroll/RootVBox/SettingsBtn");
        _settingsBtn.Pressed += () => EmitSignal(SignalName.SettingsRequested);
    }

    public void SetPanelPosition(Vector2 pos)
    {
        _panel.Position = pos;
    }
}
