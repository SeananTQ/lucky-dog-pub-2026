#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using DataTables;
using Godot;

namespace LuckyDogRise;

public class PlayerInventory
{
    private readonly Dictionary<int, int> _ownedItemCounts = new();
    private readonly Dictionary<EItemType, int> _equipped = new();
    private readonly HashSet<int> _newItemIds = new();
#if DEBUG
    // 录制辅助用的临时视觉覆盖，不进入存档，也不改变实际拥有状态。
    private readonly Dictionary<EItemType, int> _debugPreviewEquipped = new();
    private readonly HashSet<EItemType> _debugPreviewActiveTypes = new();
#endif

    public event Action? EquipmentChanged;
    public event Action? InventoryChanged;

    public PlayerInventory()
    {
#if DEBUG
        ResetToDebugAllItems(emitChanged: false);
#endif
    }

    public bool Owns(int itemId) => _ownedItemCounts.TryGetValue(itemId, out var count) && count > 0;

    public int GetCount(int itemId) => _ownedItemCounts.TryGetValue(itemId, out var count) ? count : 0;

    public bool IsNew(int itemId) => _newItemIds.Contains(itemId);

    public bool IsEquipped(int itemId)
    {
        var item = FindItem(itemId);
        return item != null && _equipped.TryGetValue(item.ItemType, out var equippedId) && equippedId == itemId;
    }

    public Item? GetEquipped(EItemType type)
    {
#if DEBUG
        if (_debugPreviewActiveTypes.Contains(type))
            return _debugPreviewEquipped.TryGetValue(type, out var previewId) ? FindItem(previewId) : null;
#endif
        if (_equipped.TryGetValue(type, out var id))
            return FindItem(id);
        return null;
    }

    public void Equip(int itemId)
    {
        var item = FindItem(itemId);
        if (item == null || !Owns(itemId)) return;
#if DEBUG
        ClearDebugPreviewForType(item.ItemType);
#endif

        if (_equipped.TryGetValue(item.ItemType, out var equippedId) && equippedId == itemId)
            return;

        _equipped[item.ItemType] = itemId;
        ClearNew(itemId, emitChanged: false);
        EquipmentChanged?.Invoke();
        InventoryChanged?.Invoke();
    }

    public void ToggleEquip(int itemId)
    {
        var item = FindItem(itemId);
        if (item == null || !Owns(itemId)) return;
#if DEBUG
        ClearDebugPreviewForType(item.ItemType);
#endif

        if (_equipped.TryGetValue(item.ItemType, out var equippedId) && equippedId == itemId)
        {
            var clearedNew = ClearNew(itemId, emitChanged: false);
            if (clearedNew)
            {
                InventoryChanged?.Invoke();
                return;
            }

            if (CanUnequip(item.ItemType))
            {
                _equipped.Remove(item.ItemType);
                EquipmentChanged?.Invoke();
                InventoryChanged?.Invoke();
            }
            return;
        }

        ClearNew(itemId, emitChanged: false);
        Equip(itemId);
    }

    public IEnumerable<Item> GetOwnedOfType(EItemType type)
    {
        return LubanData.Tables.TbItem.DataList
            .Where(item => Owns(item.Id) && item.ItemType == type)
            .OrderByDescending(item => IsNew(item.Id))
            .ThenByDescending(item => item.SortOrder)
            .ThenBy(item => item.Id);
    }

    public Item? GetDefaultOfType(EItemType type)
    {
        return GetOwnedOfType(type).FirstOrDefault();
    }

    public Dictionary<int, int> GetOwnedItemCounts()
    {
        return _ownedItemCounts
            .Where(pair => pair.Value > 0)
            .OrderBy(pair => pair.Key)
            .ToDictionary(pair => pair.Key, pair => pair.Value);
    }

    public IReadOnlyCollection<int> GetOwnedIds()
    {
        return GetOwnedItemCounts().Keys.ToArray();
    }

    public IReadOnlyCollection<int> GetNewItemIds()
    {
        return _newItemIds.OrderBy(id => id).ToArray();
    }

    public Dictionary<string, int> GetEquippedIdsByTypeName()
    {
        return _equipped
            .OrderBy(pair => pair.Key.ToString())
            .ToDictionary(pair => pair.Key.ToString(), pair => pair.Value);
    }

#if DEBUG
    /// <summary>只用于 Debug 录制展示；null 表示该类型临时留空。</summary>
    public void SetDebugPreviewEquipment(IReadOnlyDictionary<EItemType, int?> selections)
    {
        _debugPreviewEquipped.Clear();
        _debugPreviewActiveTypes.Clear();

        foreach (var (type, itemId) in selections)
        {
            _debugPreviewActiveTypes.Add(type);
            if (!itemId.HasValue)
                continue;

            var item = FindItem(itemId.Value);
            if (item != null && item.ItemType == type)
                _debugPreviewEquipped[type] = item.Id;
        }

        EquipmentChanged?.Invoke();
    }

    private void ClearDebugPreviewForType(EItemType type)
    {
        bool removed = _debugPreviewActiveTypes.Remove(type);
        _debugPreviewEquipped.Remove(type);
        if (removed)
            EquipmentChanged?.Invoke();
    }

    public void ResetToDebugAllItems(bool emitChanged = true)
    {
        _ownedItemCounts.Clear();
        _newItemIds.Clear();
        _debugPreviewEquipped.Clear();
        _debugPreviewActiveTypes.Clear();
        foreach (var item in LubanData.Tables.TbItem.DataList)
            _ownedItemCounts[item.Id] = 1; // 临时：拥有全部道具用于测试

        ApplyDefaultEquipment();
        if (emitChanged)
        {
            EquipmentChanged?.Invoke();
            InventoryChanged?.Invoke();
        }
    }
#endif

    public void LoadState(
        IReadOnlyDictionary<int, int> ownedItemCounts,
        IReadOnlyDictionary<string, int> equippedIdsByTypeName,
        IEnumerable<int>? newItemIds = null,
        bool emitChanged = true)
    {
        _ownedItemCounts.Clear();
        _equipped.Clear();
        _newItemIds.Clear();
#if DEBUG
        _debugPreviewEquipped.Clear();
        _debugPreviewActiveTypes.Clear();
#endif

        var validIds = LubanData.Tables.TbItem.DataList.Select(item => item.Id).ToHashSet();
        foreach (var (id, count) in ownedItemCounts)
        {
            if (validIds.Contains(id) && count > 0)
                _ownedItemCounts[id] = count;
        }

        foreach (var (typeName, itemId) in equippedIdsByTypeName)
        {
            if (!Enum.TryParse<EItemType>(typeName, out var type))
                continue;

            var item = FindItem(itemId);
            if (item == null || item.ItemType != type || !Owns(itemId))
                continue;

            _equipped[type] = itemId;
        }

        if (newItemIds != null)
        {
            foreach (var id in newItemIds.Where(id => validIds.Contains(id) && Owns(id)).Distinct())
                _newItemIds.Add(id);
        }

        ApplyDefaultEquipment();
        if (emitChanged)
        {
            EquipmentChanged?.Invoke();
            InventoryChanged?.Invoke();
        }
    }

    public void LoadState(IEnumerable<int> ownedItemIds, IReadOnlyDictionary<string, int> equippedIdsByTypeName, bool emitChanged = true)
    {
        LoadState(
            ownedItemIds.Distinct().ToDictionary(id => id, _ => 1),
            equippedIdsByTypeName,
            newItemIds: null,
            emitChanged);
    }

    public void AddItem(int itemId, int count = 1, bool markNew = true, bool autoEquipIfSlotEmpty = true)
    {
        var item = FindItem(itemId);
        if (item == null || count <= 0)
            return;

        _ownedItemCounts[itemId] = GetCount(itemId) + count;
        if (markNew)
            _newItemIds.Add(itemId);

        var equipmentChanged = autoEquipIfSlotEmpty && EquipAcquiredItemIfSlotEmpty(item);
        if (equipmentChanged)
            EquipmentChanged?.Invoke();
        InventoryChanged?.Invoke();
    }

    public bool ClearNew(int itemId, bool emitChanged = true)
    {
        if (!_newItemIds.Remove(itemId))
            return false;

        if (emitChanged)
            InventoryChanged?.Invoke();
        return true;
    }

    public bool CanUnequip(EItemType type)
    {
        var config = LubanData.Tables.TbEquipmentSlotConfig.GetOrDefault(type);
        return config != null && string.Equals(config.CanUnequip, "True", StringComparison.OrdinalIgnoreCase);
    }

    private static Item? FindItem(int id)
    {
        return LubanData.Tables.TbItem.DataList.FirstOrDefault(item => item.Id == id);
    }

    private bool ApplyDefaultEquipment()
    {
        var changed = false;
        // 枚举值与装备位表应保持同步。未配置的类型先跳过默认装备补齐，
        // 但明确记录 Warning，避免新装扮位漏配表时被静默掩盖。
        foreach (EItemType type in Enum.GetValues(typeof(EItemType)))
        {
            var slot = LubanData.Tables.TbEquipmentSlotConfig.GetOrDefault(type);
            if (slot == null)
            {
                GD.PushWarning($"[Inventory] Item type has no equipment slot config; skipping default equipment: {type}");
                continue;
            }

            if (_equipped.ContainsKey(type))
                continue;

            if (string.Equals(slot.CanUnequip, "True", StringComparison.OrdinalIgnoreCase))
                continue;

            Item? selected = null;
            if (slot.DefaultItemId > 0)
            {
                var configuredDefault = FindItem(slot.DefaultItemId);
                if (configuredDefault == null)
                {
                    GD.PushWarning(
                        $"[Inventory] Configured default item does not exist: {type} -> {slot.DefaultItemId}; " +
                        "falling back to the first owned item of this type.");
                }
                else if (configuredDefault.ItemType != type)
                {
                    GD.PushWarning(
                        $"[Inventory] Configured default item has the wrong type: {type} -> " +
                        $"{slot.DefaultItemId} ({configuredDefault.ItemType}); falling back to the first owned item of this type.");
                }
                else if (!Owns(configuredDefault.Id))
                {
                    GD.PushWarning(
                        $"[Inventory] Configured default item is not owned: {type} -> {slot.DefaultItemId}; " +
                        "falling back to the first owned item of this type.");
                }
                else
                {
                    selected = configuredDefault;
                }
            }
            else
            {
                GD.PushWarning(
                    $"[Inventory] Required equipment slot has no configured default item: {type}; " +
                    "falling back to the first owned item of this type.");
            }

            selected ??= GetOwnedOfType(type).FirstOrDefault();
            if (selected != null)
            {
                _equipped[type] = selected.Id;
                changed = true;
            }
            else
            {
                GD.PushError($"[Inventory] Required equipment slot has no owned item: {type}");
            }
        }

        return changed;
    }

    private bool EquipAcquiredItemIfSlotEmpty(Item item)
    {
        if (_equipped.ContainsKey(item.ItemType))
            return false;

        _equipped[item.ItemType] = item.Id;
        return true;
    }

    public static string ToResPath(string lubanPath)
    {
        return "res://Assets/" + lubanPath.Replace('\\', '/');
    }
}
