namespace LuckyDogRise.Rooms;

/// <summary>Optional recovery of a membership interrupted during this process.</summary>
public interface IRoomReconnectService
{
    RoomReconnectContext ReconnectContext { get; }
    bool TryReconnect(RoomClient client);
    void CancelReconnect(RoomClient client);
}
