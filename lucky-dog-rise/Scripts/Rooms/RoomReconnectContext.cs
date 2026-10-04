using System;
using System.Diagnostics;

namespace LuckyDogRise.Rooms;

/// <summary>
/// Process-local intent to return to one interrupted membership. It survives a
/// Steam session replacement, but is never persisted or shared between accounts.
/// </summary>
public sealed class RoomReconnectContext
{
    public const double LifetimeSeconds = 180;
    public const int MaximumAttempts = 3;
    private readonly Func<double> _clock;
    private long _source;
    private long _generation;
    private ulong _steamId;
    private uint _appId;
    private ulong _lobbyId;
    private double _expiresAt;
    private double _attemptExpiresAt;
    private double _retryAt;
    private int _attempts;

    public bool IsPending { get; private set; }
    public bool IsAttempting { get; private set; }
    public string StatusKey { get; private set; } = "";
    public RoomFailure LastFailure { get; private set; }
    public int Attempts => _attempts;

    public RoomReconnectContext(Func<double> clock = null)
        => _clock = clock ?? (() => (double)Stopwatch.GetTimestamp() / Stopwatch.Frequency);

    // A new service invalidates old service handles without dropping a pending
    // same-account target. Late teardown from the old service cannot cancel it.
    public long Bind(ulong steamId, uint appId)
    {
        if (_steamId != 0 && (_steamId != steamId || _appId != appId)) Clear();
        _steamId = steamId;
        _appId = appId;
        if (IsAttempting) FinishAttempt(RoomFailure.Unavailable);
        return ++_source;
    }

    public bool IsSourceCurrent(long source) => source == _source && source != 0;

    public void Interrupt(long source, ulong lobbyId)
    {
        if (!IsSourceCurrent(source) || _steamId == 0 || lobbyId == 0) return;
        Tick();
        if (IsPending) return; // Repeated disconnect callbacks do not restart the budget.
        _generation++;
        _lobbyId = lobbyId;
        _expiresAt = _clock() + LifetimeSeconds;
        _retryAt = _clock();
        _attempts = 0;
        IsPending = true;
        IsAttempting = false;
        LastFailure = RoomFailure.None;
        StatusKey = "Rooms_ReconnectWaiting";
    }

    public bool TryBeginAttempt(long source, out ulong lobbyId, out long generation)
    {
        lobbyId = 0;
        generation = 0;
        Tick();
        if (!IsSourceCurrent(source) || !IsPending || IsAttempting || _clock() < _retryAt) return false;
        if (_attempts >= MaximumAttempts) { Stop(RoomFailure.Unavailable); return false; }
        IsAttempting = true;
        _attempts++;
        _attemptExpiresAt = _clock() + RoomRules.RequestTimeout;
        StatusKey = "Rooms_ReconnectJoining";
        generation = ++_generation;
        lobbyId = _lobbyId;
        return true;
    }

    public bool IsAttemptCurrent(long source, long generation)
        => IsSourceCurrent(source) && IsPending && IsAttempting
            && generation != 0 && generation == _generation
            && _clock() < _expiresAt && _clock() < _attemptExpiresAt;

    public void CompleteAttempt(long source, long generation, RoomFailure failure)
    {
        if (!IsSourceCurrent(source) || !IsPending || !IsAttempting || generation != _generation) return;
        Tick();
        if (!IsPending || !IsAttempting) return;
        if (failure == RoomFailure.None)
        {
            IsPending = IsAttempting = false;
            _lobbyId = 0;
            LastFailure = RoomFailure.None;
            StatusKey = "Rooms_ReconnectRestored";
            return;
        }
        FinishAttempt(failure);
    }

    private void FinishAttempt(RoomFailure failure)
    {
        IsAttempting = false;
        LastFailure = failure;
        if (failure != RoomFailure.Unavailable || _attempts >= MaximumAttempts || _clock() >= _expiresAt)
        { Stop(failure); return; }
        _retryAt = _clock() + (_attempts <= 1 ? 5 : 15);
        StatusKey = "Rooms_ReconnectWaiting";
    }

    public void Tick()
    {
        if (!IsPending) return;
        if (_clock() >= _expiresAt) Stop(RoomFailure.Unavailable);
        else if (IsAttempting && _clock() >= _attemptExpiresAt) FinishAttempt(RoomFailure.Unavailable);
    }

    public void Cancel()
    {
        bool pending = IsPending;
        Clear();
        StatusKey = pending ? "Rooms_ReconnectCancelled" : "";
    }

    public void Stop(RoomFailure failure = RoomFailure.Unavailable)
    {
        Clear();
        LastFailure = failure;
        StatusKey = "Rooms_ReconnectStopped";
    }

    public void Clear()
    {
        _generation++;
        _lobbyId = 0;
        IsPending = IsAttempting = false;
        StatusKey = "";
        LastFailure = RoomFailure.None;
    }
}
