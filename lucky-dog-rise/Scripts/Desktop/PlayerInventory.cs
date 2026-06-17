using System;
using System.Collections.Generic;
using System.Linq;
using DataTables;

namespace LuckyDogRise;

public class PlayerInventory
{
    private readonly HashSet<int> _ownedIds = new();
    private readonly Dictionary<EItemType, int> _equipped = new();

    public event Action? EquipmentChanged;

    public PlayerInventory()
    {
        foreach (var item in LubanData.Tables.TbItem.DataList)
        {
            _ownedIds.Add(item.Id); // 临时：拥有全部道具用于测试
        }

        // 自动装备每种类型的第一个道具
        foreach (EItemType type in Enum.GetValues(typeof(EItemType)))
        {
            var first = GetOwnedOfType(type).FirstOrDefault();
            if (first != null)
                _equipped[type] = first.Id;
        }
    }

    public bool Owns(int itemId) => _ownedIds.Contains(itemId);

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
        if (item == null || !_ownedIds.Contains(itemId)) return;

        _equipped[item.ItemType] = itemId;
        EquipmentChanged?.Invoke();
    }

    public IEnumerable<Item> GetOwnedOfType(EItemType type)
    {
        return LubanData.Tables.TbItem.DataList
            .Where(item => _ownedIds.Contains(item.Id) && item.ItemType == type)
            .OrderByDescending(item => item.SortOrder);
    }

    public Item? GetDefaultOfType(EItemType type)
    {
        return GetOwnedOfType(type).FirstOrDefault();
    }

    private static Item? FindItem(int id)
    {
        return LubanData.Tables.TbItem.DataList.FirstOrDefault(item => item.Id == id);
    }

    public static string ToResPath(string lubanPath)
    {
        return "res://Assets/" + lubanPath.Replace('\\', '/');
    }
}
