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
    private readonly Func<long, RoomCompanionPlan> _createCompanions;
    private readonly Dictionary<ulong, (int Id, long Presence)> _identities = new();
    private readonly HashSet<ulong> _banned = new();
    private readonly HashSet<ulong> _blockedLobbies = new();
    private RoomClient _client;
    private Pending _inFlight;
    private Pending _queued;
    private ulong _lobby;
    private ulong _accessOwner;
    private string _acceptedName = "";
    private ulong _renameOwner;
    private readonly HashSet<string> _staleRenameNames = new(StringComparer.Ordinal);
    private RoomCompanionPlan _companionPlan;
    private bool _companionPlanAvailable;
    private SteamRoomData _lastRoomData;
    private long _lastCompanionSecond = -1;
    private string _pendingCompanions = "";
    private double _nextCompanionWrite;
    private int _companionWriteFailures;
    private bool _companionWriteInProgress;
    private bool _refreshLobbyAfterCompanionWrite;
    private SteamRoomGameState _gameState;
    private long? _roomCreatedAt;
    private long _gameNow;
    private double _gameClock;
    private double _gameClockSampleAt;
    private long _lastGameDisplaySecond = -1;
    private ulong _gameOwner;
    private bool _gameWriteInProgress;
    private double _nextGameCleanup;
    private Guid _deadlineProposal;
    private double _proposalDeadline;
    private long _gameVoteSequence;
    private Guid _localGameProposal;
    private double _localCompanionAcceptAt;
    private readonly HashSet<Guid> _invalidGameProposals = new();
    private readonly Dictionary<ulong, (Guid Proposal, Guid Session, long Sequence, bool Accept)> _gameVotes = new();
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
        Func<double> now = null, SteamRoomInviteInbox invitations = null,
        Func<long, RoomCompanionPlan> companions = null)
    {
        _transport = transport;
        _invitations = invitations ?? new SteamRoomInviteInbox();
        _defaultSkin = defaultSkin;
        _validSkin = validSkin;
        _validHeadwear = validHeadwear;
        _validReaction = validReaction;
        _createCompanions = companions;
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
                    int visibleCount = data.Count;
                    if (RoomCompanionPlan.TryRead(data.Companions, out var companions))
                    {
                        // Our current room may have already observed a later retirement
                        // than this discovery callback. Never revive it just for the list.
                        if (data.LobbyId == _lobby && _companionPlan != null)
                            companions = companions.SameGeneration(_companionPlan)
                                ? companions.MergeRetirements(_companionPlan) : null;
                        if (companions != null)
                            visibleCount += companions.WithHumanCount(data.Count).RemainingCount;
                    }
                    listings.Add(new RoomListing(SteamRoomProtocol.Encode(data.LobbyId), data.Name,
                        SteamRoomProtocol.IsGameValid(data.Game) ? data.Game : "social", visibleCount,
                        data.Capacity, data.Count));
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
            if (pending.Request.Operation == RoomOperation.Create && _createCompanions != null)
            {
                var companions = _createCompanions(_transport.ServerTime);
                if (companions != null && !_transport.SetCompanions(result.LobbyId, companions.ToWire()))
                    throw new InvalidOperationException("Steam room companions could not be initialized.");
            }
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
                ClearRenameOverride();
                ResetCompanions();
                ResetGameState();
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
        && SteamRoomProtocol.TryDecodeGameState(data.GameState, out _)
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

    public string SetGame(RoomClient client, string gameId) => "Rooms_GameChangeRequired";

    private void ResetGameState()
    {
        _gameState = null; _roomCreatedAt = null; _gameNow = 0; _lastGameDisplaySecond = -1;
        _gameClock = 0; _gameClockSampleAt = Now;
        _gameOwner = 0; _gameWriteInProgress = false; _nextGameCleanup = 0;
        _deadlineProposal = Guid.Empty; _proposalDeadline = 0; _gameVoteSequence = 0;
        _localGameProposal = Guid.Empty; _localCompanionAcceptAt = 0;
        _invalidGameProposals.Clear(); _gameVotes.Clear();
    }

    private void AdvanceGameClock()
    {
        double now = Now;
        _gameClock = Math.Max(_gameClock + Math.Max(0, now - _gameClockSampleAt), _transport.ServerTime);
        _gameClockSampleAt = now;
        _gameNow = (long)Math.Min(long.MaxValue, Math.Floor(_gameClock));
    }

    private SteamRoomGameVoter[] CurrentVoters(SteamRoomData data)
    {
        var voters = new List<SteamRoomGameVoter>();
        foreach (var raw in data.Members.Where(m => !_banned.Contains(m.SteamId)).OrderBy(m => m.SteamId))
        {
            var session = raw.SteamId == _transport.LocalSteamId ? _chatSession
                : Guid.TryParseExact(raw.ChatSession, "N", out var parsed) ? parsed : Guid.Empty;
            if (raw.SteamId == 0 || session == Guid.Empty || voters.Any(v => v.SteamId == raw.SteamId)) return null;
            voters.Add(new(raw.SteamId, session));
        }
        if (RoomCompanionPlan.TryRead(data.Companions, out var plan)
            && (_companionPlan == null || plan.SameGeneration(_companionPlan)))
        {
            if (_companionPlan != null) plan = plan.MergeRetirements(_companionPlan);
            foreach (var bot in plan.WithHumanCount(voters.Count).MembersAt(_gameNow).OrderBy(m => m.Id))
                voters.Add(new(0, Guid.Empty, bot.Id, bot.Presence));
        }
        return voters.Count is >= 1 and <= RoomRules.Capacity ? voters.ToArray() : null;
    }

    private bool ProposalMatches(SteamRoomData data, SteamRoomGameProposal proposal)
        => proposal.Owner == data.OwnerId && proposal.StartedAt <= _gameNow + 2
            && !_invalidGameProposals.Contains(proposal.Id)
            && CurrentVoters(data) is { } voters && voters.SequenceEqual(proposal.Voters);

    private SteamRoomData ObserveGameState(SteamRoomData data)
    {
        if (!SteamRoomProtocol.TryDecodeGameState(data.GameState, out var incoming)) return data;
        AdvanceGameClock();
        if (_roomCreatedAt == null && data.CreatedAt is > 0 && data.CreatedAt <= _gameNow + 2)
            _roomCreatedAt = data.CreatedAt;
        if (_gameOwner != 0 && _gameOwner != data.OwnerId && _gameState?.Proposal is { } previous)
            _invalidGameProposals.Add(previous.Id);
        _gameOwner = data.OwnerId;
        // Same revision has exactly one payload; late callbacks cannot undo a
        // successful local write or revive a completed/cancelled proposal.
        if (_gameState == null || incoming.Revision > _gameState.Revision) _gameState = incoming;
        if (_gameState.Proposal is { } p && (!ProposalMatches(data, p) || _gameNow >= p.ExpiresAt))
            _invalidGameProposals.Add(p.Id);
        return data with { Game = _gameState.Game, GameState = SteamRoomProtocol.EncodeGameState(_gameState), CreatedAt = _roomCreatedAt };
    }

    private string ReadGame(RoomClient client, bool ownerOnly, out SteamRoomData data, bool allowBusy = false)
    {
        data = null;
        if (!IsAvailable || !ReferenceEquals(client, _client) || _lobby == 0 || client.IsBusy && !allowBusy)
            return "Rooms_GameChangeUnavailable";
        data = ApplyRenameOverride(_transport.ReadLobby(_lobby, true));
        if (!Compatible(data) || data.Members == null || !data.Members.Any(m => m.SteamId == _transport.LocalSteamId))
            return "Rooms_GameChangeUnavailable";
        data = ObserveGameState(data);
        Publish(data);
        return ownerOnly && data.OwnerId != _transport.LocalSteamId ? "Rooms_GameChangeNotOwner" : "";
    }

    private string WriteGame(SteamRoomData data, SteamRoomGameState next)
    {
        var lobby = _lobby;
        if (next.Revision <= 0 || data.OwnerId != _transport.LocalSteamId || _gameWriteInProgress)
            return "Rooms_GameChangeUnavailable";
        _gameWriteInProgress = true;
        try
        {
            string wire = SteamRoomProtocol.EncodeGameState(next);
            if (!_transport.SetGameState(lobby, wire)) return "Rooms_GameChangeUpdateFailed";
            if (_lobby != lobby) return "Rooms_GameChangeUnavailable";
            _gameState = next;
            Publish(data with { Game = next.Game, GameState = wire });
            return "ok";
        }
        finally { _gameWriteInProgress = false; }
    }

    private string GetProposal(RoomClient client, string id, bool ownerOnly,
        out SteamRoomData data, out SteamRoomGameProposal proposal)
    {
        proposal = null;
        var error = ReadGame(client, ownerOnly, out data);
        if (error.Length > 0) return error;
        if (!Guid.TryParseExact(id, "N", out var guid) || _gameState.Proposal is not { } p || p.Id != guid)
            return "Rooms_GameChangeInvalidProposal";
        if (_gameNow >= p.ExpiresAt) return "Rooms_GameChangeExpired";
        if (!ProposalMatches(data, p)) return "Rooms_GameChangeInvalidProposal";
        proposal = p;
        return "";
    }

    public string ProposeGameChange(RoomClient client, string gameId)
    {
        try
        {
            var error = ReadGame(client, true, out var data);
            if (error.Length > 0) return error;
            if (!RoomRules.IsRoomMode(gameId) || gameId == _gameState.Game) return "Rooms_GameChangeInvalidMode";
            if (_gameState.Proposal is { } current && ProposalMatches(data, current) && _gameNow < current.ExpiresAt)
                return "Rooms_GameChangeAlreadyPending";
            var voters = CurrentVoters(data);
            if (voters == null || _gameNow <= 0 || _gameNow > long.MaxValue - 60) return "Rooms_GameChangeUnavailable";
            var proposal = new SteamRoomGameProposal(Guid.NewGuid(), gameId, data.OwnerId, _chatSession,
                _gameNow, _gameNow + 60, voters);
            _localGameProposal = proposal.Id;
            _localCompanionAcceptAt = Now + RoomRules.CompanionVoteDelay;
            return WriteGame(data, new(checked(_gameState.Revision + 1), _gameState.Game, proposal));
        }
        catch { return "Rooms_GameChangeUpdateFailed"; }
    }

    public string RespondGameChange(RoomClient client, string proposalId, bool accept)
    {
        try
        {
            var error = GetProposal(client, proposalId, false, out var data, out var proposal);
            if (error.Length > 0) return error;
            if (proposal.Owner == _transport.LocalSteamId) return accept ? "ok" : "Rooms_GameChangeInvalidProposal";
            if (_gameVotes.TryGetValue(_transport.LocalSteamId, out var prior)
                && prior.Proposal == proposal.Id && prior.Session == _chatSession && prior.Accept == accept) return "ok";
            var sequence = checked(++_gameVoteSequence);
            _transport.SetGameVote(_lobby, SteamRoomProtocol.EncodeGameVote(proposal.Id, _chatSession, sequence, accept));
            _gameVotes[_transport.LocalSteamId] = (proposal.Id, _chatSession, sequence, accept);
            Publish(data);
            return "ok";
        }
        catch { return "Rooms_GameChangeUpdateFailed"; }
    }

    public string ConfirmGameChange(RoomClient client, string proposalId)
    {
        try
        {
            var error = GetProposal(client, proposalId, true, out var data, out var proposal);
            if (error.Length > 0) return error;
            if (BuildGameChange(data)?.HasMajority != true) return "Rooms_GameChangeNoMajority";
            return WriteGame(data, new(checked(_gameState.Revision + 1), proposal.Target));
        }
        catch { return "Rooms_GameChangeUpdateFailed"; }
    }

    public string CancelGameChange(RoomClient client, string proposalId)
    {
        try
        {
            var error = GetProposal(client, proposalId, true, out var data, out _);
            return error.Length > 0 ? error : WriteGame(data, new(checked(_gameState.Revision + 1), _gameState.Game));
        }
        catch { return "Rooms_GameChangeUpdateFailed"; }
    }

    private RoomGameChange BuildGameChange(SteamRoomData data)
    {
        if (_gameState?.Proposal is not { } p || _gameNow >= p.ExpiresAt || !ProposalMatches(data, p)) return null;
        if (_deadlineProposal != p.Id)
        { _deadlineProposal = p.Id; _proposalDeadline = Now + p.ExpiresAt - _gameNow; }
        else _proposalDeadline = Math.Min(_proposalDeadline, Now + p.ExpiresAt - _gameNow);
        var votes = new List<RoomGameVote>();
        foreach (var voter in p.Voters)
        {
            if (voter.SteamId == 0)
            {
                votes.Add(new(voter.CompanionId, voter.CompanionPresence,
                    _gameNow >= p.StartedAt + 1 && (p.Id != _localGameProposal || Now >= _localCompanionAcceptAt)
                        ? RoomGameVoteState.Accepted : RoomGameVoteState.Pending));
                continue;
            }
            if (!_identities.TryGetValue(voter.SteamId, out var identity)) return null;
            var state = voter.SteamId == p.Owner ? RoomGameVoteState.Accepted : RoomGameVoteState.Pending;
            var raw = data.Members.FirstOrDefault(m => m.SteamId == voter.SteamId);
            if (raw != null && SteamRoomProtocol.TryDecodeGameVote(raw.GameVote, out var proposal, out var session, out var sequence, out var accept)
                && proposal == p.Id && session == voter.Session
                && (!_gameVotes.TryGetValue(voter.SteamId, out var old) || old.Proposal != p.Id
                    || old.Session != session || sequence > old.Sequence))
                _gameVotes[voter.SteamId] = (proposal, session, sequence, accept);
            if (voter.SteamId != p.Owner && _gameVotes.TryGetValue(voter.SteamId, out var vote)
                && vote.Proposal == p.Id && vote.Session == voter.Session)
                state = vote.Accept ? RoomGameVoteState.Accepted : RoomGameVoteState.Declined;
            votes.Add(new(identity.Id, identity.Presence, state));
        }
        return new(p.Id.ToString("N"), p.Target, _proposalDeadline, votes.ToArray());
    }

    private static bool SameGameChange(RoomGameChange a, RoomGameChange b) => a == null ? b == null
        : b != null && a.Id == b.Id && a.TargetGameId == b.TargetGameId
            && a.ExpiresAt == b.ExpiresAt && a.Votes.SequenceEqual(b.Votes);

    public string SetName(RoomClient client, string name)
    {
        if (!IsAvailable || !ReferenceEquals(client, _client) || _lobby == 0 || client.IsBusy)
            return "Rooms_NameUnavailable";
        try
        {
            var lobby = _lobby;
            var raw = _transport.ReadLobby(lobby, true);
            if (raw?.OwnerId != _transport.LocalSteamId) return "Rooms_NameNotOwner";
            if (!Compatible(raw) || raw.Members == null) return "Rooms_NameUnavailable";
            if (!RoomRules.TryNormalizeName(name, out var normalized)) return "Rooms_InvalidName";
            var data = ApplyRenameOverride(raw);
            if (data.Name == normalized) return "ok";
            if (!_transport.SetName(lobby, normalized)) return "Rooms_NameUpdateFailed";
            if (_lobby != lobby) return "Rooms_NameUnavailable";
            _staleRenameNames.Add(raw.Name);
            _staleRenameNames.Add(data.Name);
            if (_staleRenameNames.Count > 40)
            {
                _staleRenameNames.Clear();
                _staleRenameNames.Add(raw.Name);
                _staleRenameNames.Add(data.Name);
            }
            _acceptedName = normalized;
            _renameOwner = data.OwnerId;
            Publish(data with { Name = normalized });
            return "ok";
        }
        catch { return "Rooms_NameUpdateFailed"; }
    }

    private SteamRoomData ApplyRenameOverride(SteamRoomData data)
    {
        if (data == null || _acceptedName.Length == 0) return data;
        if (data.OwnerId != _renameOwner || data.Name == _acceptedName || !_staleRenameNames.Contains(data.Name))
        {
            ClearRenameOverride();
            return data;
        }
        // An accepted SetLobbyData updates the local name immediately. A delayed
        // callback with its old value must not flicker it backwards or write it back.
        return data with { Name = _acceptedName };
    }

    private void ClearRenameOverride()
    {
        _acceptedName = "";
        _renameOwner = 0;
        _staleRenameNames.Clear();
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
            var data = ApplyRenameOverride(_transport.ReadLobby(lobby, true));
            if (data?.OwnerId != _transport.LocalSteamId) return "Rooms_KickNotOwner";
            if (!Compatible(data) || data.Members == null
                || !SteamRoomProtocol.TryDecodeBannedMembers(data.BannedMembers, out var bans))
                return "Rooms_KickUnavailable";
            if (memberId < 0)
            {
                if (!RoomCompanionPlan.TryRead(data.Companions, out var plan)
                    || _companionPlan != null && !plan.SameGeneration(_companionPlan))
                    return "Rooms_KickInvalidTarget";
                plan = plan.MergeRetirements(_companionPlan).WithHumanCount(data.Count);
                if (!plan.TryRetire(memberId, presence, out var retired)) return "Rooms_KickInvalidTarget";
                bool written;
                _companionWriteInProgress = true;
                try { written = _transport.SetCompanions(lobby, retired.ToWire()); }
                finally { _companionWriteInProgress = false; }
                if (!written) return "Rooms_KickUnavailable";
                if (_lobby != lobby) return "Rooms_KickUnavailable";
                _companionPlan = retired.MergeRetirements(_companionPlan);
                _pendingCompanions = "";
                _companionWriteFailures = 0;
                Publish(data with { Companions = _companionPlan.ToWire() });
                return "";
            }
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
        try
        {
            if (ReadGame(client, false, out var room, allowBusy: true).Length > 0) return "Rooms_ChatUnavailable";
            if (room.Game != "social") return "Rooms_ChatDisabled";
        }
        catch { return "Rooms_ChatUnavailable"; }
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
            if (!_transport.SendChat(_lobby, SteamRoomProtocol.EncodeChat(_chatSession, sequence, sentAt, text, _gameState.Revision)))
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
            || !SteamRoomProtocol.TryDecodeChat(bytes, out var session, out var sequence, out var sentAt, out var text, out var modeRevision)) return;
        try
        {
            if (ReadGame(_client, false, out var room, allowBusy: true).Length > 0 || room.Game != "social"
                || modeRevision != _gameState.Revision) return;
        }
        catch { return; }
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
        if (lobby == _lobby)
        {
            if (_gameState?.Proposal is { } proposal && proposal.Voters.Any(v => v.SteamId == member))
                _invalidGameProposals.Add(proposal.Id);
            _identities.Remove(member); _chatSessions.Remove(member); _receivedChatAt.Remove(member);
        }
    }

    private void OnLobbyChanged(ulong lobby)
    {
        if (_disposed || _lobby == 0 || lobby != 0 && lobby != _lobby) return;
        if (_companionWriteInProgress || _gameWriteInProgress)
        {
            // Steam dispatches later, but a fake/alternate transport may notify
            // from its setter. Re-read on the next tick rather than nesting Publish.
            _refreshLobbyAfterCompanionWrite = true;
            return;
        }
        if (!IsAvailable) { Disconnect(); return; }
        try
        {
            var data = ApplyRenameOverride(_transport.ReadLobby(_lobby, true));
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

    private void Publish(SteamRoomData data, bool refreshCompanions = true)
    {
        data = ObserveGameState(data);
        if (refreshCompanions)
        {
            UpdateCompanionPlan(data);
            _lastRoomData = data;
        }
        var present = data.Members.Where(m => !_banned.Contains(m.SteamId)).Select(m => m.SteamId).ToHashSet();
        foreach (var stale in _identities.Keys.Where(id => !present.Contains(id)).ToArray()) _identities.Remove(stale);
        foreach (var stale in _chatSessions.Keys.Where(id => !present.Contains(id)).ToArray()) _chatSessions.Remove(stale);
        foreach (var stale in _receivedChatAt.Keys.Where(id => !present.Contains(id)).ToArray()) _receivedChatAt.Remove(stale);
        foreach (var stale in _gameVotes.Keys.Where(id => !present.Contains(id)).ToArray()) _gameVotes.Remove(stale);
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
            var visibleName = new string((raw.Name ?? "").Where(c => !char.IsControl(c)).Take(64).ToArray());
            var name = raw.SteamId == _transport.LocalSteamId && string.IsNullOrWhiteSpace(visibleName)
                ? "" : SteamRoomProtocol.SafePersonaName(raw.Name);
            members.Add(new RoomMember(identity.Id, name,
                valid && _validSkin(skin) ? skin : _defaultSkin,
                valid && (hat == 0 || _validHeadwear(hat)) ? hat : 0,
                valid && _validReaction(reaction) ? reaction : 1001, identity.Presence,
                validActivity ? sequence : 0, validActivity && active));
        }
        var owner = _identities.TryGetValue(data.OwnerId, out var ownerIdentity) ? ownerIdentity.Id : 0;
        var displayed = members.OrderBy(m => m.Id).ToList();
        if (_companionPlanAvailable)
        {
            _lastCompanionSecond = Math.Max(_lastCompanionSecond, _transport.ServerTime);
            foreach (var member in _companionPlan.MembersAt(_lastCompanionSecond))
            {
                if (displayed.Count >= RoomRules.Capacity) break;
                displayed.Add(member with
                {
                    SkinId = _validSkin(member.SkinId) ? member.SkinId : _defaultSkin,
                    HeadwearId = member.HeadwearId == 0 || _validHeadwear(member.HeadwearId) ? member.HeadwearId : 0,
                    Reaction = _validReaction(member.Reaction) ? member.Reaction : 1001
                });
            }
        }
        var game = SteamRoomProtocol.IsGameValid(data.Game) ? data.Game : "social";
        var gameChange = BuildGameChange(data);
        var previous = _client.View;
        if (previous != null && previous.Code == SteamRoomProtocol.Encode(_lobby)
            && previous.Name == data.Name && previous.GameId == game && previous.OwnerId == owner
            && previous.Access == data.Access && previous.CreatedAt == data.CreatedAt
            && SameGameChange(previous.GameChange, gameChange) && previous.Members.SequenceEqual(displayed)) return;
        _client.Receive(new RoomSnapshot(SteamRoomProtocol.Encode(_lobby), data.Name,
            game, owner, ++_revision, displayed.ToArray(), data.Access, data.CreatedAt, _gameNow, gameChange));
    }

    private void UpdateCompanionPlan(SteamRoomData data)
    {
        if (!RoomCompanionPlan.TryRead(data.Companions, out var incoming)
            || _companionPlan != null && !incoming.SameGeneration(_companionPlan))
        {
            // Corrupt/missing or a replaced generation cannot fabricate people or
            // cause disconnects. Retain the retirement memory for a later valid update.
            _companionPlanAvailable = false;
            _pendingCompanions = "";
            return;
        }
        _companionPlan = incoming.MergeRetirements(_companionPlan).WithHumanCount(data.Count);
        _companionPlanAvailable = true;
        var desired = _companionPlan.ToWire();
        if (data.OwnerId != _transport.LocalSteamId || desired == data.Companions)
        {
            _pendingCompanions = "";
            _companionWriteFailures = 0;
            return;
        }
        // Only retirement changes are written. Animation derives from the shared
        // plan and Steam clock locally; it never writes per-second member data.
        if (_pendingCompanions != desired)
        {
            _pendingCompanions = desired;
            _nextCompanionWrite = Now;
            _companionWriteFailures = 0;
        }
        FlushCompanionWrite(data);
    }

    private void FlushCompanionWrite(SteamRoomData data)
    {
        if (_pendingCompanions.Length == 0 || Now < _nextCompanionWrite
            || data.OwnerId != _transport.LocalSteamId || data.LobbyId != _lobby
            || _companionWriteInProgress) return;
        var lobby = _lobby;
        var written = _pendingCompanions;
        _companionWriteInProgress = true;
        try
        {
            if (_transport.SetCompanions(lobby, written))
            {
                // A synchronous test transport callback may already have queued
                // a newer retirement mask. Do not clear that update with this result.
                if (_lobby == lobby && _pendingCompanions == written)
                {
                    _pendingCompanions = "";
                    _companionWriteFailures = 0;
                }
                return;
            }
        }
        catch { /* Optional companions keep the last safe local retirement state. */ }
        finally { _companionWriteInProgress = false; }
        if (_lobby != lobby || _pendingCompanions != written) return;
        _companionWriteFailures++;
        _nextCompanionWrite = Now + (_companionWriteFailures switch { 1 => 5, 2 => 15, 3 => 30, _ => 60 });
    }

    private void ResetCompanions()
    {
        _companionPlan = null;
        _companionPlanAvailable = false;
        _lastRoomData = null;
        _lastCompanionSecond = -1;
        _pendingCompanions = "";
        _companionWriteFailures = 0;
        _nextCompanionWrite = 0;
        _refreshLobbyAfterCompanionWrite = false;
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
        ClearRenameOverride();
        ResetCompanions();
        ResetGameState();
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
        if (!_disposed && IsAvailable && _lobby != 0 && _lastRoomData != null)
        {
            try
            {
                if (_refreshLobbyAfterCompanionWrite)
                {
                    _refreshLobbyAfterCompanionWrite = false;
                    OnLobbyChanged(_lobby);
                    if (_lobby == 0 || _lastRoomData == null) return;
                }
                FlushCompanionWrite(_lastRoomData);
                AdvanceGameClock();
                if (_gameState?.Proposal is { } proposal && _gameNow != _lastGameDisplaySecond)
                {
                    _lastGameDisplaySecond = _gameNow;
                    Publish(_lastRoomData, refreshCompanions: false);
                }
                if (_gameState?.Proposal is { } invalid && (!ProposalMatches(_lastRoomData, invalid) || _gameNow >= invalid.ExpiresAt)
                    && _lastRoomData.OwnerId == _transport.LocalSteamId && Now >= _nextGameCleanup)
                {
                    _nextGameCleanup = Now + 2;
                    WriteGame(_lastRoomData, new(checked(_gameState.Revision + 1), _gameState.Game));
                }
                long second = Math.Max(_lastCompanionSecond, _transport.ServerTime);
                if (_companionPlanAvailable && second != _lastCompanionSecond)
                {
                    _lastCompanionSecond = second;
                    Publish(_lastRoomData, refreshCompanions: false);
                }
            }
            catch { Disconnect(); }
        }
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
