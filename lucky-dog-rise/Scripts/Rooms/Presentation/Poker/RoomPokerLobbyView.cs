#if !DEMO_BUILD && !RECORDING_BUILD
using System.Linq;
using Godot;
using LuckyDogRise.Rooms.Poker;

namespace LuckyDogRise.Rooms;

// One roster in the room page. The endpoint also runs while the page is hidden.
public partial class RoomPokerLobbyView : VBoxContainer
{
    [Export] private Label _status;
    [Export] private Button _start;
    private RoomClient _client;
    public PokerNetworkSession Session { get; private set; }
    public bool CanPrepare { get; set; } = true;
    public void Bind(RoomClient client)
    {
        if (_client == client) return;
        Session?.Dispose(); Session = null; _client = client;
    }
    public override void _Ready() => _start.Pressed += () =>
    {
        if (!CanPrepare || _client?.IsBusy != false || Session == null) return;
        if (Session.State?.Status == PokerTableStatus.Interrupted) Session.Propose();
        else Session.Start();
    };
    public void ToggleReady()
    {
        if (CanPrepare && _client?.IsBusy == false && Session?.IsOwner == false)
            Session.SetReady(!Session.IsReady);
    }
    public void PresentMember(HBoxContainer row, RoomMember member)
    {
        var status = row.GetNode<Label>("PokerStatus");
        var ready = row.GetNode<Button>("Ready");
        bool poker = _client?.View?.GameId == RoomRules.MischiefPoker;
        var peer = Session?.Context?.Members.FirstOrDefault(m => m.LocalId == member.Id);
        var p = Session?.State?.Players.FirstOrDefault(p => p.Key == peer?.Key && p.Ticket == peer.Ticket);
        bool owner = member.Id == _client?.View?.OwnerId;
        ready.Visible = poker && member.Id == _client.Id && !owner && p?.Playing != true
            && Session?.State?.Status is PokerTableStatus.Preparing or PokerTableStatus.Playing;
        ready.Text = Session?.IsReady == true ? RoomPokerText.T("取消准备", "Not ready") : RoomPokerText.T("准备", "Ready");
        ready.Disabled = Session?.Connected != true || !CanPrepare || _client?.IsBusy == true;
        status.Visible = poker && !ready.Visible;
        status.Text = p?.Playing == true ? RoomPokerText.T("参局", "Playing") : owner
            ? RoomPokerText.T("房主", "Host") : p?.Ready == true ? RoomPokerText.T("已准备", "Ready") : RoomPokerText.T("等待", "Waiting");
    }
    public override void _Process(double delta)
    {
        if (_client?.View == null || _client.IsLocalArrival)
        { Session?.Dispose(); Session = null; Hide(); return; }
        if (Session == null && _client.OpenPokerLink() is { } link) Session = new(link);
        Session?.Tick(delta);
        Visible = Session != null && _client.View.GameId == RoomRules.MischiefPoker;
        if (!Visible) return;
        bool preparing = Session.State?.Status == PokerTableStatus.Preparing;
        bool interrupted = Session.State?.Status == PokerTableStatus.Interrupted;
        _start.Visible = Session.IsOwner && (preparing || interrupted);
        _start.Text = interrupted ? RoomPokerText.T("重新准备", "Set up again") : RoomPokerText.T("开始牌局", "Start game");
        _start.Disabled = !CanPrepare || _client.IsBusy || !Session.Connected || preparing && !Session.CanStart;
        _status.Text = Session.Notice switch
        {
            "connecting" => RoomPokerText.T("正在连接牌桌…", "Connecting to the table…"),
            "member_left" or "membership_changed" => RoomPokerText.T("参局成员或房主已变化，牌局已中止。", "A participant or host changed. The game has stopped."),
            "host_ended" => RoomPokerText.T("房主已结束牌桌。", "The host closed the table."),
            "cannot_start" => RoomPokerText.T("暂时无法继续牌局。", "The game cannot continue."),
            _ => preparing ? RoomPokerText.T("房主开始时确认参局，至少两位。未准备的玩家暂不参局。", "The host joins when starting. At least two players required; others may wait.")
                : Session.State?.Continuation == PokerContinuation.Paused ? RoomPokerText.T("已暂停续局", "Next hand paused")
                : Session.State?.Continuation == PokerContinuation.PauseRequested ? RoomPokerText.T("本局结束后暂停", "Pausing after this hand")
                : Session.IsParticipant ? RoomPokerText.T("牌局进行中", "Game in progress")
                : RoomPokerText.T("准备后从下一局加入。", "Ready up to join the next hand.")
        };
    }
    public override void _ExitTree() { Session?.Dispose(); Session = null; }
}
#endif
