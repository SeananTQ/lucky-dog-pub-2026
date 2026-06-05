using Godot;

namespace LuckyDogRise;

public partial class ChipRewardController : Node2D
{
    [Signal]
    public delegate void CollectedEventHandler();

    private const float SlideDuration = 0.4f;
    private const float SlideDistance = 300f;

    private Button _clickButton = null!;
    private Label _amountLabel = null!;
    private bool _collected;

    public override void _Ready()
    {
        _clickButton = GetNode<Button>("ClickButton");
        _amountLabel = GetNode<Label>("AmountLabel");
        _clickButton.Pressed += OnClicked;
    }

    public void Setup(int amount)
    {
        _amountLabel.Text = $"+{amount}";
    }

    private void OnClicked()
    {
        if (_collected) return;
        _collected = true;
        _clickButton.Disabled = true;

        AudioManager.Instance.PlaySfxByName("ChipCollect.wav");

        var tween = CreateTween();
        tween.TweenProperty(this, "position:y", Position.Y + SlideDistance, SlideDuration)
            .SetEase(Tween.EaseType.In)
            .SetTrans(Tween.TransitionType.Quad);
        tween.TweenProperty(this, "modulate:a", 0f, 0.15f);
        tween.TweenCallback(Callable.From(() =>
        {
            EmitSignal(SignalName.Collected);
            QueueFree();
        }));
    }
}
