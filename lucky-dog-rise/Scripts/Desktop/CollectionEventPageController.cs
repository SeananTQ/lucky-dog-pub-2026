using Godot;
using DataTables;
using System;
using System.Collections.Generic;
using System.Linq;

namespace LuckyDogRise;

public sealed record CollectionEventDefinition(
    string EventId,
    IReadOnlyList<int> GrandPrizeItemIds,
    IReadOnlyList<int> CollectionItemIds);

public partial class CollectionEventPageController : VBoxContainer
{
    private const double RevealDelaySeconds = 1.0;
    private static readonly PackedScene RewardCellScene =
        GD.Load<PackedScene>("res://Scenes/Prefabs/CollectionEventRewardCell.tscn");

    [Export] private CollectionEventRewardCellController _grandPrizeLeft = null!;
    [Export] private CollectionEventRewardCellController _grandPrizeCenter = null!;
    [Export] private CollectionEventRewardCellController _grandPrizeRight = null!;
    [Export] private GridContainer _collectionGrid = null!;

    private readonly Dictionary<int, CollectionEventRewardCellController> _cellsByItemId = new();
    private GameData _gameData;
    private CollectionEventDefinition _definition;
    private int _revealRequestVersion;

    public override void _Ready()
    {
        VisibilityChanged += OnVisibilityChanged;
    }

    public override void _ExitTree()
    {
        UnbindGameData();
    }

    public void Configure(GameData gameData, CollectionEventDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(gameData);
        ArgumentNullException.ThrowIfNull(definition);

        UnbindGameData();
        _gameData = gameData;
        _definition = definition;
        _gameData.InventoryChanged += OnInventoryChanged;
        _gameData.CollectionEventStateChanged += OnCollectionEventStateChanged;
        RebuildRewardCells();
        RefreshPresentation();
    }

    public void NotifyPageEntered()
    {
        if (!IsVisibleInTree())
            return;

        RefreshPresentation();
        QueuePendingReveals();
    }

    private void UnbindGameData()
    {
        if (_gameData == null)
            return;

        _gameData.InventoryChanged -= OnInventoryChanged;
        _gameData.CollectionEventStateChanged -= OnCollectionEventStateChanged;
        _gameData = null;
    }

    private void RebuildRewardCells()
    {
        _revealRequestVersion++;
        _cellsByItemId.Clear();

        foreach (var child in _collectionGrid.GetChildren())
            child.QueueFree();

        var grandPrizeCells = new[] { _grandPrizeLeft, _grandPrizeCenter, _grandPrizeRight };
        for (var i = 0; i < grandPrizeCells.Length; i++)
        {
            var item = i < _definition.GrandPrizeItemIds.Count
                ? LubanData.Tables.TbItem.GetOrDefault(_definition.GrandPrizeItemIds[i])
                : null;
            grandPrizeCells[i].Visible = item != null;
            if (item != null)
            {
                grandPrizeCells[i].Setup(item, revealed: false, revealVariant: i + 1);
                _cellsByItemId[item.Id] = grandPrizeCells[i];
            }
        }

        for (var i = 0; i < _definition.CollectionItemIds.Count; i++)
        {
            var item = LubanData.Tables.TbItem.GetOrDefault(_definition.CollectionItemIds[i]);
            if (item == null || _cellsByItemId.ContainsKey(item.Id))
                continue;

            var cell = RewardCellScene.Instantiate<CollectionEventRewardCellController>();
            cell.CustomMinimumSize = new Vector2(58f, 58f);
            cell.Setup(item, revealed: false, revealVariant: i + 1);
            _collectionGrid.AddChild(cell);
            _cellsByItemId[item.Id] = cell;
        }
    }

    private void RefreshPresentation()
    {
        if (_gameData == null || _definition == null)
            return;

        var revealedIds = _gameData.GetCollectionEventRevealedItemIds(_definition.EventId);
        foreach (var (itemId, cell) in _cellsByItemId)
            cell.SetRevealed(revealedIds.Contains(itemId));
    }

    private void OnInventoryChanged()
    {
        RefreshPresentation();
        if (IsVisibleInTree())
            QueuePendingReveals();
    }

    private void OnCollectionEventStateChanged(string eventId)
    {
        if (_definition == null
            || !string.IsNullOrEmpty(eventId)
            && !string.Equals(eventId, _definition.EventId, StringComparison.Ordinal))
            return;

        RefreshPresentation();
    }

    private void OnVisibilityChanged()
    {
        if (IsVisibleInTree())
            NotifyPageEntered();
        else
            _revealRequestVersion++;
    }

    private async void QueuePendingReveals()
    {
        if (_gameData == null || _definition == null || !IsVisibleInTree())
            return;

        var ownedIds = _gameData.Inventory.GetOwnedIds().ToHashSet();
        var revealedIds = _gameData.GetCollectionEventRevealedItemIds(_definition.EventId);
        var pendingIds = _cellsByItemId.Keys
            .Where(itemId => ownedIds.Contains(itemId) && !revealedIds.Contains(itemId))
            .ToArray();
        if (pendingIds.Length == 0)
            return;

        var requestVersion = ++_revealRequestVersion;
        await ToSignal(GetTree().CreateTimer(RevealDelaySeconds), SceneTreeTimer.SignalName.Timeout);
        if (requestVersion != _revealRequestVersion || !IsVisibleInTree() || _gameData == null)
            return;

        var cells = pendingIds
            .Where(_cellsByItemId.ContainsKey)
            .Select(itemId => _cellsByItemId[itemId])
            .ToArray();
        if (cells.Length == 0)
            return;

        foreach (var cell in cells)
            cell.PlayReveal();
        await ToSignal(GetTree().CreateTimer(0.45), SceneTreeTimer.SignalName.Timeout);
        if (requestVersion != _revealRequestVersion || _gameData == null)
            return;

        _gameData.MarkCollectionEventItemsRevealed(_definition.EventId, pendingIds);
    }
}
