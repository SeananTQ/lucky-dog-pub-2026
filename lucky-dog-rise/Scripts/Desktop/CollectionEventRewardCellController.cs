using Godot;
using DataTables;

namespace LuckyDogRise;

public enum CollectionEventRewardVisualState
{
    Covered,
    Revealed,
    Shining,
}

public partial class CollectionEventRewardCellController : PanelContainer
{
    [Export] private TextureRect _plate = null!;
    [Export] private TextureRect _icon = null!;
    [Export] private TextureRect _frame = null!;
    [Export] private TextureRect _revealedCover = null!;
    [Export] private TextureRect _shineCover = null!;
    [Export] private TextureRect _fullCover = null!;

    private Tween _revealTween;
    private int _revealVariant;
    private int _shineVariant;

    public int ItemId { get; private set; }

    public void Setup(
        Item item,
        CollectionEventRewardVisualState state,
        int revealVariant,
        int shineVariant)
    {
        ItemId = item.Id;
        _revealVariant = Mathf.PosMod(revealVariant - 1, 4) + 1;
        _shineVariant = Mathf.PosMod(shineVariant - 1, 3) + 1;
        _icon.Texture = PlayerInventory.LoadItemIconOrFallback(item.IconPath);
        LoadTextureOrClear(_plate, $"res://Assets/UI/ItemUI/Plate_{item.ItemRarity}.png");
        LoadTextureOrClear(_frame, $"res://Assets/UI/ItemUI/Frame_{item.ItemRarity}.png");
        _revealedCover.Texture = GD.Load<Texture2D>(
            $"res://Assets/Event/ItemUI_ScratchCover_Reveal{_revealVariant}.png");
        _shineCover.Texture = GD.Load<Texture2D>(
            $"res://Assets/Event/ItemUI_Shine_{_shineVariant}.png");
        SetVisualState(state);
        TooltipText = item.Name;
    }

    public void SetVisualState(CollectionEventRewardVisualState state)
    {
        _revealTween?.Kill();
        _revealTween = null;
        var rewardVisible = state != CollectionEventRewardVisualState.Covered;
        SetRewardVisualsVisible(rewardVisible);
        _revealedCover.Modulate = Colors.White;
        _revealedCover.Visible = state == CollectionEventRewardVisualState.Revealed;
        _shineCover.Modulate = Colors.White;
        _shineCover.Visible = state == CollectionEventRewardVisualState.Shining;
        _fullCover.Modulate = Colors.White;
        _fullCover.Visible = state == CollectionEventRewardVisualState.Covered;
    }

    public Tween PlayReveal()
    {
        _revealTween?.Kill();
        SetRewardVisualsVisible(true);
        _revealedCover.Visible = true;
        _revealedCover.Modulate = Colors.White;
        _shineCover.Visible = false;
        _fullCover.Visible = true;
        _fullCover.Modulate = Colors.White;

        _revealTween = CreateTween();
        _revealTween.TweenProperty(_fullCover, "modulate:a", 0f, 1.0)
            .SetTrans(Tween.TransitionType.Cubic)
            .SetEase(Tween.EaseType.InOut);
        _revealTween.Finished += () =>
        {
            _fullCover.Visible = false;
            _fullCover.Modulate = Colors.White;
            _revealedCover.Modulate = Colors.White;
            _revealTween = null;
        };
        return _revealTween;
    }

    private void SetRewardVisualsVisible(bool visible)
    {
        _plate.Visible = visible;
        _icon.Visible = visible;
        _frame.Visible = visible;
    }

    private static void LoadTextureOrClear(TextureRect rect, string path)
    {
        rect.Texture = ResourceLoader.Exists(path) ? GD.Load<Texture2D>(path) : null;
    }
}
