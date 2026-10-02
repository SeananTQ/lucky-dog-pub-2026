#if !DEMO_BUILD && !RECORDING_BUILD
using System;
using System.Globalization;
using System.Collections.Generic;
using System.Linq;
using System.Buffers.Binary;
using System.Text;

namespace LuckyDogRise.Rooms;

public sealed record SteamRoomGameVoter(ulong SteamId, Guid Session, int CompanionId = 0, long CompanionPresence = 0);
public sealed record SteamRoomGameProposal(Guid Id, string Target, ulong Owner, Guid OwnerSession,
    long StartedAt, long ExpiresAt, SteamRoomGameVoter[] Voters);
public sealed record SteamRoomGameState(long Revision, string Game, SteamRoomGameProposal Proposal = null);

// A lobby id is encoded losslessly, without a mapping server or collision-prone short hash.
public static class SteamRoomProtocol
{
    // Earlier clients can bypass voting and cannot enforce work-mode chat rules.
    public const string Version = "lucky-dog-room-5";
    public const string ProtocolKey = "ld_protocol";
    public const string NameKey = "ld_name";
    public const string GameKey = "ld_game";
    public const string GameVoteKey = "ld_game_vote";
    public const string CreatedAtKey = "ld_created";
    public const string AppearanceKey = "ld_appearance";
    public const string ActivityKey = "ld_activity";
    public const string ChatSessionKey = "ld_chat_session";
    public const string BanListKey = "ld_bans";
    public const string AccessKey = "ld_access";
    public const string CompanionsKey = "ld_companions";
    public const int MaxBannedMembers = 256;
    public const int MaxChatBytes = 512;
    private static readonly UTF8Encoding ChatEncoding = new(false, true);

    // Current mode and the entire proposal share one owner-written key. A partial
    // metadata write can never switch the mode while leaving an old vote active.
    public static string EncodeGameState(SteamRoomGameState state)
    {
        string prefix = $"1|{state.Revision.ToString(CultureInfo.InvariantCulture)}|{state.Game}";
        if (state.Proposal is not { } p) return prefix;
        return prefix + string.Create(CultureInfo.InvariantCulture,
            $"|{p.Id:N}|{p.Target}|{p.Owner:X16}|{p.OwnerSession:N}|{p.StartedAt}|{p.ExpiresAt}|")
            + string.Join(",", p.Voters.Select(v => v.SteamId != 0
                ? $"h{v.SteamId:X16}.{v.Session:N}"
                : string.Create(CultureInfo.InvariantCulture, $"b{v.CompanionId}.{v.CompanionPresence}")));
    }

    public static bool TryDecodeGameState(string value, out SteamRoomGameState state)
    {
        state = null;
        if (value == null || value.Length > 1024) return false;
        var p = value.Split('|');
        if (p.Length is not (3 or 10) || p[0] != "1"
            || !long.TryParse(p[1], NumberStyles.None, CultureInfo.InvariantCulture, out var revision) || revision <= 0
            || !RoomRules.IsRoomMode(p[2])) return false;
        if (p.Length == 3) { state = new(revision, p[2]); return true; }
        if (!Guid.TryParseExact(p[3], "N", out var id) || id == Guid.Empty || !RoomRules.IsRoomMode(p[4]) || p[4] == p[2]
            || p[5].Length != 16 || !ulong.TryParse(p[5], NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var owner) || owner == 0
            || !Guid.TryParseExact(p[6], "N", out var ownerSession) || ownerSession == Guid.Empty
            || !long.TryParse(p[7], NumberStyles.None, CultureInfo.InvariantCulture, out var start) || start <= 0
            || !long.TryParse(p[8], NumberStyles.None, CultureInfo.InvariantCulture, out var end) || end <= start
            || end - start != (long)RoomRules.GameChangeLifetime) return false;
        var entries = p[9].Split(',');
        if (entries.Length is < 1 or > RoomRules.Capacity) return false;
        var voters = new List<SteamRoomGameVoter>();
        foreach (var entry in entries)
        {
            var parts = entry.Split('.');
            if (parts.Length != 2 || parts[0].Length < 2) return false;
            if (parts[0][0] == 'h')
            {
                if (parts[0].Length != 17 || !ulong.TryParse(parts[0][1..], NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var steam)
                    || steam == 0 || !Guid.TryParseExact(parts[1], "N", out var session) || session == Guid.Empty
                    || voters.Any(v => v.SteamId == steam)) return false;
                voters.Add(new(steam, session));
            }
            else if (parts[0][0] == 'b')
            {
                if (!int.TryParse(parts[0][1..], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var bot) || bot is < -3 or > -1
                    || !long.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var presence) || presence <= 0
                    || voters.Any(v => v.CompanionId == bot)) return false;
                voters.Add(new(0, Guid.Empty, bot, presence));
            }
            else return false;
        }
        if (!voters.Any(v => v.SteamId == owner && v.Session == ownerSession)) return false;
        state = new(revision, p[2], new(id, p[4], owner, ownerSession, start, end, voters.ToArray()));
        return true;
    }

    public static string EncodeGameVote(Guid proposal, Guid session, long sequence, bool accept)
        => string.Create(CultureInfo.InvariantCulture, $"1:{proposal:N}:{session:N}:{sequence}:{(accept ? 1 : 0)}");
    public static bool TryDecodeGameVote(string value, out Guid proposal, out Guid session, out long sequence, out bool accept)
    {
        proposal = session = Guid.Empty; sequence = 0; accept = false;
        if (value == null || value.Length > 100) return false;
        var parts = value.Split(':');
        if (parts.Length != 5 || parts[0] != "1" || parts[4] is not ("0" or "1")
            || !Guid.TryParseExact(parts[1], "N", out proposal) || proposal == Guid.Empty
            || !Guid.TryParseExact(parts[2], "N", out session) || session == Guid.Empty
            || !long.TryParse(parts[3], NumberStyles.None, CultureInfo.InvariantCulture, out sequence) || sequence <= 0) return false;
        accept = parts[4] == "1";
        return true;
    }

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
    public static byte[] EncodeChat(Guid session, long sequence, long sentAt, string text, long modeRevision = 1)
    {
        var body = ChatEncoding.GetBytes(text);
        var bytes = new byte[44 + body.Length];
        "LDC2"u8.CopyTo(bytes);
        session.TryWriteBytes(bytes.AsSpan(4, 16));
        BinaryPrimitives.WriteInt64LittleEndian(bytes.AsSpan(20), sequence);
        BinaryPrimitives.WriteInt64LittleEndian(bytes.AsSpan(28), sentAt);
        BinaryPrimitives.WriteInt64LittleEndian(bytes.AsSpan(36), modeRevision);
        body.CopyTo(bytes, 44);
        return bytes;
    }

    public static bool TryDecodeChat(byte[] bytes, out Guid session, out long sequence, out long sentAt, out string text)
        => TryDecodeChat(bytes, out session, out sequence, out sentAt, out text, out _);

    public static bool TryDecodeChat(byte[] bytes, out Guid session, out long sequence, out long sentAt, out string text, out long modeRevision)
    {
        session = Guid.Empty;
        sequence = sentAt = modeRevision = 0;
        text = "";
        if (bytes == null || bytes.Length is <= 44 or > MaxChatBytes
            || !bytes.AsSpan(0, 4).SequenceEqual("LDC2"u8)) return false;
        session = new Guid(bytes.AsSpan(4, 16));
        sequence = BinaryPrimitives.ReadInt64LittleEndian(bytes.AsSpan(20));
        sentAt = BinaryPrimitives.ReadInt64LittleEndian(bytes.AsSpan(28));
        modeRevision = BinaryPrimitives.ReadInt64LittleEndian(bytes.AsSpan(36));
        if (session == Guid.Empty || sequence <= 0 || sentAt <= 0 || modeRevision <= 0) return false;
        try { text = ChatEncoding.GetString(bytes, 44, bytes.Length - 44); }
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
        return new string(code);
    }

    public static bool TryDecode(string code, out ulong lobbyId)
    {
        lobbyId = 0;
        if (code == null || code.Length > 32) return false;
        code = code.Trim().ToUpperInvariant();
        // Display/copy the shorter code; previously shared codes remain usable.
        if (code.StartsWith("LD-", StringComparison.Ordinal)) code = code[3..];
        if (code.Length != 13) return false;
        for (var index = 0; index < code.Length; index++)
        {
            var value = Alphabet.IndexOf(code[index]);
            if (value < 0 || index == 0 && value > 15) { lobbyId = 0; return false; }
            lobbyId = (lobbyId << 5) | (uint)value;
        }
        return lobbyId != 0;
    }

    public static bool IsNameValid(string name) => RoomRules.TryNormalizeName(name, out _);

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
