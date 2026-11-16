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
    private Guid _networkPokerTableId;
    private bool _networkPokerAttached;

    private void TickNetworkPoker(double delta)
    {
        var lobby = _settingsPanel?.RoomPokerLobby;
        var session = lobby?.Session;
        bool poker = _desktopRoomClient?.View?.GameId == RoomRules.MischiefPoker;
        bool reserved = poker;
        if (lobby != null) lobby.CanPrepare = true;
#if DEBUG
        if (_pokerPreview != null)
        {
            if (reserved) StopPokerPreview(); else return;
        }
#endif
        if (_gameManager != null)
        {
            if (reserved && !_gameManager.IsSessionPokerReserved) _gameManager.DiscardSoloHandForRoomPoker();
            _gameManager.IsSessionPokerReserved = reserved;
        }
        bool available = poker && session?.State != null;
        if (_networkPokerTable != null && (!available || _networkPokerSession != session || _networkPokerTableId != session.State.Table))
            StopNetworkPoker();
        if (available && _networkPokerTable == null)
        {
            // Run the same presentation clock even when the player has never opened the poker view.
            // It remains hidden until an explicit mode change; network state never switches windows.
            _networkPokerTable = GD.Load<PackedScene>("res://Scenes/Rooms/Poker/RoomPokerTable.tscn").Instantiate<RoomPokerTableView>();
            AddChild(_networkPokerTable); _networkPokerTable.Hide();
            _networkPokerSession = session; _networkPokerTableId = session.State.Table;
            _networkPokerTable.FlipRequested += slot =>
            {
                if (!NetworkPokerInputAllowed || !session.Act(PokerAction.Flip, slot)) return;
                _networkPokerTable.PredictLocalFlip(slot);
                TickNetworkPoker(0);
            };
        }
        if (!available)
        {
            PresentNetworkHands(null, poker);
            PresentRoomPokerReactions(null);
            return;
        }
        if (!_networkPokerAttached && _gameManager != null && CurrentMode == Mode.Play)
        {
            _gameManager.BeginRoomPoker(_networkPokerTable);
            BindRoomPokerPaws(_networkPokerTable);
            _networkPokerAttached = true;
            _networkPokerPanel = GD.Load<PackedScene>("res://Scenes/Rooms/Poker/RoomPokerPanel.tscn").Instantiate<RoomPokerPanelView>();
            _infoPanel.SessionContent.AddChild(_networkPokerPanel);
            _networkPokerPanel.ActionRequested += lobby.ActivateLocal;
            _networkPokerPanel.RestartRequested += lobby.RestartLocal;
            _networkPokerPayout = GD.Load<PackedScene>("res://Scenes/Rooms/Poker/RoomPokerPayout.tscn").Instantiate<RoomPokerPayoutView>();
            AddChild(_networkPokerPayout);
            _gameManager.SessionPokerTurnEnd += NetworkPokerEndTurn;
        }
        var view = session.DisplayView;
        if (view == null)
        {
            _networkPokerTable.PresentWaiting();
            if (_networkPokerAttached)
            {
                _gameManager.SetRoomPokerInput(false);
                _networkPokerPanel.PresentWaiting();
                _networkPokerPanel.PresentNetwork(session, lobby.ActionsAvailable);
                _infoPanel.SetSessionChips(null, RoomPokerText.T("牌局筹码", "Table chips"), sessionActive: true);
                _networkPokerPayout.Hide();
            }
            PresentNetworkHands(null, true);
            PresentRoomPokerReactions(null);
            return;
        }
        bool visible = _networkPokerAttached && CurrentMode == Mode.Play && !_hiddenByFullscreenApp && !UiInteractionOccupancy.IsOccupied(this);
        bool act = NetworkPokerInputAllowed && session.Connected && !session.ActionPending && session.State.DealRemaining == 0
            && view.Public.Phase == PokerPhase.Acting && view.Public.ActivePlayerId == view.PlayerId;
        _networkPokerTable.Round.PauseAfterPayout = !session.IsCollecting;
        _networkPokerTable.Round.CollectionDealerId = session.State.Continuation == PokerContinuation.Restarting
            ? session.Context.Members.First(m => m.Key == session.Context.OwnerKey).LocalId : null;
        if (session.IsCollecting) _networkPokerTable.Round.CollectAuthoritative(view);
        _networkPokerTable.Present(view, act, delta, animate: visible,
            otherCardsMoving: visible && _roomPokerDogs?.RemoteDogs.Any(d => d.PokerHand.IsMoving) == true,
            actionPending: session.ActionPending && session.Connected);
        // A late snapshot or background stall must not replay an obsolete settlement before collecting.
        if (view.Public.Phase == PokerPhase.Settled && session.State.SettlementElapsed >= PokerNetworkSession.PayoutDuration(view.Public))
            _networkPokerTable.Round.CompletePayout(view);
        if (_networkPokerAttached)
        {
            _gameManager.SetRoomPokerInput(act && !_networkPokerTable.IsDealing);
            var display = _networkPokerTable.Round.DisplayView;
            _networkPokerPanel.Present(display, session.LastError, _networkPokerTable.Round);
            _networkPokerPanel.PresentNetwork(session, lobby.ActionsAvailable);
            _infoPanel.SetSessionChips(display.Public.Seats.FirstOrDefault(s => s.PlayerId == display.PlayerId)?.Chips,
                RoomPokerText.T("牌局筹码", "Table chips"), sessionActive: true);
            PresentRoomPokerPayout(_networkPokerTable, _networkPokerPayout, view, visible);
        }
        PresentNetworkHands(_networkPokerTable.Round.DisplayView, true);
        PresentRoomPokerReactions(_networkPokerTable);
    }

    private void PresentRoomPokerReactions(RoomPokerTableView table)
    {
        void Apply(DogVisual dog, int id)
        {
            if (GodotObject.IsInstanceValid(dog)) dog.SetRoomPokerReaction(table?.DogReactionFor(id));
        }
        int localId = table?.Round.DisplayView?.PlayerId ?? 0;
        Apply(_bossDogVisual, localId);
        Apply(_gameManager?.RoomPokerDog, localId);
        foreach (var dogs in new[] { _roomPokerDogs, _roomDesktop })
            if (GodotObject.IsInstanceValid(dogs))
                foreach (var dog in dogs.RemoteDogs) Apply(dog.Dog, dog.MemberId);
    }

    private void PresentNetworkHands(PokerPlayerView view, bool show)
    {
        foreach (var dogs in new[] { _roomPokerDogs, _roomDesktop })
        {
            if (!GodotObject.IsInstanceValid(dogs)) continue;
            foreach (var dog in dogs.RemoteDogs)
            {
                if (!show) { dog.PokerHand.Hide(); continue; }
                if (view == null) { dog.PokerHand.PresentWaiting(); continue; }
                var result = view.Public.Settlement?.Players.FirstOrDefault(p => p.PlayerId == dog.MemberId);
                dog.PokerHand.Present(view.Public, dog.MemberId, result?.HoleCards, _networkPokerTable.Emphasis,
                    _networkPokerTable.Showdown.Cue, dogs.IsVisibleInTree(),
                    waiting: !view.Public.Seats.Any(s => s.PlayerId == dog.MemberId),
                    dealDelay: RoomPokerHandView.RemoteDealDelay, round: _networkPokerTable.Round);
            }
            bool local = show && (dogs == _roomDesktop || !_networkPokerAttached);
            if (!local) { dogs.LocalPokerHand.Hide(); continue; }
            if (view == null) dogs.LocalPokerHand.PresentWaiting();
            else dogs.LocalPokerHand.Present(view.Public, _desktopRoomClient.Id, view.HoleCards,
                _networkPokerTable.Emphasis, _networkPokerTable.Showdown.Cue, dogs.IsVisibleInTree(),
                round: _networkPokerTable.Round);
        }
    }
    private bool NetworkPokerInputAllowed => _networkPokerTable != null && CurrentMode == Mode.Play
        && !_hiddenByFullscreenApp && !IsSettingsPanelOverlappingPoker() && !UiInteractionOccupancy.IsOccupied(this)
        && !_networkPokerTable.IsDealing && _roomPokerDogs?.LocalChat.Editing != true;
    private void NetworkPokerEndTurn() { if (NetworkPokerInputAllowed) _networkPokerSession?.Act(PokerAction.EndTurn); }

    private void BindRoomPokerPaws(RoomPokerTableView table)
    {
        table.BindPaws(GetViewport(), (id, right) =>
        {
            var dog = id == table.Round.DisplayView?.PlayerId ? _gameManager?.RoomPokerDog
                : _roomPokerDogs?.RemoteDogs.FirstOrDefault(d => d.MemberId == id)?.Dog;
            return GodotObject.IsInstanceValid(dog) ? dog.PokerPaw(right) : null;
        }, node =>
        {
            var transform = node.GetGlobalTransformWithCanvas();
            if (node.GetViewport() != _playSubViewport) return transform;
            return _playViewport.GetGlobalTransformWithCanvas()
                * new Transform2D(0, _playViewport.Size / _playSubViewport.Size, 0, Vector2.Zero) * transform;
        });
    }

    private void PresentRoomPokerPayout(RoomPokerTableView table, RoomPokerPayoutView payout, PokerPlayerView view, bool visible)
    {
        var sequence = table.Showdown;
        Vector2 FromTable(Vector2 point) => _playViewport.GetGlobalTransformWithCanvas()
            * (point * (_playViewport.Size / _playSubViewport.Size));
        if (visible && table.Round.DealerInFlight && view.Public.Settlement is { } result)
        {
            Vector2? Anchor(int id) => id == view.PlayerId ? FromTable(table.LocalPanel.DealerAnchor)
                : _roomPokerDogs?.RemoteDogs.FirstOrDefault(d => d.MemberId == id)?.PokerHand.DealerAnchor;
            var start = Anchor(view.Public.Dealer.PlayerId);
            var end = Anchor(table.Round.CollectionDealerId ?? result.NextDealer.PlayerId);
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
        PresentRoomPokerReactions(null);
        if (_networkPokerAttached && GodotObject.IsInstanceValid(_gameManager))
        { _gameManager.SessionPokerTurnEnd -= NetworkPokerEndTurn; _gameManager.EndRoomPoker(); }
        if (_networkPokerAttached && GodotObject.IsInstanceValid(_infoPanel)) _infoPanel.SetSessionChips(null);
        foreach (var node in new CanvasItem[] { _networkPokerTable, _networkPokerPanel, _networkPokerPayout })
            if (GodotObject.IsInstanceValid(node)) { node.Hide(); node.GetParent()?.RemoveChild(node); node.QueueFree(); }
        foreach (var dogs in new[] { _roomPokerDogs, _roomDesktop })
            if (GodotObject.IsInstanceValid(dogs))
            { dogs.LocalPokerHand.Hide(); foreach (var dog in dogs.RemoteDogs) dog.PokerHand.Hide(); }
        _networkPokerAttached = false;
        _networkPokerTable = null; _networkPokerPanel = null; _networkPokerPayout = null; _networkPokerSession = null;
    }
}
#endif
