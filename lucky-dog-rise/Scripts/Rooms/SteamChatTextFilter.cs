#if !DEMO_BUILD && !RECORDING_BUILD
using System;
using System.Text;
using Steamworks;

namespace LuckyDogRise.Rooms;

// Owned by one SteamRoomTransport session. The platform recovery layer creates a
// fresh instance after rebuilding Steam; this class never initializes Steam itself.
internal sealed class SteamChatTextFilter
{
    internal delegate int FilterCall(ETextFilteringContext context, CSteamID sender,
        string input, out string output, uint outputBytes);

    private readonly Func<uint, bool> _initialize;
    private readonly FilterCall _filter;
    private bool _initialized;

    public SteamChatTextFilter() : this(Initialize, SteamUtils.FilterText) { }

    internal SteamChatTextFilter(Func<uint, bool> initialize, FilterCall filter)
    {
        _initialize = initialize;
        _filter = filter;
    }

    public string Filter(ulong senderSteamId, string text)
    {
        if (senderSteamId == 0 || RoomRules.ValidateChat(text).Length > 0)
            throw new ArgumentException("Invalid Steam chat filter input.");

        if (!_initialized)
        {
            // False means filtering is unavailable for this game language. Steam
            // then passes text through; it is not a per-message error or retry signal.
            _initialize(0);
            _initialized = true;
        }

        // Capacity is in UTF-8 bytes, including the terminator. Allow replacement
        // glyphs (e.g. hearts) to take more bytes than the original ASCII characters.
        uint capacity = checked((uint)Encoding.UTF8.GetMaxByteCount(text.Length) + 1);
        int replaced = _filter(ETextFilteringContext.k_ETextFilteringContextChat,
            new CSteamID(senderSteamId), text, out var output, capacity);
        // Zero is a valid unmodified result. Never fall back to input on SDK errors.
        if (replaced < 0 || RoomRules.ValidateChat(output).Length > 0)
            throw new InvalidOperationException("Steam chat filtering returned invalid output.");
        return output;
    }

    private static bool Initialize(uint options)
    {
        bool supported = SteamUtils.InitFilterText(options);
        if (!supported)
            Godot.GD.Print("[Rooms] Steam text filtering is unavailable for the current game language; using Steam passthrough.");
        return supported;
    }
}
#endif
