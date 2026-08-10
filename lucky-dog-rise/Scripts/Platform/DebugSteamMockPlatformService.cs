#if DEBUG
using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace LuckyDogRise;

/// <summary>
/// Debug-only decorator. Real mode is a transparent pass-through; mock modes never
/// submit inventory writes to Steam and publish the same events as the real service.
/// </summary>
public sealed class DebugSteamMockPlatformService : IGamePlatformService, IPlatformInventoryService,
    IRecoverablePlatformService, IPlatformAchievementSyncOperations, IPlatformAchievementTestOperations,
    IDebugSteamMockController
{
    private const ulong VoucherInstanceId = 9_100_000_000_000_001;
    private const ulong RewardInstanceId = 9_100_000_000_000_002;
    private const ulong LinkTreeReceiptInstanceBase = 9_100_100_000_000_000;
    private const ulong LinkTreeRewardInstanceBase = 9_100_200_000_000_000;
    private const int EventLimit = 50;
    private readonly IGamePlatformService _inner;
    private readonly IPlatformInventoryService _innerInventory;
    private readonly IRecoverablePlatformService _innerRecoverable;
    private readonly IPlatformAchievementTestOperations _innerAchievementTest;
    private readonly List<string> _events = [];
    private readonly List<PlatformInventoryItem> _mockItems = [];
    private readonly Dictionary<(int Bundle, int Receipt), DebugSteamLinkTreeGrant> _linkTreeGrants = [];
    private DebugSteamScenario _scenario;
    private DebugSteamPhase _phase = DebugSteamPhase.Ready;
    private PlatformConnectionState _connectionState = PlatformConnectionState.Ready;
    private double _phaseStartedAt;
    private int _voucherItemDefId;
    private int _rewardItemDefId;
    private bool _exchangePending;
    private ulong _pendingInputInstanceId;
    private int _pendingInputItemDefId;
    private int _pendingOutputItemDefId;
    private bool _promoGrantPending;
    private int _pendingPromoItemDefId;
    private int _pendingReceiptItemDefId;
    private PlatformInventoryTrustState _inventoryTrustState = PlatformInventoryTrustState.Unknown;
    private string _inventoryTrustMessage = "Steam Mock 尚未完成可信同步。";
    private string _lastEvent = "真实 Steam";
    private bool _disposed;

    public DebugSteamMockPlatformService(IGamePlatformService inner)
    {
        _inner = inner;
        _innerInventory = inner as IPlatformInventoryService;
        _innerRecoverable = inner as IRecoverablePlatformService;
        _innerAchievementTest = inner as IPlatformAchievementTestOperations;
        inner.UserStatsReady += OnInnerUserStatsReady;
        if (_innerInventory != null)
        {
            _innerInventory.InventorySnapshotChanged += OnInnerInventorySnapshotChanged;
            _innerInventory.PromoItemGrantCompleted += OnInnerPromoItemGrantCompleted;
            _innerInventory.PlaytimeDropCompleted += OnInnerPlaytimeDropCompleted;
            _innerInventory.InventoryExchangeCompleted += OnInnerInventoryExchangeCompleted;
        }
        if (_innerRecoverable != null)
        {
            _innerRecoverable.ConnectionStateChanged += OnInnerConnectionStateChanged;
            _innerRecoverable.InventoryTrustStateChanged += OnInnerInventoryTrustStateChanged;
        }
        if (_innerAchievementTest != null)
            _innerAchievementTest.StoreStatusChanged += OnInnerStoreStatusChanged;
        _phaseStartedAt = NowSeconds();
        PublishSnapshot();
    }

    public event Action UserStatsReady = delegate { };
    public event Action<string> StoreStatusChanged = delegate { };
    public event Action<PlatformInventorySnapshot> InventorySnapshotChanged = delegate { };
    public event Action<PlatformPromoItemGrantResult> PromoItemGrantCompleted = delegate { };
    public event Action<PlatformPlaytimeDropResult> PlaytimeDropCompleted = delegate { };
    public event Action<PlatformInventoryExchangeResult> InventoryExchangeCompleted = delegate { };
    public event Action<PlatformConnectionState> ConnectionStateChanged = delegate { };
    public event Action<PlatformInventoryTrustState> InventoryTrustStateChanged = delegate { };
    public event Action<DebugSteamMockSnapshot> SnapshotChanged = delegate { };

    public bool IsMockActive => _scenario != DebugSteamScenario.RealSteam;
    public string ProviderName => IsMockActive ? "Steam Debug Mock" : _inner.ProviderName;
    public string StatusMessage => IsMockActive ? _lastEvent : _inner.StatusMessage;
    public bool IsAvailable => IsMockActive ? _connectionState != PlatformConnectionState.Unavailable : _inner.IsAvailable;
    public uint AppId => _inner.AppId;
    public string PersonaName => IsMockActive ? "Debug Mock" : _inner.PersonaName;
    public bool IsReadyForWrites => !IsMockActive && (_inner as IPlatformAchievementTestOperations)?.IsReadyForWrites == true;
    public bool IsInventoryReady => IsMockActive
        ? _connectionState == PlatformConnectionState.Ready
          && _inventoryTrustState == PlatformInventoryTrustState.Trusted
        : _innerInventory?.IsInventoryReady == true;
    public bool IsPromoGrantPending => IsMockActive ? _promoGrantPending : _innerInventory?.IsPromoGrantPending == true;
    public bool IsPlaytimeDropPending => IsMockActive ? false : _innerInventory?.IsPlaytimeDropPending == true;
    public bool IsExchangePending => IsMockActive ? _exchangePending : _innerInventory?.IsExchangePending == true;
    public IReadOnlyList<PlatformInventoryItem> InventoryItems => IsMockActive ? _mockItems : _innerInventory?.InventoryItems ?? [];
    public PlatformConnectionState ConnectionState => IsMockActive
        ? _connectionState
        : _innerRecoverable?.ConnectionState ?? PlatformConnectionState.Offline;
    public PlatformInventoryTrustState InventoryTrustState => IsMockActive
        ? _inventoryTrustState
        : _innerRecoverable?.InventoryTrustState ?? PlatformInventoryTrustState.Unknown;
    public string InventoryTrustMessage => IsMockActive
        ? _inventoryTrustMessage
        : _innerRecoverable?.InventoryTrustMessage ?? "Steam 库存状态未知。";
    public DebugSteamMockSnapshot Snapshot => new(
        _scenario,
        _phase,
        Math.Max(0.0, NowSeconds() - _phaseStartedAt),
        ConnectionState,
        InventoryTrustState,
        GetVoucherQuantity(),
        _exchangePending || _promoGrantPending,
        _exchangePending ? "盲盒兑换" : _promoGrantPending ? "LinkTree 领奖" : "无",
        _lastEvent,
        _events.ToArray());

    public void RunCallbacks()
    {
        if (_disposed)
            return;
        _inner.RunCallbacks();
        if (!IsMockActive || (!_exchangePending && !_promoGrantPending))
            return;

        var elapsed = NowSeconds() - _phaseStartedAt;
        switch (_scenario)
        {
            case DebugSteamScenario.SlowSuccess
                when _phase is DebugSteamPhase.ExchangeWaiting or DebugSteamPhase.PromoGrantWaiting
                     && elapsed >= 3.0:
                CompletePendingOperationSuccess("慢响应在 3 秒后返回成功结果。");
                break;
            case DebugSteamScenario.TimeoutVerifiedSuccess or DebugSteamScenario.TimeoutVerifiedFallback
                when _phase is DebugSteamPhase.ExchangeWaiting or DebugSteamPhase.PromoGrantWaiting
                     && elapsed >= 10.0:
                BeginInventoryVerification("请求 10 秒无回执，开始完整库存复查。");
                break;
            case DebugSteamScenario.TimeoutVerifiedSuccess when _phase == DebugSteamPhase.InventoryVerification && elapsed >= 10.0:
                CompleteVerification(success: true);
                break;
            case DebugSteamScenario.TimeoutVerifiedFallback when _phase == DebugSteamPhase.InventoryVerification && elapsed >= 10.0:
                CompleteVerification(success: false);
                break;
            case DebugSteamScenario.DisconnectAfterSubmit or DebugSteamScenario.DisconnectRecoverSuccess
                when _phase is DebugSteamPhase.ExchangeWaiting or DebugSteamPhase.PromoGrantWaiting
                     && elapsed >= 1.0:
                SetPhase(DebugSteamPhase.Unavailable, PlatformConnectionState.Unavailable, "提交 1 秒后连接中断，请求结果未知。");
                break;
            case DebugSteamScenario.DisconnectRecoverSuccess when _phase == DebugSteamPhase.Unavailable && elapsed >= 10.0:
                BeginInventoryVerification("断联保持 10 秒后恢复，开始同步库存。");
                break;
            case DebugSteamScenario.DisconnectRecoverSuccess when _phase == DebugSteamPhase.InventoryVerification && elapsed >= 10.0:
                CompleteVerification(success: true);
                break;
        }
        PublishSnapshot();
    }

    public bool TrySelectScenario(DebugSteamScenario scenario, out string message)
    {
        if (_exchangePending
            || _promoGrantPending
            || (!IsMockActive && scenario != DebugSteamScenario.RealSteam
                && (_innerInventory?.IsExchangePending == true
                    || _innerInventory?.IsPromoGrantPending == true
                    || _innerInventory?.IsPlaytimeDropPending == true)))
        {
            message = "当前真实或模拟 Steam 交易仍在处理中，不能切换场景。";
            return false;
        }
        _scenario = scenario;
        ResetScenario();
        message = scenario == DebugSteamScenario.RealSteam ? "已恢复真实 Steam。" : "已进入 Steam Mock 沙箱。";
        return true;
    }

    public void ConfigureBlindBox(int voucherItemDefId, int rewardItemDefId)
    {
        _voucherItemDefId = voucherItemDefId;
        _rewardItemDefId = rewardItemDefId;
        if (IsMockActive)
            ResetScenario();
    }

    public void ConfigureLinkTreeGrants(IReadOnlyList<DebugSteamLinkTreeGrant> grants)
    {
        _linkTreeGrants.Clear();
        foreach (var grant in grants.Where(grant => grant.BundleItemDefId > 0 && grant.ReceiptItemDefId > 0))
            _linkTreeGrants[(grant.BundleItemDefId, grant.ReceiptItemDefId)] = grant;
    }

    public void ResetScenario()
    {
        _exchangePending = false;
        _pendingInputInstanceId = 0;
        _pendingInputItemDefId = 0;
        _pendingOutputItemDefId = 0;
        _promoGrantPending = false;
        _pendingPromoItemDefId = 0;
        _pendingReceiptItemDefId = 0;
        _mockItems.Clear();
        if (_scenario == DebugSteamScenario.RealSteam)
        {
            _events.Clear();
            _phase = DebugSteamPhase.Ready;
            _phaseStartedAt = NowSeconds();
            _lastEvent = "真实 Steam";
            SetInventoryTrustState(PlatformInventoryTrustState.Unknown, "正在恢复真实 Steam 库存同步。");
            _innerInventory?.StartInventorySynchronization();
            PublishSnapshot();
            return;
        }

        if (_voucherItemDefId > 0)
            _mockItems.Add(new PlatformInventoryItem(VoucherInstanceId, _voucherItemDefId, 1));
        _events.Clear();
        if (_scenario == DebugSteamScenario.UnavailableBeforeOpen)
        {
            SetInventoryTrustState(PlatformInventoryTrustState.RevalidationRequired, "Steam Mock 请求前不可用。");
            SetPhase(DebugSteamPhase.Unavailable, PlatformConnectionState.Unavailable, "开盒前 Steam 已不可用，将立即选择本地 Fallback。");
        }
        else
        {
            SetInventoryTrustState(PlatformInventoryTrustState.Trusted, "Steam Mock 初始库存可信。");
            SetPhase(DebugSteamPhase.Ready, PlatformConnectionState.Ready, "模拟库存已重置，包含 1 张确定实例的装扮券。");
        }
        PublishInventorySnapshot("Steam Mock 初始库存。", succeeded: _connectionState == PlatformConnectionState.Ready);
        PublishSnapshot();
    }

    public void AdvancePhase()
    {
        if (!IsMockActive || (!_exchangePending && !_promoGrantPending))
            return;
        switch (_phase)
        {
            case DebugSteamPhase.ExchangeWaiting or DebugSteamPhase.PromoGrantWaiting
                when _scenario == DebugSteamScenario.SlowSuccess:
                CompletePendingOperationSuccess("手动推进：模拟请求成功。");
                break;
            case DebugSteamPhase.ExchangeWaiting or DebugSteamPhase.PromoGrantWaiting
                when _scenario is DebugSteamScenario.TimeoutVerifiedSuccess or DebugSteamScenario.TimeoutVerifiedFallback:
                BeginInventoryVerification("手动推进：进入库存复查。");
                break;
            case DebugSteamPhase.ExchangeWaiting or DebugSteamPhase.PromoGrantWaiting
                when _scenario is DebugSteamScenario.DisconnectAfterSubmit or DebugSteamScenario.DisconnectRecoverSuccess:
                SetPhase(DebugSteamPhase.Unavailable, PlatformConnectionState.Unavailable, "手动推进：连接中断，结果未知。");
                break;
            case DebugSteamPhase.Unavailable:
                BeginInventoryVerification("手动推进：恢复连接并开始库存复查。");
                break;
            case DebugSteamPhase.InventoryVerification:
                CompleteVerification(_scenario != DebugSteamScenario.TimeoutVerifiedFallback);
                break;
        }
        PublishSnapshot();
    }

    public void StartInventorySynchronization()
    {
        if (!IsMockActive)
        {
            _innerInventory?.StartInventorySynchronization();
            return;
        }
        if (_connectionState == PlatformConnectionState.Unavailable)
        {
            SetInventoryTrustState(PlatformInventoryTrustState.RevalidationRequired, "Mock 仍处于断联阶段。");
            AddEvent("Mock 忽略库存同步请求：当前仍处于断联阶段。");
            return;
        }
        PublishInventorySnapshot("Steam Mock 库存同步完成。", true);
    }

    public bool TryGrantPromoItem(int promoItemDefId, int receiptItemDefId, out string message)
    {
        if (!IsMockActive)
        {
            if (_innerInventory != null)
                return _innerInventory.TryGrantPromoItem(promoItemDefId, receiptItemDefId, out message);
            message = "当前平台不支持 Steam 库存。";
            return false;
        }
        if (_scenario == DebugSteamScenario.UnavailableBeforeOpen || !IsInventoryReady)
        {
            message = "Steam Mock 当前不可用，LinkTree 领奖尚未提交。";
            AddEvent(message);
            return false;
        }
        if (_exchangePending || _promoGrantPending)
        {
            message = "Steam Mock 已有库存写事务正在处理。";
            return false;
        }
        if (!_linkTreeGrants.ContainsKey((promoItemDefId, receiptItemDefId)))
        {
            message = $"Steam Mock 未配置 LinkTree Bundle/回执：{promoItemDefId}/{receiptItemDefId}。";
            AddEvent(message);
            return false;
        }

        _promoGrantPending = true;
        _pendingPromoItemDefId = promoItemDefId;
        _pendingReceiptItemDefId = receiptItemDefId;
        SetPhase(DebugSteamPhase.PromoGrantWaiting, PlatformConnectionState.Ready, "已提交模拟 LinkTree AddPromoItem 请求。");
        message = "Steam Mock 已接收 LinkTree 领奖请求。";
        PublishSnapshot();
        return true;
    }

    public bool TryTriggerPlaytimeDrop(int generatorItemDefId, int outputItemDefId, out string message)
    {
        if (!IsMockActive)
        {
            if (_innerInventory != null)
                return _innerInventory.TryTriggerPlaytimeDrop(generatorItemDefId, outputItemDefId, out message);
            message = "当前平台不支持 Steam 库存。";
            return false;
        }
        message = "Steam Mock 第一版不模拟 TriggerItemDrop；未向 Steam 写入。";
        AddEvent(message);
        return false;
    }

    public bool TryExchangeItem(ulong inputInstanceId, int inputItemDefId, int outputItemDefId, out string message)
    {
        if (!IsMockActive)
        {
            if (_innerInventory != null)
                return _innerInventory.TryExchangeItem(inputInstanceId, inputItemDefId, outputItemDefId, out message);
            message = "当前平台不支持 Steam 库存。";
            return false;
        }
        if (_scenario == DebugSteamScenario.UnavailableBeforeOpen || !IsInventoryReady)
        {
            message = "Steam Mock 当前不可用。";
            return false;
        }
        if (_exchangePending || _promoGrantPending
            || inputInstanceId != VoucherInstanceId || inputItemDefId != _voucherItemDefId)
        {
            message = "Steam Mock 兑换请求与模拟库存不匹配，或已有在途请求。";
            return false;
        }
        _exchangePending = true;
        _pendingInputInstanceId = inputInstanceId;
        _pendingInputItemDefId = inputItemDefId;
        _pendingOutputItemDefId = outputItemDefId;
        SetPhase(DebugSteamPhase.ExchangeWaiting, PlatformConnectionState.Ready, "已提交模拟 ExchangeItems 请求。");
        message = "Steam Mock 已接收兑换请求。";
        PublishSnapshot();
        return true;
    }

    public void RequestReconnect()
    {
        if (!IsMockActive)
            _innerRecoverable?.RequestReconnect();
        else
            AddEvent("业务请求恢复连接；Mock 将遵循当前场景阶段。", publish: true);
    }

    public void RequireInventoryRevalidation(string reason)
    {
        if (!IsMockActive)
        {
            _innerRecoverable?.RequireInventoryRevalidation(reason);
            return;
        }

        SetInventoryTrustState(PlatformInventoryTrustState.RevalidationRequired, reason);
        AddEvent($"库存标记为待确认：{reason}", publish: true);
    }

    public bool OpenFriendsOverlay() => !IsMockActive && _inner.OpenFriendsOverlay();
    public PlatformAchievementReadResult ReadAchievementStates(IEnumerable<string> names) =>
        _inner.ReadAchievementStates(names);

    public PlatformAchievementUnlockResult UnlockAchievements(IEnumerable<string> names)
    {
        if (IsMockActive)
            return new PlatformAchievementUnlockResult(false, "Steam Mock 沙箱禁止上传成就。", []);
        return (_inner as IPlatformAchievementSyncOperations)?.UnlockAchievements(names)
               ?? new PlatformAchievementUnlockResult(false, "当前平台不支持成就写入。", []);
    }

    public bool TrySetAchievementForTesting(string apiName, bool unlocked, out string message)
    {
        if (IsMockActive)
        {
            message = "Steam Mock 沙箱禁止修改真实成就。";
            return false;
        }
        if (_inner is IPlatformAchievementTestOperations test)
            return test.TrySetAchievementForTesting(apiName, unlocked, out message);
        message = "当前平台不支持测试成就写入。";
        return false;
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _inner.UserStatsReady -= OnInnerUserStatsReady;
        if (_innerInventory != null)
        {
            _innerInventory.InventorySnapshotChanged -= OnInnerInventorySnapshotChanged;
            _innerInventory.PromoItemGrantCompleted -= OnInnerPromoItemGrantCompleted;
            _innerInventory.PlaytimeDropCompleted -= OnInnerPlaytimeDropCompleted;
            _innerInventory.InventoryExchangeCompleted -= OnInnerInventoryExchangeCompleted;
        }
        if (_innerRecoverable != null)
        {
            _innerRecoverable.ConnectionStateChanged -= OnInnerConnectionStateChanged;
            _innerRecoverable.InventoryTrustStateChanged -= OnInnerInventoryTrustStateChanged;
        }
        if (_innerAchievementTest != null)
            _innerAchievementTest.StoreStatusChanged -= OnInnerStoreStatusChanged;
        _inner.Dispose();
    }

    private void BeginInventoryVerification(string message) =>
        SetPhase(DebugSteamPhase.InventoryVerification, PlatformConnectionState.InventorySyncing, message);

    private void CompletePendingOperationSuccess(string message)
    {
        if (_exchangePending)
            CompleteExchangeSuccess(message);
        else if (_promoGrantPending)
            CompletePromoGrantSuccess(message);
    }

    private void CompleteExchangeSuccess(string message)
    {
        ApplySuccessfulInventoryResult();
        _exchangePending = false;
        SetPhase(DebugSteamPhase.Completed, PlatformConnectionState.Ready, message);
        InventoryExchangeCompleted(new PlatformInventoryExchangeResult(
            _pendingInputInstanceId, _pendingInputItemDefId, _pendingOutputItemDefId,
            true, message, _mockItems.Where(item => item.InstanceId == RewardInstanceId).ToArray()));
        PublishInventorySnapshot("兑换回调后的完整模拟库存。", true);
    }

    private void CompletePromoGrantSuccess(string message)
    {
        var changedItems = ApplySuccessfulPromoGrant();
        _promoGrantPending = false;
        SetPhase(DebugSteamPhase.Completed, PlatformConnectionState.Ready, message);
        PromoItemGrantCompleted(new PlatformPromoItemGrantResult(
            _pendingPromoItemDefId,
            _pendingReceiptItemDefId,
            true,
            true,
            message,
            changedItems));
        PublishInventorySnapshot("LinkTree 领奖回调后的完整模拟库存。", true);
    }

    private void CompleteVerification(bool success)
    {
        var operation = _exchangePending ? "盲盒兑换" : _promoGrantPending ? "LinkTree 领奖" : "库存请求";
        if (success && _exchangePending)
            ApplySuccessfulInventoryResult();
        else if (success && _promoGrantPending)
            ApplySuccessfulPromoGrant();
        _exchangePending = false;
        _promoGrantPending = false;
        SetPhase(DebugSteamPhase.Completed, PlatformConnectionState.Ready,
            success
                ? $"库存复查确认：{operation}已成功。"
                : $"库存复查确认：{operation}未发生；盲盒可安全 Fallback，LinkTree 可重试。");
        PublishInventorySnapshot(_lastEvent, true);
    }

    private void ApplySuccessfulInventoryResult()
    {
        _mockItems.RemoveAll(item => item.InstanceId == VoucherInstanceId);
        if (_rewardItemDefId > 0 && _mockItems.All(item => item.InstanceId != RewardInstanceId))
            _mockItems.Add(new PlatformInventoryItem(RewardInstanceId, _rewardItemDefId, 1));
    }

    private IReadOnlyList<PlatformInventoryItem> ApplySuccessfulPromoGrant()
    {
        if (!_linkTreeGrants.TryGetValue(
                (_pendingPromoItemDefId, _pendingReceiptItemDefId),
                out var grant))
        {
            return [];
        }

        var changedItems = new List<PlatformInventoryItem>();
        var receipt = new PlatformInventoryItem(
            LinkTreeReceiptInstanceBase + checked((ulong)grant.ReceiptItemDefId),
            grant.ReceiptItemDefId,
            1);
        if (_mockItems.All(item => item.InstanceId != receipt.InstanceId))
        {
            _mockItems.Add(receipt);
            changedItems.Add(receipt);
        }

        if (grant.RewardItemDefId > 0)
        {
            var reward = new PlatformInventoryItem(
                LinkTreeRewardInstanceBase + checked((ulong)grant.ReceiptItemDefId),
                grant.RewardItemDefId,
                1);
            if (_mockItems.All(item => item.InstanceId != reward.InstanceId))
            {
                _mockItems.Add(reward);
                changedItems.Add(reward);
            }
        }

        return changedItems;
    }

    private void PublishInventorySnapshot(string message, bool succeeded)
    {
        SetInventoryTrustState(
            succeeded ? PlatformInventoryTrustState.Trusted : PlatformInventoryTrustState.RevalidationRequired,
            message);
        AddEvent(message);
        DiagnosticLog.Record("steam_mock_inventory_snapshot", new Dictionary<string, object>
        {
            ["scenario"] = _scenario.ToString(),
            ["phase"] = _phase.ToString(),
            ["succeeded"] = succeeded,
            ["items"] = string.Join(",", _mockItems.Select(item => $"{item.InstanceId}:{item.ItemDefId}x{item.Quantity}")),
        });
        InventorySnapshotChanged(new PlatformInventorySnapshot(
            succeeded,
            message,
            _mockItems.Select(item => item.ItemDefId).ToHashSet(),
            _mockItems.ToArray()));
    }

    private void SetPhase(DebugSteamPhase phase, PlatformConnectionState state, string message)
    {
        var previousState = _connectionState;
        _phase = phase;
        _phaseStartedAt = NowSeconds();
        _connectionState = state;
        if (state is PlatformConnectionState.Unavailable or PlatformConnectionState.InventorySyncing)
            SetInventoryTrustState(PlatformInventoryTrustState.RevalidationRequired, message);
        AddEvent(message);
        DiagnosticLog.Record("steam_mock_phase", new Dictionary<string, object>
        {
            ["scenario"] = _scenario.ToString(), ["phase"] = phase.ToString(), ["state"] = state.ToString(), ["event"] = message,
        });
        if (previousState != state)
            ConnectionStateChanged(state);
    }

    private void SetInventoryTrustState(PlatformInventoryTrustState state, string message)
    {
        var changed = _inventoryTrustState != state;
        _inventoryTrustState = state;
        _inventoryTrustMessage = message;
        if (changed)
            InventoryTrustStateChanged(state);
    }

    private uint GetVoucherQuantity() => checked((uint)_mockItems
        .Where(item => item.ItemDefId == _voucherItemDefId)
        .Sum(item => (long)item.Quantity));

    private void AddEvent(string message, bool publish = false)
    {
        _lastEvent = message;
        _events.Add($"{DateTime.Now:HH:mm:ss}  {message}");
        if (_events.Count > EventLimit)
            _events.RemoveRange(0, _events.Count - EventLimit);
        if (publish)
            PublishSnapshot();
    }

    private void PublishSnapshot() => SnapshotChanged(Snapshot);
    private static double NowSeconds() => Time.GetTicksMsec() / 1000.0;

    private void OnInnerUserStatsReady() { if (!IsMockActive) UserStatsReady(); }
    private void OnInnerInventorySnapshotChanged(PlatformInventorySnapshot value) { if (!IsMockActive) InventorySnapshotChanged(value); }
    private void OnInnerPromoItemGrantCompleted(PlatformPromoItemGrantResult value) { if (!IsMockActive) PromoItemGrantCompleted(value); }
    private void OnInnerPlaytimeDropCompleted(PlatformPlaytimeDropResult value) { if (!IsMockActive) PlaytimeDropCompleted(value); }
    private void OnInnerInventoryExchangeCompleted(PlatformInventoryExchangeResult value) { if (!IsMockActive) InventoryExchangeCompleted(value); }
    private void OnInnerConnectionStateChanged(PlatformConnectionState value) { if (!IsMockActive) ConnectionStateChanged(value); }
    private void OnInnerInventoryTrustStateChanged(PlatformInventoryTrustState value) { if (!IsMockActive) InventoryTrustStateChanged(value); }
    private void OnInnerStoreStatusChanged(string value) { if (!IsMockActive) StoreStatusChanged(value); }
}
#endif
