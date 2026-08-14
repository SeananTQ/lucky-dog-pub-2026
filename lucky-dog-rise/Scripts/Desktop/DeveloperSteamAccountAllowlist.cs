#if DEBUG
#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Godot;

namespace LuckyDogRise;

internal static class DeveloperSteamAccountAllowlist
{
    internal const string ResourcePath = "res://Build/Developer/steam-account-allowlist.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    internal static DeveloperSteamAccountAccessResult Check(string steamId64)
    {
        if (!TryNormalizeSteamId64(steamId64, out var normalizedSteamId64))
            return DeveloperSteamAccountAccessResult.ConfigurationError(
                "Steam returned an invalid SteamID64.");

        if (!Godot.FileAccess.FileExists(ResourcePath))
            return DeveloperSteamAccountAccessResult.ConfigurationError(
                $"The developer allowlist is missing: {ResourcePath}");

        try
        {
            var json = Godot.FileAccess.GetFileAsString(ResourcePath);
            var document = JsonSerializer.Deserialize<DeveloperSteamAccountAllowlistDocument>(json, JsonOptions);
            if (document?.Accounts == null)
                return DeveloperSteamAccountAccessResult.ConfigurationError(
                    "The developer allowlist must contain an accounts array.");

            var entries = new List<ValidatedDeveloperSteamAccount>(document.Accounts.Count);
            for (var index = 0; index < document.Accounts.Count; index++)
            {
                var entry = document.Accounts[index];
                if (entry == null || !TryNormalizeSteamId64(entry.SteamId64, out var entrySteamId64))
                    return DeveloperSteamAccountAccessResult.ConfigurationError(
                        $"accounts[{index}].steamId64 is not a valid SteamID64.");

                entries.Add(new ValidatedDeveloperSteamAccount(
                    entrySteamId64,
                    entry.Note?.Trim() ?? string.Empty,
                    entry.Enabled));
            }

            var duplicate = entries
                .GroupBy(entry => entry.SteamId64, StringComparer.Ordinal)
                .FirstOrDefault(group => group.Count() > 1);
            if (duplicate != null)
                return DeveloperSteamAccountAccessResult.ConfigurationError(
                    $"SteamID64 {duplicate.Key} appears more than once in the developer allowlist.");

            var match = entries.FirstOrDefault(entry =>
                string.Equals(entry.SteamId64, normalizedSteamId64, StringComparison.Ordinal));
            if (match == null || !match.Enabled)
                return DeveloperSteamAccountAccessResult.Denied();

            return DeveloperSteamAccountAccessResult.Allow(match.Note);
        }
        catch (JsonException exception)
        {
            return DeveloperSteamAccountAccessResult.ConfigurationError(
                $"The developer allowlist contains invalid JSON: {exception.Message}");
        }
        catch (Exception exception)
        {
            GD.PushError($"[DeveloperAccountAllowlist] Failed to read {ResourcePath}: {exception}");
            return DeveloperSteamAccountAccessResult.ConfigurationError(
                $"The developer allowlist could not be read: {exception.Message}");
        }
    }

    private static bool TryNormalizeSteamId64(string? value, out string normalized)
    {
        normalized = string.Empty;
        if (string.IsNullOrWhiteSpace(value))
            return false;

        var candidate = value.Trim();
        if (!ulong.TryParse(candidate, out var numericValue) || numericValue == 0)
            return false;

        normalized = numericValue.ToString();
        return string.Equals(candidate, normalized, StringComparison.Ordinal);
    }

    private sealed class DeveloperSteamAccountAllowlistDocument
    {
        public List<DeveloperSteamAccountAllowlistEntry?>? Accounts { get; set; }
    }

    private sealed class DeveloperSteamAccountAllowlistEntry
    {
        public string? SteamId64 { get; set; }
        public string? Note { get; set; }
        public bool Enabled { get; set; }
    }

    private sealed record ValidatedDeveloperSteamAccount(string SteamId64, string Note, bool Enabled);
}

internal sealed record DeveloperSteamAccountAccessResult(
    bool Allowed,
    bool ConfigurationValid,
    string Note,
    string ErrorMessage)
{
    internal static DeveloperSteamAccountAccessResult Allow(string note) =>
        new(true, true, note, string.Empty);

    internal static DeveloperSteamAccountAccessResult Denied() =>
        new(false, true, string.Empty, string.Empty);

    internal static DeveloperSteamAccountAccessResult ConfigurationError(string errorMessage) =>
        new(false, false, string.Empty, errorMessage);
}
#endif
