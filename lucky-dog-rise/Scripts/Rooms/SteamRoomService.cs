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
public sealed class SteamRoomService : IRoomService, IDisposable
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
    private readonly int _defaultSkin;
    private readonly Func<int, bool> _validSkin;
    private readonly Func<int, bool> _validHeadwear;
    private readonly Func<int, bool> _validReaction;
    private readonly Func<double> _now;
    private readonly Dictionary<ulong, (int Id, long Presence)> _identities = new();
    private RoomClient _client;
    private Pending _inFlight;
    private Pending _queued;
    private ulong _lobby;
    private long _nextPresence;
    private long _revision;
    private int _nextMemberId = 2;
    private bool _completing;
    private bool _disposed;
    private bool _suspended;
    private bool _settlementTimedOut;
    private string _lastAppearance = "";
    private string _lastActivity = "";
    public bool IsAvailable => !_disposed && !_suspended && !_settlementTimedOut && _transport.IsAvailable;
    public bool RestartRequired => !_disposed && _settlementTimedOut;

    public SteamRoomService(ISteamRoomTransport transport, int defaultSkin,
        Func<int, bool> validSkin, Func<int, bool> validHeadwear, Func<int, bool> validReaction,
        Func<double> now = null)
    {
        _transport = transport;
        _defaultSkin = defaultSkin;
        _validSkin = validSkin;
        _validHeadwear = validHeadwear;
        _validReaction = validReaction;
        _now = now ?? (() => (double)Stopwatch.GetTimestamp() / Stopwatch.Frequency);
        _transport.LobbyChanged += OnLobbyChanged;
        _transport.MemberDeparted += OnMemberDeparted;
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
                    if (!Compatible(data)) continue;
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
            // Publish local appearance before switching our committed view. A failed join
            // leaves the old room and its window layout untouched.
            var appearance = AppearanceOf(pending.Client);
            _transport.SetAppearance(result.LobbyId, appearance);
            var activity = SteamRoomProtocol.EncodeActivity(pending.Client.ActivitySequence + 1, false);
            _transport.SetActivity(result.LobbyId, activity);
            pending.Client.TryComplete(pending.Request.Id, () =>
            {
                var previous = _lobby;
                _lobby = result.LobbyId;
                _identities.Clear();
                _nextMemberId = 2;
                _lastAppearance = appearance;
                _lastActivity = activity;
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

    public string SendChat(RoomClient client, string text) => "Steam 房间聊天尚未开放。";

    private void OnMemberDeparted(ulong lobby, ulong member)
    {
        if (lobby == _lobby) _identities.Remove(member);
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
            Publish(data);
        }
        catch { Disconnect(); }
    }

    private void Publish(SteamRoomData data)
    {
        var present = data.Members.Select(m => m.SteamId).ToHashSet();
        foreach (var stale in _identities.Keys.Where(id => !present.Contains(id)).ToArray()) _identities.Remove(stale);
        var members = new List<RoomMember>();
        foreach (var raw in data.Members.DistinctBy(m => m.SteamId).Take(RoomRules.Capacity))
        {
            if (raw.SteamId == 0) continue;
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
            members.OrderBy(m => m.Id).ToArray()));
    }

    public void Leave(RoomClient client)
    {
        if (!ReferenceEquals(client, _client)) return;
        if (_queued?.Client == client) _queued = null;
        if (_inFlight?.Client == client) _inFlight.Cancelled = true;
        var old = _lobby;
        _lobby = 0;
        _identities.Clear();
        _lastAppearance = "";
        _lastActivity = "";
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
        if (_client != null) Leave(_client);
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
        _inFlight?.Handle?.Dispose();
        _inFlight = null;
        _transport.Dispose();
    }
}
#endif
