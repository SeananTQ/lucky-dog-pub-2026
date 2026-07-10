using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using DataTables;

namespace LuckyDogRise;

public partial class ChipRewardController : Node2D
    , IInteractionHintTarget
{
    [Signal]
    public delegate void CollectedEventHandler();

    [Signal]
    public delegate void InteractionActivatedEventHandler();

    private const float SlideDuration = 0.4f;
    private const float SlideDistance = 300f;
    private const float ChipSpawnInterval = 0.025f;
    private const float ChipDropDuration = 0.2f;
    private const float ChipDropHeight = 42f;
    private const float LeftChipStartRotation = -0.22f;
    private const float RightChipStartRotation = 0.18f;
    private const float HintFirstLift = 8f;
    private const float HintSecondLift = 4f;
    private const float HintFirstRotation = -0.06f;
    private const float HintSecondRotation = 0.035f;
    private const double HintFirstDuration = 0.11;
    private const double HintSecondDuration = 0.09;
    private const string ChipPathPrefix = "res://Assets/v1/ChipStack/Chip_";

    private Button _clickButton = null!;
    private Label _amountLabel = null!;
    private Node2D _pileAnchors = null!;
    [Export] private Godot.Collections.Array<Marker2D> _pileMarkers = new();
    private Vector2 _chipLayerOffset = new(0f, -8f);
    private readonly List<Tween> _spawnTweens = new();
    private bool _collected;
    private readonly List<Tween> _hintTweens = new();
    private readonly Dictionary<Marker2D, Vector2> _markerRestPositions = new();

    public bool CanPlayInteractionHint => !_collected && IsInstanceValid(_pileAnchors);

    public override void _Ready()
    {
        _clickButton = GetNode<Button>("ClickButton");
        _amountLabel = GetNode<Label>("AmountLabel");
        _pileAnchors = GetNode<Node2D>("PileAnchors");
        foreach (var marker in _pileMarkers)
            _markerRestPositions[marker] = marker.Position;
        CacheSampleOffsetAndClearSamples();
        _clickButton.Pressed += OnClicked;
    }

    public void Setup(int amount, EHandRank rank)
    {
        _amountLabel.Text = $"+{amount}";
        BuildChipPile(rank);
    }

    private void CacheSampleOffsetAndClearSamples()
    {
        if (_pileMarkers.Count > 0)
        {
            var samples = _pileMarkers[0].GetChildren().OfType<Sprite2D>().OrderBy(sprite => sprite.Position.Y).ToArray();
            if (samples.Length >= 2)
                _chipLayerOffset = samples[0].Position - samples[1].Position;
        }

        foreach (var marker in _pileMarkers)
        {
            foreach (var sample in marker.GetChildren().OfType<Sprite2D>().ToArray())
                sample.Free();
        }
    }

    private void BuildChipPile(EHandRank rank)
    {
        var layout = GetLayout(rank);
        var pileZIndices = GetPileZIndices();
        var spawnDelay = 0f;
        for (var pileIndex = 0; pileIndex < layout.Length && pileIndex < _pileMarkers.Count; pileIndex++)
        {
            var pile = layout[pileIndex];
            for (var layer = 0; layer < pile.Count; layer++)
            {
                var restPosition = _chipLayerOffset * layer;
                var startRotation = (pileIndex + layer) % 2 == 0
                    ? LeftChipStartRotation
                    : RightChipStartRotation;
                var chip = new Sprite2D
                {
                    Texture = LoadChipTexture(pile.Color, layer),
                    Position = restPosition + new Vector2(0f, -ChipDropHeight),
                    ZIndex = pileZIndices.GetValueOrDefault(_pileMarkers[pileIndex], pileIndex * 100) + layer,
                    Rotation = startRotation,
                    Modulate = Colors.Transparent,
                    Visible = false,
                };
                _pileMarkers[pileIndex].AddChild(chip);
                QueueChipSpawn(chip, restPosition, spawnDelay);
                spawnDelay += ChipSpawnInterval;
            }
        }
    }

    private Dictionary<Marker2D, int> GetPileZIndices()
    {
        return _pileMarkers
            .Where(marker => marker != null)
            .OrderBy(marker => marker.GlobalPosition.Y)
            .Select((marker, index) => new { marker, zIndex = index * 100 })
            .ToDictionary(item => item.marker, item => item.zIndex);
    }

    private void QueueChipSpawn(Sprite2D chip, Vector2 restPosition, float delay)
    {
        var tween = CreateTween();
        _spawnTweens.Add(tween);
        tween.TweenInterval(delay);
        tween.TweenCallback(Callable.From(() =>
        {
            chip.Visible = true;
            chip.Modulate = Colors.White;
            var chipTween = CreateTween();
            _spawnTweens.Add(chipTween);
            chipTween.SetParallel(true);
            // 旧的缩放弹出保留在这里，方便之后比较或回退。
            // chipTween.TweenProperty(chip, "scale", Vector2.One, ChipPopDuration)
            //     .SetTrans(Tween.TransitionType.Back)
            //     .SetEase(Tween.EaseType.Out);
            // chipTween.TweenProperty(chip, "modulate:a", 1f, ChipPopDuration * 0.6f);
            chipTween.TweenProperty(chip, "position", restPosition, ChipDropDuration)
                .SetTrans(Tween.TransitionType.Cubic)
                .SetEase(Tween.EaseType.Out);
            chipTween.TweenProperty(chip, "rotation", 0f, ChipDropDuration)
                .SetTrans(Tween.TransitionType.Quad)
                .SetEase(Tween.EaseType.Out);
        }));
    }

    private static ChipPileSpec[] GetLayout(EHandRank rank) => rank switch
    {
        EHandRank.JacksOrBetter => [new("Green", 2)],
        EHandRank.TwoPair => [new("Blue", 3), new("Green", 1)],
        EHandRank.ThreeOfAKind => [new("Red", 4), new("Green", 2)],
        EHandRank.Straight => [new("Red", 4), new("Black", 3), new("Green", 1)],
        EHandRank.Flush => [new("Blue", 5), new("Green", 3), new("Purple", 1)],
        EHandRank.FullHouse => [new("Purple", 6), new("Black", 3), new("Green", 2)],
        EHandRank.FourOfAKind => [new("Green", 7), new("Purple", 4), new("Black", 2)],
        EHandRank.StraightFlush => [new("Green", 8), new("Purple", 5), new("Black", 3), new("Orange", 1)],
        EHandRank.RoyalFlush => [new("Orange", 9), new("Black", 6), new("Purple", 4), new("Green", 2)],
        _ => Array.Empty<ChipPileSpec>(),
    };

    private static Texture2D LoadChipTexture(string color, int layer)
    {
        var variant = layer % 2 == 0 ? "A" : "B";
        var path = $"{ChipPathPrefix}{color}_{variant}.png";
        return ResourceLoader.Exists(path) ? GD.Load<Texture2D>(path) : null;
    }

    private void OnClicked()
    {
        if (_collected) return;
        _collected = true;
        ResetHintAnimations();
        _clickButton.Disabled = true;
        EmitSignal(SignalName.InteractionActivated);
        KillSpawnTweens();

        AudioManager.Instance.PlaySfxByName("ChipCollect.wav");

        var tween = CreateTween();
        tween.TweenProperty(this, "position:y", Position.Y + SlideDistance, SlideDuration)
            .SetEase(Tween.EaseType.In)
            .SetTrans(Tween.TransitionType.Quad);
        tween.TweenProperty(this, "modulate:a", 0f, 0.15f);
        tween.TweenCallback(Callable.From(() =>
        {
            EmitSignal(SignalName.Collected);
            QueueFree();
        }));
    }

    private void KillSpawnTweens()
    {
        foreach (var tween in _spawnTweens)
            tween?.Kill();
        _spawnTweens.Clear();
    }

    public void PlayInteractionHint()
    {
        if (!CanPlayInteractionHint)
            return;

        GD.Print("[ChipReward] Play interaction hint");
        ResetHintAnimations();

        var activeMarkers = _pileMarkers.Where(marker => marker.GetChildCount() > 0).ToArray();
        for (var index = 0; index < activeMarkers.Length; index++)
        {
            var firstLift = HintFirstLift - (index % 3);
            var firstRotation = index % 2 == 0 ? HintFirstRotation : -HintFirstRotation * 0.85f;
            var delay = index * 0.04 + (index % 3 == 2 ? 0.012 : 0.0);
            _hintTweens.Add(CreatePileHintTween(activeMarkers[index], delay, firstLift, firstRotation));
        }
    }

    private Tween CreatePileHintTween(Marker2D marker, double delay, float firstLift, float firstRotation)
    {
        var restPosition = _markerRestPositions[marker];
        var tween = CreateTween();
        tween.TweenInterval(delay);
        tween.TweenProperty(marker, "position:y", restPosition.Y - firstLift, HintFirstDuration)
            .SetTrans(Tween.TransitionType.Quad)
            .SetEase(Tween.EaseType.Out);
        tween.Parallel().TweenProperty(marker, "rotation", firstRotation, HintFirstDuration)
            .SetTrans(Tween.TransitionType.Quad)
            .SetEase(Tween.EaseType.Out);
        tween.Chain().TweenProperty(marker, "position:y", restPosition.Y, HintFirstDuration)
            .SetTrans(Tween.TransitionType.Bounce)
            .SetEase(Tween.EaseType.Out);
        tween.Parallel().TweenProperty(marker, "rotation", 0f, HintFirstDuration)
            .SetTrans(Tween.TransitionType.Quad)
            .SetEase(Tween.EaseType.Out);
        tween.Chain().TweenProperty(marker, "position:y", restPosition.Y - HintSecondLift, HintSecondDuration)
            .SetTrans(Tween.TransitionType.Quad)
            .SetEase(Tween.EaseType.Out);
        tween.Parallel().TweenProperty(marker, "rotation", -firstRotation * 0.5f, HintSecondDuration)
            .SetTrans(Tween.TransitionType.Quad)
            .SetEase(Tween.EaseType.Out);
        tween.Chain().TweenProperty(marker, "position:y", restPosition.Y, HintSecondDuration)
            .SetTrans(Tween.TransitionType.Bounce)
            .SetEase(Tween.EaseType.Out);
        tween.Parallel().TweenProperty(marker, "rotation", 0f, HintSecondDuration)
            .SetTrans(Tween.TransitionType.Quad)
            .SetEase(Tween.EaseType.Out);
        return tween;
    }

    private void ResetHintAnimations()
    {
        foreach (var tween in _hintTweens)
            tween?.Kill();
        _hintTweens.Clear();
        _pileAnchors.Position = Vector2.Zero;
        _pileAnchors.Rotation = 0f;
        foreach (var marker in _pileMarkers)
        {
            marker.Position = _markerRestPositions[marker];
            marker.Rotation = 0f;
        }
    }

    private readonly record struct ChipPileSpec(string Color, int Count);
}
