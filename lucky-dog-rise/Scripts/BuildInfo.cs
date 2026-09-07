using System;
using System.Linq;
using System.Reflection;
using System.Globalization;
using DataTables;
using Godot;

namespace LuckyDogRise;

public enum BuildChannel
{
    Dev,
    Playtest,
    Demo,
    Release,
}

public static class BuildInfo
{
    private const string PlaytestFeature = "lucky_playtest";
    private const string DemoFeature = "lucky_demo";
    private const string ReleaseFeature = "lucky_release";
    public const uint PlaytestSteamAppId = 4972240;
    public const uint DemoSteamAppId = 5220880;
    public const uint ReleaseSteamAppId = 2583700;

    public static BuildChannel Channel
    {
        get
        {
#if DEBUG
            return BuildChannel.Dev;
#else
            if (OS.HasFeature(PlaytestFeature))
                return BuildChannel.Playtest;
            return OS.HasFeature(DemoFeature) ? BuildChannel.Demo : BuildChannel.Release;
#endif
        }
    }

#if DEBUG
    public static DebugGameplayChannel SelectedDebugGameplayChannel { get; private set; } =
        DebugGameplayChannel.Standard;
    public static bool IsDebugDemo => SelectedDebugGameplayChannel == DebugGameplayChannel.Demo;

    public static void ConfigureDebugGameplayChannel(DebugGameplayChannel channel)
    {
        SelectedDebugGameplayChannel = channel;
        GD.Print($"[Build] Debug gameplay channel: {channel}.");
    }
#endif

#if DEBUG
    public const bool IsDevelopment = true;
#else
    public const bool IsDevelopment = false;
#endif

    public static string BuildCommit { get; } = ReadAssemblyMetadata("BuildCommit", "unknown");
    public static string ValidationError { get; private set; } = string.Empty;
    public static uint ExpectedSteamAppId => Channel switch
    {
        BuildChannel.Playtest => PlaytestSteamAppId,
        BuildChannel.Demo => DemoSteamAppId,
        BuildChannel.Release => ReleaseSteamAppId,
        _ => 0,
    };

    public static bool IncludesCurrentChannel(EBuildChannelMask channelMask)
    {
        if (Channel == BuildChannel.Dev)
            return true;

        var currentChannelMask = Channel switch
        {
            BuildChannel.Playtest => EBuildChannelMask.Playtest,
            BuildChannel.Demo => EBuildChannelMask.Demo,
            BuildChannel.Release => EBuildChannelMask.Release,
            _ => (EBuildChannelMask)0,
        };
        return (channelMask & currentChannelMask) != 0;
    }

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
        var demo = OS.HasFeature(DemoFeature);
        var release = OS.HasFeature(ReleaseFeature);
        if ((Convert.ToInt32(playtest) + Convert.ToInt32(demo) + Convert.ToInt32(release) != 1)
            || !TryGetSaveHmacKey(out _))
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

        ValidationError = "This Playtest build expired on September 25, 2026. Please request a newer build.";
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

public static class BuildCapabilities
{
    private static bool IsPackagedDemo => BuildInfo.Channel == BuildChannel.Demo;
    private static bool IsDemoRuntime
    {
        get
        {
#if DEBUG
            if (BuildInfo.IsDebugDemo)
                return true;
#endif
            return IsPackagedDemo;
        }
    }

    public static bool BlindBoxes => true;
    public static bool CollectionEvents => IsDemoRuntime;
    // LinkTree remains outside the current Demo-debug work. The packaged review
    // Demo keeps its existing closed state; Debug Demo leaves the page untouched.
    public static bool LinkTree => !IsPackagedDemo;
    public static bool SteamInventory => !IsDemoRuntime;
    public static bool PlatformStatistics => !IsDemoRuntime;
    public static bool Achievements => !IsDemoRuntime;
    public static bool SteamCloud
    {
        get
        {
#if DEBUG
            // The editor is connected through the local Playtest AppID. Never let
            // a Demo simulation read or write that app's Cloud namespace.
            if (BuildInfo.IsDebugDemo)
                return false;
#endif
            return true;
        }
    }
}
