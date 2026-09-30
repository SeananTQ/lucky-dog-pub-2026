#if !DEMO_BUILD && !RECORDING_BUILD
using System;
using System.Globalization;
using System.Linq;

namespace LuckyDogRise.Rooms;

// A lobby id is encoded losslessly, without a mapping server or collision-prone short hash.
public static class SteamRoomProtocol
{
    public const string Version = "lucky-dog-room-1";
    public const string ProtocolKey = "ld_protocol";
    public const string NameKey = "ld_name";
    public const string GameKey = "ld_game";
    public const string AppearanceKey = "ld_appearance";
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
}
#endif
