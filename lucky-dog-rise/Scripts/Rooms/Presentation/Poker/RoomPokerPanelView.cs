#if !DEMO_BUILD && !RECORDING_BUILD
using System;
using System.Linq;
using Godot;
using LuckyDogRise.Rooms.Poker;

namespace LuckyDogRise.Rooms;

public partial class RoomPokerPanelView : VBoxContainer
{
    [Export] private Label _title, _summary, _orderHeading, _order, _resultHeading, _result, _error;
    [Export] private Button _next, _pause;
    [Export] private Label _pauseStatus;
    public event Action NextRequested;
    public event Action PauseRequested;
    public override void _Ready()
    {
        _next.Pressed += () => NextRequested?.Invoke();
        _pause.Pressed += () => PauseRequested?.Invoke();
    }
    public void PresentNetwork(PokerNetworkSession session)
    {
        _next.Hide();
        var state = session.State;
        _pause.Visible = session.IsOwner;
        _pause.Disabled = !session.Connected || state.Continuation == PokerContinuation.Collecting
            || state.Continuation == PokerContinuation.Paused && !session.CanResume;
        _pause.Text = state.Continuation == PokerContinuation.Paused ? RoomPokerText.T("继续", "Resume")
            : state.Continuation == PokerContinuation.PauseRequested ? RoomPokerText.T("撤销", "Undo") : RoomPokerText.T("暂停", "Pause");
        _pause.TooltipText = RoomPokerText.T("本局结束并派彩后暂停，不影响当前回合。", "Pause after this hand's payouts. Current turns continue.");
        _pauseStatus.Text = state.Continuation == PokerContinuation.Paused ? RoomPokerText.T("已暂停续局", "Next hand paused")
            : state.Continuation == PokerContinuation.PauseRequested ? RoomPokerText.T("本局结束后暂停", "Pausing after this hand") : "";
        _pauseStatus.Visible = _pauseStatus.Text.Length > 0;
    }
    public void Present(PokerPlayerView view, PokerError error, PokerRoundPresentation round)
    {
        var state = view.Public;
        string Seat(int id) => state.Seats.Single(s => s.PlayerId == id).SeatNumber.ToString();
        _title.Text = RoomPokerText.T("捣乱德州", "Poker");
        _summary.Text = RoomPokerText.T($"第 {state.HandNumber} 局 · 奖池 {state.Settlement?.Pot ?? state.Pot}\n庄家 {Seat(state.Dealer.PlayerId)} 号 · 连坐 {state.Dealer.ConsecutiveHands}/4", $"Hand {state.HandNumber} · Pot {state.Settlement?.Pot ?? state.Pot}\nDealer #{Seat(state.Dealer.PlayerId)} · Streak {state.Dealer.ConsecutiveHands}/4");
        _order.Text = string.Join(" → ", state.ActionOrder.Select(Seat));
        _orderHeading.Text = RoomPokerText.T("行动顺序", "Turn order");
        _resultHeading.Text = round.WinnersVisible
            ? RoomPokerText.T("本局结算", "Results") : RoomPokerText.T("玩家状态", "Players");
        _order.TooltipText = RoomPokerText.T("按座位编号的行动顺序", "Turn order by seat number");
        if (round.WinnersVisible && state.Settlement is { } result)
        {
            _summary.Text = RoomPokerText.T($"第 {state.HandNumber} 局 · 奖池 {result.Pot}\n下局庄家：{Seat(result.NextDealer.PlayerId)} 号", $"Hand {state.HandNumber} · Pot {result.Pot}\nNext dealer: #{Seat(result.NextDealer.PlayerId)}")
                + (result.DealerChange is PokerDealerChange.Tie or PokerDealerChange.StreakLimit ? RoomPokerText.T(" · 逆序", " · Reversed") : "");
            _result.Text = string.Join("\n", result.Players.Select(p => $"#{Seat(p.PlayerId)} {RoomPokerText.Rank(p.BestHand.Value.Rank)}  +{p.Award}"))
                + (result.SystemTip > 0 ? RoomPokerText.T($"\n系统收走余数：{result.SystemTip}", $"\nRemainder removed: {result.SystemTip}") : "");
        }
        else
        {
            _result.Text = string.Join("\n", state.ActionOrder.Select(id =>
            {
                var done = state.CompletedTurns.FirstOrDefault(t => t.PlayerId == id);
                string label = done != null ? done.Reason == PokerTurnEnd.TimedOut ? RoomPokerText.T("超时结束", "Timed out") : RoomPokerText.T("已结束", "Done")
                    : id == state.ActivePlayerId ? RoomPokerText.T($"操作中 · {Math.Ceiling(state.SecondsRemaining):0}秒", $"Acting · {Math.Ceiling(state.SecondsRemaining):0}s") : RoomPokerText.T("等待", "Waiting");
                return $"#{Seat(id)}  {label}";
            }));
        }
        _next.Text = RoomPokerText.T("下一局", "Next hand");
        _next.Disabled = !round.CanRequestNext;
        _next.Visible = round.Stage == PokerRoundStage.AwaitingNext && round.PauseAfterPayout;
        _error.Text = error == PokerError.InsufficientChips || round.Stage == PokerRoundStage.AwaitingNext && !round.CanAffordNext
            ? RoomPokerText.T("有人不足 100 筹码，无法开始下一局。", "A player has fewer than 100 chips. The next hand cannot start.")
            : error == PokerError.None ? "" : RoomPokerText.T("操作未生效，请按当前回合重试。", "Action rejected. Check the current turn.");
        _error.Visible = _error.Text.Length > 0;
    }
}
#endif
