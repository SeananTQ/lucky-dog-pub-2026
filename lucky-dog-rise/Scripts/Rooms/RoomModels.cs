namespace LuckyDogRise.Rooms;

// Room data is independent of Godot, Steam, inventory and the development simulator.
// Member Id is a session-local key. A platform adapter maps SteamID64 to this key;
// it must never cast a SteamID64 to int or use a local slot as platform identity.
public sealed record RoomMember(int Id, string Name, int SkinId, int HeadwearId,
    int Reaction, long Presence, long ActivitySequence = 0, bool TongueActive = false);
public sealed record RoomListing(string Code, string Name, string GameId, int Count, int Capacity);
public sealed record RoomSnapshot(string Code, string Name, string GameId, int OwnerId,
    long Revision, RoomMember[] Members);
public sealed record RoomChat(long Id, string Code, int SenderId, long Presence,
    string Text, double ExpiresAt);

public static class RoomRules
{
    public const int Capacity = 6;
    public const int MaxChatCharacters = 120;
    public const double ChatLifetime = 6;
    public const double ChatCooldown = 1.5;
    public const double RequestTimeout = 15;
    // Animation runs locally. Only active/idle transitions and a bounded renewal
    // are sent; no key contents, input counts or animation frames cross the room.
    public const double InputActivityHold = 0.7;
    public const double ActivityRenewal = 1;
    public const double ActivityLease = 3;
}

public enum RoomOperation { None, Search, Create, Join }
public enum RoomRequestState { Idle, Pending, Succeeded, Failed, Cancelled, TimedOut }
public enum RoomFailure { None, InvalidName, NotFound, Full, Unavailable, NoMatchingRoom }
public sealed record RoomRequest(long Id, RoomOperation Operation, string Value);
public sealed record RoomResult(RoomFailure Failure, RoomListing[] Rooms = null)
{
    public static RoomResult Success { get; } = new(RoomFailure.None);
}
