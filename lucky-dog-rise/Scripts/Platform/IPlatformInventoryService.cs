using System;
using System.Collections.Generic;

namespace LuckyDogRise;

public interface IPlatformInventoryService
{
    event Action<PlatformInventorySnapshot> InventorySnapshotChanged;
    event Action<PlatformPromoItemGrantResult> PromoItemGrantCompleted;
    event Action<PlatformPlaytimeDropResult> PlaytimeDropCompleted;

    bool IsInventoryReady { get; }
    bool IsPromoGrantPending { get; }
    bool IsPlaytimeDropPending { get; }
    IReadOnlyList<PlatformInventoryItem> InventoryItems { get; }

    void StartInventorySynchronization();
    bool TryGrantPromoItem(int promoItemDefId, int receiptItemDefId, out string message);
    bool TryTriggerPlaytimeDrop(int generatorItemDefId, out string message);
}

public readonly record struct PlatformInventoryItem(
    ulong InstanceId,
    int ItemDefId,
    uint Quantity);

public sealed record PlatformInventorySnapshot(
    bool Succeeded,
    string Message,
    IReadOnlySet<int> OwnedItemDefIds,
    IReadOnlyList<PlatformInventoryItem> Items);

public readonly record struct PlatformPromoItemGrantResult(
    int PromoItemDefId,
    int ReceiptItemDefId,
    bool Succeeded,
    bool ReceiptOwned,
    string Message,
    IReadOnlyList<PlatformInventoryItem> ChangedItems);

public sealed record PlatformPlaytimeDropResult(
    int GeneratorItemDefId,
    bool Succeeded,
    string Message,
    IReadOnlyList<PlatformInventoryItem> ChangedItems);
