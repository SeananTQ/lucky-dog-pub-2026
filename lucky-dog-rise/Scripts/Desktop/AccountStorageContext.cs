#nullable enable

using System;
using System.Globalization;
using System.Linq;

namespace LuckyDogRise;

/// <summary>
/// Immutable ownership and path boundary for every account-scoped local file.
/// Machine settings and diagnostics intentionally remain outside this context.
/// </summary>
public sealed class AccountStorageContext
{
    private AccountStorageContext(string provider, string accountId)
    {
        Provider = NormalizeComponent(provider, nameof(provider));
        AccountId = NormalizeComponent(accountId, nameof(accountId));
        RootPath = $"user://accounts/{Provider}/{AccountId}";
    }

    public string Provider { get; }
    public string AccountId { get; }
    public string RootPath { get; }
    public string SaveDirectoryPath => $"{RootPath}/saves";
    public string SavePath => $"{SaveDirectoryPath}/profile_0.json";
    public string SaveBackupPath => $"{SaveDirectoryPath}/profile_0.backup.json";
    public string SaveCorruptPath => $"{SaveDirectoryPath}/profile_0.corrupt.json";
    public string SaveInvalidSignaturePath => $"{SaveDirectoryPath}/profile_0.invalid_signature.json";
    public string SaveTempPath => $"{SaveDirectoryPath}/profile_0.tmp.json";
    public string PlayerProgressPath => $"{RootPath}/player_progress_0.json";
    public string PlayerProgressBackupPath => $"{RootPath}/player_progress_0.backup.json";
    public string PlayerProgressTempPath => $"{RootPath}/player_progress_0.temp.json";
    public string PlayerProgressCorruptPath => $"{RootPath}/player_progress_0.corrupt.json";
    public string ProgressionPath => $"{RootPath}/progress.cfg";
    public string ProgressionBackupPath => $"{RootPath}/progress.backup.cfg";
    public string ProgressionTempPath => $"{RootPath}/progress.temp.cfg";
    public string ProgressionCorruptPath => $"{RootPath}/progress.corrupt.cfg";

    public static AccountStorageContext ForSteam(string steamId)
    {
        if (string.IsNullOrWhiteSpace(steamId) || steamId.Any(character => !char.IsAsciiDigit(character)))
            throw new ArgumentException("Steam account ID must be a numeric SteamID64.", nameof(steamId));
        return new AccountStorageContext("steam", steamId);
    }

    public static AccountStorageContext ForDevelopment(string name) =>
        new("dev", name);

    public bool Owns(string? provider, string? accountId) =>
        string.Equals(Provider, provider, StringComparison.Ordinal)
        && string.Equals(AccountId, accountId, StringComparison.Ordinal);

    public override string ToString() => $"{Provider}:{AccountId}";

    private static string NormalizeComponent(string value, string parameterName)
    {
        var normalized = value?.Trim().ToLower(CultureInfo.InvariantCulture) ?? string.Empty;
        if (normalized.Length == 0
            || normalized.Any(character => !(char.IsAsciiLetterOrDigit(character) || character is '_' or '-')))
            throw new ArgumentException("Storage identity contains an invalid path component.", parameterName);
        return normalized;
    }
}
