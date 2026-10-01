#if !DEMO_BUILD && !RECORDING_BUILD
using System;
using System.Collections.Generic;
using Steamworks;

namespace LuckyDogRise.Rooms;

/// <summary>Thin Steamworks.NET binding; it never initializes Steam or runs a callback pump.</summary>
public sealed class SteamRoomTransport : ISteamRoomTransport
{
    private readonly SteamworksRuntime _runtime;
    private readonly Callback<LobbyDataUpdate_t> _dataChanged;
    private readonly Callback<LobbyChatUpdate_t> _membersChanged;
    private readonly Callback<LobbyChatMsg_t> _chatReceived;
    private readonly Callback<PersonaStateChange_t> _personaChanged;
    private readonly Callback<SteamServersDisconnected_t> _disconnected;
    private bool _disposed;
    public ulong LocalSteamId { get; }
    public event Action<ulong> LobbyChanged = delegate { };
    public event Action<ulong, ulong> MemberDeparted = delegate { };
    public event Action<ulong, ulong, byte[]> ChatReceived = delegate { };
    public long ServerTime => CanUseApi ? SteamUtils.GetServerRealTime() : 0;

    public SteamRoomTransport(SteamworksRuntime runtime)
    {
        _runtime = runtime;
        LocalSteamId = runtime.SteamId;
        _chatReceived = Callback<LobbyChatMsg_t>.Create(data =>
        {
            if (!CanUseApi || data.m_eChatEntryType != (byte)EChatEntryType.k_EChatEntryTypeChatMsg) return;
            // Chat entry indices are valid only inside this callback. Copy now;
            // never retain an index or fetch lobby history on join.
            var buffer = new byte[4096];
            int length = SteamMatchmaking.GetLobbyChatEntry(new CSteamID(data.m_ulSteamIDLobby),
                (int)data.m_iChatID, out var sender, buffer, buffer.Length, out var kind);
            if (length <= 0 || length > SteamRoomProtocol.MaxChatBytes
                || kind != EChatEntryType.k_EChatEntryTypeChatMsg || sender.m_SteamID != data.m_ulSteamIDUser) return;
            ChatReceived(data.m_ulSteamIDLobby, sender.m_SteamID, buffer.AsSpan(0, length).ToArray());
        });
        _dataChanged = Callback<LobbyDataUpdate_t>.Create(data =>
        {
            if (data.m_bSuccess != 0 && CanUseApi) LobbyChanged(data.m_ulSteamIDLobby);
        });
        _membersChanged = Callback<LobbyChatUpdate_t>.Create(data =>
        {
            if (!CanUseApi) return;
            const EChatMemberStateChange departing = EChatMemberStateChange.k_EChatMemberStateChangeLeft
                | EChatMemberStateChange.k_EChatMemberStateChangeDisconnected
                | EChatMemberStateChange.k_EChatMemberStateChangeKicked
                | EChatMemberStateChange.k_EChatMemberStateChangeBanned;
            if (((EChatMemberStateChange)data.m_rgfChatMemberStateChange & departing) != 0)
                MemberDeparted(data.m_ulSteamIDLobby, data.m_ulSteamIDUserChanged);
            LobbyChanged(data.m_ulSteamIDLobby);
        });
        _personaChanged = Callback<PersonaStateChange_t>.Create(_ =>
        {
            if (CanUseApi) LobbyChanged(0);
        });
        _disconnected = Callback<SteamServersDisconnected_t>.Create(_ => LobbyChanged(0));
    }

    private bool CanUseApi => !_disposed && _runtime.IsInitialized
        && _runtime.TryGetCurrentSteamId(out var actual) && actual == LocalSteamId;

    public bool IsAvailable
    {
        get
        {
            try { return CanUseApi && SteamUser.BLoggedOn(); }
            catch { return false; }
        }
    }

    private void RequireAvailable()
    {
        if (!IsAvailable) throw new InvalidOperationException("The Steam room session is unavailable.");
    }

    public IDisposable Search(Action<SteamRoomSearchResult> completed)
    {
        RequireAvailable();
        SteamMatchmaking.AddRequestLobbyListStringFilter(SteamRoomProtocol.ProtocolKey,
            SteamRoomProtocol.Version, ELobbyComparison.k_ELobbyComparisonEqual);
        SteamMatchmaking.AddRequestLobbyListDistanceFilter(ELobbyDistanceFilter.k_ELobbyDistanceFilterWorldwide);
        SteamMatchmaking.AddRequestLobbyListResultCountFilter(50);
        var handle = SteamMatchmaking.RequestLobbyList();
        if (handle == SteamAPICall_t.Invalid)
        {
            completed(new SteamRoomSearchResult(RoomFailure.Unavailable, []));
            return null;
        }
        var call = CallResult<LobbyMatchList_t>.Create((result, ioFailure) =>
        {
            if (ioFailure || !IsAvailable)
            {
                completed(new SteamRoomSearchResult(RoomFailure.Unavailable, []));
                return;
            }
            var ids = new List<ulong>();
            for (var index = 0; index < Math.Min(result.m_nLobbiesMatching, 50u); index++)
                ids.Add(SteamMatchmaking.GetLobbyByIndex(index).m_SteamID);
            completed(new SteamRoomSearchResult(RoomFailure.None, ids.ToArray()));
        });
        call.Set(handle);
        return call;
    }

    public IDisposable Create(Action<SteamRoomJoinResult> completed)
    {
        RequireAvailable();
        var handle = SteamMatchmaking.CreateLobby(ELobbyType.k_ELobbyTypePublic, RoomRules.Capacity);
        if (handle == SteamAPICall_t.Invalid)
        {
            completed(new SteamRoomJoinResult(RoomFailure.Unavailable, 0));
            return null;
        }
        var call = CallResult<LobbyCreated_t>.Create((result, ioFailure) =>
            completed(new SteamRoomJoinResult(!ioFailure && result.m_eResult == EResult.k_EResultOK
                ? RoomFailure.None : RoomFailure.Unavailable, result.m_ulSteamIDLobby)));
        call.Set(handle);
        return call;
    }

    public IDisposable Join(ulong lobbyId, Action<SteamRoomJoinResult> completed)
    {
        RequireAvailable();
        var id = new CSteamID(lobbyId);
        if (!id.IsValid() || !id.IsLobby())
        {
            completed(new SteamRoomJoinResult(RoomFailure.NotFound, 0));
            return null;
        }
        var handle = SteamMatchmaking.JoinLobby(id);
        if (handle == SteamAPICall_t.Invalid)
        {
            completed(new SteamRoomJoinResult(RoomFailure.Unavailable, 0));
            return null;
        }
        var call = CallResult<LobbyEnter_t>.Create((result, ioFailure) =>
        {
            var response = (EChatRoomEnterResponse)result.m_EChatRoomEnterResponse;
            var failure = ioFailure ? RoomFailure.Unavailable : response switch
            {
                EChatRoomEnterResponse.k_EChatRoomEnterResponseSuccess => RoomFailure.None,
                EChatRoomEnterResponse.k_EChatRoomEnterResponseFull => RoomFailure.Full,
                EChatRoomEnterResponse.k_EChatRoomEnterResponseDoesntExist => RoomFailure.NotFound,
                _ => RoomFailure.Unavailable
            };
            completed(new SteamRoomJoinResult(failure, result.m_ulSteamIDLobby));
        });
        call.Set(handle);
        return call;
    }

    public SteamRoomData ReadLobby(ulong lobbyId, bool includeMembers)
    {
        RequireAvailable();
        var lobby = new CSteamID(lobbyId);
        if (!lobby.IsValid() || !lobby.IsLobby()) return null;
        var count = SteamMatchmaking.GetNumLobbyMembers(lobby);
        var members = new List<SteamRoomMemberData>();
        if (includeMembers && count is >= 1 and <= RoomRules.Capacity)
        {
            for (var index = 0; index < count; index++)
            {
                var member = SteamMatchmaking.GetLobbyMemberByIndex(lobby, index);
                SteamFriends.RequestUserInformation(member, true);
                members.Add(new SteamRoomMemberData(member.m_SteamID, SteamFriends.GetFriendPersonaName(member),
                    SteamMatchmaking.GetLobbyMemberData(lobby, member, SteamRoomProtocol.AppearanceKey),
                    SteamMatchmaking.GetLobbyMemberData(lobby, member, SteamRoomProtocol.ActivityKey),
                    SteamMatchmaking.GetLobbyMemberData(lobby, member, SteamRoomProtocol.ChatSessionKey)));
            }
        }
        return new SteamRoomData(lobbyId, SteamMatchmaking.GetLobbyData(lobby, SteamRoomProtocol.ProtocolKey),
            SteamMatchmaking.GetLobbyData(lobby, SteamRoomProtocol.NameKey),
            SteamMatchmaking.GetLobbyData(lobby, SteamRoomProtocol.GameKey),
            SteamMatchmaking.GetLobbyOwner(lobby).m_SteamID, count,
            SteamMatchmaking.GetLobbyMemberLimit(lobby), members.ToArray());
    }

    public bool InitializeLobby(ulong lobbyId, string name)
    {
        RequireAvailable();
        var lobby = new CSteamID(lobbyId);
        return SteamMatchmaking.SetLobbyData(lobby, SteamRoomProtocol.NameKey, name)
            && SteamMatchmaking.SetLobbyData(lobby, SteamRoomProtocol.GameKey, "social")
            && SteamMatchmaking.SetLobbyData(lobby, SteamRoomProtocol.ProtocolKey, SteamRoomProtocol.Version);
    }

    public bool SetGame(ulong lobbyId, string game)
    {
        RequireAvailable();
        return SteamMatchmaking.SetLobbyData(new CSteamID(lobbyId), SteamRoomProtocol.GameKey, game);
    }

    public void SetAppearance(ulong lobbyId, string appearance)
    {
        RequireAvailable();
        SteamMatchmaking.SetLobbyMemberData(new CSteamID(lobbyId), SteamRoomProtocol.AppearanceKey, appearance);
    }

    public void Leave(ulong lobbyId)
    {
        // Leave is safe offline, but must never act under a replacement Steam identity.
        if (CanUseApi && lobbyId != 0) SteamMatchmaking.LeaveLobby(new CSteamID(lobbyId));
    }

    public void SetActivity(ulong lobbyId, string activity)
    {
        RequireAvailable();
        SteamMatchmaking.SetLobbyMemberData(new CSteamID(lobbyId), SteamRoomProtocol.ActivityKey, activity);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _chatReceived.Dispose();
        _disconnected.Dispose();
        _personaChanged.Dispose();
        _membersChanged.Dispose();
        _dataChanged.Dispose();
    }

    public void SetChatSession(ulong lobbyId, string session)
    {
        RequireAvailable();
        SteamMatchmaking.SetLobbyMemberData(new CSteamID(lobbyId), SteamRoomProtocol.ChatSessionKey, session);
    }

    public bool SendChat(ulong lobbyId, byte[] message)
    {
        RequireAvailable();
        return message.Length <= SteamRoomProtocol.MaxChatBytes
            && SteamMatchmaking.SendLobbyChatMsg(new CSteamID(lobbyId), message, message.Length);
    }
}
#endif
