using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Godot;
using Steamworks;

namespace LuckyDogRise;

public partial class SteamInventoryTestController : Control
{
    private enum InventoryRequestKind
    {
        FullInventory,
        AddPromoItem,
    }

    [Export] private Label _statusLabel = null!;
    [Export] private Label _identityLabel = null!;
    [Export] private Label _definitionStatusLabel = null!;
    [Export] private Label _inventoryStatusLabel = null!;
    [Export] private OptionButton _promoItemOption = null!;
    [Export] private CheckButton _enableGrantCheck = null!;
    [Export] private Button _loadDefinitionsButton = null!;
    [Export] private Button _refreshInventoryButton = null!;
    [Export] private Button _addPromoItemButton = null!;
    [Export] private Button _retryButton = null!;
    [Export] private Button _quitButton = null!;
    [Export] private TextEdit _operationLog = null!;

    private readonly Dictionary<int, InventoryRequestKind> _pendingRequests = new();
    private readonly Queue<string> _logLines = new();
    private SteamworksRuntime _runtime = null!;
    private Callback<SteamInventoryDefinitionUpdate_t> _definitionUpdateCallback = null!;
    private Callback<SteamInventoryFullUpdate_t> _fullUpdateCallback = null!;
    private Callback<SteamInventoryResultReady_t> _resultReadyCallback = null!;
    private bool _definitionsLoaded;

    public override void _Ready()
    {
        _loadDefinitionsButton.Pressed += LoadDefinitions;
        _refreshInventoryButton.Pressed += RefreshInventory;
        _addPromoItemButton.Pressed += AddSelectedPromoItem;
        _enableGrantCheck.Toggled += _ => UpdateControls();
        _retryButton.Pressed += InitializeSteamworks;
        _quitButton.Pressed += () => GetTree().Quit();

        PopulatePromoItemOptions();
        InitializeSteamworks();
    }

    public override void _Process(double delta)
    {
        _runtime?.RunCallbacks();
    }

    public override void _ExitTree()
    {
        ShutdownSteamworks();
    }

    private void InitializeSteamworks()
    {
        ShutdownSteamworks();
        _definitionsLoaded = false;
        _enableGrantCheck.ButtonPressed = false;
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
            _definitionStatusLabel.Text = "Steam ItemDef：请求被拒绝";
        UpdateControls();
    }

    private void RefreshInventory()
    {
        if (_runtime?.IsInitialized != true)
            return;

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
        UpdateControls();
    }

    private void OnDefinitionUpdated(SteamInventoryDefinitionUpdate_t callback)
    {
        var count = 0u;
        if (!SteamInventory.GetItemDefinitionIDs(null!, ref count))
        {
            _definitionsLoaded = false;
            _definitionStatusLabel.Text = "Steam ItemDef：回调已到达，但读取数量失败";
            AppendLog("SteamInventoryDefinitionUpdate_t：GetItemDefinitionIDs 数量读取失败");
            UpdateControls();
            return;
        }

        var serverItemDefs = new SteamItemDef_t[count];
        if (count > 0 && !SteamInventory.GetItemDefinitionIDs(serverItemDefs, ref count))
        {
            _definitionsLoaded = false;
            _definitionStatusLabel.Text = "Steam ItemDef：回调已到达，但读取定义失败";
            AppendLog("SteamInventoryDefinitionUpdate_t：GetItemDefinitionIDs 内容读取失败");
            UpdateControls();
            return;
        }

        var serverIds = serverItemDefs.Select(itemDef => (int)itemDef).ToHashSet();
        var localDefinitions = LubanData.Tables.TbSteamItemDef.DataList.Where(itemDef => itemDef.IsEnabled).ToArray();
        var missingIds = localDefinitions.Where(itemDef => !serverIds.Contains(itemDef.Id)).Select(itemDef => itemDef.Id).ToArray();
        _definitionsLoaded = missingIds.Length == 0;
        _definitionStatusLabel.Text = missingIds.Length == 0
            ? $"Steam ItemDef：服务器 {count} 条，本地启用 {localDefinitions.Length} 条，全部匹配"
            : $"Steam ItemDef：服务器 {count} 条；缺少本地定义 {string.Join(", ", missingIds)}";
        AppendLog($"SteamInventoryDefinitionUpdate_t：服务器返回 {count} 条定义");
        UpdateControls();
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
            else
                ShowPromoGrantResult(items);
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

    private void UpdateControls()
    {
        var available = _runtime?.IsInitialized == true;
        var hasPromoOptions = _promoItemOption.ItemCount > 0;
        var grantPending = HasPendingRequest(InventoryRequestKind.AddPromoItem);

        _loadDefinitionsButton.Disabled = !available;
        _refreshInventoryButton.Disabled = !available || HasPendingRequest(InventoryRequestKind.FullInventory);
        _promoItemOption.Disabled = !available || !hasPromoOptions || grantPending;
        _enableGrantCheck.Disabled = !available || !_definitionsLoaded || !hasPromoOptions || grantPending;
        _addPromoItemButton.Disabled = !available
            || !_definitionsLoaded
            || !hasPromoOptions
            || !_enableGrantCheck.ButtonPressed
            || grantPending;
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
