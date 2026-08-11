using Godot;
using DataTables;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace LuckyDogRise;

public partial class GameData : Node
{
#if DEBUG
    public bool StartInSteamMockSimulation { get; set; }
#endif
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
    public bool ActiveBlindBoxPreparationPending =>
        IsBlindBoxPreparationBlockingInventoryWrites(ActiveBlindBoxRuntimeState.PendingPreparation)
        || _platformInventoryService?.IsPlaytimeDropPending == true;
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
    private double _nextPlatformPlaytimeDropAttemptAtSeconds;
#if DEBUG
    private bool _blindBoxLocalTestMode;
    private bool _steamMockSimulationActive;
    private DebugBlindBoxProgressMode _steamMockProgressMode = DebugBlindBoxProgressMode.Loop;
    private BlindBoxRuntimeState _blindBoxLocalTestRuntimeState = new();
    private int _blindBoxLocalTestPreparedRewardCount;
    private int _blindBoxLocalTestSavedChips;
    private double _blindBoxLocalTestSavedTotalPlaySeconds;
    private Dictionary<int, int> _blindBoxLocalTestSavedInventoryCounts = new();
    private Dictionary<string, int> _blindBoxLocalTestSavedEquippedItems = new();
    private List<int> _blindBoxLocalTestSavedNewItemIds = new();
    private LuckyDealBuffState _blindBoxLocalTestSavedLuckyDealBuffState = new();
    private RefreshmentRuntimeState _blindBoxLocalTestSavedRefreshmentRuntimeState = new();
    private HashSet<int> _blindBoxLocalTestSavedLinkTreeRewardIds = [];
    private bool _blindBoxLocalTestSavedLinkTreeLedgerInitialized;
    private double _blindBoxLocalTestSavedNextPlatformPlaytimeDropAttemptAtSeconds;
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

    private static bool IsBlindBoxPreparationBlockingInventoryWrites(
        PendingBlindBoxPreparation pending) =>
        pending != null && pending.Phase != BlindBoxPreparationPhase.RetryWaiting;

    public override void _Ready()
    {
        ValidateRefreshmentConfigs();
        _blindBoxService = new BlindBoxService(this);
#if DEBUG
        _saveDataMode = StartInSteamMockSimulation
            ? SettingsManager.SaveDataMode.LocalSave
            : SettingsManager.LoadSaveDataMode();
#else
        _saveDataMode = SettingsManager.LoadSaveDataMode();
#endif
        PlayerProgress = new PlayerProgress();
        _profileAutosaveTimer = ProfileAutosaveSeconds;
        _playerProgressSaveTimer = PlayerProgressAutosaveSeconds;
        LoadDataForCurrentMode();
        Inventory.EquipmentChanged += OnInventoryEquipmentChanged;
        Inventory.InventoryChanged += OnInventoryChanged;
#if DEBUG
        if (StartInSteamMockSimulation && !SetSteamMockSimulationActive(true))
            GD.PushError("[Steam Mock] Failed to enter the startup Debug sandbox.");
#endif
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
#if DEBUG
        if (_steamMockSimulationActive)
            ConfigureSteamMockBlindBox();
#endif
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
        var simulationText = _steamMockSimulationActive
            ? "Steam Mock: 开启（网络与准备状态以上方面板为准）"
            : _blindBoxLocalTestMode
                ? $"本地测试: 开启，虚拟待揭晓奖励 x{_blindBoxLocalTestPreparedRewardCount}"
                : "本地测试: 关闭（使用真实 Steam/离线流程）";
        return $"{simulationText}\n{debugStatus}\n入口最终状态: {GetBlindBoxHintState().Status}";
    }

    public bool IsBlindBoxLocalTestMode => _blindBoxLocalTestMode && !_steamMockSimulationActive;
    public bool IsSteamMockSimulationActive => _steamMockSimulationActive;
    public DebugBlindBoxProgressMode SteamMockProgressMode => _steamMockProgressMode;
    public int BlindBoxLocalTestPreparedRewardCount =>
        _steamMockSimulationActive ? 0 : _blindBoxLocalTestPreparedRewardCount;
    public string SteamMockBlindBoxBusinessPhase =>
        ActiveBlindBoxRuntimeState.PendingPreparation?.Phase.ToString()
        ?? (ActiveBlindBoxRuntimeState.PreparedReward != null ? "Prepared" : "Idle");
    public bool SteamMockBlindBoxRewardIsLate =>
        ActiveBlindBoxRuntimeState.PendingPreparation?.IsLate == true
        || ActiveBlindBoxRuntimeState.PreparedReward?.IsLate == true;

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
        _blindBoxLocalTestPreparedRewardCount = 0;
        _blindBoxLocalTestRuntimeState = CreateSteamMockRuntimeState();
        ResetPlaytimeDropTransientState();
        ConfigureSteamMockBlindBox();
        if (_platformInventoryService is IDebugSteamMockController startupController)
            startupController.ResetScenario();
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
        PendingLinkTreeClaim = null;
        _appliedLinkTreeRewardIds.Clear();
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
        _blindBoxLocalTestPreparedRewardCount = 0;
        _blindBoxLocalTestRuntimeState = CreateSteamMockRuntimeState();
        ResetPlaytimeDropTransientState();
        ConfigureSteamMockBlindBox();
        if (_platformInventoryService is IDebugSteamMockController controller)
            controller.ResetScenario();
        MaintainLoopPresentation();
        EmitSignal(SignalName.ChipsChanged, Chips);
        EmitSignal(SignalName.BlindBoxStateChanged);
        EmitSignal(SignalName.RefreshmentStateChanged);
        DiagnosticLog.Record("steam_mock_sandbox_reset");
        return true;
    }

    public void SetSteamMockProgressMode(DebugBlindBoxProgressMode mode)
    {
        if (!Enum.IsDefined(mode) || _steamMockProgressMode == mode)
            return;
        _steamMockProgressMode = mode;
        DiagnosticLog.Record("steam_mock_progress_mode_selected", new Dictionary<string, object>
        {
            ["mode"] = mode.ToString(),
            ["appliesAfterReset"] = true,
        });
    }

    private BlindBoxRuntimeState CreateSteamMockRuntimeState() => new()
    {
        SequenceIndex = _steamMockProgressMode == DebugBlindBoxProgressMode.Loop
            ? GetEnabledSequenceSchedules().Count
            : 0,
    };

    private void ConfigureSteamMockBlindBox()
    {
        BlindBox box = null;
        if (_steamMockProgressMode == DebugBlindBoxProgressMode.BeginnerSequence)
        {
            var schedule = GetEnabledSequenceSchedules().ElementAtOrDefault(
                Math.Max(0, _blindBoxLocalTestRuntimeState.SequenceIndex));
            box = schedule == null
                ? null
                : LubanData.Tables.TbBlindBox.GetOrDefault(schedule.BlindBoxId);
        }
        else if (_blindBoxService.TryGetLoopSchedule(out _, out var loopBox))
        {
            box = loopBox;
        }

        if (box != null)
            ConfigureSteamMockBlindBox(box);
    }

    private void ConfigureSteamMockBlindBox(BlindBox box)
    {
        if (_platformInventoryService is not IDebugSteamMockController controller)
        {
            return;
        }

        var reward = LubanData.Tables.TbItem.DataList
            .Where(item => item.SteamItemDefId > 0 && _blindBoxService.IsRewardCandidate(box, item))
            .OrderBy(item => item.Id)
            .FirstOrDefault();
        if (reward == null)
        {
            if (box.IsPlatformInventoryRequired)
                GD.PushError($"[Steam Mock] Blind box {box.Id} has no valid Steam reward candidate.");
            return;
        }
        controller.ConfigureBlindBox(box.Id, reward.SteamItemDefId);
    }

    private static List<BlindBoxSchedule> GetEnabledSequenceSchedules() =>
        LubanData.Tables.TbBlindBoxSchedule.DataList
            .Where(schedule => schedule.IsEnabled && !schedule.IsLoopTrack)
            .OrderBy(schedule => schedule.StartSeconds)
            .ThenBy(schedule => schedule.Id)
            .ToList();

    public void AdjustBlindBoxLocalTestPreparedRewardCount(int delta)
    {
        if (!_blindBoxLocalTestMode || _steamMockSimulationActive || delta == 0)
            return;

        _blindBoxLocalTestPreparedRewardCount = Math.Clamp(
            _blindBoxLocalTestPreparedRewardCount + delta,
            0,
            1);
        if (_blindBoxLocalTestPreparedRewardCount == 0)
            _blindBoxLocalTestRuntimeState.PreparedReward = null;
        else
            EnsureLocalTestPreparedReward();
        EmitSignal(SignalName.BlindBoxStateChanged);
    }

    public bool AdvanceBlindBoxLocalTestPresentation()
    {
        if (!_blindBoxLocalTestMode || PendingBlindBoxReward != null)
            return false;

        MaintainLoopPresentation();
        var state = _blindBoxLocalTestRuntimeState;
        var sequence = GetEnabledSequenceSchedules().ElementAtOrDefault(Math.Max(0, state.SequenceIndex));
        if (sequence != null)
        {
            var presentationSeconds = state.SequenceIndex == 0
                ? Math.Max(sequence.StartSeconds, sequence.IntervalSeconds)
                : Math.Max(sequence.StartSeconds, state.LastClaimSeconds + Math.Max(0, sequence.IntervalSeconds));
            state.ScheduleSeconds = Math.Max(state.ScheduleSeconds, presentationSeconds);
            if (_steamMockSimulationActive && sequence.SteamPlaytimeGeneratorItemDefId > 0)
            {
                TotalPlaySeconds = Math.Max(TotalPlaySeconds, _blindBoxService.GetSteamEligibilityRealSecondsForDebug(sequence));
                _nextPlatformPlaytimeDropAttemptAtSeconds = 0.0;
                MaintainSteamPlaytimeDrops();
                if (state.PendingPreparation != null || state.PreparedReward != null)
                {
                    EmitSignal(SignalName.BlindBoxStateChanged);
                    return true;
                }
            }
        }
        else
        {
            state.ScheduleSeconds = Math.Max(state.ScheduleSeconds, state.NextLoopPresentationSeconds);
            if (_steamMockSimulationActive && state.PreparedReward == null)
            {
                _nextPlatformPlaytimeDropAttemptAtSeconds = 0.0;
                MaintainSteamPlaytimeDrops();
                if (state.PendingPreparation != null)
                {
                    EmitSignal(SignalName.BlindBoxStateChanged);
                    return true;
                }
            }
        }
        MaintainLoopPresentation();
        EmitSignal(SignalName.BlindBoxStateChanged);
        return true;
    }

    public bool ClearBlindBoxLocalTestPresentation()
    {
        if (!_blindBoxLocalTestMode || PendingBlindBoxReward != null)
            return false;

        _blindBoxLocalTestRuntimeState.LockedPresentation = null;
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

        // A dormant production preparation (for example RetryWaiting or
        // RevalidationRequired) remains in _blindBoxRuntimeState while the Debug
        // sandbox uses its separate runtime state. It is safe to preserve and resume
        // that preparation after leaving Mock. Only an operation that is actually in
        // flight must prevent the platform decorator from changing scenarios.
        if (PendingBlindBoxReward != null
            || PendingLinkTreeClaim != null
            || _platformInventoryService?.IsPlaytimeDropPending == true
            || _platformInventoryService?.IsPromoGrantPending == true)
        {
            GD.PushWarning("[BlindBox] Cannot enter local test mode while a reveal, LinkTree claim, or Steam inventory write is active.");
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
        _blindBoxLocalTestSavedLinkTreeRewardIds = _appliedLinkTreeRewardIds.ToHashSet();
        _blindBoxLocalTestSavedLinkTreeLedgerInitialized = LinkTreeRewardLedgerInitialized;
        _blindBoxLocalTestSavedNextPlatformPlaytimeDropAttemptAtSeconds =
            _nextPlatformPlaytimeDropAttemptAtSeconds;
        _appliedLinkTreeRewardIds.Clear();
        LinkTreeRewardLedgerInitialized = true;
        _blindBoxLocalTestRuntimeState = new BlindBoxRuntimeState
        {
            SequenceIndex = LubanData.Tables.TbBlindBoxSchedule.DataList.Count(schedule =>
                schedule.IsEnabled && !schedule.IsLoopTrack),
        };
        _blindBoxLocalTestPreparedRewardCount = 1;
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
        Chips = _blindBoxLocalTestSavedChips;
        TotalPlaySeconds = _blindBoxLocalTestSavedTotalPlaySeconds;
        Inventory.LoadState(
            _blindBoxLocalTestSavedInventoryCounts,
            _blindBoxLocalTestSavedEquippedItems,
            _blindBoxLocalTestSavedNewItemIds,
            emitChanged: true);
        _luckyDealBuffState = _blindBoxLocalTestSavedLuckyDealBuffState;
        _refreshmentRuntimeState = _blindBoxLocalTestSavedRefreshmentRuntimeState;
        _appliedLinkTreeRewardIds.Clear();
        _appliedLinkTreeRewardIds.UnionWith(_blindBoxLocalTestSavedLinkTreeRewardIds);
        LinkTreeRewardLedgerInitialized = _blindBoxLocalTestSavedLinkTreeLedgerInitialized;
        _nextPlatformPlaytimeDropAttemptAtSeconds =
            _blindBoxLocalTestSavedNextPlatformPlaytimeDropAttemptAtSeconds;
        PendingLinkTreeClaim = null;
        PlayerProgress.EndDebugSimulation();

        _blindBoxLocalTestMode = false;
        _steamMockSimulationActive = false;
        _blindBoxLocalTestRuntimeState = new BlindBoxRuntimeState();
        _blindBoxLocalTestPreparedRewardCount = 0;
        _blindBoxLocalTestSavedInventoryCounts = new Dictionary<int, int>();
        _blindBoxLocalTestSavedEquippedItems = new Dictionary<string, int>();
        _blindBoxLocalTestSavedNewItemIds = new List<int>();
        _blindBoxLocalTestSavedLuckyDealBuffState = new LuckyDealBuffState();
        _blindBoxLocalTestSavedRefreshmentRuntimeState = new RefreshmentRuntimeState();
        _blindBoxLocalTestSavedLinkTreeRewardIds = [];
        _blindBoxLocalTestSavedLinkTreeLedgerInitialized = false;
        _blindBoxLocalTestSavedNextPlatformPlaytimeDropAttemptAtSeconds = 0.0;
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

        EnsureLocalTestPreparedReward();
        _blindBoxService.MaintainPresentation(_blindBoxLocalTestRuntimeState);
    }

    private void EnsureLocalTestPreparedReward()
    {
        if (_blindBoxLocalTestPreparedRewardCount <= 0
            || _blindBoxLocalTestRuntimeState.PreparedReward != null
            || !_blindBoxService.TryGetLoopSchedule(out var schedule, out var box)
            || schedule == null || box == null)
            return;

        var item = LubanData.Tables.TbItem.DataList
            .Where(candidate => candidate.SteamItemDefId > 0 && _blindBoxService.IsRewardCandidate(box, candidate))
            .OrderBy(candidate => candidate.Id)
            .FirstOrDefault();
        if (item == null)
            return;

        _blindBoxLocalTestRuntimeState.PreparedReward = new PreparedBlindBoxReward
        {
            ScheduleId = schedule.Id,
            BlindBoxId = box.Id,
            PlatformInstanceId = ulong.MaxValue,
            SteamItemDefId = item.SteamItemDefId,
            ItemId = item.Id,
        };
    }
#endif

    public BlindBoxHintState GetBlindBoxHintState()
    {
        MaintainLoopPresentation();
        return _blindBoxService.GetHintState(
            ActiveBlindBoxRuntimeState,
            PendingBlindBoxReward);
    }

    public PendingBlindBoxReward TryOpenBlindBox()
    {
        MaintainLoopPresentation();
        if (PendingBlindBoxReward != null)
            return PendingBlindBoxReward;

        if (!_blindBoxService.TryGetLockedPresentation(
                ActiveBlindBoxRuntimeState,
                out var schedule,
                out var box,
                out var presentation)
            || schedule == null || box == null || presentation == null)
            return null;

        if (presentation.Kind is LockedBlindBoxPresentationKind.PreparedSteam
            or LockedBlindBoxPresentationKind.LateSteam)
        {
            return FinalizePreparedBlindBoxOpen(schedule, box, presentation);
        }

        var result = _blindBoxService.TryOpenNext(TotalPlaySeconds, ActiveBlindBoxRuntimeState);
        return result == null ? null : FinalizeLocalBlindBoxOpen(result);
    }

    private PendingBlindBoxReward FinalizeLocalBlindBoxOpen(BlindBoxOpenResult result)
    {
        var presentationKind = ActiveBlindBoxRuntimeState.LockedPresentation?.Kind
                               ?? LockedBlindBoxPresentationKind.ScheduledLocal;
        PendingBlindBoxReward = result.PendingReward;
        PendingBlindBoxReward.CompletesSchedule =
            _blindBoxService.ConsumeOpenedPresentation(ActiveBlindBoxRuntimeState);
        if (CanRecordPlayerProgress)
        {
            PlayerProgress.RecordBlindBoxOpened(PlayerProgressSource.BlindBox);
            PlayerProgress.RecordBlindBoxChipsSpent(
                _blindBoxService.ResolvePrice(result.Schedule, result.Box, useScheduleOverride: true).ActualCost,
                PlayerProgressSource.BlindBox);
        }
        DiagnosticLog.Record("blindbox_opened", new Dictionary<string, object>
        {
            ["source"] = presentationKind.ToString(),
            ["fallbackReason"] = presentationKind == LockedBlindBoxPresentationKind.Fallback
                ? ActiveBlindBoxRuntimeState.PendingPreparation?.Phase.ToString()
                  ?? "no_prepared_reward_at_presentation"
                : string.Empty,
            ["scheduleId"] = result.Schedule.Id,
            ["boxId"] = result.Box.Id,
            ["rewardItemId"] = result.Item.Id,
        });
        EmitSignal(SignalName.BlindBoxStateChanged);
        SaveImmediatelyIfUsingLocalSave();
        return PendingBlindBoxReward;
    }

    private PendingBlindBoxReward FinalizePreparedBlindBoxOpen(
        BlindBoxSchedule schedule,
        BlindBox box,
        LockedBlindBoxPresentation presentation)
    {
        var prepared = ActiveBlindBoxRuntimeState.PreparedReward;
        if (prepared == null
            || prepared.PlatformInstanceId != presentation.PreparedPlatformInstanceId
            || prepared.BlindBoxId != box.Id)
            return null;

        var item = LubanData.Tables.TbItem.GetOrDefault(prepared.ItemId);
        if (item == null || !_blindBoxService.IsRewardCandidate(box, item))
            return null;

        var useScheduleOverride = presentation.Kind != LockedBlindBoxPresentationKind.LateSteam;
        var price = _blindBoxService.ResolvePrice(schedule, box, useScheduleOverride);
        if (Chips < price.ActualCost)
            return null;
        if (price.ActualCost > 0)
            ModifyChips(-price.ActualCost);

        var result = _blindBoxService.CreateOpenResult(
            TotalPlaySeconds,
            schedule,
            box,
            item,
            price.ActualCost,
            prepared.PlatformInstanceId,
            completesSchedule: presentation.Kind != LockedBlindBoxPresentationKind.LateSteam);
        if (result == null)
        {
            if (price.ActualCost > 0)
                ModifyChips(price.ActualCost);
            return null;
        }

        ActiveBlindBoxRuntimeState.PreparedReward = null;
        PendingBlindBoxReward = result.PendingReward;
        PendingBlindBoxReward.CompletesSchedule =
            _blindBoxService.ConsumeOpenedPresentation(ActiveBlindBoxRuntimeState);
#if DEBUG
        if (_blindBoxLocalTestMode && !_steamMockSimulationActive)
            _blindBoxLocalTestPreparedRewardCount = 0;
#endif
        if (CanRecordPlayerProgress)
        {
            PlayerProgress.RecordBlindBoxOpened(PlayerProgressSource.BlindBox);
            PlayerProgress.RecordBlindBoxChipsSpent(price.ActualCost, PlayerProgressSource.BlindBox);
        }
        DiagnosticLog.Record("blindbox_opened", new Dictionary<string, object>
        {
            ["source"] = presentation.Kind.ToString(),
            ["scheduleId"] = schedule.Id,
            ["boxId"] = box.Id,
            ["rewardItemId"] = item.Id,
            ["platformInstanceId"] = prepared.PlatformInstanceId,
        });
        EmitSignal(SignalName.BlindBoxStateChanged);
        SaveImmediatelyIfUsingLocalSave();
        return PendingBlindBoxReward;
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
        if (PendingBlindBoxReward != null)
            return;
        if (_blindBoxService.MaintainPresentation(ActiveBlindBoxRuntimeState))
        {
            if (ActiveBlindBoxRuntimeState.LockedPresentation is { } locked)
            {
                DiagnosticLog.Record("blindbox_presentation_locked", new Dictionary<string, object>
                {
                    ["scheduleId"] = locked.ScheduleId,
                    ["blindBoxId"] = locked.BlindBoxId,
                    ["kind"] = locked.Kind.ToString(),
                    ["preparedPlatformInstanceId"] = locked.PreparedPlatformInstanceId,
                    ["preparationPhase"] = ActiveBlindBoxRuntimeState.PendingPreparation?.Phase.ToString(),
                });
            }
            QueueSaveIfUsingLocalSave();
        }
    }

    private void MaintainSteamPlaytimeDrops()
    {
#if DEBUG
        if (_blindBoxLocalTestMode && !_steamMockSimulationActive)
            return;
#endif
        if (_platformInventoryService?.IsInventoryReady != true
            || PendingLinkTreeClaim != null
            || _platformInventoryService.IsPlaytimeDropPending
            || _platformInventoryService.IsPromoGrantPending)
            return;

        var runtimeState = ActiveBlindBoxRuntimeState;
        var pending = runtimeState.PendingPreparation;
        if (pending != null)
        {
            if (pending.Phase == BlindBoxPreparationPhase.RetryWaiting
                && TotalPlaySeconds >= pending.RetryNotBeforeTotalPlaySeconds)
            {
                runtimeState.PendingPreparation = null;
                SaveImmediatelyIfUsingLocalSave();
            }
            else
            {
                if (pending.Phase != BlindBoxPreparationPhase.RetryWaiting)
                    _platformInventoryService.StartInventorySynchronization();
                return;
            }
        }

        if (!_blindBoxService.TryGetPreparationCandidate(
                runtimeState,
                TotalPlaySeconds,
                out var schedule,
                out var box)
            || schedule == null
            || box == null
            || schedule.SteamPlaytimeGeneratorItemDefId <= 0)
            return;

        var now = Time.GetTicksMsec() / 1000.0;
        if (now < _nextPlatformPlaytimeDropAttemptAtSeconds)
            return;

        var baseline = BuildInstanceQuantityMap(_platformInventoryService.InventoryItems);
#if DEBUG
        if (_steamMockSimulationActive)
            ConfigureSteamMockBlindBox(box);
#endif
        if (!_platformInventoryService.TryTriggerPlaytimeDrop(schedule.SteamPlaytimeGeneratorItemDefId, out var message))
        {
            _nextPlatformPlaytimeDropAttemptAtSeconds = now + 5.0;
            _recoverablePlatformService?.RequestReconnect();
            return;
        }

        _nextPlatformPlaytimeDropAttemptAtSeconds = now + SteamPlaytimeDropMinimumAttemptIntervalSeconds;
        runtimeState.PendingPreparation = new PendingBlindBoxPreparation
        {
            ScheduleId = schedule.Id,
            BlindBoxId = box.Id,
            GeneratorItemDefId = schedule.SteamPlaytimeGeneratorItemDefId,
            Phase = BlindBoxPreparationPhase.Submitted,
            SubmittedAtTotalPlaySeconds = TotalPlaySeconds,
            InventoryQuantitiesBeforeRequest = baseline,
        };
        _blindBoxService.MarkPreparationRequestAccepted(runtimeState, schedule);
        SaveImmediatelyIfUsingLocalSave();
        DiagnosticLog.Record("blindbox_preparation_submitted", new Dictionary<string, object>
        {
            ["scheduleId"] = schedule.Id,
            ["blindBoxId"] = box.Id,
            ["generatorItemDefId"] = schedule.SteamPlaytimeGeneratorItemDefId,
            ["baselineInstances"] = baseline.Count,
        });
        GD.Print($"[BlindBox] Preparation Schedule={schedule.Id}: {message}");
        EmitSignal(SignalName.BlindBoxStateChanged);
    }

    private void OnPlatformPlaytimeDropCompleted(PlatformPlaytimeDropResult result)
    {
        var runtimeState = ActiveBlindBoxRuntimeState;
        var pending = runtimeState.PendingPreparation;
        if (pending == null
            || pending.GeneratorItemDefId != result.GeneratorItemDefId)
            return;

        GD.Print($"[BlindBox] {result.Message}");
        var resolveReason = "platform_request_failed";
        if (result.Succeeded
            && TryResolvePreparedReward(pending, result.ChangedItems, out var prepared, out resolveReason))
        {
            CompletePreparedReward(runtimeState, pending, prepared);
            return;
        }

        DiagnosticLog.Record("blindbox_preparation_callback_unresolved", new Dictionary<string, object>
        {
            ["scheduleId"] = pending.ScheduleId,
            ["generatorItemDefId"] = pending.GeneratorItemDefId,
            ["succeeded"] = result.Succeeded,
            ["reason"] = resolveReason,
            ["changedItems"] = string.Join(",", result.ChangedItems.Select(item =>
                $"{item.InstanceId}:{item.ItemDefId}x{item.Quantity}")),
        });

        pending.Phase = result.Succeeded
            ? BlindBoxPreparationPhase.RetryWaiting
            : BlindBoxPreparationPhase.RevalidationRequired;
        pending.RetryNotBeforeTotalPlaySeconds = Math.Max(
            pending.SubmittedAtTotalPlaySeconds + SteamPlaytimeDropMinimumAttemptIntervalSeconds,
            TotalPlaySeconds + (result.Succeeded ? 0.0 : 5.0));
        SaveImmediatelyIfUsingLocalSave();
        if (!result.Succeeded)
            _recoverablePlatformService?.RequestReconnect();
        _platformInventoryService?.StartInventorySynchronization();
        EmitSignal(SignalName.BlindBoxStateChanged);
    }

    private void OnPlatformInventorySnapshotChanged(PlatformInventorySnapshot snapshot)
    {
        if (!snapshot.Succeeded)
            return;

#if DEBUG
        if (_blindBoxLocalTestMode && !_steamMockSimulationActive)
            return;
#endif
        ReconcilePendingPreparation(snapshot.Items);
        ReconcilePreparedRewardPresence(snapshot.Items);
        ReconcilePlatformInventory(snapshot.Items);
    }

    private void ReconcilePreparedRewardPresence(IReadOnlyList<PlatformInventoryItem> platformItems)
    {
        var runtimeState = ActiveBlindBoxRuntimeState;
        var prepared = runtimeState.PreparedReward;
        if (prepared == null)
            return;

        // A visible or already-running reveal is immutable. Before that boundary, a trusted
        // full snapshot may revoke a stale prepared slot when its exact Steam instance vanished.
        if (runtimeState.LockedPresentation?.PreparedPlatformInstanceId
                == prepared.PlatformInstanceId
            || PendingBlindBoxReward is
            {
                IsPlatformInventoryReward: true,
                PlatformInstanceId: var pendingInstanceId,
            } && pendingInstanceId == prepared.PlatformInstanceId)
            return;

        var stillExists = platformItems.Any(item =>
            item.InstanceId == prepared.PlatformInstanceId
            && item.ItemDefId == prepared.SteamItemDefId
            && item.Quantity > 0);
        if (stillExists)
            return;

        DiagnosticLog.Record("blindbox_prepared_reward_missing", new Dictionary<string, object>
        {
            ["scheduleId"] = prepared.ScheduleId,
            ["blindBoxId"] = prepared.BlindBoxId,
            ["platformInstanceId"] = prepared.PlatformInstanceId,
            ["steamItemDefId"] = prepared.SteamItemDefId,
        });
        runtimeState.PreparedReward = null;
        SaveImmediatelyIfUsingLocalSave();
        EmitSignal(SignalName.BlindBoxStateChanged);
    }

    private void UnbindPlatformInventoryService()
    {
        if (_platformInventoryService != null)
        {
            _platformInventoryService.InventorySnapshotChanged -= OnPlatformInventorySnapshotChanged;
            _platformInventoryService.PlaytimeDropCompleted -= OnPlatformPlaytimeDropCompleted;
        }
        _platformInventoryService = null;
        _recoverablePlatformService = null;
    }

    private void ReconcilePendingPreparation(IReadOnlyList<PlatformInventoryItem> platformItems)
    {
        var runtimeState = ActiveBlindBoxRuntimeState;
        var pending = runtimeState.PendingPreparation;
        if (pending == null)
            return;

        if (TryResolvePreparedReward(pending, platformItems, out var prepared, out var reason))
        {
            CompletePreparedReward(runtimeState, pending, prepared);
            return;
        }

        if (_platformInventoryService?.IsPlaytimeDropPending == true)
            return;

        pending.Phase = BlindBoxPreparationPhase.RetryWaiting;
        pending.RetryNotBeforeTotalPlaySeconds = Math.Max(
            pending.SubmittedAtTotalPlaySeconds + SteamPlaytimeDropMinimumAttemptIntervalSeconds,
            TotalPlaySeconds);
        DiagnosticLog.Record("blindbox_preparation_revalidation_empty", new Dictionary<string, object>
        {
            ["scheduleId"] = pending.ScheduleId,
            ["generatorItemDefId"] = pending.GeneratorItemDefId,
            ["reason"] = reason,
            ["retryAt"] = pending.RetryNotBeforeTotalPlaySeconds,
        });
        SaveImmediatelyIfUsingLocalSave();
        EmitSignal(SignalName.BlindBoxStateChanged);
    }

    private bool TryResolvePreparedReward(
        PendingBlindBoxPreparation pending,
        IReadOnlyList<PlatformInventoryItem> platformItems,
        out PlatformInventoryItem reward,
        out string reason)
    {
        reward = default;
        var box = LubanData.Tables.TbBlindBox.GetOrDefault(pending.BlindBoxId);
        if (box == null)
        {
            reason = "blind_box_missing";
            return false;
        }

        var increments = platformItems
            .Where(platformItem =>
            {
                pending.InventoryQuantitiesBeforeRequest.TryGetValue(platformItem.InstanceId, out var before);
                return platformItem.Quantity > before;
            })
            .GroupBy(platformItem => platformItem.InstanceId)
            .Select(group => group.OrderByDescending(item => item.Quantity).First())
            .ToArray();
        var candidates = increments
            .Where(platformItem =>
            {
                var item = FindLocalItem(platformItem.ItemDefId);
                return item != null && _blindBoxService.IsRewardCandidate(box, item);
            })
            .ToArray();

        if (increments.Length != 1 || candidates.Length != 1)
        {
            reason = increments.Length == 0
                ? "no_inventory_increment"
                : "ambiguous_or_invalid_inventory_increment";
            return false;
        }

        reward = candidates[0];
        reason = "unique_valid_increment";
        return true;
    }

    private void CompletePreparedReward(
        BlindBoxRuntimeState runtimeState,
        PendingBlindBoxPreparation pending,
        PlatformInventoryItem reward)
    {
        var item = FindLocalItem(reward.ItemDefId);
        if (item == null)
            return;

        runtimeState.PreparedReward = new PreparedBlindBoxReward
        {
            ScheduleId = pending.ScheduleId,
            BlindBoxId = pending.BlindBoxId,
            PlatformInstanceId = reward.InstanceId,
            SteamItemDefId = reward.ItemDefId,
            ItemId = item.Id,
            IsLate = pending.IsLate,
        };
        runtimeState.PendingPreparation = null;
        DiagnosticLog.Record("blindbox_preparation_confirmed", new Dictionary<string, object>
        {
            ["scheduleId"] = pending.ScheduleId,
            ["blindBoxId"] = pending.BlindBoxId,
            ["generatorItemDefId"] = pending.GeneratorItemDefId,
            ["platformInstanceId"] = reward.InstanceId,
            ["steamItemDefId"] = reward.ItemDefId,
            ["itemId"] = item.Id,
            ["isLate"] = pending.IsLate,
        });
        SaveImmediatelyIfUsingLocalSave();
        EmitSignal(SignalName.BlindBoxStateChanged);
    }

    private static Dictionary<ulong, uint> BuildInstanceQuantityMap(
        IReadOnlyList<PlatformInventoryItem> platformItems) =>
        platformItems
            .GroupBy(item => item.InstanceId)
            .ToDictionary(
                group => group.Key,
                group => group.Aggregate(0u, (quantity, item) => checked(quantity + item.Quantity)));

    private static Item FindLocalItem(int steamItemDefId) =>
        LubanData.Tables.TbItem.DataList.FirstOrDefault(item => item.SteamItemDefId == steamItemDefId);

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
        var withheldInstanceIds = new HashSet<ulong>();
        if (ActiveBlindBoxRuntimeState.PreparedReward is { PlatformInstanceId: > 0 } preparedReward)
            withheldInstanceIds.Add(preparedReward.PlatformInstanceId);
        if (PendingBlindBoxReward is
            {
                IsPlatformInventoryReward: true,
                PlatformInstanceId: > 0,
            } pendingReward)
            withheldInstanceIds.Add(pendingReward.PlatformInstanceId);

        foreach (var instanceId in withheldInstanceIds)
        {
            var platformItem = platformItems.FirstOrDefault(item =>
                item.InstanceId == instanceId && item.Quantity > 0);
            if (platformItem.InstanceId != 0
                && countsByItemDef.TryGetValue(platformItem.ItemDefId, out var count))
                countsByItemDef[platformItem.ItemDefId] = Math.Max(0, count - 1);
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
                ownedCounts[item.Id] = desiredCount;
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
        var completedSchedule = PendingBlindBoxReward.CompletesSchedule;
        PendingBlindBoxReward = null;
        AddItem(itemId, count: 1, markNew: true, source: PlayerProgressSource.BlindBox);
        _blindBoxService.CompleteClaimedPresentation(
            ActiveBlindBoxRuntimeState,
            scheduleId,
            completedSchedule);
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
        if (_blindBoxLocalTestMode && !_steamMockSimulationActive)
        {
            GD.PushWarning("[LinkTree] Real claims are disabled while blind-box local test mode is active.");
            return false;
        }
#endif
        if (linkTreeId <= 0 || steamClaimBundleItemDefId <= 0 || steamReceiptItemDefId <= 0)
            return false;
        if (IsBlindBoxPreparationBlockingInventoryWrites(ActiveBlindBoxRuntimeState.PendingPreparation)
            || _platformInventoryService?.IsPlaytimeDropPending == true)
        {
            GD.PushWarning("[LinkTree] A blind-box or playtime inventory transaction is pending.");
            return false;
        }
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
#if DEBUG
            if (!_blindBoxLocalTestMode)
#endif
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
#else
        if (_steamMockSimulationActive)
        {
            GD.PushWarning("[Steam Mock] Inventory source cannot change while the Mock sandbox is active.");
            return;
        }
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
        _nextPlatformPlaytimeDropAttemptAtSeconds = 0.0;
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
