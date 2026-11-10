#if !DEMO_BUILD && !RECORDING_BUILD
#nullable enable
using System;
using System.Linq;
using System.Text.Json;

namespace LuckyDogRise.Rooms.Poker;

// Keys identify a platform member; tickets identify this particular room membership.
// LocalId is presentation-only and is deliberately never used to authenticate a sender.
public sealed record PokerPeer(string Key, string Ticket, int LocalId, string Name, bool Companion, bool ReadyOnEntry = false);
public sealed record PokerRoomContext(string Room, string LocalKey, string OwnerKey, PokerPeer[] Members, string Game = "mischief");
public interface IPokerLink : IDisposable
{
    PokerRoomContext? Context { get; }
    event Action<string, byte[]> Received;
    bool Send(string recipient, byte[] payload);
    void Poll();
}
public enum PokerTableStatus { Idle, Preparing, Playing, Interrupted }
public enum PokerContinuation { Automatic, PauseRequested, Paused, Collecting }
public sealed record PokerParticipant(string Key, string Ticket, int Id, bool Ready, bool Playing, bool Companion);
public sealed record PokerNetworkState(Guid Table, long Version, PokerTableStatus Status,
    PokerParticipant[] Players, PokerPlayerView? Hand, double DealRemaining, string Notice, long AckSequence = 0,
    PokerContinuation Continuation = PokerContinuation.Automatic, double SettlementElapsed = 0);
public sealed record PokerPacket(int Protocol, string Room, string SenderTicket, string RecipientTicket,
    string Kind, long Sequence, Guid Table, bool Ready = false, PokerCommand? Command = null,
    long FinishedHand = 0, PokerNetworkState? State = null);

public static class PokerNetworkProtocol
{
    public const int Version = 2;
    public const int MaxBytes = 32768;
    private static readonly JsonSerializerOptions Options = new() { MaxDepth = 24 };
    public static byte[] Encode(PokerPacket packet) => JsonSerializer.SerializeToUtf8Bytes(packet, Options);
    public static PokerPacket? Decode(byte[] bytes)
    {
        if (bytes.Length is < 2 or > MaxBytes) return null;
        try
        {
            var p = JsonSerializer.Deserialize<PokerPacket>(bytes, Options);
            return p is { Protocol: Version, Sequence: > 0 } && p.Room is { Length: > 0 and <= 80 }
                && p.SenderTicket is { Length: > 0 and <= 80 } && p.RecipientTicket is { Length: > 0 and <= 80 }
                && p.Kind is "state" or "hello" or "prepare" or "ready" or "start" or "action" or "finished" or "stop" or "pause" or "resume" ? p : null;
        }
        catch (JsonException) { return null; }
        catch (NotSupportedException) { return null; }
    }

    public static bool ValidState(PokerNetworkState s, PokerRoomContext room)
    {
        try
        {
            if (!Enum.IsDefined(s.Status) || s.Version <= 0 || s.Players == null || s.Players.Length > 6
                || s.Players.Any(p => p == null || p.Id is < 1 or > 6 || p.Key is not { Length: > 0 and <= 80 }
                    || p.Ticket is not { Length: > 0 and <= 80 })
                || s.Players.Select(p => p.Id).Distinct().Count() != s.Players.Length
                || s.Players.Select(p => p.Key).Distinct().Count() != s.Players.Length
                || !double.IsFinite(s.DealRemaining) || s.DealRemaining is < 0 or > 10
                || s.Notice == null || s.Notice.Length > 100 || s.AckSequence < 0
                || !Enum.IsDefined(s.Continuation) || !double.IsFinite(s.SettlementElapsed)
                || s.SettlementElapsed < 0 || s.SettlementElapsed > 86400) return false;
            if (s.Status is PokerTableStatus.Playing or PokerTableStatus.Preparing && s.Table == Guid.Empty) return false;
            if (s.Status != PokerTableStatus.Interrupted && s.Players.Any(p => !room.Members.Any(m =>
                m.Key == p.Key && m.Ticket == p.Ticket && m.Companion == p.Companion))) return false;
            var v = s.Hand;
            if (v == null) return s.Status != PokerTableStatus.Playing;
            if (s.Status != PokerTableStatus.Playing) return false;
            var h = v.Public;
            var ids = s.Players.Where(p => p.Playing).Select(p => p.Id).ToArray();
            var self = s.Players.FirstOrDefault(p => p.Key == room.LocalKey);
            if (h == null || !Enum.IsDefined(h.Phase) || h.Phase == PokerPhase.Ready || h.SessionId == Guid.Empty
                || h.HandNumber < 1 || h.Revision < 1 || h.Seats.Count is < 2 or > 6
                || h.Seats.Select(p => p.PlayerId).Distinct().Count() != ids.Length
                || h.Seats.Count != ids.Length || h.Seats.Select(p => p.SeatNumber).Distinct().Count() != ids.Length
                || h.Seats.Any(p => !ids.Contains(p.PlayerId) || p.Chips < 0 || p.Chips > 6000 || p.SeatNumber is < 1 or > 6)
                || h.Board.Count != 5 || h.Board.Any(c => c.Card is < 0 or >= 52)
                || h.Board.Select(c => c.Card).Distinct().Count() != 5
                || h.ActionOrder.Count != ids.Length || h.ActionOrder.Distinct().Count() != ids.Length
                || h.ActionOrder.Any(id => !ids.Contains(id)) || !ValidDealer(h.Dealer, ids)
                || h.CompletedTurns.Count > ids.Length || h.CompletedTurns.Select(t => t.PlayerId).Distinct().Count() != h.CompletedTurns.Count
                || h.CompletedTurns.Any(t => !ids.Contains(t.PlayerId) || t.FlipsUsed is < 0 or > 2 || !Enum.IsDefined(t.Reason))
                || !double.IsFinite(h.SecondsRemaining) || h.SecondsRemaining is < 0 or > 15
                || h.TurnDeadline.HasValue && !double.IsFinite(h.TurnDeadline.Value)
                || h.Pot is < 0 or > 600 || h.TotalSystemTips is < 0 or > 6000
                || h.Phase == PokerPhase.Acting && (!h.ActivePlayerId.HasValue || !ids.Contains(h.ActivePlayerId.Value))
                || h.FlipsRemaining is < 0 or > 2) return false;
            if (self?.Playing == true)
            {
                if (v.PlayerId != self.Id || v.HoleCards.Count != 2 || v.HoleCards.Any(c => c is < 0 or >= 52)
                    || v.HoleCards.Concat(h.Board.Select(b => b.Card)).Distinct().Count() != 7) return false;
            }
            else if (v.PlayerId != 0 || v.HoleCards.Count != 0) return false;
            if (h.Phase != PokerPhase.Settled) return h.Settlement == null;
            var r = h.Settlement;
            return r != null && r.Players.Count == ids.Length && r.Winners.Count is >= 1 and <= 6
                && r.Players.Select(p => p.PlayerId).Distinct().Count() == ids.Length
                && r.Winners.Distinct().Count() == r.Winners.Count && r.Winners.All(ids.Contains)
                && ValidDealer(r.NextDealer, ids) && Enum.IsDefined(r.DealerChange)
                && r.Pot == ids.Length * 100 && r.SystemTip >= 0 && r.SystemTip < r.Winners.Count
                && r.Players.Sum(p => p.Award) + r.SystemTip == r.Pot
                && r.Players.All(p => ids.Contains(p.PlayerId) && p.HoleCards.Count == 2
                    && p.HoleCards.All(c => c is >= 0 and < 52) && p.BestHand.Cards.Count == 5
                    && p.BestHand.Cards.Distinct().Count() == 5
                    && p.BestHand.Cards.All(c => p.HoleCards.Contains(c) || h.Board.Any(b => b.Card == c))
                    && Enum.IsDefined(p.BestHand.Value.Rank) && p.BestHand.Value.TieBreak >= 0
                    && p.Award == (r.Winners.Contains(p.PlayerId) ? r.Pot / r.Winners.Count : 0))
                && r.Players.SelectMany(p => p.HoleCards).Concat(h.Board.Select(b => b.Card)).Distinct().Count() == ids.Length * 2 + 5
                && (self?.Playing != true || r.Players.Single(p => p.PlayerId == self.Id).HoleCards.SequenceEqual(v.HoleCards))
                && r.Replacements.Count <= 5 && r.Replacements.Select(p => p.Slot).Distinct().Count() == r.Replacements.Count
                && r.Replacements.All(p => p.Slot is >= 0 and < 5 && p.OldCard is >= 0 and < 52
                    && p.NewCard == h.Board[p.Slot].Card);
        }
        catch (Exception e) when (e is NullReferenceException or ArgumentException or InvalidOperationException) { return false; }
    }
    private static bool ValidDealer(PokerDealer d, int[] ids) => d != null && ids.Contains(d.PlayerId)
        && d.ConsecutiveHands is >= 1 and <= 4 && Enum.IsDefined(d.Direction);

    // Rebind canonical seat IDs to the viewer's existing dog IDs, without changing seat numbers.
    public static PokerPlayerView? ForDisplay(PokerNetworkState? state, PokerRoomContext? room)
    {
        if (state?.Hand is not { } v || room == null) return null;
        int Id(int id)
        {
            var p = state.Players.FirstOrDefault(p => p.Id == id);
            return room.Members.FirstOrDefault(m => m.Key == p?.Key && m.Ticket == p.Ticket)?.LocalId ?? int.MinValue + id;
        }
        var h = v.Public;
        PokerDealer Dealer(PokerDealer d) => d with { PlayerId = Id(d.PlayerId) };
        var r = h.Settlement;
        if (r != null) r = r with
        {
            Winners = r.Winners.Select(Id).ToArray(), NextDealer = Dealer(r.NextDealer),
            Players = r.Players.Select(p => p with { PlayerId = Id(p.PlayerId) }).ToArray()
        };
        return v with { PlayerId = Id(v.PlayerId), Public = h with
        {
            Seats = h.Seats.Select(p => p with { PlayerId = Id(p.PlayerId) }).ToArray(),
            Dealer = Dealer(h.Dealer), ActionOrder = h.ActionOrder.Select(Id).ToArray(),
            ActivePlayerId = h.ActivePlayerId is int id ? Id(id) : null,
            CompletedTurns = h.CompletedTurns.Select(t => t with { PlayerId = Id(t.PlayerId) }).ToArray(), Settlement = r
        } };
    }
}
#endif
