#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using DataTables;
using Godot;

namespace LuckyDogRise;

public sealed class ConsumableCloudDocument
{
    public const int CurrentVersion = 1;

    public int Version { get; set; } = CurrentVersion;
    public long UpdatedAtUnixMilliseconds { get; set; }
    public Dictionary<int, int> ItemCounts { get; set; } = new();
}

public sealed class ConsumableCloudLocalState
{
    public bool CloudBaselineEstablished { get; set; }
    public ConsumableCloudDocument Document { get; set; } = new();
}

/// <summary>
/// Synchronizes only RefreshmentBlindBox item counts. Buff duration, selected
/// table refreshment and New markers remain local profile state.
/// </summary>
public sealed class ConsumableCloudSynchronizer
{
    public const string CloudFileName = "consumables_v1.json";
    private const double UploadDebounceSeconds = 3.0;
    private const double RetrySeconds = 15.0;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
    };

    private readonly IPlatformCloudStorageService _cloud;
    private readonly PlayerInventory _inventory;
    private readonly string _localPath;
    private ConsumableCloudLocalState _localState;
    private double _nextAttemptSeconds;
    private bool _dirty;
    private bool _sessionReconciled;
    private bool _applyingRemote;
    private bool _disposed;

    public ConsumableCloudSynchronizer(
        IPlatformCloudStorageService cloud,
        AccountStorageContext storageContext,
        PlayerInventory inventory,
        bool trustCurrentLocalCounts)
    {
        _cloud = cloud;
        _inventory = inventory;
        _localPath = storageContext.ConsumableCloudLocalPath;
        _localState = LoadLocalState() ?? new ConsumableCloudLocalState
        {
            CloudBaselineEstablished = false,
            Document = CreateDocument(CaptureCounts(), updatedAtUnixMilliseconds: 0),
        };

        var currentCounts = CaptureCounts();
        if (trustCurrentLocalCounts
            && !CountsEqual(_localState.Document.ItemCounts, currentCounts))
        {
            _localState.Document = CreateDocument(currentCounts, NowUnixMilliseconds());
            _dirty = true;
            SaveLocalState();
        }

        _inventory.InventoryChanged += OnInventoryChanged;
        TrySynchronizeNow();
    }

    public void Process()
    {
        if (_disposed || Time.GetTicksMsec() / 1000.0 < _nextAttemptSeconds)
            return;

        if (!_sessionReconciled || _dirty)
            TrySynchronizeNow();
    }

    public void FlushForShutdown()
    {
        if (_disposed)
            return;
        CaptureLocalMutation();
        TrySynchronizeNow();
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _inventory.InventoryChanged -= OnInventoryChanged;
        _disposed = true;
    }

    private void OnInventoryChanged()
    {
        if (_applyingRemote || _disposed)
            return;
        CaptureLocalMutation();
    }

    private void CaptureLocalMutation()
    {
        var currentCounts = CaptureCounts();
        if (CountsEqual(_localState.Document.ItemCounts, currentCounts))
            return;

        _localState.Document = CreateDocument(currentCounts, NowUnixMilliseconds());
        _dirty = true;
        SaveLocalState();
        _nextAttemptSeconds = Time.GetTicksMsec() / 1000.0 + UploadDebounceSeconds;
    }

    private void TrySynchronizeNow()
    {
        if (!_cloud.IsCloudAvailable)
        {
            ScheduleRetry("Steam Cloud 当前不可用，保留本地消耗品数据。");
            return;
        }

        if (!_localState.CloudBaselineEstablished)
        {
            EstablishCloudBaseline();
            return;
        }

        if (!_sessionReconciled)
        {
            ReconcileExistingBaseline();
            return;
        }

        if (!_dirty)
            return;

        WriteCurrentDocument();
    }

    private void ReconcileExistingBaseline()
    {
        var remoteResult = _cloud.ReadCloudTextFile(CloudFileName);
        if (!remoteResult.Succeeded)
        {
            ScheduleRetry(remoteResult.Message);
            return;
        }

        if (!remoteResult.Exists)
        {
            _dirty = true;
            WriteCurrentDocument();
            return;
        }

        if (!TryParseDocument(remoteResult.Content, out var remoteDocument, out var failure))
        {
            ScheduleRetry($"Steam Cloud 消耗品文件无法读取，未覆盖本地数据：{failure}");
            return;
        }

        if (remoteDocument.UpdatedAtUnixMilliseconds > _localState.Document.UpdatedAtUnixMilliseconds)
        {
            _localState.Document = remoteDocument;
            ApplyDocumentToInventory(remoteDocument);
            SaveLocalState();
            _dirty = false;
            GD.Print($"[ConsumableCloud] Applied newer {CloudFileName} from Steam Cloud.");
        }
        else if (remoteDocument.UpdatedAtUnixMilliseconds < _localState.Document.UpdatedAtUnixMilliseconds)
        {
            _dirty = true;
            WriteCurrentDocument();
            return;
        }
        else if (!CountsEqual(remoteDocument.ItemCounts, _localState.Document.ItemCounts))
        {
            var convergedCounts = GetConsumableItemIds()
                .ToDictionary(
                    itemId => itemId,
                    itemId => Math.Max(
                        _localState.Document.ItemCounts.GetValueOrDefault(itemId),
                        remoteDocument.ItemCounts.GetValueOrDefault(itemId)))
                .Where(pair => pair.Value > 0)
                .ToDictionary(pair => pair.Key, pair => pair.Value);
            _localState.Document = CreateDocument(convergedCounts, NowUnixMilliseconds());
            ApplyDocumentToInventory(_localState.Document);
            SaveLocalState();
            _dirty = true;
            WriteCurrentDocument();
            return;
        }

        _sessionReconciled = true;
        _nextAttemptSeconds = double.PositiveInfinity;
    }

    private void EstablishCloudBaseline()
    {
        var remoteResult = _cloud.ReadCloudTextFile(CloudFileName);
        if (!remoteResult.Succeeded)
        {
            ScheduleRetry(remoteResult.Message);
            return;
        }

        if (!remoteResult.Exists)
        {
            _localState.Document = CreateDocument(CaptureCounts(), NowUnixMilliseconds());
            _localState.CloudBaselineEstablished = true;
            _dirty = true;
            SaveLocalState();
            WriteCurrentDocument();
            return;
        }

        if (!TryParseDocument(remoteResult.Content, out var remoteDocument, out var failure))
        {
            ScheduleRetry($"Steam Cloud 消耗品文件无法读取，未覆盖本地数据：{failure}");
            return;
        }

        // The first version upgrade has no common baseline. Preserve both sides by
        // taking the larger count per item. After this one-time convergence, normal
        // changes use last-write-wins via UpdatedAtUnixMilliseconds.
        var mergedCounts = GetConsumableItemIds().ToDictionary(
            itemId => itemId,
            itemId => Math.Max(
                _localState.Document.ItemCounts.GetValueOrDefault(itemId),
                remoteDocument.ItemCounts.GetValueOrDefault(itemId)));
        mergedCounts = mergedCounts
            .Where(pair => pair.Value > 0)
            .ToDictionary(pair => pair.Key, pair => pair.Value);

        _localState.Document = CreateDocument(mergedCounts, NowUnixMilliseconds());
        _localState.CloudBaselineEstablished = true;
        ApplyDocumentToInventory(_localState.Document);
        _dirty = true;
        SaveLocalState();
        WriteCurrentDocument();
    }

    private void WriteCurrentDocument()
    {
        var json = JsonSerializer.Serialize(_localState.Document, JsonOptions);
        if (!_cloud.TryWriteCloudTextFile(CloudFileName, json, out var message))
        {
            ScheduleRetry(message);
            return;
        }

        _dirty = false;
        _sessionReconciled = true;
        _nextAttemptSeconds = double.PositiveInfinity;
        GD.Print($"[ConsumableCloud] {message}");
        DiagnosticLog.Record("consumable_cloud_synchronized", new Dictionary<string, object>
        {
            ["fileName"] = CloudFileName,
            ["itemKinds"] = _localState.Document.ItemCounts.Count,
            ["updatedAtUnixMilliseconds"] = _localState.Document.UpdatedAtUnixMilliseconds,
        });
    }

    private void ApplyDocumentToInventory(ConsumableCloudDocument document)
    {
        _applyingRemote = true;
        try
        {
            _inventory.ReplaceItemCounts(
                GetConsumableItemIds(),
                document.ItemCounts,
                emitChanged: true);
        }
        finally
        {
            _applyingRemote = false;
        }
    }

    private Dictionary<int, int> CaptureCounts()
    {
        var counts = _inventory.GetOwnedItemCounts();
        return GetConsumableItemIds()
            .Where(itemId => counts.GetValueOrDefault(itemId) > 0)
            .ToDictionary(itemId => itemId, itemId => counts[itemId]);
    }

    private ConsumableCloudLocalState? LoadLocalState()
    {
        try
        {
            var absolutePath = ProjectSettings.GlobalizePath(_localPath);
            if (!File.Exists(absolutePath))
                return null;
            var state = JsonSerializer.Deserialize<ConsumableCloudLocalState>(File.ReadAllText(absolutePath));
            if (state?.Document == null
                || state.Document.Version != ConsumableCloudDocument.CurrentVersion)
                return null;
            state.Document.ItemCounts = NormalizeCounts(state.Document.ItemCounts);
            return state;
        }
        catch (Exception exception)
        {
            GD.PushWarning($"[ConsumableCloud] Local mirror read failed: {exception.Message}");
            return null;
        }
    }

    private void SaveLocalState()
    {
        try
        {
            var absolutePath = ProjectSettings.GlobalizePath(_localPath);
            var directory = Path.GetDirectoryName(absolutePath);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);
            var tempPath = absolutePath + ".tmp";
            File.WriteAllText(tempPath, JsonSerializer.Serialize(_localState, JsonOptions));
            File.Move(tempPath, absolutePath, overwrite: true);
        }
        catch (Exception exception)
        {
            GD.PushWarning($"[ConsumableCloud] Local mirror write failed: {exception.Message}");
        }
    }

    private void ScheduleRetry(string message)
    {
        _nextAttemptSeconds = Time.GetTicksMsec() / 1000.0 + RetrySeconds;
        GD.PushWarning($"[ConsumableCloud] {message} Retrying in {RetrySeconds:0}s.");
    }

    private static bool TryParseDocument(
        string json,
        out ConsumableCloudDocument document,
        out string failure)
    {
        document = new ConsumableCloudDocument();
        failure = string.Empty;
        try
        {
            var parsed = JsonSerializer.Deserialize<ConsumableCloudDocument>(json);
            if (parsed == null || parsed.Version != ConsumableCloudDocument.CurrentVersion)
            {
                failure = $"unsupported version {parsed?.Version ?? 0}";
                return false;
            }
            parsed.ItemCounts = NormalizeCounts(parsed.ItemCounts);
            document = parsed;
            return true;
        }
        catch (Exception exception)
        {
            failure = exception.Message;
            return false;
        }
    }

    private static ConsumableCloudDocument CreateDocument(
        IReadOnlyDictionary<int, int> counts,
        long updatedAtUnixMilliseconds) =>
        new()
        {
            Version = ConsumableCloudDocument.CurrentVersion,
            UpdatedAtUnixMilliseconds = updatedAtUnixMilliseconds,
            ItemCounts = NormalizeCounts(counts),
        };

    private static Dictionary<int, int> NormalizeCounts(IReadOnlyDictionary<int, int>? counts)
    {
        var validIds = GetConsumableItemIds().ToHashSet();
        return (counts ?? new Dictionary<int, int>())
            .Where(pair => validIds.Contains(pair.Key) && pair.Value > 0)
            .OrderBy(pair => pair.Key)
            .ToDictionary(pair => pair.Key, pair => pair.Value);
    }

    private static int[] GetConsumableItemIds() =>
        LubanData.Tables.TbItem.DataList
            .Where(item => item.ItemType == EItemType.Refreshment
                           && item.AcquisitionType == EAcquisitionType.RefreshmentBlindBox)
            .Select(item => item.Id)
            .OrderBy(id => id)
            .ToArray();

    private static bool CountsEqual(
        IReadOnlyDictionary<int, int> left,
        IReadOnlyDictionary<int, int> right) =>
        left.Count == right.Count
        && left.All(pair => right.GetValueOrDefault(pair.Key) == pair.Value);

    private static long NowUnixMilliseconds() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
}
