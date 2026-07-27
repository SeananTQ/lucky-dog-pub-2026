using System;
using System.Collections.Generic;

namespace LuckyDogRise;

public interface IPlatformInventoryService
{
    event Action<PlatformInventorySnapshot> InventorySnapshotChanged;
    event Action<PlatformPromoItemGrantResult> PromoItemGrantCompleted;

    bool IsInventoryReady { get; }
    bool IsPromoGrantPending { get; }

    void StartInventorySynchronization();
    bool TryGrantPromoItem(int itemDefId, out string message);
}

public sealed record PlatformInventorySnapshot(
    bool Succeeded,
    string Message,
    IReadOnlySet<int> OwnedItemDefIds);

public readonly record struct PlatformPromoItemGrantResult(
    int ItemDefId,
    bool Succeeded,
    bool ReceiptOwned,
    string Message);
