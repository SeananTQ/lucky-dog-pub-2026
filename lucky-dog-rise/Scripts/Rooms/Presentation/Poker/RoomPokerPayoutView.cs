#if !DEMO_BUILD && !RECORDING_BUILD
using Godot;

namespace LuckyDogRise.Rooms;

// Cosmetic only. The settlement has already paid every winner exactly once.
// Frame-driven positions follow a dragged target; hiding/freeing needs no delayed callbacks.
public partial class RoomPokerPayoutView : Node2D
{
    [Export] private Texture2D _chip;
    [Export] private Label _dealer;
    private bool _payoutVisible;
    public bool DealerFlying => Visible && _dealer.Visible;
    private Vector2 _start, _end;
    private double _seconds;
    private float _size;
    public void Present(Vector2 start, Vector2 end, double seconds, float size, bool visible)
    {
        _start = start; _end = end; _seconds = seconds; _size = size;
        _dealer.Hide();
        Visible = _payoutVisible = visible && seconds >= 0 && seconds < 1.15;
        if (Visible) QueueRedraw();
    }

    public void PresentDealer(Vector2 start, Vector2 end, double seconds, float size, bool visible)
    {
        _payoutVisible = false;
        Visible = _dealer.Visible = visible;
        if (!visible) return;
        float t = Mathf.Clamp((float)(seconds / Poker.PokerRoundPresentation.DealerFlightSeconds), 0, 1);
        float eased = t * t * (3 - 2 * t);
        _dealer.Scale = Vector2.One * (size / 22);
        _dealer.Position = start.Lerp(end, eased) + Vector2.Up * Mathf.Sin(t * Mathf.Pi) * size * 2
            - _dealer.Size * _dealer.Scale / 2;
        QueueRedraw();
    }
    public override void _Draw()
    {
        if (!_payoutVisible) return;
        for (int i = 0; i < 5; i++)
        {
            float t = (float)((_seconds - i * 0.08) / 0.75);
            if (t < 0 || t > 1) continue;
            float eased = 1 - (1 - t) * (1 - t);
            Vector2 position = _start.Lerp(_end, eased) + Vector2.Up * Mathf.Sin(t * Mathf.Pi) * _size * 2;
            float alpha = Mathf.Min(1, t * 12) * Mathf.Min(1, (1 - t) * 8);
            Vector2 size = new(_size, _size * _chip.GetHeight() / _chip.GetWidth());
            DrawTextureRect(_chip, new Rect2(position - size / 2, size), false,
                new Color(1, 1, 1, alpha));
        }
    }
}
#endif
