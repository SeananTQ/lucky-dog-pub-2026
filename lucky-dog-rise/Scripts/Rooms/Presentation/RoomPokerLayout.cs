#if !DEMO_BUILD && !RECORDING_BUILD
using System;
using Godot;

namespace LuckyDogRise.Rooms;

public enum RoomPokerDisplay { Hidden, Free }

// Local pre-join host preference. Both hosts use freely placed poker companions.
public enum RoomDisplayMode { FullScreenFree, HalfScreenCompact }

// Local presentation only. No room mode, seat, membership or GPU auto-detection.
public static class RoomPokerLayout
{
    public static float FitScale(Vector2I available, float requested, int infoBase, int gameBase, int accessory)
    {
        // Leave room for edge padding and independent integer rounding of Info/A.
        // This is a render scale only; saved preferences and pet scale are unchanged.
        return Math.Min(requested, Math.Max(.01f, Math.Min(
            (available.X - 12f) / (infoBase + gameBase),
            (available.Y - accessory - 12f) / gameBase)));
    }
}
#endif
