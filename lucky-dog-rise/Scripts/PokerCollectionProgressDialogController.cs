using Godot;

namespace LuckyDogRise;

public partial class PokerCollectionProgressDialogController : CanvasLayer
{
    [Signal] public delegate void OverlayVisibilityChangedEventHandler(bool visible);
    [Signal] public delegate void SecondaryActionRequestedEventHandler();
    [Signal] public delegate void ConfirmedEventHandler();

    private Label _title = null!;
    private Label _message = null!;
    private Button _confirmButton = null!;
    private Button _secondaryButton = null!;
    private ScrollContainer _messageScroll = null!;
    private PanelContainer _messageBackground = null!;
    private bool _layoutQueued;

    public bool IsOverlayVisible => Visible;

    public override void _Ready()
    {
        _title = GetNode<Label>("Overlay/Center/Panel/Margin/Content/Title");
        _messageScroll = GetNode<ScrollContainer>("Overlay/Center/Panel/Margin/Content/MessageScroll");
        _messageBackground = _messageScroll.GetNode<PanelContainer>("MessageBg");
        _message = _messageBackground.GetNode<Label>("Message");
        _confirmButton = GetNode<Button>("Overlay/Center/Panel/Margin/Content/ConfirmRow/ConfirmButton");
        _secondaryButton = GetNode<Button>("Overlay/Center/Panel/Margin/Content/ConfirmRow/SecondaryButton");
        _secondaryButton.Pressed += () => EmitSignal(SignalName.SecondaryActionRequested);
        _message.MinimumSizeChanged += QueueBodyLayout;
        _message.Resized += QueueBodyLayout;
        _title.Resized += QueueBodyLayout;
        _confirmButton.Resized += QueueBodyLayout;
        _confirmButton.Pressed += HideNotice;
        L10n.Changed += RefreshLocalizedText;
        Visible = false;
        QueueBodyLayout();
    }

    public override void _ExitTree()
    {
        L10n.Changed -= RefreshLocalizedText;
    }

    // Single action stays centered; optional secondary action shares the row equally.
    public void SetSecondaryAction(string text)
    {
        bool hasSecondary = !string.IsNullOrWhiteSpace(text);
        _secondaryButton.Text = text;
        _secondaryButton.Visible = hasSecondary;
        _confirmButton.CustomMinimumSize = new Vector2(hasSecondary ? 0 : 480, 112);
        _confirmButton.SizeFlagsHorizontal = hasSecondary ? Control.SizeFlags.ExpandFill : Control.SizeFlags.Fill;
        QueueBodyLayout();
    }

    private void QueueBodyLayout()
    {
        if (_layoutQueued)
            return;
        _layoutQueued = true;
        Callable.From(UpdateBodyLayout).CallDeferred();
    }

    private void UpdateBodyLayout()
    {
        _layoutQueued = false;
        // Keep 40 design pixels outside the dialog, including when titles/actions wrap.
        // Panel + margin padding = 112; two VBox gaps = 64.
        float available = Mathf.Max(120, 1200 - 80 - 112 - 64
            - _title.Size.Y - _confirmButton.Size.Y);
        float height = Mathf.Min(_messageBackground.GetCombinedMinimumSize().Y, available);
        _messageScroll.CustomMinimumSize = new Vector2(0, height);
    }

    public void ShowNotice()
    {
        RefreshLocalizedText();
        SetSecondaryAction(string.Empty);
        _messageScroll.ScrollVertical = 0;
        Visible = true;
        EmitSignal(SignalName.OverlayVisibilityChanged, true);
        GetViewport().GuiReleaseFocus();
    }

    private void RefreshLocalizedText()
    {
        if (_title == null || _message == null || _confirmButton == null)
            return;

        _title.Text = L10n.Tr(L10nKey.CollectionEvent_FirstDogProgressTitle);
        _message.Text = L10n.Tr(L10nKey.CollectionEvent_FirstDogProgressBody);
        _confirmButton.Text = L10n.Tr(L10nKey.CollectionEvent_FirstDogProgressConfirm);
        QueueBodyLayout();
    }

    private void HideNotice()
    {
        if (!Visible)
            return;

        Visible = false;
        EmitSignal(SignalName.OverlayVisibilityChanged, false);
        EmitSignal(SignalName.Confirmed);
    }
}
