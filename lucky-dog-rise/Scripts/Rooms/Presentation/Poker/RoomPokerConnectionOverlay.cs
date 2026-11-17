#if !DEMO_BUILD && !RECORDING_BUILD
using Godot;

namespace LuckyDogRise.Rooms;

// Poker recovery owns its lifecycle; only the welcome dialog's visual styles are shared.
public partial class RoomPokerConnectionOverlay : Control
{
    [Export] private Label _title, _message;
    [Export] private Button _confirm;
    private enum Notice { Migration, Reconnect, Failed }
    private Notice _notice;

    public override void _EnterTree() => UiInteractionOccupancy.TrackVisibility(this);

    public override void _Ready()
    {
        Hide();
        PrepareScaledFont();
        _confirm.Pressed += () => { if (_notice == Notice.Failed) Hide(); };
        L10n.Changed += RefreshText;
    }

    public override void _ExitTree() => L10n.Changed -= RefreshText;

    public void ShowWaiting(bool migrating) => ShowNotice(migrating ? Notice.Migration : Notice.Reconnect);
    public void ShowFailure() => ShowNotice(Notice.Failed);

    private void ShowNotice(Notice notice)
    {
        if (Visible && _notice == notice) return;
        _notice = notice;
        _confirm.Visible = notice == Notice.Failed;
        RefreshText();
        Show();
        GetViewport().GuiReleaseFocus();
    }

    private void RefreshText()
    {
        // Read the room catalog directly, including before the Online tab has ever opened.
        _title.Text = InGameRoomPreview.ChatText(_notice == Notice.Failed
            ? "Rooms_PokerRecoveryFailedTitle" : "Rooms_PokerRecoveryTitle");
        _message.Text = InGameRoomPreview.ChatText(_notice switch
        {
            Notice.Migration => "Rooms_PokerMigrationBody",
            Notice.Reconnect => "Rooms_PokerReconnectBody",
            _ => "Rooms_PokerRecoveryFailedBody"
        });
        _confirm.Text = L10n.Tr(L10nKey.PokerWelcome_Confirm);
    }

    private void PrepareScaledFont()
    {
        // As in PokerWelcomeDialog: sample at the 2x poker-canvas scale without mutating shared fonts.
        var font = (Font)_message.GetThemeFont("font").DuplicateDeep(Resource.DeepDuplicateMode.All);
        var textServer = TextServerManager.GetPrimaryInterface();
        foreach (var rid in font.GetRids()) textServer.FontSetOversampling(rid, 2.0);
        var fontTheme = new Theme();
        foreach (string type in new[] { "PokerWelcomeTitle", "PokerWelcomeBody", "PokerWelcomeConfirm" })
            fontTheme.SetFont("font", type, font);
        _message.GetParent<Control>().Theme = fontTheme;
    }
}
#endif
