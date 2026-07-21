namespace LuckyDogRise;

public sealed class SteamGamePlatformService : IGamePlatformService
{
    private readonly SteamworksRuntime _runtime;

    public SteamGamePlatformService(SteamworksRuntime runtime)
    {
        _runtime = runtime;
    }

    public string ProviderName => "Steam";
    public string StatusMessage => _runtime.StatusMessage;
    public bool IsAvailable => _runtime.IsInitialized;
    public uint AppId => _runtime.AppId;
    public string PersonaName => _runtime.PersonaName;

    public void RunCallbacks() => _runtime.RunCallbacks();
    public bool OpenFriendsOverlay() => _runtime.OpenFriendsOverlay();
    public void Dispose() => _runtime.Dispose();
}
