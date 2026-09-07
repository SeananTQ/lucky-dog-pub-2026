using Godot;

namespace LuckyDogRise;

public partial class WishlistCallToActionOverlayController : Control
{
    [Signal] public delegate void PrimaryPressedEventHandler(bool suppressFutureExitPrompts);
    [Signal] public delegate void SecondaryPressedEventHandler(bool suppressFutureExitPrompts);

    private Button _primaryButton = null!;
    private Button _secondaryButton = null!;
    private CheckBox _suppressExitCheckBox = null!;

    public override void _Ready()
    {
        _primaryButton = GetNode<Button>("OverlayPanel/Margin/Content/ButtonRow/PrimaryButton");
        _secondaryButton = GetNode<Button>("OverlayPanel/Margin/Content/ButtonRow/SecondaryButton");
        _suppressExitCheckBox = GetNode<CheckBox>("OverlayPanel/Margin/Content/SuppressExitCheckBox");
        _primaryButton.Pressed += () => EmitAndHide(primary: true);
        _secondaryButton.Pressed += () => EmitAndHide(primary: false);
        Visible = false;
    }

    public void ShowCallToAction(bool isExitPrompt)
    {
        _suppressExitCheckBox.Visible = isExitPrompt;
        _suppressExitCheckBox.SetPressedNoSignal(false);
        _primaryButton.Text = isExitPrompt ? "加入愿望单并退出" : "加入愿望单";
        _secondaryButton.Text = isExitPrompt ? "直接退出" : "稍后再说";
        Visible = true;
        _primaryButton.GrabFocus();
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
            _suppressExitCheckBox.Visible && _suppressExitCheckBox.ButtonPressed;
        HideOverlay();
        EmitSignal(
            primary ? SignalName.PrimaryPressed : SignalName.SecondaryPressed,
            suppressFutureExitPrompts);
    }
}
