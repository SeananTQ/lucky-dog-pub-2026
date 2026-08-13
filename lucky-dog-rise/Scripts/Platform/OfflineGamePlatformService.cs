namespace LuckyDogRise;

using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

/// <summary>Safe fallback for development, DRM-free launch, or Steam failures.</summary>
public sealed class OfflineGamePlatformService : IGamePlatformService
{
    private const string StorageTestAccountPrefix = "--storage-test-account=";
    private readonly string _accountId;
    public event Action UserStatsReady
    {
        add { }
        remove { }
    }

    public OfflineGamePlatformService(string statusMessage)
    {
        StatusMessage = statusMessage;
        _accountId = ResolveDevelopmentAccountId();
    }

    public string ProviderName => "Offline";
    public string StatusMessage { get; }
    public bool IsAvailable => false;
    public uint AppId => 0;
    public string PersonaName => string.Empty;
    public string AccountProvider => _accountId.Length > 0 ? "dev" : string.Empty;
    public string AccountId => _accountId;

    public void RunCallbacks()
    {
    }

    public bool OpenFriendsOverlay() => false;

    public PlatformAchievementReadResult ReadAchievementStates(IEnumerable<string> achievementApiNames) =>
        new(false, StatusMessage, Array.Empty<PlatformAchievementState>());

    public void Dispose()
    {
    }

    private static string ResolveDevelopmentAccountId()
    {
        var args = OS.GetCmdlineUserArgs();
        if (args.Any(argument =>
                string.Equals(argument, "--diagnostics-export-smoke", StringComparison.OrdinalIgnoreCase)))
            return "diagnostics-smoke";
#if DEBUG
        if (args.Any(argument =>
                string.Equals(argument, "--identity-unavailable-smoke", StringComparison.OrdinalIgnoreCase)))
            return string.Empty;
        if (args.Any(argument =>
                string.Equals(argument, "--single-instance-smoke", StringComparison.OrdinalIgnoreCase)))
        {
            var requested = args.FirstOrDefault(argument =>
                argument.StartsWith(StorageTestAccountPrefix, StringComparison.OrdinalIgnoreCase));
            var value = requested?[StorageTestAccountPrefix.Length..].Trim().ToLowerInvariant() ?? string.Empty;
            if (value.Length > 0 && value.All(character => char.IsAsciiLetterOrDigit(character) || character is '_' or '-'))
                return value;
        }
#endif
#if !DEBUG
        return string.Empty;
#else
        return "offline";
#endif
    }
}
