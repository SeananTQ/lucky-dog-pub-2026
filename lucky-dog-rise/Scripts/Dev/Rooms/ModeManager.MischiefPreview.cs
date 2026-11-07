#if DEBUG && !DEMO_BUILD && !RECORDING_BUILD
using System;
using System.Linq;
using Godot;
using LuckyDogRise.Rooms;
using LuckyDogRise.Rooms.Poker;

namespace LuckyDogRise;

public partial class ModeManager
{
    private MischiefPokerPreviewSession _pokerPreview;
    private RoomPokerTableView _pokerPreviewTable;
    private RoomPokerPanelView _pokerPreviewPanel;
    private RoomClient _pokerPreviewClient;
    private long _pokerPreviewSession;
    private (int Id, long Presence)[] _pokerPreviewRoster;
    private string _pokerPreviewGame;
    private int _pokerPreviewOwner;
    public MischiefPokerPreviewSession PokerPreviewForSmoke => _pokerPreview;

    private void StartPokerPreview(RoomClient client)
    {
        if (_pokerPreview != null)
        {
            SwitchToPlay();
            if (CurrentMode == Mode.Play) _settingsPanel.CloseImmediate();
            return;
        }
        if (client != _desktopRoomClient || client.View == null || client.IsBusy || client.IsLocalArrival
            || client.View.Members.Length < 2 || UiInteractionOccupancy.IsOccupied(this)) return;
        if (_gameManager != null && !_gameManager.CanBeginRoomPoker)
        {
            _settingsPanel.ShowPokerPreviewNotice(RoomPokerText.T("请先完成当前单人牌局并领取奖励，再开始本机试玩。", "Finish your solo hand and collect its reward before starting the preview."));
            return;
        }
        SwitchToPlay();
        if (CurrentMode != Mode.Play) return;
        _pokerPreviewTable = _gameManager.BeginRoomPoker();
        if (_pokerPreviewTable == null) return;
        _pokerPreviewClient = client;
        _pokerPreviewSession = client.LayoutSession;
        _pokerPreviewRoster = client.View.Members.Select(m => (m.Id, m.Presence)).OrderBy(m => m.Id).ToArray();
        _pokerPreviewGame = client.View.GameId;
        _pokerPreviewOwner = client.View.OwnerId;
        _pokerPreview = new(_pokerPreviewRoster.Select(m => m.Id).ToArray(), client.View.OwnerId, client.Id);
        _pokerPreview.NextHand();
        _pokerPreviewPanel = GD.Load<PackedScene>("res://Scenes/Rooms/Poker/RoomPokerPanel.tscn").Instantiate<RoomPokerPanelView>();
        _infoPanel.SessionContent.AddChild(_pokerPreviewPanel);
        _pokerPreviewTable.FlipRequested += PokerPreviewFlip;
        _gameManager.SessionPokerTurnEnd += PokerPreviewEndTurn;
        _pokerPreviewPanel.NextRequested += () => { if (PokerPreviewInputAllowed) _pokerPreview?.NextHand(); };
        _pokerPreviewPanel.PauseRequested += () => { if (_pokerPreview != null) _pokerPreview.Paused = !_pokerPreview.Paused; };
        _pokerPreviewPanel.StepRequested += () => _pokerPreview?.Step();
        _pokerPreviewPanel.TimeoutRequested += () => _pokerPreview?.ExpireTurn();
        _pokerPreviewPanel.PresetSelected += preset => { if (_pokerPreview != null) _pokerPreview.NextDealPreset = preset; };
        _pokerPreviewPanel.ExitRequested += StopPokerPreview;
        _settingsPanel.CloseImmediate();
        TickPokerPreview(0);
    }

    private bool PokerPreviewInputAllowed => CurrentMode == Mode.Play && !_hiddenByFullscreenApp
        && !_settingsPanel.IsOpen && !UiInteractionOccupancy.IsOccupied(this)
        && _roomPokerDogs?.LocalChat.Editing != true;
    private void PokerPreviewFlip(int slot) { if (PokerPreviewInputAllowed) _pokerPreview?.Flip(slot); }
    private void PokerPreviewEndTurn() { if (PokerPreviewInputAllowed) _pokerPreview?.EndTurn(); }

    partial void TickPokerPreview(double delta)
    {
        if (_pokerPreview == null) return;
        var client = _pokerPreviewClient;
        if (client != _desktopRoomClient || client.LayoutSession != _pokerPreviewSession || client.View == null
            || client.JoinedCode.Length == 0 || client.View.GameId != _pokerPreviewGame || client.View.OwnerId != _pokerPreviewOwner
            || !_pokerPreviewRoster.SequenceEqual(client.View.Members.Select(m => (m.Id, m.Presence)).OrderBy(m => m.Id)))
        {
            StopPokerPreview();
            _settingsPanel.ShowPokerPreviewNotice(RoomPokerText.T("房间成员或玩法已变化，本机试玩已结束。", "The room changed. The local preview has ended."));
            return;
        }
        _pokerPreview.Tick(delta);
        var view = _pokerPreview.View;
        bool canAct = PokerPreviewInputAllowed && view.Public.Phase == PokerPhase.Acting && view.Public.ActivePlayerId == view.PlayerId;
        _gameManager.SetRoomPokerInput(canAct);
        _pokerPreviewTable.Present(view, canAct);
        _pokerPreviewPanel.Present(view, _pokerPreview.Paused, _pokerPreview.LastError);
        _infoPanel.SetSessionChips(view.Public.Seats.Single(s => s.PlayerId == view.PlayerId).Chips,
            RoomPokerText.T("试玩筹码", "Preview chips"));
        PresentPreviewHands(_roomPokerDogs, view.Public);
        PresentPreviewHands(_roomDesktop, view.Public);
    }

    private static void PresentPreviewHands(RoomDesktopPreview dogs, PokerPublicView state)
    {
        if (dogs == null) return;
        foreach (var dog in dogs.RemoteDogs)
        {
            var seat = state?.Seats.FirstOrDefault(s => s.PlayerId == dog.MemberId);
            if (seat == null) { dog.PokerHand.Hide(); continue; }
            var result = state.Settlement?.Players.FirstOrDefault(p => p.PlayerId == dog.MemberId);
            string marker = dog.MemberId == state.Dealer.PlayerId ? RoomPokerText.T(" · 庄", " · D") : "";
            if (dog.MemberId == state.ActivePlayerId) marker += " ◀";
            dog.PokerHand.Present(result?.HoleCards, $"#{seat.SeatNumber}{marker}\n{seat.Chips:N0}", dog.MemberId == state.ActivePlayerId);
        }
    }

    private void StopPokerPreview()
    {
        if (_pokerPreview == null) return;
        _pokerPreview = null;
        _gameManager.SessionPokerTurnEnd -= PokerPreviewEndTurn;
        _gameManager.EndRoomPoker();
        _infoPanel.SetSessionChips(null);
        _pokerPreviewTable.Hide();
        _pokerPreviewTable.QueueFree();
        _pokerPreviewPanel.Hide();
        _pokerPreviewPanel.QueueFree();
        _pokerPreviewTable = null;
        _pokerPreviewPanel = null;
        _pokerPreviewClient = null;
        PresentPreviewHands(_roomPokerDogs, null);
        PresentPreviewHands(_roomDesktop, null);
    }
}
#endif
