using Godot;

namespace LuckyDogRise;

public partial class WishlistCallToActionOverlayController : Control
{
    [Signal] public delegate void PrimaryPressedEventHandler(bool suppressFutureExitPrompts);
    [Signal] public delegate void SecondaryPressedEventHandler(bool suppressFutureExitPrompts);
    [Signal] public delegate void ClosedEventHandler();

    private Button _primaryButton = null!;
    private Button _secondaryButton = null!;
    private Button _closeButton = null!;
    private Button _promptButton = null!;
    private Control _suppressExitArea = null!;
    private CheckButton _suppressExitCheckBox = null!;
    private Label _title = null!;
    private Label _message = null!;
    private bool _isExitPrompt;

    public override void _Ready()
    {
        _primaryButton = GetNode<Button>("OverlayPanel/Margin/Content/ButtonColumn/PrimaryButton");
        _secondaryButton = GetNode<Button>("OverlayPanel/Margin/Content/ButtonColumn/SecondaryButton");
        _closeButton = GetNode<Button>("CloseButton");
        _promptButton = GetNode<Button>("OverlayPanel/Margin/Content/WishlistPrompt/HitArea");
        _suppressExitArea = GetNode<Control>("OverlayPanel/Margin/Content/SuppressExitArea");
        _suppressExitCheckBox = GetNode<CheckButton>("OverlayPanel/Margin/Content/SuppressExitArea/CheckBox");
        _title = GetNode<Label>("OverlayPanel/Margin/Content/Title");
        _message = GetNode<Label>("OverlayPanel/Margin/Content/WishlistPrompt/Balloon/CopyMargins/Message");
        _primaryButton.Pressed += () => EmitAndHide(primary: true);
        _promptButton.Pressed += () => EmitAndHide(primary: true);
        _secondaryButton.Pressed += () => EmitAndHide(primary: false);
        _closeButton.Pressed += CloseWithoutAction;
        L10n.Changed += RefreshPresentation;
        RefreshPresentation();
        Visible = false;
    }

    public override void _ExitTree()
    {
        L10n.Changed -= RefreshPresentation;
    }

    public void ShowCallToAction(bool isExitPrompt)
    {
        _isExitPrompt = isExitPrompt;
        _suppressExitArea.Visible = isExitPrompt;
        _closeButton.Visible = isExitPrompt;
        _suppressExitCheckBox.SetPressedNoSignal(false);
        RefreshPresentation();
        Visible = true;
        GetViewport().GuiReleaseFocus();
    }

    public void HideOverlay()
    {
        Visible = false;
        _suppressExitCheckBox.SetPressedNoSignal(false);
    }

    public void SetOverlayRect(Vector2 position, Vector2 size)
    {
        Position = position;
        Size = size;
    }

    private void EmitAndHide(bool primary)
    {
        var suppressFutureExitPrompts =
            _suppressExitArea.Visible && _suppressExitCheckBox.ButtonPressed;
        HideOverlay();
        EmitSignal(
            primary ? SignalName.PrimaryPressed : SignalName.SecondaryPressed,
            suppressFutureExitPrompts);
    }

    private void CloseWithoutAction()
    {
        HideOverlay();
        EmitSignal(SignalName.Closed);
    }

    private void RefreshPresentation()
    {
        _title.Text = L10n.Tr(L10nKey.WishlistCallToAction_Title);
        _message.AddThemeFontSizeOverride(
            "font_size",
            WishlistCallToAction.GetShyRequestFontSize(L10n.CurrentLocale));
        _message.Text = L10n.Tr(L10nKey.CollectionEvent_WishlistShyRequest);
        _primaryButton.Text = _isExitPrompt
            ? L10n.Tr(L10nKey.WishlistCallToAction_ExitAndOpenSteamButton)
            : L10n.Tr(L10nKey.CollectionEvent_WishlistButton);
        _secondaryButton.Text = _isExitPrompt
            ? L10n.Tr(L10nKey.WishlistCallToAction_ExitGameButton)
            : "稍后再说";
        _suppressExitCheckBox.Text = L10n.Tr(L10nKey.WishlistCallToAction_SuppressExitReminder);
    }
}
