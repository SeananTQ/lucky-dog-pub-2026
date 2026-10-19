using Godot;

namespace LuckyDogRise;

public partial class PokerWelcomeDialogController : CanvasLayer
{
    [Signal] public delegate void OverlayVisibilityChangedEventHandler(bool visible);
    [Signal] public delegate void ConfirmedEventHandler();

    [Export] private Label _title = null!;
    [Export] private Label _message = null!;
    [Export] private Button _confirmButton = null!;

    private bool _requested;
    private bool _pokerActive;
    private bool _blocked;
    public bool IsOverlayVisible => Visible;

    public override void _Ready()
    {
        Visible = false;
        PrepareScaledFont();
        _confirmButton.Pressed += Confirm;
        L10n.Changed += RefreshText;
        RefreshText();
    }

    private void PrepareScaledFont()
    {
        // Control.Scale does not affect font rasterization. Sample this dialog's font at
        // its 2x poker-canvas scale, without changing the shared theme/font resources.
        var font = (Font)_message.GetThemeFont("font").DuplicateDeep(Resource.DeepDuplicateMode.All);
        var textServer = TextServerManager.GetPrimaryInterface();
        foreach (var rid in font.GetRids())
            textServer.FontSetOversampling(rid, 2.0);
        var fontTheme = new Theme();
        foreach (string type in new[] { "PokerWelcomeTitle", "PokerWelcomeBody", "PokerWelcomeConfirm" })
            fontTheme.SetFont("font", type, font);
        _message.GetParent<Control>().Theme = fontTheme;
    }

    public override void _ExitTree() => L10n.Changed -= RefreshText;

    public bool IsPointOverConfirmButton(Vector2 viewportPosition) => IsOverlayVisible
        && _confirmButton.GetGlobalRect().HasPoint(viewportPosition);

    public void SetRequested(bool requested)
    {
        _requested = requested;
        RefreshVisibility();
    }

    public void SetContext(bool pokerActive, bool blocked)
    {
        _pokerActive = pokerActive;
        _blocked = blocked;
        RefreshVisibility();
    }

    private void RefreshVisibility()
    {
        bool show = _requested && _pokerActive && !_blocked;
        if (Visible == show) return;
        Visible = show;
        if (show) GetViewport().GuiReleaseFocus();
        EmitSignal(SignalName.OverlayVisibilityChanged, show);
    }

    private void Confirm()
    {
        if (!Visible) return;
        _requested = false;
        RefreshVisibility();
        EmitSignal(SignalName.Confirmed);
    }

    private void RefreshText()
    {
        _title.Text = L10n.Tr(L10nKey.PokerWelcome_Title);
        _message.Text = L10n.Format(L10nKey.PokerWelcome_Body,
            L10n.Tr(L10nKey.PokerToolbar_Help), L10n.Tr(L10nKey.PokerToolbar_Tutorial));
        _confirmButton.Text = L10n.Tr(L10nKey.PokerWelcome_Confirm);
    }
}
