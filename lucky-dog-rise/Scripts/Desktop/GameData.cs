using Godot;
using DataTables;
using System.Collections.Generic;
using System.Linq;

namespace LuckyDogRise;

public partial class GameData : Node
{
    public const int StartingChips = 1000;

    [Signal] public delegate void ChipsChangedEventHandler(int chips);
    [Signal] public delegate void HandResolvedEventHandler(EHandRank rank, int payout);
    [Signal] public delegate void NewHandStartedEventHandler();
    [Signal] public delegate void EquipmentChangedEventHandler();
    [Signal] public delegate void InventoryChangedEventHandler();
    [Signal] public delegate void BlindBoxStateChangedEventHandler();

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
    public int BetAmount => 50;
    public ProgressionManager Progression { get; } = new();

    private readonly Dictionary<int, int> _blindBoxClaimedCountsBySchedule = new();
    private BlindBoxService _blindBoxService;
    private SettingsManager.SaveDataMode _saveDataMode;
    private bool _saveDirty;
    private double _saveTimer;
    private double _blindBoxTickTimer;
    private const double SaveDebounceSeconds = 0.75;
    private const double BlindBoxTickSeconds = 1.0;

    public override void _Ready()
    {
        _blindBoxService = new BlindBoxService(this);
        _saveDataMode = SettingsManager.LoadSaveDataMode();
        LoadDataForCurrentMode();
        Inventory.EquipmentChanged += OnInventoryEquipmentChanged;
        Inventory.InventoryChanged += OnInventoryChanged;
        EmitSignal(SignalName.ChipsChanged, Chips);
        EmitSignal(SignalName.EquipmentChanged);
    }

    public override void _Process(double delta)
    {
        TotalPlaySeconds += delta;
        _blindBoxTickTimer -= delta;
        if (_blindBoxTickTimer <= 0.0)
        {
            _blindBoxTickTimer = BlindBoxTickSeconds;
            EmitSignal(SignalName.BlindBoxStateChanged);
            QueueSaveIfUsingLocalSave();
        }

        if (!_saveDirty)
            return;

        _saveTimer -= delta;
        if (_saveTimer <= 0.0)
            FlushSave();
    }

    public override void _ExitTree()
    {
        FlushSave();
    }

    public void EquipItem(int itemId)
    {
        Inventory.Equip(itemId);
    }

    public void ToggleEquipItem(int itemId)
    {
        Inventory.ToggleEquip(itemId);
    }

    public void AddItem(int itemId, int count = 1, bool markNew = true)
    {
        Inventory.AddItem(itemId, count, markNew);
        QueueSaveIfUsingLocalSave();
    }

    public BlindBox GetNextAvailableBlindBox()
    {
        return _blindBoxService.GetNextAvailableBox(
            TotalPlaySeconds,
            _blindBoxClaimedCountsBySchedule,
            PendingBlindBoxReward);
    }

    public int GetBlindBoxDisplayCost(BlindBox box)
    {
        return _blindBoxService.GetDisplayCost(box);
    }

    public PendingBlindBoxReward TryOpenBlindBox()
    {
        if (PendingBlindBoxReward != null)
            return PendingBlindBoxReward;

        var result = _blindBoxService.TryOpenNext(TotalPlaySeconds, _blindBoxClaimedCountsBySchedule);
        if (result == null)
            return null;

        PendingBlindBoxReward = result.PendingReward;
        IncrementBlindBoxClaimedCount(result.Schedule.Id);
        EmitSignal(SignalName.BlindBoxStateChanged);
        QueueSaveIfUsingLocalSave();
        return PendingBlindBoxReward;
    }

    public void ClaimPendingBlindBoxReward()
    {
        if (PendingBlindBoxReward == null)
            return;

        var itemId = PendingBlindBoxReward.ItemId;
        PendingBlindBoxReward = null;
        AddItem(itemId, count: 1, markNew: true);
        EmitSignal(SignalName.BlindBoxStateChanged);
        QueueSaveIfUsingLocalSave();
    }

    public void ModifyChips(int delta)
    {
        Chips += delta;
        Progression.UpdateHighScore(Chips);
        EmitSignal(SignalName.ChipsChanged, Chips);
        QueueSaveIfUsingLocalSave();
    }

    public bool CanAffordBet => Chips >= BetAmount;

    public void ResetToStart()
    {
        Chips = StartingChips;
        TotalPlaySeconds = 0;
        PendingBlindBoxReward = null;
        _blindBoxClaimedCountsBySchedule.Clear();
        Progression.Reset();
        EmitSignal(SignalName.ChipsChanged, Chips);
        EmitSignal(SignalName.BlindBoxStateChanged);
        QueueSaveIfUsingLocalSave();
    }

    public void SetSaveDataMode(SettingsManager.SaveDataMode mode)
    {
        if (_saveDataMode == mode)
            return;

        FlushSave();
        _saveDataMode = mode;
        SettingsManager.SaveSaveDataMode(mode);
        LoadDataForCurrentMode();
        EmitSignal(SignalName.ChipsChanged, Chips);
        EmitSignal(SignalName.EquipmentChanged);
    }

    public void ResetLocalSave()
    {
        FlushSave();
        var profile = SaveManager.ResetLocalSave();
        if (_saveDataMode == SettingsManager.SaveDataMode.LocalSave)
        {
            Chips = profile.Chips;
            TotalPlaySeconds = profile.TotalPlaySeconds;
            LoadBlindBoxState(profile);
            Inventory.LoadState(profile.OwnedItemCounts, profile.EquippedItemIdsByType, profile.NewItemIds, emitChanged: false);
            EmitSignal(SignalName.ChipsChanged, Chips);
            EmitSignal(SignalName.EquipmentChanged);
            EmitSignal(SignalName.BlindBoxStateChanged);
        }
    }

    private void LoadDataForCurrentMode()
    {
        if (_saveDataMode == SettingsManager.SaveDataMode.LocalSave)
        {
            var profile = SaveManager.LoadOrCreate();
            Chips = profile.Chips;
            TotalPlaySeconds = profile.TotalPlaySeconds;
            LoadBlindBoxState(profile);
            Inventory.LoadState(profile.OwnedItemCounts, profile.EquippedItemIdsByType, profile.NewItemIds, emitChanged: false);
            QueueSaveIfUsingLocalSave();
            return;
        }

        Chips = StartingChips;
        TotalPlaySeconds = 0;
        PendingBlindBoxReward = null;
        _blindBoxClaimedCountsBySchedule.Clear();
        Inventory.ResetToDebugAllItems(emitChanged: false);
        _saveDirty = false;
        _saveTimer = 0.0;
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
        if (_saveDataMode != SettingsManager.SaveDataMode.LocalSave)
            return;

        _saveDirty = true;
        _saveTimer = SaveDebounceSeconds;
    }

    private void FlushSave()
    {
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
            BlindBoxClaimedCountsBySchedule = _blindBoxClaimedCountsBySchedule
                .OrderBy(pair => pair.Key)
                .ToDictionary(pair => pair.Key, pair => pair.Value),
            PendingBlindBoxReward = PendingBlindBoxReward,
        });
        _saveDirty = false;
    }

    private void IncrementBlindBoxClaimedCount(int scheduleId)
    {
        _blindBoxClaimedCountsBySchedule.TryGetValue(scheduleId, out var count);
        _blindBoxClaimedCountsBySchedule[scheduleId] = count + 1;
    }

    private void LoadBlindBoxState(SaveProfile profile)
    {
        _blindBoxClaimedCountsBySchedule.Clear();
        foreach (var (scheduleId, count) in profile.BlindBoxClaimedCountsBySchedule)
        {
            if (count > 0)
                _blindBoxClaimedCountsBySchedule[scheduleId] = count;
        }
        PendingBlindBoxReward = profile.PendingBlindBoxReward;
    }
}
