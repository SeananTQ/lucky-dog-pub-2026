#nullable enable

using System.Collections.Generic;
using System.Linq;
using DataTables;
using Godot;

namespace LuckyDogRise;

public partial class RecoveredItemsOverlayController : Control
{
    [Signal] public delegate void ConfirmedEventHandler();
    [Export] public bool IsRewardSummary { get; set; }

    private static readonly PackedScene ItemCellScene =
        GD.Load<PackedScene>("res://Scenes/Prefabs/ItemCell.tscn");

    private Label _title = null!;
    private Label _detail = null!;
    private GridContainer _itemGrid = null!;
    private ScrollContainer _scroll = null!;
    private Button _confirmButton = null!;

    public override void _EnterTree() => UiInteractionOccupancy.TrackVisibility(this);

    public override void _Ready()
    {
        _title = GetNode<Label>("OverlayPanel/Margin/Content/Title");
        _detail = GetNode<Label>("OverlayPanel/Margin/Content/Detail");
        _scroll = GetNode<ScrollContainer>("OverlayPanel/Margin/Content/ItemsPanel/ItemsScroll");
        if (IsRewardSummary)
            _scroll.GetParent<Control>().SizeFlagsStretchRatio = 6f;
        _itemGrid = _scroll.GetNode<GridContainer>("ItemGrid");
        _confirmButton = GetNode<Button>("OverlayPanel/Margin/Content/ConfirmButton");
        _confirmButton.Pressed += () => EmitSignal(SignalName.Confirmed);
        RefreshLocalizedText();
    }

    public void ShowItems(IReadOnlyDictionary<int, int> itemCounts) =>
        PopulateItems(itemCounts.OrderBy(pair => pair.Key));

    public void ShowRewards(IEnumerable<int> itemIds) =>
        PopulateItems(itemIds.Select(id => KeyValuePair.Create(id, 1)));

    private void PopulateItems(IEnumerable<KeyValuePair<int, int>> items)
    {
        foreach (var child in _itemGrid.GetChildren())
        {
            _itemGrid.RemoveChild(child);
            child.QueueFree();
        }

        foreach (var (itemId, count) in items)
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
        if (Visible && IsRewardSummary)
        {
            _scroll.ScrollVertical = 0;
            _confirmButton.GrabFocus();
        }
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

        _detail.Visible = !IsRewardSummary;
        if (IsRewardSummary)
        {
            _title.Text = L10n.Format(L10nKey.LinkTree_ItemsReceived, _itemGrid.GetChildCount());
            _confirmButton.Text = L10n.Tr(L10nKey.Common_Confirm);
            return;
        }
        _title.Text = L10n.Tr(L10nKey.RecoveredItems_Title);
        _detail.Text = L10n.Tr(L10nKey.RecoveredItems_Detail);
        _confirmButton.Text = L10n.Tr(L10nKey.RecoveredItems_Confirm);
    }
}
