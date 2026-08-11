using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Godot;
using Steamworks;

namespace LuckyDogRise;

public partial class SteamInventoryTestController : Control
{
    private const ulong DefinitionLoadTimeoutMsec = 10000;
    private const ulong DefinitionPollIntervalMsec = 250;
    private const ulong PlaytimeDropMinimumAttemptIntervalMsec = 65000;

    private enum InventoryRequestKind
    {
        FullInventory,
        AddPromoItem,
        TriggerPlaytimeDrop,
        ConsumeItem,
        GenerateItem,
    }

    private sealed record PlaytimeGeneratorTestOption(
        DataTables.SteamItemDef Definition,
        DataTables.BlindBoxSchedule Schedule);

    [Export] private Label _statusLabel = null!;
    [Export] private Label _identityLabel = null!;
    [Export] private Label _definitionStatusLabel = null!;
    [Export] private Label _inventoryStatusLabel = null!;
    [Export] private OptionButton _promoItemOption = null!;
    [Export] private Label _promoItemStatusLabel = null!;
    [Export] private OptionButton _playtimeGeneratorOption = null!;
    [Export] private OptionButton _maintenanceItemOption = null!;
    [Export] private CheckButton _enableGrantCheck = null!;
    [Export] private CheckButton _enablePlaytimeDropCheck = null!;
    [Export] private CheckButton _enableMaintenanceCheck = null!;
    [Export] private Button _loadDefinitionsButton = null!;
    [Export] private Button _refreshInventoryButton = null!;
    [Export] private Button _addPromoItemButton = null!;
    [Export] private Button _triggerPlaytimeDropButton = null!;
    [Export] private Button _consumeItemButton = null!;
    [Export] private Button _generateItemButton = null!;
    [Export] private Label _playtimeDropStatusLabel = null!;
    [Export] private Button _retryButton = null!;
    [Export] private Button _quitButton = null!;
    [Export] private TextEdit _operationLog = null!;

    private readonly Dictionary<int, InventoryRequestKind> _pendingRequests = new();
    private readonly Queue<string> _logLines = new();
    private readonly List<PlaytimeGeneratorTestOption> _playtimeGeneratorOptions = new();
    private SteamItemDetails_t[] _lastInventoryItems = Array.Empty<SteamItemDetails_t>();
    private SteamworksRuntime _runtime = null!;
    private Callback<SteamInventoryDefinitionUpdate_t> _definitionUpdateCallback = null!;
    private Callback<SteamInventoryFullUpdate_t> _fullUpdateCallback = null!;
    private Callback<SteamInventoryResultReady_t> _resultReadyCallback = null!;
    private bool _definitionsLoaded;
    private bool _inventoryLoaded;
    private ulong _nextPlaytimeDropAttemptMsec;
    private bool _definitionLoadPending;
    private ulong _definitionLoadDeadlineMsec;
    private ulong _nextDefinitionPollMsec;

    public override void _Ready()
    {
        _loadDefinitionsButton.Pressed += LoadDefinitions;
        _refreshInventoryButton.Pressed += RefreshInventory;
        _addPromoItemButton.Pressed += AddSelectedPromoItem;
        _triggerPlaytimeDropButton.Pressed += TriggerSelectedPlaytimeDrop;
        _consumeItemButton.Pressed += ConsumeSelectedItem;
        _generateItemButton.Pressed += GenerateSelectedItem;
        _enableGrantCheck.Toggled += _ => UpdateControls();
        _enablePlaytimeDropCheck.Toggled += _ => UpdateControls();
        _enableMaintenanceCheck.Toggled += _ => UpdateControls();
        _promoItemOption.ItemSelected += _ =>
        {
            _enableGrantCheck.ButtonPressed = false;
            UpdatePromoItemStatus();
            UpdateControls();
        };
        _playtimeGeneratorOption.ItemSelected += _ =>
        {
            _enablePlaytimeDropCheck.ButtonPressed = false;
            UpdatePlaytimeDropStatus();
            UpdateControls();
        };
        _maintenanceItemOption.ItemSelected += _ =>
        {
            _enableMaintenanceCheck.ButtonPressed = false;
            UpdateControls();
        };
        _retryButton.Pressed += InitializeSteamworks;
        _quitButton.Pressed += () => GetTree().Quit();

        PopulatePromoItemOptions();
        PopulatePlaytimeGeneratorOptions();
        PopulateMaintenanceItemOptions();
        InitializeSteamworks();
    }

    public override void _Process(double delta)
    {
        _runtime?.RunCallbacks();
        PollDefinitionLoad();
    }

    public override void _ExitTree()
    {
        ShutdownSteamworks();
    }

    private void InitializeSteamworks()
    {
        ShutdownSteamworks();
        _definitionsLoaded = false;
        _definitionLoadPending = false;
        _inventoryLoaded = false;
        _lastInventoryItems = Array.Empty<SteamItemDetails_t>();
        _enableGrantCheck.ButtonPressed = false;
        _enablePlaytimeDropCheck.ButtonPressed = false;
        _enableMaintenanceCheck.ButtonPressed = false;
        _definitionStatusLabel.Text = "Steam ItemDef：尚未加载";
        _inventoryStatusLabel.Text = "玩家库存：尚未读取";
        UpdatePlaytimeDropStatus();
        ClearLog();

        _runtime = new SteamworksRuntime();
        if (!_runtime.TryInitialize())
        {
            _statusLabel.Text = _runtime.StatusMessage;
            _identityLabel.Text = $"本地配置 AppID：{FormatRequestedAppId(_runtime.RequestedAppId)}\n实际 AppID：-\n玩家：-\nSteamID：-";
            AppendLog($"初始化失败：{_runtime.StatusMessage}");
            UpdateControls();
            return;
        }

        _definitionUpdateCallback = Callback<SteamInventoryDefinitionUpdate_t>.Create(OnDefinitionUpdated);
        _fullUpdateCallback = Callback<SteamInventoryFullUpdate_t>.Create(OnFullInventoryUpdated);
        _resultReadyCallback = Callback<SteamInventoryResultReady_t>.Create(OnInventoryResultReady);

        _statusLabel.Text = "Steamworks 初始化成功，等待库存操作。";
        _identityLabel.Text =
            $"本地配置 AppID：{FormatRequestedAppId(_runtime.RequestedAppId)}\n" +
            $"实际 AppID：{_runtime.AppId}\n" +
            $"玩家：{_runtime.PersonaName}\n" +
            $"SteamID：{_runtime.SteamId}";
        AppendLog($"初始化成功：AppID={_runtime.AppId}, Player={_runtime.PersonaName}, SteamID={_runtime.SteamId}");
        UpdateControls();
    }

    private void ShutdownSteamworks()
    {
        foreach (var handleValue in _pendingRequests.Keys.ToArray())
            SteamInventory.DestroyResult((SteamInventoryResult_t)handleValue);
        _pendingRequests.Clear();

        _resultReadyCallback?.Dispose();
        _resultReadyCallback = null;
        _fullUpdateCallback?.Dispose();
        _fullUpdateCallback = null;
        _definitionUpdateCallback?.Dispose();
        _definitionUpdateCallback = null;
        _runtime?.Dispose();
        _runtime = null;
    }

    private void PopulatePromoItemOptions()
    {
        _promoItemOption.Clear();
        var activeBundleIds = LubanData.Tables.TbLinkTree.DataList
            .Where(entry => entry.IsEnabled && entry.SteamClaimBundleItemDefId > 0)
            .Select(entry => entry.SteamClaimBundleItemDefId)
            .ToHashSet();
        var definitions = LubanData.Tables.TbSteamItemDef.DataList
            .Where(itemDef => itemDef.IsEnabled
                              && itemDef.GrantedManually
                              && string.Equals(itemDef.PromoRule, "manual", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(itemDef => activeBundleIds.Contains(itemDef.Id))
            .ThenBy(itemDef => itemDef.Type == DataTables.ESteamItemDefType.Bundle ? 0 : 1)
            .ThenBy(itemDef => itemDef.Id);
        foreach (var itemDef in definitions)
        {
            var index = _promoItemOption.ItemCount;
            var activeMarker = activeBundleIds.Contains(itemDef.Id) ? "[LinkTree启用] " : string.Empty;
            _promoItemOption.AddItem($"{activeMarker}{itemDef.Id} - {itemDef.Name} ({itemDef.Type})");
            _promoItemOption.SetItemMetadata(index, itemDef.Id);
        }

        if (_promoItemOption.ItemCount > 0)
            _promoItemOption.Selected = 0;
        UpdatePromoItemStatus();
    }

    private void UpdatePromoItemStatus()
    {
        var definition = GetSelectedPromoDefinition();
        if (definition == null)
        {
            _promoItemStatusLabel.Text = "Promo 发放：没有可显式领取的 ItemDef";
            return;
        }

        var entries = LubanData.Tables.TbLinkTree.DataList
            .Where(entry => entry.SteamClaimBundleItemDefId == definition.Id)
            .OrderBy(entry => entry.Id)
            .ToArray();
        var entryText = entries.Length == 0
            ? "未被 LinkTree 引用"
            : string.Join("；", entries.Select(entry =>
                $"LinkTree {entry.Id}/{entry.Key}，回执={entry.SteamReceiptItemDefId}"));
        var bundleText = string.IsNullOrWhiteSpace(definition.Bundle) ? "<无>" : definition.Bundle;
        _promoItemStatusLabel.Text =
            $"Promo 发放：目标={definition.Id}，Type={definition.Type}，Bundle={bundleText}\n{entryText}";
    }

    private void PopulatePlaytimeGeneratorOptions()
    {
        _playtimeGeneratorOption.Clear();
        _playtimeGeneratorOptions.Clear();

        var schedulesByGenerator = LubanData.Tables.TbBlindBoxSchedule.DataList
            .Where(schedule => schedule.IsEnabled && schedule.SteamPlaytimeGeneratorItemDefId > 0)
            .GroupBy(schedule => schedule.SteamPlaytimeGeneratorItemDefId)
            .ToDictionary(group => group.Key, group => group.OrderBy(schedule => schedule.Id).First());
        var definitions = LubanData.Tables.TbSteamItemDef.DataList
            .Where(itemDef => itemDef.IsEnabled
                              && itemDef.Type == DataTables.ESteamItemDefType.PlaytimeGenerator)
            .Select(itemDef => new PlaytimeGeneratorTestOption(
                itemDef,
                schedulesByGenerator.GetValueOrDefault(itemDef.Id)))
            .OrderBy(option => option.Schedule is { IsLoopTrack: true } ? 0
                : option.Definition.SteamUseDropLimit && option.Definition.SteamDropLimit == 0 ? 1
                : 2)
            .ThenBy(option => option.Schedule?.Id ?? int.MaxValue)
            .ThenBy(option => option.Definition.Id);

        foreach (var option in definitions)
        {
            var schedule = option.Schedule;
            var box = schedule == null
                ? null
                : LubanData.Tables.TbBlindBox.GetOrDefault(schedule.BlindBoxId);
            var kind = schedule is { IsLoopTrack: true }
                ? "循环"
                : option.Definition.SteamUseDropLimit && option.Definition.SteamDropLimit == 0
                    ? "已退役"
                    : "一次性";
            var scheduleText = schedule == null ? "未被 Schedule 引用" : $"Schedule {schedule.Id}";
            var index = _playtimeGeneratorOption.ItemCount;
            _playtimeGeneratorOption.AddItem(
                $"[{kind}] {option.Definition.Id} - {option.Definition.Name}；" +
                $"{scheduleText}{(box == null ? string.Empty : $" / {box.Name}")}");
            _playtimeGeneratorOptions.Add(option);
            _playtimeGeneratorOption.SetItemMetadata(index, _playtimeGeneratorOptions.Count - 1);
        }

        if (_playtimeGeneratorOption.ItemCount > 0)
            _playtimeGeneratorOption.Selected = 0;
        UpdatePlaytimeDropStatus();
    }

    private void PopulateMaintenanceItemOptions()
    {
        _maintenanceItemOption.Clear();

        var costItemDefIds = LubanData.Tables.TbItem.DataList
            .Where(item => item.SteamItemDefId > 0)
            .Select(item => item.SteamItemDefId)
            .Distinct()
            .OrderBy(itemDefId => itemDefId);
        foreach (var itemDefId in costItemDefIds)
        {
            var definition = LubanData.Tables.TbSteamItemDef.GetOrDefault(itemDefId);
            var name = definition?.Name ?? "缺少本地 SteamItemDef 定义";
            var index = _maintenanceItemOption.ItemCount;
            _maintenanceItemOption.AddItem($"{itemDefId} - {name}");
            _maintenanceItemOption.SetItemMetadata(index, itemDefId);
        }

        if (_maintenanceItemOption.ItemCount > 0)
            _maintenanceItemOption.Selected = 0;
    }

    private void LoadDefinitions()
    {
        if (_runtime?.IsInitialized != true)
            return;

        _definitionsLoaded = false;
        _definitionStatusLabel.Text = "Steam ItemDef：正在请求……";
        var accepted = SteamInventory.LoadItemDefinitions();
        AppendLog($"LoadItemDefinitions：{(accepted ? "请求已接受" : "请求被拒绝")}");
        if (!accepted)
        {
            _definitionStatusLabel.Text = "Steam ItemDef：请求被拒绝";
        }
        else
        {
            var now = Time.GetTicksMsec();
            _definitionLoadPending = true;
            _definitionLoadDeadlineMsec = now + DefinitionLoadTimeoutMsec;
            _nextDefinitionPollMsec = now;
            PollDefinitionLoad();
        }
        UpdateControls();
    }

    private void RefreshInventory()
    {
        if (_runtime?.IsInitialized != true)
            return;

        _enableMaintenanceCheck.ButtonPressed = false;

        if (HasPendingRequest(InventoryRequestKind.FullInventory))
        {
            AppendLog("GetAllItems：已有读取请求正在等待回调");
            return;
        }

        var accepted = SteamInventory.GetAllItems(out var handle);
        AppendLog($"GetAllItems：{(accepted ? $"请求已接受，Handle={HandleValue(handle)}" : "请求被拒绝")}");
        if (!accepted)
        {
            _inventoryStatusLabel.Text = "玩家库存：GetAllItems 请求被拒绝";
            return;
        }

        TrackRequest(handle, InventoryRequestKind.FullInventory);
        _inventoryLoaded = false;
        _inventoryStatusLabel.Text = "玩家库存：正在读取……";
        UpdateControls();
    }

    private void AddSelectedPromoItem()
    {
        if (_runtime?.IsInitialized != true || !_enableGrantCheck.ButtonPressed || _promoItemOption.ItemCount == 0)
            return;

        if (HasPendingRequest(InventoryRequestKind.AddPromoItem))
        {
            AppendLog("AddPromoItem：已有真实发放请求正在等待回调");
            return;
        }

        var itemDefId = (int)_promoItemOption.GetItemMetadata(_promoItemOption.Selected);
        var accepted = SteamInventory.AddPromoItem(out var handle, (SteamItemDef_t)itemDefId);
        AppendLog($"AddPromoItem({itemDefId})：{(accepted ? $"请求已接受，Handle={HandleValue(handle)}" : "请求被拒绝")}");
        if (!accepted)
            return;

        TrackRequest(handle, InventoryRequestKind.AddPromoItem);
        _enableGrantCheck.ButtonPressed = false;
        _enableMaintenanceCheck.ButtonPressed = false;
        UpdateControls();
    }

    private void TriggerSelectedPlaytimeDrop()
    {
        if (_runtime?.IsInitialized != true || !_enablePlaytimeDropCheck.ButtonPressed)
            return;

        var option = GetSelectedPlaytimeGenerator();
        if (option == null)
        {
            AppendLog("TriggerItemDrop：没有可用的 PlaytimeGenerator");
            return;
        }
        if (HasPendingRequest(InventoryRequestKind.TriggerPlaytimeDrop))
        {
            AppendLog("TriggerItemDrop：已有游玩投放请求正在等待回调");
            return;
        }

        var nowMsec = Time.GetTicksMsec();
        if (nowMsec < _nextPlaytimeDropAttemptMsec)
        {
            var remainingSeconds = (int)Math.Ceiling(
                (_nextPlaytimeDropAttemptMsec - nowMsec) / 1000.0);
            AppendLog($"TriggerItemDrop：Steam 按分钟限流，请等待 {remainingSeconds} 秒后再测试");
            return;
        }

        var accepted = SteamInventory.TriggerItemDrop(
            out var handle,
            (SteamItemDef_t)option.Definition.Id);
        var scheduleText = option.Schedule == null
            ? "未被启用 Schedule 引用"
            : $"Schedule={option.Schedule.Id}";
        AppendLog(
            $"TriggerItemDrop({option.Definition.Id})：{scheduleText}；" +
            (accepted ? $"请求已接受，Handle={HandleValue(handle)}" : "请求被拒绝"));
        if (!accepted)
            return;

        _nextPlaytimeDropAttemptMsec = nowMsec + PlaytimeDropMinimumAttemptIntervalMsec;
        TrackRequest(handle, InventoryRequestKind.TriggerPlaytimeDrop);
        _enablePlaytimeDropCheck.ButtonPressed = false;
        UpdateControls();
    }

    private void ConsumeSelectedItem()
    {
        if (_runtime?.IsInitialized != true || !_enableMaintenanceCheck.ButtonPressed)
            return;

        var itemDefId = GetSelectedMaintenanceItemDefId();
        var item = _lastInventoryItems.FirstOrDefault(candidate =>
            (int)candidate.m_iDefinition == itemDefId && candidate.m_unQuantity > 0);
        if ((ulong)item.m_itemId == 0)
        {
            AppendLog($"ConsumeItem({itemDefId})：最近一次库存中没有可消耗实例");
            UpdateControls();
            return;
        }

        var accepted = SteamInventory.ConsumeItem(out var handle, item.m_itemId, 1);
        AppendLog(
            $"ConsumeItem({itemDefId})：Instance={(ulong)item.m_itemId}, Qty=1, " +
            (accepted ? $"请求已接受，Handle={HandleValue(handle)}" : "请求被拒绝"));
        if (!accepted)
            return;

        TrackRequest(handle, InventoryRequestKind.ConsumeItem);
        _enableGrantCheck.ButtonPressed = false;
        _enableMaintenanceCheck.ButtonPressed = false;
        UpdateControls();
    }

    private void GenerateSelectedItem()
    {
        if (_runtime?.IsInitialized != true || !_enableMaintenanceCheck.ButtonPressed)
            return;

        var itemDefId = GetSelectedMaintenanceItemDefId();
        SteamItemDef_t[] itemDefs = [(SteamItemDef_t)itemDefId];
        uint[] quantities = [1];
        var accepted = SteamInventory.GenerateItems(out var handle, itemDefs, quantities, 1);
        AppendLog($"GenerateItems({itemDefId})：{(accepted ? $"请求已接受，Handle={HandleValue(handle)}" : "请求被拒绝")}");
        if (!accepted)
            return;

        TrackRequest(handle, InventoryRequestKind.GenerateItem);
        _enableGrantCheck.ButtonPressed = false;
        _enableMaintenanceCheck.ButtonPressed = false;
        UpdateControls();
    }

    private void OnDefinitionUpdated(SteamInventoryDefinitionUpdate_t callback)
    {
        AppendLog("SteamInventoryDefinitionUpdate_t：收到定义更新回调");
        if (TryApplyLoadedDefinitions("回调"))
            return;

        _definitionsLoaded = false;
        _definitionStatusLabel.Text = "Steam ItemDef：收到回调，但定义尚不可读取";
        UpdateControls();
    }

    private void PollDefinitionLoad()
    {
        if (!_definitionLoadPending || _runtime?.IsInitialized != true)
            return;

        var now = Time.GetTicksMsec();
        if (now < _nextDefinitionPollMsec)
            return;

        _nextDefinitionPollMsec = now + DefinitionPollIntervalMsec;
        if (TryApplyLoadedDefinitions("主动读取"))
            return;

        if (now < _definitionLoadDeadlineMsec)
            return;

        _definitionLoadPending = false;
        _definitionsLoaded = false;
        _definitionStatusLabel.Text = "Steam ItemDef：加载超时，请重试";
        AppendLog("LoadItemDefinitions：10 秒内未能读取定义，已停止等待");
        UpdateControls();
    }

    private bool TryApplyLoadedDefinitions(string source)
    {
        var count = 0u;
        if (!SteamInventory.GetItemDefinitionIDs(null!, ref count))
            return false;

        var serverItemDefs = new SteamItemDef_t[count];
        if (count > 0 && !SteamInventory.GetItemDefinitionIDs(serverItemDefs, ref count))
            return false;

        _definitionLoadPending = false;
        var serverIds = serverItemDefs.Select(itemDef => (int)itemDef).ToHashSet();
        // Steamworks Admin publishes playtime generators, but the client definition list does
        // not enumerate them. TriggerItemDrop is the authoritative runtime check for that type.
        var localItemDefIds = LubanData.Tables.TbSteamItemDef.DataList
            .Where(itemDef => itemDef.IsEnabled &&
                              itemDef.Type != DataTables.ESteamItemDefType.PlaytimeGenerator)
            .Select(itemDef => itemDef.Id)
            .Concat(LubanData.Tables.TbItem.DataList
                .Where(item => item.SteamItemDefId > 0)
                .Select(item => item.SteamItemDefId))
            .Distinct()
            .OrderBy(id => id)
            .ToArray();
        var playtimeGeneratorCount = LubanData.Tables.TbSteamItemDef.DataList.Count(itemDef =>
            itemDef.IsEnabled && itemDef.Type == DataTables.ESteamItemDefType.PlaytimeGenerator);
        var missingIds = localItemDefIds.Where(id => !serverIds.Contains(id)).ToArray();
        _definitionsLoaded = missingIds.Length == 0;
        _definitionStatusLabel.Text = missingIds.Length == 0
            ? $"Steam ItemDef：服务器 {count} 条；可枚举定义 {localItemDefIds.Length} 条全部匹配；" +
              $"PlaytimeGenerator {playtimeGeneratorCount} 条请用触发请求验证"
            : $"Steam ItemDef：服务器 {count} 条；服务器未返回本地配置中的定义 {string.Join(", ", missingIds)}";
        AppendLog($"ItemDef {source}成功：服务器返回 {count} 条定义");
        UpdateControls();
        return true;
    }

    private void OnFullInventoryUpdated(SteamInventoryFullUpdate_t callback)
    {
        var handleValue = HandleValue(callback.m_handle);
        if (!_pendingRequests.ContainsKey(handleValue))
            TrackRequest(callback.m_handle, InventoryRequestKind.FullInventory);
        AppendLog($"SteamInventoryFullUpdate_t：Handle={handleValue}");
    }

    private void OnInventoryResultReady(SteamInventoryResultReady_t callback)
    {
        var handle = callback.m_handle;
        var handleValue = HandleValue(handle);
        var knownRequest = _pendingRequests.Remove(handleValue, out var requestKind);

        try
        {
            if (!knownRequest)
            {
                AppendLog($"SteamInventoryResultReady_t：收到未跟踪 Handle={handleValue}, Result={callback.m_result}");
                return;
            }

            if (_runtime == null || !SteamInventory.CheckResultSteamID(handle, new CSteamID(_runtime.SteamId)))
            {
                AppendLog($"{requestKind}：Handle={handleValue} 的 SteamID 校验失败");
                return;
            }

            if (callback.m_result != EResult.k_EResultOK)
            {
                AppendLog($"{requestKind}：Steam 返回 {callback.m_result}");
                if (requestKind == InventoryRequestKind.FullInventory)
                    _inventoryStatusLabel.Text = $"玩家库存：读取失败（{callback.m_result}）";
                return;
            }

            var items = ReadResultItems(handle);
            if (items == null)
            {
                AppendLog($"{requestKind}：GetResultItems 失败");
                return;
            }

            if (requestKind == InventoryRequestKind.FullInventory)
                ShowInventory(items);
            else if (requestKind == InventoryRequestKind.AddPromoItem)
                ShowPromoGrantResult(items);
            else if (requestKind == InventoryRequestKind.TriggerPlaytimeDrop)
                ShowPlaytimeDropResult(items);
            else
                ShowInventoryMutationResult(requestKind, items);
        }
        finally
        {
            SteamInventory.DestroyResult(handle);
            AppendLog($"DestroyResult：Handle={handleValue}");
            UpdateControls();
        }
    }

    private SteamItemDetails_t[] ReadResultItems(SteamInventoryResult_t handle)
    {
        var count = 0u;
        if (!SteamInventory.GetResultItems(handle, null!, ref count))
            return null;
        if (count == 0)
            return Array.Empty<SteamItemDetails_t>();

        var items = new SteamItemDetails_t[count];
        return SteamInventory.GetResultItems(handle, items, ref count) ? items : null;
    }

    private void ShowInventory(IReadOnlyCollection<SteamItemDetails_t> items)
    {
        _inventoryLoaded = true;
        _lastInventoryItems = items.ToArray();
        var builder = new StringBuilder();
        builder.AppendLine($"玩家库存：{items.Count} 个实例/堆叠");
        foreach (var item in items
                     .OrderBy(item => (int)item.m_iDefinition)
                     .ThenBy(item => (ulong)item.m_itemId))
        {
            builder.AppendLine(
                $"ItemDef={(int)item.m_iDefinition}, Instance={(ulong)item.m_itemId}, Qty={item.m_unQuantity}, Flags={item.m_unFlags}");
        }

        _inventoryStatusLabel.Text = builder.ToString().TrimEnd();
        AppendLog($"GetAllItems 完成：{items.Count} 个实例/堆叠");
    }

    private void ShowPromoGrantResult(IReadOnlyCollection<SteamItemDetails_t> items)
    {
        if (items.Count == 0)
        {
            AppendLog("AddPromoItem 完成，但结果中没有新增物品；通常表示该回执已领取或当前不符合资格");
            RefreshInventory();
            return;
        }

        foreach (var item in items)
        {
            var itemDefId = (int)item.m_iDefinition;
            AppendLog(
                $"AddPromoItem 返回：ItemDef={itemDefId}, Instance={(ulong)item.m_itemId}, " +
                $"Qty={item.m_unQuantity}, Flags={item.m_unFlags}；{DescribePromoResultItem(itemDefId)}");
        }
        RefreshInventory();
    }

    private static string DescribePromoResultItem(int itemDefId)
    {
        var receiptEntry = LubanData.Tables.TbLinkTree.DataList.FirstOrDefault(entry =>
            entry.SteamReceiptItemDefId == itemDefId);
        if (receiptEntry != null)
            return $"LinkTree 永久回执（{receiptEntry.Id}/{receiptEntry.Key}）";

        var localItem = LubanData.Tables.TbItem.DataList.FirstOrDefault(item =>
            item.SteamItemDefId == itemDefId);
        if (localItem != null)
            return $"本地 Item={localItem.Id} {localItem.Name}";

        return "未映射的 Steam 库存物品";
    }

    private void ShowPlaytimeDropResult(IReadOnlyCollection<SteamItemDetails_t> items)
    {
        if (items.Count == 0)
        {
            AppendLog(
                "TriggerItemDrop 完成，但结果中没有物品；通常表示游玩时间尚未满足、仍在冷却，或已达到 drop_limit");
            RefreshInventory();
            return;
        }

        foreach (var item in items)
        {
            var localItem = LubanData.Tables.TbItem.DataList.FirstOrDefault(candidate =>
                candidate.SteamItemDefId == (int)item.m_iDefinition);
            var localText = localItem == null
                ? "未映射到本地 Item"
                : $"最终装扮 Item={localItem.Id} {localItem.Name}, Rarity={localItem.ItemRarity}";
            AppendLog(
                $"TriggerItemDrop 发放：ItemDef={(int)item.m_iDefinition}, Instance={(ulong)item.m_itemId}, " +
                $"Qty={item.m_unQuantity}, Flags={item.m_unFlags}；{localText}");
        }
        RefreshInventory();
    }

    private void ShowInventoryMutationResult(
        InventoryRequestKind requestKind,
        IReadOnlyCollection<SteamItemDetails_t> items)
    {
        AppendLog($"{requestKind} 成功：Steam 返回 {items.Count} 条变更");
        if (requestKind == InventoryRequestKind.ConsumeItem)
        {
            AppendLog(
                "注意：ConsumeItem 只删除库存实例，不会重置 Steam 的一次性 Promo 发放记录；" +
                "同一 ItemDef 再次调用 AddPromoItem 可能返回成功但不产生物品。");
        }
        foreach (var item in items)
        {
            AppendLog(
                $"{requestKind}：ItemDef={(int)item.m_iDefinition}, Instance={(ulong)item.m_itemId}, " +
                $"Qty={item.m_unQuantity}, Flags={item.m_unFlags}");
        }
        RefreshInventory();
    }

    private void UpdateControls()
    {
        var available = _runtime?.IsInitialized == true;
        var hasPromoOptions = _promoItemOption.ItemCount > 0;
        var hasPlaytimeOptions = _playtimeGeneratorOption.ItemCount > 0;
        var hasMaintenanceOptions = _maintenanceItemOption.ItemCount > 0;
        var grantPending = HasPendingRequest(InventoryRequestKind.AddPromoItem);
        var playtimeDropPending = HasPendingRequest(InventoryRequestKind.TriggerPlaytimeDrop);
        var anyRequestPending = _pendingRequests.Count > 0;
        var selectedItemDefId = GetSelectedMaintenanceItemDefId();
        var selectedItemOwned = selectedItemDefId > 0 && _lastInventoryItems.Any(item =>
            (int)item.m_iDefinition == selectedItemDefId && item.m_unQuantity > 0);

        _loadDefinitionsButton.Disabled = !available;
        _refreshInventoryButton.Disabled = !available || HasPendingRequest(InventoryRequestKind.FullInventory);
        _promoItemOption.Disabled = !available || !hasPromoOptions || grantPending;
        _playtimeGeneratorOption.Disabled = !available || !hasPlaytimeOptions || playtimeDropPending;
        _maintenanceItemOption.Disabled = !available || !hasMaintenanceOptions || anyRequestPending;
        _enableGrantCheck.Disabled = !available || !_definitionsLoaded || !hasPromoOptions || grantPending;
        _enablePlaytimeDropCheck.Disabled = !available
            || !_definitionsLoaded
            || !hasPlaytimeOptions
            || playtimeDropPending;
        _enableMaintenanceCheck.Disabled = !available
            || !_definitionsLoaded
            || !_inventoryLoaded
            || !hasMaintenanceOptions
            || anyRequestPending;
        _addPromoItemButton.Disabled = !available
            || !_definitionsLoaded
            || !hasPromoOptions
            || !_enableGrantCheck.ButtonPressed
            || grantPending;
        _triggerPlaytimeDropButton.Disabled = !available
            || !_definitionsLoaded
            || !hasPlaytimeOptions
            || !_enablePlaytimeDropCheck.ButtonPressed
            || anyRequestPending;
        _consumeItemButton.Disabled = !_enableMaintenanceCheck.ButtonPressed
            || anyRequestPending
            || !selectedItemOwned;
        _generateItemButton.Disabled = !_enableMaintenanceCheck.ButtonPressed
            || anyRequestPending;
        UpdatePlaytimeDropStatus();
        UpdatePromoItemStatus();
    }

    private PlaytimeGeneratorTestOption GetSelectedPlaytimeGenerator()
    {
        if (_playtimeGeneratorOption.ItemCount == 0 || _playtimeGeneratorOption.Selected < 0)
            return null;
        var optionIndex = (int)_playtimeGeneratorOption.GetItemMetadata(_playtimeGeneratorOption.Selected);
        return optionIndex >= 0 && optionIndex < _playtimeGeneratorOptions.Count
            ? _playtimeGeneratorOptions[optionIndex]
            : null;
    }

    private void UpdatePlaytimeDropStatus()
    {
        var option = GetSelectedPlaytimeGenerator();
        if (option == null)
        {
            _playtimeDropStatusLabel.Text = "游玩投放：没有已配置的 PlaytimeGenerator";
            return;
        }

        var schedule = option.Schedule;
        var retired = option.Definition.SteamUseDropLimit && option.Definition.SteamDropLimit == 0;
        var scheduleText = schedule == null
            ? "未被启用 Schedule 引用"
            : $"Schedule {schedule.Id}{(schedule.IsLoopTrack ? "（循环）" : "（一次性）")}";
        _playtimeDropStatusLabel.Text =
            $"游玩投放：Generator {option.Definition.Id} → Bundle {option.Definition.Bundle}；" +
            $"{scheduleText}{(retired ? "；drop_limit=0，预期不发放" : string.Empty)}";
    }

    private int GetSelectedPromoItemDefId()
    {
        return _promoItemOption.ItemCount > 0 && _promoItemOption.Selected >= 0
            ? (int)_promoItemOption.GetItemMetadata(_promoItemOption.Selected)
            : 0;
    }

    private DataTables.SteamItemDef GetSelectedPromoDefinition()
    {
        var itemDefId = GetSelectedPromoItemDefId();
        return itemDefId > 0 ? LubanData.Tables.TbSteamItemDef.GetOrDefault(itemDefId) : null;
    }

    private int GetSelectedMaintenanceItemDefId()
    {
        return _maintenanceItemOption.ItemCount > 0 && _maintenanceItemOption.Selected >= 0
            ? (int)_maintenanceItemOption.GetItemMetadata(_maintenanceItemOption.Selected)
            : 0;
    }

    private void TrackRequest(SteamInventoryResult_t handle, InventoryRequestKind requestKind)
    {
        _pendingRequests[HandleValue(handle)] = requestKind;
    }

    private bool HasPendingRequest(InventoryRequestKind requestKind)
    {
        return _pendingRequests.Values.Contains(requestKind);
    }

    private static int HandleValue(SteamInventoryResult_t handle) => (int)handle;

    private static string FormatRequestedAppId(uint appId) => appId == 0 ? "未找到" : appId.ToString();

    private void AppendLog(string message)
    {
        _logLines.Enqueue($"[{DateTime.Now:HH:mm:ss}] {message}");
        while (_logLines.Count > 18)
            _logLines.Dequeue();
        _operationLog.Text = string.Join("\n", _logLines);
        _operationLog.SetCaretLine(_operationLog.GetLineCount() - 1);
    }

    private void ClearLog()
    {
        _logLines.Clear();
        _operationLog.Text = string.Empty;
    }
}
