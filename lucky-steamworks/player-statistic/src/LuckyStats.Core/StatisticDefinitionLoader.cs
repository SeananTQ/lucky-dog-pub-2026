using System.Text.Json;

namespace LuckyStats.Core;

public static class StatisticDefinitionLoader
{
    public static IReadOnlyList<StatisticDefinition> LoadSynced(string path)
    {
        using var stream = File.OpenRead(path);
        using var document = JsonDocument.Parse(stream);
        if (document.RootElement.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException("tbplayerstatistic.json 的根节点必须是数组。");

        var result = new List<StatisticDefinition>();
        foreach (var row in document.RootElement.EnumerateArray())
        {
            if (!row.GetProperty("SyncToPlatform").GetBoolean())
                continue;

            var apiName = row.GetProperty("PlatformApiName").GetString() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(apiName))
                continue;

            result.Add(new StatisticDefinition(
                row.GetProperty("StatisticId").GetInt32(),
                row.GetProperty("StatisticKey").GetString() ?? string.Empty,
                row.GetProperty("DisplayName").GetString() ?? apiName,
                (StatisticUnit)row.GetProperty("Unit").GetInt32(),
                (StatisticType)row.GetProperty("StatisticType").GetInt32(),
                apiName,
                row.TryGetProperty("Notes", out var notes) ? notes.GetString() ?? string.Empty : string.Empty));
        }

        if (result.Count != 12)
            throw new InvalidDataException($"预期 12 项 SyncToPlatform=true 的统计，实际读取到 {result.Count} 项。");

        return result.OrderBy(x => x.StatisticId).ToArray();
    }
}
