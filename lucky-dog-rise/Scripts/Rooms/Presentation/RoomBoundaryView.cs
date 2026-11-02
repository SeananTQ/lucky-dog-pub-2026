#if !DEMO_BUILD && !RECORDING_BUILD
using Godot;

namespace LuckyDogRise.Rooms;

// Only draws feedback; it never participates in mouse hit testing.
public partial class RoomBoundaryView : Control
{
    private RoomBoundaryEdges _edges;
    private Rect2 _bounds;
    private bool _held;
    private double _remaining;
    public override void _Ready() { MouseFilter = MouseFilterEnum.Ignore; SetProcess(false); }
    internal void Indicate(Rect2 bounds, RoomBoundaryEdges edges)
    {
        _bounds = bounds;
        _edges = edges;
        _held = true;
        _remaining = .4;
        Show();
        SetProcess(true);
        QueueRedraw();
    }
    internal void Release() => _held = false;
    internal void Clear() { _edges = RoomBoundaryEdges.None; _held = false; Hide(); SetProcess(false); }
    public override void _Process(double delta)
    {
        if (_held) return;
        _remaining -= delta;
        if (_remaining <= 0) Clear();
        else QueueRedraw();
    }
    public override void _Draw()
    {
        var color = new Color(1f, .28f, .28f, .5f * (float)(_remaining / .4));
        var r = _bounds.Grow(-2);
        if ((_edges & RoomBoundaryEdges.Left) != 0) DrawLine(r.Position, new(r.Position.X, r.End.Y), color, 3);
        if ((_edges & RoomBoundaryEdges.Right) != 0) DrawLine(new(r.End.X, r.Position.Y), r.End, color, 3);
        if ((_edges & RoomBoundaryEdges.Top) != 0) DrawLine(r.Position, new(r.End.X, r.Position.Y), color, 3);
        if ((_edges & RoomBoundaryEdges.Bottom) != 0) DrawLine(new(r.Position.X, r.End.Y), r.End, color, 3);
    }
}
#endif
