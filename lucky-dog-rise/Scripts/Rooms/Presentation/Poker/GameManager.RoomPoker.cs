#if !DEMO_BUILD && !RECORDING_BUILD
using Godot;
using LuckyDogRise.Rooms;

namespace LuckyDogRise;

public partial class GameManager
{
    public bool CanBeginRoomPoker => !IsSessionPokerActive && State == GameState.WaitingForBet && _pendingReward == null
        && !_blindBoxOverlay.Visible && !_itemArea.IsUseBalloonOpen;

    public void DiscardSoloHandForRoomPoker()
    {
        if (IsSessionPokerActive || State == GameState.WaitingForBet && _pendingReward == null && _pendingPayout == 0) return;
        ++_soloHandGeneration;
        if (IsInstanceValid(_pendingReward)) { _pendingReward.Hide(); _pendingReward.QueueFree(); }
        _pendingReward = null; _pendingPayout = 0;
        _cardTable.ClearCards();
        _handArea.Enabled = false;
        SetState(GameState.WaitingForBet);
        _chipStack.ShowAfterRewardCollected();
        _hud.SetMessage("");
    }

    public RoomPokerTableView BeginRoomPoker(RoomPokerTableView existing = null)
    {
        if (IsSessionPokerActive) return null;
        if (existing == null && !CanBeginRoomPoker) return null;
        DiscardSoloHandForRoomPoker();
        IsSessionPokerActive = true;
        _quickBar.SetSoloGuideVisible(false);
        _cardTable.Hide();
        _chipStack.Hide();
        _itemArea.Hide();
        _handArea.Enabled = false;
        var table = existing ?? GD.Load<PackedScene>("res://Scenes/Rooms/Poker/RoomPokerTable.tscn").Instantiate<RoomPokerTableView>();
        if (table.GetParent() != null) table.Reparent(this, false); else AddChild(table);
        table.Show();
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
