using System.Text.Json;

namespace LuckyStats.Core;

public static class StatisticDefinitionLoader
{
    private static readonly string[] RequiredAnalysisApiNames =
    [
        "STAT_DESKTOP_MODE_SECONDS",
        "STAT_PT1_COHORT_MEMBER",
        "STAT_PT1_RETURNED_AFTER_24H",
        "STAT_PT1_RETURNED_AFTER_72H",
        "STAT_PT1_RETURNED_AFTER_168H",
        "STAT_PT1_POKER_GUIDANCE_COMPLETED",
        "STAT_PT1_POKER_GUIDANCE_SECONDS",
        "STAT_PT1_POKER_GUIDANCE_HANDS",
        "STAT_PT1_NEWCOMER_BOX_1_STEAM_CLAIMED",
        "STAT_PT1_NEWCOMER_BOX_4_STEAM_CLAIMED",
        "STAT_PT1_FIRST_LOOP_BOX_STEAM_CLAIMED",
        "STAT_PT1_ANY_LINKTREE_REWARD_CLAIMED"
    ];

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

        var configuredApiNames = result.Select(x => x.ApiName).ToHashSet(StringComparer.Ordinal);
        var missingRequired = RequiredAnalysisApiNames.Where(x => !configuredApiNames.Contains(x)).ToArray();
        if (missingRequired.Length > 0)
            throw new InvalidDataException($"缺少分析工具必需的平台统计：{string.Join(", ", missingRequired)}。");

        return result.OrderBy(x => x.StatisticId).ToArray();
    }
}
