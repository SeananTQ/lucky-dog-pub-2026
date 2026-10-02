using System;
using System.Collections.Generic;
using System.Linq;

namespace LuckyDogRise.Rooms;

public sealed class RoomClient : IDisposable
{
    private readonly IRoomService _service;
    private readonly Dictionary<int, RoomChat> _bubbles = new();
    private readonly Dictionary<int, long> _chatSequences = new();
    private readonly Dictionary<int, double> _receivedChatAt = new();
    private readonly Dictionary<int, (long Presence, long Sequence, double Until)> _activity = new();
    private double _inputActiveUntil;
    private double _nextActivityRenewal;
    public long ActivitySequence { get; private set; }
    public bool TongueActive { get; private set; }
    public int Id { get; }
    public string Name { get; }
    public int SkinId { get; private set; }
    public int HeadwearId { get; private set; }
    public int Reaction { get; private set; } = 1001;
    public string JoinedCode { get; private set; } = "";
    public long Session { get; private set; }
    public RoomSnapshot View { get; private set; }
    public float LocalScale { get; set; } = 1;
    public HashSet<int> HiddenMembers { get; } = new();
    public bool ShowNames { get; set; } = true;
    public IReadOnlyDictionary<int, RoomChat> Bubbles => _bubbles;
    public event Action Changed;
    // Local presentation only; never a room chat message or transport payload.
    public event Action<RoomFailure, bool> JoinFailed;
    private long _nextRequestId;
    private RoomRequest _request;
    private double _now;
    private double _deadline;
    private bool _joinAfterSearch;
    private long _directoryCreateRequestId;
    private bool _disposed;
    private bool _committing;
    public bool IsBusy => _request != null;
    public RoomOperation Operation { get; private set; }
    public RoomRequestState RequestState { get; private set; }
    public RoomFailure Failure { get; private set; }
    public RoomListing[] Listings { get; private set; } = Array.Empty<RoomListing>();
    public long ActiveRequestId => _request?.Id ?? 0;

    public bool Create(string name, bool joiningFromDirectory = false)
        => BeginRequest(RoomOperation.Create, name, joiningFromDirectory);
    public bool Join(string code) => BeginRequest(RoomOperation.Join, code);
    public bool Search() => BeginRequest(RoomOperation.Search, "");
    public bool FindAndJoin()
    {
        if (IsBusy || _disposed || _committing) return false;
        _joinAfterSearch = true;
        return BeginRequest(RoomOperation.Search, "");
    }
    public string SetGame(string gameId) => _disposed ? "房间连接已关闭。" : _service.SetGame(this, gameId);
    public string SetAccess(RoomAccess access) => _disposed || _committing || IsBusy
        ? "Rooms_AccessUnavailable" : _service.SetAccess(this, access);
    public string SendChat(string text) => _disposed ? "房间连接已关闭。" : _service.SendChat(this, text);
    public string Kick(int memberId, long presence) => _disposed || _committing
        ? "Rooms_KickUnavailable" : _service.Kick(this, memberId, presence);

    private bool BeginRequest(RoomOperation operation, string value, bool joiningFromDirectory = false)
    {
        if (IsBusy || _disposed || _committing) return false;
        Operation = operation;
        RequestState = RoomRequestState.Pending;
        Failure = RoomFailure.None;
        _request = new RoomRequest(++_nextRequestId, operation, value ?? "");
        // This is presentation intent for one accepted request, not a different
        // transport operation. Busy/reentrant rejected calls cannot overwrite it.
        _directoryCreateRequestId = operation == RoomOperation.Create && joiningFromDirectory ? _request.Id : 0;
        _deadline = _now + RoomRules.RequestTimeout;
        var request = _request;
        Changed?.Invoke();
        if (!IsRequestCurrent(request.Id)) return false;
        try { _service.Request(this, request); }
        catch
        {
            _service.Cancel(this, request.Id);
            TryComplete(request.Id, () => new RoomResult(RoomFailure.Unavailable));
        }
        return true;
    }

    public bool IsRequestCurrent(long id) => !_disposed && _request?.Id == id;

    // Service adapters use this gate before applying a completed operation locally.
    // Cancellation does NOT imply that a remote network operation was cancelled.
    public bool TryComplete(long id, Func<RoomResult> commit)
    {
        if (!IsRequestCurrent(id)) return false;
        RoomResult result;
        _committing = true;
        try { result = commit() ?? new RoomResult(RoomFailure.Unavailable); }
        catch { result = new RoomResult(RoomFailure.Unavailable); }
        finally { _committing = false; }
        if (!IsRequestCurrent(id)) return false;
        var autoJoin = _joinAfterSearch && Operation == RoomOperation.Search;
        bool directoryJoin = _directoryCreateRequestId == id && Operation == RoomOperation.Create;
        _directoryCreateRequestId = 0;
        _joinAfterSearch = false;
        _request = null;
        Failure = result.Failure;
        RequestState = Failure == RoomFailure.None ? RoomRequestState.Succeeded : RoomRequestState.Failed;
        if (Operation == RoomOperation.Search && Failure == RoomFailure.None)
        {
            Listings = (RoomListing[])(result.Rooms ?? Array.Empty<RoomListing>()).Clone();
            if (autoJoin)
            {
                var room = Listings.FirstOrDefault(r => r.Count < r.Capacity && r.Code != JoinedCode);
                if (room != null) { Join(room.Code); return true; }
                Failure = RoomFailure.NoMatchingRoom;
                RequestState = RoomRequestState.Failed;
            }
        }
        if ((Operation == RoomOperation.Join || autoJoin || directoryJoin) && RequestState == RoomRequestState.Failed)
            JoinFailed?.Invoke(Failure, false);
        Changed?.Invoke();
        return true;
    }

    public void Cancel() => CancelPending(RoomRequestState.Cancelled);
    private void CancelPending(RoomRequestState state)
    {
        if (_request == null) return;
        bool joining = Operation == RoomOperation.Join || _joinAfterSearch || _directoryCreateRequestId == _request.Id;
        var id = _request.Id;
        _directoryCreateRequestId = 0;
        _request = null; // Invalidate before invoking a possibly reentrant adapter.
        _joinAfterSearch = false;
        RequestState = state;
        Failure = RoomFailure.None;
        _service.Cancel(this, id);
        // Cancellation adapters can reenter; an old timeout must not overwrite
        // a newer attempt or an explicit leave from that callback.
        if (_disposed || _nextRequestId != id || RequestState != state) return;
        if (joining && state == RoomRequestState.TimedOut) JoinFailed?.Invoke(RoomFailure.None, true);
        if (!_committing) Changed?.Invoke();
    }
    public void Leave()
    {
        if (_disposed || _committing) return;
        _committing = true;
        try
        {
            Cancel();
            _service.Leave(this);
            Operation = RoomOperation.None;
            RequestState = RoomRequestState.Idle;
            Failure = RoomFailure.None;
        }
        finally { _committing = false; }
        Changed?.Invoke();
    }

    // Host removal is terminal for this membership, including any search that
    // would otherwise auto-join when its delayed callback arrives. Notify only
    // after the request/session have been invalidated, while guarding against
    // observers starting another request inside the removal notification.
    internal void RemoveFromRoom(RoomFailure reason)
    {
        if (_disposed) return;
        var wasCommitting = _committing;
        _committing = true;
        try
        {
            var requestId = _request?.Id ?? 0;
            _request = null;
            _joinAfterSearch = false;
            _directoryCreateRequestId = 0;
            if (requestId != 0) _service.Cancel(this, requestId);
            var session = Session;
            _service.Leave(this);
            if (Session == session || JoinedCode.Length > 0 || View != null) BeginSession("");
            Operation = RoomOperation.None;
            RequestState = RoomRequestState.Failed;
            Failure = reason;
            Changed?.Invoke();
        }
        finally { _committing = wasCommitting; }
    }
    public void AdvanceTo(double now)
    {
        if (_disposed) return;
        if (!double.IsFinite(now) || now < _now) throw new ArgumentOutOfRangeException(nameof(now));
        _now = now;
        if (_request != null && now >= _deadline) CancelPending(RoomRequestState.TimedOut);
        if (TongueActive)
        {
            if (now >= _inputActiveUntil) StopInputActivity();
            else if (now >= _nextActivityRenewal) PublishActivity();
        }
        bool expired = false;
        foreach (var (id, activity) in _activity.ToArray())
            if (activity.Until > 0 && now >= activity.Until)
            {
                _activity[id] = (activity.Presence, activity.Sequence, 0);
                expired = true;
            }
        if (expired) Changed?.Invoke();
        ExpireBubbles(now);
    }
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Changed = null;
        JoinFailed = null;
        CancelPending(RoomRequestState.Cancelled);
        _service.Leave(this);
    }

    public RoomClient(IRoomService service, int id, string name, int skinId)
        => (_service, Id, Name, SkinId) = (service, id, name, skinId);

    public void SetAppearance(int skinId, int headwearId, int reaction)
    {
        if (_disposed) return;
        SkinId = skinId;
        HeadwearId = headwearId;
        Reaction = reaction;
        _service.UpdateAppearance(this);
    }

    public void SetReaction(int reaction)
    {
        if (Reaction != reaction) SetAppearance(SkinId, HeadwearId, reaction);
    }

    public void NotifyInputActivity()
    {
        if (_disposed || JoinedCode.Length == 0) return;
        _inputActiveUntil = _now + RoomRules.InputActivityHold;
        if (TongueActive) return;
        TongueActive = true;
        PublishActivity();
    }

    public void StopInputActivity()
    {
        _inputActiveUntil = 0;
        if (_disposed || !TongueActive) return;
        TongueActive = false;
        PublishActivity();
    }

    private void PublishActivity()
    {
        ActivitySequence++;
        _nextActivityRenewal = _now + RoomRules.ActivityRenewal;
        _service.UpdateActivity(this);
    }

    public bool IsTongueActive(int memberId) => _activity.TryGetValue(memberId, out var value)
        && value.Until > _now;

    internal void BeginSession(string code)
    {
        Session++;
        JoinedCode = code;
        View = null;
        TongueActive = false;
        ActivitySequence++;
        _inputActiveUntil = _nextActivityRenewal = 0;
        _activity.Clear();
        _bubbles.Clear();
        _chatSequences.Clear();
        _receivedChatAt.Clear();
        if (!_committing) Changed?.Invoke();
    }

    internal void Receive(RoomSnapshot snapshot)
    {
        if (_disposed || snapshot.Code != JoinedCode || snapshot.Revision <= (View?.Revision ?? -1)) return;
        View = snapshot with { Members = (RoomMember[])snapshot.Members.Clone() };
        foreach (var id in _activity.Keys.ToArray())
            if (!View.Members.Any(m => m.Id == id && m.Presence == _activity[id].Presence)) _activity.Remove(id);
        foreach (var member in View.Members)
        {
            // Re-reading cached Steam metadata or changing a hat must not renew
            // a stale active flag. Only a newer activity sequence grants a lease.
            if (!_activity.TryGetValue(member.Id, out var previous) || member.ActivitySequence > previous.Sequence)
                _activity[member.Id] = (member.Presence, member.ActivitySequence,
                    member.TongueActive ? _now + RoomRules.ActivityLease : 0);
        }
        foreach (var id in _chatSequences.Keys.ToArray())
            if (!View.Members.Any(m => m.Id == id))
            { _bubbles.Remove(id); _chatSequences.Remove(id); _receivedChatAt.Remove(id); }
        foreach (var id in _bubbles.Keys.ToArray())
            if (!View.Members.Any(m => m.Id == id && m.Presence == _bubbles[id].Presence))
            { _bubbles.Remove(id); _chatSequences.Remove(id); _receivedChatAt.Remove(id); }
        if (!_committing && !_disposed) Changed?.Invoke();
    }

    internal void Receive(RoomChat chat, double now)
    {
        if (_disposed || chat.Code != JoinedCode || chat.ExpiresAt <= now || RoomRules.ValidateChat(chat.Text).Length > 0
            || View == null || !View.Members.Any(m => !m.IsCompanion && m.Id == chat.SenderId && m.Presence == chat.Presence)
            || (_chatSequences.TryGetValue(chat.SenderId, out var last) && chat.Id <= last)
            || (_receivedChatAt.TryGetValue(chat.SenderId, out var receivedAt)
                && now - receivedAt < RoomRules.ChatCooldown)) return;
        _chatSequences[chat.SenderId] = chat.Id;
        _receivedChatAt[chat.SenderId] = now;
        _bubbles[chat.SenderId] = chat;
        Changed?.Invoke();
    }

    internal void ExpireBubbles(double now)
    {
        var expired = _bubbles.Where(p => p.Value.ExpiresAt <= now).Select(p => p.Key).ToArray();
        foreach (var id in expired) _bubbles.Remove(id);
        if (expired.Length > 0) Changed?.Invoke();
    }
}
