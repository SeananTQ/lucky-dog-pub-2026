namespace LuckyDogRise;

using System;
using System.Collections.Generic;

/// <summary>Safe fallback for development, DRM-free launch, or Steam failures.</summary>
public sealed class OfflineGamePlatformService : IGamePlatformService
{
    public event Action UserStatsReady
    {
        add { }
        remove { }
    }

    public OfflineGamePlatformService(string statusMessage)
    {
        StatusMessage = statusMessage;
    }

    public string ProviderName => "Offline";
    public string StatusMessage { get; }
    public bool IsAvailable => false;
    public uint AppId => 0;
    public string PersonaName => string.Empty;

    public void RunCallbacks()
    {
    }

    public bool OpenFriendsOverlay() => false;

    public PlatformAchievementReadResult ReadAchievementStates(IEnumerable<string> achievementApiNames) =>
        new(false, StatusMessage, Array.Empty<PlatformAchievementState>());

    public void Dispose()
    {
    }
}
