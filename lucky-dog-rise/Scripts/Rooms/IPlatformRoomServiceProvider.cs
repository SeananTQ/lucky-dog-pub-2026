#if !DEMO_BUILD && !RECORDING_BUILD
namespace LuckyDogRise.Rooms;

/// <summary>The current Steam session's room service; null while offline or in a sandbox.</summary>
public interface IPlatformRoomServiceProvider
{
    IRoomService RoomService { get; }
    bool RoomRestartRequired => false;
}
#endif
