namespace LuckyDogRise;

public readonly record struct PlatformCloudFileReadResult(
    bool Succeeded,
    bool Exists,
    string Content,
    long SteamTimestamp,
    string Message);

/// <summary>
/// Small explicit Steam Remote Storage boundary. This is intentionally separate
/// from Steam Inventory so cloud saves never enter the inventory write queue.
/// </summary>
public interface IPlatformCloudStorageService
{
    bool IsCloudAvailable { get; }
    PlatformCloudFileReadResult ReadCloudTextFile(string fileName);
    bool TryWriteCloudTextFile(string fileName, string content, out string message);
}
