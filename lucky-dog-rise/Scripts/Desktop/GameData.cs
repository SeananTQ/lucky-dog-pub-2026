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

    private sealed class BlindBoxPresentationDecision
    {
        public required int ScheduleId { get; init; }
        public required int ConfiguredBoxId { get; init; }
        public required BlindBox PresentedBox { get; init; }
        public required BlindBoxPaymentSource PaymentSource { get; init; }
    }

    private readonly record struct PlatformInventoryConsumptionReservation(
        uint QuantityBefore,
        uint ReservedQuantity);

    public const int StartingChips = 500;
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
    [Signal] public delegate void RefreshmentStateChangedEventHandler();
    [Signal] public delegate void RefreshmentSelectionRefusedEventHandler();

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
    private RefreshmentRuntimeState _refreshmentRuntimeState = new();
    public PendingLinkTreeClaim PendingLinkTreeClaim { get; private set; }
    private readonly HashSet<int> _appliedLinkTreeRewardIds = new();
    public bool LinkTreeRewardLedgerInitialized { get; private set; } = true;
    private BlindBoxService _blindBoxService;
    private IPlatformInventoryService _platformInventoryService;
    private IRecoverablePlatformService _recoverablePlatformService;
    private PendingPlatformPlaytimeDrop _pendingPlatformPlaytimeDrop;
    private BlindBoxPresentationDecision _blindBoxPresentationDecision;
    private readonly Dictionary<ulong, PlatformInventoryConsumptionReservation>
        _platformInventoryConsumptionReservations = new();
    private readonly Dictionary<int, double> _playtimeDropRetryAtSeconds = new();
    private readonly Dictionary<int, int> _playtimeDropEmptyResultCounts = new();
    private double _nextPlatformPlaytimeDropAttemptAtSeconds;
    private bool _blindBoxFallbackEnabled = true;
#if DEBUG
    private bool _blindBoxLocalTestMode;
    private bool _steamMockSimulationActive;
    private BlindBoxRuntimeState _blindBoxLocalTestRuntimeState = new();
    private int _blindBoxLocalTestVoucherCount;
    private int _blindBoxLocalTestSavedChips;
    private double _blindBoxLocalTestSavedTotalPlaySeconds;
    private Dictionary<int, int> _blindBoxLocalTestSavedInventoryCounts = new();
    private Dictionary<string, int> _blindBoxLocalTestSavedEquippedItems = new();
    private List<int> _blindBoxLocalTestSavedNewItemIds = new();
    private LuckyDealBuffState _blindBoxLocalTestSavedLuckyDealBuffState = new();
    private RefreshmentRuntimeState _blindBoxLocalTestSavedRefreshmentRuntimeState = new();
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
        ValidateRefreshmentConfigs();
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
        EmitSignal(SignalName.RefreshmentStateChanged);
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
        var item = LubanData.Tables.TbItem.GetOrDefault(itemId);
        if (item != null && item.ItemType == EItemType.Refreshment)
        {
            TrySelectTableRefreshment(itemId);
            return;
        }

        Inventory.ToggleEquip(itemId);
    }

    public void AddItem(int itemId, int count = 1, bool markNew = true, PlayerProgressSource source = PlayerProgressSource.Gameplay)
    {
        var item = LubanData.Tables.TbItem.GetOrDefault(itemId);
        // Debug 发放只用于调整/录制，不应改变玩家当前的真实穿搭。
        var autoEquipNewOutfit = source != PlayerProgressSource.Debug
            && item is not { ItemType: EItemType.Refreshment }
            && SettingsManager.LoadAutoEquipNewOutfits();
        Inventory.AddItem(itemId, count, markNew, autoEquipNewOutfit);
        if (CanRecordPlayerProgress && source != PlayerProgressSource.Debug)
        {
            if (item != null)
                PlayerProgress.RecordExternalItemAcquired(item, count, source);
        }

        if (item is { ItemType: EItemType.Refreshment }
            && _refreshmentRuntimeState.Status == TableRefreshmentStatus.Empty
            && _luckyDealBuffState.RemainingHands <= 0)
            SetTableRefreshment(item.Id);
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
        var simulationText = _steamMockSimulationActive
            ? "Steam Mock: 开启（模拟券以上方面板为准）"
            : _blindBoxLocalTestMode
                ? $"本地测试: 开启，虚拟装扮券 x{_blindBoxLocalTestVoucherCount}"
                : "本地测试: 关闭（使用真实 Steam/离线流程）";
        var statusText = $"{simulationText}\n{debugStatus}\n兜底: {(_blindBoxFallbackEnabled ? "开启" : "关闭")}" +
                         $"\n入口最终状态: {hintState.Status}";
        if (_blindBoxLocalTestMode)
            return statusText;

        if (_blindBoxService.TryGetLoopSchedule(out _, out var loopBox)
            && loopBox != null
            && UsesSteamInventoryExchange(loopBox))
        {
            var loopVoucherQuantity = GetPlatformInventoryQuantity(loopBox.SteamOpenCostItemDefId);
            var loopVoucherLimit = GetLoopSteamVoucherInventoryLimit();
            var limitText = loopVoucherLimit > 0
                ? $"{loopVoucherQuantity}/{loopVoucherLimit}" +
                  (IsLoopSteamVoucherInventoryLimitReached(loopVoucherQuantity, loopVoucherLimit)
                      ? "，投放暂停"
                      : "，可继续投放")
                : $"{loopVoucherQuantity}/无限制";
            statusText += $"\n循环Steam券: ItemDef={loopBox.SteamOpenCostItemDefId}, Qty={limitText}";
        }

        if (hintState.Box == null || !UsesSteamInventoryExchange(hintState.Box))
            return statusText;

        var voucherItemDefId = hintState.Box.SteamOpenCostItemDefId;
        var voucherQuantity = GetPlatformInventoryQuantity(voucherItemDefId);
        return statusText + $"\nSteam券: ItemDef={voucherItemDefId}, Qty={voucherQuantity}";
    }

    public bool IsBlindBoxFallbackEnabled => _blindBoxFallbackEnabled;
    public bool IsBlindBoxLocalTestMode => _blindBoxLocalTestMode && !_steamMockSimulationActive;
    public bool IsSteamMockSimulationActive => _steamMockSimulationActive;
    public int BlindBoxLocalTestVoucherCount => _steamMockSimulationActive ? 0 : _blindBoxLocalTestVoucherCount;

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
        if (_steamMockSimulationActive)
            return false;
        if (enabled == _blindBoxLocalTestMode)
            return true;
        return enabled ? BeginBlindBoxLocalTestMode() : EndBlindBoxLocalTestMode();
    }

    public bool SetSteamMockSimulationActive(bool enabled)
    {
        if (BuildInfo.Channel != BuildChannel.Dev)
            return false;
        if (enabled == _steamMockSimulationActive)
            return true;

        if (!enabled)
        {
            _steamMockSimulationActive = false;
            return EndBlindBoxLocalTestMode(force: true);
        }

        if (_blindBoxLocalTestMode || !BeginBlindBoxLocalTestMode())
            return false;
        _steamMockSimulationActive = true;
        _blindBoxLocalTestRuntimeState.LockedLoopScheduleId = 0;
        _blindBoxLocalTestRuntimeState.LockedLoopBlindBoxId = 0;
        _blindBoxPresentationDecision = null;
        ConfigureSteamMockBlindBox();
        MaintainLoopPresentation();
        EmitSignal(SignalName.BlindBoxStateChanged);
        DiagnosticLog.Record("steam_mock_sandbox_entered");
        return true;
    }

    public bool ResetSteamMockSimulation()
    {
        if (!_steamMockSimulationActive)
            return false;

        PendingBlindBoxReward = null;
        PendingPlatformBlindBoxOpen = null;
        _platformInventoryConsumptionReservations.Clear();
        Chips = _blindBoxLocalTestSavedChips;
        TotalPlaySeconds = _blindBoxLocalTestSavedTotalPlaySeconds;
        Inventory.LoadState(
            _blindBoxLocalTestSavedInventoryCounts,
            _blindBoxLocalTestSavedEquippedItems,
            _blindBoxLocalTestSavedNewItemIds,
            emitChanged: true);
        _luckyDealBuffState = new LuckyDealBuffState
        {
            RemainingHands = _blindBoxLocalTestSavedLuckyDealBuffState.RemainingHands,
            TriggerChance = _blindBoxLocalTestSavedLuckyDealBuffState.TriggerChance,
            LuckyDealMode = _blindBoxLocalTestSavedLuckyDealBuffState.LuckyDealMode,
        };
        _refreshmentRuntimeState = CloneRefreshmentRuntimeState(_blindBoxLocalTestSavedRefreshmentRuntimeState);
        _blindBoxLocalTestRuntimeState = new BlindBoxRuntimeState
        {
            SequenceIndex = LubanData.Tables.TbBlindBoxSchedule.DataList.Count(schedule =>
                schedule.IsEnabled && !schedule.IsLoopTrack),
        };
        _blindBoxPresentationDecision = null;
        ConfigureSteamMockBlindBox();
        MaintainLoopPresentation();
        EmitSignal(SignalName.ChipsChanged, Chips);
        EmitSignal(SignalName.BlindBoxStateChanged);
        EmitSignal(SignalName.RefreshmentStateChanged);
        DiagnosticLog.Record("steam_mock_sandbox_reset");
        return true;
    }

    private void ConfigureSteamMockBlindBox()
    {
        if (_platformInventoryService is not IDebugSteamMockController controller
            || !_blindBoxService.TryGetLoopSchedule(out _, out var decorationBox)
            || decorationBox == null)
        {
            return;
        }

        var reward = LubanData.Tables.TbItem.DataList
            .Where(item => item.SteamItemDefId > 0 && _blindBoxService.IsRewardCandidate(decorationBox, item))
            .OrderBy(item => item.Id)
            .FirstOrDefault();
        if (reward == null)
        {
            GD.PushError($"[Steam Mock] Blind box {decorationBox.Id} has no valid Steam reward candidate.");
            return;
        }
        controller.ConfigureBlindBox(decorationBox.SteamOpenCostItemDefId, reward.SteamItemDefId);
    }

    public void AdjustBlindBoxLocalTestVoucherCount(int delta)
    {
        if (!_blindBoxLocalTestMode || _steamMockSimulationActive || delta == 0)
            return;

        _blindBoxLocalTestVoucherCount = Math.Clamp(_blindBoxLocalTestVoucherCount + delta, 0, 99);
        EmitSignal(SignalName.BlindBoxStateChanged);
    }

    public bool AdvanceBlindBoxLocalTestPresentation()
    {
        if (!_blindBoxLocalTestMode || PendingBlindBoxReward != null)
            return false;

        MaintainLoopPresentation();
        _blindBoxLocalTestRuntimeState.ScheduleSeconds = Math.Max(
            _blindBoxLocalTestRuntimeState.ScheduleSeconds,
            _blindBoxLocalTestRuntimeState.NextLoopPresentationSeconds);
        MaintainLoopPresentation();
        EmitSignal(SignalName.BlindBoxStateChanged);
        return true;
    }

    public bool ClearBlindBoxLocalTestPresentation()
    {
        if (!_blindBoxLocalTestMode || PendingBlindBoxReward != null)
            return false;

        _blindBoxLocalTestRuntimeState.LockedLoopScheduleId = 0;
        _blindBoxLocalTestRuntimeState.LockedLoopBlindBoxId = 0;
        _blindBoxPresentationDecision = null;
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
            || PendingLinkTreeClaim != null
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
            LuckyDealMode = _luckyDealBuffState.LuckyDealMode,
        };
        _blindBoxLocalTestSavedRefreshmentRuntimeState = CloneRefreshmentRuntimeState(_refreshmentRuntimeState);
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
        _refreshmentRuntimeState = _blindBoxLocalTestSavedRefreshmentRuntimeState;
        PlayerProgress.EndDebugSimulation();

        _blindBoxLocalTestMode = false;
        _steamMockSimulationActive = false;
        _blindBoxLocalTestRuntimeState = new BlindBoxRuntimeState();
        _blindBoxLocalTestVoucherCount = 0;
        _blindBoxLocalTestSavedInventoryCounts = new Dictionary<int, int>();
        _blindBoxLocalTestSavedEquippedItems = new Dictionary<string, int>();
        _blindBoxLocalTestSavedNewItemIds = new List<int>();
        _blindBoxLocalTestSavedLuckyDealBuffState = new LuckyDealBuffState();
        _blindBoxLocalTestSavedRefreshmentRuntimeState = new RefreshmentRuntimeState();
        _saveDirty = false;
        _saveTimer = 0.0;
        EmitSignal(SignalName.ChipsChanged, Chips);
        EmitSignal(SignalName.BlindBoxStateChanged);
        EmitSignal(SignalName.RefreshmentStateChanged);
        if (synchronizeInventory)
            _platformInventoryService?.StartInventorySynchronization();
        GD.Print("[BlindBox] Left local test mode and restored the real local/Steam-backed state.");
        return true;
    }

    private void MaintainBlindBoxLocalTestPresentation()
    {
        if (!_blindBoxLocalTestMode
            || _steamMockSimulationActive
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
        if (_blindBoxLocalTestMode && !_steamMockSimulationActive)
        {
            if (state.Status is (BlindBoxHintStatus.Ready or BlindBoxHintStatus.NotEnoughChips)
                && _blindBoxService.TryGetNextAvailable(
                    ActiveBlindBoxRuntimeState,
                    out var localTestSchedule,
                    out var localTestBox)
                && localTestSchedule != null
                && localTestBox != null
                && TryGetOrCreateBlindBoxPresentationDecision(
                    localTestSchedule,
                    localTestBox,
                    out var localTestPresentation,
                    canOpenPlatformOverride: _blindBoxLocalTestVoucherCount > 0,
                    voucherQuantityOverride: checked((uint)_blindBoxLocalTestVoucherCount)))
            {
                return WithBlindBoxPaymentSource(
                    _blindBoxService.CreateReadyHintState(localTestPresentation.PresentedBox),
                    localTestPresentation.PaymentSource);
            }

            return state;
        }
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
            && configuredBox != null
            && TryGetOrCreateBlindBoxPresentationDecision(
                currentSchedule,
                configuredBox,
                out var presentation))
        {
            return WithBlindBoxPaymentSource(
                _blindBoxService.CreateReadyHintState(presentation.PresentedBox),
                presentation.PaymentSource);
        }

        if (!UsesSteamInventoryExchange(state.Box))
            return state;
        if (CanOpenPlatformBlindBox(state.Box))
            return WithBlindBoxPaymentSource(state, BlindBoxPaymentSource.SteamVoucher);
        if (_blindBoxFallbackEnabled && _blindBoxService.GetFallbackRefreshmentBox() is { } fallbackBox)
            return WithBlindBoxPaymentSource(
                _blindBoxService.CreateReadyHintState(fallbackBox),
                BlindBoxPaymentSource.SteamFallback);
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

    private static BlindBoxHintState WithBlindBoxPaymentSource(
        BlindBoxHintState state,
        BlindBoxPaymentSource paymentSource)
    {
        return new BlindBoxHintState
        {
            Status = state.Status,
            Box = state.Box,
            Cost = state.Cost,
            RemainingSeconds = state.RemainingSeconds,
            PaymentSource = paymentSource,
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
        if (_blindBoxLocalTestMode && !_steamMockSimulationActive)
            return TryOpenBlindBoxLocalTest();
#endif

        if (_blindBoxService.TryGetNextAvailable(ActiveBlindBoxRuntimeState, out var schedule, out var box)
            && schedule != null
            && box != null)
        {
            if (TryGetOrCreateBlindBoxPresentationDecision(schedule, box, out var presentation))
            {
                if (presentation.PaymentSource == BlindBoxPaymentSource.SteamFallback
                    && presentation.PresentedBox.Id != box.Id)
                {
                    return TryOpenFallbackBlindBox(schedule, box);
                }

                if (presentation.PaymentSource == BlindBoxPaymentSource.SteamVoucher)
                {
                    if (CanOpenPlatformBlindBox(presentation.PresentedBox)
                        && BeginPlatformBlindBoxOpen(schedule, presentation.PresentedBox))
                    {
                        return null;
                    }

                    if (_blindBoxFallbackEnabled)
                        return TryOpenFallbackBlindBox(schedule, presentation.PresentedBox);
                    return null;
                }
            }
            else if (UsesSteamInventoryExchange(box))
            {
                _recoverablePlatformService?.RequestReconnect();
                return null;
            }
        }

        var result = _blindBoxService.TryOpenNext(TotalPlaySeconds, ActiveBlindBoxRuntimeState);
        if (result == null)
            return null;

        return FinalizeLocalBlindBoxOpen(result);
    }

    private bool TryGetOrCreateBlindBoxPresentationDecision(
        BlindBoxSchedule schedule,
        BlindBox configuredBox,
        out BlindBoxPresentationDecision decision,
        bool? canOpenPlatformOverride = null,
        uint? voucherQuantityOverride = null)
    {
        if (_blindBoxPresentationDecision is { } existing
            && existing.ScheduleId == schedule.Id
            && existing.ConfiguredBoxId == configuredBox.Id)
        {
            decision = existing;
            return true;
        }

        var presentedBox = ResolveVoucherUpgradeBox(schedule, configuredBox);
        var paymentSource = presentedBox.BoxType == EBlindBoxType.Refreshment
            ? BlindBoxPaymentSource.LocalRefreshment
            : BlindBoxPaymentSource.Chips;
        var voucherItemDefId = 0;
        var voucherQuantity = 0u;

        if (UsesSteamInventoryExchange(presentedBox))
        {
            voucherItemDefId = presentedBox.SteamOpenCostItemDefId;
            voucherQuantity = voucherQuantityOverride ?? GetPlatformInventoryQuantity(voucherItemDefId);
            var canOpenPlatform = canOpenPlatformOverride ?? CanOpenPlatformBlindBox(presentedBox);
            if (canOpenPlatform)
            {
                paymentSource = BlindBoxPaymentSource.SteamVoucher;
            }
            else if (_blindBoxFallbackEnabled
                     && _blindBoxService.GetFallbackRefreshmentBox() is { } fallbackBox)
            {
                presentedBox = fallbackBox;
                paymentSource = BlindBoxPaymentSource.SteamFallback;
            }
            else
            {
                decision = null;
                return false;
            }
        }
        else if (schedule.IsLoopTrack && presentedBox.BoxType == EBlindBoxType.Refreshment)
        {
            paymentSource = BlindBoxPaymentSource.SteamFallback;
        }

        decision = new BlindBoxPresentationDecision
        {
            ScheduleId = schedule.Id,
            ConfiguredBoxId = configuredBox.Id,
            PresentedBox = presentedBox,
            PaymentSource = paymentSource,
        };
        _blindBoxPresentationDecision = decision;

        DiagnosticLog.Record("blindbox_presentation_locked", new Dictionary<string, object>
        {
            ["scheduleId"] = schedule.Id,
            ["configuredBoxId"] = configuredBox.Id,
            ["presentedBoxId"] = presentedBox.Id,
            ["paymentSource"] = paymentSource.ToString(),
            ["platformReady"] = canOpenPlatformOverride
                ?? (_platformInventoryService?.IsInventoryReady == true),
            ["voucherItemDefId"] = voucherItemDefId,
            ["voucherQuantity"] = voucherQuantity,
        });
        GD.Print(
            $"[BlindBox] Locked presentation Schedule={schedule.Id}, ConfiguredBox={configuredBox.Id}, " +
            $"PresentedBox={presentedBox.Id}, Source={paymentSource}, VoucherQty={voucherQuantity}.");
        return true;
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
        DiagnosticLog.Record("blindbox_opened", new Dictionary<string, object>
        {
            ["source"] = "fallback",
            ["fallbackReason"] = _platformInventoryService?.IsInventoryReady == true
                ? "voucher_missing_or_exchange_failed"
                : "platform_not_ready",
            ["platformState"] = _recoverablePlatformService?.ConnectionState.ToString(),
#if DEBUG
            ["steamMockScenario"] = (_platformInventoryService as IDebugSteamMockController)?.Snapshot.Scenario.ToString(),
#endif
            ["scheduleId"] = originalSchedule.Id,
            ["boxId"] = fallbackBox.Id,
            ["rewardItemId"] = result.Item.Id,
            ["chips"] = Chips,
        });
        return FinalizeLocalBlindBoxOpen(result);
    }

    private PendingBlindBoxReward FinalizeLocalBlindBoxOpen(BlindBoxOpenResult result)
    {
        PendingBlindBoxReward = result.PendingReward;
        _blindBoxService.ConsumeOpenedSchedule(ActiveBlindBoxRuntimeState, result.Schedule);
        _blindBoxPresentationDecision = null;
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
        if (_blindBoxLocalTestMode && !_steamMockSimulationActive)
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
        if (_blindBoxService.MaintainLoopPresentation(
                ActiveBlindBoxRuntimeState,
                canLockPresentation,
                selectedBox.Id))
        {
            QueueSaveIfUsingLocalSave();
        }
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
        var loopVoucherLimit = GetLoopSteamVoucherInventoryLimit();
        if (schedule.IsLoopTrack
            && IsLoopSteamVoucherInventoryLimitReached(outputQuantity, loopVoucherLimit))
        {
            GD.Print(
                $"[BlindBox] Skipped recurring TriggerItemDrop({schedule.SteamPlaytimeGeneratorItemDefId}): " +
                $"Steam voucher ItemDef={box.SteamOpenCostItemDefId} is at inventory limit " +
                $"{outputQuantity}/{loopVoucherLimit}.");
            CompleteSteamPlaytimeDrop(schedule.Id, grantCount);
            return;
        }

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
        if (_blindBoxLocalTestMode && !_steamMockSimulationActive)
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
        _blindBoxService.ConsumeOpenedSchedule(ActiveBlindBoxRuntimeState, schedule);
        _blindBoxPresentationDecision = null;
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

    private static int GetLoopSteamVoucherInventoryLimit()
    {
        var config = LubanData.Tables.TbGameDevelopConfig.DataList.FirstOrDefault();
        return Math.Max(0, config?.BlindBoxLoopSteamVoucherInventoryLimit ?? 0);
    }

    private static bool IsLoopSteamVoucherInventoryLimitReached(uint quantity, int limit) =>
        limit > 0 && quantity >= (uint)limit;

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
#if DEBUG
        // Mock snapshots must drive pending-transaction verification, but they are not
        // authoritative ownership data and must never overwrite the sandbox inventory.
        if (_steamMockSimulationActive)
            return;
#endif

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

    public bool TryApplyLinkTreeRewardOnce(int linkTreeId, int chipDelta)
    {
        if (linkTreeId <= 0)
            return false;
        if (_appliedLinkTreeRewardIds.Contains(linkTreeId))
            return true;

        if (chipDelta != 0)
        {
            Chips = checked(Chips + chipDelta);
            Progression.UpdateHighScore(Chips);
            EmitSignal(SignalName.ChipsChanged, Chips);
        }

        _appliedLinkTreeRewardIds.Add(linkTreeId);
        SaveImmediatelyIfUsingLocalSave();
        return true;
    }

    public bool HasAppliedLinkTreeReward(int linkTreeId) =>
        linkTreeId > 0 && _appliedLinkTreeRewardIds.Contains(linkTreeId);

    public void InitializeLinkTreeRewardLedgerBaseline(IEnumerable<int> linkTreeIds)
    {
        if (LinkTreeRewardLedgerInitialized)
            return;

        foreach (var linkTreeId in linkTreeIds.Where(id => id > 0))
            _appliedLinkTreeRewardIds.Add(linkTreeId);
        LinkTreeRewardLedgerInitialized = true;
        SaveImmediatelyIfUsingLocalSave();
        DiagnosticLog.Record("linktree_ledger_baseline_initialized", new Dictionary<string, object>
        {
            ["receiptCount"] = _appliedLinkTreeRewardIds.Count,
            ["chipsGranted"] = 0,
        });
        GD.Print($"[LinkTree] Initialized legacy reward ledger baseline with {_appliedLinkTreeRewardIds.Count} receipt(s); no historical chips were granted.");
    }

    public bool CanAffordBet => Chips >= BetAmount;
    public int LuckyDealRemainingHands => _luckyDealBuffState.RemainingHands;
    public RefreshmentRuntimeState RefreshmentState => _refreshmentRuntimeState;
    public bool IsRefreshmentBuffActive =>
        _refreshmentRuntimeState.Status == TableRefreshmentStatus.BuffActive
        && _luckyDealBuffState.RemainingHands > 0;
    public bool IsUsingLocalSave => _saveDataMode == SettingsManager.SaveDataMode.LocalSave;

    /// <summary>
    /// 背包 E 标记只表示这份消耗品尚未使用、当前正摆在桌上。
    /// BuffActive 中的来源物品已经被消耗，之后新获得的同类物品不能继承 E 标记。
    /// </summary>
    public bool IsTableRefreshmentSelected(int itemId) =>
        itemId > 0
        && _refreshmentRuntimeState.CurrentItemId == itemId
        && _refreshmentRuntimeState.Status == TableRefreshmentStatus.ReadyToUse;

    public bool TrySelectTableRefreshment(int itemId)
    {
        if (_refreshmentRuntimeState.Status == TableRefreshmentStatus.BuffActive)
        {
            EmitSignal(SignalName.RefreshmentSelectionRefused);
            return false;
        }

        var item = LubanData.Tables.TbItem.GetOrDefault(itemId);
        if (item == null
            || item.ItemType != EItemType.Refreshment
            || !Inventory.Owns(itemId)
            || !TryGetUsableRefreshmentConfig(itemId, out _))
            return false;

        SetTableRefreshment(itemId);
        Inventory.ClearNew(itemId);
        QueueSaveIfUsingLocalSave();
        return true;
    }

    public bool TryUseTableRefreshment()
    {
        if (!_refreshmentRuntimeState.IsReadyToUse
            || _luckyDealBuffState.RemainingHands > 0)
            return false;

        var itemId = _refreshmentRuntimeState.CurrentItemId;
        var item = LubanData.Tables.TbItem.GetOrDefault(itemId);
        if (item == null
            || item.ItemType != EItemType.Refreshment
            || !Inventory.Owns(itemId)
            || !TryGetUsableRefreshmentConfig(itemId, out var config))
        {
            ClearTableRefreshment();
            return false;
        }

        var previousState = CloneRefreshmentRuntimeState(_refreshmentRuntimeState);
        var previousRemainingHands = _luckyDealBuffState.RemainingHands;
        var previousTriggerChance = _luckyDealBuffState.TriggerChance;
        var previousLuckyDealMode = _luckyDealBuffState.LuckyDealMode;
        _refreshmentRuntimeState = new RefreshmentRuntimeState
        {
            CurrentItemId = itemId,
            Status = TableRefreshmentStatus.BuffActive,
            BuffSourceItemId = itemId,
            BuffTotalHands = config.DurationHands,
        };
        GrantLuckyDealBuff(
            config.DurationHands,
            config.LuckyDealTriggerChance,
            config.LuckyDealMode);

        // RemoveItem emits InventoryChanged synchronously. Publish the complete Buff state
        // first so inventory observers never see a transient Empty table state.
        if (!Inventory.RemoveItem(itemId, 1))
        {
            _refreshmentRuntimeState = previousState;
            _luckyDealBuffState.RemainingHands = previousRemainingHands;
            _luckyDealBuffState.TriggerChance = previousTriggerChance;
            _luckyDealBuffState.LuckyDealMode = previousLuckyDealMode;
            return false;
        }

        if (!string.IsNullOrWhiteSpace(config.UseSfxCue))
            AudioManager.Instance.PlaySfx(config.UseSfxCue);
        EmitSignal(SignalName.RefreshmentStateChanged);
        QueueSaveIfUsingLocalSave();
        return true;
    }

    private void SetTableRefreshment(int itemId)
    {
        _refreshmentRuntimeState = new RefreshmentRuntimeState
        {
            CurrentItemId = itemId,
            Status = TableRefreshmentStatus.ReadyToUse,
        };
        EmitSignal(SignalName.RefreshmentStateChanged);
    }

    private void ClearTableRefreshment()
    {
        if (_refreshmentRuntimeState.Status == TableRefreshmentStatus.Empty)
            return;

        _refreshmentRuntimeState = new RefreshmentRuntimeState();
        EmitSignal(SignalName.RefreshmentStateChanged);
        QueueSaveIfUsingLocalSave();
    }

    private void SanitizeTableRefreshment()
    {
        if (_refreshmentRuntimeState.Status == TableRefreshmentStatus.Empty)
            return;

        if (_refreshmentRuntimeState.Status == TableRefreshmentStatus.BuffActive)
        {
            if (_luckyDealBuffState.RemainingHands > 0)
                return;

            ClearTableRefreshment();
            return;
        }

        var itemId = _refreshmentRuntimeState.CurrentItemId;
        var item = LubanData.Tables.TbItem.GetOrDefault(itemId);
        if (item == null
            || item.ItemType != EItemType.Refreshment
            || !Inventory.Owns(itemId)
            || !TryGetUsableRefreshmentConfig(itemId, out _))
            ClearTableRefreshment();
    }

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
    public void GrantLuckyDealBuff(
        int turns,
        float triggerChance,
        ELuckyDealMode luckyDealMode = ELuckyDealMode.GuidedDraw)
    {
        if (turns <= 0)
            return;

        _luckyDealBuffState.RemainingHands = checked(_luckyDealBuffState.RemainingHands + turns);
        _luckyDealBuffState.TriggerChance = Mathf.Clamp(triggerChance, 0f, 1f);
        _luckyDealBuffState.LuckyDealMode = luckyDealMode;
        QueueSaveIfUsingLocalSave();
    }

    /// <summary>在一局成功下注时消耗一次 Buff；未触发幸运牌局同样消耗。</summary>
    public bool TryConsumeLuckyDealBuff(out ELuckyDealMode luckyDealMode, out float triggerChance)
    {
        luckyDealMode = ELuckyDealMode.GuidedDraw;
        triggerChance = 0f;
        if (_luckyDealBuffState.RemainingHands <= 0)
            return false;

        _luckyDealBuffState.RemainingHands--;
        luckyDealMode = _luckyDealBuffState.LuckyDealMode;
        triggerChance = _luckyDealBuffState.TriggerChance;
        if (_luckyDealBuffState.RemainingHands <= 0
            && _refreshmentRuntimeState.Status == TableRefreshmentStatus.BuffActive)
        {
            _refreshmentRuntimeState = new RefreshmentRuntimeState();
            EmitSignal(SignalName.RefreshmentStateChanged);
        }
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
        _refreshmentRuntimeState = new RefreshmentRuntimeState();
        Progression.Reset();
        EmitSignal(SignalName.ChipsChanged, Chips);
        EmitSignal(SignalName.BlindBoxStateChanged);
        EmitSignal(SignalName.RefreshmentStateChanged);
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
        EmitSignal(SignalName.RefreshmentStateChanged);
    }

#if DEBUG
    public void ResetLocalSave()
    {
        EndBlindBoxLocalTestMode(force: true);
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
            LoadRefreshmentState(profile);
            EmitSignal(SignalName.ChipsChanged, Chips);
            EmitSignal(SignalName.EquipmentChanged);
            EmitSignal(SignalName.BlindBoxStateChanged);
            EmitSignal(SignalName.RefreshmentStateChanged);
        }
    }
#endif

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
        LoadRefreshmentState(profile);
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
            LoadRefreshmentState(profile);
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
        _refreshmentRuntimeState = new RefreshmentRuntimeState();
        Inventory.ResetToDebugAllItems(emitChanged: false);
        EnsureDefaultTableRefreshment();
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
        SanitizeTableRefreshment();
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
            AppliedLinkTreeRewardIds = _appliedLinkTreeRewardIds.OrderBy(id => id).ToList(),
            LinkTreeRewardLedgerInitialized = LinkTreeRewardLedgerInitialized,
            BlindBoxRuntimeState = _blindBoxRuntimeState,
            PendingBlindBoxReward = PendingBlindBoxReward,
            PendingPlatformBlindBoxOpen = PendingPlatformBlindBoxOpen,
            PendingLinkTreeClaim = PendingLinkTreeClaim,
            LuckyDealBuffState = _luckyDealBuffState,
            RefreshmentRuntimeState = _refreshmentRuntimeState,
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
        _appliedLinkTreeRewardIds.Clear();
        foreach (var linkTreeId in profile.AppliedLinkTreeRewardIds ?? [])
            _appliedLinkTreeRewardIds.Add(linkTreeId);
        LinkTreeRewardLedgerInitialized = profile.LinkTreeRewardLedgerInitialized ?? true;
    }

    private void LoadLuckyDealBuffState(SaveProfile profile)
    {
        _luckyDealBuffState = profile.LuckyDealBuffState ?? new LuckyDealBuffState();
    }

    private void LoadRefreshmentState(SaveProfile profile)
    {
        _refreshmentRuntimeState = profile.RefreshmentRuntimeState ?? new RefreshmentRuntimeState();
        SanitizeTableRefreshment();
        EnsureDefaultTableRefreshment();
    }

    private void EnsureDefaultTableRefreshment()
    {
        if (_refreshmentRuntimeState.Status != TableRefreshmentStatus.Empty
            || _luckyDealBuffState.RemainingHands > 0)
            return;

        var item = Inventory.GetOwnedOfType(EItemType.Refreshment)
            .FirstOrDefault(item => TryGetUsableRefreshmentConfig(item.Id, out _));
        if (item == null)
            return;

        _refreshmentRuntimeState = new RefreshmentRuntimeState
        {
            CurrentItemId = item.Id,
            Status = TableRefreshmentStatus.ReadyToUse,
        };
    }

    private static RefreshmentRuntimeState CloneRefreshmentRuntimeState(RefreshmentRuntimeState state)
    {
        return new RefreshmentRuntimeState
        {
            CurrentItemId = state.CurrentItemId,
            Status = state.Status,
            BuffSourceItemId = state.BuffSourceItemId,
            BuffTotalHands = state.BuffTotalHands,
        };
    }

    private static void ValidateRefreshmentConfigs()
    {
        foreach (var config in LubanData.Tables.TbRefreshmentConfig.DataList)
            _ = TryGetUsableRefreshmentConfig(config.ItemId, out _, logError: true);

        foreach (var item in LubanData.Tables.TbItem.DataList.Where(item => item.ItemType == EItemType.Refreshment))
        {
            if (LubanData.Tables.TbRefreshmentConfig.GetOrDefault(item.Id) == null)
                GD.PushError($"[RefreshmentConfig] Missing config for Refreshment item {item.Id} ({item.Name}).");
        }
    }

    private static bool TryGetUsableRefreshmentConfig(
        int itemId,
        out RefreshmentConfig config,
        bool logError = false)
    {
        config = LubanData.Tables.TbRefreshmentConfig.GetOrDefault(itemId);
        string error = null;
        if (config == null)
            error = "config is missing";
        else if (config.ItemId_Ref?.ItemType != EItemType.Refreshment)
            error = "referenced Item is not Refreshment";
        else if (config.DurationHands is < 1 or > 99)
            error = $"DurationHands {config.DurationHands} is outside 1-99";
        else if (config.LuckyDealTriggerChance is < 0f or > 1f)
            error = $"LuckyDealTriggerChance {config.LuckyDealTriggerChance} is outside 0-1";
        else if (config.LuckyDealMode != ELuckyDealMode.GuidedDraw)
            error = $"LuckyDealMode '{config.LuckyDealMode}' is not implemented";

        if (error == null)
            return true;

        if (logError)
            GD.PushError($"[RefreshmentConfig] Item {itemId}: {error}.");
        return false;
    }
}
