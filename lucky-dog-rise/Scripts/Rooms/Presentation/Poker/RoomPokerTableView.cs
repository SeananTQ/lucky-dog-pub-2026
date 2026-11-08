#if !DEMO_BUILD && !RECORDING_BUILD
using System;
using System.Linq;
using Godot;
using LuckyDogRise.Rooms.Poker;

namespace LuckyDogRise.Rooms;

// Receives only a player projection. The authority, opponents' hidden cards and remaining deck stay outside the UI.
public partial class RoomPokerTableView : Node2D
{
    [Export] private Godot.Collections.Array<RoomPokerCardView> _board = new();
    [Export] private RoomPokerHandView _hand;
    [Export] private Label _turn;
    public event Action<int> FlipRequested;
    private long _handNumber = -1;
    private Guid _session;
#if DEBUG
    // Code-only opt-out for state/logic smoke tests; ordinary Mock play still animates.
    internal static bool SkipDealAnimationForTests { get; set; }
    internal static bool DealAnimationsEnabled => !SkipDealAnimationForTests;
#else
    internal static bool DealAnimationsEnabled => true;
#endif
    public bool IsDealing => _hand.IsDealing || _board.Any(card => card.IsDealing);
    public PokerShowdownSequence Showdown { get; } = new();
    public RoomPokerHandView LocalPanel => _hand;
    public PokerShowdownEmphasis Emphasis => Showdown.Revealed ? Showdown.Current : null;
    public override void _Ready()
    {
        for (int i = 0; i < _board.Count; i++)
        {
            int slot = i;
            _board[i].Pressed += () => FlipRequested?.Invoke(slot);
        }
    }
    public void Present(PokerPlayerView view, bool canAct, double presentationDelta = 0, bool animate = true)
    {
        Showdown.Update(view, presentationDelta);
        var state = view.Public;
        bool sameHand = _session == state.SessionId && _handNumber == state.HandNumber;
        animate &= IsVisibleInTree();
        _hand.Present(state, view.PlayerId, view.HoleCards, Emphasis, Showdown.Cue, animate);
        bool deal = !sameHand && state.Phase == PokerPhase.Acting && animate && DealAnimationsEnabled;
        float pitchStart = deal ? Mathf.Lerp(0.98f, 1f, GD.Randf()) : 1f;
        for (int i = 0; i < _board.Count; i++)
        {
            if (!sameHand || !animate || state.Phase != PokerPhase.Acting) _board[i].StopDealAnimation();
            _board[i].Present(state.Board[i].Card, !state.Board[i].PendingReplacement, animate && sameHand);
            int card = state.Board[i].Card;
            _board[i].SetEmphasis(Emphasis != null && !Emphasis.WinnerCards.Contains(card),
                Emphasis?.LocalCards.Contains(card) == true, Emphasis?.DecisiveCard == card,
                Showdown.Cue, animate);
            if (deal) _board[i].AnimateDeal(RoomPokerHandView.DealDuration + RoomPokerHandView.RemoteDealDelay + i * 0.2,
                pitchStart + i * 0.015f);
        }
        _session = state.SessionId;
        _handNumber = state.HandNumber;
        foreach (var card in _board) card.Disabled = !canAct || IsDealing;
        _turn.Text = IsDealing ? RoomPokerText.T("发牌中…", "Dealing…") : state.Phase == PokerPhase.Acting
            ? state.ActivePlayerId == view.PlayerId
                ? RoomPokerText.T($"轮到你 · 还可翻 {state.FlipsRemaining} 次 · {Math.Ceiling(state.SecondsRemaining):0} 秒", $"Your turn · {state.FlipsRemaining} flips · {Math.Ceiling(state.SecondsRemaining):0}s")
                : RoomPokerText.T($"等待 {state.Seats.Single(s => s.PlayerId == state.ActivePlayerId).SeatNumber} 号操作", $"Waiting for seat {state.Seats.Single(s => s.PlayerId == state.ActivePlayerId).SeatNumber}")
            : state.Phase == PokerPhase.AwaitingDraw ? RoomPokerText.T("补牌中…", "Replacing cards…")
            : RoomPokerText.T("本局结束 · 可查看各自手牌", "Showdown · All hands revealed");
    }
}

// Preview copy only: Chinese and English fallback, pending final product terminology/localization.
public static class RoomPokerText
{
    public static string T(string chinese, string english) => TranslationServer.GetLocale().StartsWith("zh", StringComparison.OrdinalIgnoreCase) ? chinese : english;
    public static string Rank(DataTables.EHandRank rank) => rank switch
    {
        DataTables.EHandRank.Nothing => T("高牌", "High card"),
        DataTables.EHandRank.OnePair => T("一对", "One pair"),
        DataTables.EHandRank.TwoPair => T("两对", "Two pair"),
        DataTables.EHandRank.ThreeOfAKind => T("三条", "Three of a kind"),
        DataTables.EHandRank.Straight => T("顺子", "Straight"),
        DataTables.EHandRank.Flush => T("同花", "Flush"),
        DataTables.EHandRank.FullHouse => T("葫芦", "Full house"),
        DataTables.EHandRank.FourOfAKind => T("四条", "Four of a kind"),
        DataTables.EHandRank.StraightFlush => T("同花顺", "Straight flush"),
        _ => T("皇家同花顺", "Royal flush")
    };
}
#endif
