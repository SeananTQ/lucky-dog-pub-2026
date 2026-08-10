#if DEBUG
using System;
using System.Collections.Generic;

namespace LuckyDogRise;

public enum DebugSteamScenario
{
    RealSteam,
    UnavailableBeforeOpen,
    SlowSuccess,
    TimeoutVerifiedSuccess,
    TimeoutVerifiedFallback,
    DisconnectAfterSubmit,
    DisconnectRecoverSuccess,
}

public enum DebugSteamPhase
{
    Ready,
    ExchangeWaiting,
    PromoGrantWaiting,
    Unavailable,
    InventoryVerification,
    Completed,
}

public sealed record DebugSteamLinkTreeGrant(
    int BundleItemDefId,
    int ReceiptItemDefId,
    int RewardItemDefId);

public sealed record DebugSteamMockSnapshot(
    DebugSteamScenario Scenario,
    DebugSteamPhase Phase,
    double PhaseElapsedSeconds,
    PlatformConnectionState ConnectionState,
    PlatformInventoryTrustState InventoryTrustState,
    uint VoucherQuantity,
    bool HasPendingTransaction,
    string PendingOperation,
    string LastEvent,
    IReadOnlyList<string> Events);

public interface IDebugSteamMockController
{
    event Action<DebugSteamMockSnapshot> SnapshotChanged;

    DebugSteamMockSnapshot Snapshot { get; }
    bool IsMockActive { get; }
    bool TrySelectScenario(DebugSteamScenario scenario, out string message);
    void ConfigureBlindBox(int voucherItemDefId, int rewardItemDefId);
    void ConfigureLinkTreeGrants(IReadOnlyList<DebugSteamLinkTreeGrant> grants);
    void ResetScenario();
    void AdvancePhase();
}
#endif
