#nullable enable

using System.Collections.Generic;
using System.Linq;
using DataTables;
using Godot;

namespace LuckyDogRise;

public partial class RecoveredItemsOverlayController : Control
{
    [Signal] public delegate void ConfirmedEventHandler();

    private static readonly PackedScene ItemCellScene =
        GD.Load<PackedScene>("res://Scenes/Prefabs/ItemCell.tscn");

    private Label _title = null!;
    private Label _detail = null!;
    private GridContainer _itemGrid = null!;
    private Button _confirmButton = null!;

    public override void _Ready()
    {
        _title = GetNode<Label>("OverlayPanel/Margin/Content/Title");
        _detail = GetNode<Label>("OverlayPanel/Margin/Content/Detail");
        _itemGrid = GetNode<GridContainer>("OverlayPanel/Margin/Content/ItemsPanel/ItemsScroll/ItemGrid");
        _confirmButton = GetNode<Button>("OverlayPanel/Margin/Content/ConfirmButton");
        _confirmButton.Pressed += () => EmitSignal(SignalName.Confirmed);
        RefreshLocalizedText();
    }

    public void ShowItems(IReadOnlyDictionary<int, int> itemCounts)
    {
        foreach (var child in _itemGrid.GetChildren())
            child.QueueFree();

        foreach (var (itemId, count) in itemCounts.OrderBy(pair => pair.Key))
        {
            var item = LubanData.Tables.TbItem.GetOrDefault(itemId);
            if (item == null || count <= 0)
                continue;

            var cell = ItemCellScene.Instantiate<ItemCellController>();
            cell.MouseFilter = MouseFilterEnum.Ignore;
            cell.TooltipText = item.Name;
            cell.Setup(item, isEquipped: false, count, isNew: false);
            _itemGrid.AddChild(cell);
        }

        RefreshLocalizedText();
        Visible = _itemGrid.GetChildCount() > 0;
    }

    public void HideOverlay() => Visible = false;

    public void SetOverlayRect(Vector2 position, Vector2 size)
    {
        Position = position;
        Size = size;
    }

    public void RefreshLocalizedText()
    {
        if (!IsNodeReady())
            return;

        _title.Text = L10n.Tr(L10nKey.RecoveredItems_Title);
        _detail.Text = L10n.Tr(L10nKey.RecoveredItems_Detail);
        _confirmButton.Text = L10n.Tr(L10nKey.RecoveredItems_Confirm);
    }
}
