using System;

namespace LuckyDogRise;

/// <summary>
/// Explicitly dangerous account mutations used only by the independent Steam
/// diagnostic scene. This is deliberately separate from IGamePlatformService.
/// </summary>
public interface IPlatformAchievementTestOperations
{
    event Action<string> StoreStatusChanged;

    bool IsReadyForWrites { get; }
    bool TrySetAchievementForTesting(string apiName, bool unlocked, out string message);
}
