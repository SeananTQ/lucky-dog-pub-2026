using System;

namespace LuckyDogRise;

/// <summary>
/// Platform boundary used by game code. Platform-specific APIs must stay behind
/// this interface so the game remains playable without Steam.
/// </summary>
public interface IGamePlatformService : IDisposable
{
    string ProviderName { get; }
    string StatusMessage { get; }
    bool IsAvailable { get; }
    uint AppId { get; }
    string PersonaName { get; }

    void RunCallbacks();
    bool OpenFriendsOverlay();
}
