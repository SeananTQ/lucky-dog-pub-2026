#if !DEMO_BUILD && !RECORDING_BUILD
#nullable enable
using System;
using System.Linq;

namespace LuckyDogRise.Rooms.Poker;

public enum PokerRoundStage
{
    Acting, FinalAction, RetractingResults, Replacing, Straightening, RevealingHands,
    Showdown, AwaitingNext, Collecting, MovingDealer, AwaitingNewHand
}

// A client's visual timeline, never the authority's turn clock or chip ledger.
// Receiving a settlement does not immediately expose it to every view on screen.
public sealed class PokerRoundPresentation
{
    public const double FlipSeconds = 0.16;
    public const double FinalActionSeconds = 0.55;
    public const double DrawerSeconds = 0.20;
    public const double StraightenSeconds = 0.53;
    public const double CollectSeconds = 0.48;
    public const double DealerFlightSeconds = 0.60;
    private static readonly long Ante = new MischiefPokerRules().Ante;
    private Guid _session;
    private long _hand = -1;
    private int _viewer;
    private double _elapsed;
    private bool _nextIssued;
    private PokerPlayerView? _source;
    public PokerRoundStage Stage { get; private set; }
    public double StageSeconds => _elapsed;
    public PokerShowdownSequence Showdown { get; } = new();
    public PokerPlayerView? DisplayView { get; private set; }
    // The preview defaults to a manual inspection stop. A normal loop can use false.
    public bool PauseAfterPayout { get; set; } = true;
    public bool ActionResultsVisible => Stage is PokerRoundStage.Acting or PokerRoundStage.FinalAction;
    public bool HandsRevealed => Stage >= PokerRoundStage.RevealingHands;
    public bool RanksVisible => Stage is PokerRoundStage.Showdown or PokerRoundStage.AwaitingNext;
    public bool WinnersVisible => RanksVisible && Showdown.Revealed;
    public bool CanAffordNext => _source?.Public.Seats.All(s => s.Chips >= Ante) == true;
    public bool CanRequestNext => Stage == PokerRoundStage.AwaitingNext && CanAffordNext;
    public bool DealerInFlight => Stage == PokerRoundStage.MovingDealer;
    public int? DealerId => DealerInFlight ? null : Stage == PokerRoundStage.AwaitingNewHand
        ? _source?.Public.Settlement?.NextDealer.PlayerId : _source?.Public.Dealer.PlayerId;

    public void Update(PokerPlayerView view, double delta, bool cardsMoving = false)
    {
        if (_session != view.Public.SessionId || _hand != view.Public.HandNumber || _viewer != view.PlayerId)
        {
            _session = view.Public.SessionId; _hand = view.Public.HandNumber; _viewer = view.PlayerId;
            Enter(PokerRoundStage.Acting);
            _nextIssued = false;
            Showdown.Update(view with { Public = view.Public with { Phase = PokerPhase.Acting, Settlement = null } }, 0);
        }
        _source = view;
        double step = double.IsFinite(delta) && delta > 0 ? Math.Min(delta, 0.1) : 0;
        if (Stage == PokerRoundStage.Acting && view.Public.Phase is PokerPhase.AwaitingDraw or PokerPhase.Settled)
            Enter(PokerRoundStage.FinalAction);
        else if (step > 0)
        {
            // The last click may still be halfway through its flip. Its result deserves a full hold.
            if (Stage == PokerRoundStage.FinalAction && cardsMoving) _elapsed = 0;
            else _elapsed += step;
            switch (Stage)
            {
                case PokerRoundStage.FinalAction when _elapsed >= FinalActionSeconds && !cardsMoving:
                    Enter(PokerRoundStage.RetractingResults); break;
                case PokerRoundStage.RetractingResults when _elapsed >= DrawerSeconds && view.Public.Settlement != null:
                    Enter(PokerRoundStage.Replacing); break;
                case PokerRoundStage.Replacing when _elapsed >= FlipSeconds && !cardsMoving:
                    Enter(PokerRoundStage.Straightening); break;
                case PokerRoundStage.Straightening when _elapsed >= StraightenSeconds && !cardsMoving:
                    Enter(PokerRoundStage.RevealingHands); break;
                case PokerRoundStage.RevealingHands when _elapsed >= FlipSeconds && !cardsMoving:
                    Enter(PokerRoundStage.Showdown); break;
                case PokerRoundStage.Showdown:
                    Showdown.Update(view, step);
                    if (Showdown.Finished) Enter(PokerRoundStage.AwaitingNext);
                    break;
                case PokerRoundStage.AwaitingNext when !PauseAfterPayout && CanAffordNext:
                    Enter(PokerRoundStage.Collecting); break;
                case PokerRoundStage.Collecting when _elapsed >= CollectSeconds && !cardsMoving:
                    Enter(view.Public.Dealer.PlayerId == view.Public.Settlement?.NextDealer.PlayerId
                        ? PokerRoundStage.AwaitingNewHand : PokerRoundStage.MovingDealer);
                    break;
                case PokerRoundStage.MovingDealer when _elapsed >= DealerFlightSeconds:
                    Enter(PokerRoundStage.AwaitingNewHand); break;
            }
        }
        DisplayView = Mask(view);
    }

    public bool RequestNext()
    {
        if (!CanRequestNext) return false;
        Enter(PokerRoundStage.Collecting);
        return true;
    }

    public void CompletePayout(PokerPlayerView view)
    {
        if (view.Public.Settlement == null || Stage >= PokerRoundStage.AwaitingNext) return;
        // Bounded catch-up for a late client; no accounting or network actions occur here.
        Showdown.Update(view, 0);
        for (int i = 0; i < 160 && !Showdown.Finished; i++) Showdown.Update(view, 0.1);
        Enter(PokerRoundStage.AwaitingNext);
        DisplayView = Mask(view);
    }

    // The caller starts exactly one authoritative hand, only after old cards/badge have finished.
    public bool TakeNextHandRequest()
    {
        if (Stage != PokerRoundStage.AwaitingNewHand || _nextIssued) return false;
        _nextIssued = true;
        return true;
    }

    public void NextHandRejected()
    {
        _nextIssued = false;
        Enter(PokerRoundStage.AwaitingNext);
    }

    private void Enter(PokerRoundStage stage) { Stage = stage; _elapsed = 0; }

    private PokerPlayerView Mask(PokerPlayerView view)
    {
        if (view.Public.Settlement is not { } result) return view;
        var state = view.Public;
        var board = Stage < PokerRoundStage.Replacing
            ? state.Board.Select((card, slot) => result.Replacements.FirstOrDefault(r => r.Slot == slot) is { } old
                ? new PokerBoardCard(old.OldCard, true) : card).ToArray() : state.Board;
        var winners = state.ActionOrder.Where(result.Winners.Contains).ToArray();
        bool Paid(int id)
        {
            if (Stage >= PokerRoundStage.AwaitingNext) return true;
            if (Stage != PokerRoundStage.Showdown || !Showdown.Revealed) return false;
            int index = Array.IndexOf(winners, id);
            return index >= 0 && (index < Showdown.Index || index == Showdown.Index && Showdown.StepSeconds >= 1.15);
        }
        var seats = state.Seats.Select(seat => seat with
        {
            Chips = seat.Chips - (Paid(seat.PlayerId) ? 0 : result.Players.First(p => p.PlayerId == seat.PlayerId).Award)
        }).ToArray();
        return view with { Public = state with
        {
            Board = board, Seats = seats, Pot = result.Pot,
            Phase = HandsRevealed ? PokerPhase.Settled : PokerPhase.AwaitingDraw,
            Settlement = HandsRevealed ? result : null
        } };
    }
}
#endif
