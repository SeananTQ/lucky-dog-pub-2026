#if !DEMO_BUILD && !RECORDING_BUILD
using System.Linq;
using Godot;
using LuckyDogRise.Rooms.Poker;

namespace LuckyDogRise.Rooms;

// Both panels share commands and authority state; only the poker information panel shows in-hand controls.
public partial class RoomPokerLobbyView : VBoxContainer
{
    [Export] private Button _start, _restart;
    private RoomClient _client;
    private readonly PokerAutoChat _autoChat = new();
    public bool AutoChatBlocked { get; set; } = true;
    public bool AutoChatRevealReady { get; set; }
    public PokerNetworkSession Session { get; private set; }
    public bool CanPrepare { get; set; } = true;
    public bool ActionsAvailable => CanPrepare && _client?.IsBusy == false;
    public void Bind(RoomClient client)
    {
        if (_client == client) return;
        if (_client != null) _client.ManualChatSent -= OnManualChatSent;
        _autoChat.Reset();
        Session?.Dispose(); Session = null; _client = client;
        if (_client != null) _client.ManualChatSent += OnManualChatSent;
    }
    private void OnManualChatSent() => _autoChat.ManualMessageSent(Session?.Context?.Room, Session?.State);
    public override void _Ready()
    {
        _autoChat.SetEnabled(SettingsManager.LoadAutoChat());
        SettingsManager.AutoChatChanged += _autoChat.SetEnabled;
        _start.Pressed += ActivateLocal;
        _restart.Pressed += RestartLocal;
    }
    public void ActivateLocal() { if (ActionsAvailable) Activate(Session); }
    public void RestartLocal() { if (ActionsAvailable) Session?.Restart(); }
    public void ToggleReady()
    {
        if (CanPrepare && _client?.IsBusy == false && Session?.IsOwner == false) Activate(Session);
    }
    public static void Activate(PokerNetworkSession session)
    {
        if (session?.Connected != true) return;
        if (!session.IsOwner) { session.SetReady(!session.IsReady); return; }
        if (session.State?.Status == PokerTableStatus.Interrupted) session.Propose();
        else if (session.State?.Status == PokerTableStatus.Preparing) session.Start();
        else if (session.State?.Continuation == PokerContinuation.Paused) session.Resume();
        else session.SetPauseRequested(session.State?.Continuation != PokerContinuation.PauseRequested);
    }
    public static void PresentActions(PokerNetworkSession session, Button primary, Button restart = null, bool available = true,
        bool includeInHandActions = true)
    {
        primary.Visible = true;
        primary.Disabled = !available || session?.Connected != true;
        primary.ThemeTypeVariation = "CapsuleButton";
        primary.ToggleMode = false;
        if (restart != null)
        {
            restart.Visible = session?.IsOwner == true && session.State is { Status: PokerTableStatus.Playing, Continuation: PokerContinuation.Paused };
            restart.Disabled = !available || session?.CanRestart != true;
            restart.Text = RoomPokerText.T("重开", "Restart");
            restart.TooltipText = RoomPokerText.T("重新分配座位和 1000 筹码，由房主坐庄；加入已准备的等待成员。", "Reset seats and chips to 1000, deal with the host as dealer, and admit ready waiting members.");
        }
        if (session == null) { primary.Text = RoomPokerText.T("准备", "Ready"); return; }
        var state = session.State;
        if (session.IsOwner)
        {
            if (state?.Status != PokerTableStatus.Playing)
            {
                primary.Text = RoomPokerText.T("开始牌局", "Start");
                primary.Disabled |= state?.Status != PokerTableStatus.Interrupted && !session.CanStart;
                primary.TooltipText = RoomPokerText.T("至少两名玩家准备后开始；房主默认参局。", "Start with at least two ready players. The host takes part automatically.");
            }
            else if (state.Continuation == PokerContinuation.Paused)
            {
                primary.Text = RoomPokerText.T("继续", "Resume"); primary.Disabled |= !session.CanResume;
                primary.TooltipText = RoomPokerText.T("保留筹码、座位与庄家进度继续；已申请入座的玩家从下一局参加。至少两人入座且各有 100 筹码。", "Keep balances, seats and dealer progress. Pending players join next hand. At least two seated players need 100 chips each.");
            }
            else
            {
                primary.Visible = includeInHandActions;
                bool pending = state.Continuation == PokerContinuation.PauseRequested;
                primary.Text = pending ? RoomPokerText.T("取消暂停", "Cancel") : RoomPokerText.T("打完暂停", "Pause");
                primary.Disabled |= session.IsCollecting;
                primary.ThemeTypeVariation = pending ? "CapsuleActionButton" : "CapsuleButton";
                primary.TooltipText = RoomPokerText.T("本局派彩后暂停；再次点击可取消。", "Pause after this hand's payouts. Click again to cancel.");
            }
        }
        else
        {
            primary.Disabled |= !session.CanSetReady;
            if (state?.Status != PokerTableStatus.Playing)
            {
                primary.Text = session.IsReady ? RoomPokerText.T("取消准备", "Cancel") : RoomPokerText.T("准备", "Ready");
                primary.ThemeTypeVariation = session.IsReady ? "CapsuleActionButton" : "CapsuleButton";
                primary.TooltipText = RoomPokerText.T("准备后等待房主开始；未准备的玩家留在房间旁观。", "Ready for the host to start. Unready players stay in the room as spectators.");
            }
            else if (session.IsPaused && session.IsParticipant && session.IsReady)
            {
                primary.Text = RoomPokerText.T("离座", "Sit out");
                primary.TooltipText = RoomPokerText.T("暂停期间立即离座，保留筹码与座位。", "Sit out while paused, keeping your chips and seat.");
            }
            else if (session.IsInHand && !session.IsPaused)
            {
                primary.Visible = includeInHandActions;
                primary.Text = session.IsReady ? RoomPokerText.T("打完离座", "Sit out") : RoomPokerText.T("取消离座", "Cancel");
                primary.ThemeTypeVariation = session.IsReady ? "CapsuleButton" : "CapsuleActionButton";
                primary.TooltipText = RoomPokerText.T("完成本局后离座，不再扣底注；保留筹码与座位。", "Sit out after this hand. Keep your seat and chips without paying further antes.");
            }
            else
            {
                bool returning = session.IsParticipant;
                primary.Text = session.IsReady ? RoomPokerText.T("取消入座", "Cancel") : RoomPokerText.T("入座", "Sit in");
                primary.ThemeTypeVariation = session.IsReady ? "CapsuleActionButton" : "CapsuleButton";
                primary.TooltipText = returning ? RoomPokerText.T("从下一局回到原座位并保留筹码；收牌期间暂不可更改。", "Return to your existing seat next hand, keeping your chips. Locked during collection.")
                    : RoomPokerText.T("从下一局入座，首次分配 1000 筹码；不会加入正在进行的这一局。收牌期间暂不可更改。", "Join next hand with 1000 initial chips. You cannot join a hand already in progress. Locked during collection.");
            }
        }
    }
    public void PresentMember(HBoxContainer row, RoomMember member)
    {
        var status = row.GetNode<Label>("PokerStatus");
        var ready = row.GetNode<Button>("Ready");
        bool poker = _client?.View?.GameId == RoomRules.MischiefPoker;
        var peer = Session?.Context?.Members.FirstOrDefault(m => m.LocalId == member.Id);
        var p = Session?.State?.Players.FirstOrDefault(p => p.Key == peer?.Key && p.Ticket == peer.Ticket);
        bool owner = member.Id == _client?.View?.OwnerId;
        if (poker && member.Id == _client.Id && !owner)
            PresentActions(Session, ready, available: CanPrepare && !_client.IsBusy, includeInHandActions: false);
        else ready.Hide();
        status.Visible = poker && !ready.Visible;
        bool inHand = p != null && Session?.State?.Hand?.Public.ActionOrder.Contains(p.Id) == true;
        status.Text = Session?.State?.Status == PokerTableStatus.Playing
            ? inHand && !Session.IsPaused
                ? p.Ready ? RoomPokerText.T("参局", "Playing") : RoomPokerText.T("待离座", "Sitting out next")
                : p?.Ready == true
                    ? p.Playing && Session.IsPaused ? RoomPokerText.T("已入座", "Seated") : RoomPokerText.T("待入座", "Joining next")
                    : RoomPokerText.T("旁观", "Spectating")
            : owner ? RoomPokerText.T("房主", "Host") : p?.Ready == true ? RoomPokerText.T("已准备", "Ready") : RoomPokerText.T("未准备", "Not ready");
    }
    public override void _Process(double delta)
    {
        if (_client?.View == null || _client.IsLocalArrival)
        { _autoChat.Reset(); Session?.Dispose(); Session = null; Hide(); return; }
        if (Session == null && _client.OpenPokerLink() is { } link) Session = new(link);
        Session?.Tick(delta);
        var line = _autoChat.Tick(Session?.Context?.Room, Session?.State, Session?.Connected == true,
            AutoChatBlocked || !_client.ChatAllowed || _client.IsBusy, AutoChatRevealReady);
        if (line != null) _client.SendAutomaticChat(RoomPokerText.AutoChat(line));
        if (Session == null || _client.View.GameId != RoomRules.MischiefPoker) { Hide(); return; }
        PresentActions(Session, _start, _restart, CanPrepare && !_client.IsBusy, includeInHandActions: false);
        _start.Visible &= Session.IsOwner;
        Visible = _start.Visible || _restart.Visible;
    }
    public override void _ExitTree()
    {
        SettingsManager.AutoChatChanged -= _autoChat.SetEnabled;
        if (_client != null) _client.ManualChatSent -= OnManualChatSent;
        _autoChat.Reset(); Session?.Dispose(); Session = null;
    }
}
#endif
