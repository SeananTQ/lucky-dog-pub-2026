#if !DEMO_BUILD && !RECORDING_BUILD
#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;

namespace LuckyDogRise.Rooms.Poker;

// One endpoint per client. Mock and Steam exchange the same bounded serialized messages.
// Only the owner constructs a match. No GameData, account storage or scene references belong here.
public sealed class PokerNetworkSession : IDisposable
{
    private readonly IPokerLink _link;
    private readonly Dictionary<string, long> _received = new();
    private readonly Dictionary<string, (double Time, int Count)> _rates = new();
    private readonly Dictionary<string, long> _finished = new();
    private PokerRoomContext? _room;
    private PokerParticipant[] _players = Array.Empty<PokerParticipant>();
    private MischiefPokerMatch? _match;
    private PokerTableStatus _status;
    private Guid _table;
    private long _sequence, _version, _pendingAction, _pendingReadySequence;
    private bool? _requestedReady;
    private double _now, _matchClock, _dealUntil, _botAt, _publishAt, _helloAt, _receivedAt, _settledAt;
    private double _companionReadyAt;
    private string _notice = "";
    private bool _dirty, _disposed;
    public PokerNetworkState? State { get; private set; }
    public PokerRoomContext? Context => _room;
    public bool IsOwner => _room != null && _room.OwnerKey == _room.LocalKey;
    public bool Connected => _room != null && (IsOwner || State != null && _now - _receivedAt <= 10);
    public bool IsParticipant => State?.Players.Any(p => p.Key == _room?.LocalKey && p.Playing) == true;
    public bool IsReady => _requestedReady ?? State?.Players.Any(p => p.Key == _room?.LocalKey && p.Ready) == true;
    public bool ActionPending => _pendingAction != 0;
    public bool CanStart => Connected && IsOwner && State?.Status == PokerTableStatus.Preparing
        && State.Players.Count(p => p.Ready) >= 2 && IsReady;
    public PokerError LastError { get; private set; }
    public string Notice => !Connected && _room != null ? "connecting" : State?.Notice ?? "";
    public PokerPlayerView? DisplayView
    {
        get
        {
            var view = PokerNetworkProtocol.ForDisplay(State, _room);
            if (view == null) return null;
            double remaining = view.Public.SecondsRemaining;
            if (!IsOwner && State!.DealRemaining == 0 && view.Public.Phase == PokerPhase.Acting)
                remaining = Math.Max(0, remaining - (_now - _receivedAt));
            return view with { Public = view.Public with { SecondsRemaining = remaining } };
        }
    }

    public PokerNetworkSession(IPokerLink link)
    {
        _link = link;
        _link.Received += Receive;
    }
    public bool Propose() => IsOwner && State?.Status is PokerTableStatus.Idle or PokerTableStatus.Interrupted && Request("prepare");
    public bool SetReady(bool ready) => !IsParticipant && (IsReady == ready || Request("ready", ready));
    public bool Start() => CanStart && Request("start");
    public bool Stop() => IsOwner && Request("stop");
    public bool FinishPresentation()
        => State?.Hand is { } h && Request("finished", hand: h.Public.HandNumber);
    public bool Act(PokerAction action, int slot = -1)
    {
        if (!Connected || ActionPending || !IsParticipant || State?.Hand is not { } v || State.DealRemaining > 0
            || v.Public.Phase != PokerPhase.Acting || v.Public.ActivePlayerId != v.PlayerId) return false;
        return Request("action", command: new(v.Public.SessionId, v.Public.HandNumber,
            v.Public.Revision, v.PlayerId, action, slot));
    }

    public void Tick(double delta)
    {
        if (_disposed || !double.IsFinite(delta) || delta < 0) return;
        _now += delta;
        var room = _link.Context;
        string Signature(PokerRoomContext? r) => r == null ? "" : r.Room + "|" + r.OwnerKey + "|"
            + r.Members.FirstOrDefault(m => m.Key == r.OwnerKey)?.Ticket + "|"
            + r.Members.FirstOrDefault(m => m.Key == r.LocalKey)?.Ticket;
        if (Signature(room) != Signature(_room))
        {
            bool wasPlaying = State?.Status == PokerTableStatus.Playing;
            _players = Array.Empty<PokerParticipant>(); _match = null; _table = Guid.Empty;
            _received.Clear(); _rates.Clear(); _finished.Clear(); State = null; _pendingAction = 0; _requestedReady = null;
            _status = wasPlaying ? PokerTableStatus.Interrupted : PokerTableStatus.Idle;
            _notice = wasPlaying ? "membership_changed" : "";
            _helloAt = _publishAt = 0; _dirty = true;
        }
        _room = room;
        if (room == null) return;
        if (IsOwner) SyncMembers();
        _link.Poll();
        if (!IsOwner)
        {
            if (_now >= _helloAt) { _helloAt = _now + 2; Request("hello"); }
            return;
        }
        if (_status == PokerTableStatus.Preparing && _now >= _companionReadyAt)
        {
            if (_players.Any(p => p.Companion && !p.Ready))
            { _players = _players.Select(p => p.Companion ? p with { Ready = true } : p).ToArray(); _dirty = true; }
        }
        if (_match != null && _status == PokerTableStatus.Playing)
        {
            var before = _match.GetPublicView();
            if (_now >= _dealUntil && before.Phase == PokerPhase.Acting)
            {
                // The authority's initial dealing interval is independent of any viewer's animation or window visibility.
                _matchClock += Math.Max(0, Math.Min(delta, _now - _dealUntil));
                _match.AdvanceTo(_matchClock);
                var v = _match.GetPublicView();
                if (v.ActivePlayerId is int id && _players.Any(p => p.Id == id && p.Companion) && _now >= _botAt)
                {
                    _botAt = _now + 1.2;
                    var action = Random.Shared.Next(4) == 0 ? PokerAction.EndTurn : PokerAction.Flip;
                    _match.Act(new(v.SessionId, v.HandNumber, v.Revision, id, action, Random.Shared.Next(5)), _matchClock);
                }
            }
            if (_match.GetPublicView().Phase == PokerPhase.AwaitingDraw)
            {
                _match.ResolveHand(); _settledAt = _now; _finished.Clear();
            }
            var current = _match.GetPublicView();
            _dirty |= current.Revision != before.Revision;
            if (current.Phase == PokerPhase.Settled && current.Seats.All(s => s.Chips >= _match.Rules.Ante))
            {
                bool done = _players.Where(p => p.Playing && !p.Companion)
                    .All(p => _finished.GetValueOrDefault(p.Key) == current.HandNumber);
                if (done) BeginHand(admitWaiting: true);
                else if (_now - _settledAt > 45) Interrupt("presentation_timeout");
            }
        }
        if (_dirty || _now >= _publishAt) Publish();
    }

    private void SyncMembers()
    {
        if (_room == null) return;
        bool Gone(PokerParticipant p) => !_room.Members.Any(m => m.Key == p.Key && m.Ticket == p.Ticket);
        foreach (var removed in _players.Where(Gone))
        { _received.Remove(removed.Key); _rates.Remove(removed.Key); _finished.Remove(removed.Key); }
        if (_players.Any(p => p.Playing && Gone(p))) Interrupt("member_left");
        // A newly elected host still needs a roster so it can explicitly propose a fresh table.
        var list = _players.Where(p => !Gone(p)).Select(p => _status == PokerTableStatus.Interrupted
            ? p with { Playing = false, Ready = false } : p).ToList();
        foreach (var m in _room.Members.OrderBy(m => m.Key == _room.OwnerKey ? 0 : 1).ThenBy(m => m.Key, StringComparer.Ordinal))
        {
            if (list.Any(p => p.Key == m.Key) || list.Count == 6) continue;
            int id = Enumerable.Range(1, 6).First(id => list.All(p => p.Id != id));
            list.Add(new(m.Key, m.Ticket, id, false, false, m.Companion));
        }
        if (!_players.SequenceEqual(list)) { _players = list.ToArray(); _dirty = true; }
    }
    private bool Request(string kind, bool ready = false, PokerCommand? command = null, long hand = 0)
    {
        if (_disposed || _room == null) return false;
        var local = _room.Members.FirstOrDefault(m => m.Key == _room.LocalKey);
        var owner = _room.Members.FirstOrDefault(m => m.Key == _room.OwnerKey);
        if (local == null || owner == null) return false;
        var packet = new PokerPacket(PokerNetworkProtocol.Version, _room.Room, local.Ticket, owner.Ticket,
            kind, ++_sequence, State?.Table ?? Guid.Empty, ready, command, hand);
        if (IsOwner) { Handle(local.Key, packet); if (_dirty) Publish(); return true; }
        bool sent = _link.Send(owner.Key, PokerNetworkProtocol.Encode(packet));
        if (sent && kind == "action") _pendingAction = packet.Sequence;
        if (sent && kind == "ready") { _requestedReady = ready; _pendingReadySequence = packet.Sequence; }
        return sent;
    }
    private void Receive(string sender, byte[] bytes)
    {
        if (_disposed || _room == null) return;
        var peer = _room.Members.FirstOrDefault(m => m.Key == sender && !m.Companion);
        var local = _room.Members.FirstOrDefault(m => m.Key == _room.LocalKey);
        var packet = PokerNetworkProtocol.Decode(bytes);
        if (peer == null || local == null || packet == null || packet.Room != _room.Room
            || packet.SenderTicket != peer.Ticket || packet.RecipientTicket != local.Ticket) return;
        if (packet.Kind == "state")
        {
            if (IsOwner || sender != _room.OwnerKey || packet.State is not { } s
                || s.Version <= (State?.Version ?? 0) || !PokerNetworkProtocol.ValidState(s, _room)) return;
            State = s; _receivedAt = _now;
            if (s.AckSequence >= _pendingAction || s.Status != PokerTableStatus.Playing) _pendingAction = 0;
            if (s.AckSequence >= _pendingReadySequence || s.Status is PokerTableStatus.Idle or PokerTableStatus.Interrupted || IsParticipant)
                _requestedReady = null;
            return;
        }
        if (!IsOwner || packet.Sequence <= _received.GetValueOrDefault(sender)) return;
        var rate = _rates.GetValueOrDefault(sender);
        if (_now - rate.Time >= 1) rate = (_now, 0);
        if (rate.Count >= 20) return;
        _rates[sender] = (rate.Time, rate.Count + 1);
        _received[sender] = packet.Sequence;
        Handle(sender, packet);
    }
    private void Handle(string sender, PokerPacket packet)
    {
        if (!IsOwner || _room == null) return;
        var p = _players.FirstOrDefault(p => p.Key == sender);
        if (p == null) return;
        if (packet.Kind == "hello") { _dirty = true; return; }
        if (packet.Kind == "prepare")
        {
            if (sender != _room.OwnerKey || _status is PokerTableStatus.Playing or PokerTableStatus.Preparing) return;
            _table = Guid.NewGuid(); _match = null; _status = PokerTableStatus.Preparing; _notice = "";
            _players = Array.Empty<PokerParticipant>(); SyncMembers();
            _players = _players.Select(x => x with { Ready = x.Key == sender }).ToArray();
            _companionReadyAt = _now + 1; _dirty = true; return;
        }
        if (packet.Table != _table || _table == Guid.Empty) return;
        switch (packet.Kind)
        {
            case "ready" when _status is PokerTableStatus.Preparing or PokerTableStatus.Playing && !p.Playing:
                _players = _players.Select(x => x.Key == sender ? x with { Ready = packet.Ready } : x).ToArray();
                _dirty = true; break;
            case "start" when sender == _room.OwnerKey && _status == PokerTableStatus.Preparing:
                var ready = _players.Where(x => x.Ready).ToArray();
                if (ready.Length < 2 || !ready.Any(x => x.Key == sender)) return;
                _players = _players.Select(x => x with { Playing = x.Ready }).ToArray();
                _match = new(ready.Select(x => x.Id).ToArray(), p.Id);
                _status = PokerTableStatus.Playing; BeginHand(false); break;
            case "action" when p.Playing && _match != null && _status == PokerTableStatus.Playing && _now >= _dealUntil:
                if (packet.Command is not { } c) return;
                // The authenticated transport identity overrides the caller-supplied PlayerId.
                LastError = _match.Act(c with { PlayerId = p.Id }, _matchClock);
                _dirty = true; break;
            case "finished" when p.Playing && _match?.GetPublicView() is { Phase: PokerPhase.Settled } v:
                if (packet.FinishedHand == v.HandNumber) _finished[sender] = v.HandNumber;
                break;
            case "stop" when sender == _room.OwnerKey:
                Interrupt("host_ended"); break;
        }
    }
    private void BeginHand(bool admitWaiting)
    {
        if (_match == null) return;
        var entrants = admitWaiting ? _players.Where(p => p.Ready && !p.Playing).Select(p => p.Id).ToArray() : Array.Empty<int>();
        LastError = _match.StartHand(_matchClock, newPlayers: entrants);
        if (LastError != PokerError.None) { Interrupt("cannot_start"); return; }
        _players = _players.Select(p => entrants.Contains(p.Id) ? p with { Playing = true } : p).ToArray();
        _dealUntil = _now + 3; _botAt = _dealUntil + 1.2; _finished.Clear(); _dirty = true;
    }
    private void Interrupt(string reason)
    {
        _status = PokerTableStatus.Interrupted; _notice = reason; _dirty = true; _match = null;
    }
    private void Publish()
    {
        if (_room == null || !IsOwner) return;
        ++_version; _dirty = false; _publishAt = _now + (_status == PokerTableStatus.Playing ? 0.5 : 2);
        var owner = _room.Members.FirstOrDefault(m => m.Key == _room.LocalKey);
        if (owner == null) return;
        foreach (var peer in _room.Members.Where(m => !m.Companion))
        {
            var p = _players.FirstOrDefault(p => p.Key == peer.Key && p.Ticket == peer.Ticket);
            PokerPlayerView? view = _status == PokerTableStatus.Playing && _match != null
                ? p?.Playing == true ? _match.GetPlayerView(p.Id)
                    : new(_match.GetPublicView(), 0, Array.Empty<int>()) : null;
            var state = new PokerNetworkState(_table, _version, _status, _players.ToArray(), view,
                _status == PokerTableStatus.Playing ? Math.Max(0, _dealUntil - _now) : 0, _notice,
                _received.GetValueOrDefault(peer.Key));
            if (peer.Key == _room.LocalKey) { State = state; _receivedAt = _now; continue; }
            _link.Send(peer.Key, PokerNetworkProtocol.Encode(new(PokerNetworkProtocol.Version, _room.Room,
                owner.Ticket, peer.Ticket, "state", ++_sequence, _table, State: state)));
        }
    }
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true; _link.Received -= Receive; _link.Dispose();
        _match = null; State = null; _room = null;
    }
}
#endif
