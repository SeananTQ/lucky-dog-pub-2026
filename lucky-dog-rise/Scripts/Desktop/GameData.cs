using Godot;
using DataTables;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace LuckyDogRise;

public partial class GameData : Node
{
    private sealed class PendingPlatformPlaytimeDrop
    {
        public int ScheduleId { get; init; }
        public int GrantCount { get; init; }
        public int GeneratorItemDefId { get; init; }
        public int OutputItemDefId { get; init; }
        public uint OutputQuantityBefore { get; init; }
    }

    private readonly record struct PlatformInventoryConsumptionReservation(
        uint QuantityBefore,
        uint ReservedQuantity);

    public const int StartingChips = 2800;
#if DEBUG
    public const int DebugAllItemsStartingChips = 36500;
#endif

    [Signal] public delegate void ChipsChangedEventHandler(int chips);
    [Signal] public delegate void HandResolvedEventHandler(EHandRank rank, int payout);
    [Signal] public delegate void NewHandStartedEventHandler();
    [Signal] public delegate void EquipmentChangedEventHandler();
    [Signal] public delegate void InventoryChangedEventHandler();
    [Signal] public delegate void BlindBoxStateChangedEventHandler();
    [Signal] public delegate void BlindBoxRewardReadyEventHandler();

    public void EmitHandResolved(EHandRank rank, int payout)
    {
        EmitSignal(SignalName.HandResolved, (int)rank, payout);
    }

    public void EmitNewHandStarted()
    {
        EmitSignal(SignalName.NewHandStarted);
    }

    public PlayerInventory Inventory { get; } = new();
    public int Chips { get; private set; } = StartingChips;
    public double TotalPlaySeconds { get; private set; }
    public PendingBlindBoxReward PendingBlindBoxReward { get; private set; }
    public PendingPlatformBlindBoxOpen PendingPlatformBlindBoxOpen { get; private set; }
    public int BetAmount => 50;
    public ProgressionManager Progression { get; } = new();
    public PlayerProgress PlayerProgress { get; private set; } = null!;

    private BlindBoxRuntimeState _blindBoxRuntimeState = new();
    private LuckyDealBuffState _luckyDealBuffState = new();
    public PendingLinkTreeClaim PendingLinkTreeClaim { get; private set; }
    private BlindBoxService _blindBoxService;
    private IPlatformInventoryService _platformInventoryService;
    private IRecoverablePlatformService _recoverablePlatformService;
    private PendingPlatformPlaytimeDrop _pendingPlatformPlaytimeDrop;
    private readonly Dictionary<ulong, PlatformInventoryConsumptionReservation>
        _platformInventoryConsumptionReservations = new();
    private readonly Dictionary<int, double> _playtimeDropRetryAtSeconds = new();
    private readonly Dictionary<int, int> _playtimeDropEmptyResultCounts = new();
    private double _nextPlatformPlaytimeDropAttemptAtSeconds;
    private bool _blindBoxFallbackEnabled = true;
#if DEBUG
    private bool _blindBoxLocalTestMode;
    private BlindBoxRuntimeState _blindBoxLocalTestRuntimeState = new();
    private int _blindBoxLocalTestVoucherCount;
    private int _blindBoxLocalTestSavedChips;
    private double _blindBoxLocalTestSavedTotalPlaySeconds;
    private Dictionary<int, int> _blindBoxLocalTestSavedInventoryCounts = new();
    private Dictionary<string, int> _blindBoxLocalTestSavedEquippedItems = new();
    private List<int> _blindBoxLocalTestSavedNewItemIds = new();
    private LuckyDealBuffState _blindBoxLocalTestSavedLuckyDealBuffState = new();
#endif
    private SettingsManager.SaveDataMode _saveDataMode;
    private bool _saveDirty;
    private double _saveTimer;
    private double _blindBoxTickTimer;
    private double _profileAutosaveTimer;
    private double _playerProgressSaveTimer;
    private bool _shutdownFlushCompleted;
    private const double SaveDebounceSeconds = 0.75;
    private const double ProfileAutosaveSeconds = 60.0;
    private const double PlayerProgressAutosaveSeconds = 60.0;
    private const double BlindBoxTickSeconds = 1.0;
    private const double SteamPlaytimeDropMinimumAttemptIntervalSeconds = 65.0;

    private BlindBoxRuntimeState ActiveBlindBoxRuntimeState
    {
        get
        {
#if DEBUG
            if (_blindBoxLocalTestMode)
                return _blindBoxLocalTestRuntimeState;
#endif
            return _blindBoxRuntimeState;
        }
    }

    public override void _Ready()
    {
        _blindBoxService = new BlindBoxService(this);
        _saveDataMode = SettingsManager.LoadSaveDataMode();
        PlayerProgress = new PlayerProgress();
        _profileAutosaveTimer = ProfileAutosaveSeconds;
        _playerProgressSaveTimer = PlayerProgressAutosaveSeconds;
        LoadDataForCurrentMode();
        Inventory.EquipmentChanged += OnInventoryEquipmentChanged;
        Inventory.InventoryChanged += OnInventoryChanged;
        EmitSignal(SignalName.ChipsChanged, Chips);
        EmitSignal(SignalName.EquipmentChanged);
        if (CanRecordPlayerProgress)
        {
            PlayerProgress.BackfillExternalInventory(Inventory);
            PlayerProgress.RecordAppLaunch();
        }
    }

    public override void _Process(double delta)
    {
        TotalPlaySeconds += delta;
        _blindBoxService.AdvanceScheduleClock(ActiveBlindBoxRuntimeState, delta);
        if (CanRecordPlayerProgress)
            PlayerProgress.RecordDuration("GameRuntimeSeconds", delta, PlayerProgressSource.Gameplay);
        _blindBoxTickTimer -= delta;
        if (_blindBoxTickTimer <= 0.0)
        {
            _blindBoxTickTimer = BlindBoxTickSeconds;
            MaintainLoopPresentation();
#if DEBUG
            if (!_blindBoxLocalTestMode)
#endif
            MaintainSteamPlaytimeDrops();
            EmitSignal(SignalName.BlindBoxStateChanged);
        }

        _profileAutosaveTimer -= delta;
        if (_profileAutosaveTimer <= 0.0)
        {
            _profileAutosaveTimer = ProfileAutosaveSeconds;
            QueueSaveIfUsingLocalSave();
        }

        if (_saveDirty)
        {
            _saveTimer -= delta;
            if (_saveTimer <= 0.0)
                FlushSave();
        }

        if (PlayerProgress.RequiresImmediateSave)
        {
            PlayerProgress.SaveIfDirty();
            _playerProgressSaveTimer = PlayerProgressAutosaveSeconds;
        }
        else if (PlayerProgress.IsDirty)
        {
            _playerProgressSaveTimer -= delta;
            if (_playerProgressSaveTimer <= 0.0)
            {
                PlayerProgress.SaveIfDirty();
                _playerProgressSaveTimer = PlayerProgressAutosaveSeconds;
            }
        }
    }

    public override void _ExitTree()
    {
#if DEBUG
        EndBlindBoxLocalTestMode(force: true, synchronizeInventory: false);
#endif
        UnbindPlatformInventoryService();
        FlushForShutdown();
    }

    public void BindPlatformInventoryService(IGamePlatformService platformService)
    {
        UnbindPlatformInventoryService();
        _platformInventoryService = platformService as IPlatformInventoryService;
        _recoverablePlatformService = platformService as IRecoverablePlatformService;
        if (_platformInventoryService == null)
            return;

        _platformInventoryService.InventorySnapshotChanged += OnPlatformInventorySnapshotChanged;
        _platformInventoryService.PlaytimeDropCompleted += OnPlatformPlaytimeDropCompleted;
        _platformInventoryService.InventoryExchangeCompleted += OnPlatformInventoryExchangeCompleted;
        _platformInventoryService.StartInventorySynchronization();

        if (_platformInventoryService.IsInventoryReady)
        {
            OnPlatformInventorySnapshotChanged(new PlatformInventorySnapshot(
                true,
                "Steam 库存已同步。",
                _platformInventoryService.InventoryItems.Select(item => item.ItemDefId).ToHashSet(),
                _platformInventoryService.InventoryItems.ToArray()));
        }
    }

    public void FlushForShutdown()
    {
        if (_shutdownFlushCompleted)
            return;

        var stopwatch = Stopwatch.StartNew();
        SaveImmediatelyIfUsingLocalSave();
        GD.Print($"[Shutdown] Profile save completed in {stopwatch.ElapsedMilliseconds} ms.");

        stopwatch.Restart();
        PlayerProgress?.FlushSession();
        GD.Print($"[Shutdown] Player progress save completed in {stopwatch.ElapsedMilliseconds} ms.");
        _shutdownFlushCompleted = true;
    }

    public void EquipItem(int itemId)
    {
        Inventory.Equip(itemId);
    }

    public void ToggleEquipItem(int itemId)
    {
        Inventory.ToggleEquip(itemId);
    }

    public void AddItem(int itemId, int count = 1, bool markNew = true, PlayerProgressSource source = PlayerProgressSource.Gameplay)
    {
        // Debug 发放只用于调整/录制，不应改变玩家当前的真实穿搭。
        var autoEquipNewOutfit = source != PlayerProgressSource.Debug
            && SettingsManager.LoadAutoEquipNewOutfits();
        Inventory.AddItem(itemId, count, markNew, autoEquipNewOutfit);
        if (CanRecordPlayerProgress && source != PlayerProgressSource.Debug)
        {
            var item = LubanData.Tables.TbItem.GetOrDefault(itemId);
            if (item != null)
                PlayerProgress.RecordExternalItemAcquired(item, count, source);
        }
        QueueSaveIfUsingLocalSave();
    }

    public BlindBox GetNextAvailableBlindBox()
    {
        MaintainLoopPresentation();
        return _blindBoxService.GetNextAvailableBox(
            ActiveBlindBoxRuntimeState,
            PendingBlindBoxReward);
    }

    public int GetBlindBoxDisplayCost(BlindBox box)
    {
        return _blindBoxService.GetDisplayCost(box);
    }

#if DEBUG
    public string GetBlindBoxDebugStatus()
    {
        MaintainLoopPresentation();
        var debugStatus = _blindBoxService.BuildDebugStatus(
            TotalPlaySeconds,
            ActiveBlindBoxRuntimeState,
            PendingBlindBoxReward);
        var hintState = GetBlindBoxHintState();
        var simulationText = _blindBoxLocalTestMode
            ? $"本地测试: 开启，虚拟装扮券 x{_blindBoxLocalTestVoucherCount}"
            : "本地测试: 关闭（使用真实 Steam/离线流程）";
        var statusText = $"{simulationText}\n{debugStatus}\n兜底: {(_blindBoxFallbackEnabled ? "开启" : "关闭")}" +
                         $"\n入口最终状态: {hintState.Status}";
        if (_blindBoxLocalTestMode)
            return statusText;
        if (hintState.Box == null || !UsesSteamInventoryExchange(hintState.Box))
            return statusText;

        var voucherItemDefId = hintState.Box.SteamOpenCostItemDefId;
        var voucherQuantity = GetPlatformInventoryQuantity(voucherItemDefId);
        return statusText + $"\nSteam券: ItemDef={voucherItemDefId}, Qty={voucherQuantity}";
    }

    public bool IsBlindBoxFallbackEnabled => _blindBoxFallbackEnabled;
    public bool IsBlindBoxLocalTestMode => _blindBoxLocalTestMode;
    public int BlindBoxLocalTestVoucherCount => _blindBoxLocalTestVoucherCount;

    public void SetBlindBoxFallbackEnabled(bool enabled)
    {
        if (_blindBoxFallbackEnabled == enabled)
            return;

        _blindBoxFallbackEnabled = enabled;
        GD.Print($"[BlindBox] Local Refreshment fallback {(enabled ? "enabled" : "disabled")} for Dev testing.");
        EmitSignal(SignalName.BlindBoxStateChanged);
    }

    public bool SetBlindBoxLocalTestMode(bool enabled)
    {
        if (BuildInfo.Channel != BuildChannel.Dev)
            return false;
        if (enabled == _blindBoxLocalTestMode)
            return true;
        return enabled ? BeginBlindBoxLocalTestMode() : EndBlindBoxLocalTestMode();
    }

    public void AdjustBlindBoxLocalTestVoucherCount(int delta)
    {
        if (!_blindBoxLocalTestMode || delta == 0)
            return;

        _blindBoxLocalTestVoucherCount = Math.Clamp(_blindBoxLocalTestVoucherCount + delta, 0, 99);
        EmitSignal(SignalName.BlindBoxStateChanged);
    }

    public bool AdvanceBlindBoxLocalTestPresentation()
    {
        if (!_blindBoxLocalTestMode || PendingBlindBoxReward != null)
            return false;

        MaintainBlindBoxLocalTestPresentation();
        _blindBoxLocalTestRuntimeState.ScheduleSeconds = Math.Max(
            _blindBoxLocalTestRuntimeState.ScheduleSeconds,
            _blindBoxLocalTestRuntimeState.NextLoopPresentationSeconds);
        MaintainBlindBoxLocalTestPresentation();
        EmitSignal(SignalName.BlindBoxStateChanged);
        return true;
    }

    public bool ClearBlindBoxLocalTestPresentation()
    {
        if (!_blindBoxLocalTestMode || PendingBlindBoxReward != null)
            return false;

        _blindBoxLocalTestRuntimeState.LockedLoopScheduleId = 0;
        _blindBoxLocalTestRuntimeState.LockedLoopBlindBoxId = 0;
        EmitSignal(SignalName.BlindBoxStateChanged);
        return true;
    }

    private bool BeginBlindBoxLocalTestMode()
    {
        if (_saveDataMode != SettingsManager.SaveDataMode.LocalSave)
        {
            GD.PushWarning("[BlindBox] Local test mode requires the local-save inventory source.");
            return false;
        }

        if (PendingBlindBoxReward != null
            || PendingPlatformBlindBoxOpen != null
            || _pendingPlatformPlaytimeDrop != null
            || _platformInventoryService?.IsPlaytimeDropPending == true
            || _platformInventoryService?.IsPromoGrantPending == true
            || _platformInventoryService?.IsExchangePending == true)
        {
            GD.PushWarning("[BlindBox] Cannot enter local test mode while a blind-box reward or Steam inventory write is pending.");
            return false;
        }

        FlushSave();
        if (!PlayerProgress.BeginDebugSimulation())
            return false;

        _blindBoxLocalTestSavedChips = Chips;
        _blindBoxLocalTestSavedTotalPlaySeconds = TotalPlaySeconds;
        _blindBoxLocalTestSavedInventoryCounts = Inventory.GetOwnedItemCounts();
        _blindBoxLocalTestSavedEquippedItems = Inventory.GetEquippedIdsByTypeName();
        _blindBoxLocalTestSavedNewItemIds = Inventory.GetNewItemIds().ToList();
        _blindBoxLocalTestSavedLuckyDealBuffState = new LuckyDealBuffState
        {
            RemainingHands = _luckyDealBuffState.RemainingHands,
            TriggerChance = _luckyDealBuffState.TriggerChance,
        };
        _blindBoxLocalTestRuntimeState = new BlindBoxRuntimeState
        {
            SequenceIndex = LubanData.Tables.TbBlindBoxSchedule.DataList.Count(schedule =>
                schedule.IsEnabled && !schedule.IsLoopTrack),
        };
        _blindBoxLocalTestVoucherCount = 1;
        _blindBoxLocalTestMode = true;

        MaintainBlindBoxLocalTestPresentation();
        _blindBoxLocalTestRuntimeState.ScheduleSeconds =
            _blindBoxLocalTestRuntimeState.NextLoopPresentationSeconds;
        MaintainBlindBoxLocalTestPresentation();
        _saveDirty = false;
        _saveTimer = 0.0;
        GD.Print("[BlindBox] Entered explicit in-memory local test mode; Steam blind-box writes and real save writes are disabled.");
        EmitSignal(SignalName.BlindBoxStateChanged);
        return true;
    }

    private bool EndBlindBoxLocalTestMode(bool force = false, bool synchronizeInventory = true)
    {
        if (!_blindBoxLocalTestMode)
            return true;
        if (!force && PendingBlindBoxReward != null)
        {
            GD.PushWarning("[BlindBox] Finish or claim the current simulated reward before leaving local test mode.");
            return false;
        }

        PendingBlindBoxReward = null;
        PendingPlatformBlindBoxOpen = null;
        Chips = _blindBoxLocalTestSavedChips;
        TotalPlaySeconds = _blindBoxLocalTestSavedTotalPlaySeconds;
        Inventory.LoadState(
            _blindBoxLocalTestSavedInventoryCounts,
            _blindBoxLocalTestSavedEquippedItems,
            _blindBoxLocalTestSavedNewItemIds,
            emitChanged: true);
        _luckyDealBuffState = _blindBoxLocalTestSavedLuckyDealBuffState;
        PlayerProgress.EndDebugSimulation();

        _blindBoxLocalTestMode = false;
        _blindBoxLocalTestRuntimeState = new BlindBoxRuntimeState();
        _blindBoxLocalTestVoucherCount = 0;
        _blindBoxLocalTestSavedInventoryCounts = new Dictionary<int, int>();
        _blindBoxLocalTestSavedEquippedItems = new Dictionary<string, int>();
        _blindBoxLocalTestSavedNewItemIds = new List<int>();
        _blindBoxLocalTestSavedLuckyDealBuffState = new LuckyDealBuffState();
        _saveDirty = false;
        _saveTimer = 0.0;
        EmitSignal(SignalName.ChipsChanged, Chips);
        EmitSignal(SignalName.BlindBoxStateChanged);
        if (synchronizeInventory)
            _platformInventoryService?.StartInventorySynchronization();
        GD.Print("[BlindBox] Left local test mode and restored the real local/Steam-backed state.");
        return true;
    }

    private void MaintainBlindBoxLocalTestPresentation()
    {
        if (!_blindBoxLocalTestMode
            || !_blindBoxService.TryGetLoopSchedule(out _, out var decorationBox)
            || decorationBox == null)
        {
            return;
        }

        var selectedBox = _blindBoxLocalTestVoucherCount > 0
            ? decorationBox
            : _blindBoxService.GetFallbackRefreshmentBox();
        if (selectedBox == null)
            return;

        _blindBoxService.MaintainLoopPresentation(
            _blindBoxLocalTestRuntimeState,
            PendingBlindBoxReward == null,
            selectedBox.Id);
    }

    private PendingBlindBoxReward TryOpenBlindBoxLocalTest()
    {
        if (!_blindBoxService.TryGetNextAvailable(
                _blindBoxLocalTestRuntimeState,
                out var schedule,
                out var box)
            || schedule == null
            || box == null)
        {
            return null;
        }

        BlindBoxOpenResult result;
        if (UsesSteamInventoryExchange(box) && _blindBoxLocalTestVoucherCount <= 0)
        {
            var fallbackBox = _blindBoxService.GetFallbackRefreshmentBox();
            result = fallbackBox == null
                ? null
                : _blindBoxService.TryOpenFallback(TotalPlaySeconds, schedule, fallbackBox);
        }
        else
        {
            result = _blindBoxService.TryOpenNext(TotalPlaySeconds, _blindBoxLocalTestRuntimeState);
            if (result != null && UsesSteamInventoryExchange(box))
                _blindBoxLocalTestVoucherCount--;
        }

        if (result == null)
            return null;

        GD.Print(
            $"[BlindBox] Local test opened Box={result.Box.Id}, Reward={result.Item.Id}; " +
            $"virtual decoration vouchers={_blindBoxLocalTestVoucherCount}.");
        return FinalizeLocalBlindBoxOpen(result);
    }
#endif

    public BlindBoxHintState GetBlindBoxHintState()
    {
        MaintainLoopPresentation();
        if (PendingPlatformBlindBoxOpen != null)
        {
            return new BlindBoxHintState
            {
                Status = BlindBoxHintStatus.Opening,
                Box = LubanData.Tables.TbBlindBox.GetOrDefault(PendingPlatformBlindBoxOpen.BlindBoxId),
                Cost = PendingPlatformBlindBoxOpen.ReservedChipCost,
            };
        }

        var state = _blindBoxService.GetHintState(
            ActiveBlindBoxRuntimeState,
            PendingBlindBoxReward);
#if DEBUG
        if (_blindBoxLocalTestMode)
            return state;
#endif
        if (state.Status == BlindBoxHintStatus.PendingReward)
            return state;

        if (state.Status == BlindBoxHintStatus.Waiting)
            return state;
        if (state.Status is not (BlindBoxHintStatus.Ready or BlindBoxHintStatus.NotEnoughChips)
            || state.Box == null)
        {
            return state;
        }

        if (_blindBoxService.TryGetNextAvailable(
                ActiveBlindBoxRuntimeState,
                out var currentSchedule,
                out var configuredBox)
            && currentSchedule != null
            && configuredBox != null)
        {
            var presentedBox = ResolveVoucherUpgradeBox(currentSchedule, configuredBox);
            if (presentedBox.Id != configuredBox.Id)
                return _blindBoxService.CreateReadyHintState(presentedBox);
        }

        if (!UsesSteamInventoryExchange(state.Box))
            return state;
        if (CanOpenPlatformBlindBox(state.Box))
            return state;
        if (_blindBoxFallbackEnabled && _blindBoxService.GetFallbackRefreshmentBox() is { } fallbackBox)
            return _blindBoxService.CreateReadyHintState(fallbackBox);
        if (_platformInventoryService?.IsInventoryReady != true)
        {
            var status = _recoverablePlatformService?.ConnectionState
                is PlatformConnectionState.Connecting or PlatformConnectionState.InventorySyncing
                ? BlindBoxHintStatus.PlatformSyncing
                : BlindBoxHintStatus.PlatformUnavailable;
            return new BlindBoxHintState
            {
                Status = status,
                Box = state.Box,
                Cost = state.Cost,
                RemainingSeconds = state.RemainingSeconds,
            };
        }

        return new BlindBoxHintState
        {
            Status = BlindBoxHintStatus.PlatformSyncing,
            Box = state.Box,
            Cost = state.Cost,
            RemainingSeconds = state.RemainingSeconds,
        };
    }

    public PendingBlindBoxReward TryOpenBlindBox()
    {
        MaintainLoopPresentation();
        if (PendingBlindBoxReward != null)
            return PendingBlindBoxReward;

        if (PendingPlatformBlindBoxOpen != null)
            return null;

#if DEBUG
        if (_blindBoxLocalTestMode)
            return TryOpenBlindBoxLocalTest();
#endif

        if (_blindBoxService.TryGetNextAvailable(_blindBoxRuntimeState, out var schedule, out var box)
            && schedule != null
            && box != null)
        {
            var presentedBox = ResolveVoucherUpgradeBox(schedule, box);
            if (UsesSteamInventoryExchange(presentedBox))
            {
                if (CanOpenPlatformBlindBox(presentedBox))
                {
                    if (BeginPlatformBlindBoxOpen(schedule, presentedBox))
                        return null;

                    if (_blindBoxFallbackEnabled)
                        return TryOpenFallbackBlindBox(schedule, presentedBox);
                    return null;
                }

                if (_blindBoxFallbackEnabled)
                    return TryOpenFallbackBlindBox(schedule, presentedBox);

                _recoverablePlatformService?.RequestReconnect();
                return null;
            }
        }

        var result = _blindBoxService.TryOpenNext(TotalPlaySeconds, _blindBoxRuntimeState);
        if (result == null)
            return null;

        return FinalizeLocalBlindBoxOpen(result);
    }

    private PendingBlindBoxReward TryOpenFallbackBlindBox(
        BlindBoxSchedule originalSchedule,
        BlindBox originalBox)
    {
        var fallbackBox = _blindBoxService.GetFallbackRefreshmentBox();
        if (fallbackBox == null)
        {
            GD.PushError("[BlindBox] Steam voucher is unavailable and no local Refreshment fallback is configured.");
            return null;
        }

        var result = _blindBoxService.TryOpenFallback(
            TotalPlaySeconds,
            originalSchedule,
            fallbackBox);
        if (result == null)
            return null;

        GD.Print(
            $"[BlindBox] Opened local fallback Box={fallbackBox.Id} for Schedule={originalSchedule.Id}; " +
            $"Steam Box={originalBox.Id}, ItemDef={originalBox.SteamOpenCostItemDefId}; no local debt created.");
        return FinalizeLocalBlindBoxOpen(result);
    }

    private PendingBlindBoxReward FinalizeLocalBlindBoxOpen(BlindBoxOpenResult result)
    {
        PendingBlindBoxReward = result.PendingReward;
        var clearedLoopPresentation = result.Schedule.IsLoopTrack
                                      && ActiveBlindBoxRuntimeState.LockedLoopBlindBoxId > 0;
        _blindBoxService.ConsumeOpenedSchedule(ActiveBlindBoxRuntimeState, result.Schedule);
        if (clearedLoopPresentation)
        {
            GD.Print(
                $"[BlindBoxTiming] Open cleared loop balloon Schedule={result.Schedule.Id}, " +
                $"Box={result.Box.Id}, Clock={ActiveBlindBoxRuntimeState.ScheduleSeconds:0.000}, " +
                $"NextPoint={ActiveBlindBoxRuntimeState.NextLoopPresentationSeconds:0.000}, " +
                $"RemainingReal={GetLoopPresentationRemainingRealSeconds():0.0}s.");
        }
        if (CanRecordPlayerProgress)
        {
            PlayerProgress.RecordBlindBoxOpened(PlayerProgressSource.BlindBox);
            PlayerProgress.RecordBlindBoxChipsSpent(GetBlindBoxDisplayCost(result.Box), PlayerProgressSource.BlindBox);
        }
        EmitSignal(SignalName.BlindBoxStateChanged);
        SaveImmediatelyIfUsingLocalSave();
        return PendingBlindBoxReward;
    }

    private bool BeginPlatformBlindBoxOpen(
        BlindBoxSchedule schedule,
        BlindBox box)
    {
        if (_platformInventoryService?.IsInventoryReady != true)
        {
            _recoverablePlatformService?.RequestReconnect();
            return false;
        }

        var cost = GetBlindBoxDisplayCost(box);
        if (Chips < cost)
            return false;

        var input = _platformInventoryService.InventoryItems.FirstOrDefault(item =>
            item.ItemDefId == box.SteamOpenCostItemDefId && item.Quantity > 0);
        if (input.InstanceId == 0)
        {
            GD.PushWarning($"[BlindBox] Steam inventory has no ItemDef={box.SteamOpenCostItemDefId} to open box {box.Id}.");
            return false;
        }

        PendingPlatformBlindBoxOpen = new PendingPlatformBlindBoxOpen
        {
            BlindBoxId = box.Id,
            ScheduleId = schedule.Id,
            InputItemDefId = box.SteamOpenCostItemDefId,
            InputInstanceId = input.InstanceId,
            ExchangeTargetItemDefId = box.SteamExchangeTargetItemDefId,
            ReservedChipCost = cost,
            InventoryQuantitiesBeforeExchange = _platformInventoryService.InventoryItems
                .GroupBy(item => item.InstanceId)
                .ToDictionary(
                    group => group.Key,
                    group => group.Aggregate(0u, (quantity, item) => checked(quantity + item.Quantity))),
            TotalPlaySeconds = TotalPlaySeconds,
            IsDeferredBacklog = false,
        };
        if (cost > 0)
            ModifyChips(-cost);
        SaveImmediatelyIfUsingLocalSave();

        if (!_platformInventoryService.TryExchangeItem(
                input.InstanceId,
                box.SteamOpenCostItemDefId,
                box.SteamExchangeTargetItemDefId,
                out var message))
        {
            CancelPendingPlatformBlindBoxOpen(refundReservedChips: true);
            GD.PushWarning($"[BlindBox] {message}");
            return false;
        }

        GD.Print($"[BlindBox] {message}");
        EmitSignal(SignalName.BlindBoxStateChanged);
        return true;
    }

    private bool CanOpenPlatformBlindBox(BlindBox box) =>
        _platformInventoryService?.IsInventoryReady == true
        && GetPlatformInventoryQuantity(box.SteamOpenCostItemDefId) > 0;

    private BlindBox ResolveVoucherUpgradeBox(
        BlindBoxSchedule schedule,
        BlindBox configuredBox)
    {
        if (_platformInventoryService?.IsInventoryReady != true)
            return configuredBox;

        foreach (var upgradeBoxId in schedule.VoucherUpgradeBlindBoxIds)
        {
            var upgradeBox = LubanData.Tables.TbBlindBox.GetOrDefault(upgradeBoxId);
            if (upgradeBox is { IsEnabled: true }
                && UsesSteamInventoryExchange(upgradeBox)
                && CanOpenPlatformBlindBox(upgradeBox))
            {
                return upgradeBox;
            }
        }

        return configuredBox;
    }

    private void MaintainLoopPresentation()
    {
#if DEBUG
        if (_blindBoxLocalTestMode)
        {
            MaintainBlindBoxLocalTestPresentation();
            return;
        }
#endif
        if (!_blindBoxService.TryGetLoopSchedule(out _, out var decorationBox)
            || decorationBox == null)
            return;

        var selectedBox = decorationBox;
        if (_blindBoxFallbackEnabled
            && !CanOpenPlatformBlindBox(decorationBox)
            && _blindBoxService.GetFallbackRefreshmentBox() is { } fallbackBox)
        {
            selectedBox = fallbackBox;
        }

        var canLockPresentation = PendingBlindBoxReward == null
                                  && PendingPlatformBlindBoxOpen == null;
        var previousLockedBoxId = _blindBoxRuntimeState.LockedLoopBlindBoxId;
        var previousNextPoint = _blindBoxRuntimeState.NextLoopPresentationSeconds;
        if (_blindBoxService.MaintainLoopPresentation(
                _blindBoxRuntimeState,
                canLockPresentation,
                selectedBox.Id))
        {
            if (previousLockedBoxId <= 0 && _blindBoxRuntimeState.LockedLoopBlindBoxId > 0)
            {
                GD.Print(
                    $"[BlindBoxTiming] Locked loop balloon Schedule={_blindBoxRuntimeState.LockedLoopScheduleId}, " +
                    $"Box={_blindBoxRuntimeState.LockedLoopBlindBoxId}, " +
                    $"Clock={_blindBoxRuntimeState.ScheduleSeconds:0.000}, " +
                    $"DuePoint={previousNextPoint:0.000}, " +
                    $"NextPoint={_blindBoxRuntimeState.NextLoopPresentationSeconds:0.000}.");
            }
            QueueSaveIfUsingLocalSave();
        }
    }

    private double GetLoopPresentationRemainingRealSeconds()
    {
        var multiplier = LubanData.Tables.TbGameDevelopConfig.DataList.FirstOrDefault()
            ?.BlindBoxWaitDurationMultiplier ?? 1f;
        return Math.Max(
            0.0,
            (_blindBoxRuntimeState.NextLoopPresentationSeconds
             - _blindBoxRuntimeState.ScheduleSeconds) * Math.Max(1f, multiplier));
    }

    private void MaintainSteamPlaytimeDrops()
    {
        if (PendingPlatformBlindBoxOpen != null
            || _platformInventoryService?.IsInventoryReady != true
            || _pendingPlatformPlaytimeDrop != null
            || _platformInventoryService.IsPlaytimeDropPending
            || _platformInventoryService.IsPromoGrantPending
            || _platformInventoryService.IsExchangePending)
        {
            return;
        }

        if (_blindBoxService.PrepareLoopDropRetryAfterInventoryVerification(_blindBoxRuntimeState))
        {
            GD.Print("[BlindBox] Full inventory is ready; retrying the unresolved recurring playtime drop.");
            SaveImmediatelyIfUsingLocalSave();
        }

        ReconcileCurrentSteamPlaytimeDropWithInventory();
        if (!_blindBoxService.TryGetNextSteamPlaytimeDrop(
                _blindBoxRuntimeState,
                out var schedule,
                out var box,
                out var grantCount)
            || schedule == null
            || box == null
            || !UsesSteamInventoryExchange(box))
        {
            return;
        }

        var now = Time.GetTicksMsec() / 1000.0;
        // Steam evaluates playtime drops at minute granularity and rate-limits more frequent
        // TriggerItemDrop calls. This throttle is shared by all schedule generators.
        if (now < _nextPlatformPlaytimeDropAttemptAtSeconds)
            return;
        if (_playtimeDropRetryAtSeconds.TryGetValue(schedule.Id, out var retryAt) && now < retryAt)
            return;

        var outputQuantity = GetPlatformInventoryQuantity(box.SteamOpenCostItemDefId);
        if (!_platformInventoryService.TryTriggerPlaytimeDrop(
                schedule.SteamPlaytimeGeneratorItemDefId,
                box.SteamOpenCostItemDefId,
                out var message))
        {
            _playtimeDropRetryAtSeconds[schedule.Id] = now + 5.0;
            _recoverablePlatformService?.RequestReconnect();
            return;
        }

        _nextPlatformPlaytimeDropAttemptAtSeconds = now + SteamPlaytimeDropMinimumAttemptIntervalSeconds;

        _pendingPlatformPlaytimeDrop = new PendingPlatformPlaytimeDrop
        {
            ScheduleId = schedule.Id,
            GrantCount = grantCount,
            GeneratorItemDefId = schedule.SteamPlaytimeGeneratorItemDefId,
            OutputItemDefId = box.SteamOpenCostItemDefId,
            OutputQuantityBefore = outputQuantity,
        };
        _blindBoxService.BeginSteamPlaytimeDrop(_blindBoxRuntimeState, schedule);
        SaveImmediatelyIfUsingLocalSave();
        GD.Print($"[BlindBox] Schedule={schedule.Id}, Grant={grantCount}: {message}");
        EmitSignal(SignalName.BlindBoxStateChanged);
    }

    private void OnPlatformPlaytimeDropCompleted(PlatformPlaytimeDropResult result)
    {
        var pending = _pendingPlatformPlaytimeDrop;
        if (pending == null
            || pending.GeneratorItemDefId != result.GeneratorItemDefId
            || pending.OutputItemDefId != result.OutputItemDefId)
        {
            return;
        }

        _pendingPlatformPlaytimeDrop = null;
        GD.Print($"[BlindBox] {result.Message}");
        var schedule = LubanData.Tables.TbBlindBoxSchedule.GetOrDefault(pending.ScheduleId);
        if (result.Succeeded && schedule is { IsLoopTrack: true })
        {
            CompleteSteamPlaytimeDrop(pending.ScheduleId, pending.GrantCount);
            if (result.ItemGranted
                && GetPlatformInventoryQuantity(pending.OutputItemDefId) <= pending.OutputQuantityBefore)
            {
                _platformInventoryService?.StartInventorySynchronization();
            }
            return;
        }

        if (result.Succeeded && result.ItemGranted)
        {
            CompleteSteamPlaytimeDrop(pending.ScheduleId, pending.GrantCount);
            if (GetPlatformInventoryQuantity(pending.OutputItemDefId) <= pending.OutputQuantityBefore)
                _platformInventoryService?.StartInventorySynchronization();
            return;
        }

        if (result.Succeeded
            && schedule != null
            && _blindBoxService.IsSteamPlaytimeDropDue(
                _blindBoxRuntimeState,
                schedule,
                pending.GrantCount))
        {
            var emptyResultCount = _playtimeDropEmptyResultCounts.GetValueOrDefault(pending.ScheduleId) + 1;
            _playtimeDropEmptyResultCounts[pending.ScheduleId] = emptyResultCount;
            _playtimeDropRetryAtSeconds[pending.ScheduleId] =
                Time.GetTicksMsec() / 1000.0 + SteamPlaytimeDropMinimumAttemptIntervalSeconds;
            GD.Print(
                $"[BlindBox] PlaytimeGenerator {pending.GeneratorItemDefId} returned no item " +
                $"after local due time (retry {emptyResultCount}); keeping Schedule {pending.ScheduleId} pending.");
            EmitSignal(SignalName.BlindBoxStateChanged);
            return;
        }

        var retryDelay = schedule == null
            ? 5.0
            : _blindBoxService.GetSteamPlaytimeDropRetryDelaySeconds(
                _blindBoxRuntimeState,
                schedule,
                pending.GrantCount);
        _playtimeDropRetryAtSeconds[pending.ScheduleId] = Time.GetTicksMsec() / 1000.0 + retryDelay;
        if (!result.Succeeded)
            _recoverablePlatformService?.RequestReconnect();
        EmitSignal(SignalName.BlindBoxStateChanged);
    }

    private void ReconcileCurrentSteamPlaytimeDropWithInventory()
    {
        if (_platformInventoryService == null
            || !_blindBoxService.TryGetNextPresentationCandidate(
                _blindBoxRuntimeState,
                out var currentSchedule,
                out var currentBox)
            || currentSchedule == null
            || currentBox == null
            || currentSchedule.IsLoopTrack
            || !UsesSteamInventoryExchange(currentBox))
        {
            return;
        }

        if (GetPlatformInventoryQuantity(currentBox.SteamOpenCostItemDefId) > 0)
        {
            if (!_blindBoxService.CompleteCurrentSteamPlaytimeDropFromInventory(
                    _blindBoxRuntimeState,
                    currentSchedule))
            {
                return;
            }

            _playtimeDropRetryAtSeconds.Remove(currentSchedule.Id);
            _playtimeDropEmptyResultCounts.Remove(currentSchedule.Id);
            GD.Print(
                $"[BlindBox] Steam inventory already contains ItemDef={currentBox.SteamOpenCostItemDefId}; " +
                $"Schedule {currentSchedule.Id} no longer needs another TriggerItemDrop request.");
            SaveImmediatelyIfUsingLocalSave();
            EmitSignal(SignalName.BlindBoxStateChanged);
            return;
        }

        if (_blindBoxService.ReopenCurrentSteamPlaytimeDrop(_blindBoxRuntimeState, currentSchedule))
        {
            GD.Print(
                $"[BlindBox] Current Schedule {currentSchedule.Id} requires " +
                $"ItemDef={currentBox.SteamOpenCostItemDefId}, but the voucher is missing; " +
                "restored its PlaytimeGenerator request.");
            SaveImmediatelyIfUsingLocalSave();
            EmitSignal(SignalName.BlindBoxStateChanged);
        }
    }

    private void OnPlatformInventoryExchangeCompleted(PlatformInventoryExchangeResult result)
    {
        var transaction = PendingPlatformBlindBoxOpen;
        if (transaction == null
            || transaction.InputInstanceId != result.InputInstanceId
            || transaction.InputItemDefId != result.InputItemDefId
            || transaction.ExchangeTargetItemDefId != result.OutputItemDefId)
        {
            return;
        }

        if (!result.Succeeded)
        {
            GD.PushWarning($"[BlindBox] {result.Message}");
            CancelPendingPlatformBlindBoxOpen(refundReservedChips: true);
            TryStartFallbackAfterPlatformOpenFailure(transaction);
            return;
        }

        ReserveConsumedPlatformInput(transaction);

        var reward = result.ChangedItems.FirstOrDefault(item =>
            item.Quantity > 0 && IsValidPlatformReward(transaction, item.ItemDefId));
        if (reward.InstanceId == 0 || !TryFinalizePlatformBlindBoxOpen(reward))
        {
            GD.Print("[BlindBox] Steam exchange completed; waiting for full inventory verification.");
            _platformInventoryService?.StartInventorySynchronization();
        }
    }

    private void OnPlatformInventorySnapshotChanged(PlatformInventorySnapshot snapshot)
    {
        if (!snapshot.Succeeded)
            return;

        ReconcilePlatformConsumptionReservations(snapshot.Items);

#if DEBUG
        if (_blindBoxLocalTestMode)
            return;
#endif

        ReconcilePendingPlaytimeDrop(snapshot.Items);

        if (PendingPlatformBlindBoxOpen is not { } transaction)
        {
            ReconcilePlatformInventory(snapshot.Items);
            return;
        }

        var reward = snapshot.Items.FirstOrDefault(item =>
        {
            if (item.Quantity == 0 || !IsValidPlatformReward(transaction, item.ItemDefId))
                return false;
            transaction.InventoryQuantitiesBeforeExchange.TryGetValue(item.InstanceId, out var previousQuantity);
            return item.Quantity > previousQuantity;
        });
        if (reward.InstanceId != 0 && TryFinalizePlatformBlindBoxOpen(reward))
        {
            ReconcilePlatformInventory(snapshot.Items);
            return;
        }

        transaction.InventoryQuantitiesBeforeExchange.TryGetValue(
            transaction.InputInstanceId,
            out var inputQuantityBefore);
        var inputQuantityNow = snapshot.Items
            .Where(item => item.InstanceId == transaction.InputInstanceId)
            .Sum(item => item.Quantity);
        if (inputQuantityNow < inputQuantityBefore)
        {
            GD.Print("[BlindBox] Steam cost item was consumed; waiting for the generated reward to appear in inventory.");
            ReconcilePlatformInventory(snapshot.Items);
            return;
        }

        if (_platformInventoryService?.IsExchangePending != true)
        {
            GD.Print("[BlindBox] Steam inventory confirms the exchange did not consume its input; reopening is available.");
            CancelPendingPlatformBlindBoxOpen(refundReservedChips: true);
            TryStartFallbackAfterPlatformOpenFailure(transaction);
        }
        ReconcilePlatformInventory(snapshot.Items);
    }

    private void TryStartFallbackAfterPlatformOpenFailure(PendingPlatformBlindBoxOpen transaction)
    {
        if (!_blindBoxFallbackEnabled)
            return;

        var schedule = LubanData.Tables.TbBlindBoxSchedule.GetOrDefault(transaction.ScheduleId);
        var box = LubanData.Tables.TbBlindBox.GetOrDefault(transaction.BlindBoxId);
        if (schedule == null || box == null || TryOpenFallbackBlindBox(schedule, box) == null)
            return;

        EmitSignal(SignalName.BlindBoxRewardReady);
    }

    private bool TryFinalizePlatformBlindBoxOpen(PlatformInventoryItem reward)
    {
        var transaction = PendingPlatformBlindBoxOpen;
        if (transaction == null)
            return false;

        var box = LubanData.Tables.TbBlindBox.GetOrDefault(transaction.BlindBoxId);
        var schedule = LubanData.Tables.TbBlindBoxSchedule.GetOrDefault(transaction.ScheduleId);
        var item = FindLocalItem(reward.ItemDefId);
        if (box == null || schedule == null || item == null)
        {
            GD.PushError(
                $"[BlindBox] Steam reward cannot be resolved. Box={transaction.BlindBoxId}, " +
                $"Schedule={transaction.ScheduleId}, ItemDef={reward.ItemDefId}.");
            return false;
        }

        var result = _blindBoxService.CreateOpenResult(
            transaction.TotalPlaySeconds,
            schedule,
            box,
            item,
            transaction.ReservedChipCost);
        if (result == null)
            return false;

        PendingBlindBoxReward = result.PendingReward;
        PendingBlindBoxReward.IsPlatformInventoryReward = true;
        PendingPlatformBlindBoxOpen = null;
        _blindBoxService.ConsumeOpenedSchedule(_blindBoxRuntimeState, schedule);
        if (CanRecordPlayerProgress)
        {
            PlayerProgress.RecordBlindBoxOpened(PlayerProgressSource.BlindBox);
            PlayerProgress.RecordBlindBoxChipsSpent(GetBlindBoxDisplayCost(box), PlayerProgressSource.BlindBox);
        }
        SaveImmediatelyIfUsingLocalSave();
        EmitSignal(SignalName.BlindBoxStateChanged);
        EmitSignal(SignalName.BlindBoxRewardReady);
        GD.Print(
            $"[BlindBox] Steam exchange confirmed reward ItemDef={reward.ItemDefId}, " +
            $"Instance={reward.InstanceId}, local Item={item.Id}.");
        return true;
    }

    private void CancelPendingPlatformBlindBoxOpen(bool refundReservedChips)
    {
        if (PendingPlatformBlindBoxOpen == null)
            return;

        var transaction = PendingPlatformBlindBoxOpen;
        var refund = refundReservedChips ? transaction.ReservedChipCost : 0;
        PendingPlatformBlindBoxOpen = null;
        _platformInventoryConsumptionReservations.Remove(transaction.InputInstanceId);
        if (refund > 0)
            ModifyChips(refund);
        EmitSignal(SignalName.BlindBoxStateChanged);
        SaveImmediatelyIfUsingLocalSave();
    }

    private void UnbindPlatformInventoryService()
    {
        if (_platformInventoryService != null)
        {
            _platformInventoryService.InventorySnapshotChanged -= OnPlatformInventorySnapshotChanged;
            _platformInventoryService.PlaytimeDropCompleted -= OnPlatformPlaytimeDropCompleted;
            _platformInventoryService.InventoryExchangeCompleted -= OnPlatformInventoryExchangeCompleted;
        }
        _platformInventoryService = null;
        _recoverablePlatformService = null;
        _pendingPlatformPlaytimeDrop = null;
        _platformInventoryConsumptionReservations.Clear();
    }

    private void ReconcilePendingPlaytimeDrop(IReadOnlyList<PlatformInventoryItem> platformItems)
    {
        var pending = _pendingPlatformPlaytimeDrop;
        if (pending == null || _platformInventoryService?.IsPlaytimeDropPending == true)
            return;

        var quantityNow = checked((uint)platformItems
            .Where(item => item.ItemDefId == pending.OutputItemDefId)
            .Sum(item => (long)item.Quantity));
        _pendingPlatformPlaytimeDrop = null;
        if (quantityNow > pending.OutputQuantityBefore)
        {
            CompleteSteamPlaytimeDrop(pending.ScheduleId, pending.GrantCount);
            return;
        }

        var schedule = LubanData.Tables.TbBlindBoxSchedule.GetOrDefault(pending.ScheduleId);
        if (schedule is { IsLoopTrack: true })
        {
            _blindBoxService.PrepareLoopDropRetryAfterInventoryVerification(_blindBoxRuntimeState);
            _playtimeDropRetryAtSeconds[pending.ScheduleId] =
                Time.GetTicksMsec() / 1000.0 + SteamPlaytimeDropMinimumAttemptIntervalSeconds;
            SaveImmediatelyIfUsingLocalSave();
            return;
        }

        _playtimeDropRetryAtSeconds[pending.ScheduleId] = Time.GetTicksMsec() / 1000.0 + 5.0;
    }

    private uint GetPlatformInventoryQuantity(int itemDefId) => checked((uint)(_platformInventoryService?.InventoryItems
        .Where(item => item.ItemDefId == itemDefId)
        .Sum(item =>
        {
            var reservedQuantity = _platformInventoryConsumptionReservations
                .GetValueOrDefault(item.InstanceId)
                .ReservedQuantity;
            return (long)(item.Quantity > reservedQuantity
                ? item.Quantity - reservedQuantity
                : 0u);
        }) ?? 0L));

    private void ReserveConsumedPlatformInput(PendingPlatformBlindBoxOpen transaction)
    {
        transaction.InventoryQuantitiesBeforeExchange.TryGetValue(
            transaction.InputInstanceId,
            out var quantityBefore);
        if (quantityBefore == 0)
            return;

        _platformInventoryConsumptionReservations[transaction.InputInstanceId] =
            new PlatformInventoryConsumptionReservation(quantityBefore, 1);
        GD.Print(
            $"[BlindBox] Reserved consumed Steam voucher ItemDef={transaction.InputItemDefId}, " +
            $"Instance={transaction.InputInstanceId} until full inventory confirmation.");
    }

    private void ReconcilePlatformConsumptionReservations(
        IReadOnlyList<PlatformInventoryItem> platformItems)
    {
        foreach (var (instanceId, reservation) in _platformInventoryConsumptionReservations.ToArray())
        {
            var quantityNow = platformItems
                .Where(item => item.InstanceId == instanceId)
                .Sum(item => (long)item.Quantity);
            if (quantityNow + reservation.ReservedQuantity > reservation.QuantityBefore)
                continue;

            _platformInventoryConsumptionReservations.Remove(instanceId);
            GD.Print(
                $"[BlindBox] Full inventory confirmed consumed Steam voucher Instance={instanceId}; " +
                "released the local reservation.");
        }
    }

    private void CompleteSteamPlaytimeDrop(int scheduleId, int grantCount)
    {
        _blindBoxService.CompleteSteamPlaytimeDrop(_blindBoxRuntimeState, scheduleId, grantCount);
        _playtimeDropRetryAtSeconds.Remove(scheduleId);
        _playtimeDropEmptyResultCounts.Remove(scheduleId);
        SaveImmediatelyIfUsingLocalSave();
        EmitSignal(SignalName.BlindBoxStateChanged);
    }

    private static bool UsesSteamInventoryExchange(BlindBox box) =>
        box.IsPlatformInventoryRequired
        && box.SteamOpenCostItemDefId > 0
        && box.SteamExchangeTargetItemDefId > 0;

    private static Item FindLocalItem(int steamItemDefId) =>
        LubanData.Tables.TbItem.DataList.FirstOrDefault(item => item.SteamItemDefId == steamItemDefId);

    private bool IsValidPlatformReward(PendingPlatformBlindBoxOpen transaction, int steamItemDefId)
    {
        var box = LubanData.Tables.TbBlindBox.GetOrDefault(transaction.BlindBoxId);
        var item = FindLocalItem(steamItemDefId);
        return box != null && item != null && _blindBoxService.IsRewardCandidate(box, item);
    }

    private void ReconcilePlatformInventory(IReadOnlyList<PlatformInventoryItem> platformItems)
    {
        if (!IsUsingLocalSave)
            return;

        var mappedItems = LubanData.Tables.TbItem.DataList
            // Initial items are permanent local entitlements. Steam owns the quantity of
            // earned decorations only; local Refreshment rewards must survive inventory sync.
            .Where(item => item.SteamItemDefId > 0
                           && item.AcquisitionType != EAcquisitionType.Initial
                           && item.AcquisitionType != EAcquisitionType.RefreshmentBlindBox)
            .ToArray();
        if (mappedItems.Length == 0)
            return;

        var countsByItemDef = platformItems
            .Where(item => item.Quantity > 0)
            .GroupBy(item => item.ItemDefId)
            .ToDictionary(
                group => group.Key,
                group => checked((int)group.Sum(item => (long)item.Quantity)));
        if (PendingBlindBoxReward is { IsPlatformInventoryReward: true } pendingReward)
        {
            var pendingItem = LubanData.Tables.TbItem.GetOrDefault(pendingReward.ItemId);
            if (pendingItem?.SteamItemDefId > 0
                && countsByItemDef.TryGetValue(pendingItem.SteamItemDefId, out var count))
            {
                countsByItemDef[pendingItem.SteamItemDefId] = Math.Max(0, count - 1);
            }
        }

        var ownedCounts = Inventory.GetOwnedItemCounts();
        var newItemIds = Inventory.GetNewItemIds().ToHashSet();
        var changed = false;
        foreach (var item in mappedItems)
        {
            var desiredCount = countsByItemDef.GetValueOrDefault(item.SteamItemDefId);
            var previousCount = ownedCounts.GetValueOrDefault(item.Id);
            if (desiredCount == previousCount)
                continue;

            changed = true;
            if (desiredCount > 0)
            {
                ownedCounts[item.Id] = desiredCount;
                if (desiredCount > previousCount)
                    newItemIds.Add(item.Id);
            }
            else
            {
                ownedCounts.Remove(item.Id);
                newItemIds.Remove(item.Id);
            }
        }

        if (!changed)
            return;

        Inventory.LoadState(
            ownedCounts,
            Inventory.GetEquippedIdsByTypeName(),
            newItemIds,
            emitChanged: true);
        SaveImmediatelyIfUsingLocalSave();
    }

    public void ClaimPendingBlindBoxReward()
    {
        if (PendingBlindBoxReward == null || !PendingBlindBoxReward.RewardShown)
            return;

        var itemId = PendingBlindBoxReward.ItemId;
        var scheduleId = PendingBlindBoxReward.ScheduleId;
        PendingBlindBoxReward = null;
        AddItem(itemId, count: 1, markNew: true, source: PlayerProgressSource.BlindBox);
        _blindBoxService.CompleteClaimedSchedule(ActiveBlindBoxRuntimeState, scheduleId);
        if (CanRecordPlayerProgress)
            PlayerProgress.RecordBlindBoxRewardClaimed(PlayerProgressSource.BlindBox);
        EmitSignal(SignalName.BlindBoxStateChanged);
        QueueSaveIfUsingLocalSave();
    }

    public void SetPendingBlindBoxRevealStep(int step)
    {
        if (PendingBlindBoxReward == null)
            return;

        PendingBlindBoxReward.RevealStep = Mathf.Max(0, step);
        SaveImmediatelyIfUsingLocalSave();
    }

    public void MarkPendingBlindBoxRewardShown()
    {
        if (PendingBlindBoxReward == null)
            return;

        PendingBlindBoxReward.RewardShown = true;
        EmitSignal(SignalName.BlindBoxStateChanged);
        SaveImmediatelyIfUsingLocalSave();
    }

    public void ModifyChips(int delta)
    {
        Chips += delta;
#if DEBUG
        if (!_blindBoxLocalTestMode)
#endif
        Progression.UpdateHighScore(Chips);
        EmitSignal(SignalName.ChipsChanged, Chips);
        QueueSaveIfUsingLocalSave();
    }

    public bool TryBeginLinkTreeClaim(
        int linkTreeId,
        int steamClaimBundleItemDefId,
        int steamReceiptItemDefId)
    {
#if DEBUG
        if (_blindBoxLocalTestMode)
        {
            GD.PushWarning("[LinkTree] Real claims are disabled while blind-box local test mode is active.");
            return false;
        }
#endif
        if (linkTreeId <= 0 || steamClaimBundleItemDefId <= 0 || steamReceiptItemDefId <= 0)
            return false;
        if (PendingLinkTreeClaim != null)
            return PendingLinkTreeClaim.LinkTreeId == linkTreeId
                && PendingLinkTreeClaim.SteamClaimBundleItemDefId == steamClaimBundleItemDefId
                && PendingLinkTreeClaim.SteamReceiptItemDefId == steamReceiptItemDefId;

        PendingLinkTreeClaim = new PendingLinkTreeClaim
        {
            LinkTreeId = linkTreeId,
            SteamClaimBundleItemDefId = steamClaimBundleItemDefId,
            SteamReceiptItemDefId = steamReceiptItemDefId,
        };
        SaveImmediatelyIfUsingLocalSave();
        return true;
    }

    public void ClearPendingLinkTreeClaim()
    {
        if (PendingLinkTreeClaim == null)
            return;

        PendingLinkTreeClaim = null;
        SaveImmediatelyIfUsingLocalSave();
    }

    public bool CanAffordBet => Chips >= BetAmount;
    public int LuckyDealRemainingHands => _luckyDealBuffState.RemainingHands;
    public bool IsUsingLocalSave => _saveDataMode == SettingsManager.SaveDataMode.LocalSave;

    public void RecordTypingInput(int count)
    {
        if (CanRecordPlayerProgress)
            PlayerProgress.RecordInputChips(count, PlayerProgressSource.Gameplay);
    }

    public void RecordDesktopModeSeconds(double delta, bool visible)
    {
        if (visible && CanRecordPlayerProgress)
            PlayerProgress.RecordDuration("DesktopModeSeconds", delta, PlayerProgressSource.Gameplay);
    }

    public void RecordPokerModeSeconds(double delta)
    {
        if (CanRecordPlayerProgress)
            PlayerProgress.RecordDuration("PokerModeSeconds", delta, PlayerProgressSource.Gameplay);
    }

    public void RecordPokerHandStarted(int bet, PlayerProgressSource source)
    {
        if (CanRecordPlayerProgress)
            PlayerProgress.RecordPokerHandStarted(bet, source);
    }

    public void RecordPokerHandResolved(EHandRank rank, int payout, bool askedDogHint, PlayerProgressSource source)
    {
        if (CanRecordPlayerProgress)
            PlayerProgress.RecordPokerHandResolved(rank, payout, askedDogHint, source);
    }

    public void RecordPokerPayoutCollected(int payout, PlayerProgressSource source)
    {
        if (CanRecordPlayerProgress)
            PlayerProgress.RecordPokerPayoutCollected(payout, source);
    }

    public void RecordPlayerProgressEvent(string eventKey, PlayerProgressSource source = PlayerProgressSource.Gameplay)
    {
        if (CanRecordPlayerProgress)
            PlayerProgress.RecordFirstEvent(eventKey, source);
    }

#if DEBUG
    public void ResetPlayerProgress() => PlayerProgress.Reset();
    public void SetPlayerProgressDebugMultiplier(int multiplier) => PlayerProgress.SetDebugMultiplier(multiplier);
    public string GetPlayerProgressDebugStatus() =>
        $"Progress file: {PlayerProgress.AbsoluteSavePath}\n" +
        $"Unlocked: {PlayerProgress.UnlockedAchievementApiNames.Count}\n" +
        $"Platform sync: {(PlayerProgress.IsPlatformSyncAllowed ? "Enabled" : "Paused by DEBUG sandbox/multiplier")}\n" +
        $"Platform-suppressed: {PlayerProgress.PlatformSuppressedAchievementCount}\n" +
        $"Statistics: {PlayerProgress.Statistics.Count}";
#endif

    /// <summary>供未来消耗品和当前 Debug 共用的幸运 Buff 发放接口。</summary>
    public void GrantLuckyDealBuff(int turns, float triggerChance)
    {
        if (turns <= 0)
            return;

        _luckyDealBuffState.RemainingHands = checked(_luckyDealBuffState.RemainingHands + turns);
        _luckyDealBuffState.TriggerChance = Mathf.Clamp(triggerChance, 0f, 1f);
        QueueSaveIfUsingLocalSave();
    }

    /// <summary>在一局成功下注时消耗一次 Buff；未触发幸运牌局同样消耗。</summary>
    public bool TryConsumeLuckyDealBuff(out float triggerChance)
    {
        triggerChance = 0f;
        if (_luckyDealBuffState.RemainingHands <= 0)
            return false;

        _luckyDealBuffState.RemainingHands--;
        triggerChance = _luckyDealBuffState.TriggerChance;
        QueueSaveIfUsingLocalSave();
        return true;
    }

#if DEBUG
    public void ResetToStart()
    {
        EndBlindBoxLocalTestMode(force: true);
        ResetPlaytimeDropTransientState();
        Chips = DebugAllItemsStartingChips;
        TotalPlaySeconds = 0;
        PendingBlindBoxReward = null;
        PendingPlatformBlindBoxOpen = null;
        PendingLinkTreeClaim = null;
        _blindBoxRuntimeState = new BlindBoxRuntimeState();
        _luckyDealBuffState = new LuckyDealBuffState();
        Progression.Reset();
        EmitSignal(SignalName.ChipsChanged, Chips);
        EmitSignal(SignalName.BlindBoxStateChanged);
        QueueSaveIfUsingLocalSave();
    }
#endif

    public void SetSaveDataMode(SettingsManager.SaveDataMode mode)
    {
#if !DEBUG
        mode = SettingsManager.SaveDataMode.LocalSave;
#endif
        if (_saveDataMode == mode)
            return;

#if DEBUG
        EndBlindBoxLocalTestMode(force: true);
#endif
        FlushSave();
        _saveDataMode = mode;
        SettingsManager.SaveSaveDataMode(mode);
        LoadDataForCurrentMode();
        if (CanRecordPlayerProgress)
            PlayerProgress.BackfillExternalInventory(Inventory);
        EmitSignal(SignalName.ChipsChanged, Chips);
        EmitSignal(SignalName.EquipmentChanged);
    }

    public void ResetLocalSave()
    {
#if DEBUG
        EndBlindBoxLocalTestMode(force: true);
#endif
        FlushSave();
        ResetPlaytimeDropTransientState();
        var profile = SaveManager.ResetLocalSave();
        if (_saveDataMode == SettingsManager.SaveDataMode.LocalSave)
        {
            Chips = profile.Chips;
            TotalPlaySeconds = profile.TotalPlaySeconds;
            LoadBlindBoxState(profile);
            LoadLuckyDealBuffState(profile);
            Inventory.LoadState(profile.OwnedItemCounts, profile.EquippedItemIdsByType, profile.NewItemIds, emitChanged: false);
            EmitSignal(SignalName.ChipsChanged, Chips);
            EmitSignal(SignalName.EquipmentChanged);
            EmitSignal(SignalName.BlindBoxStateChanged);
        }
    }

    private void LoadDataForCurrentMode()
    {
        ResetPlaytimeDropTransientState();
#if !DEBUG
        var profile = SaveManager.LoadOrCreate();
        Chips = profile.Chips;
        TotalPlaySeconds = profile.TotalPlaySeconds;
        LoadBlindBoxState(profile);
        LoadLuckyDealBuffState(profile);
        Inventory.LoadState(profile.OwnedItemCounts, profile.EquippedItemIdsByType, profile.NewItemIds, emitChanged: false);
        QueueSaveIfUsingLocalSave();
#else
        if (_saveDataMode == SettingsManager.SaveDataMode.LocalSave)
        {
            var profile = SaveManager.LoadOrCreate();
            Chips = profile.Chips;
            TotalPlaySeconds = profile.TotalPlaySeconds;
            LoadBlindBoxState(profile);
            LoadLuckyDealBuffState(profile);
            Inventory.LoadState(profile.OwnedItemCounts, profile.EquippedItemIdsByType, profile.NewItemIds, emitChanged: false);
            QueueSaveIfUsingLocalSave();
            return;
        }

        Chips = DebugAllItemsStartingChips;
        TotalPlaySeconds = 0;
        PendingBlindBoxReward = null;
        PendingPlatformBlindBoxOpen = null;
        PendingLinkTreeClaim = null;
        _blindBoxRuntimeState = new BlindBoxRuntimeState();
        _luckyDealBuffState = new LuckyDealBuffState();
        Inventory.ResetToDebugAllItems(emitChanged: false);
        _saveDirty = false;
        _saveTimer = 0.0;
#endif
    }

    private void ResetPlaytimeDropTransientState()
    {
        _pendingPlatformPlaytimeDrop = null;
        _playtimeDropRetryAtSeconds.Clear();
        _playtimeDropEmptyResultCounts.Clear();
    }

    private void OnInventoryEquipmentChanged()
    {
        EmitSignal(SignalName.EquipmentChanged);
        QueueSaveIfUsingLocalSave();
    }

    private void OnInventoryChanged()
    {
        EmitSignal(SignalName.InventoryChanged);
        QueueSaveIfUsingLocalSave();
    }

    private void QueueSaveIfUsingLocalSave()
    {
#if DEBUG
        if (_blindBoxLocalTestMode)
            return;
#endif
        if (_saveDataMode != SettingsManager.SaveDataMode.LocalSave)
            return;

        _saveDirty = true;
        _saveTimer = SaveDebounceSeconds;
    }

    private bool CanRecordPlayerProgress
    {
        get
        {
#if DEBUG
            return _saveDataMode == SettingsManager.SaveDataMode.LocalSave;
#else
            return true;
#endif
        }
    }

    public void SaveImmediatelyIfUsingLocalSave()
    {
        QueueSaveIfUsingLocalSave();
        FlushSave();
    }

    private void FlushSave()
    {
#if DEBUG
        if (_blindBoxLocalTestMode)
            return;
#endif
        if (!_saveDirty || _saveDataMode != SettingsManager.SaveDataMode.LocalSave)
            return;

        SaveManager.Save(new SaveProfile
        {
            Chips = Chips,
            TotalPlaySeconds = TotalPlaySeconds,
            OwnedItemIds = Inventory.GetOwnedIds().ToList(),
            OwnedItemCounts = Inventory.GetOwnedItemCounts(),
            EquippedItemIdsByType = Inventory.GetEquippedIdsByTypeName(),
            NewItemIds = Inventory.GetNewItemIds().ToList(),
            BlindBoxRuntimeState = _blindBoxRuntimeState,
            PendingBlindBoxReward = PendingBlindBoxReward,
            PendingPlatformBlindBoxOpen = PendingPlatformBlindBoxOpen,
            PendingLinkTreeClaim = PendingLinkTreeClaim,
            LuckyDealBuffState = _luckyDealBuffState,
        });
        _saveDirty = false;
        _profileAutosaveTimer = ProfileAutosaveSeconds;
    }

    private void LoadBlindBoxState(SaveProfile profile)
    {
        _blindBoxRuntimeState = profile.BlindBoxRuntimeState ?? new BlindBoxRuntimeState();
        PendingBlindBoxReward = profile.PendingBlindBoxReward;
        PendingPlatformBlindBoxOpen = profile.PendingPlatformBlindBoxOpen;
        PendingLinkTreeClaim = profile.PendingLinkTreeClaim;
        GD.Print(
            $"[BlindBoxTiming] Loaded Clock={_blindBoxRuntimeState.ScheduleSeconds:0.000}, " +
            $"NextPoint={_blindBoxRuntimeState.NextLoopPresentationSeconds:0.000}, " +
            $"LockedSchedule={_blindBoxRuntimeState.LockedLoopScheduleId}, " +
            $"LockedBox={_blindBoxRuntimeState.LockedLoopBlindBoxId}, " +
            $"RemainingReal={GetLoopPresentationRemainingRealSeconds():0.0}s.");
    }

    private void LoadLuckyDealBuffState(SaveProfile profile)
    {
        _luckyDealBuffState = profile.LuckyDealBuffState ?? new LuckyDealBuffState();
    }
}
