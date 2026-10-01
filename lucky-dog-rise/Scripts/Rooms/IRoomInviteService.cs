#if !DEMO_BUILD && !RECORDING_BUILD
namespace LuckyDogRise.Rooms;

/// <summary>Optional platform invitations; local room simulations do not send Steam invitations.</summary>
public interface IRoomInviteService
{
    bool HasPendingJoinRequest { get; }
    // "ok" means the picker was requested, never that an invitation was sent or accepted.
    string InviteFriends(RoomClient client);
    // Consume the latest explicit join intent once. The page uses its normal RoomClient.Join path.
    bool TryTakeJoinRequest(out string roomCode);
}
#endif
