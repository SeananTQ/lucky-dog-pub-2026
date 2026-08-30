using LuckyStats.Core;

namespace LuckyStats.Infrastructure;

public sealed class SteamWebApiClient : IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly string _apiKey;
    private readonly uint _appId;

    public SteamWebApiClient(string keyFile, uint appId = ProjectPaths.PlaytestAppId, HttpMessageHandler? handler = null)
    {
        _apiKey = ReadKey(keyFile);
        _appId = appId;
        _httpClient = handler is null ? new HttpClient() : new HttpClient(handler);
        _httpClient.BaseAddress = new Uri("https://partner.steam-api.com/");
        _httpClient.Timeout = TimeSpan.FromSeconds(30);
        _httpClient.DefaultRequestHeaders.Add("x-webapi-key", _apiKey);
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("LuckyDogRise-PlayerStatisticAnalyzer/1.0");
    }

    public async Task<ParsedSteamResponse> GetGlobalStatsAsync(
        IReadOnlyList<StatisticDefinition> definitions,
        CancellationToken cancellationToken = default)
    {
        var query = new List<string>
        {
            $"appid={_appId}",
            $"count={definitions.Count}"
        };
        for (var i = 0; i < definitions.Count; i++)
            query.Add($"name[{i}]={Uri.EscapeDataString(definitions[i].ApiName)}");

        var json = await GetStringAsync(
            "ISteamUserStats/GetGlobalStatsForGame/v1/?" + string.Join('&', query),
            cancellationToken);
        return SteamStatsJsonParser.ParseGlobal(json, definitions.Select(x => x.ApiName).ToArray());
    }

    public async Task<ParsedSteamResponse> GetUserStatsAsync(
        string steamId,
        IReadOnlyList<StatisticDefinition> definitions,
        CancellationToken cancellationToken = default)
    {
        if (!ulong.TryParse(steamId, out _))
            throw new ArgumentException("SteamID 必须是数字形式的 SteamID64。", nameof(steamId));

        var path = $"ISteamUserStats/GetUserStatsForGame/v2/?appid={_appId}&steamid={Uri.EscapeDataString(steamId)}";
        var json = await GetStringAsync(path, cancellationToken);
        return SteamStatsJsonParser.ParseUser(json, definitions.Select(x => x.ApiName).ToArray());
    }

    private async Task<string> GetStringAsync(string path, CancellationToken cancellationToken)
    {
        using var response = await _httpClient.GetAsync(path, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException(
                $"Steam Web API 返回 {(int)response.StatusCode} {response.ReasonPhrase}。响应：{Truncate(body, 300)}",
                null,
                response.StatusCode);
        return body;
    }

    private static string ReadKey(string keyFile)
    {
        if (!File.Exists(keyFile))
            throw new FileNotFoundException("找不到 Steam Web API Key 文件。", keyFile);

        var key = File.ReadAllText(keyFile).Trim();
        if (key.Length < 20 || key.Any(char.IsWhiteSpace))
            throw new InvalidDataException("Steam Web API Key 文件格式无效，应只包含一行 Key。 Key 不会被记录到日志。 ");
        return key;
    }

    private static string Truncate(string value, int maxLength) =>
        value.Length <= maxLength ? value : value[..maxLength] + "…";

    public void Dispose() => _httpClient.Dispose();
}
