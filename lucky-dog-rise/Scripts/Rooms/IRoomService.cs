namespace LuckyDogRise.Rooms;

/// <summary>
/// Room/session boundary. Callbacks and all client updates run on the caller's
/// main loop. Implementations own transport and platform handles, never UI nodes.
/// Requests may finish on a later tick. Before committing local membership, call
/// client.TryComplete(request.Id, commit). Cancelled/expired operations cannot commit.
/// A real adapter must also leave any remote membership created by a late success;
/// cleanup must target that operation's resource, never the client's newer room.
/// Same-room retries require serialized remote joins or operation-owned leases:
/// leaving a stale LobbyId alone could also evict a newer successful membership.
/// Cancel and Leave must be idempotent and must not throw.
/// </summary>
public interface IRoomService
{
    // Monotonic seconds in this service's time domain. Client.AdvanceTo and
    // RoomChat.ExpiresAt must use this clock, not an independent engine clock.
    double Now { get; }
    void Request(RoomClient client, RoomRequest request);
    void Cancel(RoomClient client, long requestId);
    void Leave(RoomClient client);
    void UpdateAppearance(RoomClient client);
    void UpdateActivity(RoomClient client);
    string Kick(RoomClient client, int memberId, long presence);
    string SetGame(RoomClient client, string gameId);
    string SetAccess(RoomClient client, RoomAccess access);
    string SendChat(RoomClient client, string text);
}
