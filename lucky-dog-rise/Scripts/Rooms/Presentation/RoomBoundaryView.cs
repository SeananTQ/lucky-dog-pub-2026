#if !DEMO_BUILD && !RECORDING_BUILD
using Godot;

namespace LuckyDogRise.Rooms;

// Only draws feedback; it never participates in mouse hit testing.
public partial class RoomBoundaryView : Control
{
    [Export] public StyleBoxFlat EdgeStyle { get; private set; } = null!;

    private const double LingerSeconds = .6;
    private const double FadeSeconds = 2;
    private const float RiseSeconds = .15f;
    private const float LineWidth = 6;
    private const float EdgeInset = 2;
    private const float EndInset = 16;
    private const float GlowOffset = 6;
    private static readonly RoomBoundaryEdges[] Edges =
        { RoomBoundaryEdges.Left, RoomBoundaryEdges.Right, RoomBoundaryEdges.Top, RoomBoundaryEdges.Bottom };

    private struct EdgeFeedback
    {
        public float Opacity;
        public float FadeFrom;
        public double IdleSeconds;
    }

    private readonly EdgeFeedback[] _feedback = new EdgeFeedback[4];
    private RoomBoundaryEdges _activeEdges;
    private Rect2 _bounds;
    private StyleBoxFlat _style = null!;
    private Color _lineColor;
    private Color _glowColor;

    public override void _Ready()
    {
        // The two room renderers share the scene resource, but animate independently.
        _style = (StyleBoxFlat)EdgeStyle.Duplicate();
        _lineColor = _style.BgColor;
        _glowColor = _style.ShadowColor;
        MouseFilter = MouseFilterEnum.Ignore;
        Clear();
    }

    internal void Indicate(Rect2 bounds, RoomBoundaryEdges edges)
    {
        bool moved = _bounds != bounds;
        _bounds = bounds;
        SetActiveEdges(edges);
        if (edges == RoomBoundaryEdges.None)
        {
            if (moved && Visible) QueueRedraw();
            return;
        }
        Show();
        SetProcess(true);
        if (moved) QueueRedraw();
    }

    internal void Release() => SetActiveEdges(RoomBoundaryEdges.None);

    private void SetActiveEdges(RoomBoundaryEdges edges)
    {
        for (int i = 0; i < Edges.Length; i++)
        {
            if ((_activeEdges & Edges[i]) == 0 || (edges & Edges[i]) != 0) continue;
            // Repeated interior motion or mouse release must not restart the linger timer.
            _feedback[i].IdleSeconds = 0;
            _feedback[i].FadeFrom = _feedback[i].Opacity;
        }
        _activeEdges = edges;
    }

    internal void Clear()
    {
        _activeEdges = RoomBoundaryEdges.None;
        System.Array.Clear(_feedback);
        Hide();
        SetProcess(false);
    }

    public override void _Process(double delta)
    {
        bool changed = false, visible = false;
        for (int i = 0; i < Edges.Length; i++)
        {
            ref var feedback = ref _feedback[i];
            float before = feedback.Opacity;
            if ((_activeEdges & Edges[i]) != 0)
                feedback.Opacity = Mathf.MoveToward(before, 1, (float)delta / RiseSeconds);
            else if (before > 0)
            {
                feedback.IdleSeconds += delta;
                float progress = (float)((feedback.IdleSeconds - LingerSeconds) / FadeSeconds);
                feedback.Opacity = feedback.FadeFrom * (1 - Mathf.SmoothStep(0, 1, progress));
            }
            changed |= before != feedback.Opacity;
            visible |= feedback.Opacity > 0;
        }
        if (!visible && _activeEdges == RoomBoundaryEdges.None) Clear();
        else if (changed) QueueRedraw();
    }

    public override void _Draw()
    {
        if (_style == null) return;
        var r = _bounds.Grow(-EdgeInset);
        if (r.Size.X < LineWidth || r.Size.Y < LineWidth) return;
        float insetX = Mathf.Min(EndInset, (r.Size.X - LineWidth) / 2);
        float insetY = Mathf.Min(EndInset, (r.Size.Y - LineWidth) / 2);
        for (int i = 0; i < Edges.Length; i++)
        {
            float opacity = _feedback[i].Opacity;
            if (opacity <= 0) continue;
            _style.BgColor = new Color(_lineColor, _lineColor.A * opacity);
            _style.ShadowColor = new Color(_glowColor, _glowColor.A * opacity);
            _style.ShadowOffset = i switch
            {
                0 => new Vector2(GlowOffset, 0), 1 => new Vector2(-GlowOffset, 0),
                2 => new Vector2(0, GlowOffset), _ => new Vector2(0, -GlowOffset)
            };
            var stripe = i switch
            {
                0 => new Rect2(r.Position.X, r.Position.Y + insetY, LineWidth, r.Size.Y - 2 * insetY),
                1 => new Rect2(r.End.X - LineWidth, r.Position.Y + insetY, LineWidth, r.Size.Y - 2 * insetY),
                2 => new Rect2(r.Position.X + insetX, r.Position.Y, r.Size.X - 2 * insetX, LineWidth),
                _ => new Rect2(r.Position.X + insetX, r.End.Y - LineWidth, r.Size.X - 2 * insetX, LineWidth)
            };
            // StyleBox shadow is narrow canvas geometry, not a blurred fullscreen texture.
            DrawStyleBox(_style, stripe);
        }
    }

#if DEBUG
    internal float OpacityForSmoke(RoomBoundaryEdges edge)
        => _feedback[System.Array.IndexOf(Edges, edge)].Opacity;
#endif
}
#endif
