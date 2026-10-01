#if !DEMO_BUILD && !RECORDING_BUILD
using System;
using System.Collections.Generic;
using System.Globalization;
using Steamworks;

namespace LuckyDogRise.Rooms;

/// <summary>
/// One pending, explicitly accepted invitation. Owned by the recovering platform so a
/// same-account Steam reconnect does not lose an invitation or replay consumed startup args.
/// Accessed on the main thread alongside the shared Steam callback pump.
/// </summary>
public sealed class SteamRoomInviteInbox
{
    private ulong _pendingLobby;
    public bool HasPending => _pendingLobby != 0;

    public bool Queue(ulong lobbyId)
    {
        var lobby = new CSteamID(lobbyId);
        if (!lobby.IsValid() || !lobby.IsLobby()) return false;
        // The latest explicit choice supersedes older invitations; never accumulate joins.
        _pendingLobby = lobbyId;
        return true;
    }

    public bool TryTake(out ulong lobbyId)
    {
        lobbyId = _pendingLobby;
        _pendingLobby = 0;
        return lobbyId != 0;
    }

    public void Clear() => _pendingLobby = 0;

    public static SteamRoomInviteInbox FromCommandLine(IEnumerable<string> arguments)
    {
        var inbox = new SteamRoomInviteInbox();
        if (arguments == null) return inbox;
        var expectLobby = false;
        foreach (var argument in arguments)
        {
            if (string.Equals(argument, "+connect_lobby", StringComparison.Ordinal))
            {
                expectLobby = true;
                continue;
            }
            if (expectLobby && ulong.TryParse(argument, NumberStyles.None,
                CultureInfo.InvariantCulture, out var lobbyId))
                inbox.Queue(lobbyId);
            expectLobby = false;
        }
        return inbox;
    }
}
#endif
