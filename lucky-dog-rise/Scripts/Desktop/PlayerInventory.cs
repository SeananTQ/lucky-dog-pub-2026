using System.Collections.Generic;
using System.Linq;
using DataTables;

namespace LuckyDogRise;

public class PlayerInventory
{
    private readonly HashSet<int> _ownedIds = new();

    public PlayerInventory()
    {
        foreach (var item in LubanData.Tables.TbItem.DataList)
        {
            if (item.BlindBoxId == 0)
                _ownedIds.Add(item.Id);
        }
    }

    public bool Owns(int itemId) => _ownedIds.Contains(itemId);

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

    /// <summary>
    /// 将 Luban AssetPathList 路径转为 Godot res:// 路径
    /// "v1\Eyewear\Sunglasses_Blade.png" → "res://Assets/v1/Eyewear/Sunglasses_Blade.png"
    /// </summary>
    public static string ToResPath(string lubanPath)
    {
        return "res://Assets/" + lubanPath.Replace('\\', '/');
    }
}
