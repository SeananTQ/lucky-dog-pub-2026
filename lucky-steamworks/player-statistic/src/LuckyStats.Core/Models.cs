namespace LuckyStats.Core;

public enum StatisticUnit
{
    Count = 1,
    Chips = 2,
    Seconds = 3,
    Flag = 4
}

public enum StatisticType
{
    Counter = 1,
    Peak = 2,
    Flag = 3
}

public enum CheckSeverity
{
    Info,
    Warning,
    Error
}

public sealed record StatisticDefinition(
    int StatisticId,
    string StatisticKey,
    string DisplayName,
    StatisticUnit Unit,
    StatisticType Type,
    string ApiName,
    string Notes);

public sealed record AccountFacts(
    string SteamId,
    string Label,
    IReadOnlyDictionary<string, long> Values,
    bool Available = true,
    string? Error = null);

public sealed record HistoricalBaseline(
    int Hours,
    DateTimeOffset CapturedAtUtc,
    long CohortCount,
    TimeSpan DistanceFromCutoff);

public sealed record AnalysisInput(
    DateTimeOffset CapturedAtUtc,
    IReadOnlyList<StatisticDefinition> Definitions,
    IReadOnlyDictionary<string, long> GlobalValues,
    IReadOnlyList<AccountFacts> ExcludedAccounts,
    IReadOnlyDictionary<int, HistoricalBaseline> RetentionBaselines);

public sealed record FactRow(
    string ApiName,
    string DisplayName,
    string Unit,
    long GlobalValue,
    long ExcludedValue,
    long AnalyzedValue);

public sealed record MetricResult(
    string Group,
    string Key,
    string DisplayName,
    string Value,
    string Formula,
    string Status,
    double? NumericValue = null);

public sealed record ValidationResult(
    CheckSeverity Severity,
    string Rule,
    string Message);

public sealed record AnalysisReport(
    DateTimeOffset CapturedAtUtc,
    IReadOnlyList<FactRow> Facts,
    IReadOnlyList<MetricResult> Metrics,
    IReadOnlyList<ValidationResult> Checks,
    IReadOnlyList<string> ExcludedAccountLabels);

public sealed record ParsedSteamResponse(
    string? SteamId,
    IReadOnlyDictionary<string, long> Values,
    string RawJson);

public sealed record HistoryPoint(
    DateTimeOffset CapturedAtUtc,
    string ApiName,
    long GlobalValue,
    long ExcludedValue,
    long AnalyzedValue);
