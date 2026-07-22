using System.Collections.Generic;

namespace LuckyDogRise;

/// <summary>
/// 正式游戏使用的平台成就写入能力。这里只允许解锁，不暴露清除或重置。
/// </summary>
public interface IPlatformAchievementSyncOperations
{
    PlatformAchievementUnlockResult UnlockAchievements(IEnumerable<string> achievementApiNames);
}

public sealed class PlatformAchievementUnlockResult
{
    public PlatformAchievementUnlockResult(
        bool succeeded,
        string message,
        IReadOnlyList<string> submittedApiNames)
    {
        Succeeded = succeeded;
        Message = message;
        SubmittedApiNames = submittedApiNames;
    }

    public bool Succeeded { get; }
    public string Message { get; }
    public IReadOnlyList<string> SubmittedApiNames { get; }
}
