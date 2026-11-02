#if !DEMO_BUILD && !RECORDING_BUILD
using System;
using Godot;

namespace LuckyDogRise.Rooms;

public enum RoomPokerDisplay { Hidden, Row, Desktop }

// Local pre-join preference. Separate from the actual poker renderer layout:
// HalfScreenCompact currently maps to Row; desktop half-screen movement is a later phase.
public enum RoomDisplayMode { FullScreenFree, HalfScreenCompact }

// Local presentation only. No room mode, seat, membership or GPU auto-detection.
public readonly record struct RoomPokerLayout(Vector2I Size, Vector2 Origin, Rect2 Row, float DogScale)
{
    public static RoomPokerLayout Calculate(Vector2I available, int info, int game, int panel,
        int accessory, float desktopScale)
    {
        // Reserve five places even with fewer members: arrivals do not resize the host.
        int width = Math.Max(info + game, Math.Min(info + game + panel, available.X));
        float scale = Math.Min(desktopScale, Math.Min(1f,
            Math.Min((width - 32f) / (5 * 220f), (available.Y - game - accessory - 16f) / 330f)));
        // At extreme UI sizes, keep the poker controls usable instead of clipping dogs.
        if (scale < .35f) scale = 0;
        int rowHeight = scale > 0 ? (int)Math.Ceiling(330 * scale) + 16 : 0;
        return new(new Vector2I(width, game + accessory + rowHeight), new Vector2(0, accessory + rowHeight),
            new Rect2(16, accessory, Math.Max(0, width - 32), rowHeight), scale);
    }
}
#endif
