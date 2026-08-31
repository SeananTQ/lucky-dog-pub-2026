#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Godot;

namespace LuckyDogRise;

public sealed class OutfitPresetSlotRecord
{
    public int SlotIndex { get; set; }
    public long UpdatedAtUnixMilliseconds { get; set; }
    public bool Deleted { get; set; }
    public Dictionary<string, int> EquippedItemIdsByType { get; set; } = new();
}

public sealed class OutfitPresetCloudDocument
{
    public const int CurrentVersion = 1;

    public int Version { get; set; } = CurrentVersion;
    public Dictionary<int, OutfitPresetSlotRecord> Slots { get; set; } = new();
}

public sealed record OutfitPresetSlotSnapshot(
    int SlotIndex,
    bool IsOccupied,
    IReadOnlyDictionary<string, int> EquippedItemIdsByType);

/// <summary>
/// Nine fixed outfit-preset slots. Each slot is a last-write-wins register;
/// deletes remain as tombstones so an older device cannot resurrect a preset.
/// </summary>
public sealed class OutfitPresetCloudSynchronizer : IDisposable
{
    public const int SlotCount = 9;
    public const string CloudFileName = "outfit_presets_v1.json";
    private const double UploadDebounceSeconds = 3.0;
    private const double RetrySeconds = 15.0;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
    };

    private readonly IPlatformCloudStorageService? _cloud;
    private readonly PlayerInventory _inventory;
    private readonly string _localPath;
    private OutfitPresetCloudDocument _document;
    private bool _dirty;
    private bool _sessionReconciled;
    private bool _disposed;
    private double _nextAttemptSeconds;

    public OutfitPresetCloudSynchronizer(
        IPlatformCloudStorageService? cloud,
        AccountStorageContext storageContext,
        PlayerInventory inventory)
    {
        _cloud = cloud;
        _inventory = inventory;
        _localPath = storageContext.OutfitPresetCloudLocalPath;
        _document = LoadLocalDocument() ?? new OutfitPresetCloudDocument();
        if (_cloud != null)
            TrySynchronizeNow();
    }

    public event Action Changed = delegate { };

    public IReadOnlyList<OutfitPresetSlotSnapshot> GetSlots()
    {
        var result = new List<OutfitPresetSlotSnapshot>(SlotCount);
        for (var slotIndex = 0; slotIndex < SlotCount; slotIndex++)
        {
            var occupied = _document.Slots.TryGetValue(slotIndex, out var slot) && !slot.Deleted;
            result.Add(new OutfitPresetSlotSnapshot(
                slotIndex,
                occupied,
                occupied
                    ? new Dictionary<string, int>(slot!.EquippedItemIdsByType, StringComparer.Ordinal)
                    : new Dictionary<string, int>()));
        }
        return result;
    }

    public bool TrySaveCurrentToEmptySlot(int slotIndex, out string message)
    {
        if (!IsValidSlot(slotIndex))
        {
            message = $"预设槽位必须在 0 到 {SlotCount - 1} 之间。";
            return false;
        }
        if (_document.Slots.TryGetValue(slotIndex, out var existing) && !existing.Deleted)
        {
            message = "这个预设槽位已经有内容，不能直接覆盖。";
            return false;
        }

        _document.Slots[slotIndex] = new OutfitPresetSlotRecord
        {
            SlotIndex = slotIndex,
            UpdatedAtUnixMilliseconds = NowUnixMilliseconds(),
            Deleted = false,
            EquippedItemIdsByType = NormalizeEquipment(_inventory.GetEquippedIdsByTypeName()),
        };
        PersistLocalChange();
        message = $"已保存装扮预设到槽位 {slotIndex + 1}。";
        return true;
    }

    public bool TryApplySlot(int slotIndex, out string message)
    {
        if (!TryGetOccupiedSlot(slotIndex, out var slot, out message))
            return false;
        return _inventory.TryApplyEquipmentPreset(slot.EquippedItemIdsByType, out message);
    }

    public bool TryDeleteSlot(int slotIndex, out string message)
    {
        if (!TryGetOccupiedSlot(slotIndex, out _, out message))
            return false;

        _document.Slots[slotIndex] = new OutfitPresetSlotRecord
        {
            SlotIndex = slotIndex,
            UpdatedAtUnixMilliseconds = NowUnixMilliseconds(),
            Deleted = true,
            EquippedItemIdsByType = new Dictionary<string, int>(),
        };
        PersistLocalChange();
        message = $"已删除装扮预设槽位 {slotIndex + 1}。";
        return true;
    }

    public void Process()
    {
        if (_disposed || _cloud == null || Time.GetTicksMsec() / 1000.0 < _nextAttemptSeconds)
            return;
        if (!_sessionReconciled || _dirty)
            TrySynchronizeNow();
    }

    public void FlushForShutdown()
    {
        if (!_disposed && _cloud != null)
            TrySynchronizeNow();
    }

    public void Dispose()
    {
        _disposed = true;
    }

    private void PersistLocalChange()
    {
        SaveLocalDocument();
        _dirty = true;
        _nextAttemptSeconds = Time.GetTicksMsec() / 1000.0 + UploadDebounceSeconds;
        Changed();
    }

    private void TrySynchronizeNow()
    {
        if (_cloud?.IsCloudAvailable != true)
        {
            ScheduleRetry("Steam Cloud 当前不可用，装扮预设保留在本地。");
            return;
        }

        if (!_sessionReconciled)
        {
            var remoteResult = _cloud.ReadCloudTextFile(CloudFileName);
            if (!remoteResult.Succeeded)
            {
                ScheduleRetry(remoteResult.Message);
                return;
            }

            if (remoteResult.Exists)
            {
                if (!TryParseDocument(remoteResult.Content, out var remote, out var failure))
                {
                    ScheduleRetry($"Steam Cloud 装扮预设文件无法读取，未覆盖本地数据：{failure}");
                    return;
                }

                var merged = MergeDocuments(_document, remote);
                var localChanged = !DocumentsEqual(_document, merged);
                var remoteChanged = !DocumentsEqual(remote, merged);
                _document = merged;
                if (localChanged)
                {
                    SaveLocalDocument();
                    Changed();
                }
                _dirty |= remoteChanged;
            }
            else
            {
                _dirty = true;
            }

            _sessionReconciled = true;
        }

        if (_dirty)
            WriteCurrentDocument();
        else
            _nextAttemptSeconds = double.PositiveInfinity;
    }

    private void WriteCurrentDocument()
    {
        var json = JsonSerializer.Serialize(_document, JsonOptions);
        var message = "Steam Cloud 装扮预设写入失败。";
        if (_cloud?.TryWriteCloudTextFile(CloudFileName, json, out message) != true)
        {
            ScheduleRetry(message);
            return;
        }

        _dirty = false;
        _nextAttemptSeconds = double.PositiveInfinity;
        GD.Print($"[OutfitPresetCloud] {message}");
        DiagnosticLog.Record("outfit_preset_cloud_synchronized", new Dictionary<string, object>
        {
            ["fileName"] = CloudFileName,
            ["occupiedSlots"] = _document.Slots.Values.Count(slot => !slot.Deleted),
            ["tombstones"] = _document.Slots.Values.Count(slot => slot.Deleted),
        });
    }

    private bool TryGetOccupiedSlot(
        int slotIndex,
        out OutfitPresetSlotRecord slot,
        out string message)
    {
        slot = new OutfitPresetSlotRecord();
        if (!IsValidSlot(slotIndex))
        {
            message = $"预设槽位必须在 0 到 {SlotCount - 1} 之间。";
            return false;
        }
        if (!_document.Slots.TryGetValue(slotIndex, out var existing) || existing.Deleted)
        {
            message = "这个预设槽位是空的。";
            return false;
        }
        slot = existing;
        message = string.Empty;
        return true;
    }

    private OutfitPresetCloudDocument? LoadLocalDocument()
    {
        try
        {
            var absolutePath = ProjectSettings.GlobalizePath(_localPath);
            if (!File.Exists(absolutePath))
                return null;
            var document = JsonSerializer.Deserialize<OutfitPresetCloudDocument>(File.ReadAllText(absolutePath));
            return document?.Version == OutfitPresetCloudDocument.CurrentVersion
                ? NormalizeDocument(document)
                : null;
        }
        catch (Exception exception)
        {
            GD.PushWarning($"[OutfitPresetCloud] Local read failed: {exception.Message}");
            return null;
        }
    }

    private void SaveLocalDocument()
    {
        try
        {
            var absolutePath = ProjectSettings.GlobalizePath(_localPath);
            var directory = Path.GetDirectoryName(absolutePath);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);
            var tempPath = absolutePath + ".tmp";
            File.WriteAllText(tempPath, JsonSerializer.Serialize(_document, JsonOptions));
            File.Move(tempPath, absolutePath, overwrite: true);
        }
        catch (Exception exception)
        {
            GD.PushWarning($"[OutfitPresetCloud] Local write failed: {exception.Message}");
        }
    }

    private void ScheduleRetry(string message)
    {
        _nextAttemptSeconds = Time.GetTicksMsec() / 1000.0 + RetrySeconds;
        GD.PushWarning($"[OutfitPresetCloud] {message} Retrying in {RetrySeconds:0}s.");
    }

    private static OutfitPresetCloudDocument MergeDocuments(
        OutfitPresetCloudDocument local,
        OutfitPresetCloudDocument remote)
    {
        var merged = new OutfitPresetCloudDocument();
        for (var slotIndex = 0; slotIndex < SlotCount; slotIndex++)
        {
            local.Slots.TryGetValue(slotIndex, out var localSlot);
            remote.Slots.TryGetValue(slotIndex, out var remoteSlot);
            var selected = SelectNewer(localSlot, remoteSlot);
            if (selected != null)
                merged.Slots[slotIndex] = CloneSlot(selected);
        }
        return merged;
    }

    private static OutfitPresetSlotRecord? SelectNewer(
        OutfitPresetSlotRecord? local,
        OutfitPresetSlotRecord? remote)
    {
        if (local == null)
            return remote;
        if (remote == null)
            return local;
        if (local.UpdatedAtUnixMilliseconds != remote.UpdatedAtUnixMilliseconds)
            return local.UpdatedAtUnixMilliseconds > remote.UpdatedAtUnixMilliseconds ? local : remote;

        // Deterministic tie breaker: deletion wins, then lexical JSON ordering.
        if (local.Deleted != remote.Deleted)
            return local.Deleted ? local : remote;
        var localKey = JsonSerializer.Serialize(NormalizeEquipment(local.EquippedItemIdsByType));
        var remoteKey = JsonSerializer.Serialize(NormalizeEquipment(remote.EquippedItemIdsByType));
        return string.CompareOrdinal(localKey, remoteKey) >= 0 ? local : remote;
    }

    private static bool TryParseDocument(
        string json,
        out OutfitPresetCloudDocument document,
        out string failure)
    {
        document = new OutfitPresetCloudDocument();
        failure = string.Empty;
        try
        {
            var parsed = JsonSerializer.Deserialize<OutfitPresetCloudDocument>(json);
            if (parsed?.Version != OutfitPresetCloudDocument.CurrentVersion)
            {
                failure = $"unsupported version {parsed?.Version ?? 0}";
                return false;
            }
            document = NormalizeDocument(parsed);
            return true;
        }
        catch (Exception exception)
        {
            failure = exception.Message;
            return false;
        }
    }

    private static OutfitPresetCloudDocument NormalizeDocument(OutfitPresetCloudDocument source)
    {
        var normalized = new OutfitPresetCloudDocument();
        foreach (var (slotIndex, slot) in (source.Slots ?? new Dictionary<int, OutfitPresetSlotRecord>())
                     .OrderBy(pair => pair.Key))
        {
            if (!IsValidSlot(slotIndex) || slot == null)
                continue;
            normalized.Slots[slotIndex] = new OutfitPresetSlotRecord
            {
                SlotIndex = slotIndex,
                UpdatedAtUnixMilliseconds = Math.Max(0, slot.UpdatedAtUnixMilliseconds),
                Deleted = slot.Deleted,
                EquippedItemIdsByType = slot.Deleted
                    ? new Dictionary<string, int>()
                    : NormalizeEquipment(slot.EquippedItemIdsByType),
            };
        }
        return normalized;
    }

    private static Dictionary<string, int> NormalizeEquipment(
        IReadOnlyDictionary<string, int>? source) =>
        (source ?? new Dictionary<string, int>())
            .Where(pair => Enum.TryParse<DataTables.EItemType>(pair.Key, out var type)
                           && PlayerInventory.IsOutfitPresetType(type)
                           && pair.Value > 0)
            .OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);

    private static OutfitPresetSlotRecord CloneSlot(OutfitPresetSlotRecord source) =>
        new()
        {
            SlotIndex = source.SlotIndex,
            UpdatedAtUnixMilliseconds = source.UpdatedAtUnixMilliseconds,
            Deleted = source.Deleted,
            EquippedItemIdsByType = new Dictionary<string, int>(source.EquippedItemIdsByType, StringComparer.Ordinal),
        };

    private static bool DocumentsEqual(
        OutfitPresetCloudDocument left,
        OutfitPresetCloudDocument right) =>
        JsonSerializer.Serialize(NormalizeDocument(left))
        == JsonSerializer.Serialize(NormalizeDocument(right));

    private static bool IsValidSlot(int slotIndex) => slotIndex is >= 0 and < SlotCount;
    private static long NowUnixMilliseconds() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
}
