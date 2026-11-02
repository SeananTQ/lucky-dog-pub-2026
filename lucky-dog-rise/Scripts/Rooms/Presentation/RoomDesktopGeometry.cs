#if !DEMO_BUILD && !RECORDING_BUILD
using System;
using Godot;

namespace LuckyDogRise.Rooms;

[Flags]
internal enum RoomBoundaryEdges { None = 0, Left = 1, Right = 2, Top = 4, Bottom = 8 }

// Screen-space geometry only. No monitor selection or native window mutation:
// a room stays on its selected monitor until the poker window is moved elsewhere.
internal static class RoomDesktopGeometry
{
    public static Rect2I Host(Rect2I screen, bool compact, int? previousX = null)
    {
        // Avoid the Windows backend's exact-monitor exclusive-fullscreen heuristic.
        var safe = new Rect2I(screen.Position + Vector2I.One, screen.Size - Vector2I.One * 2);
        int width = compact ? Math.Max(1, safe.Size.X / 2) : safe.Size.X;
        int x = compact ? Math.Clamp(previousX ?? safe.End.X - width, safe.Position.X, safe.End.X - width) : safe.Position.X;
        return new Rect2I(x, safe.Position.Y, width, safe.Size.Y);
    }

    public static Vector2 Clamp(Vector2 origin, Rect2 footprint, Rect2 bounds)
    {
        static float Fit(float value, float min, float max) => max >= min
            ? Mathf.Clamp(value, min, max) : (min + max) / 2f;
        return new Vector2(Fit(origin.X, bounds.Position.X - footprint.Position.X, bounds.End.X - footprint.End.X),
            Fit(origin.Y, bounds.Position.Y - footprint.Position.Y, bounds.End.Y - footprint.End.Y));
    }

    public static Rect2I Push(Rect2I host, Rect2I screen, Vector2 desiredScreenOrigin, Rect2 footprint)
    {
        var clamped = Clamp(desiredScreenOrigin, footprint, host);
        int shift = Mathf.RoundToInt(desiredScreenOrigin.X - clamped.X);
        return Host(screen, true, host.Position.X + shift);
    }

    public static RoomBoundaryEdges Blocked(Vector2 requested, Vector2 accepted)
    {
        var edges = RoomBoundaryEdges.None;
        if (requested.X < accepted.X - .5f) edges |= RoomBoundaryEdges.Left;
        if (requested.X > accepted.X + .5f) edges |= RoomBoundaryEdges.Right;
        if (requested.Y < accepted.Y - .5f) edges |= RoomBoundaryEdges.Top;
        if (requested.Y > accepted.Y + .5f) edges |= RoomBoundaryEdges.Bottom;
        return edges;
    }
}
#endif
