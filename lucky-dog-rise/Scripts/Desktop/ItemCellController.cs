using Godot;
using DataTables;

namespace LuckyDogRise;

public partial class ItemCellController : PanelContainer
{
    [Signal] public delegate void PressedEventHandler();

    [Export] private TextureRect _iconRect = null!;
    [Export] private TextureRect _markEquipped = null!;
    [Export] private TextureRect _markNew = null!;
    [Export] private Control _countBadge = null!;
    [Export] private Label _countLabel = null!;

    private TextureRect _plate = null!;
    private TextureRect _frame = null!;

    public int ItemId { get; private set; }

    public void Setup(Item item, bool isEquipped, int count = 1, bool isNew = false)
    {
        _plate ??= GetNode<TextureRect>("Content/Plate");
        _frame ??= GetNode<TextureRect>("Content/Frame");

        ItemId = item.Id;

        var iconPath = PlayerInventory.ToResPath(item.IconPath);
        if (ResourceLoader.Exists(iconPath))
            _iconRect.Texture = GD.Load<Texture2D>(iconPath);

        // 品质底板和边框
        LoadTextureOrClear(_plate, $"res://Assets/UI/ItemUI/Plate_{item.ItemRarity}.png");
        LoadTextureOrClear(_frame, $"res://Assets/UI/ItemUI/Frame_{item.ItemRarity}.png");

        _markEquipped.Visible = isEquipped;
        _markNew.Visible = isNew;
        _countBadge.Visible = count > 1;
        _countLabel.Text = $"x{count}";
    }

    private static void LoadTextureOrClear(TextureRect rect, string path)
    {
        rect.Texture = ResourceLoader.Exists(path) ? GD.Load<Texture2D>(path) : null;
    }

    public override void _GuiInput(InputEvent @event)
    {
        if (@event is InputEventMouseButton mb && mb.Pressed && mb.ButtonIndex == MouseButton.Left)
            EmitSignal(SignalName.Pressed);
    }
}
