#if !DEMO_BUILD && !RECORDING_BUILD
using Godot;

namespace LuckyDogRise.Rooms;

// Snapshot-driven card. Replacing a snapshot cancels the old flip; no unowned SceneTree timers.
public partial class RoomPokerCardView : TextureButton
{
    [Export] private Texture2D _back;
    private Tween _flip;
    private int _card = -2;
    private bool _faceUp;
    public int VisibleCard => _faceUp ? _card : -1;

    public void Present(int card, bool faceUp, bool animate)
    {
        if (_card == card && _faceUp == faceUp) return;
        bool hadCard = _card != -2;
        _card = card;
        _faceUp = faceUp && card >= 0;
        var texture = _faceUp ? GD.Load<Texture2D>(DeckManager.CardToAssetPath(card)) : _back;
        _flip?.Kill();
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

    public override void _ExitTree() => _flip?.Kill();
}
#endif
