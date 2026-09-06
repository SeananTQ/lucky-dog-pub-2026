using Godot;
using DataTables;

namespace LuckyDogRise;

public partial class CollectionEventRewardCellController : PanelContainer
{
    [Export] private TextureRect _plate = null!;
    [Export] private TextureRect _icon = null!;
    [Export] private TextureRect _frame = null!;
    [Export] private TextureRect _revealedCover = null!;
    [Export] private TextureRect _fullCover = null!;

    private Tween _revealTween;

    public int ItemId { get; private set; }

    public void Setup(Item item, bool revealed, int revealVariant)
    {
        ItemId = item.Id;
        _icon.Texture = PlayerInventory.LoadItemIconOrFallback(item.IconPath);
        LoadTextureOrClear(_plate, $"res://Assets/UI/ItemUI/Plate_{item.ItemRarity}.png");
        LoadTextureOrClear(_frame, $"res://Assets/UI/ItemUI/Frame_{item.ItemRarity}.png");
        _revealedCover.Texture = LoadRevealCover(revealVariant);
        SetRevealed(revealed);
        TooltipText = item.Name;
    }

    public void SetRevealed(bool revealed)
    {
        _revealTween?.Kill();
        _revealTween = null;
        _revealedCover.Modulate = Colors.White;
        _revealedCover.Visible = revealed;
        _fullCover.Modulate = Colors.White;
        _fullCover.Visible = !revealed;
    }

    public Tween PlayReveal()
    {
        _revealTween?.Kill();
        _revealedCover.Visible = true;
        _revealedCover.Modulate = new Color(1f, 1f, 1f, 0f);
        _fullCover.Visible = true;
        _fullCover.Modulate = Colors.White;

        _revealTween = CreateTween().SetParallel();
        _revealTween.TweenProperty(_fullCover, "modulate:a", 0f, 0.42)
            .SetTrans(Tween.TransitionType.Cubic)
            .SetEase(Tween.EaseType.InOut);
        _revealTween.TweenProperty(_revealedCover, "modulate:a", 1f, 0.24)
            .SetDelay(0.18)
            .SetTrans(Tween.TransitionType.Cubic)
            .SetEase(Tween.EaseType.Out);
        _revealTween.Finished += () =>
        {
            _fullCover.Visible = false;
            _fullCover.Modulate = Colors.White;
            _revealedCover.Modulate = Colors.White;
            _revealTween = null;
        };
        return _revealTween;
    }

    private static Texture2D LoadRevealCover(int revealVariant)
    {
        var normalizedVariant = Mathf.PosMod(revealVariant - 1, 4) + 1;
        return GD.Load<Texture2D>(
            $"res://Assets/Event/ItemUI_ScratchCover_Reveal{normalizedVariant}.png");
    }

    private static void LoadTextureOrClear(TextureRect rect, string path)
    {
        rect.Texture = ResourceLoader.Exists(path) ? GD.Load<Texture2D>(path) : null;
    }
}
