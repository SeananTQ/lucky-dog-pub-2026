#if !DEMO_BUILD && !RECORDING_BUILD
#nullable enable
using System;
using System.Linq;

namespace LuckyDogRise.Rooms.Poker;

// Recovery data is deliberately separate from the projection consumed by UI. This casual
// ruleset trusts room members with hidden information; it is not an anti-cheat mechanism.
public sealed record PokerPrivateSeat(int PlayerId, int[] Cards);
public sealed record PokerMatchSnapshot(PokerPublicView Public, int[] Deck, int DeckIndex,
    PokerPrivateSeat[] Hands, double Clock);
public sealed record PokerBankrollReserve(string Key, int Id, long Chips, int SeatNumber);
public sealed record PokerRecoverySnapshot(string Room, string Owner, string OwnerTicket, Guid Epoch,
    Guid Table, long Version, PokerTableStatus Status, PokerParticipant[] Players,
    PokerParticipant[] RetiredCompanions, PokerParticipant[] DepartedPlayers,
    PokerBankrollReserve[] Reserves, PokerMatchSnapshot? Match, int NextPlayerId,
    PokerContinuation Continuation, double DealRemaining, double SettlementElapsed,
    double CollectionRemaining, bool RestartRequired, string Notice);

public sealed partial class MischiefPokerMatch
{
    public PokerMatchSnapshot ExportRecovery() => new(GetPublicView(), (int[])_deck.Clone(), _deckIndex,
        _players.Select((id, i) => new PokerPrivateSeat(id, (int[])_holes[i].Clone())).ToArray(), _now);

    // Validate the full deck and its dealing history before constructing a usable authority.
    // Import never re-applies antes, awards or changes the session/hand/revision.
    public static bool TryRestore(PokerMatchSnapshot? snapshot, out MischiefPokerMatch? match)
    {
        match = null;
        try
        {
            if (snapshot?.Public is not { } h || h.Phase == PokerPhase.Ready || h.Seats.Count is < 2 or > 6
                || snapshot.Deck is not { Length: 52 } deck || deck.Any(c => c is < 0 or >= 52)
                || deck.Distinct().Count() != 52 || snapshot.Hands.Length != h.Seats.Count
                || snapshot.Hands.Select(s => s.PlayerId).Distinct().Count() != h.Seats.Count
                || !double.IsFinite(snapshot.Clock) || snapshot.Clock < 0
                || h.Phase == PokerPhase.Acting && (h.TurnDeadline == null
                    || Math.Abs(h.TurnDeadline.Value - snapshot.Clock - h.SecondsRemaining) > .001)) return false;
            var ids = h.Seats.Select(s => s.PlayerId).ToArray();
            var holes = ids.Select(id => snapshot.Hands.Single(s => s.PlayerId == id).Cards).ToArray();
            if (holes.Where((cards, i) => cards.Length != (h.ActionOrder.Contains(ids[i]) ? 2 : 0)).Any()) return false;
            int[] initial = Enumerable.Range(0, 2).SelectMany(pass => holes.Where(cards => cards.Length == 2).Select(cards => cards[pass])).ToArray();
            if (!deck.Take(initial.Length).SequenceEqual(initial)) return false;
            int index = initial.Length + 5;
            for (int slot = 0; slot < 5; slot++)
            {
                var replacement = h.Settlement?.Replacements.SingleOrDefault(r => r.Slot == slot);
                if (replacement == null)
                { if (h.Board[slot].Card != deck[initial.Length + slot]) return false; }
                else if (replacement.OldCard != deck[initial.Length + slot] || replacement.NewCard != deck[index++]) return false;
            }
            if (index != snapshot.DeckIndex) return false;
            // Reuse public-view validation without exposing these private cards to presentation.
            var peers = ids.Select(id => new PokerPeer(id.ToString(), "recovery", id, "", false)).ToArray();
            var players = ids.Select(id => new PokerParticipant(id.ToString(), "recovery", id, true, true, false)).ToArray();
            var context = new PokerRoomContext("recovery", peers[0].Key, peers[0].Key, peers);
            var view = new PokerPlayerView(h, ids[0], holes[0]);
            if (!PokerNetworkProtocol.ValidState(new(Guid.NewGuid(), 1, PokerTableStatus.Playing,
                players, view, 0, ""), context)) return false;
            if (h.CompletedTurns.Where((t, i) => t.PlayerId != h.ActionOrder[i]).Any()
                || h.Phase == PokerPhase.Acting && (h.CompletedTurns.Count >= h.ActionOrder.Count
                    || h.ActivePlayerId != h.ActionOrder[h.CompletedTurns.Count])
                || h.Phase != PokerPhase.Acting && h.CompletedTurns.Count != h.ActionOrder.Count) return false;
            if (h.Settlement is { } result)
            {
                foreach (var player in result.Players)
                {
                    var cards = snapshot.Hands.Single(s => s.PlayerId == player.PlayerId).Cards;
                    var best = PokerHandEvaluator.EvaluateSeven(h.Board.Select(b => b.Card).Concat(cards).ToArray());
                    if (!cards.SequenceEqual(player.HoleCards) || best.Value != player.BestHand.Value
                        || !best.Cards.SequenceEqual(player.BestHand.Cards)) return false;
                }
                var bestValue = result.Players.Select(p => p.BestHand.Value).Max();
                if (!result.Players.Where(p => p.BestHand.Value == bestValue).Select(p => p.PlayerId)
                    .Order().SequenceEqual(result.Winners.Order())) return false;
            }
            var restored = new MischiefPokerMatch(ids, h.Dealer.PlayerId)
            {
                SessionId = h.SessionId, _seatNumbers = h.Seats.Select(s => s.SeatNumber).ToArray(),
                _chips = h.Seats.Select(s => s.Chips).ToArray(), _deck = (int[])deck.Clone(),
                _deckIndex = index, _holes = holes.Select(cards => (int[])cards.Clone()).ToArray(),
                _board = h.Board.Select(b => b.Card).ToArray(), _order = h.ActionOrder.Select(id => Array.IndexOf(ids, id)).ToArray(),
                _turnIndex = h.CompletedTurns.Count, _flipsUsed = h.Phase == PokerPhase.Acting ? 2 - h.FlipsRemaining : 0,
                _now = snapshot.Clock, _deadline = h.TurnDeadline ?? snapshot.Clock,
                _handNumber = h.HandNumber, _revision = h.Revision, _pot = h.Pot, _systemTips = h.TotalSystemTips,
                _phase = h.Phase, _dealer = h.Dealer, _settlement = h.Settlement
            };
            for (int i = 0; i < 5; i++) restored._pending[i] = h.Board[i].PendingReplacement;
            restored._completedTurns.AddRange(h.CompletedTurns); restored._flips.AddRange(h.Flips);
            match = restored; return true;
        }
        catch (Exception e) when (e is NullReferenceException or ArgumentException or InvalidOperationException or IndexOutOfRangeException)
        { return false; }
    }
}
#endif
