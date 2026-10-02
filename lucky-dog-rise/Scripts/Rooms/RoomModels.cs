namespace LuckyDogRise.Rooms;

// Room data is independent of Godot, Steam, inventory and the development simulator.
// Member Id is a session-local key. A platform adapter maps SteamID64 to this key;
// it must never cast a SteamID64 to int or use a local slot as platform identity.
public sealed record RoomMember(int Id, string Name, int SkinId, int HeadwearId,
    int Reaction, long Presence, long ActivitySequence = 0, bool TongueActive = false,
    bool IsCompanion = false);
// Count is the number of visible characters. Only real Steam members consume
// admission slots; older development providers without companions can omit HumanCount.
public sealed record RoomListing(string Code, string Name, string GameId, int Count, int Capacity,
    int? HumanCount = null)
{
    public bool IsFull => (HumanCount ?? Count) >= Capacity;
}
public enum RoomAccess { Public = 0, FriendsOnly = 1, InviteOnly = 2 }
public sealed record RoomSnapshot(string Code, string Name, string GameId, int OwnerId,
    long Revision, RoomMember[] Members, RoomAccess Access = RoomAccess.Public);
public sealed record RoomChat(long Id, string Code, int SenderId, long Presence,
    string Text, double ExpiresAt);

public static class RoomRules
{
    public const int MaxNameCharacters = 40;
    public static bool TryNormalizeName(string value, out string name)
    {
        name = "";
        if (value == null) return false;
        for (int i = 0; i < value.Length; i++)
        {
            if (char.IsControl(value[i])) return false;
            if (!char.IsSurrogate(value[i])) continue;
            if (!char.IsHighSurrogate(value[i]) || i + 1 >= value.Length || !char.IsLowSurrogate(value[++i]))
                return false;
        }
        name = value.Trim();
        return name.Length is > 0 and <= MaxNameCharacters;
    }
    public static string ValidateChat(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return "Rooms_ChatEmpty";
        if (text.Length > MaxChatCharacters) return "Rooms_ChatTooLong";
        for (int i = 0; i < text.Length; i++)
        {
            if (char.IsControl(text[i])) return "Rooms_ChatSingleLine";
            if (!char.IsSurrogate(text[i])) continue;
            if (!char.IsHighSurrogate(text[i]) || i + 1 >= text.Length || !char.IsLowSurrogate(text[++i]))
                return "Rooms_ChatSingleLine";
        }
        return "";
    }
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
public enum RoomFailure { None, InvalidName, NotFound, Full, Unavailable, NoMatchingRoom, Removed, Banned, AccessDenied }
public sealed record RoomRequest(long Id, RoomOperation Operation, string Value);
public sealed record RoomResult(RoomFailure Failure, RoomListing[] Rooms = null)
{
    public static RoomResult Success { get; } = new(RoomFailure.None);
}
