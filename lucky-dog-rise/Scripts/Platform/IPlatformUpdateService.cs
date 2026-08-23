namespace LuckyDogRise;

/// <summary>
/// Platform-specific entry point for handing a running build back to its client
/// so installed content can be verified and the game can be launched again.
/// </summary>
public interface IPlatformUpdateService
{
    bool CanUpdateAndRestart { get; }
    bool TryMarkContentCorrupt(bool missingFilesOnly, out string message);
}
