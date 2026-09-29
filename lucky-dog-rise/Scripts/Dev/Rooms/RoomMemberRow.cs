#if DEBUG && !RECORDING_BUILD
using System;
using Godot;

namespace LuckyDogRise.Rooms;

public partial class RoomMemberRow : HBoxContainer
{
    [Export] private Label _name = null!;
    [Export] private Button _hide = null!;
    public void Bind(RoomMember member, bool owner, bool local, bool hidden, Action toggle)
    {
        _name.Text = (owner ? "♛ " : "") + member.Name + (local ? " · 本机" : "");
        _hide.Text = hidden ? "显示" : "隐藏";
        _hide.Visible = !local;
        _hide.Pressed += () => toggle();
    }
}
#endif
