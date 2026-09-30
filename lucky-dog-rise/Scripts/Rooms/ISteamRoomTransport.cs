#if !DEMO_BUILD && !RECORDING_BUILD
using System;

namespace LuckyDogRise.Rooms;

public sealed record SteamRoomMemberData(ulong SteamId, string Name, string Appearance, string Activity = "");
public sealed record SteamRoomData(ulong LobbyId, string Protocol, string Name, string Game,
    ulong OwnerId, int Count, int Capacity, SteamRoomMemberData[] Members);
public sealed record SteamRoomJoinResult(RoomFailure Failure, ulong LobbyId);
public sealed record SteamRoomSearchResult(RoomFailure Failure, ulong[] LobbyIds);

/// <summary>
/// All callbacks run on the shared platform callback pump. Disposing a request handle
/// only unregisters its callback; it does NOT cancel a remote create/join operation.
/// </summary>
public interface ISteamRoomTransport : IDisposable
{
    ulong LocalSteamId { get; }
    bool IsAvailable { get; }
    event Action<ulong> LobbyChanged;
    event Action<ulong, ulong> MemberDeparted;
    IDisposable Search(Action<SteamRoomSearchResult> completed);
    IDisposable Create(Action<SteamRoomJoinResult> completed);
    IDisposable Join(ulong lobbyId, Action<SteamRoomJoinResult> completed);
    SteamRoomData ReadLobby(ulong lobbyId, bool includeMembers);
    bool InitializeLobby(ulong lobbyId, string name);
    bool SetGame(ulong lobbyId, string game);
    void SetAppearance(ulong lobbyId, string appearance);
    void SetActivity(ulong lobbyId, string activity);
    void Leave(ulong lobbyId);
}
#endif
