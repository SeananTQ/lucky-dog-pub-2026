#if !DEMO_BUILD && !RECORDING_BUILD
using Godot;

namespace LuckyDogRise.Rooms;

public partial class InGameRoomPreview
{
    [Export] private CenterContainer _connectionWait = null!;
    [Export] private Label _connectionWaitMessage = null!;
    [Export] private Button _connectionWaitCancel = null!;
    private bool _waitingForRoomConnection;
    private double _connectionWaitElapsed;
    private const double ConnectionAdviceDelaySeconds = 10;
    private RoomArrivalState _awaitingArrivalFrame;

    private void InitializeArrivalControls()
    {
        _connectionWaitCancel.Pressed += () =>
        {
            if (_client?.IsLocalArrival == true) OnLeave();
            else CancelRoomReconnect();
            _dirty = true;
        };
    }

    private void UpdateRoomConnection(double delta)
    {
        // Inventory synchronization is not a room outage. Use the current room
        // service plus the arrival retry state, not the inventory's Ready gate.
        bool unavailable = !_isMock && (_roomServiceUnavailable || _service == null || _restartRequired
            || _service is SteamRoomService { IsAvailable: false });
        bool waiting = unavailable || _client?.Arrival?.WaitingForConnection == true;
        if (waiting != _waitingForRoomConnection)
        {
            _waitingForRoomConnection = waiting;
            _connectionWaitElapsed = 0;
            _dirty = true;
            if (waiting)
            {
                CancelKick();
                CancelNameEdit();
                CloseRoomDropdowns();
                HideGameProposal();
            }
            NotifyPageChanged();
        }
        if (!waiting) return;
        double previous = _connectionWaitElapsed;
        _connectionWaitElapsed += System.Math.Max(0, delta);
        if (previous < ConnectionAdviceDelaySeconds && _connectionWaitElapsed >= ConnectionAdviceDelaySeconds)
            _dirty = true;
    }

    private bool RenderConnectionWait()
    {
        _connectionWait.Visible = _waitingForRoomConnection;
        if (!_waitingForRoomConnection) return false;
        _connection.Hide();
        _lobby.Hide();
        _room.Hide();
        _status.Hide();
        _cancel.Hide();
        _reconnectPanel.Hide();
        _create.Disabled = _join.Disabled = _random.Disabled = _refresh.Disabled = true;
        _invite.Disabled = true;
        string message = L10n.Tr(_restartRequired ? "Rooms_SteamRestartRequired" : "Rooms_ConnectingService");
        if (!_restartRequired && _connectionWaitElapsed >= ConnectionAdviceDelaySeconds)
            message += "\n\n" + L10n.Tr("Rooms_ConnectionAdvice");
        _connectionWaitMessage.Text = message;
        bool arrival = _client?.IsLocalArrival == true;
        _connectionWaitCancel.Visible = arrival || Reconnect?.IsPending == true;
        _connectionWaitCancel.Text = L10n.Tr(arrival ? "Rooms_Leave" : "Rooms_ReconnectCancel");
        return true;
    }

    private void RenderArrival()
    {
        var arrival = _client?.Arrival;
        GetNode<Button>("Room/Browse").Disabled = arrival != null;
    }

    private void UpdateArrivalPresentation()
    {
        var arrival = _client?.Arrival;
        if (arrival == null) { _awaitingArrivalFrame = null; return; }
        if (!arrival.Presented && !ReferenceEquals(_awaitingArrivalFrame, arrival)
            && IsVisibleInTree() && !_waitingForRoomConnection)
        {
            _awaitingArrivalFrame = arrival;
            ObserveArrivalAfterDraw(_client, arrival);
        }
        foreach (var (identity, row) in _memberRows)
            row.Modulate = new Color(1, 1, 1, arrival.Phase == RoomArrivalPhase.Departing
                && identity.Id == arrival.HostMemberId ? 1f - (float)arrival.DepartureProgress : 1f);
    }

    private async void ObserveArrivalAfterDraw(RoomClient client, RoomArrivalState arrival)
    {
        // Start the observation time only after the new cast has had a rendered
        // frame. Headless checks have no draw signal, so use two process frames.
        var tree = GetTree();
        if (DisplayServer.GetName() == "headless")
        {
            await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
            if (!GodotObject.IsInstanceValid(this) || _exitingTree || !IsInsideTree()) return;
            await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
        }
        else await tree.ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        if (GodotObject.IsInstanceValid(this) && !_exitingTree && IsInsideTree()
            && ReferenceEquals(_client, client) && ReferenceEquals(client.Arrival, arrival))
        {
            if (IsVisibleInTree() && !_waitingForRoomConnection) client.MarkArrivalPresented();
            else _awaitingArrivalFrame = null;
        }
    }
}
#endif
