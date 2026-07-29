using System;
using System.Collections.Generic;
using Godot;

namespace LuckyDogRise;

public sealed class RecoveringSteamPlatformService : IGamePlatformService, IPlatformInventoryService,
    IRecoverablePlatformService, IPlatformAchievementSyncOperations, IPlatformAchievementTestOperations
{
    private const double InventoryTimeoutSeconds = 10.0;
    private static readonly double[] RetryDelaySeconds = [5.0, 15.0, 30.0, 60.0];

    private SteamGamePlatformService _session;
    private string _statusMessage = "Steam 尚未连接。";
    private double _nextRetryAtSeconds;
    private double _inventoryDeadlineSeconds;
    private double _promoGrantDeadlineSeconds;
    private double _exchangeDeadlineSeconds;
    private int _retryIndex;
    private bool _inventorySynchronizationRequested;
    private bool _disposed;

    public RecoveringSteamPlatformService()
    {
        TryConnect();
    }

    public event Action UserStatsReady = delegate { };
    public event Action<string> StoreStatusChanged = delegate { };
    public event Action<PlatformInventorySnapshot> InventorySnapshotChanged = delegate { };
    public event Action<PlatformPromoItemGrantResult> PromoItemGrantCompleted = delegate { };
    public event Action<PlatformInventoryExchangeResult> InventoryExchangeCompleted = delegate { };
    public event Action<PlatformConnectionState> ConnectionStateChanged = delegate { };

    public string ProviderName => "Steam";
    public string StatusMessage => _statusMessage;
    public bool IsAvailable => _session?.IsAvailable == true;
    public uint AppId => _session?.AppId ?? 0;
    public string PersonaName => _session?.PersonaName ?? string.Empty;
    public bool IsReadyForWrites => _session?.IsReadyForWrites == true;
    public bool IsInventoryReady => ConnectionState == PlatformConnectionState.Ready
        && _session?.IsInventoryReady == true;
    public bool IsPromoGrantPending => _session?.IsPromoGrantPending == true;
    public bool IsExchangePending => _session?.IsExchangePending == true;
    public IReadOnlyList<PlatformInventoryItem> InventoryItems => _session?.InventoryItems ?? [];
    public PlatformConnectionState ConnectionState { get; private set; } = PlatformConnectionState.Offline;

    public void RunCallbacks()
    {
        if (_disposed)
            return;

        _session?.RunCallbacks();
        var now = NowSeconds();
        if (_session != null && !_session.IsAvailable)
        {
            HandleDisconnected(_session.StatusMessage);
            return;
        }

        if (ConnectionState == PlatformConnectionState.InventorySyncing
            && _inventoryDeadlineSeconds > 0.0
            && now >= _inventoryDeadlineSeconds)
        {
            HandleInventoryFailure("Steam 库存同步超时。", publishSnapshot: true);
            return;
        }

        if (_promoGrantDeadlineSeconds > 0.0 && now >= _promoGrantDeadlineSeconds)
        {
            _promoGrantDeadlineSeconds = 0.0;
            SetConnectionState(PlatformConnectionState.InventorySyncing);
            _inventoryDeadlineSeconds = now + InventoryTimeoutSeconds;
            if (_session?.RecoverTimedOutPromoGrant() != true)
                HandleInventoryFailure("Steam 领奖请求超时，且库存复查无法启动。", publishSnapshot: true);
            return;
        }

        if (_exchangeDeadlineSeconds > 0.0 && now >= _exchangeDeadlineSeconds)
        {
            _exchangeDeadlineSeconds = 0.0;
            SetConnectionState(PlatformConnectionState.InventorySyncing);
            _inventoryDeadlineSeconds = now + InventoryTimeoutSeconds;
            if (_session?.RecoverTimedOutExchange() != true)
                HandleInventoryFailure("Steam 库存兑换超时，且库存复查无法启动。", publishSnapshot: true);
            return;
        }

        if (_session != null
            && ConnectionState == PlatformConnectionState.Unavailable
            && _inventorySynchronizationRequested
            && now >= _nextRetryAtSeconds)
        {
            BeginInventorySynchronization();
            return;
        }

        if (_session == null && now >= _nextRetryAtSeconds)
            TryConnect();
    }

    public void RequestReconnect()
    {
        if (_disposed)
            return;
        if (_session?.IsAvailable == true)
        {
            if (_inventorySynchronizationRequested
                && ConnectionState is PlatformConnectionState.Connecting or PlatformConnectionState.Unavailable)
                BeginInventorySynchronization();
            return;
        }

        _nextRetryAtSeconds = 0.0;
        TryConnect();
    }

    public void StartInventorySynchronization()
    {
        _inventorySynchronizationRequested = true;
        if (_session?.IsAvailable == true)
            BeginInventorySynchronization();
        else
            RequestReconnect();
    }

    public bool TryGrantPromoItem(int itemDefId, out string message)
    {
        if (!IsInventoryReady || _session == null)
        {
            message = "Steam 库存尚未连接。";
            return false;
        }

        var accepted = _session.TryGrantPromoItem(itemDefId, out message);
        if (accepted)
            _promoGrantDeadlineSeconds = NowSeconds() + InventoryTimeoutSeconds;
        return accepted;
    }

    public bool TryExchangeItem(
        ulong inputInstanceId,
        int inputItemDefId,
        int outputItemDefId,
        out string message)
    {
        if (!IsInventoryReady || _session == null)
        {
            message = "Steam 库存尚未连接。";
            return false;
        }

        var accepted = _session.TryExchangeItem(
            inputInstanceId,
            inputItemDefId,
            outputItemDefId,
            out message);
        if (accepted)
            _exchangeDeadlineSeconds = NowSeconds() + InventoryTimeoutSeconds;
        return accepted;
    }

    public bool OpenFriendsOverlay() => _session?.OpenFriendsOverlay() == true;

    public PlatformAchievementReadResult ReadAchievementStates(IEnumerable<string> achievementApiNames) =>
        _session?.ReadAchievementStates(achievementApiNames)
        ?? new PlatformAchievementReadResult(false, StatusMessage, Array.Empty<PlatformAchievementState>());

    public PlatformAchievementUnlockResult UnlockAchievements(IEnumerable<string> achievementApiNames) =>
        _session?.UnlockAchievements(achievementApiNames)
        ?? new PlatformAchievementUnlockResult(false, StatusMessage, Array.Empty<string>());

    public bool TrySetAchievementForTesting(string apiName, bool unlocked, out string message)
    {
        if (_session != null)
            return _session.TrySetAchievementForTesting(apiName, unlocked, out message);

        message = StatusMessage;
        return false;
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        DisposeSession();
        _disposed = true;
        SetConnectionState(PlatformConnectionState.Offline);
    }

    private void TryConnect()
    {
        if (_disposed || _session != null)
            return;

        SetConnectionState(PlatformConnectionState.Connecting);
        var runtime = new SteamworksRuntime();
        if (!runtime.TryInitialize())
        {
            _statusMessage = runtime.StatusMessage;
            runtime.Dispose();
            ScheduleRetry(_statusMessage);
            return;
        }

        _session = new SteamGamePlatformService(runtime);
        _session.UserStatsReady += OnUserStatsReady;
        _session.StoreStatusChanged += OnStoreStatusChanged;
        _session.InventorySnapshotChanged += OnInventorySnapshotChanged;
        _session.PromoItemGrantCompleted += OnPromoItemGrantCompleted;
        _session.InventoryExchangeCompleted += OnInventoryExchangeCompleted;
        _statusMessage = runtime.StatusMessage;
        GD.Print($"[PlatformRecovery] Steam connected. AppID={AppId}, Persona={PersonaName}");

        if (_inventorySynchronizationRequested)
            BeginInventorySynchronization();
    }

    private void BeginInventorySynchronization()
    {
        if (_session?.IsAvailable != true || ConnectionState == PlatformConnectionState.InventorySyncing)
            return;

        SetConnectionState(PlatformConnectionState.InventorySyncing);
        _inventoryDeadlineSeconds = NowSeconds() + InventoryTimeoutSeconds;
        _session.RestartInventorySynchronization();
    }

    private void OnInventorySnapshotChanged(PlatformInventorySnapshot snapshot)
    {
        _inventoryDeadlineSeconds = 0.0;
        if (snapshot.Succeeded)
        {
            _statusMessage = snapshot.Message;
            _retryIndex = 0;
            _nextRetryAtSeconds = 0.0;
            SetConnectionState(PlatformConnectionState.Ready);
        }
        else
        {
            HandleInventoryFailure(snapshot.Message, publishSnapshot: false);
        }

        InventorySnapshotChanged(snapshot);
    }

    private void OnPromoItemGrantCompleted(PlatformPromoItemGrantResult result)
    {
        _promoGrantDeadlineSeconds = 0.0;
        PromoItemGrantCompleted(result);
    }

    private void OnInventoryExchangeCompleted(PlatformInventoryExchangeResult result)
    {
        _exchangeDeadlineSeconds = 0.0;
        InventoryExchangeCompleted(result);
    }

    private void OnUserStatsReady() => UserStatsReady();
    private void OnStoreStatusChanged(string message) => StoreStatusChanged(message);

    private void HandleInventoryFailure(string message, bool publishSnapshot)
    {
        _session?.CancelPendingInventorySynchronization();
        _inventoryDeadlineSeconds = 0.0;
        _statusMessage = message;
        SetConnectionState(PlatformConnectionState.Unavailable);
        ScheduleRetry(message, preserveState: true);
        if (publishSnapshot)
            InventorySnapshotChanged(new PlatformInventorySnapshot(false, message, new HashSet<int>(), []));
    }

    private void HandleDisconnected(string message)
    {
        _statusMessage = string.IsNullOrWhiteSpace(message) ? "Steam 连接已中断。" : message;
        DisposeSession();
        ScheduleRetry(_statusMessage);
    }

    private void ScheduleRetry(string reason, bool preserveState = false)
    {
        var delay = RetryDelaySeconds[Math.Min(_retryIndex, RetryDelaySeconds.Length - 1)];
        _retryIndex = Math.Min(_retryIndex + 1, RetryDelaySeconds.Length - 1);
        _nextRetryAtSeconds = NowSeconds() + delay;
        if (!preserveState)
            SetConnectionState(PlatformConnectionState.Unavailable);
        GD.PushWarning($"[PlatformRecovery] {reason} Retrying in {delay:0}s.");
    }

    private void DisposeSession()
    {
        if (_session == null)
            return;

        _session.UserStatsReady -= OnUserStatsReady;
        _session.StoreStatusChanged -= OnStoreStatusChanged;
        _session.InventorySnapshotChanged -= OnInventorySnapshotChanged;
        _session.PromoItemGrantCompleted -= OnPromoItemGrantCompleted;
        _session.InventoryExchangeCompleted -= OnInventoryExchangeCompleted;
        _session.Dispose();
        _session = null;
        _inventoryDeadlineSeconds = 0.0;
        _promoGrantDeadlineSeconds = 0.0;
        _exchangeDeadlineSeconds = 0.0;
    }

    private void SetConnectionState(PlatformConnectionState state)
    {
        if (ConnectionState == state)
            return;

        ConnectionState = state;
        GD.Print($"[PlatformRecovery] State -> {state}");
        ConnectionStateChanged(state);
    }

    private static double NowSeconds() => Time.GetTicksMsec() / 1000.0;
}
