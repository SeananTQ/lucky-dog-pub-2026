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
    Unknown,
    Trusted,
    Dirty,
}

public interface IRecoverablePlatformService
{
    event Action<PlatformConnectionState> ConnectionStateChanged;
    event Action<PlatformInventoryTrustState> InventoryTrustStateChanged;

    PlatformConnectionState ConnectionState { get; }
    PlatformInventoryTrustState InventoryTrustState { get; }
    string InventoryTrustMessage { get; }
    void RequestReconnect();
    void MarkInventoryDirty(string reason);
}
