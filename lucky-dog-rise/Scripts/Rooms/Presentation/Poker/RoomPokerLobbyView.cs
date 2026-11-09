#if !DEMO_BUILD && !RECORDING_BUILD
using System.Linq;
using Godot;
using LuckyDogRise.Rooms.Poker;

namespace LuckyDogRise.Rooms;

public partial class RoomPokerLobbyView : VBoxContainer
{
    [Export] private Label _heading, _status, _roster;
    [Export] private Button _propose, _ready, _start, _cancel;
    private RoomClient _client;
    public PokerNetworkSession Session { get; private set; }
    public bool CanPrepare { get; set; }
    public void Bind(RoomClient client)
    {
        if (_client == client) return;
        Session?.Dispose(); Session = null; _client = client;
    }
    public override void _Ready()
    {
        _propose.Pressed += () => { if (CanPrepare) Session?.Propose(); };
        _ready.Pressed += () => { if (CanPrepare || Session?.IsReady == true) Session?.SetReady(!Session.IsReady); };
        _start.Pressed += () => { if (CanPrepare) Session?.Start(); };
        _cancel.Pressed += () => Session?.Stop();
    }
    public override void _Process(double delta)
    {
        if (_client?.View == null || _client.IsLocalArrival)
        { Session?.Dispose(); Session = null; Hide(); return; }
        if (Session == null && _client.OpenPokerLink() is { } link) Session = new(link);
        Visible = Session != null;
        if (Session == null) return;
        Session.Tick(delta);
        var s = Session.State;
        _heading.Text = RoomPokerText.T("捣乱德州", "Mischief Holdem");
        _propose.Text = RoomPokerText.T("发起牌局", "Set up table");
        _ready.Text = Session.IsReady ? RoomPokerText.T("取消准备", "Not ready") : RoomPokerText.T("准备", "Ready");
        _start.Text = RoomPokerText.T("开始牌局", "Start game");
        bool preparing = s?.Status == PokerTableStatus.Preparing;
        bool playing = s?.Status == PokerTableStatus.Playing;
        _cancel.Text = playing ? RoomPokerText.T("结束牌局", "End game") : RoomPokerText.T("取消牌局", "Cancel table");
        _propose.Visible = Session.IsOwner && !preparing && !playing;
        _propose.Disabled = !CanPrepare || _client.IsBusy || !Session.Connected;
        _ready.Visible = preparing || playing && !Session.IsParticipant;
        _ready.Disabled = !Session.Connected || !CanPrepare && !Session.IsReady;
        _start.Visible = Session.IsOwner && preparing;
        _start.Disabled = !Session.CanStart || !CanPrepare || _client.IsBusy;
        _cancel.Visible = Session.IsOwner && (preparing || playing && s.Hand?.Public.Phase == PokerPhase.Settled);
        _status.Text = Session.Notice switch
        {
            "connecting" => RoomPokerText.T("正在连接牌桌…", "Connecting to the table…"),
            "member_left" or "membership_changed" => RoomPokerText.T("参局成员或房主已变化，牌局已中止。", "A participant or host changed. The game has stopped."),
            "presentation_timeout" => RoomPokerText.T("等待玩家同步超时，牌局已中止。", "A player did not finish synchronizing. The game has stopped."),
            "host_ended" => RoomPokerText.T("房主已结束牌桌。", "The host closed the table."),
            "cannot_start" => RoomPokerText.T("暂时无法继续牌局。", "The game cannot continue."),
            _ => !CanPrepare && !Session.IsParticipant
                ? RoomPokerText.T("请先完成单人牌局及奖励领取，再准备。", "Finish your solo hand and collect rewards before preparing.")
                : preparing ? RoomPokerText.T("准备后由房主开始，至少两位。未准备的玩家暂不参局。", "The host starts with at least two ready players. Others can wait.")
                : playing ? Session.IsParticipant ? RoomPokerText.T("牌局进行中", "Game in progress")
                    : RoomPokerText.T("等待入局：准备后从下一局加入。", "Waiting: ready up to join the next hand.")
                : RoomPokerText.T("等待房主发起牌局", "Waiting for the host to set up a table")
        };
        _roster.Visible = preparing || playing;
        _roster.Text = s == null ? "" : string.Join("\n", s.Players.Select(p =>
        {
            string name = Session.Context?.Members.FirstOrDefault(m => m.Key == p.Key)?.Name ?? "";
            string status = p.Playing ? RoomPokerText.T("参局", "Playing") : p.Ready ? RoomPokerText.T("已准备", "Ready") : RoomPokerText.T("等待", "Waiting");
            return $"{name} · {status}";
        }));
    }
    public override void _ExitTree() { Session?.Dispose(); Session = null; }
}
#endif
