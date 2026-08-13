using System;
using System.Collections.Generic;
using System.Linq;
using Steamworks;

namespace LuckyDogRise;

public sealed class SteamGamePlatformService : IGamePlatformService, IPlatformAchievementTestOperations,
    IPlatformAchievementSyncOperations, IPlatformInventoryService
{
    private enum InventoryRequestKind
    {
        FullInventory,
        AddPromoItem,
        TriggerPlaytimeDrop,
    }

    private readonly record struct InventoryRequest(
        InventoryRequestKind Kind,
        int ItemDefId = 0,
        int OutputItemDefId = 0);

    private readonly SteamworksRuntime _runtime;
    private readonly Callback<UserStatsReceived_t> _userStatsReceivedCallback;
    private readonly Callback<UserStatsStored_t> _userStatsStoredCallback;
    private readonly Callback<UserAchievementStored_t> _userAchievementStoredCallback;
    private readonly Callback<SteamInventoryDefinitionUpdate_t> _inventoryDefinitionUpdateCallback;
    private readonly Callback<SteamInventoryFullUpdate_t> _inventoryFullUpdateCallback;
    private readonly Callback<SteamInventoryResultReady_t> _inventoryResultReadyCallback;
    private readonly Dictionary<int, InventoryRequest> _inventoryRequests = new();
    private readonly HashSet<int> _ownedInventoryItemDefIds = new();
    private readonly HashSet<int> _loggedPlaytimeGeneratorItemDefIds = new();
    private PlatformInventoryItem[] _inventoryItems = [];
    private bool _hasPendingAchievementStore;
    private bool _inventorySynchronizationStarted;
    private InventoryRequest? _promoItemAwaitingInventoryVerification;
    private InventoryRequest? _playtimeDropAwaitingInventoryVerification;

    public SteamGamePlatformService(SteamworksRuntime runtime)
    {
        _runtime = runtime;
        _userStatsReceivedCallback = Callback<UserStatsReceived_t>.Create(OnUserStatsReceived);
        _userStatsStoredCallback = Callback<UserStatsStored_t>.Create(OnUserStatsStored);
        _userAchievementStoredCallback = Callback<UserAchievementStored_t>.Create(OnUserAchievementStored);
        _inventoryDefinitionUpdateCallback = Callback<SteamInventoryDefinitionUpdate_t>.Create(OnInventoryDefinitionUpdated);
        _inventoryFullUpdateCallback = Callback<SteamInventoryFullUpdate_t>.Create(OnFullInventoryUpdated);
        _inventoryResultReadyCallback = Callback<SteamInventoryResultReady_t>.Create(OnInventoryResultReady);
    }

    public event Action UserStatsReady = delegate { };
    public event Action<string> StoreStatusChanged = delegate { };
    public event Action<PlatformInventorySnapshot> InventorySnapshotChanged = delegate { };
    public event Action<PlatformPromoItemGrantResult> PromoItemGrantCompleted = delegate { };
    public event Action<PlatformPlaytimeDropResult> PlaytimeDropCompleted = delegate { };

    public string ProviderName => "Steam";
    public string StatusMessage => _runtime.StatusMessage;
    public bool IsAvailable => _runtime.IsInitialized;
    public uint AppId => _runtime.AppId;
    public string PersonaName => _runtime.PersonaName;
    public string AccountProvider => "steam";
    public string AccountId => _runtime.SteamId.ToString(System.Globalization.CultureInfo.InvariantCulture);
    public bool IsReadyForWrites { get; private set; }
    public bool IsInventoryReady { get; private set; }
    public bool IsPromoGrantPending => _inventoryRequests.Values.Any(request =>
        request.Kind == InventoryRequestKind.AddPromoItem) || _promoItemAwaitingInventoryVerification != null;
    public bool IsPlaytimeDropPending => _inventoryRequests.Values.Any(request =>
        request.Kind == InventoryRequestKind.TriggerPlaytimeDrop)
        || _playtimeDropAwaitingInventoryVerification != null;
    public IReadOnlyList<PlatformInventoryItem> InventoryItems => _inventoryItems;

    public void RunCallbacks() => _runtime.RunCallbacks();
    public bool TryGetLiveAccountId(out string accountId)
    {
        accountId = string.Empty;
        if (!_runtime.TryGetCurrentSteamId(out var steamId))
            return false;
        accountId = steamId.ToString(System.Globalization.CultureInfo.InvariantCulture);
        return true;
    }
    public bool OpenFriendsOverlay() => _runtime.OpenFriendsOverlay();

    public void StartInventorySynchronization()
    {
        if (!IsAvailable || _inventorySynchronizationStarted)
            return;

        _inventorySynchronizationStarted = true;
        RestartInventorySynchronization();
    }

    public void RestartInventorySynchronization()
    {
        if (!IsAvailable)
            return;

        CancelPendingInventorySynchronization();
        IsInventoryReady = false;
        if (!SteamInventory.LoadItemDefinitions())
            Godot.GD.PushWarning("[SteamInventory] Steam 拒绝刷新库存 ItemDef；继续读取玩家库存。");

        // DefinitionUpdate is a cache/update notification, not a reliable startup gate.
        // GetAllItems must run on every launch even when Steam has no definition update to publish.
        RequestFullInventory();
    }

    public void CancelPendingInventorySynchronization()
    {
        var fullInventoryHandles = _inventoryRequests
            .Where(pair => pair.Value.Kind == InventoryRequestKind.FullInventory)
            .Select(pair => pair.Key)
            .ToArray();
        foreach (var handleValue in fullInventoryHandles)
        {
            SteamInventory.DestroyResult((SteamInventoryResult_t)handleValue);
            _inventoryRequests.Remove(handleValue);
        }
    }

    public bool RecoverTimedOutPromoGrant()
    {
        var promoRequest = _inventoryRequests.FirstOrDefault(pair =>
            pair.Value.Kind == InventoryRequestKind.AddPromoItem);
        if (promoRequest.Value.Kind != InventoryRequestKind.AddPromoItem || promoRequest.Value.ItemDefId <= 0)
            return false;

        SteamInventory.DestroyResult((SteamInventoryResult_t)promoRequest.Key);
        _inventoryRequests.Remove(promoRequest.Key);
        _promoItemAwaitingInventoryVerification = promoRequest.Value;
        if (RequestFullInventory())
            return true;

        var request = _promoItemAwaitingInventoryVerification.Value;
        _promoItemAwaitingInventoryVerification = null;
        PromoItemGrantCompleted(new PlatformPromoItemGrantResult(
            request.ItemDefId,
            request.OutputItemDefId,
            false,
            false,
            $"Steam 领奖请求超时，无法复查回执 ItemDef={request.OutputItemDefId}。",
            []));
        return false;
    }

    public bool TryGrantPromoItem(int promoItemDefId, int receiptItemDefId, out string message)
    {
        if (!IsAvailable || !IsInventoryReady)
        {
            message = "Steam 库存尚未同步完成。";
            return false;
        }
        if (promoItemDefId <= 0 || receiptItemDefId <= 0)
        {
            message = $"无效的 Steam Promo/回执 ItemDef：{promoItemDefId}/{receiptItemDefId}";
            return false;
        }
        if (_ownedInventoryItemDefIds.Contains(receiptItemDefId))
        {
            message = $"Steam 库存已拥有回执 ItemDef={receiptItemDefId}。";
            return false;
        }
        if (_inventoryRequests.Count > 0 || IsPromoGrantPending)
        {
            message = "已有 Steam 库存请求正在处理。";
            return false;
        }

        if (!SteamInventory.AddPromoItem(out var handle, (SteamItemDef_t)promoItemDefId))
        {
            message = $"Steam 拒绝 AddPromoItem({promoItemDefId}) 请求。";
            return false;
        }

        _inventoryRequests[HandleValue(handle)] = new InventoryRequest(
            InventoryRequestKind.AddPromoItem,
            promoItemDefId,
            OutputItemDefId: receiptItemDefId);
        message = $"已提交 AddPromoItem({promoItemDefId})，等待回执 ItemDef={receiptItemDefId}。";
        return true;
    }

    public bool TryTriggerPlaytimeDrop(int generatorItemDefId, out string message)
    {
        if (!IsAvailable || !IsInventoryReady)
        {
            message = "Steam 库存尚未同步完成。";
            return false;
        }
        if (generatorItemDefId <= 0)
        {
            message = "Steam 游玩投放参数无效。";
            return false;
        }
        if (_inventoryRequests.Count > 0 || IsPromoGrantPending || IsPlaytimeDropPending)
        {
            message = "已有 Steam 库存请求正在处理。";
            return false;
        }

        LogPlaytimeGeneratorDefinitionOnce(generatorItemDefId);

        if (!SteamInventory.TriggerItemDrop(out var handle, (SteamItemDef_t)generatorItemDefId))
        {
            message = $"Steam 拒绝 TriggerItemDrop({generatorItemDefId}) 请求。";
            return false;
        }

        _inventoryRequests[HandleValue(handle)] = new InventoryRequest(
            InventoryRequestKind.TriggerPlaytimeDrop,
            generatorItemDefId);
        message = $"已提交 TriggerItemDrop({generatorItemDefId})，等待 Steam 回执。";
        return true;
    }

    private void LogPlaytimeGeneratorDefinitionOnce(int generatorItemDefId)
    {
        if (!_loggedPlaytimeGeneratorItemDefIds.Add(generatorItemDefId))
            return;

        var properties = new[]
        {
            "type",
            "bundle",
            "drop_interval",
            "use_drop_limit",
            "drop_limit",
            "use_drop_window",
            "drop_window",
            "drop_max_per_window",
        };
        var values = new List<string>();
        foreach (var property in properties)
        {
            var bufferSize = 4096u;
            if (SteamInventory.GetItemDefinitionProperty(
                    (SteamItemDef_t)generatorItemDefId,
                    property,
                    out var value,
                    ref bufferSize)
                && !string.IsNullOrWhiteSpace(value))
            {
                values.Add($"{property}={value}");
            }
        }

        var definitionSummary = values.Count > 0
            ? string.Join(", ", values)
            : "properties unavailable from Steam cache";
        Godot.GD.Print($"[SteamInventory] Active ItemDef {generatorItemDefId}: {definitionSummary}.");
        DiagnosticLog.Record("steam_playtime_generator_definition", new Dictionary<string, object>
        {
            ["generatorItemDefId"] = generatorItemDefId,
            ["properties"] = definitionSummary,
        });
    }

    public bool RecoverTimedOutPlaytimeDrop()
    {
        var requestPair = _inventoryRequests.FirstOrDefault(pair =>
            pair.Value.Kind == InventoryRequestKind.TriggerPlaytimeDrop);
        if (requestPair.Value.Kind != InventoryRequestKind.TriggerPlaytimeDrop)
            return false;

        SteamInventory.DestroyResult((SteamInventoryResult_t)requestPair.Key);
        _inventoryRequests.Remove(requestPair.Key);
        _playtimeDropAwaitingInventoryVerification = requestPair.Value;
        if (RequestFullInventory())
            return true;

        var request = _playtimeDropAwaitingInventoryVerification.Value;
        _playtimeDropAwaitingInventoryVerification = null;
        PlaytimeDropCompleted(new PlatformPlaytimeDropResult(
            request.ItemDefId,
            false,
            "Steam 游玩投放请求超时，无法启动库存复查。",
            []));
        return false;
    }

    public PlatformAchievementReadResult ReadAchievementStates(IEnumerable<string> achievementApiNames)
    {
        if (!IsAvailable)
            return new(false, StatusMessage, Array.Empty<PlatformAchievementState>());

        try
        {
            var configuredNames = new HashSet<string>(StringComparer.Ordinal);
            var achievementCount = SteamUserStats.GetNumAchievements();
            for (uint index = 0; index < achievementCount; index++)
            {
                var apiName = SteamUserStats.GetAchievementName(index);
                if (!string.IsNullOrWhiteSpace(apiName))
                    configuredNames.Add(apiName);
            }

            var states = achievementApiNames
                .Where(apiName => !string.IsNullOrWhiteSpace(apiName))
                .Distinct(StringComparer.Ordinal)
                .Select(apiName => ReadAchievementState(apiName, configuredNames))
                .ToArray();
            if (states.Any(state => state.IsConfigured && state.ReadSucceeded))
                IsReadyForWrites = true;
            return new(true, $"Steam 返回 {achievementCount} 项成就定义。", states);
        }
        catch (Exception exception)
        {
            return new(false, $"读取 Steam 成就失败：{exception.GetType().Name}: {exception.Message}", Array.Empty<PlatformAchievementState>());
        }
    }

    public bool TrySetAchievementForTesting(string apiName, bool unlocked, out string message)
    {
        if (!IsAvailable || !IsReadyForWrites)
        {
            message = "Steam 用户统计尚未就绪，拒绝写入。";
            return false;
        }

        if (string.IsNullOrWhiteSpace(apiName) || !SteamUserStats.GetAchievement(apiName, out _))
        {
            message = $"Steam 后台不存在成就：{apiName}";
            return false;
        }

        var changed = unlocked
            ? SteamUserStats.SetAchievement(apiName)
            : SteamUserStats.ClearAchievement(apiName);
        if (!changed)
        {
            message = $"Steam 拒绝{(unlocked ? "解锁" : "清除")}成就：{apiName}";
            return false;
        }

        if (!SteamUserStats.StoreStats())
        {
            message = $"已修改内存状态，但 StoreStats 请求失败：{apiName}";
            return false;
        }

        message = $"已提交{(unlocked ? "解锁" : "清除")}请求：{apiName}，等待 Steam 回调。";
        return true;
    }

    public PlatformAchievementUnlockResult UnlockAchievements(IEnumerable<string> achievementApiNames)
    {
        if (!IsAvailable || !IsReadyForWrites)
            return new(false, "Steam 用户统计尚未就绪，拒绝写入。", Array.Empty<string>());

        var submittedApiNames = new List<string>();
        foreach (var apiName in achievementApiNames
                     .Where(apiName => !string.IsNullOrWhiteSpace(apiName))
                     .Distinct(StringComparer.Ordinal))
        {
            if (!SteamUserStats.GetAchievement(apiName, out var isUnlocked))
                continue;
            if (isUnlocked)
                continue;
            if (!SteamUserStats.SetAchievement(apiName))
                return new(false, $"Steam 拒绝解锁成就：{apiName}", submittedApiNames);
            submittedApiNames.Add(apiName);
            _hasPendingAchievementStore = true;
        }

        if (!_hasPendingAchievementStore)
            return new(true, "没有需要提交的 Steam 成就。", submittedApiNames);
        if (!SteamUserStats.StoreStats())
            return new(false, "成就已写入 Steam 内存状态，但 StoreStats 请求失败。", submittedApiNames);

        _hasPendingAchievementStore = false;
        return new(true, $"已向 Steam 提交 {submittedApiNames.Count} 项成就。", submittedApiNames);
    }

    public void Dispose()
    {
        foreach (var handleValue in _inventoryRequests.Keys.ToArray())
            SteamInventory.DestroyResult((SteamInventoryResult_t)handleValue);
        _inventoryRequests.Clear();
        _inventoryResultReadyCallback.Dispose();
        _inventoryFullUpdateCallback.Dispose();
        _inventoryDefinitionUpdateCallback.Dispose();
        _userAchievementStoredCallback.Dispose();
        _userStatsStoredCallback.Dispose();
        _userStatsReceivedCallback.Dispose();
        _runtime.Dispose();
    }

    private void OnInventoryDefinitionUpdated(SteamInventoryDefinitionUpdate_t callback)
    {
        var count = 0u;
        if (!SteamInventory.GetItemDefinitionIDs(null!, ref count))
        {
            PublishInventoryFailure("Steam ItemDef 数量读取失败。");
            return;
        }

        var serverDefinitions = new SteamItemDef_t[count];
        if (count > 0 && !SteamInventory.GetItemDefinitionIDs(serverDefinitions, ref count))
        {
            PublishInventoryFailure("Steam ItemDef 内容读取失败。");
            return;
        }

        var serverIds = serverDefinitions.Select(itemDef => (int)itemDef).ToHashSet();
        var missingIds = LubanData.Tables.TbSteamItemDef.DataList
            // Steam does not enumerate published playtime generators here. Their validity is
            // checked by the TriggerItemDrop result when a schedule requests a drop.
            .Where(itemDef => itemDef.IsEnabled &&
                              itemDef.Type != DataTables.ESteamItemDefType.PlaytimeGenerator &&
                              !serverIds.Contains(itemDef.Id))
            .Select(itemDef => itemDef.Id)
            .Concat(LubanData.Tables.TbItem.DataList
                .Where(item => item.SteamItemDefId > 0 && !serverIds.Contains(item.SteamItemDefId))
                .Select(item => item.SteamItemDefId))
            .Distinct()
            .OrderBy(id => id)
            .ToArray();
        if (missingIds.Length > 0)
        {
            PublishInventoryFailure($"Steam 后台缺少 ItemDef：{string.Join(", ", missingIds)}");
            return;
        }

        if (!IsInventoryReady)
            RequestFullInventory();
    }

    private void OnFullInventoryUpdated(SteamInventoryFullUpdate_t callback)
    {
        var handleValue = HandleValue(callback.m_handle);
        _inventoryRequests.TryAdd(handleValue, new InventoryRequest(InventoryRequestKind.FullInventory));
    }

    private void OnInventoryResultReady(SteamInventoryResultReady_t callback)
    {
        var handle = callback.m_handle;
        var handleValue = HandleValue(handle);
        var isTracked = _inventoryRequests.Remove(handleValue, out var request);

        try
        {
            if (!isTracked)
                return;
            if (!SteamInventory.CheckResultSteamID(handle, new CSteamID(_runtime.SteamId)))
            {
                CompleteInventoryRequestFailure(request, "Steam 库存结果的 SteamID 校验失败。");
                return;
            }
            if (callback.m_result != EResult.k_EResultOK)
            {
                CompleteInventoryRequestFailure(request, $"Steam 库存请求失败：{callback.m_result}");
                return;
            }

            var items = ReadInventoryResultItems(handle);
            if (items == null)
            {
                CompleteInventoryRequestFailure(request, "Steam GetResultItems 失败。");
                return;
            }

            if (request.Kind == InventoryRequestKind.FullInventory)
                ApplyFullInventory(items);
            else if (request.Kind == InventoryRequestKind.AddPromoItem)
                ApplyPromoGrantResult(request, items);
            else if (request.Kind == InventoryRequestKind.TriggerPlaytimeDrop)
                ApplyPlaytimeDropResult(request, items);
        }
        finally
        {
            SteamInventory.DestroyResult(handle);
        }
    }

    private void ApplyFullInventory(IReadOnlyCollection<SteamItemDetails_t> items)
    {
        _inventoryItems = items
            .Where(item => item.m_unQuantity > 0)
            .Select(ToPlatformInventoryItem)
            .ToArray();
        _ownedInventoryItemDefIds.Clear();
        foreach (var item in _inventoryItems)
            _ownedInventoryItemDefIds.Add(item.ItemDefId);

        IsInventoryReady = true;
        InventorySnapshotChanged(new PlatformInventorySnapshot(
            true,
            $"Steam 库存同步完成：{items.Count} 个实例/堆叠。",
            new HashSet<int>(_ownedInventoryItemDefIds),
            _inventoryItems.ToArray()));

        if (_promoItemAwaitingInventoryVerification is { } promoRequest)
        {
            _promoItemAwaitingInventoryVerification = null;
            var receiptOwned = _ownedInventoryItemDefIds.Contains(promoRequest.OutputItemDefId);
            PromoItemGrantCompleted(new PlatformPromoItemGrantResult(
                promoRequest.ItemDefId,
                promoRequest.OutputItemDefId,
                receiptOwned,
                receiptOwned,
                receiptOwned
                    ? $"Steam 库存已确认回执 ItemDef={promoRequest.OutputItemDefId}。"
                    : $"Steam 未发放回执 ItemDef={promoRequest.OutputItemDefId}。",
                []));
        }

        CompletePlaytimeDropInventoryVerification();
    }

    private void ApplyPlaytimeDropResult(
        InventoryRequest request,
        IReadOnlyCollection<SteamItemDetails_t> items)
    {
        var changedItems = items.Select(ToPlatformInventoryItem).ToArray();
        PlaytimeDropCompleted(new PlatformPlaytimeDropResult(
            request.ItemDefId,
            true,
            changedItems.Length > 0
                ? $"Steam 已处理 PlaytimeGenerator {request.ItemDefId}，返回 {changedItems.Length} 个库存变化。"
                : $"Steam 已处理 PlaytimeGenerator {request.ItemDefId}，本次没有返回物品。",
            changedItems));
        // An empty TriggerItemDrop result does not guarantee that the cached full inventory is
        // current. Always reconcile once so the business layer can attribute instance deltas.
        RequestFullInventory();
    }

    private void ApplyPromoGrantResult(InventoryRequest request, IReadOnlyCollection<SteamItemDetails_t> items)
    {
        var changedItems = items.Select(ToPlatformInventoryItem).ToArray();
        var receiptOwned = changedItems.Any(item =>
            item.ItemDefId == request.OutputItemDefId && item.Quantity > 0);
        if (receiptOwned)
        {
            _ownedInventoryItemDefIds.Add(request.OutputItemDefId);
            PromoItemGrantCompleted(new PlatformPromoItemGrantResult(
                request.ItemDefId,
                request.OutputItemDefId,
                true,
                true,
                $"Steam 已通过 Promo ItemDef={request.ItemDefId} 发放回执 ItemDef={request.OutputItemDefId}。",
                changedItems));
            RequestFullInventory();
            return;
        }

        _promoItemAwaitingInventoryVerification = request;
        if (!RequestFullInventory())
        {
            _promoItemAwaitingInventoryVerification = null;
            PromoItemGrantCompleted(new PlatformPromoItemGrantResult(
                request.ItemDefId,
                request.OutputItemDefId,
                false,
                false,
                $"Steam 未返回回执 ItemDef={request.OutputItemDefId}，且库存复查请求失败。",
                changedItems));
        }
    }

    private bool RequestFullInventory()
    {
        if (_inventoryRequests.Values.Any(request => request.Kind == InventoryRequestKind.FullInventory))
            return true;
        if (!SteamInventory.GetAllItems(out var handle))
        {
            PublishInventoryFailure("Steam 拒绝 GetAllItems 请求。");
            return false;
        }

        _inventoryRequests[HandleValue(handle)] = new InventoryRequest(InventoryRequestKind.FullInventory);
        return true;
    }

    private void CompleteInventoryRequestFailure(InventoryRequest request, string message)
    {
        if (request.Kind == InventoryRequestKind.AddPromoItem)
        {
            _promoItemAwaitingInventoryVerification = null;
            PromoItemGrantCompleted(new PlatformPromoItemGrantResult(
                request.ItemDefId,
                request.OutputItemDefId,
                false,
                false,
                message,
                []));
            return;
        }

        if (request.Kind == InventoryRequestKind.TriggerPlaytimeDrop)
        {
            _playtimeDropAwaitingInventoryVerification = null;
            PlaytimeDropCompleted(new PlatformPlaytimeDropResult(
                request.ItemDefId,
                false,
                message,
                []));
            return;
        }

        PublishInventoryFailure(message);
    }

    private void PublishInventoryFailure(string message)
    {
        IsInventoryReady = false;
        Godot.GD.PushWarning($"[SteamInventory] {message}");
        InventorySnapshotChanged(new PlatformInventorySnapshot(
            false, message, new HashSet<int>(_ownedInventoryItemDefIds), _inventoryItems.ToArray()));
    }

    private static SteamItemDetails_t[] ReadInventoryResultItems(SteamInventoryResult_t handle)
    {
        var count = 0u;
        if (!SteamInventory.GetResultItems(handle, null!, ref count))
            return null;
        if (count == 0)
            return Array.Empty<SteamItemDetails_t>();

        var items = new SteamItemDetails_t[count];
        return SteamInventory.GetResultItems(handle, items, ref count) ? items : null;
    }

    private static int HandleValue(SteamInventoryResult_t handle) => (int)handle;

    private void CompletePlaytimeDropInventoryVerification()
    {
        if (_playtimeDropAwaitingInventoryVerification is not { } request)
            return;

        _playtimeDropAwaitingInventoryVerification = null;
        PlaytimeDropCompleted(new PlatformPlaytimeDropResult(
            request.ItemDefId,
            true,
            $"Steam 已完成 PlaytimeGenerator {request.ItemDefId} 的库存复查。",
            []));
    }

    private static PlatformInventoryItem ToPlatformInventoryItem(SteamItemDetails_t item) => new(
        (ulong)item.m_itemId,
        (int)item.m_iDefinition,
        item.m_unQuantity);

    private static PlatformAchievementState ReadAchievementState(string apiName, HashSet<string> configuredNames)
    {
        if (!configuredNames.Contains(apiName))
            return new(apiName, IsConfigured: false, ReadSucceeded: false, IsUnlocked: false);

        var readSucceeded = SteamUserStats.GetAchievement(apiName, out var isUnlocked);
        return new(apiName, IsConfigured: true, readSucceeded, isUnlocked);
    }

    private void OnUserStatsReceived(UserStatsReceived_t callback)
    {
        if (callback.m_nGameID != AppId)
            return;

        if (callback.m_eResult != EResult.k_EResultOK)
        {
            Godot.GD.PushWarning($"[Steamworks] UserStatsReceived failed: {callback.m_eResult}");
            return;
        }

        Godot.GD.Print($"[Steamworks] User stats ready for AppID {AppId}.");
        IsReadyForWrites = true;
        UserStatsReady();
    }

    private void OnUserStatsStored(UserStatsStored_t callback)
    {
        if (callback.m_nGameID != AppId)
            return;

        var message = callback.m_eResult == EResult.k_EResultOK
            ? "Steam 已持久化成就/统计状态。"
            : $"Steam StoreStats 失败：{callback.m_eResult}";
        if (callback.m_eResult != EResult.k_EResultOK)
            _hasPendingAchievementStore = true;
        Godot.GD.Print($"[Steamworks] {message}");
        StoreStatusChanged(message);
    }

    private void OnUserAchievementStored(UserAchievementStored_t callback)
    {
        if (callback.m_nGameID != AppId)
            return;

        var message = callback.m_nMaxProgress == 0
            ? "Steam 已处理成就状态变更。"
            : $"Steam 已处理成就进度：{callback.m_nCurProgress}/{callback.m_nMaxProgress}";
        Godot.GD.Print($"[Steamworks] {message}");
        StoreStatusChanged(message);
    }
}
