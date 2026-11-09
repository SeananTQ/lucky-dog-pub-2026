#if !DEMO_BUILD && !RECORDING_BUILD
using System;
using System.Linq;
using Godot;
using LuckyDogRise.Rooms;
using LuckyDogRise.Rooms.Poker;

namespace LuckyDogRise;

public partial class ModeManager
{
    private RoomPokerTableView _networkPokerTable;
    private RoomPokerPanelView _networkPokerPanel;
    private RoomPokerPayoutView _networkPokerPayout;
    private PokerNetworkSession _networkPokerSession;
    private Guid _networkPokerTableId, _networkPokerPrompt;
    private long _networkPokerFinished;
    private double _networkPokerFinishRetry;
    private bool _networkPokerInterruptedNotice;

    private void TickNetworkPoker(double delta)
    {
        var lobby = _settingsPanel?.RoomPokerLobby;
        var session = lobby?.Session;
        if (_gameManager != null) _gameManager.IsSessionPokerReserved = session?.IsReady == true
            && session.State.Status is PokerTableStatus.Preparing or PokerTableStatus.Playing;
        if (lobby != null) lobby.CanPrepare = (_gameManager == null || _networkPokerTable != null || _gameManager.CanBeginRoomPoker)
            && !UiInteractionOccupancy.IsOccupied(this) && !_hiddenByFullscreenApp;
        if (lobby?.CanPrepare == false && session?.IsReady == true && !session.IsParticipant)
            session.SetReady(false);
        if (session?.State is { Status: PokerTableStatus.Preparing } prep && !session.IsOwner
            && prep.Table != _networkPokerPrompt && !UiInteractionOccupancy.IsOccupied(this)
            && !_hiddenByFullscreenApp && CurrentMode != Mode.Immersive)
        {
            CancelWindowDrag(); RefreshSettingsPanelModeActions(); PositionPanelInBestSlot();
            if (_settingsPanel.ShowRoomPage())
            { _networkPokerPrompt = prep.Table; _settingsPanelOpenedAtSeconds = Time.GetTicksMsec() / 1000.0; }
        }
        bool playing = session?.State?.Status == PokerTableStatus.Playing && session.IsParticipant;
        if (_networkPokerTable != null && (!playing || _networkPokerSession != session || _networkPokerTableId != session.State.Table))
        {
            _networkPokerInterruptedNotice = session?.State?.Status == PokerTableStatus.Interrupted;
            StopNetworkPoker();
        }
        if (_networkPokerInterruptedNotice && !UiInteractionOccupancy.IsOccupied(this)
            && !_hiddenByFullscreenApp && CurrentMode != Mode.Immersive)
        {
            if (session?.State?.Status == PokerTableStatus.Interrupted)
            {
                CancelWindowDrag(); RefreshSettingsPanelModeActions(); PositionPanelInBestSlot();
                _settingsPanel.ShowRoomPage();
                _settingsPanelOpenedAtSeconds = Time.GetTicksMsec() / 1000.0;
            }
            _networkPokerInterruptedNotice = false;
        }
        if (!playing) return;
        if (_networkPokerTable == null)
        {
            if (_gameManager != null && !_gameManager.CanBeginRoomPoker || UiInteractionOccupancy.IsOccupied(this)) return;
            SwitchToPlay();
            if (CurrentMode != Mode.Play) return;
            _networkPokerTable = _gameManager.BeginRoomPoker();
            if (_networkPokerTable == null) return;
            _networkPokerTable.Round.PauseAfterPayout = false;
            _networkPokerSession = session; _networkPokerTableId = session.State.Table; _networkPokerFinished = 0;
            _networkPokerPanel = GD.Load<PackedScene>("res://Scenes/Rooms/Poker/RoomPokerPanel.tscn").Instantiate<RoomPokerPanelView>();
            _infoPanel.SessionContent.AddChild(_networkPokerPanel);
            _networkPokerPayout = GD.Load<PackedScene>("res://Scenes/Rooms/Poker/RoomPokerPayout.tscn").Instantiate<RoomPokerPayoutView>();
            AddChild(_networkPokerPayout);
            _networkPokerTable.FlipRequested += slot => { if (NetworkPokerInputAllowed) _networkPokerSession.Act(PokerAction.Flip, slot); };
            _gameManager.SessionPokerTurnEnd += NetworkPokerEndTurn;
            _settingsPanel.CloseImmediate();
        }
        var view = session.DisplayView;
        if (view == null) return;
        bool visible = CurrentMode == Mode.Play && !_hiddenByFullscreenApp && !UiInteractionOccupancy.IsOccupied(this);
        bool act = NetworkPokerInputAllowed && session.Connected && !session.ActionPending && session.State.DealRemaining == 0
            && view.Public.Phase == PokerPhase.Acting && view.Public.ActivePlayerId == view.PlayerId;
        _networkPokerTable.Present(view, act, delta, animate: visible,
            otherCardsMoving: _roomPokerDogs?.RemoteDogs.Any(d => d.PokerHand.IsMoving) == true);
        if (_networkPokerTable.Round.TakeNextHandRequest()) _networkPokerFinished = view.Public.HandNumber;
        _networkPokerFinishRetry -= delta;
        if (_networkPokerFinished == view.Public.HandNumber && _networkPokerFinishRetry <= 0)
        { session.FinishPresentation(); _networkPokerFinishRetry = 2; }
        _gameManager.SetRoomPokerInput(act && !_networkPokerTable.IsDealing);
        var display = _networkPokerTable.Round.DisplayView;
        _networkPokerPanel.Present(display, session.LastError, _networkPokerTable.Round);
        _infoPanel.SetSessionChips(display.Public.Seats.Single(s => s.PlayerId == display.PlayerId).Chips,
            RoomPokerText.T("牌局筹码", "Table chips"));
        foreach (var dogs in new[] { _roomPokerDogs, _roomDesktop })
        {
            if (dogs == null) continue;
            foreach (var dog in dogs.RemoteDogs)
            {
                var result = display.Public.Settlement?.Players.FirstOrDefault(p => p.PlayerId == dog.MemberId);
                dog.PokerHand.Present(display.Public, dog.MemberId, result?.HoleCards, _networkPokerTable.Emphasis,
                    _networkPokerTable.Showdown.Cue, dogs.IsVisibleInTree(),
                    waiting: !display.Public.Seats.Any(s => s.PlayerId == dog.MemberId),
                    dealDelay: RoomPokerHandView.RemoteDealDelay, round: _networkPokerTable.Round);
            }
        }
        PresentRoomPokerPayout(_networkPokerTable, _networkPokerPayout, view, visible);
    }
    private bool NetworkPokerInputAllowed => _networkPokerTable != null && CurrentMode == Mode.Play
        && !_hiddenByFullscreenApp && !_settingsPanel.IsOpen && !UiInteractionOccupancy.IsOccupied(this)
        && !_networkPokerTable.IsDealing && _roomPokerDogs?.LocalChat.Editing != true;
    private void NetworkPokerEndTurn() { if (NetworkPokerInputAllowed) _networkPokerSession?.Act(PokerAction.EndTurn); }

    private void PresentRoomPokerPayout(RoomPokerTableView table, RoomPokerPayoutView payout, PokerPlayerView view, bool visible)
    {
        var sequence = table.Showdown;
        Vector2 FromTable(Vector2 point) => _playViewport.GetGlobalTransformWithCanvas()
            * (point * (_playViewport.Size / _playSubViewport.Size));
        if (visible && table.Round.DealerInFlight && view.Public.Settlement is { } result)
        {
            Vector2? Anchor(int id) => id == view.PlayerId ? FromTable(table.LocalPanel.DealerAnchor)
                : _roomPokerDogs?.RemoteDogs.FirstOrDefault(d => d.MemberId == id)?.PokerHand.DealerAnchor;
            var start = Anchor(view.Public.Dealer.PlayerId); var end = Anchor(result.NextDealer.PlayerId);
            if (start.HasValue && end.HasValue)
                payout.PresentDealer(start.Value, end.Value, table.Round.StageSeconds, 22 * _playViewport.GetGlobalTransformWithCanvas().Scale.X, true);
            else payout.Hide();
            return;
        }
        if (!visible || !table.Round.WinnersVisible || sequence.Current is not { } current) { payout.Hide(); return; }
        Vector2 target;
        if (current.WinnerId == view.PlayerId) target = FromTable(table.LocalPanel.AwardAnchor);
        else
        {
            var dog = _roomPokerDogs?.RemoteDogs.FirstOrDefault(d => d.MemberId == current.WinnerId);
            if (dog == null || dog.PokerHand.IsWaiting) { payout.Hide(); return; }
            target = dog.PokerHand.AwardAnchor;
        }
        payout.Present(FromTable(new Vector2(600, 940)), target, sequence.StepSeconds,
            24 * _playViewport.GetGlobalTransformWithCanvas().Scale.X, true);
    }
    private void StopNetworkPoker()
    {
        if (_networkPokerTable == null) return;
        if (GodotObject.IsInstanceValid(_gameManager))
        { _gameManager.SessionPokerTurnEnd -= NetworkPokerEndTurn; _gameManager.EndRoomPoker(); }
        if (GodotObject.IsInstanceValid(_infoPanel)) _infoPanel.SetSessionChips(null);
        foreach (var node in new CanvasItem[] { _networkPokerTable, _networkPokerPanel, _networkPokerPayout })
            if (GodotObject.IsInstanceValid(node)) { node.Hide(); node.QueueFree(); }
        foreach (var dogs in new[] { _roomPokerDogs, _roomDesktop })
            if (GodotObject.IsInstanceValid(dogs)) foreach (var dog in dogs.RemoteDogs) dog.PokerHand.Hide();
        _networkPokerTable = null; _networkPokerPanel = null; _networkPokerPayout = null; _networkPokerSession = null;
    }
}
#endif
