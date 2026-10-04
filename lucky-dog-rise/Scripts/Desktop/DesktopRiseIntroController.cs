using Godot;

namespace LuckyDogRise;

public partial class DesktopRiseIntroController : Node2D
{
    [Signal] public delegate void FinishedEventHandler();
    [Signal] public delegate void StatusBarRevealRequestedEventHandler();

    private CanvasLayer _revealLayer = null!;
    private Control _revealClip = null!;
    private Node2D _frontRoot = null!;
    private DogVisual _headDog = null!;
    private DogVisual _clawDog = null!;
    private DogVisual _frontTongueDog = null!;
    private Sprite2D _maskedTongue = null!;
    private Sprite2D _frontTongue = null!;
    private Tween _tween = null!;
    private Tween _tongueTween = null!;
    private int _playRevision;
    private int _finishedRevision = -1;

    private Vector2 _dogPosition;
    private Vector2 _dogScale = Vector2.One;
    private float _taskbarTopY;
    private Vector2 _reverseTonguePosition;
    private bool _reverseShowsTongue;

    [Export] private float _playbackTimeScale = 2.0f;
    [Export] private float _clawRiseOffset = 80f;
    [Export] private float _clawLatchOffset = 0f;
    [Export] private float _headPeekOffset = 64f;
    [Export] private float _headHiddenOffset = 128f;
    [Export] private float _taskbarClipWidth = 265f;
    [Export] private float _headPeekSeconds = 0.10f;
    [Export] private float _clawRiseSeconds = 0.18f;
    [Export] private float _postClawLatchPauseSeconds = 0.08f;
    [Export] private float _headRiseSeconds = 0.38f;
    [Export] private float _tongueSquashSeconds = 0.08f;
    [Export] private float _tongueSquashScaleY = 0.16f;
    [Export] private float _tongueSquashOffsetY = -34f;
    [Export] private float _tongueTransitionHeadProgress = 0.47f;
    [Export] private float _endHoldSeconds = 0.08f;

    public override void _Ready()
    {
        _revealLayer = GetNode<CanvasLayer>("RevealLayer");
        _revealClip = GetNode<Control>("RevealLayer/RevealClip");
        _frontRoot = GetNode<Node2D>("RevealLayer/FrontRoot");
        _headDog = GetNode<DogVisual>("RevealLayer/RevealClip/HeadDog");
        _clawDog = GetNode<DogVisual>("RevealLayer/RevealClip/ClawDog");
        _frontTongueDog = GetNode<DogVisual>("RevealLayer/FrontRoot/FrontTongueDog");
        _maskedTongue = GetNode<Sprite2D>("RevealLayer/RevealClip/HeadDog/HeadRoot/Tonghe");
        _frontTongue = GetNode<Sprite2D>("RevealLayer/FrontRoot/FrontTongueDog/HeadRoot/Tonghe");

        _headDog.SetHitButtonEnabled(false);
        _clawDog.SetHitButtonEnabled(false);
        _frontTongueDog.SetHitButtonEnabled(false);
        HideImmediate();
    }

    public void BindGameData(GameData gameData)
    {
        if (!IsNodeReady()) return;
        _headDog.GameData = gameData;
        _clawDog.GameData = gameData;
        _frontTongueDog.GameData = gameData;
        RefreshVisuals();
    }

    public void Configure(
        Vector2 contentOffset,
        Vector2 dogPosition,
        Vector2 dogScale,
        float taskbarTopY,
        float desktopPetScaleFactor = 1f)
    {
        _dogPosition = dogPosition;
        _dogScale = dogScale;
        _taskbarTopY = taskbarTopY;
        if (!IsNodeReady()) return;

        _revealLayer.Offset = contentOffset;
        _revealLayer.Scale = Vector2.One * desktopPetScaleFactor;
        _revealClip.Position = Vector2.Zero;
        // This game-owned clip is the real occluder. It remains correct even when
        // the player's Windows taskbar is translucent or fully transparent.
        _revealClip.Size = new Vector2(_taskbarClipWidth, _taskbarTopY);
        ApplyDogTransform(_dogPosition);
    }

    public void RefreshVisuals()
    {
        if (!IsNodeReady()) return;
        _headDog.RefreshEquippedDisguiseVisuals();
        _clawDog.RefreshEquippedDisguiseVisuals();
        _frontTongueDog.RefreshEquippedDisguiseVisuals();
    }

    public void Play()
    {
        if (!IsNodeReady()) return;

        int revision = ++_playRevision;
        _tween?.Kill();
        _tween = null;
        _tongueTween?.Kill();
        _tongueTween = null;
        RefreshVisuals();
        Visible = true;
        _revealLayer.Visible = true;

        var clawStart = _dogPosition + new Vector2(0f, _clawRiseOffset);
        var clawLatch = _dogPosition + new Vector2(0f, _clawLatchOffset);
        var headHiddenStart = _dogPosition + new Vector2(0f, _headHiddenOffset);
        var headStart = _dogPosition + new Vector2(0f, _headPeekOffset);

        if (_clawDog.GetParent() != _revealClip)
            _clawDog.Reparent(_revealClip, keepGlobalTransform: false);

        _headDog.Position = headHiddenStart;
        _clawDog.Position = clawStart;
        _frontTongueDog.Position = headHiddenStart;
        _headDog.SetIntroPartVisibility(showHeadParts: true, showTongue: false, showClaws: false);
        _clawDog.ShowClawPalm();
        _clawDog.SetIntroPartVisibility(showHeadParts: false, showTongue: false, showClaws: true);
        _frontTongueDog.SetIntroPartVisibility(showHeadParts: false, showTongue: false, showClaws: false);
        _headDog.SetIntroTongueScaleY(1f);
        _frontTongueDog.SetIntroTongueScaleY(1f);
        var tongueBasePosition = _maskedTongue.Position;
        var tongueSquashPosition = tongueBasePosition + new Vector2(0f, _tongueSquashOffsetY);
        _maskedTongue.Position = tongueBasePosition;
        _frontTongue.Position = tongueBasePosition;

        _tween = CreateTween();
        _tween.SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.Out);
        _tween.TweenProperty(_headDog, "position", headStart, Seconds(_headPeekSeconds))
            .SetTrans(Tween.TransitionType.Cubic).SetEase(Tween.EaseType.Out);
        _tween.Parallel().TweenProperty(_clawDog, "position", clawLatch, Seconds(_clawRiseSeconds))
            .SetTrans(Tween.TransitionType.Quart).SetEase(Tween.EaseType.Out);
        _tween.TweenCallback(Callable.From(() =>
        {
            if (revision != _playRevision) return;
            // The same claw crosses from the masked region into the taskbar foreground.
            _clawDog.ShowClawBack();
            _clawDog.Reparent(_frontRoot, keepGlobalTransform: true);
        }));
        _tween.TweenInterval(Seconds(_postClawLatchPauseSeconds));
        _tween.TweenCallback(Callable.From(() =>
        {
            if (revision != _playRevision) return;
            _headDog.SetIntroPartVisibility(showHeadParts: true, showTongue: true, showClaws: false);
            StartTongueForegroundTransition(
                revision,
                tongueBasePosition,
                tongueSquashPosition);
        }));
        _tween.TweenProperty(_headDog, "position", _dogPosition, Seconds(_headRiseSeconds))
            .SetTrans(Tween.TransitionType.Quart).SetEase(Tween.EaseType.Out);
        _tween.TweenCallback(Callable.From(() =>
        {
            if (revision == _playRevision)
                EmitSignal(SignalName.StatusBarRevealRequested);
        }));
        _tween.TweenInterval(Seconds(_endHoldSeconds));
        _tween.TweenCallback(Callable.From(() =>
        {
            if (revision != _playRevision || _finishedRevision == revision) return;
            _finishedRevision = revision;
            HideVisuals();
            EmitSignal(SignalName.Finished);
        }));
    }

    public void HideImmediate()
    {
        _playRevision++;
        _tween?.Kill();
        _tween = null;
        _tongueTween?.Kill();
        _tongueTween = null;
        HideVisuals();
    }

    // Reuse the startup parts and mask inside a room avatar's own canvas. A new
    // CanvasLayer would escape its parent's position, scale and visibility.
    // Copy the displayed pose, never bind the remote dog to local equipment.
    public void BeginReverse(DogVisual source)
    {
        HideImmediate();
        _revealClip.Reparent(this, keepGlobalTransform: false);
        _frontRoot.Reparent(this, keepGlobalTransform: false);
        _dogPosition = new Vector2(_taskbarClipWidth / 2f, _revealClip.Size.Y);
        Position = source.Position - _dogPosition;
        _dogScale = source.Scale;
        foreach (var dog in new[] { _headDog, _clawDog, _frontTongueDog })
        {
            CopyPose(source, dog);
            dog.SetProcess(false);
        }
        ApplyDogTransform(_dogPosition);
        _reverseTonguePosition = _maskedTongue.Position;
        _reverseShowsTongue = _maskedTongue.Visible;
        _headDog.GetNode<Node2D>("ClawLeft").Hide();
        _headDog.GetNode<Node2D>("ClawRight").Hide();
        foreach (var dog in new[] { _clawDog, _frontTongueDog })
            foreach (var part in dog.GetNode<Node2D>("HeadRoot").GetChildren())
                if (part is CanvasItem visual) visual.Hide();
        _frontTongueDog.GetNode<Node2D>("ClawLeft").Hide();
        _frontTongueDog.GetNode<Node2D>("ClawRight").Hide();
        Show();
        SeekReverse(0);
    }

    // Sample the startup timeline backwards. The room owns elapsed time, so a
    // hidden window, skipped frame or delayed snapshot cannot restart departure.
    public void SeekReverse(float progress)
    {
        float latchAt = Mathf.Max(_headPeekSeconds, _clawRiseSeconds);
        float riseAt = latchAt + _postClawLatchPauseSeconds;
        float time = (1f - Mathf.Clamp(progress, 0f, 1f)) * (riseAt + _headRiseSeconds + _endHoldSeconds);
        float HeadOffset(float at) => at < riseAt
            ? Mathf.Lerp(_headHiddenOffset, _headPeekOffset, EaseOut(at / _headPeekSeconds, 3))
            : Mathf.Lerp(_headPeekOffset, 0f, EaseOut((at - riseAt) / _headRiseSeconds, 4));
        _headDog.Position = _dogPosition + Vector2.Down * HeadOffset(time);

        bool latched = time >= latchAt;
        var clawParent = latched ? (Node)_frontRoot : _revealClip;
        if (_clawDog.GetParent() != clawParent)
        {
            _clawDog.Reparent(clawParent, keepGlobalTransform: false);
            if (latched) _clawDog.ShowClawBack();
            else _clawDog.ShowClawPalm();
        }
        _clawDog.Position = _dogPosition + Vector2.Down * Mathf.Lerp(
            _clawRiseOffset, _clawLatchOffset, EaseOut(time / _clawRiseSeconds, 4));

        float squashAt = riseAt + _headRiseSeconds * _tongueTransitionHeadProgress;
        float foregroundAt = squashAt + _tongueSquashSeconds;
        bool foreground = time >= foregroundAt;
        _maskedTongue.Visible = _reverseShowsTongue && time >= riseAt && !foreground;
        _frontTongue.Visible = _reverseShowsTongue && foreground;
        _frontTongueDog.Position = _dogPosition + Vector2.Down * HeadOffset(foregroundAt);
        float tongueProgress = Mathf.Clamp((time - (foreground ? foregroundAt : squashAt)) / _tongueSquashSeconds, 0f, 1f);
        var tongue = foreground ? _frontTongue : _maskedTongue;
        float tongueScale = EaseOut(tongueProgress, 3);
        tongue.Scale = new Vector2(1f, foreground
            ? Mathf.Lerp(_tongueSquashScaleY, 1f, tongueScale)
            : Mathf.Lerp(1f, _tongueSquashScaleY, tongueScale));
        tongue.Position = _reverseTonguePosition + Vector2.Down * _tongueSquashOffsetY
            * (foreground ? 1f - tongueProgress : tongueProgress);
    }

    private static float EaseOut(float progress, int power)
        => 1f - Mathf.Pow(1f - Mathf.Clamp(progress, 0f, 1f), power);

    private static void CopyPose(Node2D source, Node2D target)
    {
        target.Transform = source.Transform;
        target.Visible = source.Visible;
        target.ZIndex = source.ZIndex;
        target.ZAsRelative = source.ZAsRelative;
        if (source is Sprite2D sprite && target is Sprite2D copy)
        {
            copy.Texture = sprite.Texture;
            copy.Material = sprite.Material;
        }
        foreach (var child in source.GetChildren())
            if (child is Node2D part && target.GetNodeOrNull<Node2D>(new NodePath(part.Name)) is { } other)
                CopyPose(part, other);
    }

    private void StartTongueForegroundTransition(
        int revision,
        Vector2 tongueBasePosition,
        Vector2 tongueSquashPosition)
    {
        _tongueTween?.Kill();
        _tongueTween = CreateTween();
        _tongueTween.TweenInterval(Seconds(_headRiseSeconds * _tongueTransitionHeadProgress));
        _tongueTween.TweenProperty(_maskedTongue, "scale:y", _tongueSquashScaleY, Seconds(_tongueSquashSeconds))
            .SetTrans(Tween.TransitionType.Cubic).SetEase(Tween.EaseType.Out);
        _tongueTween.Parallel().TweenProperty(_maskedTongue, "position", tongueSquashPosition, Seconds(_tongueSquashSeconds));
        _tongueTween.TweenCallback(Callable.From(() =>
        {
            if (revision != _playRevision) return;
            _headDog.SetIntroPartVisibility(showHeadParts: true, showTongue: false, showClaws: false);
            _frontTongueDog.Position = _headDog.Position;
            _frontTongueDog.SetIntroTongueScaleY(_tongueSquashScaleY);
            _frontTongue.Position = tongueSquashPosition;
            _frontTongueDog.SetIntroPartVisibility(showHeadParts: false, showTongue: true, showClaws: false);
        }));
        _tongueTween.TweenProperty(_frontTongue, "scale:y", 1f, Seconds(_tongueSquashSeconds))
            .SetTrans(Tween.TransitionType.Cubic).SetEase(Tween.EaseType.Out);
        _tongueTween.Parallel().TweenProperty(_frontTongue, "position", tongueBasePosition, Seconds(_tongueSquashSeconds));
    }

    private void HideVisuals()
    {
        Visible = false;
        if (_revealLayer != null)
            _revealLayer.Visible = false;
        _headDog?.SetIntroTongueScaleY(1f);
        _frontTongueDog?.SetIntroTongueScaleY(1f);
        _headDog?.SetIntroPartVisibility(false, false, false);
        _clawDog?.SetIntroPartVisibility(false, false, false);
        _frontTongueDog?.SetIntroPartVisibility(false, false, false);
    }

    private void ApplyDogTransform(Vector2 position)
    {
        if (!IsNodeReady()) return;
        _headDog.Position = position;
        _clawDog.Position = position;
        _headDog.Scale = _dogScale;
        _clawDog.Scale = _dogScale;
        _frontTongueDog.Position = position;
        _frontTongueDog.Scale = _dogScale;
    }

    private double Seconds(float baseSeconds) => baseSeconds * _playbackTimeScale;
}
