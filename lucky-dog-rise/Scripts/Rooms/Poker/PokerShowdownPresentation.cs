#if !DEMO_BUILD && !RECORDING_BUILD
#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using DataTables;

namespace LuckyDogRise.Rooms.Poker;

// Per-viewer explanation, built exclusively from an already public settlement.
// These masks never change the evaluator, awards, or private-card visibility.
public sealed record PokerShowdownEmphasis(int WinnerId, IReadOnlySet<int> LocalCards,
    IReadOnlySet<int> WinnerCards, int? DecisiveCard);

public static class PokerShowdownPresentation
{
    public static PokerShowdownEmphasis? Create(PokerPublicView state, int viewerId, int winnerId)
    {
        if (state.Phase != PokerPhase.Settled || state.Settlement is not { } settlement
            || !settlement.Winners.Contains(winnerId)) return null;
        var winner = settlement.Players.Single(p => p.PlayerId == winnerId);
        var local = settlement.Players.FirstOrDefault(p => p.PlayerId == viewerId);
        int[] winnerBest = SelectBest(state.Board, winner.HoleCards);
        int[] localBest = local == null ? Array.Empty<int>() : SelectBest(state.Board, local.HoleCards);
        bool localWinner = settlement.Winners.Contains(viewerId);
        bool fullTie = localWinner && settlement.Winners.Count > 1;
        var bright = Core(winnerBest, winner.BestHand.Value.Rank, fullTie);
        var outlined = local == null ? new HashSet<int>() : Core(localBest, local.BestHand.Value.Rank, fullTie);
        int? decisive = null;
        // A sole local winner still needs to see why this hand won. The strongest opponent
        // selects the necessary kicker, but receives no extra UI or comparison animation.
        var opponent = localWinner
            ? fullTie ? null : settlement.Players.Where(p => p.PlayerId != winnerId)
                .OrderByDescending(p => p.BestHand.Value).FirstOrDefault()
            : local;
        if (opponent != null && opponent.BestHand.Value.Rank == winner.BestHand.Value.Rank
            && winner.BestHand.Value.CompareTo(opponent.BestHand.Value) > 0
            && HasKickers(winner.BestHand.Value.Rank))
        {
            var opponentBest = localWinner ? SelectBest(state.Board, opponent.HoleCards) : localBest;
            var opponentCore = Core(opponentBest, opponent.BestHand.Value.Rank, false);
            // Compare core ranks, not physical card IDs or just the category name.
            var winnerCore = bright.Select(Rank).OrderByDescending(r => r);
            if (winnerCore.SequenceEqual(opponentCore.Select(Rank).OrderByDescending(r => r)))
            {
                var winnerKickers = winnerBest.Except(bright).OrderByDescending(Rank).ToArray();
                var opponentKickers = opponentBest.Except(opponentCore).OrderByDescending(Rank).ToArray();
                for (int i = 0; i < winnerKickers.Length; i++)
                {
                    if (Rank(winnerKickers[i]) == Rank(opponentKickers[i])) continue;
                    bright.Add(winnerKickers[i]);
                    outlined.Add(localWinner ? winnerKickers[i] : opponentKickers[i]);
                    if (!localWinner) decisive = winnerKickers[i];
                    break;
                }
            }
        }
        return new(winnerId, outlined, bright, decisive);
    }

    // Keep the first equal-strength subset: public slots left-to-right, then hole slots.
    // This is only a stable visual choice; suits never resolve a showdown tie.
    public static int[] SelectBest(IReadOnlyList<PokerBoardCard> board, IReadOnlyList<int> holes)
    {
        int[] cards = board.Select(c => c.Card).Concat(holes).ToArray();
        if (board.Count != 5 || holes.Count != 2) throw new ArgumentException("Five board and two hole cards required.");
        int[] best = Array.Empty<int>();
        PokerHandValue bestValue = default;
        Span<int> candidate = stackalloc int[5];
        for (int a = 0; a < 3; a++)
        for (int b = a + 1; b < 4; b++)
        for (int c = b + 1; c < 5; c++)
        for (int d = c + 1; d < 6; d++)
        for (int e = d + 1; e < 7; e++)
        {
            candidate[0] = cards[a]; candidate[1] = cards[b]; candidate[2] = cards[c];
            candidate[3] = cards[d]; candidate[4] = cards[e];
            var value = PokerHandEvaluator.EvaluateFive(candidate);
            if (best.Length != 0 && value.CompareTo(bestValue) <= 0) continue;
            bestValue = value;
            best = candidate.ToArray();
        }
        return best;
    }

    public static int Rank(int card) => card % 13 == 0 ? 14 : card % 13 + 1;
    private static bool HasKickers(EHandRank rank) => rank is EHandRank.OnePair or EHandRank.TwoPair
        or EHandRank.ThreeOfAKind or EHandRank.FourOfAKind;
    private static HashSet<int> Core(int[] best, EHandRank rank, bool full)
    {
        if (full || !HasKickers(rank)) return best.ToHashSet();
        int count = rank == EHandRank.ThreeOfAKind ? 3 : rank == EHandRank.FourOfAKind ? 4 : 2;
        var ranks = best.GroupBy(Rank).Where(g => g.Count() == count).Select(g => g.Key).ToHashSet();
        return best.Where(c => ranks.Contains(Rank(c))).ToHashSet();
    }
}

// One pass through winners in this hand's action order. No accounting or scene timers.
public sealed class PokerShowdownSequence
{
    public const double RevealSeconds = 0.35;
    public const double WinnerSeconds = 2.4;
    private Guid _session;
    private long _hand = -1;
    private int _viewer;
    private PokerShowdownEmphasis[] _steps = Array.Empty<PokerShowdownEmphasis>();
    private double _elapsed;
    public int Index { get; private set; }
    public long Cue { get; private set; }
    public PokerShowdownEmphasis? Current => _steps.Length == 0 ? null : _steps[Index];
    public double StepSeconds => Math.Max(0, _elapsed - RevealSeconds - Index * WinnerSeconds);
    public bool Revealed => _elapsed >= RevealSeconds;
    public bool Finished => _steps.Length > 0 && _elapsed >= RevealSeconds + _steps.Length * WinnerSeconds;

    public void Update(PokerPlayerView view, double delta)
    {
        var state = view.Public;
        if (_session != state.SessionId || _hand != state.HandNumber || _viewer != view.PlayerId || state.Phase != PokerPhase.Settled)
        {
            _session = state.SessionId; _hand = state.HandNumber; _viewer = view.PlayerId;
            _steps = Array.Empty<PokerShowdownEmphasis>(); _elapsed = 0; Index = 0;
        }
        if (state.Phase != PokerPhase.Settled || state.Settlement == null) return;
        if (_steps.Length == 0)
        {
            _steps = state.ActionOrder.Where(state.Settlement.Winners.Contains)
                .Select(id => PokerShowdownPresentation.Create(state, view.PlayerId, id)!).ToArray();
            Cue++;
            return; // Give a newly received reveal a frame even after a long background stall.
        }
        if (!double.IsFinite(delta) || delta <= 0) return;
        _elapsed = Math.Min(RevealSeconds + _steps.Length * WinnerSeconds, _elapsed + Math.Min(delta, 0.1));
        int index = Math.Clamp((int)((_elapsed - RevealSeconds) / WinnerSeconds), 0, _steps.Length - 1);
        if (Index != index) { Index = index; Cue++; }
    }
}
#endif
