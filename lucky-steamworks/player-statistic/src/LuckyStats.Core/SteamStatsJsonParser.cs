using System.Globalization;
using System.Text.Json;

namespace LuckyStats.Core;

public static class SteamStatsJsonParser
{
    public static ParsedSteamResponse ParseGlobal(string json, IReadOnlyCollection<string> expectedApiNames)
    {
        using var document = JsonDocument.Parse(json);
        var response = document.RootElement.GetProperty("response");
        if (response.TryGetProperty("result", out var result) && result.GetInt32() != 1)
            throw new InvalidDataException($"Steam 全局统计查询失败，result={result.GetRawText()}。");

        if (!response.TryGetProperty("globalstats", out var globalStats))
            throw new InvalidDataException("Steam 响应缺少 response.globalstats。");

        var values = new Dictionary<string, long>(StringComparer.Ordinal);
        foreach (var apiName in expectedApiNames)
        {
            if (!globalStats.TryGetProperty(apiName, out var stat))
            {
                values[apiName] = 0;
                continue;
            }

            values[apiName] = stat.TryGetProperty("total", out var total)
                ? ReadLong(total)
                : 0;
        }

        return new ParsedSteamResponse(null, values, json);
    }

    public static ParsedSteamResponse ParseUser(string json, IReadOnlyCollection<string> expectedApiNames)
    {
        using var document = JsonDocument.Parse(json);
        var playerStats = document.RootElement.GetProperty("playerstats");
        if (playerStats.TryGetProperty("success", out var success) && !success.GetBoolean())
        {
            var error = playerStats.TryGetProperty("error", out var errorElement)
                ? errorElement.GetString()
                : "未知错误";
            throw new InvalidDataException($"Steam 玩家统计查询失败：{error}");
        }

        var steamId = playerStats.TryGetProperty("steamID", out var id)
            ? id.GetString()
            : null;
        var values = expectedApiNames.ToDictionary(x => x, _ => 0L, StringComparer.Ordinal);
        if (playerStats.TryGetProperty("stats", out var stats))
        {
            foreach (var stat in stats.EnumerateArray())
            {
                var name = stat.GetProperty("name").GetString();
                if (name is not null && values.ContainsKey(name))
                    values[name] = ReadLong(stat.GetProperty("value"));
            }
        }

        return new ParsedSteamResponse(steamId, values, json);
    }

    private static long ReadLong(JsonElement value)
    {
        return value.ValueKind switch
        {
            JsonValueKind.Number => value.TryGetInt64(out var number)
                ? number
                : checked((long)value.GetDouble()),
            JsonValueKind.String when long.TryParse(
                value.GetString(),
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var number) => number,
            _ => throw new InvalidDataException($"无法把 Steam 数值 {value.GetRawText()} 解析为整数。")
        };
    }
}
