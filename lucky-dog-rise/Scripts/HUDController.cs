using Godot;

namespace LuckyDogRise;

public partial class HUDController : CanvasLayer
{
    private Button _dealButton = null!;
    private Button _drawButton = null!;
    private Label _rankLabel = null!;
    private Label _messageLabel = null!;
    private PanelContainer _messagePanel = null!;
    private CanvasLayer _overlay = null!;
    private Label _centerLabel = null!;

    public override void _Ready()
    {
        _dealButton = GetNode<Button>("DealButton");
        _drawButton = GetNode<Button>("DrawButton");
        _rankLabel = GetNode<Label>("RankPanel/RankLabel");
        _messageLabel = GetNode<Label>("MessagePanel/MessageLabel");
        _messagePanel = GetNode<PanelContainer>("MessagePanel");
        _overlay = GetParent().GetNode<CanvasLayer>("Overlay");
        _centerLabel = _overlay.GetNode<Label>("OverlayPanel/OverlayVBox/CenterLabel");
    }

    // 按钮状态
    public void UpdateButtons(GameState state, bool debugMode)
    {
        _dealButton.Visible = debugMode;
        _drawButton.Visible = debugMode;
        _messagePanel.Visible = debugMode;

        if (!debugMode) return;

        switch (state)
        {
            case GameState.WaitingForBet:
                _dealButton.Disabled = false;
                _dealButton.Text = "DEAL";
                _drawButton.Disabled = true;
                break;
            case GameState.Dealt:
            case GameState.Holding:
                _dealButton.Disabled = true;
                _drawButton.Disabled = false;
                break;
            case GameState.Drawing:
                _dealButton.Disabled = true;
                _drawButton.Disabled = true;
                break;
            case GameState.Settled:
                _dealButton.Disabled = false;
                _dealButton.Text = "NEXT HAND";
                _drawButton.Disabled = true;
                break;
            case GameState.GameOver:
                _dealButton.Disabled = false;
                _dealButton.Text = "RESTART";
                _drawButton.Disabled = true;
                break;
        }
    }

    // 消息
    public void SetMessage(string msg)
    {
        _messageLabel.Text = msg;
    }

    // 按钮可见性
    public void SetDealButtonVisible(bool visible)
    {
        _dealButton.Visible = visible;
    }

    // Overlay
    public void ShowOverlay(string text)
    {
        _overlay.Visible = true;
        _centerLabel.Text = text;
    }

    public void HideOverlay()
    {
        _overlay.Visible = false;
    }

    // 信号连接
    public void ConnectDeal(GodotObject target, string method)
    {
        _dealButton.Pressed += () => target.Call(method);
    }

    public void ConnectDraw(GodotObject target, string method)
    {
        _drawButton.Pressed += () => target.Call(method);
    }
}
