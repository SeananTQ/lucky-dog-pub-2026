using Godot;
using DataTables;
using System;

namespace LuckyDogRise;

/// <summary>Cycles a rank row's examples without suggesting a discard/draw interaction.</summary>
public partial class PokerHandExampleController : HBoxContainer
{
    [Export] public EHandRank HandRank { get; private set; }
    [Export] private Godot.Collections.Array<TextureRect> _cards = new();

    private const double HoldSeconds = 10;
    private const double FadeSeconds = 0.6;
    private readonly Random _random = new();
    private Godot.Collections.Array<Texture2D> _faces;
    private Texture2D _anyCard;
    private int[] _example;
    private double _elapsed;
    private bool _swapped;

    public override void _Ready() => SetProcess(false);

    public void Initialize(Godot.Collections.Array<Texture2D> faces, Texture2D anyCard)
    {
        if (_cards.Count != 5 || faces.Count != 52 || anyCard == null)
            throw new InvalidOperationException($"Incomplete help card bindings for {HandRank}.");
        _faces = faces;
        _anyCard = anyCard;
    }

    public void SetRunning(bool running)
    {
        SetProcess(running);
        _elapsed = 0;
        _swapped = false;
        SetCardAlpha(1);
        if (running && _example == null)
            ReplaceExample();
    }

    public override void _Process(double delta)
    {
        if (!IsVisibleInTree())
            return;
        _elapsed += delta;
        if (_elapsed < HoldSeconds)
            return;
        double fadeTime = _elapsed - HoldSeconds;
        if (fadeTime < FadeSeconds)
        {
            SetCardAlpha(1 - Mathf.SmoothStep(0, 1, (float)(fadeTime / FadeSeconds)));
            return;
        }
        if (!_swapped)
        {
            ReplaceExample();
            _swapped = true;
        }
        SetCardAlpha(Mathf.SmoothStep(0, 1, (float)((fadeTime - FadeSeconds) / FadeSeconds)));
        if (fadeTime >= FadeSeconds * 2)
        {
            SetCardAlpha(1);
            _elapsed = 0;
            _swapped = false;
        }
    }

    private void ReplaceExample()
    {
        _example = PokerHandExamples.Next(HandRank, _random, _example);
        int visibleCount = PokerHandExamples.VisibleCardCount(HandRank);
        for (int i = 0; i < 5; i++)
            _cards[i].Texture = i < visibleCount ? _faces[_example[i]] : _anyCard;
    }

    private void SetCardAlpha(float alpha)
    {
        for (int i = 0; i < _cards.Count; i++)
        {
            var color = _cards[i].SelfModulate;
            color.A = alpha;
            _cards[i].SelfModulate = color;
        }
    }
}
