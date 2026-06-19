using Godot;

namespace LuckyDogRise;

public partial class HUDController : CanvasLayer
{
    private Control _messagePanel = null!;
    private CanvasLayer _overlay = null!;
    private Label _centerLabel = null!;

    public override void _Ready()
    {
        _messagePanel = GetNode<Control>("MessagePanel");
        _messagePanel.Visible = false;
        _overlay = GetParent().GetNode<CanvasLayer>("Overlay");
        _centerLabel = _overlay.GetNode<Label>("OverlayPanel/OverlayVBox/CenterLabel");
    }

    public void SetMessage(string msg)
    {
        _messagePanel.Visible = false;
    }

    public void ShowOverlay(string text)
    {
        _overlay.Visible = true;
        _centerLabel.Text = text;
    }

    public void HideOverlay()
    {
        _overlay.Visible = false;
    }
}
