#if !DEMO_BUILD && !RECORDING_BUILD
using System;

namespace LuckyDogRise.Rooms;

public sealed record SteamRoomMemberData(ulong SteamId, string Name, string Appearance, string Activity = "", string ChatSession = "", string GameVote = "");
public sealed record SteamRoomData(ulong LobbyId, string Protocol, string Name, string Game,
    ulong OwnerId, int Count, int Capacity, SteamRoomMemberData[] Members, string BannedMembers = "1:",
    RoomAccess Access = RoomAccess.Public, string Companions = "", long? CreatedAt = null, string GameState = "");
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
    long ServerTime { get; }
    event Action<ulong> LobbyChanged;
    event Action<ulong, ulong> MemberDeparted;
    event Action<ulong, ulong, byte[]> ChatReceived;
    event Action<ulong> JoinRequested;
    bool OpenInviteDialog(ulong lobbyId);
    IDisposable Search(Action<SteamRoomSearchResult> completed);
    IDisposable Create(Action<SteamRoomJoinResult> completed);
    IDisposable Join(ulong lobbyId, Action<SteamRoomJoinResult> completed);
    SteamRoomData ReadLobby(ulong lobbyId, bool includeMembers);
    bool InitializeLobby(ulong lobbyId, string name);
    bool SetGame(ulong lobbyId, string game);
    bool SetGameState(ulong lobbyId, string state) => false;
    void SetGameVote(ulong lobbyId, string vote) { }
    bool SetName(ulong lobbyId, string name) => false;
    // False guarantees the previous policy remains intact. Throw if compensation
    // fails and native admission can no longer be confirmed against metadata.
    bool SetAccess(ulong lobbyId, RoomAccess access);
    bool SetCompanions(ulong lobbyId, string companions);
    bool SetBannedMembers(ulong lobbyId, string members);
    void SetAppearance(ulong lobbyId, string appearance);
    void SetActivity(ulong lobbyId, string activity);
    void SetChatSession(ulong lobbyId, string session);
    // Display-only: use the viewer's preferences and the actual author's Steam identity.
    // SendChat must still receive the original text for other viewers to filter themselves.
    string FilterChatForDisplay(ulong senderSteamId, string text);
    bool SendChat(ulong lobbyId, byte[] message);
    void Leave(ulong lobbyId);
}
#endif
