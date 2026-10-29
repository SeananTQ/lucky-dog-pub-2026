#if !DEMO_BUILD && !RECORDING_BUILD
using System;
using Godot;

namespace LuckyDogRise.Rooms;

public partial class RoomPokerDisplayChoice : Control
{
    [Export] private Button _desktop = null!;
    [Export] private Button _row = null!;
    [Export] private Button _cancel = null!;
    private Action<RoomPokerDisplay> _selected;
    public override void _EnterTree() => UiInteractionOccupancy.TrackVisibility(this);
    public override void _Ready()
    {
        _desktop.Pressed += () => Select(RoomPokerDisplay.Desktop);
        _row.Pressed += () => Select(RoomPokerDisplay.Row);
        _cancel.Pressed += Dismiss;
    }
    public void Present(Action<RoomPokerDisplay> selected)
    {
        _selected = selected;
        Show();
        // Focus the overlay, not one of the answers.
        GrabFocus();
    }
    public void Dismiss() { _selected = null; Hide(); }
    private void Select(RoomPokerDisplay mode)
    {
        if (!IsVisibleInTree()) return;
        var selected = _selected;
        Dismiss();
        selected?.Invoke(mode);
    }
    public override void _GuiInput(InputEvent input)
    {
        if (input is InputEventKey { Pressed: true, Keycode: Key.Escape })
        { Dismiss(); AcceptEvent(); }
    }
}
#endif
