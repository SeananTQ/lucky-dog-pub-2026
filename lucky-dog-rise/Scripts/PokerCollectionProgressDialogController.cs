using Godot;

namespace LuckyDogRise;

public partial class PokerCollectionProgressDialogController : CanvasLayer
{
    [Signal] public delegate void OverlayVisibilityChangedEventHandler(bool visible);

    private const string ChineseTitle = "小狗来啦 Demo 庆典进行中";
    private const string ChineseMessage =
        "感谢您参与《小狗来啦》Demo 试玩！目前，庆典装扮收集活动正在进行中。\n" +
        "恭喜您刚刚获得了一只小狗，这也让您的庆典收藏取得了重大进展！\n" +
        "我们已经为您打开庆典页面，快去看看这位新伙伴和您的收集进度吧。\n" +
        "继续加油，争取收集更多庆典装扮吧！";

    private Label _title = null!;
    private Label _message = null!;
    private Button _confirmButton = null!;
    private bool _uiPreviewPinned;

    public bool IsOverlayVisible => Visible;

    public override void _Ready()
    {
        _title = GetNode<Label>("Overlay/Center/Panel/Margin/Content/Title");
        _message = GetNode<Label>("Overlay/Center/Panel/Margin/Content/MessageBg/Message");
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
        GetViewport().GuiReleaseFocus();
    }

    public void ShowPinnedUiPreview()
    {
        _uiPreviewPinned = true;
        if (!Visible)
            ShowNotice();
    }

    private void HideNotice()
    {
        if (!Visible || _uiPreviewPinned)
            return;

        Visible = false;
        EmitSignal(SignalName.OverlayVisibilityChanged, false);
    }
}
