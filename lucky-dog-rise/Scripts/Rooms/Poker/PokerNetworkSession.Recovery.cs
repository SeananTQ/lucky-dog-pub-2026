#if !DEMO_BUILD && !RECORDING_BUILD
#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace LuckyDogRise.Rooms.Poker;

public sealed partial class PokerNetworkSession
{
    public const double PeerTimeoutSeconds = 10;
    public const double AutoplaySeconds = 5;
    public const int IdleTurnLimit = 2;
    public const double MigrationTimeoutSeconds = 10;
    private bool _linkAvailable, _restartRequired;
    private Guid _epoch = Guid.NewGuid(), _migrationId;
    private double _migrationStarted, _migrationRequestAt;
    private PokerRecoverySnapshot? _lastRecovery;
    private readonly Dictionary<string, PokerRecoverySnapshot?> _offers = new();
    private readonly Dictionary<string, double> _lastHeard = new();
    private readonly Dictionary<string, long> _recoveryRequests = new();
    private readonly List<PokerParticipant> _departedPlayers = new();
    private readonly List<PokerBankrollReserve> _reserves = new();
    public bool IsMigrating { get; private set; }
    // Process-local reconnect cache only. Presentation must never inspect the deck or hidden hands.
    public PokerRecoverySnapshot? RecoveryCopy => _lastRecovery;
    public bool IsAutoplay => State?.Players.Any(p => p.Key == _room?.LocalKey && p.Control != PokerControl.Manual) == true;
    public bool CanReclaim => Connected && IsAutoplay && !IsCollecting && State?.Status == PokerTableStatus.Playing;
    public bool Reclaim() => CanReclaim && Request("reclaim", hand: State!.Hand!.Public.HandNumber);
    public bool IsAutoplayForDisplayId(int displayId) => State != null && _room != null
        && State.Players.Concat(State.DepartedPlayers ?? []).Any(p => p.Control != PokerControl.Manual
            && PokerNetworkProtocol.DisplayId(State, _room, p.Id) == displayId);
    private IEnumerable<PokerParticipant> AllParticipants() => _players.Concat(_departedPlayers);
    private void UpdatePlayer(int id, Func<PokerParticipant, PokerParticipant> update)
    {
        _players = _players.Select(p => p.Id == id ? update(p) : p).ToArray(); _dirty = true;
    }
    private void CountIdleTurns(PokerPublicView before, PokerPublicView after)
    {
        foreach (var turn in after.CompletedTurns.Skip(before.CompletedTurns.Count))
        {
            var player = _players.FirstOrDefault(p => p.Id == turn.PlayerId);
            if (player == null || player.Companion || player.Control != PokerControl.Manual) continue;
            bool idle = turn.Reason == PokerTurnEnd.TimedOut && turn.FlipsUsed == 0;
            int count = idle ? player.IdleTurns + 1 : 0;
            UpdatePlayer(player.Id, p => p with { IdleTurns = Math.Min(IdleTurnLimit, count),
                Control = count >= IdleTurnLimit ? PokerControl.Away : PokerControl.Manual,
                ControlSince = count >= IdleTurnLimit ? _matchClock : 0 });
        }
    }
    private void UpdateDisconnectedPlayers()
    {
        foreach (var p in _players.Where(p => !p.Companion && p.Playing && p.Key != _room!.LocalKey
            && (p.Ready || _match?.GetPublicView().ActionOrder.Contains(p.Id) == true)
            && p.Control != PokerControl.Disconnected && _now - _lastHeard.GetValueOrDefault(p.Key, _now) >= PeerTimeoutSeconds).ToArray())
            UpdatePlayer(p.Id, x => x with { Ready = false, Control = PokerControl.Disconnected, ControlSince = _matchClock });
    }
    private void ResetRecovery()
    {
        _lastRecovery = null; _offers.Clear(); _lastHeard.Clear(); _recoveryRequests.Clear(); _departedPlayers.Clear(); _reserves.Clear();
        _epoch = Guid.NewGuid(); IsMigrating = false; _restartRequired = false;
    }
    private PokerRecoverySnapshot CaptureRecovery(PokerPeer owner) => new(_room!.Room, owner.Key, owner.Ticket, _epoch,
        _table, _version, _status, _players.ToArray(), _retiredCompanions.ToArray(), _departedPlayers.ToArray(),
        _reserves.ToArray(), _match?.ExportRecovery(), _nextPlayerId, _continuation,
        _status == PokerTableStatus.Playing ? Math.Max(0, _dealUntil - _now) : 0,
        _match?.GetPublicView().Phase == PokerPhase.Settled ? Math.Min(86400, Math.Max(0, _now - _settledAt)) : 0,
        Math.Clamp(_collectUntil - _now, 0, CollectionDuration), _restartRequired, _notice);

    private bool AcceptRecoveryWithState(PokerPacket packet)
    {
        var r = packet.Recovery;
        var s = packet.State!;
        if (!PokerNetworkProtocol.ValidRecovery(r) || r == null || r.Room != _room!.Room || r.Owner != _room.OwnerKey || r.OwnerTicket != packet.SenderTicket
            || r.Table != s.Table || r.Version != s.Version || r.Epoch != packet.Epoch || r.Epoch != s.Epoch
            || r.Status != s.Status || r.Continuation != s.Continuation || r.RestartRequired != s.RestartRequired
            || r.DealRemaining != s.DealRemaining || r.SettlementElapsed != s.SettlementElapsed
            || !r.Players.SequenceEqual(s.Players) || !r.DepartedPlayers.SequenceEqual(s.DepartedPlayers ?? [])
            || !r.RetiredCompanions.SequenceEqual(s.RetiredCompanions ?? [])) return false;
        // A displayed/acknowledged action and the recovery copy must be the SAME revision.
        // Do not accept a fresh private projection paired with an obsolete full snapshot.
        if (r.Match != null)
        {
            if (s.Hand == null || JsonSerializer.Serialize(r.Match.Public) != JsonSerializer.Serialize(s.Hand.Public)) return false;
            if (s.Hand.PlayerId > 0 && !r.Match.Hands.Single(h => h.PlayerId == s.Hand.PlayerId).Cards.SequenceEqual(s.Hand.HoleCards)) return false;
        }
        else if (s.Hand != null) return false;
        _lastRecovery = r;
        return true;
    }
    private void BeginMigration()
    {
        IsMigrating = true; _migrationId = Guid.NewGuid(); _migrationStarted = _now; _migrationRequestAt = 0;
        _offers.Clear(); _pendingAction = 0; _requestedReady = null; _botPacing.Reset();
        // Freeze the locally extrapolated clock for the overlay. No player command is accepted now.
        if (State?.Hand is { } hand)
            State = State with { Hand = hand with { Public = hand.Public with {
                SecondsRemaining = Math.Max(0, hand.Public.SecondsRemaining - Math.Max(0, _now - _receivedAt)) } } };
    }
    private void SendRecovery(string peerKey, string kind, Guid migration, PokerRecoverySnapshot? recovery = null)
    {
        var local = _room?.Members.FirstOrDefault(p => p.Key == _room.LocalKey);
        var peer = _room?.Members.FirstOrDefault(p => p.Key == peerKey && !p.Companion);
        if (local == null || peer == null || peer.Key == local.Key) return;
        _link.Send(peerKey, PokerNetworkProtocol.Encode(new(PokerNetworkProtocol.Version, _room!.Room,
            local.Ticket, peer.Ticket, kind, ++_sequence, recovery?.Table ?? _table, Epoch: migration, Recovery: recovery)));
    }
    private bool ReceiveRecovery(string sender, PokerPacket packet)
    {
        if (packet.Kind == "recovery_request")
        {
            string source = sender + ":" + packet.SenderTicket;
            if (!IsOwner && sender == _room!.OwnerKey && packet.Epoch != Guid.Empty
                && packet.Sequence > _recoveryRequests.GetValueOrDefault(source))
            {
                _recoveryRequests[source] = packet.Sequence;
                if (!IsMigrating) BeginMigration();
                SendRecovery(sender, "recovery_offer", packet.Epoch, _lastRecovery);
            }
            return true;
        }
        if (packet.Kind != "recovery_offer") return false;
        if (!IsOwner || !IsMigrating || packet.Epoch != _migrationId) return true;
        if (packet.Recovery == null) _offers[sender] = null;
        else if (packet.Recovery.Room == _room!.Room && PokerNetworkProtocol.ValidRecovery(packet.Recovery))
            _offers[sender] = packet.Recovery;
        return true;
    }
    private void TickMigration()
    {
        if (!IsOwner)
        {
            // The host's resumed state is accepted in Receive even while this overlay is active.
            if (_now - _migrationStarted > MigrationTimeoutSeconds + PeerTimeoutSeconds)
            { IsMigrating = false; _receivedAt = double.NegativeInfinity; }
            return;
        }
        var peers = _room!.Members.Where(p => !p.Companion && p.Key != _room.LocalKey).ToArray();
        if (_now >= _migrationRequestAt)
        {
            _migrationRequestAt = _now + .5;
            foreach (var peer in peers.Where(p => !_offers.ContainsKey(p.Key)))
                SendRecovery(peer.Key, "recovery_request", _migrationId);
        }
        if (_now - _migrationStarted < .75) return;
        if (peers.Any(p => !_offers.ContainsKey(p.Key)))
        {
            if (_now - _migrationStarted >= MigrationTimeoutSeconds) FailMigration();
            return;
        }
        var candidates = peers.Select(p => _offers[p.Key]).Append(_lastRecovery)
            .Where(r => r != null && r.Room == _room.Room).Cast<PokerRecoverySnapshot>().ToArray();
        if (candidates.Length == 0) { FailMigration(); return; }
        var latest = candidates.OrderByDescending(r => r.Version).First();
        // Equal versions with unequal contents are unsafe. Do not guess which deck or award to use.
        if (candidates.Where(r => r.Version == latest.Version).Any(r => JsonSerializer.Serialize(r) != JsonSerializer.Serialize(latest))
            || !PokerNetworkProtocol.ValidRecovery(latest)) { FailMigration(); return; }
        if (latest.Match != null && !MischiefPokerMatch.TryRestore(latest.Match, out _match)) { FailMigration(); return; }
        if (latest.Match == null) _match = null;
        _players = latest.Players.ToArray();
        _retiredCompanions.Clear(); _retiredCompanions.AddRange(latest.RetiredCompanions);
        _departedPlayers.Clear(); _departedPlayers.AddRange(latest.DepartedPlayers);
        _reserves.Clear(); _reserves.AddRange(latest.Reserves);
        _table = latest.Table; _version = latest.Version; _status = latest.Status;
        _nextPlayerId = latest.NextPlayerId; _continuation = latest.Continuation;
        _matchClock = latest.Match?.Clock ?? 0;
        _dealUntil = _now + latest.DealRemaining; _settledAt = _now - latest.SettlementElapsed;
        _collectUntil = _now + latest.CollectionRemaining;
        _restartRequired = latest.RestartRequired || latest.Status == PokerTableStatus.Playing;
        // Beta finishes this hand then requires Restart, even if the old owner was collecting.
        if (_restartRequired) _continuation = latest.Match?.Public.Phase == PokerPhase.Settled
            ? PokerContinuation.Paused : PokerContinuation.PauseRequested;
        _notice = _restartRequired ? "host_recovered" : latest.Notice;
        _epoch = Guid.NewGuid(); _entryPrepared = true;
        _received.Clear(); _rates.Clear(); _lastHeard.Clear(); _plannedRetirements.Clear();
        foreach (var p in _room.Members) _lastHeard[p.Key] = _now;
        IsMigrating = false; SyncMembers(); _dirty = true; Publish();
    }
    private void FailMigration()
    {
        IsMigrating = false; _epoch = Guid.NewGuid(); _entryPrepared = true;
        _version = Math.Max(Math.Max(_version, State?.Version ?? 0),
            _offers.Values.Append(_lastRecovery).Where(r => r != null).Select(r => r!.Version).DefaultIfEmpty(0).Max());
        _players = []; _table = Guid.NewGuid(); _received.Clear(); _rates.Clear();
        Interrupt("recovery_failed"); SyncMembers(); Publish();
    }
}
#endif
