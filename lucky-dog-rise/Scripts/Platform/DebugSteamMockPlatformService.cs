#if DEBUG
using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace LuckyDogRise;

/// <summary>
/// Debug-only platform decorator. Real mode forwards every call. Mock modes intercept
/// inventory writes and drive the production callbacks/state machine without touching Steam.
/// </summary>
public sealed class DebugSteamMockPlatformService : IGamePlatformService, IPlatformInventoryService,
    IRecoverablePlatformService, IPlatformAchievementSyncOperations, IPlatformAchievementTestOperations,
    IDebugSteamMockController
{
    private const ulong RewardInstanceBase = 9_100_000_000_000_000;
    private const ulong LinkTreeReceiptInstanceBase = 9_100_100_000_000_000;
    private const ulong LinkTreeRewardInstanceBase = 9_100_200_000_000_000;
    private const int EventLimit = 50;

    private readonly IGamePlatformService _inner;
    private readonly IPlatformInventoryService _innerInventory;
    private readonly IRecoverablePlatformService _innerRecoverable;
    private readonly IPlatformAchievementTestOperations _innerAchievementTest;
    private readonly bool _canUseRealSteam;
    private readonly string _fallbackAccountId;
    private readonly List<string> _events = [];
    private readonly List<PlatformInventoryItem> _mockItems = [];
    private readonly Dictionary<(int Bundle, int Receipt), DebugSteamLinkTreeGrant> _linkTreeGrants = [];

    private DebugSteamScenario _scenario = DebugSteamScenario.NormalSuccess;
    private bool _mockActive;
    private DebugSteamPhase _phase = DebugSteamPhase.Ready;
    private PlatformConnectionState _connectionState = PlatformConnectionState.Ready;
    private PlatformInventoryTrustState _inventoryTrustState = PlatformInventoryTrustState.Unknown;
    private string _inventoryTrustMessage = "Steam Mock 尚未完成可信同步。";
    private string _lastEvent = "真实 Steam";
    private double _phaseStartedAt;
    private int _blindBoxId;
    private int _rewardItemDefId;
    private int _pendingGeneratorItemDefId;
    private bool _playtimeDropPending;
    private bool _promoGrantPending;
    private int _pendingPromoItemDefId;
    private int _pendingReceiptItemDefId;
    private ulong _nextRewardInstanceOffset;
    private ulong _lastRewardInstanceId;
    private DebugSteamPlaytimeDropRule _playtimeDropRule;
    private readonly Queue<double> _dropWindowGrantTimes = new();
    private readonly HashSet<int> _activatedGeneratorItemDefIds = [];
    private double _simulatedPlaytimeSeconds;
    private double _simulatedElapsedSeconds;
    private double _playtimeAtLastGrantSeconds;
    private double _simulationClockUpdatedAt;
    private bool _disposed;
    private bool _pendingGeneratorWasActivation;

    public DebugSteamMockPlatformService(
        IGamePlatformService inner,
        bool startInMock = false,
        bool canUseRealSteam = true,
        DebugSteamScenario initialScenario = DebugSteamScenario.NormalSuccess)
    {
        _inner = inner;
        _mockActive = startInMock;
        _canUseRealSteam = canUseRealSteam;
        _fallbackAccountId = startInMock ? "steam_mock" : string.Empty;
        _scenario = initialScenario;
        _innerInventory = inner as IPlatformInventoryService;
        _innerRecoverable = inner as IRecoverablePlatformService;
        _innerAchievementTest = inner as IPlatformAchievementTestOperations;
        inner.UserStatsReady += OnInnerUserStatsReady;
        if (_innerInventory != null)
        {
            _innerInventory.InventorySnapshotChanged += OnInnerInventorySnapshotChanged;
            _innerInventory.PromoItemGrantCompleted += OnInnerPromoItemGrantCompleted;
            _innerInventory.PlaytimeDropCompleted += OnInnerPlaytimeDropCompleted;
        }
        if (_innerRecoverable != null)
        {
            _innerRecoverable.ConnectionStateChanged += OnInnerConnectionStateChanged;
            _innerRecoverable.InventoryTrustStateChanged += OnInnerInventoryTrustStateChanged;
            _innerRecoverable.AccountIdentityConflictDetected += OnInnerAccountIdentityConflictDetected;
        }
        if (_innerAchievementTest != null)
            _innerAchievementTest.StoreStatusChanged += OnInnerStoreStatusChanged;
        _phaseStartedAt = NowSeconds();
        _simulationClockUpdatedAt = _phaseStartedAt;
        if (startInMock)
            ResetScenario();
        else
            PublishSnapshot();
    }

    public event Action UserStatsReady = delegate { };
    public event Action<string> StoreStatusChanged = delegate { };
    public event Action<PlatformInventorySnapshot> InventorySnapshotChanged = delegate { };
    public event Action<PlatformPromoItemGrantResult> PromoItemGrantCompleted = delegate { };
    public event Action<PlatformPlaytimeDropResult> PlaytimeDropCompleted = delegate { };
    public event Action<PlatformConnectionState> ConnectionStateChanged = delegate { };
    public event Action<PlatformInventoryTrustState> InventoryTrustStateChanged = delegate { };
    public event Action<string, string> AccountIdentityConflictDetected = delegate { };
    public event Action<DebugSteamMockSnapshot> SnapshotChanged = delegate { };

    public bool IsMockActive => _mockActive;
    public bool CanUseRealSteam => _canUseRealSteam;
    public string ProviderName => IsMockActive ? "Steam Debug Mock" : _inner.ProviderName;
    public string StatusMessage => IsMockActive ? _lastEvent : _inner.StatusMessage;
    public bool IsAvailable => IsMockActive ? _connectionState != PlatformConnectionState.Unavailable : _inner.IsAvailable;
    public uint AppId => _inner.AppId;
    public string PersonaName => IsMockActive ? "Debug Mock" : _inner.PersonaName;
    public string AccountProvider => _fallbackAccountId.Length > 0 ? "dev" : _inner.AccountProvider;
    public string AccountId => _fallbackAccountId.Length > 0 ? _fallbackAccountId : _inner.AccountId;
    public bool IsReadyForWrites => !IsMockActive && _innerAchievementTest?.IsReadyForWrites == true;
    public bool IsInventoryReady => IsMockActive
        ? _connectionState == PlatformConnectionState.Ready
          && _inventoryTrustState == PlatformInventoryTrustState.Trusted
        : _innerInventory?.IsInventoryReady == true;
    public bool IsPromoGrantPending => IsMockActive ? _promoGrantPending : _innerInventory?.IsPromoGrantPending == true;
    public bool IsPlaytimeDropPending => IsMockActive ? _playtimeDropPending : _innerInventory?.IsPlaytimeDropPending == true;
    public IReadOnlyList<PlatformInventoryItem> InventoryItems =>
        IsMockActive ? _mockItems : _innerInventory?.InventoryItems ?? [];
    public PlatformConnectionState ConnectionState => IsMockActive
        ? _connectionState
        : _innerRecoverable?.ConnectionState ?? PlatformConnectionState.Offline;
    public bool CanRequestClientRelaunch => !IsMockActive
        && _innerRecoverable?.CanRequestClientRelaunch == true;
    public PlatformInventoryTrustState InventoryTrustState => IsMockActive
        ? _inventoryTrustState
        : _innerRecoverable?.InventoryTrustState ?? PlatformInventoryTrustState.Unknown;
    public string InventoryTrustMessage => IsMockActive
        ? _inventoryTrustMessage
        : _innerRecoverable?.InventoryTrustMessage ?? "Steam 库存状态未知。";
    public bool HasAccountIdentityConflict => _innerRecoverable?.HasAccountIdentityConflict == true;
    public DebugSteamMockSnapshot Snapshot => new(
        _scenario,
        _phase,
        Math.Max(0.0, NowSeconds() - _phaseStartedAt),
        ConnectionState,
        InventoryTrustState,
        _pendingGeneratorItemDefId,
        _pendingGeneratorItemDefId > 0 && _activatedGeneratorItemDefIds.Contains(_pendingGeneratorItemDefId),
        _playtimeDropPending && _pendingGeneratorWasActivation,
        _lastRewardInstanceId,
        _simulatedPlaytimeSeconds,
        _playtimeDropRule?.DropIntervalSeconds ?? 0.0,
        _dropWindowGrantTimes.Count,
        _playtimeDropRule?.DropMaxPerWindow ?? 0,
        _playtimeDropPending || _promoGrantPending,
        _playtimeDropPending
            ? _pendingGeneratorWasActivation ? "Generator 预热" : "盲盒奖励准备"
            : _promoGrantPending ? "LinkTree 领奖" : "无",
        _lastEvent,
        _events.ToArray());

    public void RunCallbacks()
    {
        if (_disposed)
            return;
        _inner.RunCallbacks();
        UpdateSimulationClock();
        if (!IsMockActive || (!_playtimeDropPending && !_promoGrantPending))
            return;

        var elapsed = NowSeconds() - _phaseStartedAt;
        switch (_scenario)
        {
            case DebugSteamScenario.NormalSuccess
                when _phase is DebugSteamPhase.PlaytimeDropWaiting or DebugSteamPhase.PromoGrantWaiting
                     && elapsed >= 0.1:
                CompleteNormalPendingOperation();
                break;
            case DebugSteamScenario.ForcedSuccess
                when _phase is DebugSteamPhase.PlaytimeDropWaiting or DebugSteamPhase.PromoGrantWaiting
                     && elapsed >= 0.1:
                CompletePendingOperationSuccess("强制快速成功返回。", enforcePlaytimeRule: false);
                break;
            case DebugSteamScenario.SlowSuccess
                when _phase is DebugSteamPhase.PlaytimeDropWaiting or DebugSteamPhase.PromoGrantWaiting
                     && elapsed >= 3.0:
                CompletePendingOperationSuccess("慢响应在 3 秒后返回成功结果。", enforcePlaytimeRule: true);
                break;
            case DebugSteamScenario.TimeoutVerifiedSuccess or DebugSteamScenario.TimeoutVerifiedFallback
                when _phase is DebugSteamPhase.PlaytimeDropWaiting or DebugSteamPhase.PromoGrantWaiting
                     && elapsed >= 10.0:
                BeginInventoryVerification("请求 10 秒无回执，开始完整库存复查。");
                break;
            case DebugSteamScenario.TimeoutVerifiedSuccess
                when _phase == DebugSteamPhase.InventoryVerification && elapsed >= 10.0:
                CompleteVerification(success: true);
                break;
            case DebugSteamScenario.TimeoutVerifiedFallback
                when _phase == DebugSteamPhase.InventoryVerification && elapsed >= 10.0:
                CompleteVerification(success: false);
                break;
            case DebugSteamScenario.DisconnectAfterSubmit or DebugSteamScenario.DisconnectRecoverSuccess
                when _phase is DebugSteamPhase.PlaytimeDropWaiting or DebugSteamPhase.PromoGrantWaiting
                     && elapsed >= 1.0:
                SetPhase(DebugSteamPhase.Unavailable, PlatformConnectionState.Unavailable,
                    "提交 1 秒后连接中断，请求结果未知。");
                break;
            case DebugSteamScenario.DisconnectRecoverSuccess
                when _phase == DebugSteamPhase.Unavailable && elapsed >= 10.0:
                BeginInventoryVerification("断联保持 10 秒后恢复，开始同步库存。");
                break;
            case DebugSteamScenario.DisconnectRecoverSuccess
                when _phase == DebugSteamPhase.InventoryVerification && elapsed >= 10.0:
                CompleteVerification(success: true);
                break;
        }
        PublishSnapshot();
    }

    public bool TrySelectScenario(DebugSteamScenario scenario, out string message)
    {
        if (_playtimeDropPending || _promoGrantPending
            || (!IsMockActive
                && (_innerInventory?.IsPromoGrantPending == true
                    || _innerInventory?.IsPlaytimeDropPending == true)))
        {
            message = "当前真实或模拟 Steam 事务仍在处理中，不能切换场景。";
            return false;
        }
        _mockActive = true;
        _scenario = scenario;
        ResetScenario();
        message = "已进入 Steam Mock 沙箱。";
        return true;
    }

    public bool TryUseRealSteam(out string message)
    {
        if (!_canUseRealSteam)
        {
            message = "当前进程从 Steam 模拟环境启动，不能切换到真实 Steam。";
            return false;
        }
        if (_playtimeDropPending || _promoGrantPending)
        {
            message = "当前模拟 Steam 事务仍在处理中，不能恢复真实 Steam。";
            return false;
        }

        _mockActive = false;
        ResetScenario();
        message = "已恢复真实 Steam。";
        return true;
    }

    public void ConfigureBlindBox(
        int blindBoxId,
        int rewardItemDefId,
        DebugSteamPlaytimeDropRule dropRule)
    {
        var generatorChanged = _playtimeDropRule?.GeneratorItemDefId != dropRule?.GeneratorItemDefId;
        if (_blindBoxId == blindBoxId
            && _rewardItemDefId == rewardItemDefId
            && _playtimeDropRule == dropRule)
            return;
        _blindBoxId = blindBoxId;
        _rewardItemDefId = rewardItemDefId;
        _playtimeDropRule = dropRule;
        if (generatorChanged)
        {
            _playtimeAtLastGrantSeconds = _simulatedPlaytimeSeconds;
            _dropWindowGrantTimes.Clear();
        }
        if (IsMockActive)
        {
            var ruleText = dropRule == null
                ? "强制回执"
                : $"资格 {dropRule.DropIntervalSeconds:0} 秒 / 窗口 {dropRule.DropWindowSeconds:0} 秒 x{dropRule.DropMaxPerWindow}";
            AddEvent(
                $"Mock 奖励配置已切换：BlindBox {blindBoxId} / ItemDef {rewardItemDefId} / {ruleText}。",
                publish: true);
        }
    }

    public void AdvanceSimulatedPlaytime(double seconds)
    {
        if (!IsMockActive || !double.IsFinite(seconds) || seconds <= 0.0)
            return;
        UpdateSimulationClock();
        _simulatedPlaytimeSeconds += seconds;
        _simulatedElapsedSeconds += seconds;
        PruneDropWindow();
        AddEvent(
            $"手动推进模拟 Steam 游玩时间 {seconds:0} 秒；累计 {_simulatedPlaytimeSeconds:0} 秒。",
            publish: true);
    }

    public void ConfigureLinkTreeGrants(IReadOnlyList<DebugSteamLinkTreeGrant> grants)
    {
        _linkTreeGrants.Clear();
        foreach (var grant in grants.Where(grant => grant.BundleItemDefId > 0 && grant.ReceiptItemDefId > 0))
            _linkTreeGrants[(grant.BundleItemDefId, grant.ReceiptItemDefId)] = grant;
    }

    public void ResetScenario()
    {
        _playtimeDropPending = false;
        _pendingGeneratorItemDefId = 0;
        _pendingGeneratorWasActivation = false;
        _promoGrantPending = false;
        _pendingPromoItemDefId = 0;
        _pendingReceiptItemDefId = 0;
        _nextRewardInstanceOffset = 0;
        _lastRewardInstanceId = 0;
        _simulatedPlaytimeSeconds = 0.0;
        _simulatedElapsedSeconds = 0.0;
        _playtimeAtLastGrantSeconds = 0.0;
        _simulationClockUpdatedAt = NowSeconds();
        _dropWindowGrantTimes.Clear();
        _activatedGeneratorItemDefIds.Clear();
        _mockItems.Clear();
        _events.Clear();
        if (!IsMockActive)
        {
            _phase = DebugSteamPhase.Ready;
            _phaseStartedAt = NowSeconds();
            _lastEvent = "真实 Steam";
            SetInventoryTrustState(PlatformInventoryTrustState.Unknown, "正在恢复真实 Steam 库存同步。");
            _innerInventory?.StartInventorySynchronization();
            PublishSnapshot();
            return;
        }

        if (_scenario == DebugSteamScenario.UnavailableBeforeOpen)
        {
            SetInventoryTrustState(PlatformInventoryTrustState.RevalidationRequired, "Steam Mock 请求前不可用。");
            SetPhase(DebugSteamPhase.Unavailable, PlatformConnectionState.Unavailable,
                "奖励准备前 Steam 已不可用，展示点将使用本地 Fallback。");
        }
        else
        {
            SetInventoryTrustState(PlatformInventoryTrustState.Trusted, "Steam Mock 初始库存可信。");
            SetPhase(DebugSteamPhase.Ready, PlatformConnectionState.Ready,
                $"模拟库存已重置，待请求 Generator（BlindBox {_blindBoxId}）。");
        }
        PublishInventorySnapshot("Steam Mock 初始库存。", _connectionState == PlatformConnectionState.Ready);
        PublishSnapshot();
    }

    public void AdvancePhase()
    {
        if (!IsMockActive || (!_playtimeDropPending && !_promoGrantPending))
            return;
        switch (_phase)
        {
            case DebugSteamPhase.PlaytimeDropWaiting or DebugSteamPhase.PromoGrantWaiting
                when _scenario == DebugSteamScenario.NormalSuccess:
                CompleteNormalPendingOperation();
                break;
            case DebugSteamPhase.PlaytimeDropWaiting or DebugSteamPhase.PromoGrantWaiting
                when _scenario == DebugSteamScenario.ForcedSuccess:
                CompletePendingOperationSuccess("手动推进：强制请求成功。", enforcePlaytimeRule: false);
                break;
            case DebugSteamPhase.PlaytimeDropWaiting or DebugSteamPhase.PromoGrantWaiting
                when _scenario == DebugSteamScenario.SlowSuccess:
                CompletePendingOperationSuccess("手动推进：模拟请求成功。", enforcePlaytimeRule: true);
                break;
            case DebugSteamPhase.PlaytimeDropWaiting or DebugSteamPhase.PromoGrantWaiting
                when _scenario is DebugSteamScenario.TimeoutVerifiedSuccess or DebugSteamScenario.TimeoutVerifiedFallback:
                BeginInventoryVerification("手动推进：进入库存复查。");
                break;
            case DebugSteamPhase.PlaytimeDropWaiting or DebugSteamPhase.PromoGrantWaiting
                when _scenario is DebugSteamScenario.DisconnectAfterSubmit or DebugSteamScenario.DisconnectRecoverSuccess:
                SetPhase(DebugSteamPhase.Unavailable, PlatformConnectionState.Unavailable,
                    "手动推进：连接中断，结果未知。");
                break;
            case DebugSteamPhase.Unavailable
                when _scenario is DebugSteamScenario.DisconnectAfterSubmit
                    or DebugSteamScenario.DisconnectRecoverSuccess:
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
        var isSelfReceiptGrant = IsNewcomerCompletionReceiptGrant(promoItemDefId, receiptItemDefId);
        if (!IsInventoryReady)
        {
            message = isSelfReceiptGrant
                ? "Steam Mock 当前不可用，新手进度回执尚未提交。"
                : "Steam Mock 当前不可用，LinkTree 领奖尚未提交。";
            AddEvent(message);
            return false;
        }
        if (_playtimeDropPending || _promoGrantPending)
        {
            message = "Steam Mock 已有库存写事务正在处理。";
            return false;
        }
        if (!isSelfReceiptGrant
            && !_linkTreeGrants.ContainsKey((promoItemDefId, receiptItemDefId)))
        {
            message = $"Steam Mock 未配置 Promo/回执：{promoItemDefId}/{receiptItemDefId}。";
            AddEvent(message);
            return false;
        }

        _promoGrantPending = true;
        _pendingPromoItemDefId = promoItemDefId;
        _pendingReceiptItemDefId = receiptItemDefId;
        SetPhase(DebugSteamPhase.PromoGrantWaiting, PlatformConnectionState.Ready,
            isSelfReceiptGrant
                ? "已提交模拟新手进度回执 AddPromoItem 请求。"
                : "已提交模拟 LinkTree AddPromoItem 请求。");
        message = isSelfReceiptGrant
            ? "Steam Mock 已接收新手进度回执请求。"
            : "Steam Mock 已接收 LinkTree 领奖请求。";
        PublishSnapshot();
        return true;
    }

    public bool TryTriggerPlaytimeDrop(int generatorItemDefId, out string message)
    {
        if (!IsMockActive)
        {
            if (_innerInventory != null)
                return _innerInventory.TryTriggerPlaytimeDrop(generatorItemDefId, out message);
            message = "当前平台不支持 Steam 库存。";
            return false;
        }
        if (!IsInventoryReady)
        {
            message = "Steam Mock 当前不可用，Generator 请求尚未提交。";
            AddEvent(message);
            return false;
        }
        if (_playtimeDropPending || _promoGrantPending)
        {
            message = "Steam Mock 已有库存写事务正在处理。";
            return false;
        }

        _playtimeDropPending = true;
        _pendingGeneratorItemDefId = generatorItemDefId;
        _pendingGeneratorWasActivation = _activatedGeneratorItemDefIds.Add(generatorItemDefId);
        if (_pendingGeneratorWasActivation)
            _playtimeAtLastGrantSeconds = _simulatedPlaytimeSeconds;
        SetPhase(DebugSteamPhase.PlaytimeDropWaiting, PlatformConnectionState.Ready,
            _pendingGeneratorWasActivation
                ? $"已提交模拟 TriggerItemDrop({generatorItemDefId}) 首次预热请求。"
                : $"已提交模拟 TriggerItemDrop({generatorItemDefId}) 奖励准备请求。");
        message = "Steam Mock 已接收盲盒奖励准备请求。";
        PublishSnapshot();
        return true;
    }

    public void RequestReconnect()
    {
        if (!IsMockActive)
            _innerRecoverable?.RequestReconnect();
        else if (_connectionState != PlatformConnectionState.Ready
                 || _inventoryTrustState != PlatformInventoryTrustState.Trusted)
            AddEvent("业务请求恢复连接；Mock 将遵循当前场景阶段。", publish: true);
    }

    public bool TryRequestClientRelaunch(out string message)
    {
        if (!IsMockActive && _innerRecoverable != null)
            return _innerRecoverable.TryRequestClientRelaunch(out message);

        message = "Steam Mock does not relaunch the Steam client.";
        return false;
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

    public PlatformAchievementUnlockResult UnlockAchievements(IEnumerable<string> names) => IsMockActive
        ? new PlatformAchievementUnlockResult(false, "Steam Mock 沙箱禁止上传成就。", [])
        : (_inner as IPlatformAchievementSyncOperations)?.UnlockAchievements(names)
          ?? new PlatformAchievementUnlockResult(false, "当前平台不支持成就写入。", []);

    public bool TrySetAchievementForTesting(string apiName, bool unlocked, out string message)
    {
        if (IsMockActive)
        {
            message = "Steam Mock 沙箱禁止修改真实成就。";
            return false;
        }
        if (_innerAchievementTest != null)
            return _innerAchievementTest.TrySetAchievementForTesting(apiName, unlocked, out message);
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
        }
        if (_innerRecoverable != null)
        {
            _innerRecoverable.ConnectionStateChanged -= OnInnerConnectionStateChanged;
            _innerRecoverable.InventoryTrustStateChanged -= OnInnerInventoryTrustStateChanged;
            _innerRecoverable.AccountIdentityConflictDetected -= OnInnerAccountIdentityConflictDetected;
        }
        if (_innerAchievementTest != null)
            _innerAchievementTest.StoreStatusChanged -= OnInnerStoreStatusChanged;
        _inner.Dispose();
    }

    private void BeginInventoryVerification(string message) =>
        SetPhase(DebugSteamPhase.InventoryVerification, PlatformConnectionState.InventorySyncing, message);

    private void CompleteNormalPendingOperation()
    {
        if (_playtimeDropPending)
        {
            CompletePlaytimeDropSuccess("正常响应完成。", enforcePlaytimeRule: true);
            return;
        }
        CompletePendingOperationSuccess("正常响应成功返回。", enforcePlaytimeRule: false);
    }

    private void CompletePendingOperationSuccess(string message, bool enforcePlaytimeRule = false)
    {
        if (_playtimeDropPending)
            CompletePlaytimeDropSuccess(message, enforcePlaytimeRule);
        else if (_promoGrantPending)
            CompletePromoGrantSuccess(message);
    }

    private void CompletePlaytimeDropSuccess(string message, bool enforcePlaytimeRule = false)
    {
        var eligibilityMessage = message;
        if (enforcePlaytimeRule && _pendingGeneratorWasActivation)
        {
            eligibilityMessage = "正常响应返回空结果：首次预热已登记 Generator 资格起点。";
            _playtimeDropPending = false;
            _pendingGeneratorWasActivation = false;
            SetPhase(DebugSteamPhase.Completed, PlatformConnectionState.Ready, eligibilityMessage);
            PlaytimeDropCompleted(new PlatformPlaytimeDropResult(
                _pendingGeneratorItemDefId,
                true,
                eligibilityMessage,
                []));
            PublishInventorySnapshot("Generator 首次预热空回执后的完整模拟库存。", true);
            return;
        }
        if (enforcePlaytimeRule && !TryConsumePlaytimeDropEligibility(out eligibilityMessage))
        {
            _playtimeDropPending = false;
            _pendingGeneratorWasActivation = false;
            SetPhase(DebugSteamPhase.Completed, PlatformConnectionState.Ready, eligibilityMessage);
            PlaytimeDropCompleted(new PlatformPlaytimeDropResult(
                _pendingGeneratorItemDefId,
                true,
                eligibilityMessage,
                []));
            PublishInventorySnapshot("盲盒奖励准备返回空回执后的完整模拟库存。", true);
            return;
        }

        if (enforcePlaytimeRule)
            message = eligibilityMessage;

        var changedItems = ApplySuccessfulPlaytimeDrop();
        _playtimeDropPending = false;
        _pendingGeneratorWasActivation = false;
        SetPhase(DebugSteamPhase.Completed, PlatformConnectionState.Ready, message);
        PlaytimeDropCompleted(new PlatformPlaytimeDropResult(
            _pendingGeneratorItemDefId,
            true,
            message,
            changedItems));
        PublishInventorySnapshot("盲盒奖励准备回调后的完整模拟库存。", true);
    }

    private bool TryConsumePlaytimeDropEligibility(out string reason)
    {
        var rule = _playtimeDropRule;
        if (rule == null || rule.GeneratorItemDefId != _pendingGeneratorItemDefId)
        {
            reason = "正常响应成功返回。";
            return true;
        }

        PruneDropWindow();
        var accumulated = Math.Max(0.0, _simulatedPlaytimeSeconds - _playtimeAtLastGrantSeconds);
        if (accumulated + 0.001 < rule.DropIntervalSeconds)
        {
            reason = $"正常响应返回空结果：模拟游玩时间尚差 {rule.DropIntervalSeconds - accumulated:0} 秒。";
            return false;
        }
        if (rule.DropWindowSeconds > 0.0
            && rule.DropMaxPerWindow > 0
            && _dropWindowGrantTimes.Count >= rule.DropMaxPerWindow)
        {
            var wait = Math.Max(
                0.0,
                rule.DropWindowSeconds - (_simulatedElapsedSeconds - _dropWindowGrantTimes.Peek()));
            reason = $"正常响应返回空结果：模拟掉落窗口已达 {rule.DropMaxPerWindow} 件，约 {wait:0} 秒后恢复。";
            return false;
        }

        _playtimeAtLastGrantSeconds = _simulatedPlaytimeSeconds;
        _dropWindowGrantTimes.Enqueue(_simulatedElapsedSeconds);
        reason = "正常响应满足模拟 Steam 掉落资格。";
        return true;
    }

    private void UpdateSimulationClock()
    {
        var now = NowSeconds();
        var delta = Math.Max(0.0, now - _simulationClockUpdatedAt);
        _simulationClockUpdatedAt = now;
        if (!IsMockActive || delta <= 0.0)
            return;
        _simulatedPlaytimeSeconds += delta;
        _simulatedElapsedSeconds += delta;
        PruneDropWindow();
    }

    private void PruneDropWindow()
    {
        var windowSeconds = _playtimeDropRule?.DropWindowSeconds ?? 0.0;
        if (windowSeconds <= 0.0)
        {
            _dropWindowGrantTimes.Clear();
            return;
        }
        while (_dropWindowGrantTimes.Count > 0
               && _simulatedElapsedSeconds - _dropWindowGrantTimes.Peek() >= windowSeconds)
        {
            _dropWindowGrantTimes.Dequeue();
        }
    }

    private void CompletePromoGrantSuccess(string message)
    {
        var operation = GetPendingPromoOperationName();
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
        PublishInventorySnapshot($"{operation}回调后的完整模拟库存。", true);
    }

    private void CompleteVerification(bool success)
    {
        var operation = _playtimeDropPending
            ? "盲盒奖励准备"
            : _promoGrantPending
                ? GetPendingPromoOperationName()
                : "库存请求";
        if (success && _playtimeDropPending)
            ApplySuccessfulPlaytimeDrop();
        else if (success && _promoGrantPending)
            ApplySuccessfulPromoGrant();
        _playtimeDropPending = false;
        _pendingGeneratorWasActivation = false;
        _promoGrantPending = false;
        SetPhase(DebugSteamPhase.Completed, PlatformConnectionState.Ready,
            success
                ? $"库存复查确认：{operation}已成功。"
                : $"库存复查完成：未发现本次{operation}请求产生的新物品。");
        PublishInventorySnapshot(_lastEvent, true, recordEvent: false);
    }

    private static bool IsNewcomerCompletionReceiptGrant(int promoItemDefId, int receiptItemDefId) =>
        promoItemDefId == receiptItemDefId
        && LubanData.Tables.TbBlindBoxSchedule.DataList.Any(schedule =>
            schedule.IsEnabled
            && schedule.SteamCompletionReceiptItemDefId == receiptItemDefId);

    private string GetPendingPromoOperationName() =>
        IsNewcomerCompletionReceiptGrant(_pendingPromoItemDefId, _pendingReceiptItemDefId)
            ? "新手进度回执"
            : "LinkTree 领奖";

    private IReadOnlyList<PlatformInventoryItem> ApplySuccessfulPlaytimeDrop()
    {
        if (_rewardItemDefId <= 0)
            return [];
        _nextRewardInstanceOffset++;
        _lastRewardInstanceId = RewardInstanceBase + _nextRewardInstanceOffset;
        var reward = new PlatformInventoryItem(_lastRewardInstanceId, _rewardItemDefId, 1);
        _mockItems.Add(reward);
        return [reward];
    }

    private IReadOnlyList<PlatformInventoryItem> ApplySuccessfulPromoGrant()
    {
        if (_pendingPromoItemDefId == _pendingReceiptItemDefId)
        {
            var selfReceipt = new PlatformInventoryItem(
                LinkTreeReceiptInstanceBase + checked((ulong)_pendingReceiptItemDefId),
                _pendingReceiptItemDefId,
                1);
            if (_mockItems.All(item => item.InstanceId != selfReceipt.InstanceId))
            {
                _mockItems.Add(selfReceipt);
                return [selfReceipt];
            }
            return [];
        }

        if (!_linkTreeGrants.TryGetValue((_pendingPromoItemDefId, _pendingReceiptItemDefId), out var grant))
            return [];
        var changedItems = new List<PlatformInventoryItem>();
        var receipt = new PlatformInventoryItem(
            LinkTreeReceiptInstanceBase + checked((ulong)grant.ReceiptItemDefId), grant.ReceiptItemDefId, 1);
        if (_mockItems.All(item => item.InstanceId != receipt.InstanceId))
        {
            _mockItems.Add(receipt);
            changedItems.Add(receipt);
        }
        if (grant.RewardItemDefId > 0)
        {
            var reward = new PlatformInventoryItem(
                LinkTreeRewardInstanceBase + checked((ulong)grant.ReceiptItemDefId), grant.RewardItemDefId, 1);
            if (_mockItems.All(item => item.InstanceId != reward.InstanceId))
            {
                _mockItems.Add(reward);
                changedItems.Add(reward);
            }
        }
        return changedItems;
    }

    private void PublishInventorySnapshot(string message, bool succeeded, bool recordEvent = true)
    {
        SetInventoryTrustState(
            succeeded ? PlatformInventoryTrustState.Trusted : PlatformInventoryTrustState.RevalidationRequired,
            message);
        if (recordEvent)
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
            ["scenario"] = _scenario.ToString(),
            ["phase"] = phase.ToString(),
            ["state"] = state.ToString(),
            ["event"] = message,
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
    private void OnInnerConnectionStateChanged(PlatformConnectionState value) { if (!IsMockActive) ConnectionStateChanged(value); }
    private void OnInnerInventoryTrustStateChanged(PlatformInventoryTrustState value) { if (!IsMockActive) InventoryTrustStateChanged(value); }
    private void OnInnerAccountIdentityConflictDetected(string expected, string actual) =>
        AccountIdentityConflictDetected(expected, actual);
    private void OnInnerStoreStatusChanged(string value) { if (!IsMockActive) StoreStatusChanged(value); }
}
#endif
