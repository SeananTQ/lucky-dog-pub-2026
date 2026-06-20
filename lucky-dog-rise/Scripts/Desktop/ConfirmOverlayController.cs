using Godot;

namespace LuckyDogRise;

public partial class ConfirmOverlayController : Control
{
    [Signal] public delegate void ConfirmedEventHandler();
    [Signal] public delegate void CanceledEventHandler();

    private Label _titleLabel = null!;
    private Label _messageLabel = null!;
    private Button _confirmButton = null!;
    private Button _cancelButton = null!;

    public override void _Ready()
    {
        _titleLabel = GetNode<Label>("OverlayPanel/Margin/Content/Title");
        _messageLabel = GetNode<Label>("OverlayPanel/Margin/Content/MessageBg/Message");
        _cancelButton = GetNode<Button>("OverlayPanel/Margin/Content/ButtonRow/CancelButton");
        _confirmButton = GetNode<Button>("OverlayPanel/Margin/Content/ButtonRow/ConfirmButton");

        _cancelButton.Pressed += Cancel;
        _confirmButton.Pressed += Confirm;
        Visible = false;
    }

    public void ShowConfirm(string title, string message, string confirmText = "确认", string cancelText = "取消")
    {
        _titleLabel.Text = title;
        _messageLabel.Text = message;
        _confirmButton.Text = confirmText;
        _cancelButton.Text = cancelText;
        Visible = true;
    }

    public void SetOverlayRect(Vector2 position, Vector2 size)
    {
        Position = position;
        Size = size;
    }

    private void Confirm()
    {
        Visible = false;
        EmitSignal(SignalName.Confirmed);
    }

    private void Cancel()
    {
        Visible = false;
        EmitSignal(SignalName.Canceled);
    }
}
