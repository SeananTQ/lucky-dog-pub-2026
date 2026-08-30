using System.Text.Json;

namespace LuckyStats.Infrastructure;

public sealed record ExcludedAccountSetting(string SteamId, string Label);

public sealed record AnalyzerPaths(
    string WorkspaceRoot,
    string DefinitionsFile,
    string KeyFile,
    string DatabaseFile,
    string ExportDirectory,
    string DeveloperAllowlistFile);

public static class ProjectPaths
{
    public const uint PlaytestAppId = 4_972_240;

    public static AnalyzerPaths Discover(string? startDirectory = null)
    {
        var workspace = FindWorkspaceRoot(startDirectory ?? AppContext.BaseDirectory);
        var localDataRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "LuckyDogRise",
            "PlayerStatisticAnalyzer");

        var definitions = Environment.GetEnvironmentVariable("LUCKY_STATS_DEFINITIONS")
                          ?? Path.Combine(workspace, "lucky-dog-rise", "Data", "Json", "tbplayerstatistic.json");
        var keyFile = Environment.GetEnvironmentVariable("LUCKY_STATS_KEY_FILE")
                      ?? Path.Combine(workspace, ".local-build", "player-statistic-4972240-webapi.txt");
        var database = Environment.GetEnvironmentVariable("LUCKY_STATS_DATABASE")
                       ?? Path.Combine(localDataRoot, "player-statistics.db");
        var exports = Environment.GetEnvironmentVariable("LUCKY_STATS_EXPORT_DIR")
                      ?? Path.Combine(localDataRoot, "exports");

        return new AnalyzerPaths(
            workspace,
            Path.GetFullPath(definitions),
            Path.GetFullPath(keyFile),
            Path.GetFullPath(database),
            Path.GetFullPath(exports),
            Path.Combine(workspace, "lucky-dog-rise", "Build", "Developer", "steam-account-allowlist.json"));
    }

    public static IReadOnlyList<ExcludedAccountSetting> LoadEnabledDeveloperAccounts(string path)
    {
        if (!File.Exists(path))
            return [];

        using var stream = File.OpenRead(path);
        using var document = JsonDocument.Parse(stream);
        if (!document.RootElement.TryGetProperty("accounts", out var accounts))
            return [];

        return accounts.EnumerateArray()
            .Where(x => !x.TryGetProperty("enabled", out var enabled) || enabled.GetBoolean())
            .Select(x => new ExcludedAccountSetting(
                x.GetProperty("steamId64").GetString() ?? string.Empty,
                x.TryGetProperty("note", out var note) ? note.GetString() ?? "开发帐号" : "开发帐号"))
            .Where(x => ulong.TryParse(x.SteamId, out _))
            .DistinctBy(x => x.SteamId, StringComparer.Ordinal)
            .ToArray();
    }

    private static string FindWorkspaceRoot(string startDirectory)
    {
        var directory = new DirectoryInfo(Path.GetFullPath(startDirectory));
        while (directory is not null)
        {
            if (Directory.Exists(Path.Combine(directory.FullName, "lucky-dog-rise"))
                && Directory.Exists(Path.Combine(directory.FullName, "lucky-steamworks")))
                return directory.FullName;
            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException(
            "无法定位 Lucky Dog Pub 工作区。请从项目目录运行，或设置 LUCKY_STATS_DEFINITIONS、LUCKY_STATS_KEY_FILE 和 LUCKY_STATS_DATABASE。");
    }
}
