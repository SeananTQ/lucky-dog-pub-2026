using System.Text.Json;
using LuckyStats.Core;

namespace LuckyStats.Infrastructure;

public sealed record SyncResult(long BatchId, AnalysisReport Report);

public sealed class StatisticsApplication : IDisposable
{
    private readonly AnalyzerPaths _paths;
    private readonly IReadOnlyList<StatisticDefinition> _definitions;
    private readonly IReadOnlyList<ExcludedAccountSetting> _excludedAccounts;
    private readonly SqliteSnapshotStore _store;
    private readonly AnalysisEngine _analysisEngine = new();

    private StatisticsApplication(
        AnalyzerPaths paths,
        IReadOnlyList<StatisticDefinition> definitions,
        IReadOnlyList<ExcludedAccountSetting> excludedAccounts,
        SqliteSnapshotStore store)
    {
        _paths = paths;
        _definitions = definitions;
        _excludedAccounts = excludedAccounts;
        _store = store;
    }

    public AnalyzerPaths Paths => _paths;
    public IReadOnlyList<StatisticDefinition> Definitions => _definitions;
    public IReadOnlyList<ExcludedAccountSetting> ExcludedAccounts => _excludedAccounts;

    public static async Task<StatisticsApplication> CreateAsync(
        string? startDirectory = null,
        CancellationToken cancellationToken = default)
    {
        var paths = ProjectPaths.Discover(startDirectory);
        var definitions = StatisticDefinitionLoader.LoadSynced(paths.DefinitionsFile);
        var excluded = ProjectPaths.LoadEnabledDeveloperAccounts(paths.DeveloperAllowlistFile);
        var store = new SqliteSnapshotStore(paths.DatabaseFile);
        await store.InitializeAsync(cancellationToken);
        return new StatisticsApplication(paths, definitions, excluded, store);
    }

    public async Task<SyncResult> SyncGlobalAsync(CancellationToken cancellationToken = default)
    {
        var capturedAt = DateTimeOffset.UtcNow;
        using var api = new SteamWebApiClient(_paths.KeyFile);
        var global = await api.GetGlobalStatsAsync(_definitions, cancellationToken);
        var captures = new List<CaptureAccountData>
        {
            new("global", null, "Steam 全局累计", global.Values, global.RawJson)
        };

        foreach (var account in _excludedAccounts)
        {
            try
            {
                var user = await api.GetUserStatsAsync(account.SteamId, _definitions, cancellationToken);
                captures.Add(new CaptureAccountData(
                    "excluded", account.SteamId, account.Label, user.Values, user.RawJson));
            }
            catch (Exception exception) when (exception is HttpRequestException or InvalidDataException)
            {
                captures.Add(new CaptureAccountData(
                    "excluded", account.SteamId, account.Label,
                    new Dictionary<string, long>(StringComparer.Ordinal), "{}", false, exception.Message));
            }
        }

        var batchId = await _store.SaveBatchAsync(capturedAt, captures, cancellationToken);
        var report = await GetLatestReportAsync(cancellationToken)
                     ?? throw new InvalidOperationException("刚保存的全局快照无法重新读取。");
        return new SyncResult(batchId, report);
    }

    public async Task<AnalysisReport?> GetLatestReportAsync(CancellationToken cancellationToken = default)
    {
        var batch = await _store.GetLatestGlobalBatchAsync(cancellationToken);
        if (batch is null)
            return null;

        var baselines = new Dictionary<int, HistoricalBaseline>();
        foreach (var hours in new[] { 24, 72, 168 })
        {
            var cutoff = batch.CapturedAtUtc.AddHours(-hours);
            var baselineBatch = await _store.GetGlobalBatchAtOrBeforeAsync(cutoff, cancellationToken);
            if (baselineBatch is null)
                continue;

            var globalCohort = baselineBatch.GlobalValues.GetValueOrDefault("STAT_PT1_COHORT_MEMBER");
            var excludedCohort = baselineBatch.Accounts
                .Where(x => x.Available)
                .Sum(x => x.Values.GetValueOrDefault("STAT_PT1_COHORT_MEMBER"));
            baselines[hours] = new HistoricalBaseline(
                hours,
                baselineBatch.CapturedAtUtc,
                globalCohort - excludedCohort,
                cutoff - baselineBatch.CapturedAtUtc);
        }

        return _analysisEngine.Analyze(new AnalysisInput(
            batch.CapturedAtUtc,
            _definitions,
            batch.GlobalValues,
            batch.Accounts,
            baselines));
    }

    public async Task<(ParsedSteamResponse Response, long BatchId)> QueryUserAsync(
        string steamId,
        CancellationToken cancellationToken = default)
    {
        using var api = new SteamWebApiClient(_paths.KeyFile);
        var response = await api.GetUserStatsAsync(steamId, _definitions, cancellationToken);
        var id = await _store.SaveBatchAsync(
            DateTimeOffset.UtcNow,
            [new CaptureAccountData("user", steamId, $"玩家 {steamId}", response.Values, response.RawJson)],
            cancellationToken);
        return (response, id);
    }

    public Task<IReadOnlyList<HistoryPoint>> GetHistoryAsync(
        string apiName,
        CancellationToken cancellationToken = default) =>
        _store.GetHistoryAsync(apiName, cancellationToken);

    public async Task<string> ExportLatestAsync(
        string? outputDirectory = null,
        CancellationToken cancellationToken = default)
    {
        var report = await GetLatestReportAsync(cancellationToken)
                     ?? throw new InvalidOperationException("尚无全局快照可导出，请先同步。");
        var directory = outputDirectory ?? Path.Combine(
            _paths.ExportDirectory,
            report.CapturedAtUtc.LocalDateTime.ToString("yyyyMMdd-HHmmss"));
        Directory.CreateDirectory(directory);

        await File.WriteAllTextAsync(
            Path.Combine(directory, "analysis-report.json"),
            JsonSerializer.Serialize(report, JsonOptions),
            cancellationToken);
        await CsvExporter.WriteFactsAsync(Path.Combine(directory, "facts.csv"), report.Facts, cancellationToken);
        await CsvExporter.WriteMetricsAsync(Path.Combine(directory, "metrics.csv"), report.Metrics, cancellationToken);
        await CsvExporter.WriteChecksAsync(Path.Combine(directory, "checks.csv"), report.Checks, cancellationToken);
        return directory;
    }

    public void Dispose()
    {
    }

    public static JsonSerializerOptions JsonOptions { get; } = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };
}
