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
        bool failed = arrival?.Phase == RoomArrivalPhase.Failed;
        _arrivalPanel.Visible = failed;
        _arrivalRetry.Visible = failed;
        _arrivalRetry.Disabled = _client?.IsBusy == true;
        _arrivalMessage.Text = failed ? L10n.Tr("Rooms_ArrivalFailed") : "";
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
