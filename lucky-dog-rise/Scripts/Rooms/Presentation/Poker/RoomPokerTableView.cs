#if !DEMO_BUILD && !RECORDING_BUILD
using System;
using System.Linq;
using Godot;
using DataTables;
using LuckyDogRise.Rooms.Poker;

namespace LuckyDogRise.Rooms;

// Receives only a player projection. The authority, opponents' hidden cards and remaining deck stay outside the UI.
public partial class RoomPokerTableView : Node2D
{
    [Export] private Godot.Collections.Array<RoomPokerCardView> _board = new();
    [Export] private RoomPokerHandView _hand;
    [Export] private Label _turn;
    [Export] private RoomPokerConnectionOverlay _connectionOverlay;
    private bool _connectionBlocking, _failureShown;
    public bool LocalAutoplay { get; set; }
    [Export] public PokerPawEffects Paws { get; private set; }
    private Func<int, bool, Node2D> _pawSource;

    public void BindPaws(Viewport viewport, Func<int, bool, Node2D> source, Func<CanvasItem, Transform2D> toWindow)
    {
        _pawSource = source;
        Paws.Bind(viewport, toWindow);
    }
    public event Action<int> FlipRequested;
    private long _handNumber = -1;
    private Guid _session;
    private long _lastFlip;
    private bool _wasAnimated;
    private PokerFlip _predicted;
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

    internal EDogReactionTrigger? DogReactionFor(int playerId)
    {
        var state = Round.DisplayView?.Public;
        // Waiting/sitting-out members keep their own desktop activity, not a losing pose.
        if (state == null || !state.ActionOrder.Contains(playerId)) return null;
        if (!Round.WinnersVisible || state.Settlement is not { } result) return EDogReactionTrigger.Default;
        if (!result.Winners.Contains(playerId)) return EDogReactionTrigger.Bored;
        var hand = result.Players.FirstOrDefault(p => p.PlayerId == playerId);
        return result.Winners.Count == 1 && hand?.BestHand.Value.Rank >= EHandRank.FullHouse
            ? EDogReactionTrigger.Starstruck : EDogReactionTrigger.Excited;
    }
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
        Paws.Cancel(); _wasAnimated = false; _predicted = null;
        foreach (var card in _board) { card.Disabled = true; card.Hide(); }
        _hand.PresentWaiting();
        _turn.Text = "";
    }
    public void PresentConnection(PokerNetworkSession session)
    {
        bool blocked = session.IsMigrating || !session.Connected;
        if (blocked)
            ShowConnectionWait(session.IsMigrating);
        else
        {
            if (_connectionBlocking) { _connectionOverlay.Hide(); _connectionBlocking = false; }
            if (!_failureShown && session.State?.Notice == "recovery_failed")
            {
                _failureShown = true;
                _connectionOverlay.ShowFailure();
            }
        }
    }

    // The network session owns recovery timing; the overlay only presents its current state.
    internal void ShowConnectionWait(bool migrating)
    {
        _connectionOverlay.ShowWaiting(migrating);
        _connectionBlocking = true;
    }
    public void Present(PokerPlayerView view, bool canAct, double presentationDelta = 0, bool animate = true,
        bool otherCardsMoving = false, bool actionPending = false)
    {
        Round.Update(view, presentationDelta, Paws.IsTouching || otherCardsMoving || _hand.IsMoving || _board.Any(card => card.IsMoving));
        view = Round.DisplayView;
        var state = view.Public;
        bool sameHand = _session == state.SessionId && _handNumber == state.HandNumber;
        animate &= IsVisibleInTree();
        if (!sameHand || !animate) Paws.Cancel();
        var prediction = _predicted;
        bool confirmed = prediction != null && state.Flips.Contains(prediction);
        if (prediction != null && (!sameHand || !animate || confirmed || !actionPending))
        {
            _predicted = null;
            if (!confirmed)
            {
                // A rejected/stale command only rolls back the local cosmetic prediction.
                Paws.Cancel();
                _board[prediction.Slot].ResetPose();
                _board[prediction.Slot].Present(state.Board[prediction.Slot].Card,
                    !state.Board[prediction.Slot].PendingReplacement, false);
            }
        }
        if (sameHand && animate && _wasAnimated && Round.ActionResultsVisible)
            foreach (var flip in state.Flips.Where(f => f.Revision > _lastFlip))
            {
                if (flip != prediction) PlayFlip(flip, view);
            }
        _lastFlip = state.Flips.LastOrDefault()?.Revision ?? 0;
        _wasAnimated = animate;
        _hand.Present(state, view.PlayerId, view.HoleCards, Emphasis, Showdown.Cue, animate, autoplay: LocalAutoplay, round: Round);
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
            if (_predicted?.Slot != i && !Paws.HasPendingFlip(_board[i].PushAnimation))
                _board[i].Present(state.Board[i].Card, !state.Board[i].PendingReplacement, animate && sameHand);
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

    // Start only after the transport accepts a local request. The authority still decides
    // legality; an ACK suppresses replay, while rejection restores the authoritative board.
    public void PredictLocalFlip(int slot)
    {
        var view = Round.DisplayView;
        if (_predicted != null || view == null || slot is < 0 or >= 5 || !IsVisibleInTree()
            || Round.Stage != PokerRoundStage.Acting || view.Public.ActivePlayerId != view.PlayerId) return;
        _predicted = new(view.Public.Revision + 1, view.PlayerId,
            3 - view.Public.FlipsRemaining, slot, view.Public.Board[slot].PendingReplacement);
        PlayFlip(_predicted, view);
    }

    private void PlayFlip(PokerFlip flip, PokerPlayerView view)
    {
        var node = _board[flip.Slot];
        int value = view.Public.Board[flip.Slot].Card, order = ++_topCard;
        float angle = node.PushAnimation.ChooseTiltAngle();
        bool local = flip.PlayerId == view.PlayerId;
        Paws.Play(_pawSource?.Invoke(flip.PlayerId, flip.Number == 2), node.PushAnimation, angle, direction =>
        {
            node.Present(value, flip.FaceUp, true, direction);
            node.AnimateMischief(order, angle);
        }, immediate: local, keepOriginalSize: local);
    }
}

// Unconfirmed preview copy uses T; confirmed auto-chat copy and ranks use the language catalogs.
public static class RoomPokerText
{
    public static string AutoChat(PokerAutoChatLine line) => line.Kind switch
    {
        PokerAutoChatKind.Win => string.Format(InGameRoomPreview.ChatText("Rooms_AutoChat_Win"), Rank(line.Rank)),
        PokerAutoChatKind.Tie => InGameRoomPreview.ChatText("Rooms_AutoChat_Tie"),
        PokerAutoChatKind.CloseLoss => InGameRoomPreview.ChatText("Rooms_AutoChat_CloseLoss"),
        _ => InGameRoomPreview.ChatText("Rooms_AutoChat_NextHand")
    };
    public static string T(string chinese, string english) => TranslationServer.GetLocale().StartsWith("zh", StringComparison.OrdinalIgnoreCase) ? chinese : english;
    public static string Rank(DataTables.EHandRank rank) => rank == DataTables.EHandRank.Nothing
        ? InGameRoomPreview.ChatText("Rooms_Poker_HighCard")
        : L10n.Tr(L10n.GetHandRankKey(rank));
}
#endif
