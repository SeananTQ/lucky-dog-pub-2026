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
    [Export] private TextureRect _shineTransitionCover = null!;
    [Export] private TextureRect _fullCover = null!;

    private static readonly Texture2D[] ShineTextures =
    {
        GD.Load<Texture2D>("res://Assets/Event/ItemUI_Shine_1.png"),
        GD.Load<Texture2D>("res://Assets/Event/ItemUI_Shine_2.png"),
        GD.Load<Texture2D>("res://Assets/Event/ItemUI_Shine_3.png"),
    };

    private const double ShineCrossFadeStartRatio = 0.4;
    private Tween _revealTween;
    private int _revealVariant;
    private int _shineVariant;
    private int _shineTextureIndex;
    private double _shineElapsedSeconds;
    private double _shineStepSeconds;
    private bool _isShining;

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
        SetVisualState(state);
        TooltipText = item.Name;
    }

    public override void _Process(double delta)
    {
        if (!_isShining || !IsVisibleInTree())
            return;

        _shineElapsedSeconds += delta;
        while (_shineElapsedSeconds >= _shineStepSeconds)
        {
            _shineElapsedSeconds -= _shineStepSeconds;
            _shineTextureIndex = (_shineTextureIndex + 1) % ShineTextures.Length;
            ApplyShineTextures();
        }

        var stepProgress = _shineElapsedSeconds / _shineStepSeconds;
        var fadeProgress = Mathf.Clamp(
            (stepProgress - ShineCrossFadeStartRatio) / (1.0 - ShineCrossFadeStartRatio),
            0.0,
            1.0);
        var easedFade = fadeProgress * fadeProgress * (3.0 - 2.0 * fadeProgress);
        _shineCover.Modulate = new Color(1f, 1f, 1f, (float)(1.0 - easedFade));
        _shineTransitionCover.Modulate = new Color(1f, 1f, 1f, (float)easedFade);
    }

    public void SetVisualState(CollectionEventRewardVisualState state)
    {
        _revealTween?.Kill();
        _revealTween = null;
        var rewardVisible = state != CollectionEventRewardVisualState.Covered;
        SetRewardVisualsVisible(rewardVisible);
        _revealedCover.Modulate = Colors.White;
        _revealedCover.Visible = state == CollectionEventRewardVisualState.Revealed;
        SetShining(state == CollectionEventRewardVisualState.Shining);
        _fullCover.Modulate = Colors.White;
        _fullCover.Visible = state == CollectionEventRewardVisualState.Covered;
    }

    public Tween PlayReveal()
    {
        _revealTween?.Kill();
        SetRewardVisualsVisible(true);
        _revealedCover.Visible = true;
        _revealedCover.Modulate = Colors.White;
        SetShining(false);
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

    private void SetShining(bool shining)
    {
        _isShining = shining;
        SetProcess(shining);
        _shineCover.Visible = shining;
        _shineTransitionCover.Visible = shining;
        if (!shining)
        {
            _shineCover.Modulate = Colors.White;
            _shineTransitionCover.Modulate = new Color(1f, 1f, 1f, 0f);
            return;
        }

        _shineTextureIndex = _shineVariant - 1;
        _shineStepSeconds = _shineVariant switch
        {
            1 => 1.45,
            2 => 1.7,
            _ => 1.25,
        };
        _shineElapsedSeconds = _shineStepSeconds * ((_shineVariant - 1) / 3.0);
        ApplyShineTextures();
        _Process(0.0);
    }

    private void ApplyShineTextures()
    {
        _shineCover.Texture = ShineTextures[_shineTextureIndex];
        _shineTransitionCover.Texture =
            ShineTextures[(_shineTextureIndex + 1) % ShineTextures.Length];
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
