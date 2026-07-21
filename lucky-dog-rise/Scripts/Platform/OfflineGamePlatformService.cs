namespace LuckyDogRise;

/// <summary>Safe fallback for development, DRM-free launch, or Steam failures.</summary>
public sealed class OfflineGamePlatformService : IGamePlatformService
{
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

    public void Dispose()
    {
    }
}
