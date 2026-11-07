#if !DEMO_BUILD && !RECORDING_BUILD
using System;
using System.Linq;
using Godot;
using LuckyDogRise.Rooms.Poker;

namespace LuckyDogRise.Rooms;

public partial class RoomPokerPanelView : VBoxContainer
{
    [Export] private Label _title, _summary, _order, _result, _error;
    [Export] private Button _next, _pause, _step, _timeout, _exit;
    [Export] private OptionButton _preset;
    public event Action NextRequested, PauseRequested, StepRequested, TimeoutRequested, ExitRequested;
    public event Action<int> PresetSelected;
    public override void _Ready()
    {
        _next.Pressed += () => NextRequested?.Invoke();
        _pause.Pressed += () => PauseRequested?.Invoke();
        _step.Pressed += () => StepRequested?.Invoke();
        _timeout.Pressed += () => TimeoutRequested?.Invoke();
        _exit.Pressed += () => ExitRequested?.Invoke();
        _preset.ItemSelected += index => PresetSelected?.Invoke((int)index);
        _preset.AddItem(""); _preset.AddItem(""); _preset.AddItem(""); _preset.AddItem("");
    }
    public void Present(PokerPlayerView view, bool paused, PokerError error)
    {
        var state = view.Public;
        string Seat(int id) => state.Seats.Single(s => s.PlayerId == id).SeatNumber.ToString();
        _title.Text = RoomPokerText.T("捣乱德州 · 本机试玩", "Poker · Local preview");
        _summary.Text = RoomPokerText.T($"第 {state.HandNumber} 局 · 奖池 {state.Settlement?.Pot ?? state.Pot}\n庄家 {Seat(state.Dealer.PlayerId)} 号 · 连坐 {state.Dealer.ConsecutiveHands}/4", $"Hand {state.HandNumber} · Pot {state.Settlement?.Pot ?? state.Pot}\nDealer #{Seat(state.Dealer.PlayerId)} · Streak {state.Dealer.ConsecutiveHands}/4");
        _order.Text = string.Join(" → ", state.ActionOrder.Select(Seat));
        _order.TooltipText = RoomPokerText.T("按座位编号的行动顺序", "Turn order by seat number");
        if (state.Settlement is { } result)
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
        _next.Disabled = state.Phase != PokerPhase.Settled;
        _pause.Text = paused ? RoomPokerText.T("继续", "Resume") : RoomPokerText.T("暂停", "Pause");
        _step.Text = RoomPokerText.T("机器人一步", "Bot step");
        _step.Disabled = state.Phase != PokerPhase.AwaitingDraw && (state.Phase != PokerPhase.Acting || state.ActivePlayerId == view.PlayerId);
        _timeout.Text = RoomPokerText.T("当前超时", "Timeout");
        _timeout.TooltipText = RoomPokerText.T("推进至当前回合的 15 秒期限", "Advance to this turn's 15-second deadline");
        _timeout.Disabled = state.Phase != PokerPhase.Acting;
        _exit.Text = RoomPokerText.T("结束试玩", "End preview");
        string[] presets = { RoomPokerText.T("下局：随机牌", "Next: Random"), RoomPokerText.T("下局：公共牌平局", "Next: Board tie"), RoomPokerText.T("下局：庄家获胜", "Next: Dealer wins"), RoomPokerText.T("下局：其他人获胜", "Next: Other winner") };
        for (int i = 0; i < presets.Length; i++) _preset.SetItemText(i, presets[i]);
        _error.Text = error == PokerError.InsufficientChips
            ? RoomPokerText.T("有人不足 100 筹码。请结束试玩后重新开始。", "A player has fewer than 100 chips. End and restart this preview.")
            : error == PokerError.None ? "" : RoomPokerText.T("操作未生效，请按当前回合重试。", "Action rejected. Check the current turn.");
        _error.Visible = _error.Text.Length > 0;
    }
}
#endif
