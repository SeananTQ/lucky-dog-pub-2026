#if !DEMO_BUILD && !RECORDING_BUILD
#nullable enable
using System;
using System.Collections.Generic;

namespace LuckyDogRise.Rooms.Poker;

public sealed record MischiefPokerRules
{
    public const int MaxPlayers = 6;
    public const int FlipsPerTurn = 2;
    public long InitialChips { get; init; } = 1000;
    public long Ante { get; init; } = 100;
    public double TurnSeconds { get; init; } = 15;
    public int MaxConsecutiveDealerHands { get; init; } = 4;

    internal void Validate()
    {
        // Keep all session accounting within Int64, including a winner receiving the entire table's chips.
        if (InitialChips <= 0 || InitialChips > long.MaxValue / MaxPlayers)
            throw new ArgumentOutOfRangeException(nameof(InitialChips));
        if (Ante <= 0 || Ante > InitialChips) throw new ArgumentOutOfRangeException(nameof(Ante));
        if (!double.IsFinite(TurnSeconds) || TurnSeconds <= 0 || TurnSeconds > 3600)
            throw new ArgumentOutOfRangeException(nameof(TurnSeconds));
        if (MaxConsecutiveDealerHands < 1) throw new ArgumentOutOfRangeException(nameof(MaxConsecutiveDealerHands));
    }
}

public enum PokerPhase { Ready, Acting, AwaitingDraw, Settled }
public enum PokerDirection { Forward = 1, Reverse = -1 }
public enum PokerAction { Flip, EndTurn }
public enum PokerTurnEnd { TwoFlipsUsed, Passed, TimedOut }
public enum PokerDealerChange { Continued, Winner, Tie, StreakLimit }
public enum PokerError
{
    None, InvalidTime, HandInProgress, InsufficientChips, InvalidDeck,
    WrongSession, StaleHand, StaleRevision, NotActing, NotYourTurn, InvalidAction, InvalidSlot,
    NoPendingDraw
}

// The authoritative adapter must supply the authenticated member ID and its monotonic clock.
// Clients cannot select another player's identity or provide the authority's time.
public sealed record PokerCommand(Guid SessionId, long HandNumber, long ExpectedRevision,
    int PlayerId, PokerAction Action, int Slot = -1);
public sealed record PokerDealer(int PlayerId, int ConsecutiveHands, PokerDirection Direction);
public sealed record PokerSeat(int PlayerId, int SeatNumber, long Chips);
// A face-down community card remains publicly known: it was already visible and has not been replaced.
public sealed record PokerBoardCard(int Card, bool PendingReplacement);
public sealed record PokerCompletedTurn(int PlayerId, int FlipsUsed, PokerTurnEnd Reason);
public sealed record PokerReplacement(int Slot, int OldCard, int NewCard);
public sealed record PokerShowdown(int PlayerId, IReadOnlyList<int> HoleCards, PokerBestHand BestHand, long Award);
public sealed record PokerSettlement(long Pot, long SystemTip, IReadOnlyList<int> Winners,
    IReadOnlyList<PokerShowdown> Players, IReadOnlyList<PokerReplacement> Replacements,
    PokerDealer NextDealer, PokerDealerChange DealerChange);

// Only a settled hand exposes opponents' cards. Neither a remaining deck nor future draw appears in a view.
public sealed record PokerPublicView(Guid SessionId, long HandNumber, long Revision, PokerPhase Phase,
    IReadOnlyList<PokerSeat> Seats, PokerDealer Dealer, IReadOnlyList<int> ActionOrder,
    IReadOnlyList<PokerBoardCard> Board, int? ActivePlayerId, int FlipsRemaining,
    double? TurnDeadline, double SecondsRemaining, IReadOnlyList<PokerCompletedTurn> CompletedTurns,
    long Pot, long TotalSystemTips, PokerSettlement? Settlement);
public sealed record PokerPlayerView(PokerPublicView Public, int PlayerId, IReadOnlyList<int> HoleCards);
#endif
