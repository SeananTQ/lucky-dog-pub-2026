#if !DEMO_BUILD && !RECORDING_BUILD
#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;

namespace LuckyDogRise.Rooms.Poker;

/// <summary>
/// Single-threaded authority for a fixed roster. Independent of Godot, Steam, saves and single-player chips.
/// UI and transports consume projections; only the authority owns this object and the undealt deck.
/// </summary>
public sealed class MischiefPokerMatch
{
    private readonly int[] _players;
    private readonly long[] _chips;
    private readonly MischiefPokerRules _rules;
    private readonly List<PokerCompletedTurn> _completedTurns = new();
    private int[] _deck = Array.Empty<int>();
    private int[][] _holes = Array.Empty<int[]>();
    private int[] _board = Array.Empty<int>();
    private readonly bool[] _pending = new bool[5];
    private int[] _order = Array.Empty<int>();
    private int _deckIndex, _turnIndex, _flipsUsed;
    private double _now, _deadline;
    private long _handNumber, _revision, _pot, _systemTips;
    private PokerPhase _phase;
    private PokerDealer _dealer;
    private PokerSettlement? _settlement;

    public Guid SessionId { get; } = Guid.NewGuid();
    public MischiefPokerRules Rules => _rules;

    /// <param name="playerIds">Fixed seat order; IDs may include companion IDs and are not SteamID64.</param>
    /// <param name="firstDealerId">The room owner for the normal first hand; must participate.</param>
    public MischiefPokerMatch(IReadOnlyList<int> playerIds, int firstDealerId, MischiefPokerRules? rules = null)
    {
        ArgumentNullException.ThrowIfNull(playerIds);
        _rules = rules ?? new MischiefPokerRules();
        _rules.Validate();
        // Core supports 2..6 participants. Product admission/ready policy belongs to the later room integration.
        if (playerIds.Count is < 2 or > MischiefPokerRules.MaxPlayers)
            throw new ArgumentException("A match needs 2..6 participants.", nameof(playerIds));
        _players = playerIds.ToArray();
        if (_players.Distinct().Count() != _players.Length || !_players.Contains(firstDealerId))
            throw new ArgumentException("Seats must be unique and include the first dealer.", nameof(playerIds));
        _chips = Enumerable.Repeat(_rules.InitialChips, _players.Length).ToArray();
        _dealer = new PokerDealer(firstDealerId, 1, PokerDirection.Forward);
    }

    /// <summary>
    /// Start only on an authority request, never automatically after settlement. A supplied full deck is
    /// a trusted deterministic-test seam, not a client command. Dealing uses two passes in fixed seat order.
    /// </summary>
    public PokerError StartHand(double nowSeconds, IReadOnlyList<int>? orderedDeck = null)
    {
        if (!IsValidTime(nowSeconds)) return PokerError.InvalidTime;
        if (_phase is PokerPhase.Acting or PokerPhase.AwaitingDraw) return PokerError.HandInProgress;
        // No negative balances, automatic refill, partial ante or unapproved elimination rule.
        if (_chips.Any(chips => chips < _rules.Ante)) return PokerError.InsufficientChips;
        int[] deck;
        if (orderedDeck != null)
        {
            if (orderedDeck.Count != 52) return PokerError.InvalidDeck;
            deck = orderedDeck.ToArray();
            if (deck.Any(card => (uint)card >= 52) || deck.Distinct().Count() != 52)
                return PokerError.InvalidDeck;
        }
        else
        {
            deck = Enumerable.Range(0, 52).ToArray();
            for (int i = deck.Length - 1; i > 0; i--)
            {
                int j = RandomNumberGenerator.GetInt32(i + 1);
                (deck[i], deck[j]) = (deck[j], deck[i]);
            }
        }

        // Validate everything before collecting any ante or replacing the previous result.
        _now = nowSeconds;
        if (_settlement != null) _dealer = _settlement.NextDealer;
        _settlement = null;
        _deck = deck;
        _deckIndex = 0;
        _holes = _players.Select(_ => new int[2]).ToArray();
        _board = new int[5];
        Array.Clear(_pending);
        _pot = _rules.Ante * _players.Length;
        for (int i = 0; i < _players.Length; i++) _chips[i] -= _rules.Ante;
        for (int card = 0; card < 2; card++)
        for (int seat = 0; seat < _players.Length; seat++) _holes[seat][card] = Draw();
        for (int slot = 0; slot < 5; slot++) _board[slot] = Draw();

        int dealerSeat = Array.IndexOf(_players, _dealer.PlayerId);
        _order = Enumerable.Range(1, _players.Length)
            .Select(step => Wrap(dealerSeat + step * (int)_dealer.Direction)).ToArray();
        _turnIndex = 0;
        _flipsUsed = 0;
        _completedTurns.Clear();
        _deadline = _now + _rules.TurnSeconds;
        _phase = PokerPhase.Acting;
        _handNumber++;
        _revision++;
        return PokerError.None;
    }

    /// <summary>Advance the authority's monotonic clock. Exact deadlines expire before accepting an action.</summary>
    public PokerError AdvanceTo(double nowSeconds)
    {
        if (!IsValidTime(nowSeconds)) return PokerError.InvalidTime;
        _now = nowSeconds;
        // A delayed update consumes expired turns from their original deadlines, not fresh 15-second windows.
        while (_phase == PokerPhase.Acting && _now >= _deadline)
        {
            FinishTurn(_deadline, PokerTurnEnd.TimedOut);
            _revision++;
        }
        return PokerError.None;
    }

    public PokerError Act(PokerCommand command, double nowSeconds)
    {
        ArgumentNullException.ThrowIfNull(command);
        var clockResult = AdvanceTo(nowSeconds);
        if (clockResult != PokerError.None) return clockResult;
        if (command.SessionId != SessionId) return PokerError.WrongSession;
        if (command.HandNumber != _handNumber) return PokerError.StaleHand;
        if (command.ExpectedRevision != _revision) return PokerError.StaleRevision;
        if (_phase != PokerPhase.Acting) return PokerError.NotActing;
        if (command.PlayerId != _players[_order[_turnIndex]]) return PokerError.NotYourTurn;

        switch (command.Action)
        {
            case PokerAction.Flip:
                if ((uint)command.Slot >= 5) return PokerError.InvalidSlot;
                _pending[command.Slot] = !_pending[command.Slot];
                _flipsUsed++;
                if (_flipsUsed == MischiefPokerRules.FlipsPerTurn) FinishTurn(_now, PokerTurnEnd.TwoFlipsUsed);
                break;
            case PokerAction.EndTurn:
                FinishTurn(_now, PokerTurnEnd.Passed);
                break;
            default:
                return PokerError.InvalidAction;
        }
        _revision++;
        return PokerError.None;
    }

    /// <summary>
    /// After every participant finishes, the authority replaces pending cards and settles exactly once.
    /// AwaitingDraw preserves the last player's flips for a subsequent UI animation; no player can act there.
    /// </summary>
    public PokerError ResolveHand()
    {
        if (_phase != PokerPhase.AwaitingDraw) return PokerError.NoPendingDraw;
        var replacements = new List<PokerReplacement>();
        for (int slot = 0; slot < 5; slot++)
        {
            if (!_pending[slot]) continue;
            int oldCard = _board[slot];
            _board[slot] = Draw();
            _pending[slot] = false;
            replacements.Add(new PokerReplacement(slot, oldCard, _board[slot]));
        }

        var hands = new PokerBestHand[_players.Length];
        Span<int> seven = stackalloc int[7];
        _board.CopyTo(seven);
        for (int i = 0; i < _players.Length; i++)
        {
            seven[5] = _holes[i][0];
            seven[6] = _holes[i][1];
            hands[i] = PokerHandEvaluator.EvaluateSeven(seven);
        }
        var best = hands[0].Value;
        foreach (var hand in hands)
            if (hand.Value.CompareTo(best) > 0) best = hand.Value;
        // Seat order here is only for stable presentation; suits/seat/dealer never break a tie.
        var winnerSeats = Enumerable.Range(0, _players.Length).Where(i => hands[i].Value == best).ToArray();
        long award = _pot / winnerSeats.Length;
        long systemTip = _pot % winnerSeats.Length;
        var results = new PokerShowdown[_players.Length];
        for (int i = 0; i < _players.Length; i++)
        {
            long playerAward = hands[i].Value == best ? award : 0;
            _chips[i] += playerAward;
            results[i] = new PokerShowdown(_players[i], Copy(_holes[i]), hands[i], playerAward);
        }
        var (nextDealer, reason) = NextDealer(winnerSeats);
        _settlement = new PokerSettlement(_pot, systemTip, Copy(winnerSeats.Select(i => _players[i])),
            Copy(results), Copy(replacements), nextDealer, reason);
        _systemTips += systemTip;
        _pot = 0;
        _phase = PokerPhase.Settled;
        _revision++;
        return PokerError.None;
    }

    public PokerPublicView GetPublicView()
    {
        bool acting = _phase == PokerPhase.Acting;
        return new PokerPublicView(SessionId, _handNumber, _revision, _phase,
            Copy(_players.Select((id, seat) => new PokerSeat(id, seat + 1, _chips[seat]))),
            _dealer, Copy(_order.Select(seat => _players[seat])),
            Copy(_board.Select((card, slot) => new PokerBoardCard(card, _pending[slot]))),
            acting ? _players[_order[_turnIndex]] : null,
            acting ? MischiefPokerRules.FlipsPerTurn - _flipsUsed : 0,
            acting ? _deadline : null, acting ? Math.Max(0, _deadline - _now) : 0,
            Copy(_completedTurns), _pot, _systemTips, _settlement);
    }

    /// <summary>Authority-side projection only. A transport must authenticate the recipient before calling.</summary>
    public PokerPlayerView GetPlayerView(int playerId)
    {
        int seat = Array.IndexOf(_players, playerId);
        if (seat < 0) throw new ArgumentException("Player is not a participant.", nameof(playerId));
        return new PokerPlayerView(GetPublicView(), playerId,
            Copy(_holes.Length == 0 ? Array.Empty<int>() : _holes[seat]));
    }

    private (PokerDealer Dealer, PokerDealerChange Reason) NextDealer(int[] winnerSeats)
    {
        int oldSeat = Array.IndexOf(_players, _dealer.PlayerId);
        bool tie = winnerSeats.Length > 1;
        bool limit = !tie && winnerSeats[0] == oldSeat
            && _dealer.ConsecutiveHands >= _rules.MaxConsecutiveDealerHands;
        if (tie || limit)
        {
            // Pass along the OLD direction first, then reverse: the old dealer is next hand's first actor.
            int successor = Wrap(oldSeat + (int)_dealer.Direction);
            return (new PokerDealer(_players[successor], 1, (PokerDirection)(-(int)_dealer.Direction)),
                tie ? PokerDealerChange.Tie : PokerDealerChange.StreakLimit);
        }
        bool retained = winnerSeats[0] == oldSeat;
        return (new PokerDealer(_players[winnerSeats[0]], retained ? _dealer.ConsecutiveHands + 1 : 1,
            _dealer.Direction), retained ? PokerDealerChange.Continued : PokerDealerChange.Winner);
    }

    private void FinishTurn(double endedAt, PokerTurnEnd reason)
    {
        _completedTurns.Add(new PokerCompletedTurn(_players[_order[_turnIndex]], _flipsUsed, reason));
        _turnIndex++;
        _flipsUsed = 0;
        if (_turnIndex == _players.Length) _phase = PokerPhase.AwaitingDraw;
        else _deadline = endedAt + _rules.TurnSeconds;
    }

    private bool IsValidTime(double nowSeconds)
        => double.IsFinite(nowSeconds) && nowSeconds >= _now && nowSeconds <= TimeSpan.MaxValue.TotalSeconds;
    private int Draw() => _deck[_deckIndex++];
    private int Wrap(int seat) => (seat % _players.Length + _players.Length) % _players.Length;
    private static IReadOnlyList<T> Copy<T>(IEnumerable<T> values) => Array.AsReadOnly(values.ToArray());
}
#endif
