#if !DEMO_BUILD && !RECORDING_BUILD
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace LuckyDogRise.Rooms;

/// <summary>
/// Steam owns lobby membership. This adapter owns one local client and maps Steam identities
/// to display identities without reading/writing inventory, statistics or local saves.
/// </summary>
public sealed class SteamRoomService : IRoomService, IRoomInviteService, IDisposable
{
    private sealed class Pending(RoomClient client, RoomRequest request)
    {
        public readonly RoomClient Client = client;
        public readonly RoomRequest Request = request;
        public IDisposable Handle;
        public bool Cancelled;
        public double RequestedAt;
        public double StartedAt;
    }

    private readonly ISteamRoomTransport _transport;
    private readonly SteamRoomInviteInbox _invitations;
    private readonly int _defaultSkin;
    private readonly Func<int, bool> _validSkin;
    private readonly Func<int, bool> _validHeadwear;
    private readonly Func<int, bool> _validReaction;
    private readonly Func<double> _now;
    private readonly Dictionary<ulong, (int Id, long Presence)> _identities = new();
    private readonly HashSet<ulong> _banned = new();
    private readonly HashSet<ulong> _blockedLobbies = new();
    private RoomClient _client;
    private Pending _inFlight;
    private Pending _queued;
    private ulong _lobby;
    private ulong _accessOwner;
    private long _nextPresence;
    private long _revision;
    private int _nextMemberId = 2;
    private bool _completing;
    private bool _disposed;
    private bool _suspended;
    private bool _settlementTimedOut;
    private string _lastAppearance = "";
    private string _lastActivity = "";
    private Guid _chatSession;
    private long _chatSequence;
    private long _chatJoinedAt;
    private double _lastChatSentAt = double.NegativeInfinity;
    private readonly Dictionary<ulong, Guid> _chatSessions = new();
    private readonly Dictionary<ulong, double> _receivedChatAt = new();
    public bool IsAvailable => !_disposed && !_suspended && !_settlementTimedOut && _transport.IsAvailable;
    public bool RestartRequired => !_disposed && _settlementTimedOut;
    public double Now => _now();
    public bool HasPendingJoinRequest => IsAvailable && _invitations.HasPending;

    public SteamRoomService(ISteamRoomTransport transport, int defaultSkin,
        Func<int, bool> validSkin, Func<int, bool> validHeadwear, Func<int, bool> validReaction,
        Func<double> now = null, SteamRoomInviteInbox invitations = null)
    {
        _transport = transport;
        _invitations = invitations ?? new SteamRoomInviteInbox();
        _defaultSkin = defaultSkin;
        _validSkin = validSkin;
        _validHeadwear = validHeadwear;
        _validReaction = validReaction;
        _now = now ?? (() => (double)Stopwatch.GetTimestamp() / Stopwatch.Frequency);
        _transport.LobbyChanged += OnLobbyChanged;
        _transport.MemberDeparted += OnMemberDeparted;
        _transport.ChatReceived += OnChatReceived;
        _transport.JoinRequested += OnJoinRequested;
    }

    public string InviteFriends(RoomClient client)
    {
        if (!IsAvailable || !ReferenceEquals(client, _client) || _lobby == 0 || client.IsBusy)
            return "Rooms_InviteUnavailable";
        try
        {
            var data = _transport.ReadLobby(_lobby, true);
            if (!Compatible(data) || data.Members == null
                || !data.Members.Any(member => member.SteamId == _transport.LocalSteamId)
                || !SteamRoomProtocol.TryDecodeBannedMembers(data.BannedMembers, out var bans)
                || bans.Contains(_transport.LocalSteamId)) return "Rooms_InviteUnavailable";
            return _transport.OpenInviteDialog(_lobby) ? "ok" : "Rooms_InviteOverlayUnavailable";
        }
        catch { return "Rooms_InviteUnavailable"; }
    }

    private void OnJoinRequested(ulong lobbyId)
    {
        if (!_disposed && !_suspended) _invitations.Queue(lobbyId);
    }

    public bool TryTakeJoinRequest(out string roomCode)
    {
        roomCode = "";
        if (!IsAvailable || !_invitations.TryTake(out var lobbyId)) return false;
        // Duplicated acceptance callbacks must not restart a pending join. A later fresh
        // acceptance after failure is still allowed, and an invite never bypasses join validation.
        var pending = _queued ?? _inFlight;
        if (pending != null && !pending.Cancelled && pending.Request.Operation == RoomOperation.Join
            && pending.Client.IsRequestCurrent(pending.Request.Id)
            && SteamRoomProtocol.TryDecode(pending.Request.Value, out var joining) && joining == lobbyId)
            return false;
        if (lobbyId == _lobby && _client?.IsBusy != true) return false;
        roomCode = SteamRoomProtocol.Encode(lobbyId);
        return true;
    }

    public void Request(RoomClient client, RoomRequest request)
    {
        if (!IsAvailable || client.Id != 1 || _client != null && !ReferenceEquals(_client, client)
            && (_client.JoinedCode.Length > 0 || _client.IsBusy))
        {
            client.TryComplete(request.Id, () => new RoomResult(RoomFailure.Unavailable));
            return;
        }
        _client = client;
        if (request.Operation == RoomOperation.Create && !SteamRoomProtocol.IsNameValid(request.Value))
        {
            client.TryComplete(request.Id, () => new RoomResult(RoomFailure.InvalidName));
            return;
        }
        if (request.Operation == RoomOperation.Join)
        {
            if (!SteamRoomProtocol.TryDecode(request.Value, out var lobby))
            {
                client.TryComplete(request.Id, () => new RoomResult(RoomFailure.NotFound));
                return;
            }
            if (_blockedLobbies.Contains(lobby))
            {
                client.TryComplete(request.Id, () => new RoomResult(RoomFailure.Banned));
                return;
            }
            if (lobby == _lobby)
            {
                client.TryComplete(request.Id, () => RoomResult.Success);
                return;
            }
        }
        // A cancelled Steam join can still succeed. Never launch a new membership operation
        // until that callback has arrived and its old membership has been cleaned up.
        _queued = new Pending(client, request) { RequestedAt = _now() };
        StartQueued();
    }

    public void Cancel(RoomClient client, long requestId)
    {
        if (_queued?.Client == client && _queued.Request.Id == requestId) _queued = null;
        if (_inFlight?.Client == client && _inFlight.Request.Id == requestId) _inFlight.Cancelled = true;
        // Keep the native CallResult registered: cancellation is local, not remote.
    }

    private void StartQueued()
    {
        if (_disposed || _completing || _inFlight != null || _queued == null) return;
        var pending = _queued;
        _queued = null;
        if (!pending.Client.IsRequestCurrent(pending.Request.Id)) return;
        // The shared callback pump may run before the page advances its client clock.
        // Do not launch a queued operation whose original UI deadline has elapsed.
        if (_now() - pending.RequestedAt >= RoomRules.RequestTimeout) return;
        if (!IsAvailable)
        {
            pending.Client.TryComplete(pending.Request.Id, () => new RoomResult(RoomFailure.Unavailable));
            return;
        }
        _inFlight = pending;
        pending.StartedAt = _now();
        try
        {
            var handle = pending.Request.Operation switch
            {
                RoomOperation.Search => _transport.Search(result => CompleteSearch(pending, result)),
                RoomOperation.Create => _transport.Create(result => CompleteMembership(pending, result)),
                RoomOperation.Join => _transport.Join(Decode(pending.Request.Value), result => CompleteMembership(pending, result)),
                _ => throw new InvalidOperationException("Unsupported room operation.")
            };
            // A fake transport or immediate native failure may complete synchronously.
            if (_inFlight == pending) pending.Handle = handle;
            else handle?.Dispose();
        }
        catch
        {
            CompleteMembership(pending, new SteamRoomJoinResult(RoomFailure.Unavailable, 0));
        }
    }

    private static ulong Decode(string code)
        => SteamRoomProtocol.TryDecode(code, out var lobby) ? lobby : 0;

    private bool CanCommit(Pending pending) => !_disposed && !pending.Cancelled && IsAvailable
        && ReferenceEquals(_client, pending.Client) && pending.Client.IsRequestCurrent(pending.Request.Id)
        && _now() - pending.RequestedAt < RoomRules.RequestTimeout;

    private void CompleteSearch(Pending pending, SteamRoomSearchResult result)
    {
        if (_inFlight != pending) return;
        _completing = true;
        try
        {
            if (!CanCommit(pending)) return;
            var failure = result.Failure;
            var listings = new List<RoomListing>();
            if (failure == RoomFailure.None)
            {
                foreach (var id in (result.LobbyIds ?? []).Distinct().Take(50))
                {
                    var data = _transport.ReadLobby(id, false);
                    if (!Compatible(data) || data.Access != RoomAccess.Public || _blockedLobbies.Contains(id)
                        || !SteamRoomProtocol.TryDecodeBannedMembers(data.BannedMembers, out var bans)
                        || bans.Contains(_transport.LocalSteamId)) continue;
                    listings.Add(new RoomListing(SteamRoomProtocol.Encode(data.LobbyId), data.Name,
                        SteamRoomProtocol.IsGameValid(data.Game) ? data.Game : "social", data.Count, data.Capacity));
                }
            }
            pending.Client.TryComplete(pending.Request.Id, () => new RoomResult(failure, listings.ToArray()));
        }
        catch { pending.Client.TryComplete(pending.Request.Id, () => new RoomResult(RoomFailure.Unavailable)); }
        finally { Finish(pending); }
    }

    private void CompleteMembership(Pending pending, SteamRoomJoinResult result)
    {
        if (_inFlight != pending) return;
        _completing = true;
        var accepted = false;
        try
        {
            if (!CanCommit(pending)) return;
            if (result.Failure != RoomFailure.None || result.LobbyId == 0)
            {
                pending.Client.TryComplete(pending.Request.Id, () => new RoomResult(
                    result.Failure == RoomFailure.None ? RoomFailure.Unavailable : result.Failure));
                return;
            }
            if (pending.Request.Operation == RoomOperation.Join && Decode(pending.Request.Value) != result.LobbyId)
                throw new InvalidOperationException("Steam returned a different lobby.");
            if (pending.Request.Operation == RoomOperation.Create
                && !_transport.InitializeLobby(result.LobbyId, pending.Request.Value.Trim()))
                throw new InvalidOperationException("Steam lobby metadata could not be initialized.");
            var data = _transport.ReadLobby(result.LobbyId, true);
            if (!Compatible(data) || data.Members == null || data.Members.Length > RoomRules.Capacity
                || !data.Members.Any(member => member.SteamId == _transport.LocalSteamId))
            {
                pending.Client.TryComplete(pending.Request.Id, () => new RoomResult(RoomFailure.NotFound));
                return;
            }
            if (!SteamRoomProtocol.TryDecodeBannedMembers(data.BannedMembers, out var bans))
            {
                pending.Client.TryComplete(pending.Request.Id, () => new RoomResult(RoomFailure.Unavailable));
                return;
            }
            if (bans.Contains(_transport.LocalSteamId) || _blockedLobbies.Contains(result.LobbyId))
            {
                _blockedLobbies.Add(result.LobbyId);
                pending.Client.TryComplete(pending.Request.Id, () => new RoomResult(RoomFailure.Banned));
                return;
            }
            // The stored policy survives the former host. Reconcile native
            // admission once when joining as owner, before committing our view.
            if (data.OwnerId == _transport.LocalSteamId
                && !_transport.SetAccess(result.LobbyId, data.Access))
            {
                pending.Client.TryComplete(pending.Request.Id, () => new RoomResult(RoomFailure.Unavailable));
                return;
            }
            // Publish local appearance before switching our committed view. A failed join
            // leaves the old room and its window layout untouched.
            var appearance = AppearanceOf(pending.Client);
            _transport.SetAppearance(result.LobbyId, appearance);
            var activity = SteamRoomProtocol.EncodeActivity(pending.Client.ActivitySequence + 1, false);
            _transport.SetActivity(result.LobbyId, activity);
            var chatSession = Guid.NewGuid();
            _transport.SetChatSession(result.LobbyId, chatSession.ToString("N"));
            pending.Client.TryComplete(pending.Request.Id, () =>
            {
                var previous = _lobby;
                _lobby = result.LobbyId;
                _accessOwner = data.OwnerId;
                _identities.Clear();
                _banned.Clear();
                _banned.UnionWith(bans);
                _nextMemberId = 2;
                _lastAppearance = appearance;
                _lastActivity = activity;
                _chatSession = chatSession;
                _chatJoinedAt = _transport.ServerTime;
                _chatSequence = 0;
                _chatSessions.Clear();
                _receivedChatAt.Clear();
                pending.Client.BeginSession(SteamRoomProtocol.Encode(_lobby));
                Publish(data);
                accepted = true;
                if (previous != 0 && previous != _lobby) SafeLeave(previous);
                return RoomResult.Success;
            });
        }
        catch { pending.Client.TryComplete(pending.Request.Id, () => new RoomResult(RoomFailure.Unavailable)); }
        finally
        {
            if (!accepted && result.Failure == RoomFailure.None && result.LobbyId != 0 && result.LobbyId != _lobby)
                SafeLeave(result.LobbyId);
            Finish(pending);
        }
    }

    private void Finish(Pending pending)
    {
        pending.Handle?.Dispose();
        pending.Handle = null;
        if (_inFlight == pending) _inFlight = null;
        _settlementTimedOut = false;
        _completing = false;
        StartQueued();
    }

    private static bool Compatible(SteamRoomData data) => data != null && data.LobbyId != 0
        && data.Protocol == SteamRoomProtocol.Version && SteamRoomProtocol.IsNameValid(data.Name)
        && SteamRoomProtocol.IsAccessValid(data.Access)
        && data.Capacity == RoomRules.Capacity && data.Count is >= 1 and <= RoomRules.Capacity;

    private string AppearanceOf(RoomClient client) => SteamRoomProtocol.EncodeAppearance(
        _validSkin(client.SkinId) ? client.SkinId : _defaultSkin,
        client.HeadwearId == 0 || _validHeadwear(client.HeadwearId) ? client.HeadwearId : 0,
        _validReaction(client.Reaction) ? client.Reaction : 1001);

    public void UpdateAppearance(RoomClient client)
    {
        if (!IsAvailable || !ReferenceEquals(client, _client) || _lobby == 0) return;
        try
        {
            var appearance = AppearanceOf(client);
            if (appearance == _lastAppearance) return;
            _transport.SetAppearance(_lobby, appearance);
            _lastAppearance = appearance;
            OnLobbyChanged(_lobby);
        }
        catch { Disconnect(); }
    }

    public string SetGame(RoomClient client, string gameId)
    {
        if (!IsAvailable || !ReferenceEquals(client, _client) || _lobby == 0) return "请先进入房间。";
        if (!SteamRoomProtocol.IsGameValid(gameId)) return "玩法标识无效。";
        try
        {
            if (_transport.ReadLobby(_lobby, false)?.OwnerId != _transport.LocalSteamId)
                return "只有房主可以修改房间玩法。";
            if (!_transport.SetGame(_lobby, gameId)) return "房间服务暂不可用。";
            OnLobbyChanged(_lobby);
            return "";
        }
        catch { return "房间服务暂不可用。"; }
    }

    public string SetAccess(RoomClient client, RoomAccess access)
    {
        if (!IsAvailable || !ReferenceEquals(client, _client) || _lobby == 0 || client.IsBusy
            || !SteamRoomProtocol.IsAccessValid(access)) return "Rooms_AccessUnavailable";
        try
        {
            var lobby = _lobby;
            var data = _transport.ReadLobby(lobby, false);
            if (data?.OwnerId != _transport.LocalSteamId) return "Rooms_AccessNotOwner";
            if (!Compatible(data)) return "Rooms_AccessUnavailable";
            if (data.Access == access) return "ok";
            if (!_transport.SetAccess(lobby, access)) return "Rooms_AccessUpdateFailed";
            OnLobbyChanged(lobby);
            return _lobby == lobby ? "ok" : "Rooms_AccessUnavailable";
        }
        catch
        {
            // A transport exception can mean both the update and its rollback
            // failed. Stop this membership instead of asserting a stale policy.
            // Existing members remain and Steam can appoint the next owner.
            Disconnect();
            return "Rooms_AccessUnavailable";
        }
    }

    public string Kick(RoomClient client, int memberId, long presence)
    {
        if (!IsAvailable || !ReferenceEquals(client, _client) || _lobby == 0)
            return "Rooms_KickUnavailable";
        try
        {
            var lobby = _lobby;
            var data = _transport.ReadLobby(lobby, true);
            if (data?.OwnerId != _transport.LocalSteamId) return "Rooms_KickNotOwner";
            if (!Compatible(data) || data.Members == null
                || !SteamRoomProtocol.TryDecodeBannedMembers(data.BannedMembers, out var bans))
                return "Rooms_KickUnavailable";
            var identity = _identities.FirstOrDefault(pair => pair.Value.Id == memberId
                && pair.Value.Presence == presence);
            var target = data.Members.FirstOrDefault(member => member.SteamId == identity.Key);
            if (target == null || target.SteamId == _transport.LocalSteamId || target.SteamId == 0)
                return "Rooms_KickInvalidTarget";
            Guid.TryParseExact(target.ChatSession, "N", out var targetSession);
            if (_chatSessions.TryGetValue(target.SteamId, out var previous) && previous != targetSession)
                return "Rooms_KickInvalidTarget";
            bans.UnionWith(_banned);
            if (bans.Contains(target.SteamId)) return ""; // Idempotent: no repeated network writes.
            if (bans.Count >= SteamRoomProtocol.MaxBannedMembers) return "Rooms_KickListFull";
            bans.Add(target.SteamId);
            // Write the durable room-scoped rule before sending a best-effort prompt.
            // Metadata callbacks also enforce removal, so lost/reordered prompts are harmless.
            if (!_transport.SetBannedMembers(lobby, SteamRoomProtocol.EncodeBannedMembers(bans)))
                return "Rooms_KickUnavailable";
            if (_lobby != lobby) return "";
            _banned.UnionWith(bans);
            if (targetSession != Guid.Empty)
            {
                try { _transport.SendChat(lobby, SteamRoomProtocol.EncodeKick(target.SteamId, targetSession)); }
                catch { /* The accepted metadata update remains authoritative. */ }
            }
            OnLobbyChanged(lobby);
            return "";
        }
        catch { return "Rooms_KickUnavailable"; }
    }

    private static string ActivityOf(RoomClient client)
        => SteamRoomProtocol.EncodeActivity(client.ActivitySequence, client.TongueActive);

    public void UpdateActivity(RoomClient client)
    {
        if (!IsAvailable || !ReferenceEquals(client, _client) || _lobby == 0) return;
        try
        {
            var activity = ActivityOf(client);
            if (activity == _lastActivity) return;
            _transport.SetActivity(_lobby, activity);
            _lastActivity = activity;
            OnLobbyChanged(_lobby);
        }
        catch { Disconnect(); }
    }

    public string SendChat(RoomClient client, string text)
    {
        if (!IsAvailable || !ReferenceEquals(client, _client) || _lobby == 0) return "Rooms_ChatUnavailable";
        var error = RoomRules.ValidateChat(text);
        if (error.Length > 0) return error;
        double now = _now();
        if (now - _lastChatSentAt < RoomRules.ChatCooldown) return "Rooms_ChatTooFast";
        var member = client.View?.Members.FirstOrDefault(m => m.Id == client.Id);
        if (member == null) return "Rooms_ChatUnavailable";
        try
        {
            long sentAt = _transport.ServerTime;
            if (sentAt <= 0) return "Rooms_ChatUnavailable";
            text = text.Trim();
            var displayText = _transport.FilterChatForDisplay(_transport.LocalSteamId, text);
            if (RoomRules.ValidateChat(displayText).Length > 0) return "Rooms_ChatUnavailable";
            long sequence = ++_chatSequence;
            _lastChatSentAt = now;
            if (!_transport.SendChat(_lobby, SteamRoomProtocol.EncodeChat(_chatSession, sequence, sentAt, text)))
                return "Rooms_ChatUnavailable";
            client.Receive(new RoomChat(sequence, client.JoinedCode, member.Id, member.Presence,
                displayText, now + RoomRules.ChatLifetime), now);
            return "";
        }
        catch { return "Rooms_ChatUnavailable"; }
    }

    private void OnChatReceived(ulong lobby, ulong sender, byte[] bytes)
    {
        if (!IsAvailable || _lobby == 0 || lobby != _lobby || sender == _transport.LocalSteamId) return;
        if (SteamRoomProtocol.TryDecodeKick(bytes, out var target, out var membership))
        {
            if (target != _transport.LocalSteamId || membership != _chatSession) return;
            try
            {
                var data = _transport.ReadLobby(lobby, true);
                // Never trust an owner id carried in a packet or our last rendered snapshot.
                if (data?.OwnerId == sender && Compatible(data)
                    && SteamRoomProtocol.TryDecodeBannedMembers(data.BannedMembers, out var bans)
                    && bans.Contains(target)) ApplyBans(bans);
            }
            catch { /* A metadata notification can still complete removal. */ }
            return;
        }
        if (_banned.Contains(sender)
            || !SteamRoomProtocol.TryDecodeChat(bytes, out var session, out var sequence, out var sentAt, out var text)) return;
        // Both clocks are supplied by Steam; local wall-clock settings are irrelevant.
        var age = _transport.ServerTime - sentAt;
        if (age < -2 || age >= RoomRules.ChatLifetime || sentAt < _chatJoinedAt) return;
        if (!_identities.TryGetValue(sender, out var identity)
            || !_chatSessions.TryGetValue(sender, out var current) || current != session) return;
        double now = _now();
        if (_receivedChatAt.TryGetValue(sender, out var last) && now - last < RoomRules.ChatCooldown) return;
        _receivedChatAt[sender] = now;
        string displayText;
        try { displayText = _transport.FilterChatForDisplay(sender, text); }
        catch
        {
            // A filtering failure must neither reveal raw text nor escape through
            // the shared Steam callback pump and tear down unrelated platform services.
            return;
        }
        if (RoomRules.ValidateChat(displayText).Length > 0) return;
        _client.Receive(new RoomChat(sequence, _client.JoinedCode, identity.Id, identity.Presence,
            displayText, now + RoomRules.ChatLifetime - Math.Max(0, age)), now);
    }

    private void OnMemberDeparted(ulong lobby, ulong member)
    {
        if (lobby == _lobby) { _identities.Remove(member); _chatSessions.Remove(member); _receivedChatAt.Remove(member); }
    }

    private void OnLobbyChanged(ulong lobby)
    {
        if (_disposed || _lobby == 0 || lobby != 0 && lobby != _lobby) return;
        if (!IsAvailable) { Disconnect(); return; }
        try
        {
            var data = _transport.ReadLobby(_lobby, true);
            if (!Compatible(data) || data.Members == null || data.Members.Length > RoomRules.Capacity
                || !data.Members.Any(m => m.SteamId == _transport.LocalSteamId))
            {
                Disconnect();
                return;
            }
            if (!SteamRoomProtocol.TryDecodeBannedMembers(data.BannedMembers, out var bans))
            {
                Disconnect();
                return;
            }
            if (!ApplyBans(bans)) return;
            if (data.OwnerId != _accessOwner)
            {
                if (data.OwnerId == _transport.LocalSteamId && !_transport.SetAccess(_lobby, data.Access))
                {
                    Disconnect();
                    return;
                }
                _accessOwner = data.OwnerId;
            }
            Publish(data);
        }
        catch { Disconnect(); }
    }

    private bool ApplyBans(HashSet<ulong> bans)
    {
        _banned.UnionWith(bans);
        if (!_banned.Contains(_transport.LocalSteamId)) return true;
        _blockedLobbies.Add(_lobby);
        _client.RemoveFromRoom(RoomFailure.Removed);
        return false;
    }

    private void Publish(SteamRoomData data)
    {
        var present = data.Members.Where(m => !_banned.Contains(m.SteamId)).Select(m => m.SteamId).ToHashSet();
        foreach (var stale in _identities.Keys.Where(id => !present.Contains(id)).ToArray()) _identities.Remove(stale);
        foreach (var stale in _chatSessions.Keys.Where(id => !present.Contains(id)).ToArray()) _chatSessions.Remove(stale);
        foreach (var stale in _receivedChatAt.Keys.Where(id => !present.Contains(id)).ToArray()) _receivedChatAt.Remove(stale);
        var members = new List<RoomMember>();
        foreach (var raw in data.Members.DistinctBy(m => m.SteamId).Take(RoomRules.Capacity))
        {
            if (raw.SteamId == 0 || _banned.Contains(raw.SteamId)) continue;
            if (Guid.TryParseExact(raw.ChatSession, "N", out var chatToken) && chatToken != Guid.Empty)
            {
                if (_chatSessions.TryGetValue(raw.SteamId, out var previousToken) && previousToken != chatToken
                    && raw.SteamId != _transport.LocalSteamId) _identities.Remove(raw.SteamId);
                _chatSessions[raw.SteamId] = chatToken;
            }
            else _chatSessions.Remove(raw.SteamId);
            if (!_identities.TryGetValue(raw.SteamId, out var identity))
            {
                identity = (raw.SteamId == _transport.LocalSteamId ? 1 : _nextMemberId++, ++_nextPresence);
                _identities.Add(raw.SteamId, identity);
            }
            // A newly joined member may not have published metadata yet. Invalid/untrusted
            // item ids are resolved to local safe defaults; they never become owned items.
            var appearance = raw.SteamId == _transport.LocalSteamId ? AppearanceOf(_client) : raw.Appearance;
            var valid = SteamRoomProtocol.TryDecodeAppearance(appearance, out var skin, out var hat, out var reaction);
            var activity = raw.SteamId == _transport.LocalSteamId ? ActivityOf(_client) : raw.Activity;
            bool validActivity = SteamRoomProtocol.TryDecodeActivity(activity, out var sequence, out var active);
            members.Add(new RoomMember(identity.Id, SteamRoomProtocol.SafePersonaName(raw.Name),
                valid && _validSkin(skin) ? skin : _defaultSkin,
                valid && (hat == 0 || _validHeadwear(hat)) ? hat : 0,
                valid && _validReaction(reaction) ? reaction : 1001, identity.Presence,
                validActivity ? sequence : 0, validActivity && active));
        }
        var owner = _identities.TryGetValue(data.OwnerId, out var ownerIdentity) ? ownerIdentity.Id : 0;
        _client.Receive(new RoomSnapshot(SteamRoomProtocol.Encode(_lobby), data.Name,
            SteamRoomProtocol.IsGameValid(data.Game) ? data.Game : "social", owner, ++_revision,
            members.OrderBy(m => m.Id).ToArray(), data.Access));
    }

    public void Leave(RoomClient client)
    {
        if (!ReferenceEquals(client, _client)) return;
        // The page also disposes its old client when a platform session becomes
        // unavailable. That teardown must not erase the shared recovery inbox.
        if (IsAvailable) _invitations.Clear();
        LeaveRoom(client);
    }

    private void LeaveRoom(RoomClient client)
    {
        if (!ReferenceEquals(client, _client)) return;
        if (_queued?.Client == client) _queued = null;
        if (_inFlight?.Client == client) _inFlight.Cancelled = true;
        var old = _lobby;
        _lobby = 0;
        _accessOwner = 0;
        _identities.Clear();
        _banned.Clear();
        _lastAppearance = "";
        _lastActivity = "";
        _chatSession = Guid.Empty;
        _chatSessions.Clear();
        _receivedChatAt.Clear();
        if (client.JoinedCode.Length > 0) client.BeginSession("");
        if (old != 0) SafeLeave(old);
    }

    // Called from the existing platform service before and after its Steam callback pump.
    public void Tick()
    {
        // A callback can be delayed beyond the UI's 15-second timeout. After a bounded
        // grace period, fail the room service closed instead of trapping new requests in
        // an endless queue. Keep the callback for late membership cleanup; it can restore
        // availability after settlement. If Steam never settles, a platform restart is
        // needed, and we must not reset the shared inventory session speculatively.
        if (_inFlight != null && _now() - _inFlight.StartedAt >= 45) _settlementTimedOut = true;
        if (!_disposed && !IsAvailable) Disconnect();
    }

    public void SetSuspended(bool suspended)
    {
        _suspended = suspended;
        if (suspended) _invitations.Clear();
        if (suspended && !_disposed) Disconnect();
    }

    private void Disconnect()
    {
        if (_queued != null)
            _queued.Client.TryComplete(_queued.Request.Id, () => new RoomResult(RoomFailure.Unavailable));
        _queued = null;
        if (_inFlight != null)
        {
            _inFlight.Cancelled = true;
            _inFlight.Client.TryComplete(_inFlight.Request.Id, () => new RoomResult(RoomFailure.Unavailable));
        }
        // A freshly accepted invitation may outlive a transient platform disconnect.
        // Recovery owns the inbox; unlike an explicit Leave/kick, teardown does not discard it.
        if (_client != null) LeaveRoom(_client);
    }

    private void SafeLeave(ulong lobby)
    {
        try { _transport.Leave(lobby); }
        catch { /* Local teardown must remain safe if Steam has already shut down. */ }
    }

    public void Dispose()
    {
        if (_disposed) return;
        Disconnect();
        _disposed = true;
        _transport.LobbyChanged -= OnLobbyChanged;
        _transport.MemberDeparted -= OnMemberDeparted;
        _transport.ChatReceived -= OnChatReceived;
        _transport.JoinRequested -= OnJoinRequested;
        _inFlight?.Handle?.Dispose();
        _inFlight = null;
        _transport.Dispose();
    }
}
#endif
