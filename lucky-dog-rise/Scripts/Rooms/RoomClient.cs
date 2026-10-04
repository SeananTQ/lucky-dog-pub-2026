using System;
using System.Collections.Generic;
using System.Linq;

namespace LuckyDogRise.Rooms;

public sealed class RoomClient : IDisposable
{
    private IRoomService _service;
    private readonly Dictionary<int, RoomChat> _bubbles = new();
    private readonly Dictionary<int, long> _chatSequences = new();
    private readonly Dictionary<int, double> _receivedChatAt = new();
    private readonly Dictionary<int, (long Presence, long Sequence, double Until)> _activity = new();
    private double _inputActiveUntil;
    private double _nextActivityRenewal;
    private double _roomAgeAtReceipt;
    private double _roomClockReceipt;
    public long ActivitySequence { get; private set; }
    public bool TongueActive { get; private set; }
    public int Id { get; }
    public long CompanionTime => _service.CompanionTime;
    public string Name { get; }
    public int SkinId { get; private set; }
    public int HeadwearId { get; private set; }
    public int Reaction { get; private set; } = 1001;
    public string JoinedCode { get; private set; } = "";
    public long Session { get; private set; }
    public long LayoutSession { get; private set; }
#if !DEMO_BUILD && !RECORDING_BUILD
    public RoomArrivalState Arrival { get; private set; }
    public bool IsLocalArrival => Arrival != null;
    private long _arrivalRevision;
    private long _arrivalChatSequence;
    private double _arrivalLastChatAt = double.NegativeInfinity;
    private RoomSnapshot _arrivalCommittedView;
#else
    public bool IsLocalArrival => false;
#endif
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
    public bool ChatAllowed => View != null && View.GameId == "social";
    public double RoomAgeSeconds => View?.CreatedAt == null ? 0 : Math.Max(0, _roomAgeAtReceipt + _now - _roomClockReceipt);
    public double GameChangeSecondsRemaining => View?.GameChange is { } change ? Math.Max(0, change.ExpiresAt - _now) : 0;
    public bool CanConfirmGameChange => !IsBusy && View?.OwnerId == Id
        && View.GameChange is { HasMajority: true } && GameChangeSecondsRemaining > 0;

    public bool Create(string name, bool joiningFromDirectory = false)
        => BeginRequest(RoomOperation.Create, name, joiningFromDirectory);
    public bool Join(string code) => BeginRequest(RoomOperation.Join, code);
    internal bool JoinForRecovery(string code, long generation) => BeginRequest(RoomOperation.Join, code, recoveryGeneration: generation);
    public bool Search() => BeginRequest(RoomOperation.Search, "");
    public bool FindAndJoin()
    {
#if !DEMO_BUILD && !RECORDING_BUILD
        if (IsLocalArrival) Cancel();
#endif
        if (IsBusy || _disposed || _committing) return false;
        CancelReconnect();
        _joinAfterSearch = true;
        return BeginRequest(RoomOperation.Search, "");
    }
    public string SetGame(string gameId) => "Rooms_GameChangeRequired";
    public string ProposeGameChange(string gameId) => CanChangeGame() ? _service.ProposeGameChange(this, gameId) : "Rooms_GameChangeUnavailable";
    public string RespondGameChange(string proposalId, bool accept) => CanChangeGame() ? _service.RespondGameChange(this, proposalId, accept) : "Rooms_GameChangeUnavailable";
    public string ConfirmGameChange(string proposalId) => CanChangeGame() ? _service.ConfirmGameChange(this, proposalId) : "Rooms_GameChangeUnavailable";
    public string CancelGameChange(string proposalId) => CanChangeGame() ? _service.CancelGameChange(this, proposalId) : "Rooms_GameChangeUnavailable";
    private bool CanChangeGame() => !_disposed && !_committing && !IsBusy && !IsLocalArrival && View != null;
    public string SetAccess(RoomAccess access) => _disposed || _committing || IsBusy || IsLocalArrival
        ? "Rooms_AccessUnavailable" : _service.SetAccess(this, access);
    public string SetName(string name) => _disposed || _committing || IsBusy || IsLocalArrival
        ? "Rooms_NameUnavailable" : _service.SetName(this, name);
    public string SendChat(string text)
    {
        if (_disposed) return "Rooms_ChatUnavailable";
        if (View != null && !ChatAllowed) return "Rooms_ChatDisabled";
#if !DEMO_BUILD && !RECORDING_BUILD
        if (IsLocalArrival) return SendArrivalChat(text);
#endif
        return _service.SendChat(this, text);
    }
    public string Kick(int memberId, long presence) => _disposed || _committing || IsLocalArrival
        ? "Rooms_KickUnavailable" : _service.Kick(this, memberId, presence);

    private bool BeginRequest(RoomOperation operation, string value, bool joiningFromDirectory = false, long recoveryGeneration = 0, bool arrivalCreation = false)
    {
#if !DEMO_BUILD && !RECORDING_BUILD
        if (IsLocalArrival && !arrivalCreation && operation is RoomOperation.Create or RoomOperation.Join) Cancel();
#endif
        if (IsBusy || _disposed || _committing) return false;
        if (recoveryGeneration == 0 && operation is RoomOperation.Create or RoomOperation.Join) CancelReconnect();
        Operation = operation;
        RequestState = RoomRequestState.Pending;
        Failure = RoomFailure.None;
        _request = new RoomRequest(++_nextRequestId, operation, value ?? "", recoveryGeneration);
#if !DEMO_BUILD && !RECORDING_BUILD
        if (arrivalCreation) _request = _request with { Arrival = Arrival };
#endif
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
        bool recovering = _request.RecoveryGeneration != 0;
#if !DEMO_BUILD && !RECORDING_BUILD
        var requestedArrival = _request.Arrival;
        bool arrivalRequest = requestedArrival != null;
#else
        const bool arrivalRequest = false;
#endif
        _directoryCreateRequestId = 0;
        _joinAfterSearch = false;
        _request = null;
        Failure = result.Failure;
        RequestState = Failure == RoomFailure.None ? RoomRequestState.Succeeded : RoomRequestState.Failed;
#if !DEMO_BUILD && !RECORDING_BUILD
        if (arrivalRequest && Arrival is { Committed: false } && Failure != RoomFailure.None)
            ScheduleArrivalRetry(Failure);
#endif
        if (Operation == RoomOperation.Search && Failure == RoomFailure.None)
        {
            Listings = (RoomListing[])(result.Rooms ?? Array.Empty<RoomListing>()).Clone();
            if (autoJoin)
            {
                var room = Listings.FirstOrDefault(r => !r.IsFull && r.Code != JoinedCode);
                if (room != null) { Join(room.Code); return true; }
                Failure = RoomFailure.NoMatchingRoom;
                RequestState = RoomRequestState.Failed;
            }
        }
        if (!arrivalRequest && (Operation == RoomOperation.Join || autoJoin || directoryJoin) && RequestState == RoomRequestState.Failed
            && (!recovering || (_service as IRoomReconnectService)?.ReconnectContext.IsPending != true))
            JoinFailed?.Invoke(Failure, false);
        Changed?.Invoke();
        return true;
    }

    private void CancelReconnect() => (_service as IRoomReconnectService)?.CancelReconnect(this);
    public void Cancel()
    {
        bool wasCommitting = _committing;
        _committing = true;
        try
        {
            CancelReconnect();
            CancelPending(RoomRequestState.Cancelled);
#if !DEMO_BUILD && !RECORDING_BUILD
            if (IsLocalArrival)
            {
                Arrival = null;
                _service.Release(this);
                if (JoinedCode.Length > 0 || View != null) BeginSession("");
            }
#endif
        }
        finally { _committing = wasCommitting; }
        if (!wasCommitting && !_disposed) Changed?.Invoke();
    }
    internal void CancelRecoveryRequest(long id)
    {
        if (_request?.Id == id && _request.RecoveryGeneration != 0) CancelPending(RoomRequestState.Cancelled);
    }
    private void CancelPending(RoomRequestState state)
    {
        if (_request == null) return;
        bool joining = Operation == RoomOperation.Join || _joinAfterSearch || _directoryCreateRequestId == _request.Id;
        bool recovering = _request.RecoveryGeneration != 0;
#if !DEMO_BUILD && !RECORDING_BUILD
        var requestedArrival = _request.Arrival;
        bool arrivalRequest = requestedArrival != null;
#else
        const bool arrivalRequest = false;
#endif
        var id = _request.Id;
        _directoryCreateRequestId = 0;
        _request = null; // Invalidate before invoking a possibly reentrant adapter.
        _joinAfterSearch = false;
        RequestState = state;
        Failure = RoomFailure.None;
        _service.Cancel(this, id);
#if !DEMO_BUILD && !RECORDING_BUILD
        if (state == RoomRequestState.TimedOut && Arrival is { Committed: false }
            && ReferenceEquals(Arrival, requestedArrival))
            ScheduleArrivalRetry(RoomFailure.Unavailable);
#endif
        // Cancellation adapters can reenter; an old timeout must not overwrite
        // a newer attempt or an explicit leave from that callback.
        if (_disposed || _nextRequestId != id || RequestState != state) return;
        if (joining && state == RoomRequestState.TimedOut && !recovering && !arrivalRequest) JoinFailed?.Invoke(RoomFailure.None, true);
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
        CancelReconnect();
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
#if !DEMO_BUILD && !RECORDING_BUILD
        AdvanceArrival();
#endif
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
        _service.Release(this);
    }

    public RoomClient(IRoomService service, int id, string name, int skinId)
        => (_service, Id, Name, SkinId) = (service, id, name, skinId);

#if !DEMO_BUILD && !RECORDING_BUILD
    public bool BeginArrival(string name, RoomCompanionPlan plan, int hostMemberId)
    {
        if (_disposed || _committing || !RoomRules.TryNormalizeName(name, out name)
            || plan == null || plan.RemainingCount != RoomCompanionPlan.Count
            || !plan.MembersAt(plan.StartedAt).Any(m => m.Id == hostMemberId)) return false;
        _committing = true;
        try
        {
            Cancel();
            _service.Leave(this);
            BeginSession($"@arrival:{Session + 1}");
            Arrival = new RoomArrivalState(name, plan, hostMemberId);
            Operation = RoomOperation.None;
            RequestState = RoomRequestState.Idle;
            Failure = RoomFailure.None;
            _arrivalRevision = _arrivalChatSequence = 0;
            _arrivalLastChatAt = double.NegativeInfinity;
            PublishArrival();
        }
        finally { _committing = false; }
        Changed?.Invoke();
        return !_disposed && Arrival != null;
    }

    public void MarkArrivalPresented()
    {
        if (Arrival == null || Arrival.Presented) return;
        Arrival.Presented = true;
        Arrival.PresentedAt = _now;
    }

    public bool RetryArrival()
    {
        if (_disposed || _committing || IsBusy || Arrival?.Phase != RoomArrivalPhase.Failed
            || !CanPrepareArrival) return false;
        return StartArrivalRequest();
    }

    public bool RebindArrivalService(IRoomService service)
    {
        if (_disposed || _committing || IsBusy || Arrival == null || Arrival.Committed || service == null) return false;
        if (ReferenceEquals(_service, service)) return true;
        _service.DetachLocalArrival(this);
        _service = service;
        // A replacement service can use another monotonic epoch. Preserve the
        // observed duration, but reset message timing instead of comparing clocks.
        double clockShift = service.Now - _now;
        _now += clockShift;
        Arrival.PresentedAt = _now - Arrival.Elapsed;
        if (!double.IsNaN(Arrival.FirstFailureAt)) Arrival.FirstFailureAt += clockShift;
        Arrival.RetryAt = _now;
        Arrival.RetrySecondsRemaining = 0;
        _arrivalLastChatAt = double.NegativeInfinity;
        _bubbles.Clear(); _chatSequences.Clear(); _receivedChatAt.Clear();
        _activity.Clear();
        _inputActiveUntil = _nextActivityRenewal = 0;
        TongueActive = false;
        PublishArrival();
        return true;
    }

    internal void FailArrival(RoomFailure failure)
    {
        if (Arrival == null || Arrival.Committed) return;
        bool alreadyFailed = Arrival.Phase == RoomArrivalPhase.Failed && Arrival.Failure == failure;
        if (!alreadyFailed) ScheduleArrivalRetry(failure);
        bool availabilityChanged = UpdateArrivalConnectionWait();
        Failure = failure;
        RequestState = RoomRequestState.Failed;
        if (!alreadyFailed || availabilityChanged) Changed?.Invoke();
    }

    private bool CanPrepareArrival => _service is not SteamRoomService steam || steam.CanPrepareArrival;

    private void ScheduleArrivalRetry(RoomFailure failure)
    {
        var arrival = Arrival;
        if (arrival == null || arrival.Committed) return;
        arrival.Phase = RoomArrivalPhase.Failed;
        arrival.Failure = failure;
        arrival.DepartureProgress = 0;
        double failedAt = Math.Max(_now, _service.Now);
        if (double.IsNaN(arrival.FirstFailureAt)) arrival.FirstFailureAt = failedAt;
        if (arrival.FailureCount < int.MaxValue) arrival.FailureCount++;
        double delay = arrival.FailureCount switch { 1 => 5, 2 => 15, 3 => 30, _ => 60 };
        arrival.RetryAt = failedAt + delay;
        arrival.RetrySecondsRemaining = delay;
        UpdateArrivalConnectionWait();
    }

    private bool UpdateArrivalConnectionWait()
    {
        if (Arrival == null || Arrival.Committed) return false;
        bool unavailable = _service is SteamRoomService steam && !steam.IsAvailable;
        // An outage before the first visible frame must not trap presentation
        // behind a wait page: let the recovered page show the cast first.
        bool waiting = !Arrival.Presented ? unavailable : Arrival.WaitingForConnection || unavailable
            || !double.IsNaN(Arrival.FirstFailureAt) && _now - Arrival.FirstFailureAt >= 10;
        if (waiting == Arrival.WaitingForConnection) return false;
        Arrival.WaitingForConnection = waiting;
        return true;
    }

    private bool StartArrivalRequest()
    {
        var arrival = Arrival;
        if (arrival == null || arrival.Committed || IsBusy || !CanPrepareArrival) return false;
        arrival.Phase = RoomArrivalPhase.Creating;
        arrival.Failure = RoomFailure.None;
        arrival.DepartureProgress = 0;
        arrival.RetrySecondsRemaining = 0;
        return BeginRequest(RoomOperation.Create, arrival.Name, joiningFromDirectory: true, arrivalCreation: true);
    }

    private void AdvanceArrival()
    {
        var arrival = Arrival;
        if (arrival == null) return;
        arrival.Elapsed = arrival.Presented ? Math.Max(0, _now - arrival.PresentedAt) : 0;
        if (arrival.Committed)
        {
            arrival.DepartureProgress = Math.Clamp((_now - arrival.DepartureAt) / RoomRules.ArrivalDepartureSeconds, 0, 1);
            if (arrival.DepartureProgress >= 1 && _arrivalCommittedView != null)
            {
                var snapshot = _arrivalCommittedView;
                Arrival = null;
                _arrivalCommittedView = null;
                // The synthetic revision has its own counter, unrelated to Steam's.
                View = null;
                Receive(snapshot);
                return;
            }
        }
        else
        {
            bool changed = UpdateArrivalConnectionWait();
            arrival.RetrySecondsRemaining = arrival.Phase == RoomArrivalPhase.Failed ? Math.Max(0, arrival.RetryAt - _now) : 0;
            if (arrival.Presented && arrival.Elapsed >= RoomRules.ArrivalObserveSeconds
                && (arrival.Phase == RoomArrivalPhase.Waiting || arrival.Phase == RoomArrivalPhase.Failed && _now >= arrival.RetryAt))
                StartArrivalRequest();
            if (changed && !_committing) Changed?.Invoke();
        }
        if (!ReferenceEquals(arrival, Arrival)) return;
        if (Math.Floor(arrival.Elapsed) > arrival.LastAppearanceAt)
        {
            arrival.LastAppearanceAt = Math.Floor(arrival.Elapsed);
            PublishArrival();
        }
    }

    internal bool PrepareArrival(long requestId, bool committed = false)
    {
        if (!IsRequestCurrent(requestId) || Arrival == null || !ReferenceEquals(_request.Arrival, Arrival)) return false;
        Arrival.Committed = committed;
        Arrival.Phase = RoomArrivalPhase.Departing;
        Arrival.DepartureProgress = 0;
        Arrival.DepartureAt = Math.Max(_now, _service.Now);
        Arrival.Failure = RoomFailure.None;
        Arrival.FirstFailureAt = double.NaN;
        Arrival.WaitingForConnection = false;
        Arrival.RetrySecondsRemaining = 0;
        if (!_committing) Changed?.Invoke();
        return IsRequestCurrent(requestId) && Arrival?.Phase == RoomArrivalPhase.Departing;
    }

    internal void AdvanceArrivalDeparture(long requestId, double progress)
    {
        if (IsRequestCurrent(requestId) && Arrival?.Phase == RoomArrivalPhase.Departing)
            Arrival.DepartureProgress = Math.Clamp(progress, 0, 1);
    }

    private void PublishArrival()
    {
        var arrival = Arrival;
        if (arrival == null) return;
        var self = new RoomMember(Id, Name, SkinId, HeadwearId, Reaction, Session, ActivitySequence, TongueActive);
        var members = new[] { self }.Concat(arrival.Plan.MembersAt(arrival.Plan.StartedAt + (long)arrival.Elapsed)).ToArray();
        ApplySnapshot(new RoomSnapshot(JoinedCode, arrival.Name, "social", arrival.HostMemberId,
            ++_arrivalRevision, members, RoomAccess.InviteOnly));
    }

    private string SendArrivalChat(string text)
    {
        var error = RoomRules.ValidateChat(text);
        if (error.Length > 0) return error;
        if (_now - _arrivalLastChatAt < RoomRules.ChatCooldown) return "Rooms_ChatTooFast";
        string display;
        try { display = _service.FilterLocalChatForDisplay(text.Trim()); }
        catch { return "Rooms_ChatUnavailable"; }
        if (RoomRules.ValidateChat(display).Length > 0) return "Rooms_ChatUnavailable";
        _arrivalLastChatAt = _now;
        Receive(new RoomChat(++_arrivalChatSequence, JoinedCode, Id, Session, display,
            _now + RoomRules.ChatLifetime), _now);
        return "";
    }
#endif

    public void SetAppearance(int skinId, int headwearId, int reaction)
    {
        if (_disposed) return;
        SkinId = skinId;
        HeadwearId = headwearId;
        Reaction = reaction;
#if !DEMO_BUILD && !RECORDING_BUILD
        if (IsLocalArrival)
        {
            PublishArrival();
            if (Arrival?.Committed != true) return;
        }
#endif
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
#if !DEMO_BUILD && !RECORDING_BUILD
        if (IsLocalArrival)
        {
            PublishArrival();
            if (Arrival?.Committed != true) return;
        }
#endif
        _service.UpdateActivity(this);
    }

    public bool IsTongueActive(int memberId) => _activity.TryGetValue(memberId, out var value)
        && value.Until > _now;

    internal void BeginSession(string code, bool preserveLayout = false, bool preserveArrival = false)
    {
        Session++;
        if (!preserveLayout) LayoutSession++;
#if !DEMO_BUILD && !RECORDING_BUILD
        if (!preserveArrival) Arrival = null;
        _arrivalCommittedView = null;
#endif
        JoinedCode = code;
        View = null;
        _roomAgeAtReceipt = _roomClockReceipt = 0;
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
        if (_disposed || snapshot.Code != JoinedCode) return;
#if !DEMO_BUILD && !RECORDING_BUILD
        if (Arrival is { Committed: true })
        {
            if (snapshot.Revision <= (_arrivalCommittedView?.Revision ?? -1)) return;
            _arrivalCommittedView = snapshot with { Members = (RoomMember[])snapshot.Members.Clone(),
                GameChange = snapshot.GameChange is { } change ? change with { Votes = (RoomGameVote[])change.Votes.Clone() } : null };
            if (View == null) PublishArrival();
            return;
        }
#endif
        if (snapshot.Revision <= (View?.Revision ?? -1)) return;
        ApplySnapshot(snapshot);
    }

    private void ApplySnapshot(RoomSnapshot snapshot)
    {
        if (snapshot.CreatedAt.HasValue && snapshot.ClockNow.HasValue)
        {
            var previousAge = View?.CreatedAt == snapshot.CreatedAt ? RoomAgeSeconds : 0;
            _roomAgeAtReceipt = Math.Max(previousAge, Math.Max(0, (double)snapshot.ClockNow.Value - snapshot.CreatedAt.Value));
            _roomClockReceipt = _now;
        }
        View = snapshot with { Members = (RoomMember[])snapshot.Members.Clone(),
            GameChange = snapshot.GameChange is { } change ? change with { Votes = (RoomGameVote[])change.Votes.Clone() } : null };
        if (!ChatAllowed) _bubbles.Clear();
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
        if (_disposed || !ChatAllowed || chat.Code != JoinedCode || chat.ExpiresAt <= now || RoomRules.ValidateChat(chat.Text).Length > 0
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
