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

    public event Action? EquipmentChanged;
    public event Action? InventoryChanged;

    public PlayerInventory()
    {
        ResetToDebugAllItems(emitChanged: false);
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
        if (_equipped.TryGetValue(type, out var id))
            return FindItem(id);
        return null;
    }

    public void Equip(int itemId)
    {
        var item = FindItem(itemId);
        if (item == null || !Owns(itemId)) return;

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

        var clearedNew = ClearNew(itemId, emitChanged: false);

        if (_equipped.TryGetValue(item.ItemType, out var equippedId) && equippedId == itemId)
        {
            if (CanUnequip(item.ItemType))
            {
                _equipped.Remove(item.ItemType);
                EquipmentChanged?.Invoke();
                InventoryChanged?.Invoke();
            }
            else if (clearedNew)
            {
                InventoryChanged?.Invoke();
            }
            return;
        }

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

    public void ResetToDebugAllItems(bool emitChanged = true)
    {
        _ownedItemCounts.Clear();
        _newItemIds.Clear();
        foreach (var item in LubanData.Tables.TbItem.DataList)
            _ownedItemCounts[item.Id] = 1; // 临时：拥有全部道具用于测试

        ApplyDefaultEquipment();
        if (emitChanged)
        {
            EquipmentChanged?.Invoke();
            InventoryChanged?.Invoke();
        }
    }

    public void LoadState(
        IReadOnlyDictionary<int, int> ownedItemCounts,
        IReadOnlyDictionary<string, int> equippedIdsByTypeName,
        IEnumerable<int>? newItemIds = null,
        bool emitChanged = true)
    {
        _ownedItemCounts.Clear();
        _equipped.Clear();
        _newItemIds.Clear();

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

    public void AddItem(int itemId, int count = 1, bool markNew = true)
    {
        var item = FindItem(itemId);
        if (item == null || count <= 0)
            return;

        _ownedItemCounts[itemId] = GetCount(itemId) + count;
        if (markNew)
            _newItemIds.Add(itemId);

        var equipmentChanged = ApplyDefaultEquipment();
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
        // 新游戏默认装备玩家拥有的每种类型第一个道具；CanUnequip 只控制之后能不能脱下。
        foreach (EItemType type in Enum.GetValues(typeof(EItemType)))
        {
            if (_equipped.ContainsKey(type))
                continue;

            var first = GetOwnedOfType(type).FirstOrDefault();
            if (first != null)
            {
                _equipped[type] = first.Id;
                changed = true;
            }
            else if (!CanUnequip(type))
            {
                GD.PushError($"[Inventory] Required equipment slot has no owned item: {type}");
            }
        }

        return changed;
    }

    public static string ToResPath(string lubanPath)
    {
        return "res://Assets/" + lubanPath.Replace('\\', '/');
    }
}
