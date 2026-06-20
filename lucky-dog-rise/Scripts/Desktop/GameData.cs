using Godot;
using DataTables;
using System.Linq;

namespace LuckyDogRise;

public partial class GameData : Node
{
    public const int StartingChips = 1000;

    [Signal] public delegate void ChipsChangedEventHandler(int chips);
    [Signal] public delegate void HandResolvedEventHandler(EHandRank rank, int payout);
    [Signal] public delegate void NewHandStartedEventHandler();
    [Signal] public delegate void EquipmentChangedEventHandler();

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
    public int BetAmount => 50;
    public ProgressionManager Progression { get; } = new();

    private SettingsManager.SaveDataMode _saveDataMode;
    private bool _saveDirty;
    private double _saveTimer;
    private const double SaveDebounceSeconds = 0.75;

    public override void _Ready()
    {
        _saveDataMode = SettingsManager.LoadSaveDataMode();
        LoadDataForCurrentMode();
        Inventory.EquipmentChanged += OnInventoryEquipmentChanged;
        EmitSignal(SignalName.ChipsChanged, Chips);
        EmitSignal(SignalName.EquipmentChanged);
    }

    public override void _Process(double delta)
    {
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
        Progression.Reset();
        EmitSignal(SignalName.ChipsChanged, Chips);
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
            Inventory.LoadState(profile.OwnedItemIds, profile.EquippedItemIdsByType, emitChanged: false);
            EmitSignal(SignalName.ChipsChanged, Chips);
            EmitSignal(SignalName.EquipmentChanged);
        }
    }

    private void LoadDataForCurrentMode()
    {
        if (_saveDataMode == SettingsManager.SaveDataMode.LocalSave)
        {
            var profile = SaveManager.LoadOrCreate();
            Chips = profile.Chips;
            Inventory.LoadState(profile.OwnedItemIds, profile.EquippedItemIdsByType, emitChanged: false);
            QueueSaveIfUsingLocalSave();
            return;
        }

        Chips = StartingChips;
        Inventory.ResetToDebugAllItems(emitChanged: false);
        _saveDirty = false;
        _saveTimer = 0.0;
    }

    private void OnInventoryEquipmentChanged()
    {
        EmitSignal(SignalName.EquipmentChanged);
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
            OwnedItemIds = Inventory.GetOwnedIds().ToList(),
            EquippedItemIdsByType = Inventory.GetEquippedIdsByTypeName(),
        });
        _saveDirty = false;
    }
}
