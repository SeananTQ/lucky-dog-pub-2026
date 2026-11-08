#if DEBUG && !DEMO_BUILD && !RECORDING_BUILD
using System;
using System.Linq;
using Godot;
using LuckyDogRise.Rooms.Poker;

namespace LuckyDogRise.Rooms;

// Development controls are hosted by the Mock panel, separate from player-facing match information.
public partial class RoomPokerDebugPanelView : VBoxContainer
{
    [Export] private Label _title, _status, _presetLabel, _hint;
    [Export] private Button _pause, _step, _timeout, _exit;
    [Export] private OptionButton _preset, _panelState;
    public event Action PauseRequested, StepRequested, TimeoutRequested, ExitRequested;
    public event Action<int> PresetSelected;
    public event Action<int> PanelStateSelected;

    public override void _Ready()
    {
        _pause.Pressed += () => PauseRequested?.Invoke();
        _step.Pressed += () => StepRequested?.Invoke();
        _timeout.Pressed += () => TimeoutRequested?.Invoke();
        _exit.Pressed += () => ExitRequested?.Invoke();
        _preset.ItemSelected += index => PresetSelected?.Invoke((int)index);
        _panelState.ItemSelected += index => PanelStateSelected?.Invoke((int)index);
        for (int i = 0; i < 9; i++) _preset.AddItem("");
        for (int i = 0; i < 3; i++) _panelState.AddItem("");
    }

    public void Present(PokerPlayerView view, bool paused, int nextDealPreset, int panelState)
    {
        var state = view.Public;
        _title.Text = RoomPokerText.T("捣乱德州 · 本机试玩控制", "Poker · Local preview controls");
        string phase = state.ActivePlayerId is { } active
            ? RoomPokerText.T($"{state.Seats.Single(s => s.PlayerId == active).SeatNumber} 号操作中 · {Math.Ceiling(state.SecondsRemaining):0} 秒", $"Seat {state.Seats.Single(s => s.PlayerId == active).SeatNumber} acting · {Math.Ceiling(state.SecondsRemaining):0}s")
            : state.Phase == PokerPhase.AwaitingDraw ? RoomPokerText.T("等待补牌", "Awaiting draw") : RoomPokerText.T("本局已结算", "Hand settled");
        _status.Text = (paused ? RoomPokerText.T("已暂停", "Paused") : RoomPokerText.T("运行中", "Running"))
            + RoomPokerText.T($" · 第 {state.HandNumber} 局 · ", $" · Hand {state.HandNumber} · ") + phase;
        _presetLabel.Text = RoomPokerText.T("下一局牌堆", "Next hand deck");
        string[] presets = { RoomPokerText.T("随机牌", "Random"), RoomPokerText.T("公共牌平局", "Board tie"), RoomPokerText.T("庄家获胜", "Dealer wins"), RoomPokerText.T("其他人获胜", "Other winner"),
            RoomPokerText.T("相同两对 · K 胜公共牌 5", "Same two pair · K beats board 5"),
            RoomPokerText.T("相同对子 · 因观察者而异", "Same pair · Viewer-specific kicker"),
            RoomPokerText.T("不同两对 · 不放大副牌", "Different two pair · No kicker bounce"),
            RoomPokerText.T("他人平局 · 本机落败", "Other players tie · Local player loses"),
            RoomPokerText.T("本机独赢 · A 副牌不放大", "Local winner · Ace kicker, no bounce") };
        for (int i = 0; i < presets.Length; i++) _preset.SetItemText(i, presets[i]);
        _preset.Select(nextDealPreset);
        _panelState.SetItemText(0, RoomPokerText.T("牌手面板：正常", "Player panels: Normal"));
        _panelState.SetItemText(1, RoomPokerText.T("首位其他玩家：等待入局（仅显示）", "First other player: Waiting (visual only)"));
        _panelState.SetItemText(2, RoomPokerText.T("首位其他玩家：掉线托管（仅显示）", "First other player: Autoplay (visual only)"));
        _panelState.Select(panelState);
        _pause.Text = paused ? RoomPokerText.T("继续", "Resume") : RoomPokerText.T("暂停", "Pause");
        _step.Text = RoomPokerText.T("机器人一步", "Bot step");
        _step.Disabled = state.Phase != PokerPhase.AwaitingDraw && (state.Phase != PokerPhase.Acting || state.ActivePlayerId == view.PlayerId);
        _timeout.Text = RoomPokerText.T("当前超时", "Force timeout");
        _timeout.Disabled = state.Phase != PokerPhase.Acting;
        _timeout.TooltipText = RoomPokerText.T("推进至当前回合的 15 秒期限", "Advance to this turn's 15-second deadline");
        _exit.Text = RoomPokerText.T("结束试玩", "End preview");
        _hint.Text = RoomPokerText.T(
            "暂停会停止倒计时和机器人，仍可手动翻牌或结束回合。切换页签、关闭 Mock 面板不会暂停牌局。\n牌堆预设从下一局生效；非随机预设下直接结束回合，不改公共牌，便于观察指定结果。\n试玩仅在本机进行，不修改单人筹码或存档。筹码不足时可结束并重新开始试玩。",
            "Pause freezes the clock and bots; manual flips and ending a turn still work. Switching tabs or closing Mock does not pause play.\nDeck presets apply to the next hand. For a fixed result, end your turn without changing the board.\nThis preview is local and does not change solo chips or saves. End and restart it if chips run out.");
    }
}
#endif
