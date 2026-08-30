using LuckyStats.Core;

var tests = new (string Name, Action Run)[]
{
    ("解析 Steam 全局 JSON", ParseGlobal),
    ("解析 Steam 玩家 JSON 并补零", ParseUser),
    ("开发帐号扣除与派生指标", AnalyzeExclusion),
    ("成熟留存分母", AnalyzeRetentionBaseline),
    ("关系异常检测", DetectInvalidRelations)
};

var failures = 0;
foreach (var test in tests)
{
    try
    {
        test.Run();
        Console.WriteLine($"PASS {test.Name}");
    }
    catch (Exception exception)
    {
        failures++;
        Console.Error.WriteLine($"FAIL {test.Name}: {exception.Message}");
    }
}

return failures == 0 ? 0 : 1;

static void ParseGlobal()
{
    const string json = """{"response":{"result":1,"globalstats":{"A":{"total":"12"},"B":{"total":3},"C":{}}}}""";
    var result = SteamStatsJsonParser.ParseGlobal(json, ["A", "B", "C"]);
    Equal(12L, result.Values["A"]);
    Equal(3L, result.Values["B"]);
    Equal(0L, result.Values["C"]);
}

static void ParseUser()
{
    const string json = """{"playerstats":{"steamID":"76561198000000000","success":true,"stats":[{"name":"A","value":1}]}}""";
    var result = SteamStatsJsonParser.ParseUser(json, ["A", "B"]);
    Equal(1L, result.Values["A"]);
    Equal(0L, result.Values["B"]);
}

static void AnalyzeExclusion()
{
    var values = EmptyValues();
    values["STAT_PT1_COHORT_MEMBER"] = 10;
    values["STAT_PT1_POKER_GUIDANCE_COMPLETED"] = 5;
    values["STAT_PT1_POKER_GUIDANCE_SECONDS"] = 500;
    var developer = EmptyValues();
    developer["STAT_PT1_COHORT_MEMBER"] = 1;
    developer["STAT_PT1_POKER_GUIDANCE_COMPLETED"] = 1;
    developer["STAT_PT1_POKER_GUIDANCE_SECONDS"] = 80;

    var report = Analyze(values, [new AccountFacts("1", "dev", developer)], new Dictionary<int, HistoricalBaseline>());
    Equal(9L, report.Facts.Single(x => x.ApiName == "STAT_PT1_COHORT_MEMBER").AnalyzedValue);
    Equal("105.0 秒", report.Metrics.Single(x => x.Key == "guidance_average_seconds").Value);
}

static void AnalyzeRetentionBaseline()
{
    var values = EmptyValues();
    values["STAT_PT1_COHORT_MEMBER"] = 12;
    values["STAT_PT1_RETURNED_AFTER_24H"] = 4;
    var baselines = new Dictionary<int, HistoricalBaseline>
    {
        [24] = new(24, DateTimeOffset.UtcNow.AddHours(-24), 8, TimeSpan.Zero)
    };
    var report = Analyze(values, [], baselines);
    Equal("50.0%", report.Metrics.Single(x => x.Key == "retention_24h").Value);
    Equal("数据不足", report.Metrics.Single(x => x.Key == "retention_72h").Value);
}

static void DetectInvalidRelations()
{
    var values = EmptyValues();
    values["STAT_PT1_COHORT_MEMBER"] = 2;
    values["STAT_PT1_RETURNED_AFTER_24H"] = 1;
    values["STAT_PT1_RETURNED_AFTER_72H"] = 2;
    values["STAT_PT1_RETURNED_AFTER_168H"] = 3;
    var report = Analyze(values, [], new Dictionary<int, HistoricalBaseline>());
    True(report.Checks.Any(x => x.Severity == CheckSeverity.Error && x.Rule.Contains("168h")));
}

static AnalysisReport Analyze(
    IReadOnlyDictionary<string, long> values,
    IReadOnlyList<AccountFacts> accounts,
    IReadOnlyDictionary<int, HistoricalBaseline> baselines) =>
    new AnalysisEngine().Analyze(new AnalysisInput(DateTimeOffset.UtcNow, Definitions(), values, accounts, baselines));

static Dictionary<string, long> EmptyValues() =>
    Definitions().ToDictionary(x => x.ApiName, _ => 0L, StringComparer.Ordinal);

static IReadOnlyList<StatisticDefinition> Definitions()
{
    var names = new[]
    {
        "STAT_DESKTOP_MODE_SECONDS", "STAT_PT1_COHORT_MEMBER", "STAT_PT1_RETURNED_AFTER_24H",
        "STAT_PT1_RETURNED_AFTER_72H", "STAT_PT1_RETURNED_AFTER_168H", "STAT_PT1_POKER_GUIDANCE_COMPLETED",
        "STAT_PT1_POKER_GUIDANCE_SECONDS", "STAT_PT1_POKER_GUIDANCE_HANDS",
        "STAT_PT1_NEWCOMER_BOX_1_STEAM_CLAIMED", "STAT_PT1_NEWCOMER_BOX_4_STEAM_CLAIMED",
        "STAT_PT1_FIRST_LOOP_BOX_STEAM_CLAIMED", "STAT_PT1_ANY_LINKTREE_REWARD_CLAIMED"
    };
    return names.Select((name, index) => new StatisticDefinition(
        1000 + index, name, name,
        name.Contains("SECONDS", StringComparison.Ordinal) ? StatisticUnit.Seconds : StatisticUnit.Flag,
        name.Contains("SECONDS", StringComparison.Ordinal) ? StatisticType.Counter : StatisticType.Flag,
        name, string.Empty)).ToArray();
}

static void Equal<T>(T expected, T actual)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
        throw new InvalidOperationException($"预期 {expected}，实际 {actual}。");
}

static void True(bool value)
{
    if (!value)
        throw new InvalidOperationException("预期条件为 true。");
}
