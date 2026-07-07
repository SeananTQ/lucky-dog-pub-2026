using Godot;

namespace LuckyDogRise;

public partial class DesktopRiseIntroController : Node2D
{
    [Signal]
    public delegate void FinishedEventHandler();

    private CanvasLayer _behindLayer = null!;
    private Control _behindClip = null!;
    private CanvasLayer _frontLayer = null!;
    private DogVisual _behindHeadDog = null!;
    private DogVisual _behindClawDog = null!;
    private DogVisual _frontClawDog = null!;
    private DogVisual _frontTongueDog = null!;
    private Tween _tween = null!;

    private Vector2 _dogPosition;
    private Vector2 _dogScale = Vector2.One;

    private const double DebugTimeScale = 10.0;

    [Export] private float _clawRiseOffset = 80f;
    [Export] private float _clawLatchOffset = 4f;
    [Export] private float _headPeekOffset = 64f;
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
        var headStart = _dogPosition + new Vector2(0f, _headPeekOffset);
        var behindTongue = _behindHeadDog.GetNode<Sprite2D>("HeadRoot/Tonghe");
        var frontTongue = _frontTongueDog.GetNode<Sprite2D>("HeadRoot/Tonghe");

        ApplyDogTransform(headStart);
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
        _tween.TweenProperty(_behindClawDog, "position", clawLatch, 0.18 * DebugTimeScale);
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
        var tongueSwitchPosition = _dogPosition + new Vector2(0f, _tongueSwitchDistance);
        _tween.TweenProperty(_behindHeadDog, "position", tongueSwitchPosition, 0.34 * DebugTimeScale);
        _tween.TweenProperty(_behindHeadDog.GetNode<Sprite2D>("HeadRoot/Tonghe"), "scale:y", _tongueSquashScaleY, 0.08 * DebugTimeScale);
        _tween.Parallel().TweenProperty(behindTongue, "position", tongueSquashPosition, 0.08 * DebugTimeScale);
        _tween.TweenCallback(Callable.From(() =>
        {
            _behindHeadDog.SetIntroPartVisibility(showHeadParts: true, showTongue: false, showClaws: false);
            _frontTongueDog.Position = tongueSwitchPosition;
            _frontTongueDog.SetIntroTongueScaleY(_tongueSquashScaleY);
            frontTongue.Position = tongueSquashPosition;
            _frontTongueDog.SetIntroPartVisibility(showHeadParts: false, showTongue: true, showClaws: false);
        }));
        TweenHeadAndTonguePositions(_tween, _dogPosition, 0.08 * DebugTimeScale);
        _tween.Parallel().TweenProperty(frontTongue, "scale:y", 1f, 0.08 * DebugTimeScale);
        _tween.Parallel().TweenProperty(frontTongue, "position", tongueBasePosition, 0.08 * DebugTimeScale);
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

    private void TweenHeadAndTonguePositions(Tween tween, Vector2 target, double duration)
    {
        tween.TweenProperty(_behindHeadDog, "position", target, duration);
        tween.Parallel().TweenProperty(_frontTongueDog, "position", target, duration);
    }
}
