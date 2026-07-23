using Godot;

namespace LuckyDogRise;

public partial class ChipStackController : Node2D, IInteractionHintTarget
{
    [Signal]
    public delegate void BetRequestedEventHandler();

    private Button _clickButton = null!;
    private Node2D _visualRoot = null!;
    private Sprite2D _chipSprite = null!;
    private Sprite2D _chipSprite2 = null!;
    private Label _hintLabel = null!;
    private Tween _appearTween = null!;
    private Tween _secondChipAppearTween = null!;
    private Tween _secondChipLandingTween = null!;
    private Tween _bottomChipHintTween = null!;
    private Tween _topChipHintTween = null!;
    private const bool ShowBetHintText = false;

    private static readonly Vector2 BottomChipRestPosition = Vector2.Zero;
    private static readonly Vector2 TopChipRestPosition = new(0f, -6f);
    private const float BottomChipStartHeight = -42f;
    private const float TopChipStartHeight = -38f;
    private const float BottomChipStartRotation = -0.22f;
    private const float TopChipStartRotation = 0.18f;
    private const double FirstChipDuration = 0.22;
    // 第二颗只比第一颗晚一个轻微错拍；两段落下会自然重叠，不会黏成一次动作。
    private const double SecondChipDelay = 0.15;
    private const double SecondChipDuration = 0.2;
    private const double LeaveDuration = 0.28;
    private const float HintFirstLift = 8f;
    private const float HintSecondLift = 4f;
    private const float HintFirstRotation = -0.06f;
    private const float HintSecondRotation = 0.035f;
    private const double HintFirstDuration = 0.11;
    private const double HintSecondDuration = 0.09;
    private const double TopChipHintDelay = 0.045;
    private const float LeaveDistance = 100f;
    private static readonly Vector2 VisualRestPosition = Vector2.Zero;
    private static readonly Vector2 LeaveOffset = new(
        -Mathf.Cos(Mathf.DegToRad(60f)) * LeaveDistance,
        -Mathf.Sin(Mathf.DegToRad(60f)) * LeaveDistance);

    public bool CanPlayInteractionHint => _visualRoot != null && _visualRoot.Visible;
    public bool IsInteractionHintPlaying =>
        (_bottomChipHintTween?.IsRunning() ?? false)
        || (_topChipHintTween?.IsRunning() ?? false);

    public override void _Ready()
    {
        _visualRoot = GetNode<Node2D>("VisualRoot");
        _chipSprite = GetNode<Sprite2D>("VisualRoot/ChipSprite");
        _chipSprite2 = GetNode<Sprite2D>("VisualRoot/ChipSprite2");
        _clickButton = GetNode<Button>("ClickButton");
        _hintLabel = GetNode<Label>("VisualRoot/HintLabel");
        _clickButton.Pressed += OnBetPressed;
        PlayAppear();
    }

    private void OnBetPressed()
    {
        // 是否可下注由 GameManager 决定。未获批准前不可提前播放离场动画，
        // 否则余额不足时筹码会离开桌面并禁用自身，导致无法再次下注。
        EmitSignal(SignalName.BetRequested);
    }

    public void PlayAppear()
    {
        _appearTween?.Kill();
        _secondChipAppearTween?.Kill();
        _secondChipLandingTween?.Kill();
        ResetHintAnimation();
        _visualRoot.Visible = true;
        _visualRoot.Position = VisualRestPosition;
        _visualRoot.Rotation = 0f;
        _visualRoot.Modulate = Colors.White;
        _clickButton.Disabled = true;

        _chipSprite.Position = BottomChipRestPosition + new Vector2(0f, BottomChipStartHeight);
        _chipSprite.Rotation = BottomChipStartRotation;
        _chipSprite2.Position = TopChipRestPosition + new Vector2(0f, TopChipStartHeight);
        _chipSprite2.Rotation = TopChipStartRotation;
        _chipSprite2.Visible = false;

        _appearTween = CreateTween().SetParallel(true);
        _appearTween.TweenProperty(_chipSprite, "position", BottomChipRestPosition, FirstChipDuration)
            .SetTrans(Tween.TransitionType.Cubic)
            .SetEase(Tween.EaseType.Out);
        _appearTween.TweenProperty(_chipSprite, "rotation", 0f, FirstChipDuration)
            .SetTrans(Tween.TransitionType.Quad)
            .SetEase(Tween.EaseType.Out);
        _appearTween.Chain().TweenCallback(Callable.From(() => AudioManager.Instance.PlaySfx("Chip_BetStackLanding")));

        _secondChipAppearTween = CreateTween();
        _secondChipAppearTween.TweenInterval(SecondChipDelay);
        _secondChipAppearTween.TweenCallback(Callable.From(StartSecondChipLanding));
    }

    /// <summary>
    /// 当更高层级的界面覆盖牌桌时，静默结束尚未完成的出现动作。
    /// 筹码会直接停在桌面上的最终状态，不再补播尚未到达落点的音效。
    /// </summary>
    public void CompleteAppearanceSilently()
    {
        var isAppearing = (_appearTween?.IsRunning() ?? false)
            || (_secondChipAppearTween?.IsRunning() ?? false)
            || (_secondChipLandingTween?.IsRunning() ?? false);
        if (!isAppearing)
            return;

        _appearTween?.Kill();
        _secondChipAppearTween?.Kill();
        _secondChipLandingTween?.Kill();
        ResetHintAnimation();

        _visualRoot.Position = VisualRestPosition;
        _visualRoot.Rotation = 0f;
        _visualRoot.Modulate = Colors.White;
        _chipSprite.Position = BottomChipRestPosition;
        _chipSprite.Rotation = 0f;
        _chipSprite2.Position = TopChipRestPosition;
        _chipSprite2.Rotation = 0f;
        _chipSprite2.Visible = true;
        _clickButton.Disabled = false;
    }

    public void PlayLeave()
    {
        _appearTween?.Kill();
        _secondChipAppearTween?.Kill();
        _secondChipLandingTween?.Kill();
        ResetHintAnimation();
        _clickButton.Disabled = true;
        AudioManager.Instance.PlaySfx("Chip_BetStackLeave");

        var leaveTween = CreateTween().SetParallel(true);
        leaveTween.TweenProperty(_visualRoot, "position", VisualRestPosition + LeaveOffset, LeaveDuration)
            .SetTrans(Tween.TransitionType.Quad)
            .SetEase(Tween.EaseType.In);
        leaveTween.TweenProperty(_visualRoot, "modulate:a", 0f, LeaveDuration)
            .SetTrans(Tween.TransitionType.Quad)
            .SetEase(Tween.EaseType.In);
        leaveTween.Chain().TweenCallback(Callable.From(() =>
        {
            _visualRoot.Visible = false;
            _visualRoot.Position = VisualRestPosition;
            _visualRoot.Rotation = 0f;
            _visualRoot.Modulate = Colors.White;
        }));
    }

    private void StartSecondChipLanding()
    {
        if (!_visualRoot.Visible) return;

        _chipSprite2.Visible = true;
        _secondChipLandingTween = CreateTween().SetParallel(true);
        _secondChipLandingTween.TweenProperty(_chipSprite2, "position", TopChipRestPosition, SecondChipDuration)
            .SetTrans(Tween.TransitionType.Cubic)
            .SetEase(Tween.EaseType.Out);
        _secondChipLandingTween.TweenProperty(_chipSprite2, "rotation", 0f, SecondChipDuration)
            .SetTrans(Tween.TransitionType.Quad)
            .SetEase(Tween.EaseType.Out);
        _secondChipLandingTween.Chain().TweenCallback(Callable.From(() => AudioManager.Instance.PlaySfx("Chip_BetStackLanding")));
        _secondChipLandingTween.TweenCallback(Callable.From(() => _clickButton.Disabled = false));
    }

    public void ShowHint(string text)
    {
        _hintLabel.Text = text;
        _hintLabel.Visible = ShowBetHintText;

        if (!_visualRoot.Visible)
            PlayAppear();
    }

    public void HideHint()
    {
        _hintLabel.Visible = false;
    }

    public void PlayInteractionHint()
    {
        if (!CanPlayInteractionHint)
            return;

        ResetHintAnimation();
        _visualRoot.Position = VisualRestPosition;
        _visualRoot.Rotation = 0f;
        // 提示动作每轮只播一次聚合音效，避免两枚筹码与自动提示循环造成叠音。
        AudioManager.Instance.PlaySfx("Chip_BetStackHint");

        _bottomChipHintTween = CreateChipHintTween(
            _chipSprite, BottomChipRestPosition, 0.0, HintFirstLift, HintFirstRotation);
        if (_chipSprite2.Visible)
        {
            _topChipHintTween = CreateChipHintTween(
                _chipSprite2, TopChipRestPosition, TopChipHintDelay, HintFirstLift - 1f, -HintFirstRotation);
        }
    }

    private Tween CreateChipHintTween(
        Sprite2D chip,
        Vector2 restPosition,
        double delay,
        float firstLift,
        float firstRotation)
    {
        var tween = CreateTween();
        tween.TweenInterval(delay);
        tween.TweenProperty(chip, "position:y", restPosition.Y - firstLift, HintFirstDuration)
            .SetTrans(Tween.TransitionType.Quad)
            .SetEase(Tween.EaseType.Out);
        tween.Parallel().TweenProperty(chip, "rotation", firstRotation, HintFirstDuration)
            .SetTrans(Tween.TransitionType.Quad)
            .SetEase(Tween.EaseType.Out);
        tween.Chain().TweenProperty(chip, "position:y", restPosition.Y, HintFirstDuration)
            .SetTrans(Tween.TransitionType.Bounce)
            .SetEase(Tween.EaseType.Out);
        tween.Parallel().TweenProperty(chip, "rotation", 0f, HintFirstDuration)
            .SetTrans(Tween.TransitionType.Quad)
            .SetEase(Tween.EaseType.Out);
        tween.Chain().TweenProperty(chip, "position:y", restPosition.Y - HintSecondLift, HintSecondDuration)
            .SetTrans(Tween.TransitionType.Quad)
            .SetEase(Tween.EaseType.Out);
        tween.Parallel().TweenProperty(chip, "rotation", -firstRotation * 0.5f, HintSecondDuration)
            .SetTrans(Tween.TransitionType.Quad)
            .SetEase(Tween.EaseType.Out);
        tween.Chain().TweenProperty(chip, "position:y", restPosition.Y, HintSecondDuration)
            .SetTrans(Tween.TransitionType.Bounce)
            .SetEase(Tween.EaseType.Out);
        tween.Parallel().TweenProperty(chip, "rotation", 0f, HintSecondDuration)
            .SetTrans(Tween.TransitionType.Quad)
            .SetEase(Tween.EaseType.Out);
        return tween;
    }

    private void ResetHintAnimation()
    {
        _bottomChipHintTween?.Kill();
        _topChipHintTween?.Kill();
        _chipSprite.Position = BottomChipRestPosition;
        _chipSprite.Rotation = 0f;
        _chipSprite2.Position = TopChipRestPosition;
        _chipSprite2.Rotation = 0f;
    }
}
