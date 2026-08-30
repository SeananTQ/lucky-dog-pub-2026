using System.Text.Json;
using LuckyStats.Infrastructure;

return await RunAsync(args);

static async Task<int> RunAsync(string[] args)
{
    try
    {
        if (args.Length == 0 || args[0] is "help" or "--help" or "-h")
        {
            PrintUsage();
            return 0;
        }

        using var app = await StatisticsApplication.CreateAsync();
        switch (args[0].ToLowerInvariant())
        {
            case "sync":
            {
                var result = await app.SyncGlobalAsync();
                WriteJson(new { result.BatchId, result.Report });
                return 0;
            }
            case "report":
            {
                var report = await app.GetLatestReportAsync();
                if (report is null)
                    throw new InvalidOperationException("尚无全局快照，请先运行 sync。");
                WriteJson(report);
                return 0;
            }
            case "user":
            {
                var steamId = ReadOption(args, "--steamid")
                              ?? throw new ArgumentException("user 命令需要 --steamid <SteamID64>。");
                var result = await app.QueryUserAsync(steamId);
                WriteJson(new { result.BatchId, result.Response.SteamId, result.Response.Values });
                return 0;
            }
            case "history":
            {
                var apiName = ReadOption(args, "--stat") ?? "STAT_PT1_COHORT_MEMBER";
                if (!app.Definitions.Any(x => x.ApiName == apiName))
                    throw new ArgumentException($"未知 Stat API Name：{apiName}");
                WriteJson(await app.GetHistoryAsync(apiName));
                return 0;
            }
            case "export":
            {
                var directory = await app.ExportLatestAsync(ReadOption(args, "--output"));
                WriteJson(new { outputDirectory = directory });
                return 0;
            }
            case "paths":
                WriteJson(new
                {
                    app.Paths.DefinitionsFile,
                    app.Paths.DatabaseFile,
                    app.Paths.ExportDirectory,
                    app.Paths.DeveloperAllowlistFile,
                    keyFileExists = File.Exists(app.Paths.KeyFile)
                });
                return 0;
            default:
                throw new ArgumentException($"未知命令：{args[0]}");
        }
    }
    catch (ArgumentException exception)
    {
        Console.Error.WriteLine(exception.Message);
        return 2;
    }
    catch (HttpRequestException exception)
    {
        Console.Error.WriteLine(exception.Message);
        return 3;
    }
    catch (Exception exception)
    {
        Console.Error.WriteLine(exception.Message);
        return 4;
    }
}

static string? ReadOption(string[] args, string name)
{
    var index = Array.FindIndex(args, x => string.Equals(x, name, StringComparison.OrdinalIgnoreCase));
    return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
}

static void WriteJson(object value) =>
    Console.WriteLine(JsonSerializer.Serialize(value, StatisticsApplication.JsonOptions));

static void PrintUsage()
{
    Console.WriteLine("""
        Lucky Dog Rise 玩家统计分析 CLI

        sync                               查询全局与需排除的开发帐号，保存快照并分析
        report                             输出最新快照的派生指标、公式和检查结果
        user --steamid <SteamID64>         查询并保存一个已知玩家的统计
        history [--stat <APIName>]         输出某项 Stat 的本地历史曲线数据
        export [--output <目录>]           导出 facts/metrics/checks CSV 与分析 JSON
        paths                              显示配置路径（不会显示 Web API Key）

        所有成功命令的标准输出均为 JSON；诊断信息写入标准错误。
        """);
}
