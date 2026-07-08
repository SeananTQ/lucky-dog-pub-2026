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
    private string _titleKey = "";
    private string _messageKey = "";
    private string _confirmKey = "";
    private string _cancelKey = "";
    private bool _usesLocalizationKeys;

    public override void _Ready()
    {
        _titleLabel = GetNode<Label>("OverlayPanel/Margin/Content/Title");
        _messageLabel = GetNode<Label>("OverlayPanel/Margin/Content/MessageBg/Message");
        _cancelButton = GetNode<Button>("OverlayPanel/Margin/Content/ButtonRow/CancelButton");
        _confirmButton = GetNode<Button>("OverlayPanel/Margin/Content/ButtonRow/ConfirmButton");

        _cancelButton.Pressed += Cancel;
        _confirmButton.Pressed += Confirm;
        L10n.Changed += RefreshLocalizedText;
        Visible = false;
    }

    public void ShowConfirm(string title, string message, string confirmText = "确认", string cancelText = "取消")
    {
        _usesLocalizationKeys = false;
        _titleLabel.Text = title;
        _messageLabel.Text = message;
        _confirmButton.Text = confirmText;
        _cancelButton.Text = cancelText;
        Visible = true;
    }

    public void ShowConfirmKey(string titleKey, string messageKey, string confirmKey = L10nKey.Common_Confirm, string cancelKey = L10nKey.Common_Cancel)
    {
        _usesLocalizationKeys = true;
        _titleKey = titleKey;
        _messageKey = messageKey;
        _confirmKey = confirmKey;
        _cancelKey = cancelKey;
        RefreshLocalizedText();
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

    private void RefreshLocalizedText()
    {
        if (!_usesLocalizationKeys)
            return;

        _titleLabel.Text = L10n.Tr(_titleKey);
        _messageLabel.Text = L10n.Tr(_messageKey);
        _confirmButton.Text = L10n.Tr(_confirmKey);
        _cancelButton.Text = L10n.Tr(_cancelKey);
    }
}
