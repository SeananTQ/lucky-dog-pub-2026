#if DEBUG && !RECORDING_BUILD
using System;
using Godot;

namespace LuckyDogRise.Rooms;

public partial class InGameRoomDirectoryRow : HBoxContainer
{
    [Export] private Label _name = null!;
    [Export] private Label _count = null!;
    [Export] private Button _join = null!;
    private bool _full;
    public void Bind(RoomListing room, bool busy, Action join)
    {
        _name.Text = room.Name;
        _name.TooltipText = room.Name;
        _count.Text = $"{room.Count}/{room.Capacity}";
        _full = room.Count >= room.Capacity;
        SetBusy(busy);
        _join.Pressed += join;
    }
    public void SetBusy(bool busy) => _join.Disabled = busy || _full;
}
#endif
