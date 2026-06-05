using Godot;

namespace LuckyDogRise;

public partial class HandAreaController : Node2D
{
    [Signal]
    public delegate void HandKnockedEventHandler();

    private const float KnockAngle = 0.08f;
    private const float KnockDownTime = 0.1f;
    private const float KnockUpTime = 0.12f;
    private const float KnockIntervalTime = 0.05f;

    public bool Enabled { get; set; }

    private Button _hitButton = null!;
    private bool _isKnocking;

    public override void _Ready()
    {
        _hitButton = GetNode<Button>("HitButton");
        _hitButton.Pressed += OnHitPressed;
    }

    private void OnHitPressed()
    {
        if (!Enabled || _isKnocking) return;
        EmitSignal(SignalName.HandKnocked);
        PlayKnockAnimation();
    }

    private void PlayKnockAnimation()
    {
        _isKnocking = true;
        AudioManager.Instance.PlaySfxByName("Knock.wav");

        var tween = CreateTween();
        tween.TweenProperty(this, "rotation", KnockAngle, KnockDownTime)
            .SetEase(Tween.EaseType.In)
            .SetTrans(Tween.TransitionType.Quad);
        tween.TweenProperty(this, "rotation", 0f, KnockUpTime)
            .SetEase(Tween.EaseType.Out)
            .SetTrans(Tween.TransitionType.Bounce);
        tween.TweenInterval(KnockIntervalTime);
        tween.TweenProperty(this, "rotation", KnockAngle, KnockDownTime)
            .SetEase(Tween.EaseType.In)
            .SetTrans(Tween.TransitionType.Quad);
        tween.TweenProperty(this, "rotation", 0f, KnockUpTime)
            .SetEase(Tween.EaseType.Out)
            .SetTrans(Tween.TransitionType.Bounce);
        tween.TweenCallback(Callable.From(() => _isKnocking = false));
    }

    public void SetClothes(Texture2D texture)
    {
        GetNode<Sprite2D>("Clothes").Texture = texture;
    }

    public void SetAccessory(Texture2D texture)
    {
        var sprite = GetNode<Sprite2D>("Accessory");
        if (texture != null)
        {
            sprite.Texture = texture;
            sprite.Visible = true;
        }
        else
        {
            sprite.Visible = false;
        }
    }
}
