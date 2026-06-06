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

    public int CardIndex { get; set; }
    public int CardValue { get; private set; } = -1;
    public bool IsHeld { get; private set; }

    public override void _Ready()
    {
        _front = GetNode<Sprite2D>("Front");
        _back = GetNode<Sprite2D>("Back");
        _button = GetNode<Button>("ClickButton");
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

    // 发牌动画：第 delay 秒后开始动画
    // delay 由 CardTableController 传入，控制每张牌依次出现的间隔
    public void AnimateDeal(float delay)
    {
        var targetPos = Position;           // 卡牌最终位置（在 CardArea 中的局部坐标）
        Scale = Vector2.Zero;               // 初始不可见
        ShowBack();

        // 用 Timer 错开实际播放时间，避免 Godot 多 Tween 同步问题
        GetTree().CreateTimer(delay).Timeout += () =>
        {
            Position = new Vector2(targetPos.X, targetPos.Y - 80);  // 起始位置：比最终位置高 80px
            Scale = new Vector2(1.3f, 1.3f);    // 起始缩放：1.3 倍（从大变小）
            Modulate = new Color(1, 1, 1, 0);   // 起始完全透明（从虚到实）

            // 下滑 + 缩小 + 显现
            var slideTween = CreateTween().SetParallel(true);
            slideTween.TweenProperty(this, "position", targetPos, DealSlideDuration)
                .SetEase(Tween.EaseType.Out).SetTrans(Tween.TransitionType.Cubic);
            slideTween.TweenProperty(this, "scale", Vector2.One, DealSlideDuration)
                .SetEase(Tween.EaseType.Out).SetTrans(Tween.TransitionType.Cubic);
            slideTween.TweenProperty(this, "modulate:a", 1f, DealSlideDuration)
                .SetEase(Tween.EaseType.Out);

            // 翻转（下滑结束后）
            GetTree().CreateTimer(DealSlideDuration).Timeout += () =>
            {
                var flipTween = CreateTween();
                flipTween.TweenProperty(this, "scale:x", 0f, FlipDuration)
                    .SetEase(Tween.EaseType.In);
                flipTween.TweenCallback(Callable.From(() => ShowFront()));
                flipTween.TweenProperty(this, "scale:x", 1f, FlipDuration)
                    .SetEase(Tween.EaseType.Out);
            };
        };
    }

    // 补牌动画：翻转显示新牌面
    public void AnimateReplace()
    {
        ResetModulate();
        Scale = Vector2.One;
        ShowBack();

        var tween = CreateTween();
        tween.TweenProperty(this, "scale:x", 0f, FlipDuration)
            .SetEase(Tween.EaseType.In);
        tween.TweenCallback(Callable.From(() => ShowFront()));
        tween.TweenProperty(this, "scale:x", 1f, FlipDuration)
            .SetEase(Tween.EaseType.Out);
    }
}
