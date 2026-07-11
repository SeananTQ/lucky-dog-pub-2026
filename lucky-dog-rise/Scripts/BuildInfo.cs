using System;
using System.Linq;
using System.Reflection;
using System.Globalization;
using Godot;

namespace LuckyDogRise;

public enum BuildChannel
{
    Dev,
    Playtest,
    Release,
}

public static class BuildInfo
{
    private const string PlaytestFeature = "lucky_playtest";
    private const string ReleaseFeature = "lucky_release";

    public static BuildChannel Channel
    {
        get
        {
#if DEBUG
            return BuildChannel.Dev;
#else
            return OS.HasFeature(PlaytestFeature) ? BuildChannel.Playtest : BuildChannel.Release;
#endif
        }
    }

#if DEBUG
    public const bool IsDevelopment = true;
#else
    public const bool IsDevelopment = false;
#endif

    public static string BuildCommit { get; } = ReadAssemblyMetadata("BuildCommit", "unknown");
    public static string ValidationError { get; private set; } = string.Empty;

    public static string DisplayVersion
    {
        get
        {
            var version = ProjectSettings.GetSetting("application/config/version", "0.0.0").AsString();
            return $"{version} {Channel} ({BuildCommit})";
        }
    }

    public static bool ValidateCurrentBuild()
    {
#if DEBUG
        ValidationError = string.Empty;
        return true;
#else
        var playtest = OS.HasFeature(PlaytestFeature);
        var release = OS.HasFeature(ReleaseFeature);
        if (!(playtest ^ release) || !TryGetSaveHmacKey(out _))
        {
            ValidationError = "This build is missing a valid channel tag or save key.";
            GD.PushError($"[Build] {ValidationError}");
            return false;
        }

        if (playtest && !ValidatePlaytestExpiry())
            return false;

        ValidationError = string.Empty;
        return true;
#endif
    }

#if !DEBUG
    private static bool ValidatePlaytestExpiry()
    {
        var rawExpiry = ReadAssemblyMetadata("PlaytestExpiresUtc", string.Empty);
        if (!DateTimeOffset.TryParse(
                rawExpiry,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out var expiresAt))
        {
            ValidationError = "This Playtest build has no valid expiration date.";
            GD.PushError($"[Build] {ValidationError}");
            return false;
        }

        if (DateTimeOffset.UtcNow < expiresAt)
            return true;

        ValidationError = "This Playtest build expired on August 11, 2026. Please request a newer build.";
        GD.PushError($"[Build] {ValidationError}");
        return false;
    }
#endif

    internal static bool TryGetSaveHmacKey(out byte[] key)
    {
#if DEBUG
        key = Convert.FromHexString("55B20C7B4E336E69F563BC01EA16CE2ABFAE4304534D7698C64D3F87681A2B44");
        return true;
#else
        var value = ReadAssemblyMetadata("SaveHmacKey", string.Empty);
        if (value.Length == 64)
        {
            try
            {
                key = Convert.FromHexString(value);
                return key.Length == 32;
            }
            catch (FormatException)
            {
            }
        }

        key = Array.Empty<byte>();
        return false;
#endif
    }

    private static string ReadAssemblyMetadata(string key, string fallback)
    {
        return Assembly.GetExecutingAssembly()
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(attribute => string.Equals(attribute.Key, key, StringComparison.Ordinal))
            ?.Value ?? fallback;
    }
}
