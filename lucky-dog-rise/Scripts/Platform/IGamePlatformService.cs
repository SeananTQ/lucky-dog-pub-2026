using System;
using System.Collections.Generic;

namespace LuckyDogRise;

/// <summary>
/// Platform boundary used by game code. Platform-specific APIs must stay behind
/// this interface so the game remains playable without Steam.
/// </summary>
public interface IGamePlatformService : IDisposable
{
    event Action UserStatsReady;

    string ProviderName { get; }
    string StatusMessage { get; }
    bool IsAvailable { get; }
    uint AppId { get; }
    string PersonaName { get; }

    void RunCallbacks();
    bool OpenFriendsOverlay();
    PlatformAchievementReadResult ReadAchievementStates(IEnumerable<string> achievementApiNames);
}

public readonly record struct PlatformAchievementState(
    string ApiName,
    bool IsConfigured,
    bool ReadSucceeded,
    bool IsUnlocked);

public sealed class PlatformAchievementReadResult
{
    public PlatformAchievementReadResult(bool succeeded, string message, IReadOnlyList<PlatformAchievementState> states)
    {
        Succeeded = succeeded;
        Message = message;
        States = states;
    }

    public bool Succeeded { get; }
    public string Message { get; }
    public IReadOnlyList<PlatformAchievementState> States { get; }
}
