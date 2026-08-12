#if DEBUG
using System;
using System.Collections.Generic;

namespace LuckyDogRise;

public enum DebugSteamScenario
{
    NormalSuccess,
    UnavailableBeforeOpen,
    SlowSuccess,
    TimeoutVerifiedSuccess,
    TimeoutVerifiedFallback,
    DisconnectAfterSubmit,
    DisconnectRecoverSuccess,
    ForcedSuccess,
}

public enum DebugSteamPhase
{
    Ready,
    PlaytimeDropWaiting,
    PromoGrantWaiting,
    Unavailable,
    InventoryVerification,
    Completed,
}

public enum DebugBlindBoxProgressMode
{
    BeginnerSequence,
    Loop,
}

public sealed record DebugSteamLinkTreeGrant(
    int BundleItemDefId,
    int ReceiptItemDefId,
    int RewardItemDefId);

public sealed record DebugSteamPlaytimeDropRule(
    int GeneratorItemDefId,
    double DropIntervalSeconds,
    double DropWindowSeconds,
    int DropMaxPerWindow);

public sealed record DebugSteamMockSnapshot(
    DebugSteamScenario Scenario,
    DebugSteamPhase Phase,
    double PhaseElapsedSeconds,
    PlatformConnectionState ConnectionState,
    PlatformInventoryTrustState InventoryTrustState,
    int GeneratorItemDefId,
    ulong RewardInstanceId,
    double SimulatedPlaytimeSeconds,
    double DropIntervalSeconds,
    int GrantsInWindow,
    int DropMaxPerWindow,
    bool HasPendingTransaction,
    string PendingOperation,
    string LastEvent,
    IReadOnlyList<string> Events);

public interface IDebugSteamMockController
{
    event Action<DebugSteamMockSnapshot> SnapshotChanged;

    DebugSteamMockSnapshot Snapshot { get; }
    bool IsMockActive { get; }
    bool CanUseRealSteam { get; }
    bool TrySelectScenario(DebugSteamScenario scenario, out string message);
    bool TryUseRealSteam(out string message);
    void ConfigureBlindBox(
        int blindBoxId,
        int rewardItemDefId,
        DebugSteamPlaytimeDropRule dropRule);
    void AdvanceSimulatedPlaytime(double seconds);
    void ConfigureLinkTreeGrants(IReadOnlyList<DebugSteamLinkTreeGrant> grants);
    void ResetScenario();
    void AdvancePhase();
}
#endif
