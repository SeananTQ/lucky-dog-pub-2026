using System;
using System.Collections.Generic;

namespace LuckyDogRise;

public interface IPlatformInventoryService
{
    event Action<PlatformInventorySnapshot> InventorySnapshotChanged;
    event Action<PlatformPromoItemGrantResult> PromoItemGrantCompleted;
    event Action<PlatformPlaytimeDropResult> PlaytimeDropCompleted;
    event Action<PlatformInventoryExchangeResult> InventoryExchangeCompleted;

    bool IsInventoryReady { get; }
    bool IsPromoGrantPending { get; }
    bool IsPlaytimeDropPending { get; }
    bool IsExchangePending { get; }
    IReadOnlyList<PlatformInventoryItem> InventoryItems { get; }

    void StartInventorySynchronization();
    bool TryGrantPromoItem(int itemDefId, out string message);
    bool TryTriggerPlaytimeDrop(int generatorItemDefId, int outputItemDefId, out string message);
    bool TryExchangeItem(
        ulong inputInstanceId,
        int inputItemDefId,
        int outputItemDefId,
        out string message);
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
    int ItemDefId,
    bool Succeeded,
    bool ReceiptOwned,
    string Message);

public sealed record PlatformPlaytimeDropResult(
    int GeneratorItemDefId,
    int OutputItemDefId,
    bool Succeeded,
    bool ItemGranted,
    string Message,
    IReadOnlyList<PlatformInventoryItem> ChangedItems);

public sealed record PlatformInventoryExchangeResult(
    ulong InputInstanceId,
    int InputItemDefId,
    int OutputItemDefId,
    bool Succeeded,
    string Message,
    IReadOnlyList<PlatformInventoryItem> ChangedItems);
