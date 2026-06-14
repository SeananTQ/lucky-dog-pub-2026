using Godot;

namespace LuckyDogRise;

public partial class HUDController : CanvasLayer
{
    private Label _messageLabel = null!;
    private CanvasLayer _overlay = null!;
    private Label _centerLabel = null!;

    public override void _Ready()
    {
        _messageLabel = GetNode<Label>("MessagePanel/MessageLabel");
        _overlay = GetParent().GetNode<CanvasLayer>("Overlay");
        _centerLabel = _overlay.GetNode<Label>("OverlayPanel/OverlayVBox/CenterLabel");
    }

    public void SetMessage(string msg)
    {
        _messageLabel.Text = msg;
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
