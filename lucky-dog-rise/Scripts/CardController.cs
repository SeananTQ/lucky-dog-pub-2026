using Godot;

namespace LuckyDogRise;

public partial class CardController : Node2D
{
    [Signal]
    public delegate void ClickedEventHandler(int index);

    private static readonly Color DimColor = new(0.6f, 0.6f, 0.6f, 1f);
    private const float FlipDuration = 0.08f;
    private const float DealSlideDuration = 0.25f;

    private Sprite2D _front = null!;
    private Sprite2D _back = null!;
    private Button _button = null!;
    private Node2D _body = null!;
    private Sprite2D _shadow = null!;

    public int CardIndex { get; set; }
    public int CardValue { get; private set; } = -1;
    public bool IsHeld { get; private set; }

    public override void _Ready()
    {
        _body = GetNode<Node2D>("CardBody");
        _shadow = GetNode<Sprite2D>("Shadow");
        _front = GetNode<Sprite2D>("CardBody/Front");
        _back = GetNode<Sprite2D>("CardBody/Back");
        _button = GetNode<Button>("CardBody/ClickButton");
        _button.Pressed += () => EmitSignal(SignalName.Clicked, CardIndex);

        ShowBack();
    }

    public void SetCard(int value, int index)
    {
        CardValue = value;
        CardIndex = index;
        if (value >= 0)
        {
            var path = DeckManager.CardToAssetPath(value);
            _front.Texture = GD.Load<Texture2D>(path);
        }
    }

    public void ShowFront()
    {
        _front.Visible = true;
        _back.Visible = false;
    }

    public void ShowBack()
    {
        _front.Visible = false;
        _back.Visible = true;
    }

    public void SetHeld(bool held)
    {
        IsHeld = held;
        _front.Modulate = held ? Colors.White : DimColor;
        _back.Modulate = held ? Colors.White : DimColor;
    }

    public void ResetModulate()
    {
        _front.Modulate = Colors.White;
        _back.Modulate = Colors.White;
    }


    /// <summary>
    ///  动画这里的行注释不要删，方便后期调整动画细节
    /// 发牌动画：第 delay 秒后开始动画
    /// </summary>
    /// <param name="delay"></param>
    public void AnimateDeal(float delay)
    {
        _body.Position = Vector2.Zero;
        _body.Scale = Vector2.Zero;
        _body.Modulate = Colors.White;
        _shadow.Scale = Vector2.One;
        _shadow.Modulate = new Color(0, 0, 0, 0.3f);
        ShowBack();

        GetTree().CreateTimer(delay).Timeout += () =>
        {
            // CardBody 在高处的起始状态
            _body.Position = new Vector2(0, -80);
            _body.Scale = new Vector2(1.3f, 1.3f);
            //_body.Modulate = new Color(1, 1, 1, 0);

            // 阴影：起始小 + 淡，落地点大 + 实
            _shadow.Scale = new Vector2(0.8f, 0.8f);
            _shadow.Modulate = new Color(0, 0, 0, 0.7f);
            _shadow.Position = new Vector2(0, 30);

            // 下滑 + 缩小 + 显现 + 阴影变化（并行）
            var slideTween = CreateTween().SetParallel(true);
            slideTween.TweenProperty(_body, "position", Vector2.Zero, DealSlideDuration)
                .SetEase(Tween.EaseType.Out).SetTrans(Tween.TransitionType.Cubic);
            slideTween.TweenProperty(_body, "scale", Vector2.One, DealSlideDuration)
                .SetEase(Tween.EaseType.Out).SetTrans(Tween.TransitionType.Cubic);
            //slideTween.TweenProperty(_body, "modulate:a", 1f, DealSlideDuration)
            //    .SetEase(Tween.EaseType.Out);

            slideTween.TweenProperty(_shadow, "position", Vector2.Zero, DealSlideDuration)
    .SetEase(Tween.EaseType.Out).SetTrans(Tween.TransitionType.Cubic);
            slideTween.TweenProperty(_shadow, "scale", new Vector2(1f, 1f), DealSlideDuration)
                .SetEase(Tween.EaseType.Out);
            slideTween.TweenProperty(_shadow, "modulate:a", 1f, DealSlideDuration)
                .SetEase(Tween.EaseType.Out);

            // 翻转（下滑结束后，阴影同步缩小再恢复）
            GetTree().CreateTimer(DealSlideDuration).Timeout += () =>
            {
                var flipTween = CreateTween().SetParallel(true);
                flipTween.TweenProperty(_body, "scale:x", 0f, FlipDuration)
                    .SetEase(Tween.EaseType.In);
                flipTween.TweenProperty(_shadow, "scale:x", 0f, FlipDuration)
                    .SetEase(Tween.EaseType.In);
                flipTween.Chain();
                flipTween.TweenCallback(Callable.From(() => ShowFront()));
                flipTween.Chain().SetParallel(true);
                flipTween.TweenProperty(_body, "scale:x", 1f, FlipDuration)
                    .SetEase(Tween.EaseType.Out);
                flipTween.TweenProperty(_shadow, "scale:x", 1f, FlipDuration)
                    .SetEase(Tween.EaseType.Out);
            };
        };
    }

    // 补牌动画：翻转显示新牌面
    public void AnimateReplace()
    {
        ResetModulate();
        _body.Scale = Vector2.One;
        ShowBack();

        var tween = CreateTween();
        tween.TweenProperty(_body, "scale:x", 0f, FlipDuration)
            .SetEase(Tween.EaseType.In);
        tween.TweenCallback(Callable.From(() => ShowFront()));
        tween.TweenProperty(_body, "scale:x", 1f, FlipDuration)
            .SetEase(Tween.EaseType.Out);
    }
}
