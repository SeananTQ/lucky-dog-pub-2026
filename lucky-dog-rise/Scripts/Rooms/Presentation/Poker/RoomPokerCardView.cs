#if !DEMO_BUILD && !RECORDING_BUILD
using Godot;

namespace LuckyDogRise.Rooms;

// Snapshot-driven card. Replacing a snapshot cancels the old flip; no unowned SceneTree timers.
public partial class RoomPokerCardView : TextureButton
{
    [Export] private Texture2D _back;
    [Export] private Panel _outline;
    private Tween _flip, _bounce;
    private int _card = -2;
    private bool _faceUp;
    private long _cue = long.MinValue;
    public int VisibleCard => _faceUp ? _card : -1;
    public bool Dimmed { get; private set; }
    public bool Outlined => _outline.Visible;
    public bool Decisive { get; private set; }

    public void Present(int card, bool faceUp, bool animate)
    {
        if (_card == card && _faceUp == faceUp) return;
        bool hadCard = _card != -2;
        _card = card;
        _faceUp = faceUp && card >= 0;
        var texture = _faceUp ? GD.Load<Texture2D>(DeckManager.CardToAssetPath(card)) : _back;
        _flip?.Kill();
        _bounce?.Kill();
        _cue = long.MinValue;
        Scale = Vector2.One;
        PivotOffset = Size / 2;
        if (animate && hadCard && IsVisibleInTree())
        {
            AudioManager.Instance?.PlaySfx(_faceUp ? "Card_PokerHandFlipToFront" : "Card_PokerHandFlipToBack");
            _flip = CreateTween();
            _flip.TweenProperty(this, "scale:x", 0f, 0.08);
            _flip.TweenCallback(Callable.From(() => TextureNormal = texture));
            _flip.TweenProperty(this, "scale:x", 1f, 0.08);
        }
        else TextureNormal = texture;
    }

    public void SetEmphasis(bool dim, bool outlined, bool decisive, long cue, bool animate)
    {
        Dimmed = dim && _faceUp;
        // SelfModulate leaves the child's green outline legible on a dimmed card.
        SelfModulate = Dimmed ? new Color(0.43f, 0.43f, 0.43f) : Colors.White;
        _outline.Visible = outlined && _faceUp;
        decisive &= _faceUp;
        if (Decisive == decisive && _cue == cue) return;
        bool wasDecisive = Decisive;
        Decisive = decisive;
        _cue = cue;
        _bounce?.Kill();
        ZIndex = decisive ? 1 : 0;
        if (!decisive)
        {
            if (wasDecisive && !(_flip?.IsRunning() ?? false)) Scale = Vector2.One;
            return;
        }
        PivotOffset = Size / 2;
        if (!animate || !IsVisibleInTree()) { Scale = Vector2.One * 1.2f; return; }
        _bounce = CreateTween();
        _bounce.TweenInterval(0.2);
        _bounce.TweenProperty(this, "scale", Vector2.One * 1.2f, 0.4)
            .SetTrans(Tween.TransitionType.Back).SetEase(Tween.EaseType.Out);
    }

    public override void _ExitTree() { _flip?.Kill(); _bounce?.Kill(); }
}
#endif
