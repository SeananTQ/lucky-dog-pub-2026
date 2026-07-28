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

public interface IRecoverablePlatformService
{
    event Action<PlatformConnectionState> ConnectionStateChanged;

    PlatformConnectionState ConnectionState { get; }
    void RequestReconnect();
}
