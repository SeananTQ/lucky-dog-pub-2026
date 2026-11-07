#if !DEMO_BUILD && !RECORDING_BUILD
using System.Collections.Generic;
using Godot;

namespace LuckyDogRise.Rooms;

public partial class RoomPokerHandView : Control
{
    [Export] private RoomPokerCardView _first;
    [Export] private RoomPokerCardView _second;
    [Export] private Label _status;
    public bool CardsRevealed => _first.VisibleCard >= 0 && _second.VisibleCard >= 0;
    public void Present(IReadOnlyList<int> cards, string status, bool active)
    {
        Show();
        _first.Present(cards?.Count == 2 ? cards[0] : -1, cards?.Count == 2, false);
        _second.Present(cards?.Count == 2 ? cards[1] : -1, cards?.Count == 2, false);
        _status.Text = status;
        Modulate = active ? Colors.White : new Color(0.88f, 0.93f, 0.94f);
    }
}
#endif
