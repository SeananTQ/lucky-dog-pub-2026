using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Godot;
using Steamworks;

namespace LuckyDogRise;

public partial class SteamInventoryTestController : Control
{
    private const int TestBlindBoxId = 4001;
    private const ulong DefinitionLoadTimeoutMsec = 10000;
    private const ulong DefinitionPollIntervalMsec = 250;

    private enum InventoryRequestKind
    {
        FullInventory,
        AddPromoItem,
        TriggerPlaytimeDrop,
        ConsumeItem,
        GenerateItem,
        ExchangeBlindBox,
    }

    [Export] private Label _statusLabel = null!;
    [Export] private Label _identityLabel = null!;
    [Export] private Label _definitionStatusLabel = null!;
    [Export] private Label _inventoryStatusLabel = null!;
    [Export] private OptionButton _promoItemOption = null!;
    [Export] private OptionButton _playtimeGeneratorOption = null!;
    [Export] private CheckButton _enableGrantCheck = null!;
    [Export] private CheckButton _enablePlaytimeDropCheck = null!;
    [Export] private CheckButton _enableMaintenanceCheck = null!;
    [Export] private CheckButton _enableExchangeCheck = null!;
    [Export] private Button _loadDefinitionsButton = null!;
    [Export] private Button _refreshInventoryButton = null!;
    [Export] private Button _addPromoItemButton = null!;
    [Export] private Button _triggerPlaytimeDropButton = null!;
    [Export] private Button _consumeItemButton = null!;
    [Export] private Button _generateItemButton = null!;
    [Export] private Button _exchangeBlindBoxButton = null!;
    [Export] private Label _exchangeStatusLabel = null!;
    [Export] private Label _playtimeDropStatusLabel = null!;
    [Export] private Button _retryButton = null!;
    [Export] private Button _quitButton = null!;
    [Export] private TextEdit _operationLog = null!;

    private readonly Dictionary<int, InventoryRequestKind> _pendingRequests = new();
    private readonly Queue<string> _logLines = new();
    private SteamItemDetails_t[] _lastInventoryItems = Array.Empty<SteamItemDetails_t>();
    private SteamworksRuntime _runtime = null!;
    private Callback<SteamInventoryDefinitionUpdate_t> _definitionUpdateCallback = null!;
    private Callback<SteamInventoryFullUpdate_t> _fullUpdateCallback = null!;
    private Callback<SteamInventoryResultReady_t> _resultReadyCallback = null!;
    private bool _definitionsLoaded;
    private bool _inventoryLoaded;
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
        _exchangeBlindBoxButton.Pressed += ExchangeTestBlindBox;
        _enableGrantCheck.Toggled += _ => UpdateControls();
        _enablePlaytimeDropCheck.Toggled += _ => UpdateControls();
        _enableMaintenanceCheck.Toggled += _ => UpdateControls();
        _enableExchangeCheck.Toggled += _ => UpdateControls();
        _promoItemOption.ItemSelected += _ =>
        {
            _enableGrantCheck.ButtonPressed = false;
            _enableMaintenanceCheck.ButtonPressed = false;
            UpdateControls();
        };
        _playtimeGeneratorOption.ItemSelected += _ =>
        {
            _enablePlaytimeDropCheck.ButtonPressed = false;
            UpdatePlaytimeDropStatus();
            UpdateControls();
        };
        _retryButton.Pressed += InitializeSteamworks;
        _quitButton.Pressed += () => GetTree().Quit();

        PopulatePromoItemOptions();
        PopulatePlaytimeGeneratorOptions();
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
        _enableExchangeCheck.ButtonPressed = false;
        _definitionStatusLabel.Text = "Steam ItemDef：尚未加载";
        _inventoryStatusLabel.Text = "玩家库存：尚未读取";
        _exchangeStatusLabel.Text = "盲盒兑换：等待读取玩家库存";
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
        foreach (var itemDef in LubanData.Tables.TbSteamItemDef.DataList.Where(itemDef => itemDef.IsEnabled))
        {
            var index = _promoItemOption.ItemCount;
            _promoItemOption.AddItem($"{itemDef.Id} - {itemDef.Name}");
            _promoItemOption.SetItemMetadata(index, itemDef.Id);
        }

        if (_promoItemOption.ItemCount > 0)
            _promoItemOption.Selected = 0;
    }

    private void PopulatePlaytimeGeneratorOptions()
    {
        _playtimeGeneratorOption.Clear();
        foreach (var schedule in LubanData.Tables.TbBlindBoxSchedule.DataList
                     .Where(schedule => schedule.IsEnabled && schedule.SteamPlaytimeGeneratorItemDefId > 0)
                     .OrderBy(schedule => schedule.Id))
        {
            var box = LubanData.Tables.TbBlindBox.GetOrDefault(schedule.BlindBoxId);
            var index = _playtimeGeneratorOption.ItemCount;
            _playtimeGeneratorOption.AddItem(
                $"Schedule {schedule.Id} / {schedule.SteamPlaytimeGeneratorItemDefId} - {box?.Name ?? "缺失盲盒"}");
            _playtimeGeneratorOption.SetItemMetadata(index, schedule.Id);
        }

        if (_playtimeGeneratorOption.ItemCount > 0)
            _playtimeGeneratorOption.Selected = 0;
        UpdatePlaytimeDropStatus();
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

        var schedule = GetSelectedPlaytimeSchedule();
        if (schedule == null)
        {
            AppendLog("TriggerItemDrop：没有可用的 BlindBoxSchedule");
            return;
        }
        if (HasPendingRequest(InventoryRequestKind.TriggerPlaytimeDrop))
        {
            AppendLog("TriggerItemDrop：已有游玩投放请求正在等待回调");
            return;
        }

        var accepted = SteamInventory.TriggerItemDrop(
            out var handle,
            (SteamItemDef_t)schedule.SteamPlaytimeGeneratorItemDefId);
        AppendLog(
            $"TriggerItemDrop({schedule.SteamPlaytimeGeneratorItemDefId})：Schedule={schedule.Id}；" +
            (accepted ? $"请求已接受，Handle={HandleValue(handle)}" : "请求被拒绝"));
        if (!accepted)
            return;

        TrackRequest(handle, InventoryRequestKind.TriggerPlaytimeDrop);
        _enablePlaytimeDropCheck.ButtonPressed = false;
        UpdateControls();
    }

    private void ConsumeSelectedItem()
    {
        if (_runtime?.IsInitialized != true || !_enableMaintenanceCheck.ButtonPressed)
            return;

        var itemDefId = GetSelectedItemDefId();
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

        var itemDefId = GetSelectedItemDefId();
        if (_lastInventoryItems.Any(item => (int)item.m_iDefinition == itemDefId && item.m_unQuantity > 0))
        {
            AppendLog($"GenerateItems({itemDefId})：库存中已存在该 ItemDef，拒绝生成重复凭证");
            UpdateControls();
            return;
        }

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

    private void ExchangeTestBlindBox()
    {
        if (_runtime?.IsInitialized != true || !_enableExchangeCheck.ButtonPressed)
            return;

        var blindBox = LubanData.Tables.TbBlindBox.GetOrDefault(TestBlindBoxId);
        if (blindBox == null || blindBox.SteamOpenCostItemDefId <= 0 || blindBox.SteamExchangeTargetItemDefId <= 0)
        {
            AppendLog($"ExchangeItems：BlindBox {TestBlindBoxId} 的 Steam 映射无效");
            UpdateControls();
            return;
        }

        if (HasPendingRequest(InventoryRequestKind.ExchangeBlindBox))
        {
            AppendLog("ExchangeItems：已有盲盒兑换请求正在等待回调");
            return;
        }

        var input = _lastInventoryItems.FirstOrDefault(item =>
            (int)item.m_iDefinition == blindBox.SteamOpenCostItemDefId && item.m_unQuantity > 0);
        if ((ulong)input.m_itemId == 0)
        {
            AppendLog($"ExchangeItems：库存中没有可消耗的 ItemDef {blindBox.SteamOpenCostItemDefId}");
            UpdateControls();
            return;
        }

        SteamItemDef_t[] outputItemDefs = [(SteamItemDef_t)blindBox.SteamExchangeTargetItemDefId];
        uint[] outputQuantities = [1];
        SteamItemInstanceID_t[] inputItemIds = [input.m_itemId];
        uint[] inputQuantities = [1];
        var accepted = SteamInventory.ExchangeItems(
            out var handle,
            outputItemDefs,
            outputQuantities,
            (uint)outputItemDefs.Length,
            inputItemIds,
            inputQuantities,
            (uint)inputItemIds.Length);
        AppendLog(
            $"ExchangeItems：消耗 ItemDef={blindBox.SteamOpenCostItemDefId}, Instance={(ulong)input.m_itemId}, Qty=1；" +
            $"目标 ItemDef={blindBox.SteamExchangeTargetItemDefId}；" +
            (accepted ? $"请求已接受，Handle={HandleValue(handle)}" : "请求被拒绝"));
        if (!accepted)
            return;

        TrackRequest(handle, InventoryRequestKind.ExchangeBlindBox);
        _enableGrantCheck.ButtonPressed = false;
        _enableMaintenanceCheck.ButtonPressed = false;
        _enableExchangeCheck.ButtonPressed = false;
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
                else if (requestKind == InventoryRequestKind.ExchangeBlindBox)
                    _exchangeStatusLabel.Text = $"盲盒兑换：失败（{callback.m_result}）";
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
            else if (requestKind == InventoryRequestKind.ExchangeBlindBox)
                ShowBlindBoxExchangeResult(items);
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
        foreach (var item in items.OrderBy(item => (int)item.m_iDefinition).Take(20))
        {
            builder.AppendLine(
                $"ItemDef={(int)item.m_iDefinition}, Instance={(ulong)item.m_itemId}, Qty={item.m_unQuantity}, Flags={item.m_unFlags}");
        }
        if (items.Count > 20)
            builder.AppendLine($"……另有 {items.Count - 20} 个实例/堆叠未展开显示");

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
            AppendLog(
                $"AddPromoItem 成功：ItemDef={(int)item.m_iDefinition}, Instance={(ulong)item.m_itemId}, Qty={item.m_unQuantity}, Flags={item.m_unFlags}");
        }
        RefreshInventory();
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
            AppendLog(
                $"TriggerItemDrop 发放：ItemDef={(int)item.m_iDefinition}, Instance={(ulong)item.m_itemId}, " +
                $"Qty={item.m_unQuantity}, Flags={item.m_unFlags}");
        }
        RefreshInventory();
    }

    private void ShowBlindBoxExchangeResult(IReadOnlyCollection<SteamItemDetails_t> items)
    {
        AppendLog($"ExchangeItems 成功：Steam 返回 {items.Count} 条库存变更");
        if (items.Count == 0)
            AppendLog("ExchangeItems：结果为空，将通过完整库存复查服务器状态");

        foreach (var item in items)
        {
            if (item.m_unQuantity == 0)
            {
                AppendLog(
                    $"ExchangeItems 消耗确认：ItemDef={(int)item.m_iDefinition}, " +
                    $"Instance={(ulong)item.m_itemId}, Qty=0, Flags={item.m_unFlags}");
                continue;
            }

            var localItem = LubanData.Tables.TbItem.DataList.FirstOrDefault(candidate =>
                candidate.SteamItemDefId == (int)item.m_iDefinition);
            var localDescription = localItem == null
                ? "未映射到本地 Item"
                : $"本地 Item={localItem.Id} {localItem.Name}, Rarity={localItem.ItemRarity}";
            AppendLog(
                $"ExchangeItems 奖励：ItemDef={(int)item.m_iDefinition}, Instance={(ulong)item.m_itemId}, " +
                $"Qty={item.m_unQuantity}, Flags={item.m_unFlags}；{localDescription}");
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
        var grantPending = HasPendingRequest(InventoryRequestKind.AddPromoItem);
        var playtimeDropPending = HasPendingRequest(InventoryRequestKind.TriggerPlaytimeDrop);
        var anyRequestPending = _pendingRequests.Count > 0;
        var selectedItemDefId = GetSelectedItemDefId();
        var selectedItemOwned = selectedItemDefId > 0 && _lastInventoryItems.Any(item =>
            (int)item.m_iDefinition == selectedItemDefId && item.m_unQuantity > 0);
        var blindBox = LubanData.Tables.TbBlindBox.GetOrDefault(TestBlindBoxId);
        var exchangeMappingValid = blindBox != null
            && blindBox.SteamOpenCostItemDefId > 0
            && blindBox.SteamExchangeTargetItemDefId > 0;
        var voucherQuantity = exchangeMappingValid
            ? _lastInventoryItems
                .Where(item => (int)item.m_iDefinition == blindBox!.SteamOpenCostItemDefId)
                .Sum(item => (int)item.m_unQuantity)
            : 0;

        _loadDefinitionsButton.Disabled = !available;
        _refreshInventoryButton.Disabled = !available || HasPendingRequest(InventoryRequestKind.FullInventory);
        _promoItemOption.Disabled = !available || !hasPromoOptions || grantPending;
        _playtimeGeneratorOption.Disabled = !available || !hasPlaytimeOptions || playtimeDropPending;
        _enableGrantCheck.Disabled = !available || !_definitionsLoaded || !hasPromoOptions || grantPending;
        _enablePlaytimeDropCheck.Disabled = !available
            || !_definitionsLoaded
            || !hasPlaytimeOptions
            || playtimeDropPending;
        _enableMaintenanceCheck.Disabled = !available
            || !_definitionsLoaded
            || !_inventoryLoaded
            || !hasPromoOptions
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
            || anyRequestPending
            || selectedItemOwned;
        _enableExchangeCheck.Disabled = !available
            || !_definitionsLoaded
            || !_inventoryLoaded
            || !exchangeMappingValid
            || voucherQuantity <= 0
            || anyRequestPending;
        _exchangeBlindBoxButton.Disabled = !_enableExchangeCheck.ButtonPressed
            || anyRequestPending
            || voucherQuantity <= 0;

        _exchangeStatusLabel.Text = !exchangeMappingValid
            ? $"盲盒兑换：BlindBox {TestBlindBoxId} 的 Steam 映射无效"
            : !_inventoryLoaded
                ? $"盲盒兑换：等待读取库存（{blindBox!.SteamOpenCostItemDefId} → {blindBox.SteamExchangeTargetItemDefId}）"
                : $"盲盒兑换：持有 ItemDef {blindBox!.SteamOpenCostItemDefId} ×{voucherQuantity}；" +
                  $"交换目标 ItemDef {blindBox.SteamExchangeTargetItemDefId}";
        UpdatePlaytimeDropStatus();
    }

    private DataTables.BlindBoxSchedule GetSelectedPlaytimeSchedule()
    {
        if (_playtimeGeneratorOption.ItemCount == 0 || _playtimeGeneratorOption.Selected < 0)
            return null;
        var scheduleId = (int)_playtimeGeneratorOption.GetItemMetadata(_playtimeGeneratorOption.Selected);
        return LubanData.Tables.TbBlindBoxSchedule.GetOrDefault(scheduleId);
    }

    private void UpdatePlaytimeDropStatus()
    {
        var schedule = GetSelectedPlaytimeSchedule();
        var box = schedule == null ? null : LubanData.Tables.TbBlindBox.GetOrDefault(schedule.BlindBoxId);
        _playtimeDropStatusLabel.Text = schedule == null
            ? "游玩投放：没有已配置的 PlaytimeGenerator"
            : $"游玩投放：Generator {schedule.SteamPlaytimeGeneratorItemDefId} → " +
              $"ItemDef {box?.SteamOpenCostItemDefId ?? 0}；Schedule {schedule.Id}";
    }

    private int GetSelectedItemDefId()
    {
        return _promoItemOption.ItemCount > 0 && _promoItemOption.Selected >= 0
            ? (int)_promoItemOption.GetItemMetadata(_promoItemOption.Selected)
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
