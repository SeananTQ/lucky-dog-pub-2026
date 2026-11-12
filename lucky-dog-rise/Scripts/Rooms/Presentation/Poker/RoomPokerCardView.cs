#if !DEMO_BUILD && !RECORDING_BUILD
using Godot;

namespace LuckyDogRise.Rooms;

// Snapshot-driven card. Replacing a snapshot cancels the old flip; no unowned SceneTree timers.
public partial class RoomPokerCardView : TextureButton
{
    [Export] private Texture2D _back;
    [Export] private Panel _outline;
    [Export] private TextureRect _dealShadow;
    private Tween _flip, _bounce, _deal, _motion;
    private ShaderMaterial _flipMaterial;
    private Vector2 _baseScale = Vector2.One, _dealPosition, _home;
    private float _homeRotation;
    private int _homeZ, _shownCard = -1;
    private Vector2? _emphasisPosition;
    private Color _dealTint;
    private bool _dealFaceHidden;
    private int _card = -2;
    private bool _faceUp;
    private long _cue = long.MinValue;
    public int VisibleCard => !_dealFaceHidden ? _shownCard : -1;
    public bool IsFlipping => _flip?.IsRunning() == true;
    public bool IsMoving => IsFlipping || IsDealing || _motion?.IsRunning() == true;
    public bool IsDealing => _deal?.IsRunning() == true;
    public bool Dimmed { get; private set; }
    public bool Outlined => _outline.Visible;
    public bool Decisive { get; private set; }
    internal Vector2 FlipDirection { get; private set; }
    internal float FlipProgress { get; private set; }

    public override void _Ready()
    {
        _baseScale = Scale;
        _home = Position; _homeRotation = Rotation; _homeZ = ZIndex;
        _flipMaterial = Material as ShaderMaterial;
        if (PivotOffset == Vector2.Zero) PivotOffset = Size / 2;
        VisibilityChanged += () => { if (!IsVisibleInTree()) StopDealAnimation(); };
    }

    public void Present(int card, bool faceUp, bool animate, Vector2 pushDirection = default)
    {
        if (_card == card && _faceUp == faceUp) return;
        StopDealAnimation();
        StopFlipAnimation();
        StopEmphasisAnimation();
        bool hadCard = _card != -2;
        _card = card;
        _faceUp = faceUp && card >= 0;
        var texture = _faceUp ? GD.Load<Texture2D>(DeckManager.CardToAssetPath(card)) : _back;
        _cue = long.MinValue;
        Scale = _baseScale;
        if (animate && hadCard && IsVisibleInTree())
        {
            // Public replacements and all revealed hands share one sound for this visual batch.
            AudioManager.Instance?.PlaySfxOncePerFrame(
                _faceUp ? "Card_PokerHandFlipToFront" : "Card_PokerHandFlipToBack", "RoomPokerFlip");
            _flip = CreateTween();
            bool directional = _flipMaterial != null && pushDirection.LengthSquared() > 0.001f;
            if (directional)
            {
                FlipDirection = pushDirection.Normalized();
                _flipMaterial.SetShaderParameter("card_size", Size);
                _flipMaterial.SetShaderParameter("flip_direction", FlipDirection);
                _flip.TweenMethod(Callable.From<float>(SetFlipProgress), 0f, 0.5f, 0.08)
                    .SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.In);
            }
            else
                _flip.TweenProperty(this, "scale:x", 0f, 0.08).SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.Out);
            _flip.TweenCallback(Callable.From(() => { TextureNormal = texture; _shownCard = _faceUp ? card : -1; }));
            if (directional)
            {
                _flip.TweenMethod(Callable.From<float>(SetFlipProgress), 0.5f, 1f, 0.08)
                    .SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.Out);
                _flip.TweenCallback(Callable.From(() => SetFlipProgress(0)));
            }
            else
                _flip.TweenProperty(this, "scale:x", _baseScale.X, 0.08).SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.Out);
        }
        else { TextureNormal = texture; _shownCard = _faceUp ? card : -1; }
    }

    private void SetFlipProgress(float progress)
    {
        FlipProgress = progress;
        _flipMaterial?.SetShaderParameter("flip_progress", progress);
    }

    private void StopFlipAnimation()
    {
        _flip?.Kill(); _flip = null;
        FlipDirection = Vector2.Zero;
        SetFlipProgress(0);
    }

    internal float ChooseMischiefAngle() => _homeRotation + Mathf.DegToRad((float)GD.RandRange(-14, 14));
    internal Transform2D PawPose(float angle) => new(angle, _baseScale, 0, Position + PivotOffset);

    public void AnimateMischief(int order, float? angle = null)
    {
        _motion?.Kill();
        ZIndex = order;
        // Control picking follows sibling order, not Z. Keep drawing and picking consistent.
        GetParent().MoveChild(this, -1);
        _motion = CreateTween().SetParallel(true);
        _motion.TweenProperty(this, "position", _home + new Vector2((float)GD.RandRange(-10, 10), (float)GD.RandRange(-10, 10)), 0.16)
            .SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.Out);
        _motion.TweenProperty(this, "rotation", angle ?? ChooseMischiefAngle(), 0.16)
            .SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.Out);
    }

    public void AnimateStraighten(int slot)
    {
        _motion?.Kill();
        _motion = CreateTween();
        if (slot > 0) _motion.TweenInterval(slot * 0.05);
        _motion.TweenProperty(this, "rotation", _homeRotation + Mathf.DegToRad(2.4f * (0.62f + slot * 0.095f)), 0.14)
            .SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.Out);
        _motion.Parallel().TweenProperty(this, "position", _home + Vector2.Up * (8 + slot), 0.14)
            .SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.Out);
        _motion.TweenProperty(this, "rotation", _homeRotation, 0.19)
            .SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.Out);
        _motion.Parallel().TweenProperty(this, "position", _home, 0.19)
            .SetTrans(Tween.TransitionType.Bounce).SetEase(Tween.EaseType.Out);
        _motion.TweenCallback(Callable.From(() => ZIndex = _homeZ));
    }

    public void AnimateCollect(int slot)
    {
        _motion?.Kill();
        StopEmphasisAnimation();
        _motion = CreateTween().SetParallel(true);
        _motion.TweenProperty(this, "position", _home + Vector2.Left * Size.X * 0.5f, 0.32)
            .SetDelay(slot * 0.04).SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.Out);
        _motion.TweenProperty(this, "modulate:a", 0f, 0.32 * 0.8).SetDelay(slot * 0.04 + 0.32 * 0.2);
    }

    public void ResetPose()
    {
        StopDealAnimation();
        StopFlipAnimation(); _motion?.Kill(); StopEmphasisAnimation();
        Position = _home; Rotation = _homeRotation; Scale = _baseScale; ZIndex = _homeZ;
        Modulate = Colors.White;
        SelfModulate = Colors.White; _outline.Hide(); Dimmed = Decisive = false; _cue = long.MinValue;
    }

    public void AnimateDeal(double delay, float pitch)
    {
        StopDealAnimation();
        StopFlipAnimation();
        StopEmphasisAnimation();
        _dealPosition = Position;
        _dealTint = Modulate;
        Vector2 shadowCenter = GetTransform() * (Size / 2);
        _dealFaceHidden = true;
        TextureNormal = _back;
        Modulate = new Color(_dealTint, 0);
        // Match the solo card's 80px drop, 1.3x size, 0.25s landing and 0.08s flip halves.
        void Drop(float progress)
        {
            float bodyScale = Mathf.Lerp(1.3f, 1f, progress);
            Position = _dealPosition + Vector2.Up * 80 * (1 - progress);
            Scale = _baseScale * bodyScale;
            // Keep the shadow on the table while its parent card drops, then flip both together.
            Vector2 center = GetTransform().AffineInverse() * (shadowCenter + Vector2.Down * 30 * (1 - progress));
            _dealShadow.Position = center - _dealShadow.Size / 2;
            _dealShadow.Scale = Vector2.One * Mathf.Lerp(0.8f, 1f, progress) / bodyScale;
            _dealShadow.Modulate = new Color(0, 0, 0, Mathf.Lerp(0.7f, 1f, progress));
        }
        _dealShadow.PivotOffset = _dealShadow.Size / 2;
        Drop(0);
        _deal = CreateTween();
        if (delay > 0) _deal.TweenInterval(delay);
        _deal.TweenCallback(Callable.From(() =>
        {
            Modulate = _dealTint;
            _dealShadow.Show();
            AudioManager.Instance?.PlaySfxOncePerFrame("Card_PokerHandDeal", "RoomPokerDeal", pitch);
        }));
        _deal.TweenMethod(Callable.From<float>(Drop), 0f, 1f, 0.25).SetTrans(Tween.TransitionType.Cubic).SetEase(Tween.EaseType.Out);
        _deal.TweenProperty(this, "scale:x", 0f, 0.08).SetEase(Tween.EaseType.In);
        _deal.TweenCallback(Callable.From(() =>
        {
            TextureNormal = _faceUp ? GD.Load<Texture2D>(DeckManager.CardToAssetPath(_card)) : _back;
            _dealFaceHidden = false;
            _shownCard = _faceUp ? _card : -1;
        }));
        _deal.TweenProperty(this, "scale:x", _baseScale.X, 0.08).SetEase(Tween.EaseType.Out);
        _deal.TweenCallback(Callable.From(RestoreDealPose));
    }

    public void StopDealAnimation()
    {
        if (_deal == null) return;
        _deal.Kill();
        RestoreDealPose();
    }

    private void RestoreDealPose()
    {
        _deal = null;
        _dealFaceHidden = false;
        _dealShadow.Hide();
        Position = _dealPosition;
        Scale = _baseScale;
        Modulate = _dealTint;
        TextureNormal = _faceUp ? GD.Load<Texture2D>(DeckManager.CardToAssetPath(_card)) : _back;
        _shownCard = _faceUp ? _card : -1;
    }

    public void SetEmphasis(bool dim, bool outlined, bool decisive, long cue, bool animate)
    {
        Dimmed = dim && _faceUp;
        // SelfModulate leaves the child's green outline legible on a dimmed card.
        SelfModulate = Dimmed ? new Color(0.43f, 0.43f, 0.43f) : Colors.White;
        _outline.Visible = outlined && _faceUp;
        decisive &= _faceUp;
        if (Decisive == decisive && _cue == cue) return;
        StopEmphasisAnimation();
        bool wasDecisive = Decisive;
        Decisive = decisive;
        _cue = cue;
        if (decisive || wasDecisive) ZIndex = decisive ? _homeZ + 1 : _homeZ;
        if (!decisive) return;
        _emphasisPosition = Position;
        if (!animate || !IsVisibleInTree()) { ApplyEmphasisScale(1.2f); return; }
        _bounce = CreateTween();
        _bounce.TweenInterval(0.2);
        _bounce.TweenMethod(Callable.From<float>(ApplyEmphasisScale), 1f, 1.2f, 0.4)
            .SetTrans(Tween.TransitionType.Back).SetEase(Tween.EaseType.Out);
    }

    private void ApplyEmphasisScale(float amount)
    {
        if (_emphasisPosition is not Vector2 position) return;
        Scale = _baseScale * amount;
        // Keep the authored bottom-center fixed, including during overshoot. Compensating
        // position instead of changing PivotOffset preserves rotated/fanned card poses.
        Vector2 anchor = new(Size.X / 2, Size.Y);
        Position = position + ((anchor - PivotOffset) * (_baseScale - Scale)).Rotated(Rotation);
    }

    private void StopEmphasisAnimation()
    {
        _bounce?.Kill();
        _bounce = null;
        if (_emphasisPosition is not Vector2 position) return;
        Position = position;
        Scale = _baseScale;
        _emphasisPosition = null;
    }

    public override void _ExitTree() { _flip?.Kill(); _bounce?.Kill(); _deal?.Kill(); _motion?.Kill(); }
}
#endif
