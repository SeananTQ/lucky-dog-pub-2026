#if !DEMO_BUILD && !RECORDING_BUILD
#nullable enable
using System;
using System.Linq;
using DataTables;

namespace LuckyDogRise.Rooms.Poker;

public enum PokerAutoChatKind { Win, Tie, CloseLoss, NextHand }
public sealed record PokerAutoChatLine(PokerAutoChatKind Kind, EHandRank Rank);

// Local speech policy only: authority still supplies the result, and ordinary
// room chat supplies filtering, rate limiting and delivery. No gameplay writes.
public sealed class PokerAutoChat
{
    private string? _room;
    private Guid _table;
    private long _hand = -1;
    private bool _consumed;
    private bool _settled;
    public bool Enabled { get; private set; } = true;
    public bool TakenOver { get; private set; }

    public void SetEnabled(bool enabled)
    {
        if (Enabled == enabled) return;
        Enabled = enabled;
        if (enabled) TakenOver = false;
        // Explicitly re-enabling resumes future opportunities, not an old result.
        _consumed |= _settled;
    }

    public void Reset()
    {
        _room = null; _table = Guid.Empty; _hand = -1;
        _consumed = _settled = TakenOver = false;
    }

    private void ObserveMatch(string? room, PokerNetworkState? state)
    {
        if (room == null || state?.Status != PokerTableStatus.Playing)
        { Reset(); return; }
        if (_room == room && _table == state.Table) return;
        Reset(); _room = room; _table = state.Table;
    }

    public void ManualMessageSent(string? room, PokerNetworkState? state)
    {
        ObserveMatch(room, state);
        if (_table != Guid.Empty) TakenOver = true;
    }

    public PokerAutoChatLine? Tick(string? room, PokerNetworkState? state, bool connected, bool blocked, bool revealReady = true)
    {
        ObserveMatch(room, state);
        if (_table == Guid.Empty || state?.Hand is not { } view) return null;
        var hand = view.Public;
        _settled = hand.Phase == PokerPhase.Settled;
        if (_hand != hand.HandNumber)
        {
            _hand = hand.HandNumber;
            // Joining/rebuilding a view after settlement must not replay it.
            _consumed = _settled;
        }
        if (_consumed || !_settled || hand.Settlement == null) return null;
        double due = PokerNetworkSession.SettlementLeadSeconds + PokerShowdownSequence.RevealSeconds + view.PlayerId % 3 * .35;
        if (state.SettlementElapsed < due) return null;
        if (!Enabled || TakenOver || !connected || blocked || state.SettlementElapsed > due + 1.5
            || state.Continuation is PokerContinuation.Collecting or PokerContinuation.Restarting)
        { _consumed = true; return null; }
        if (!revealReady) return null;
        _consumed = true;
        return ChooseLine(view);
    }

    public static PokerAutoChatLine? ChooseLine(PokerPlayerView view)
    {
        var hand = view.Public;
        var result = hand.Settlement?.Players.FirstOrDefault(p => p.PlayerId == view.PlayerId);
        if (result == null || !hand.ActionOrder.Contains(view.PlayerId)) return null;
        if (hand.Settlement!.Winners.Contains(view.PlayerId))
            return new(hand.Settlement.Winners.Count > 1 ? PokerAutoChatKind.Tie : PokerAutoChatKind.Win, result.BestHand.Value.Rank);
        return ((hand.HandNumber + view.PlayerId) % 3) switch
        {
            1 => new(PokerAutoChatKind.CloseLoss, result.BestHand.Value.Rank),
            2 => new(PokerAutoChatKind.NextHand, result.BestHand.Value.Rank),
            _ => null
        };
    }
}
#endif
