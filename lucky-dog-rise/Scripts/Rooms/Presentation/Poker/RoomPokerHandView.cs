#if !DEMO_BUILD && !RECORDING_BUILD
using System.Collections.Generic;
using System.Linq;
using Godot;
using LuckyDogRise.Rooms.Poker;

namespace LuckyDogRise.Rooms;

// Hole cards plus independent dealer, action, winner and presence states.
public partial class RoomPokerHandView : Control
{
    public const double DealDuration = 0.65;
    public const double RemoteDealDelay = 0.16;
    private static readonly double TurnSeconds = new MischiefPokerRules().TurnSeconds;
    [Export] private RoomPokerCardView _first, _second;
    [Export] private Panel _plate, _drawer;
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
    public Vector2 DealerAnchor => _dealer.GetGlobalRect().GetCenter();
    public bool DealerVisible => _dealer.Visible;
    public string ResultText => _rank.Text;
    public bool ResultVisible => _drawer.Visible;
    public bool IsDealing => _deal?.IsRunning() == true;
    public bool IsMoving => IsDealing || _collect?.IsRunning() == true || _first.IsMoving || _second.IsMoving;
    private System.Guid _session;
    private long _hand = -1;
    private Tween _deal, _drawerMotion, _collect;
    private bool _drawerOpen;
    private PokerRoundStage _stage;
    private (Vector2 Position, float Rotation, int Z, Color Tint) _firstPose, _secondPose;

    public override void _Ready()
    {
        _firstPose = (_first.Position, _first.Rotation, _first.ZIndex, _first.Modulate);
        _secondPose = (_second.Position, _second.Rotation, _second.ZIndex, _second.Modulate);
        _drawer.Position = new Vector2(0, -_drawer.Size.Y);
        _drawer.Hide();
        VisibilityChanged += () => { if (!IsVisibleInTree()) StopDeal(); };
    }

    public void Present(PokerPublicView state, int playerId, IReadOnlyList<int> cards,
        PokerShowdownEmphasis emphasis, long cue, bool animate, bool waiting = false, bool autoplay = false,
        double dealDelay = 0, PokerRoundPresentation round = null)
    {
        Show();
        var seat = state.Seats.FirstOrDefault(s => s.PlayerId == playerId);
        waiting |= seat == null;
        bool sameHand = _session == state.SessionId && _hand == state.HandNumber;
        if (!sameHand || waiting || !animate || state.Phase != PokerPhase.Acting) StopDeal();
        if (!sameHand)
        {
            _collect?.Kill(); _collect = null;
            _first.ResetPose(); _second.ResetPose();
            SetDrawer("", false);
        }
        _session = state.SessionId; _hand = state.HandNumber;
        bool won = !waiting && (round?.WinnersVisible ?? true) && state.Settlement?.Winners.Contains(playerId) == true;
        bool active = !waiting && state.Phase == PokerPhase.Acting && state.ActivePlayerId == playerId;
        _first.Visible = _second.Visible = !waiting;
        _waiting.Visible = waiting;
        _autoplay.Visible = autoplay && !waiting;
        _winner.Visible = won;
        _dealer.Visible = !waiting && (round == null ? state.Dealer.PlayerId : round.DealerId) == playerId;
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
        var turn = state.CompletedTurns.FirstOrDefault(t => t.PlayerId == playerId);
        string status = !waiting && result != null && (round?.RanksVisible ?? true)
            ? RoomPokerText.Rank(result.BestHand.Value.Rank)
            : !waiting && turn != null && (round?.ActionResultsVisible ?? state.Phase != PokerPhase.Settled)
                ? turn.FlipsUsed == 0 ? "PASS" : RoomPokerText.T($"翻牌 ×{turn.FlipsUsed}", $"Flips ×{turn.FlipsUsed}") : "";
        SetDrawer(status, animate && sameHand);
        _plate.ThemeTypeVariation = won
            ? emphasis?.WinnerId == playerId ? "RoomPokerWinnerFocus" : "RoomPokerWinner"
            : active ? "RoomPokerActive" : "RoomPokerPlate";
        if (waiting) cards = null;
        PresentCard(_first, cards?.Count == 2 ? cards[0] : -1);
        PresentCard(_second, cards?.Count == 2 ? cards[1] : -1);
        if (!sameHand && !waiting && state.Phase == PokerPhase.Acting && animate
            && IsVisibleInTree() && RoomPokerTableView.DealAnimationsEnabled)
            BeginDeal(dealDelay);
        if (round?.Stage == PokerRoundStage.Collecting && _stage != PokerRoundStage.Collecting && !waiting)
            BeginCollect();
        if (round?.Stage == PokerRoundStage.AwaitingNext && _stage >= PokerRoundStage.Collecting)
        {
            _collect?.Kill(); _collect = null;
            _first.ResetPose(); _second.ResetPose();
        }
        _stage = round?.Stage ?? PokerRoundStage.Acting;
        Modulate = Colors.White;

        void PresentCard(RoomPokerCardView card, int value)
        {
            card.Present(value, value >= 0, animate && sameHand);
            card.SetEmphasis(emphasis != null && !emphasis.WinnerCards.Contains(value),
                !waiting && emphasis?.LocalCards.Contains(value) == true,
                !waiting && emphasis?.DecisiveCard == value, cue, animate);
        }
    }

    private void SetDrawer(string text, bool animate)
    {
        bool open = text.Length > 0;
        if (open) _rank.Text = text;
        if (open == _drawerOpen) return;
        _drawerOpen = open;
        _drawerMotion?.Kill();
        _drawer.Show();
        float target = open ? 0 : -_drawer.Size.Y;
        if (!animate || !IsVisibleInTree())
        { _drawer.Position = new Vector2(0, target); _drawer.Visible = open; if (!open) _rank.Text = ""; return; }
        _drawerMotion = CreateTween();
        _drawerMotion.TweenProperty(_drawer, "position:y", target, PokerRoundPresentation.DrawerSeconds)
            .SetTrans(Tween.TransitionType.Cubic).SetEase(Tween.EaseType.Out);
        if (!open) _drawerMotion.TweenCallback(Callable.From(() => { _drawer.Hide(); _rank.Text = ""; }));
    }

    private void BeginCollect()
    {
        StopDeal();
        _collect?.Kill();
        _collect = CreateTween().SetParallel(true);
        void Collect(RoomPokerCardView card, double delay)
        {
            _collect.TweenProperty(card, "position", card.Position + Vector2.Down * card.Size.Y * 0.5f, 0.32)
                .SetDelay(delay).SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.Out);
            _collect.TweenProperty(card, "modulate:a", 0f, 0.26).SetDelay(delay + 0.06);
        }
        Collect(_first, 0); Collect(_second, 0.04);
    }

    private void BeginDeal(double delay)
    {
        // The scene owns the final poses. Only temporary offsets are animated, so tilted/fanned
        // cards and the future in-hand pose do not need a different animation implementation.
        Vector2 center = ((_first.GetTransform() * (_first.Size / 2))
            + (_second.GetTransform() * (_second.Size / 2))) / 2;
        float angle = _firstPose.Rotation;
        Vector2 StackPosition(RoomPokerCardView card) => center - card.PivotOffset
            - ((card.Size / 2 - card.PivotOffset) * card.Scale).Rotated(angle);
        Vector2 firstStack = StackPosition(_first), secondStack = StackPosition(_second);
        Vector2 lift = Vector2.Up * Mathf.Min(_first.Size.Y, _second.Size.Y) * 0.24f;
        _first.Position = firstStack + lift;
        _second.Position = secondStack + lift;
        _first.Rotation = _second.Rotation = angle;
        _first.ZIndex = System.Math.Max(_firstPose.Z, _secondPose.Z) + 1;
        _first.Modulate = new Color(_firstPose.Tint, 0);
        _second.Modulate = new Color(_secondPose.Tint, 0);
        _deal = CreateTween();
        if (delay > 0) _deal.TweenInterval(delay);
        _deal.TweenProperty(_first, "position", firstStack, 0.28).SetTrans(Tween.TransitionType.Cubic).SetEase(Tween.EaseType.Out);
        _deal.Parallel().TweenProperty(_second, "position", secondStack, 0.28).SetTrans(Tween.TransitionType.Cubic).SetEase(Tween.EaseType.Out);
        _deal.Parallel().TweenProperty(_first, "modulate", _firstPose.Tint, 0.12);
        _deal.Parallel().TweenProperty(_second, "modulate", _secondPose.Tint, 0.12);
        _deal.TweenInterval(0.05);
        _deal.TweenProperty(_first, "position", _firstPose.Position, 0.32).SetTrans(Tween.TransitionType.Cubic).SetEase(Tween.EaseType.Out);
        _deal.Parallel().TweenProperty(_second, "position", _secondPose.Position, 0.32).SetTrans(Tween.TransitionType.Cubic).SetEase(Tween.EaseType.Out);
        _deal.Parallel().TweenProperty(_first, "rotation", _firstPose.Rotation, 0.32).SetTrans(Tween.TransitionType.Cubic).SetEase(Tween.EaseType.Out);
        _deal.Parallel().TweenProperty(_second, "rotation", _secondPose.Rotation, 0.32).SetTrans(Tween.TransitionType.Cubic).SetEase(Tween.EaseType.Out);
        _deal.TweenCallback(Callable.From(RestoreDealPose));
    }

    private void StopDeal()
    {
        if (_deal == null) return;
        _deal.Kill();
        RestoreDealPose();
    }

    private void RestoreDealPose()
    {
        _deal = null;
        _first.Position = _firstPose.Position; _first.Rotation = _firstPose.Rotation;
        _first.ZIndex = _firstPose.Z; _first.Modulate = _firstPose.Tint;
        _second.Position = _secondPose.Position; _second.Rotation = _secondPose.Rotation;
        _second.ZIndex = _secondPose.Z; _second.Modulate = _secondPose.Tint;
    }

    public override void _ExitTree() { _deal?.Kill(); _drawerMotion?.Kill(); _collect?.Kill(); }
}
#endif
