using System;

namespace LuckyDogRise;

public enum PlatformConnectionState
{
    Offline,
    Connecting,
    InventorySyncing,
    Ready,
    Unavailable,
}

public enum PlatformInventoryTrustState
{
    /// <summary>No verified inventory snapshot is available yet.</summary>
    Unknown,

    /// <summary>The latest full inventory snapshot can be used for platform decisions.</summary>
    Trusted,

    /// <summary>The cached snapshot must not be trusted until a full inventory sync succeeds.</summary>
    RevalidationRequired,
}

public interface IRecoverablePlatformService
{
    event Action<PlatformConnectionState> ConnectionStateChanged;
    event Action<PlatformInventoryTrustState> InventoryTrustStateChanged;
    event Action<string, string> AccountIdentityConflictDetected;

    PlatformConnectionState ConnectionState { get; }
    PlatformInventoryTrustState InventoryTrustState { get; }
    string InventoryTrustMessage { get; }
    bool HasAccountIdentityConflict { get; }
    bool CanRequestClientRelaunch { get; }
    void RequestReconnect();
    bool TryRequestClientRelaunch(out string message);
    void RequireInventoryRevalidation(string reason);
}
