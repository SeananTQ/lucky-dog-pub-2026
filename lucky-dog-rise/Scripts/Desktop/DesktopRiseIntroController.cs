using Godot;

namespace LuckyDogRise;

public partial class DesktopRiseIntroController : Node2D
{
    [Signal]
    public delegate void FinishedEventHandler();
    [Signal]
    public delegate void StatusBarRevealRequestedEventHandler();

    private CanvasLayer _behindLayer = null!;
    private Control _behindClip = null!;
    private CanvasLayer _frontLayer = null!;
    private DogVisual _behindHeadDog = null!;
    private DogVisual _behindClawDog = null!;
    private DogVisual _frontClawDog = null!;
    private DogVisual _frontTongueDog = null!;
    private Tween _tween = null!;
    private Tween _tongueLayerTween = null!;

    private Vector2 _dogPosition;
    private Vector2 _dogScale = Vector2.One;

    private const double DebugTimeScale = 2.0;

    [Export] private float _clawRiseOffset = 80f;
    [Export] private float _clawLatchOffset = 0f;
    [Export] private float _headPeekOffset = 64f;
    [Export] private float _headHiddenOffset = 128f;
    [Export] private float _tongueSwitchDistance = 5f;
    [Export] private float _tongueSquashScaleY = 0.16f;
    [Export] private float _tongueSquashOffsetY = -34f;
    [Export] private float _clipWidth = 265f;

    public override void _Ready()
    {
        _behindLayer = GetNode<CanvasLayer>("BehindLayer");
        _behindClip = GetNode<Control>("BehindLayer/BehindClip");
        _frontLayer = GetNode<CanvasLayer>("FrontLayer");
        _behindHeadDog = GetNode<DogVisual>("BehindLayer/BehindClip/BehindHeadDog");
        _behindClawDog = GetNode<DogVisual>("BehindLayer/BehindClip/BehindClawDog");
        _frontClawDog = GetNode<DogVisual>("FrontLayer/FrontClawDog");
        _frontTongueDog = GetNode<DogVisual>("FrontLayer/FrontTongueDog");

        _behindHeadDog.ShowEquippedEyewearByDefault = true;
        _behindClawDog.ShowEquippedEyewearByDefault = true;
        _frontClawDog.ShowEquippedEyewearByDefault = true;
        _frontTongueDog.ShowEquippedEyewearByDefault = true;
        _behindHeadDog.SetHitButtonEnabled(false);
        _behindClawDog.SetHitButtonEnabled(false);
        _frontClawDog.SetHitButtonEnabled(false);
        _frontTongueDog.SetHitButtonEnabled(false);
        HideImmediate();
    }

    public void BindGameData(GameData gameData)
    {
        if (!IsNodeReady()) return;

        _behindHeadDog.GameData = gameData;
        _behindClawDog.GameData = gameData;
        _frontClawDog.GameData = gameData;
        _frontTongueDog.GameData = gameData;
        RefreshVisuals();
    }

    public void Configure(Vector2 contentOffset, Vector2 dogPosition, Vector2 dogScale)
    {
        _dogPosition = dogPosition;
        _dogScale = dogScale;

        if (!IsNodeReady()) return;

        _behindLayer.Offset = contentOffset;
        _frontLayer.Offset = contentOffset;
        _behindClip.Position = Vector2.Zero;
        _behindClip.Size = new Vector2(_clipWidth, _dogPosition.Y);
        ApplyDogTransform(_dogPosition);
    }

    public void RefreshVisuals()
    {
        if (!IsNodeReady()) return;

        _behindHeadDog.RefreshEquippedDisguiseVisuals();
        _behindHeadDog.RefreshEquippedEyewear(showIfEquipped: true);
        _behindClawDog.RefreshEquippedDisguiseVisuals();
        _behindClawDog.RefreshEquippedEyewear(showIfEquipped: true);
        _frontClawDog.RefreshEquippedDisguiseVisuals();
        _frontClawDog.RefreshEquippedEyewear(showIfEquipped: true);
        _frontTongueDog.RefreshEquippedDisguiseVisuals();
        _frontTongueDog.RefreshEquippedEyewear(showIfEquipped: true);
    }

    public void Play()
    {
        if (!IsNodeReady()) return;

        _tween?.Kill();
        RefreshVisuals();
        Visible = true;
        _behindLayer.Visible = true;
        _frontLayer.Visible = true;

        var clawStart = _dogPosition + new Vector2(0f, _clawRiseOffset);
        var clawLatch = _dogPosition + new Vector2(0f, _clawLatchOffset);
        var headHiddenStart = _dogPosition + new Vector2(0f, _headHiddenOffset);
        var headStart = _dogPosition + new Vector2(0f, _headPeekOffset);
        var behindTongue = _behindHeadDog.GetNode<Sprite2D>("HeadRoot/Tonghe");
        var frontTongue = _frontTongueDog.GetNode<Sprite2D>("HeadRoot/Tonghe");

        ApplyDogTransform(headHiddenStart);
        _behindClawDog.Position = clawStart;
        _behindHeadDog.SetIntroPartVisibility(showHeadParts: true, showTongue: false, showClaws: false);
        _behindClawDog.ShowClawPalm();
        _behindClawDog.SetIntroPartVisibility(showHeadParts: false, showTongue: false, showClaws: true);
        _frontClawDog.SetIntroPartVisibility(showHeadParts: false, showTongue: false, showClaws: false);
        _frontTongueDog.SetIntroPartVisibility(showHeadParts: false, showTongue: false, showClaws: false);
        _behindHeadDog.SetIntroTongueScaleY(1f);
        _frontTongueDog.SetIntroTongueScaleY(1f);
        var tongueBasePosition = behindTongue.Position;
        var tongueSquashPosition = tongueBasePosition + new Vector2(0f, _tongueSquashOffsetY);
        behindTongue.Position = tongueBasePosition;
        frontTongue.Position = tongueBasePosition;

        _tween = CreateTween();
        _tween.SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.Out);
        _tween.TweenProperty(_behindHeadDog, "position", headStart, 0.10 * DebugTimeScale)
            .SetTrans(Tween.TransitionType.Cubic)
            .SetEase(Tween.EaseType.Out);
        _tween.Parallel().TweenProperty(_frontTongueDog, "position", headStart, 0.10 * DebugTimeScale)
            .SetTrans(Tween.TransitionType.Cubic)
            .SetEase(Tween.EaseType.Out);
        _tween.Parallel().TweenProperty(_behindClawDog, "position", clawLatch, 0.18 * DebugTimeScale)
            .SetTrans(Tween.TransitionType.Quart)
            .SetEase(Tween.EaseType.Out);
        _tween.TweenCallback(Callable.From(() =>
        {
            _behindClawDog.SetIntroPartVisibility(showHeadParts: false, showTongue: false, showClaws: false);
            _frontClawDog.Position = clawLatch;
            _frontClawDog.ShowClawBack();
            _frontClawDog.SetIntroPartVisibility(showHeadParts: false, showTongue: false, showClaws: true);
        }));
        _tween.TweenInterval(0.08 * DebugTimeScale);
        _tween.TweenCallback(Callable.From(() =>
        {
            _frontTongueDog.Position = headStart;
            _behindHeadDog.SetIntroPartVisibility(showHeadParts: true, showTongue: true, showClaws: false);
        }));

        var tongueTransitionStarted = false;
        var frontTongueVisible = false;
        _tween.TweenMethod(
            Callable.From<float>(progress =>
            {
                var eased = EaseOutQuart(progress);
                var headPosition = headStart.Lerp(_dogPosition, eased);
                _behindHeadDog.Position = headPosition;

                if (frontTongueVisible)
                    _frontTongueDog.Position = headPosition;

                if (tongueTransitionStarted || headPosition.Y > _dogPosition.Y + _tongueSwitchDistance)
                    return;

                tongueTransitionStarted = true;
                _tongueLayerTween?.Kill();
                _tongueLayerTween = CreateTween();
                _tongueLayerTween.SetTrans(Tween.TransitionType.Cubic).SetEase(Tween.EaseType.Out);
                _tongueLayerTween.TweenProperty(behindTongue, "scale:y", _tongueSquashScaleY, 0.08 * DebugTimeScale);
                _tongueLayerTween.Parallel().TweenProperty(behindTongue, "position", tongueSquashPosition, 0.08 * DebugTimeScale);
                _tongueLayerTween.TweenCallback(Callable.From(() =>
                {
                    _behindHeadDog.SetIntroPartVisibility(showHeadParts: true, showTongue: false, showClaws: false);
                    _frontTongueDog.Position = _behindHeadDog.Position;
                    _frontTongueDog.SetIntroTongueScaleY(_tongueSquashScaleY);
                    frontTongue.Position = tongueSquashPosition;
                    _frontTongueDog.SetIntroPartVisibility(showHeadParts: false, showTongue: true, showClaws: false);
                    frontTongueVisible = true;
                    EmitSignal(SignalName.StatusBarRevealRequested);
                }));
                _tongueLayerTween.TweenProperty(frontTongue, "scale:y", 1f, 0.08 * DebugTimeScale);
                _tongueLayerTween.Parallel().TweenProperty(frontTongue, "position", tongueBasePosition, 0.08 * DebugTimeScale);
            }),
            0f,
            1f,
            0.38 * DebugTimeScale)
            .SetTrans(Tween.TransitionType.Linear)
            .SetEase(Tween.EaseType.InOut);
        _tween.TweenCallback(Callable.From(() =>
        {
            if (!tongueTransitionStarted)
            {
                tongueTransitionStarted = true;
                _behindHeadDog.SetIntroPartVisibility(showHeadParts: true, showTongue: false, showClaws: false);
                _frontTongueDog.Position = _dogPosition;
                _frontTongueDog.SetIntroPartVisibility(showHeadParts: false, showTongue: true, showClaws: false);
            }
        }));
        _tween.TweenInterval(0.08 * DebugTimeScale);
        _tween.TweenCallback(Callable.From(() =>
        {
            HideImmediate();
            EmitSignal(SignalName.Finished);
        }));
    }

    public void HideImmediate()
    {
        _tween?.Kill();
        _tween = null;
        _tongueLayerTween?.Kill();
        _tongueLayerTween = null;
        Visible = false;
        if (_behindLayer != null)
            _behindLayer.Visible = false;
        if (_frontLayer != null)
            _frontLayer.Visible = false;
        _behindHeadDog?.SetIntroTongueScaleY(1f);
        _frontTongueDog?.SetIntroTongueScaleY(1f);
    }

    private void ApplyDogTransform(Vector2 position)
    {
        if (!IsNodeReady()) return;

        _behindHeadDog.Position = position;
        _behindClawDog.Position = position;
        _frontClawDog.Position = position;
        _frontTongueDog.Position = position;
        _behindHeadDog.Scale = _dogScale;
        _behindClawDog.Scale = _dogScale;
        _frontClawDog.Scale = _dogScale;
        _frontTongueDog.Scale = _dogScale;
    }

    private static float EaseOutQuart(float t)
    {
        t = Mathf.Clamp(t, 0f, 1f);
        var inv = 1f - t;
        return 1f - inv * inv * inv * inv;
    }
}
