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

    PlatformConnectionState ConnectionState { get; }
    PlatformInventoryTrustState InventoryTrustState { get; }
    string InventoryTrustMessage { get; }
    void RequestReconnect();
    void RequireInventoryRevalidation(string reason);
}
