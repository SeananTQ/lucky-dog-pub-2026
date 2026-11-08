#if !DEMO_BUILD && !RECORDING_BUILD
using System.Collections.Generic;
using System.Linq;
using Godot;
using LuckyDogRise.Rooms.Poker;

namespace LuckyDogRise.Rooms;

// Hole cards plus independent dealer, action, winner and presence states.
public partial class RoomPokerHandView : Control
{
    private static readonly double TurnSeconds = new MischiefPokerRules().TurnSeconds;
    [Export] private RoomPokerCardView _first, _second;
    [Export] private Panel _plate;
    [Export] private Label _seat, _chips, _dealer, _rank;
    [Export] private TextureRect _winner, _waiting, _autoplay;
    [Export] private ProgressBar _clock;
    public bool CardsRevealed => _first.Visible && _second.Visible && _first.VisibleCard >= 0 && _second.VisibleCard >= 0;
    public bool IsWinner => _winner.Visible;
    public bool IsWaiting => _waiting.Visible;
    public bool IsAutoplay => _autoplay.Visible;
    public bool IsActing => _clock.Visible;
    public RoomPokerCardView FirstCard => _first;
    public RoomPokerCardView SecondCard => _second;
    public Vector2 AwardAnchor => _plate.GetGlobalRect().GetCenter();
    private System.Guid _session;
    private long _hand = -1;

    public void Present(PokerPublicView state, int playerId, IReadOnlyList<int> cards,
        PokerShowdownEmphasis emphasis, long cue, bool animate, bool waiting = false, bool autoplay = false)
    {
        Show();
        var seat = state.Seats.FirstOrDefault(s => s.PlayerId == playerId);
        waiting |= seat == null;
        bool sameHand = _session == state.SessionId && _hand == state.HandNumber;
        _session = state.SessionId; _hand = state.HandNumber;
        bool won = !waiting && state.Settlement?.Winners.Contains(playerId) == true;
        bool active = !waiting && state.Phase == PokerPhase.Acting && state.ActivePlayerId == playerId;
        _first.Visible = _second.Visible = !waiting;
        _waiting.Visible = waiting;
        _autoplay.Visible = autoplay && !waiting;
        _winner.Visible = won;
        _dealer.Visible = !waiting && state.Dealer.PlayerId == playerId;
        _clock.Visible = active;
        _clock.Value = active ? 100 * state.SecondsRemaining / TurnSeconds : 0;
        _seat.Text = waiting ? "—" : $"#{seat.SeatNumber}";
        _chips.Text = waiting ? "—" : seat.Chips.ToString("N0");
        _dealer.TooltipText = RoomPokerText.T("本局庄家", "Dealer");
        _winner.TooltipText = RoomPokerText.T("本局胜者", "Hand winner");
        _waiting.TooltipText = RoomPokerText.T("等待入局", "Waiting to join a hand");
        _autoplay.TooltipText = RoomPokerText.T("掉线托管", "Disconnected · Autoplay");
        _clock.TooltipText = RoomPokerText.T("当前行动者 · 剩余时间", "Current player · Time remaining");
        var result = state.Settlement?.Players.FirstOrDefault(p => p.PlayerId == playerId);
        _rank.Text = result != null && !waiting ? RoomPokerText.Rank(result.BestHand.Value.Rank) : "";
        _plate.ThemeTypeVariation = won
            ? emphasis?.WinnerId == playerId ? "RoomPokerWinnerFocus" : "RoomPokerWinner"
            : active ? "RoomPokerActive" : "RoomPokerPlate";
        if (waiting) cards = null;
        PresentCard(_first, cards?.Count == 2 ? cards[0] : -1);
        PresentCard(_second, cards?.Count == 2 ? cards[1] : -1);
        Modulate = Colors.White;

        void PresentCard(RoomPokerCardView card, int value)
        {
            card.Present(value, value >= 0, animate && sameHand);
            card.SetEmphasis(emphasis != null && !emphasis.WinnerCards.Contains(value),
                !waiting && emphasis?.LocalCards.Contains(value) == true,
                !waiting && emphasis?.DecisiveCard == value, cue, animate);
        }
    }
}
#endif
