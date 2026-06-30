using Godot;

namespace LuckyDogRise;

public partial class ChipStackController : Node2D
{
    [Signal]
    public delegate void BetPlacedEventHandler();

    private Button _clickButton = null!;
    private Label _hintLabel = null!;
    private const bool ShowBetHintText = false;

    public override void _Ready()
    {
        _clickButton = GetNode<Button>("ClickButton");
        _hintLabel = GetNode<Label>("HintLabel");
        _clickButton.Pressed += () => EmitSignal(SignalName.BetPlaced);
    }

    public void ShowHint(string text)
    {
        _hintLabel.Text = text;
        _hintLabel.Visible = ShowBetHintText;
    }

    public void HideHint()
    {
        _hintLabel.Visible = false;
    }
}
