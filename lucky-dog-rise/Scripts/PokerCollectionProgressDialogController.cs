using Godot;

namespace LuckyDogRise;

public partial class PokerCollectionProgressDialogController : CanvasLayer
{
    [Signal] public delegate void OverlayVisibilityChangedEventHandler(bool visible);

    private const string ChineseTitle = "庆典收集取得重大进展！";
    private const string ChineseMessage =
        "你正在参与 Demo 庆典装扮收集活动！刚刚获得的新狗狗，让你的收藏取得了重大进展。\n\n" +
        "庆典页面已经为你打开，快去看看吧。继续加油，收集更多庆典装扮！";

    private Label _title = null!;
    private Label _message = null!;
    private Button _confirmButton = null!;

    public bool IsOverlayVisible => Visible;

    public override void _Ready()
    {
        _title = GetNode<Label>("Overlay/Center/Panel/Margin/Content/Title");
        _message = GetNode<Label>("Overlay/Center/Panel/Margin/Content/Message");
        _confirmButton = GetNode<Button>("Overlay/Center/Panel/Margin/Content/ConfirmRow/ConfirmButton");
        _confirmButton.Pressed += HideNotice;
        Visible = false;
    }

    public void ShowNotice()
    {
        _title.Text = ChineseTitle;
        _message.Text = ChineseMessage;
        _confirmButton.Text = L10n.Tr(L10nKey.Common_Confirm);
        Visible = true;
        EmitSignal(SignalName.OverlayVisibilityChanged, true);
        _confirmButton.GrabFocus();
    }

    private void HideNotice()
    {
        if (!Visible)
            return;

        Visible = false;
        EmitSignal(SignalName.OverlayVisibilityChanged, false);
    }
}
