#if !DEMO_BUILD && !RECORDING_BUILD
using Godot;
using LuckyDogRise.Rooms;

namespace LuckyDogRise;

public partial class GameManager
{
    public bool CanBeginRoomPoker => State == GameState.WaitingForBet && _pendingReward == null
        && !_blindBoxOverlay.Visible && !_itemArea.IsUseBalloonOpen;

    public RoomPokerTableView BeginRoomPoker()
    {
        if (IsSessionPokerActive || !CanBeginRoomPoker) return null;
        IsSessionPokerActive = true;
        _quickBar.SetSoloGuideVisible(false);
        _cardTable.Hide();
        _chipStack.Hide();
        _itemArea.Hide();
        _handArea.Enabled = false;
        var table = GD.Load<PackedScene>("res://Scenes/Rooms/Poker/RoomPokerTable.tscn").Instantiate<RoomPokerTableView>();
        AddChild(table);
        RefreshOverlayInteractionContexts();
        RefreshInteractionHintTargets();
        return table;
    }

    public void SetRoomPokerInput(bool enabled) => _handArea.Enabled = IsSessionPokerActive && enabled;

    public void EndRoomPoker()
    {
        if (!IsSessionPokerActive) return;
        IsSessionPokerActive = false;
        _quickBar.SetSoloGuideVisible(true);
        _handArea.Enabled = false;
        _cardTable.Show();
        _chipStack.Show();
        _itemArea.Show();
        RefreshOverlayInteractionContexts();
        RefreshInteractionHintTargets();
    }
}
#endif
