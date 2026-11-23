using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace LuckyDogRise;

// The Windows Playtest client reads its signed-in account's Steam library cache.
// Release grants are verified by Steam Inventory.
internal static class PlaytestParticipationPlaytime
{
    internal readonly record struct Gift(int AppId, int BundleId, string PromoRule);

    private static readonly string[] AppsPath = ["UserLocalConfigStore", "Software", "Valve", "Steam", "apps"];
    private static readonly Regex Entry = new(
        """\G(?:\s|//[^\r\n]*)*(?:\}|"(?'key'(?:[^"\\]|\\.)*)"\s*(?:(?'open'\{)|"(?'value'(?:[^"\\]|\\.)*)"))""",
        RegexOptions.CultureInvariant);

    internal static HashSet<int> Read(uint accountId, Gift[] gifts)
    {
        if (!OperatingSystem.IsWindows() || accountId == 0)
            return null;
        try
        {
            var steam = Registry.GetValue(@"HKEY_CURRENT_USER\Software\Valve\Steam", "SteamPath", null) as string
                ?? Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node\Valve\Steam", "InstallPath", null) as string;
            if (string.IsNullOrWhiteSpace(steam) || !Path.IsPathFullyQualified(steam))
                return null;
            var path = Path.Combine(steam, "userdata", accountId.ToString(CultureInfo.InvariantCulture), "config", "localconfig.vdf");
            using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(file);
            return Evaluate(reader.ReadToEnd(), gifts);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException or ArgumentException)
        {
            return null;
        }
    }

    internal static HashSet<int> Evaluate(string config, Gift[] gifts)
    {
        var sections = new List<string>();
        var minutes = new Dictionary<int, int>();
        bool inApps = false;
        for (var entry = Entry.Match(config); entry.Success; entry = entry.NextMatch())
        {
            var key = entry.Groups["key"];
            if (!key.Success)
            {
                if (sections.Count == 0)
                    break;
                if (inApps && sections.Count == AppsPath.Length)
                    return EvaluateGifts(minutes, gifts);
                sections.RemoveAt(sections.Count - 1);
                continue;
            }
            if (entry.Groups["open"].Success)
            {
                sections.Add(key.Value);
                if (!inApps && sections.Count == AppsPath.Length)
                    inApps = sections.SequenceEqual(AppsPath, StringComparer.OrdinalIgnoreCase);
                continue;
            }
            if (inApps && sections.Count == AppsPath.Length + 1
                && key.Value.Equals("Playtime", StringComparison.OrdinalIgnoreCase)
                && int.TryParse(sections[^1], NumberStyles.None, CultureInfo.InvariantCulture, out var appId)
                && gifts.Any(gift => gift.AppId == appId))
            {
                if (!int.TryParse(entry.Groups["value"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var value))
                    return null;
                minutes[appId] = value;
            }
        }
        // Incomplete or unfamiliar file layout. Retry on the next refresh.
        return null;
    }

    private static HashSet<int> EvaluateGifts(Dictionary<int, int> minutes, Gift[] gifts)
    {
        var eligible = new HashSet<int>();
        foreach (var gift in gifts)
        {
            var condition = gift.PromoRule.Split('/');
            if (condition[0] != "played:" + gift.AppId.ToString(CultureInfo.InvariantCulture) || condition.Length > 2)
                return null;
            var threshold = 1;
            if (condition.Length == 2 && (!int.TryParse(condition[1], NumberStyles.None, CultureInfo.InvariantCulture, out threshold) || threshold < 1))
                return null;
            if (minutes.TryGetValue(gift.AppId, out var played) && played >= threshold)
                eligible.Add(gift.BundleId);
        }
        return eligible;
    }
}
