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

    private enum InventoryRequestKind
    {
        FullInventory,
        AddPromoItem,
        ConsumeItem,
        GenerateItem,
    }

    [Export] private Label _statusLabel = null!;
    [Export] private Label _identityLabel = null!;
    [Export] private Label _definitionStatusLabel = null!;
    [Export] private Label _inventoryStatusLabel = null!;
    [Export] private OptionButton _promoItemOption = null!;
    [Export] private CheckButton _enableGrantCheck = null!;
    [Export] private CheckButton _enableMaintenanceCheck = null!;
    [Export] private Button _loadDefinitionsButton = null!;
    [Export] private Button _refreshInventoryButton = null!;
    [Export] private Button _addPromoItemButton = null!;
    [Export] private Button _consumeItemButton = null!;
    [Export] private Button _generateItemButton = null!;
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
        _consumeItemButton.Pressed += ConsumeSelectedItem;
        _generateItemButton.Pressed += GenerateSelectedItem;
        _enableGrantCheck.Toggled += _ => UpdateControls();
        _enableMaintenanceCheck.Toggled += _ => UpdateControls();
        _promoItemOption.ItemSelected += _ =>
        {
            _enableGrantCheck.ButtonPressed = false;
            _enableMaintenanceCheck.ButtonPressed = false;
            UpdateControls();
        };
        _retryButton.Pressed += InitializeSteamworks;
        _quitButton.Pressed += () => GetTree().Quit();

        PopulatePromoItemOptions();
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
        _enableMaintenanceCheck.ButtonPressed = false;
        _definitionStatusLabel.Text = "Steam ItemDef：尚未加载";
        _inventoryStatusLabel.Text = "玩家库存：尚未读取";
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
        var localDefinitions = LubanData.Tables.TbSteamItemDef.DataList.Where(itemDef => itemDef.IsEnabled).ToArray();
        var missingIds = localDefinitions.Where(itemDef => !serverIds.Contains(itemDef.Id)).Select(itemDef => itemDef.Id).ToArray();
        _definitionsLoaded = missingIds.Length == 0;
        _definitionStatusLabel.Text = missingIds.Length == 0
            ? $"Steam ItemDef：服务器 {count} 条，本地启用 {localDefinitions.Length} 条，全部匹配"
            : $"Steam ItemDef：服务器 {count} 条；缺少本地定义 {string.Join(", ", missingIds)}";
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
        var grantPending = HasPendingRequest(InventoryRequestKind.AddPromoItem);
        var anyRequestPending = _pendingRequests.Count > 0;
        var selectedItemDefId = GetSelectedItemDefId();
        var selectedItemOwned = selectedItemDefId > 0 && _lastInventoryItems.Any(item =>
            (int)item.m_iDefinition == selectedItemDefId && item.m_unQuantity > 0);

        _loadDefinitionsButton.Disabled = !available;
        _refreshInventoryButton.Disabled = !available || HasPendingRequest(InventoryRequestKind.FullInventory);
        _promoItemOption.Disabled = !available || !hasPromoOptions || grantPending;
        _enableGrantCheck.Disabled = !available || !_definitionsLoaded || !hasPromoOptions || grantPending;
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
        _consumeItemButton.Disabled = !_enableMaintenanceCheck.ButtonPressed
            || anyRequestPending
            || !selectedItemOwned;
        _generateItemButton.Disabled = !_enableMaintenanceCheck.ButtonPressed
            || anyRequestPending
            || selectedItemOwned;
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
