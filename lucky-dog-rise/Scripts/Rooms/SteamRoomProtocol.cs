#if !DEMO_BUILD && !RECORDING_BUILD
using System;
using System.Globalization;
using System.Collections.Generic;
using System.Linq;
using System.Buffers.Binary;
using System.Text;

namespace LuckyDogRise.Rooms;

// A lobby id is encoded losslessly, without a mapping server or collision-prone short hash.
public static class SteamRoomProtocol
{
    // Older owners do not reconcile admission policy during ownership transfer.
    public const string Version = "lucky-dog-room-3";
    public const string ProtocolKey = "ld_protocol";
    public const string NameKey = "ld_name";
    public const string GameKey = "ld_game";
    public const string AppearanceKey = "ld_appearance";
    public const string ActivityKey = "ld_activity";
    public const string ChatSessionKey = "ld_chat_session";
    public const string BanListKey = "ld_bans";
    public const string AccessKey = "ld_access";
    public const int MaxBannedMembers = 256;
    public const int MaxChatBytes = 512;
    private static readonly UTF8Encoding ChatEncoding = new(false, true);

    public static bool IsAccessValid(RoomAccess access)
        => access is RoomAccess.Public or RoomAccess.FriendsOnly or RoomAccess.InviteOnly;

    public static string EncodeAccess(RoomAccess access) => access switch
    {
        RoomAccess.Public => "public",
        RoomAccess.FriendsOnly => "friends",
        RoomAccess.InviteOnly => "invite",
        _ => throw new ArgumentOutOfRangeException(nameof(access))
    };

    public static RoomAccess DecodeAccess(string value) => value switch
    {
        "public" => RoomAccess.Public,
        "friends" => RoomAccess.FriendsOnly,
        "invite" => RoomAccess.InviteOnly,
        _ => (RoomAccess)(-1)
    };

    // Steam has no transaction or lobby-type getter. Apply the real admission
    // rule before advertising it. A failed second write must restore native
    // admission; an uncertain rollback is an explicit session error.
    public static bool WriteAccess(RoomAccess previous, RoomAccess next,
        Func<RoomAccess, bool> writeNative, Func<RoomAccess, bool> writeMetadata)
    {
        if (!IsAccessValid(previous) || !IsAccessValid(next)) return false;
        if (!writeNative(next)) return false;
        if (previous == next) return true; // Ownership reconciliation, no metadata churn.
        bool metadataUncertain = false;
        try
        {
            if (writeMetadata(next)) return true;
        }
        catch { metadataUncertain = true; }
        if (!writeNative(previous) || metadataUncertain && !writeMetadata(previous))
            throw new InvalidOperationException("Steam lobby admission rollback could not be confirmed.");
        return false;
    }

    // Single owner-written value, small enough for Steam lobby metadata. Never
    // silently discard old bans when this bounded list fills up.
    public static string EncodeBannedMembers(IEnumerable<ulong> members)
    {
        var ids = members.Distinct().OrderBy(id => id).ToArray();
        if (ids.Length > MaxBannedMembers || ids.Contains(0UL))
            throw new ArgumentOutOfRangeException(nameof(members));
        return "1:" + string.Join(",", ids.Select(id => id.ToString("X16", CultureInfo.InvariantCulture)));
    }

    public static bool TryDecodeBannedMembers(string value, out HashSet<ulong> members)
    {
        members = new HashSet<ulong>();
        if (value == null || value.Length > 2 + MaxBannedMembers * 17
            || !value.StartsWith("1:", StringComparison.Ordinal)) return false;
        if (value.Length == 2) return true;
        var entries = value[2..].Split(',');
        if (entries.Length > MaxBannedMembers) return false;
        foreach (var entry in entries)
            if (entry.Length != 16 || !ulong.TryParse(entry, NumberStyles.AllowHexSpecifier,
                CultureInfo.InvariantCulture, out var id) || id == 0 || !members.Add(id)) return false;
        return true;
    }

    // A management prompt, never text chat. The authoritative ban is lobby data;
    // the membership token prevents delayed prompts targeting another visit.
    public static byte[] EncodeKick(ulong target, Guid membership)
    {
        var bytes = new byte[28];
        "LDK1"u8.CopyTo(bytes);
        BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(4), target);
        membership.TryWriteBytes(bytes.AsSpan(12));
        return bytes;
    }

    public static bool TryDecodeKick(byte[] bytes, out ulong target, out Guid membership)
    {
        target = 0;
        membership = Guid.Empty;
        if (bytes == null || bytes.Length != 28 || !bytes.AsSpan(0, 4).SequenceEqual("LDK1"u8)) return false;
        target = BinaryPrimitives.ReadUInt64LittleEndian(bytes.AsSpan(4));
        membership = new Guid(bytes.AsSpan(12));
        return target != 0 && membership != Guid.Empty;
    }

    // Binary envelope: magic/version, per-membership token, sequence, Steam time,
    // UTF-8 payload. Identity always comes from Steam's callback, never this body.
    public static byte[] EncodeChat(Guid session, long sequence, long sentAt, string text)
    {
        var body = ChatEncoding.GetBytes(text);
        var bytes = new byte[36 + body.Length];
        "LDC1"u8.CopyTo(bytes);
        session.TryWriteBytes(bytes.AsSpan(4, 16));
        BinaryPrimitives.WriteInt64LittleEndian(bytes.AsSpan(20), sequence);
        BinaryPrimitives.WriteInt64LittleEndian(bytes.AsSpan(28), sentAt);
        body.CopyTo(bytes, 36);
        return bytes;
    }

    public static bool TryDecodeChat(byte[] bytes, out Guid session, out long sequence, out long sentAt, out string text)
    {
        session = Guid.Empty;
        sequence = sentAt = 0;
        text = "";
        if (bytes == null || bytes.Length is <= 36 or > MaxChatBytes
            || !bytes.AsSpan(0, 4).SequenceEqual("LDC1"u8)) return false;
        session = new Guid(bytes.AsSpan(4, 16));
        sequence = BinaryPrimitives.ReadInt64LittleEndian(bytes.AsSpan(20));
        sentAt = BinaryPrimitives.ReadInt64LittleEndian(bytes.AsSpan(28));
        if (session == Guid.Empty || sequence <= 0 || sentAt <= 0) return false;
        try { text = ChatEncoding.GetString(bytes, 36, bytes.Length - 36); }
        catch (DecoderFallbackException) { return false; }
        return RoomRules.ValidateChat(text).Length == 0;
    }
    private const string Alphabet = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";

    public static string Encode(ulong lobbyId)
    {
        if (lobbyId == 0) throw new ArgumentOutOfRangeException(nameof(lobbyId));
        Span<char> code = stackalloc char[13];
        for (var index = 12; index >= 0; index--)
        {
            code[index] = Alphabet[(int)(lobbyId & 31)];
            lobbyId >>= 5;
        }
        return "LD-" + new string(code);
    }

    public static bool TryDecode(string code, out ulong lobbyId)
    {
        lobbyId = 0;
        if (code == null || code.Length > 32) return false;
        code = code.Trim().ToUpperInvariant();
        if (!code.StartsWith("LD-", StringComparison.Ordinal) || code.Length != 16) return false;
        for (var index = 3; index < code.Length; index++)
        {
            var value = Alphabet.IndexOf(code[index]);
            if (value < 0 || index == 3 && value > 15) { lobbyId = 0; return false; }
            lobbyId = (lobbyId << 5) | (uint)value;
        }
        return lobbyId != 0;
    }

    public static bool IsNameValid(string name) => !string.IsNullOrWhiteSpace(name)
        && name.Trim().Length <= 40 && !name.Any(char.IsControl);

    public static bool IsGameValid(string game) => !string.IsNullOrEmpty(game)
        && game.Length <= 32 && game.All(c => c is >= 'a' and <= 'z' or >= '0' and <= '9' or '_' or '-');

    public static string EncodeAppearance(int skin, int headwear, int reaction)
        => string.Create(CultureInfo.InvariantCulture, $"1:{skin}:{headwear}:{reaction}");

    public static bool TryDecodeAppearance(string value, out int skin, out int headwear, out int reaction)
    {
        skin = headwear = reaction = 0;
        if (value == null || value.Length > 64) return false;
        var parts = value.Split(':');
        return parts.Length == 4 && parts[0] == "1"
            && int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out skin) && skin > 0
            && int.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out headwear)
            && int.TryParse(parts[3], NumberStyles.None, CultureInfo.InvariantCulture, out reaction) && reaction > 0;
    }

    public static string SafePersonaName(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "Steam player";
        return new string(value.Where(c => !char.IsControl(c)).Take(64).ToArray());
    }

    // Activity remains separate from appearance; missing activity uses idle defaults.
    public static string EncodeActivity(long sequence, bool active)
        => string.Create(CultureInfo.InvariantCulture, $"1:{sequence}:{(active ? 1 : 0)}");

    public static bool TryDecodeActivity(string value, out long sequence, out bool active)
    {
        sequence = 0;
        active = false;
        if (value == null || value.Length > 32) return false;
        var parts = value.Split(':');
        if (parts.Length != 3 || parts[0] != "1" || parts[2] is not ("0" or "1")
            || !long.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out sequence)
            || sequence < 0) { sequence = 0; return false; }
        active = parts[2] == "1";
        return true;
    }
}
#endif
