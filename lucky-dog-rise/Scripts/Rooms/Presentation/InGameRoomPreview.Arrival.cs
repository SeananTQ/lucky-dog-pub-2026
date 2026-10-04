#if !DEMO_BUILD && !RECORDING_BUILD
using Godot;

namespace LuckyDogRise.Rooms;

public partial class InGameRoomPreview
{
    private VBoxContainer _arrivalPanel;
    private Label _arrivalMessage;
    private Button _arrivalRetry;
    private RoomArrivalState _awaitingArrivalFrame;

    private void InitializeArrivalControls()
    {
        _arrivalPanel = GetNode<VBoxContainer>("Arrival");
        _arrivalMessage = GetNode<Label>("Arrival/Message");
        _arrivalRetry = GetNode<Button>("Arrival/Retry");
        _arrivalRetry.Pressed += () =>
        {
            RefreshService();
            _noticeKey = "";
            _client?.RetryArrival();
            _dirty = true;
        };
    }

    private void RenderArrival()
    {
        var arrival = _client?.Arrival;
        _arrivalPanel.Visible = arrival != null;
        _arrivalRetry.Visible = arrival?.Phase == RoomArrivalPhase.Failed;
        _arrivalRetry.Disabled = _client?.IsBusy == true;
        _arrivalMessage.Text = arrival == null ? "" : L10n.Tr(arrival.Phase switch
        {
            RoomArrivalPhase.Failed => "Rooms_ArrivalFailed",
            RoomArrivalPhase.Departing => "Rooms_ArrivalDeparting",
            _ => "Rooms_ArrivalPreparing"
        });
        GetNode<Button>("Room/Browse").Disabled = arrival != null;
    }

    private void UpdateArrivalPresentation()
    {
        var arrival = _client?.Arrival;
        if (arrival == null) { _awaitingArrivalFrame = null; return; }
        if (!arrival.Presented && !ReferenceEquals(_awaitingArrivalFrame, arrival) && IsVisibleInTree())
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
            if (IsVisibleInTree()) client.MarkArrivalPresented();
            else _awaitingArrivalFrame = null;
        }
    }
}
#endif
