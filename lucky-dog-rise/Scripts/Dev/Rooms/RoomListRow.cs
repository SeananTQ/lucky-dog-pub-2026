#if DEBUG && !RECORDING_BUILD
using System;
using Godot;

namespace LuckyDogRise.Rooms;

public partial class RoomListRow : HBoxContainer
{
    [Export] private TextureRect _gameIcon = null!;
    [Export] private Label _name = null!;
    [Export] private Label _count = null!;
    [Export] private Button _join = null!;
    private bool _full;
    public void Bind(RoomListing room, Action join, bool busy = false)
    {
        _name.Text = room.Name;
        _name.TooltipText = room.Name + "\n" + room.Code;
        _count.Text = $"{room.Count}/{room.Capacity}";
        _gameIcon.TooltipText = room.GameId == "social" ? "一起待着（玩法图标占位）" : "玩法同步测试（非实际扑克）";
        _gameIcon.Modulate = room.GameId == "social" ? Colors.White : new Color(0.7f, 0.9f, 1f);
        _full = room.IsFull;
        SetRequestPending(busy);
        _join.Pressed += () => join();
    }
    public void SetRequestPending(bool busy) => _join.Disabled = busy || _full;
}
#endif
