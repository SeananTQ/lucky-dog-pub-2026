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
    private PokerBoardCard[] _lastBoard = Array.Empty<PokerBoardCard>();
    private PokerRoundStage _stage;
    private int _topCard;
#if DEBUG
    // Code-only opt-out for state/logic smoke tests; ordinary Mock play still animates.
    internal static bool SkipDealAnimationForTests { get; set; }
    internal static bool DealAnimationsEnabled => !SkipDealAnimationForTests;
#else
    internal static bool DealAnimationsEnabled => true;
#endif
    public bool IsDealing => _hand.IsDealing || _board.Any(card => card.IsDealing);
    public PokerRoundPresentation Round { get; } = new();
    public PokerShowdownSequence Showdown => Round.Showdown;
    public RoomPokerHandView LocalPanel => _hand;
    public PokerShowdownEmphasis Emphasis => Round.WinnersVisible ? Showdown.Current : null;
    public override void _Ready()
    {
        for (int i = 0; i < _board.Count; i++)
        {
            int slot = i;
            _board[i].Pressed += () => FlipRequested?.Invoke(slot);
        }
    }
    public void PresentWaiting()
    {
        foreach (var card in _board) { card.Disabled = true; card.Hide(); }
        _hand.PresentWaiting();
        _turn.Text = "";
    }
    public void Present(PokerPlayerView view, bool canAct, double presentationDelta = 0, bool animate = true,
        bool otherCardsMoving = false)
    {
        Round.Update(view, presentationDelta, otherCardsMoving || _hand.IsMoving || _board.Any(card => card.IsMoving));
        view = Round.DisplayView;
        var state = view.Public;
        bool sameHand = _session == state.SessionId && _handNumber == state.HandNumber;
        animate &= IsVisibleInTree();
        _hand.Present(state, view.PlayerId, view.HoleCards, Emphasis, Showdown.Cue, animate, round: Round);
        bool deal = !sameHand && state.Phase == PokerPhase.Acting && animate && DealAnimationsEnabled;
        if (!sameHand) _topCard = 0;
        float pitchStart = deal ? Mathf.Lerp(0.98f, 1f, GD.Randf()) : 1f;
        bool straighten = Round.Stage == PokerRoundStage.Straightening && _stage != Round.Stage;
        bool collect = Round.Stage == PokerRoundStage.Collecting && _stage != Round.Stage;
        bool restore = Round.Stage == PokerRoundStage.AwaitingNext && _stage >= PokerRoundStage.Collecting;
        for (int i = 0; i < _board.Count; i++)
        {
            _board[i].Show();
            if (!sameHand || restore)
            {
                _board[i].ResetPose();
                _board[i].GetParent().MoveChild(_board[i], -1);
            }
            if (!sameHand || !animate || state.Phase != PokerPhase.Acting) _board[i].StopDealAnimation();
            _board[i].Present(state.Board[i].Card, !state.Board[i].PendingReplacement, animate && sameHand);
            if (sameHand && animate && Round.ActionResultsVisible && _lastBoard.Length == _board.Count
                && _lastBoard[i].PendingReplacement != state.Board[i].PendingReplacement)
                _board[i].AnimateMischief(++_topCard);
            int card = state.Board[i].Card;
            _board[i].SetEmphasis(Emphasis != null && !Emphasis.WinnerCards.Contains(card),
                Emphasis?.LocalCards.Contains(card) == true, Emphasis?.DecisiveCard == card,
                Showdown.Cue, animate);
            if (deal) _board[i].AnimateDeal(RoomPokerHandView.DealDuration + RoomPokerHandView.RemoteDealDelay + i * 0.2,
                pitchStart + i * 0.015f);
            if (straighten) _board[i].AnimateStraighten(i);
            if (collect) _board[i].AnimateCollect(i);
        }
        if (straighten && animate) AudioManager.Instance?.PlaySfxOncePerFrame("Card_PokerBoardStraighten", "RoomPokerStraighten");
        _session = state.SessionId;
        _handNumber = state.HandNumber;
        _lastBoard = state.Board.ToArray();
        _stage = Round.Stage;
        foreach (var card in _board) card.Disabled = !canAct || IsDealing || Round.Stage != PokerRoundStage.Acting;
        _turn.Text = IsDealing ? RoomPokerText.T("发牌中…", "Dealing…") : state.Phase == PokerPhase.Acting
            ? state.ActivePlayerId == view.PlayerId
                ? RoomPokerText.T($"轮到你 · 还可翻 {state.FlipsRemaining} 次 · {Math.Ceiling(state.SecondsRemaining):0} 秒", $"Your turn · {state.FlipsRemaining} flips · {Math.Ceiling(state.SecondsRemaining):0}s")
                : RoomPokerText.T($"等待 {state.Seats.Single(s => s.PlayerId == state.ActivePlayerId).SeatNumber} 号操作", $"Waiting for seat {state.Seats.Single(s => s.PlayerId == state.ActivePlayerId).SeatNumber}")
            : Round.Stage switch
            {
                PokerRoundStage.FinalAction or PokerRoundStage.RetractingResults => RoomPokerText.T("本轮操作结束", "All turns completed"),
                PokerRoundStage.Replacing or PokerRoundStage.Straightening => RoomPokerText.T("补牌中…", "Completing the board…"),
                PokerRoundStage.RevealingHands => RoomPokerText.T("亮牌中…", "Revealing hands…"),
                PokerRoundStage.Collecting => RoomPokerText.T("收牌中…", "Collecting cards…"),
                PokerRoundStage.MovingDealer or PokerRoundStage.AwaitingNewHand => RoomPokerText.T("交接庄家…", "Passing the dealer button…"),
                _ => RoomPokerText.T("本局结束 · 可查看各自手牌", "Showdown · All hands revealed")
            };
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
