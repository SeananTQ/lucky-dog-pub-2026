using Godot;
using System.Collections.Generic;

namespace LuckyDogRise;

public partial class CollectionCelebrationConfettiController : Control
{
    [Export(PropertyHint.Range, "2,80,2")]
    private int _pieceCount = 36;

    [Export(PropertyHint.Range, "0,1,0.01")]
    private float _sourceHeightRatio = 0.45f;

    [Export(PropertyHint.Range, "0,1,0.01")]
    private float _burstDurationSeconds = 0.12f;

    [Export(PropertyHint.Range, "0,2000,10")]
    private float _gravity = 650f;

    [Export(PropertyHint.Range, "0,10,0.1")]
    private float _airDragPerSecond = 2.8f;

    [Export(PropertyHint.Range, "0,10,0.1")]
    private float _replayCooldownSeconds = 2.5f;

    private static readonly Texture2D[] ConfettiTextures = LoadConfettiTextures();
    private readonly List<ConfettiPiece> _pieces = new();
    private readonly RandomNumberGenerator _random = new();
    private bool _playing;
    private ulong _nextAllowedPlayTicks;

    public override void _Ready()
    {
        SetProcess(false);
    }

    public override void _Process(double delta)
    {
        if (!_playing)
            return;

        var deltaSeconds = (float)delta;
        var anyAlive = false;
        foreach (var piece in _pieces)
        {
            piece.Age += deltaSeconds;
            if (piece.Age < piece.SpawnDelay)
                continue;

            var lifeAge = piece.Age - piece.SpawnDelay;
            if (lifeAge >= piece.Lifetime)
            {
                piece.Sprite.Visible = false;
                continue;
            }

            anyAlive = true;
            piece.Sprite.Visible = true;
            var dragFactor = Mathf.Exp(-_airDragPerSecond * deltaSeconds);
            piece.Velocity *= dragFactor;
            piece.Velocity.Y += _gravity * deltaSeconds;
            var flutterVelocity = Mathf.Sin(
                lifeAge * piece.FlutterFrequency + piece.FlutterPhase) * piece.FlutterSpeed;
            piece.Sprite.Position += (piece.Velocity + Vector2.Right * flutterVelocity) * deltaSeconds;
            piece.Sprite.Rotation += piece.AngularVelocity * deltaSeconds;

            const float emergenceDuration = 0.08f;
            var emergenceProgress = Mathf.Clamp(lifeAge / emergenceDuration, 0f, 1f);
            var emergenceScale = 1f - Mathf.Pow(1f - emergenceProgress, 3f);
            piece.Sprite.Scale = Vector2.One
                                 * piece.TargetScale
                                 * Mathf.Lerp(0.45f, 1f, emergenceScale);

            const float fadeDuration = 0.38f;
            var remaining = piece.Lifetime - lifeAge;
            var alpha = Mathf.Clamp(remaining / fadeDuration, 0f, 1f);
            piece.Sprite.Modulate = new Color(1f, 1f, 1f, alpha);
        }

        if (!anyAlive && _pieces.TrueForAll(piece => piece.Age >= piece.SpawnDelay))
            StopPlayback();
    }

    public void Play()
    {
        TryPlay();
    }

    public bool TryPlay()
    {
        var nowTicks = Time.GetTicksMsec();
        if (_playing || nowTicks < _nextAllowedPlayTicks)
            return false;
        if (ConfettiTextures.Length == 0 || Size.X <= 0f || Size.Y <= 0f)
            return false;

        ClearPieces();
        _nextAllowedPlayTicks = nowTicks
                                + (ulong)Mathf.CeilToInt(_replayCooldownSeconds * 1000f);
        _random.Randomize();
        var sourceY = Size.Y * _sourceHeightRatio;
        for (var i = 0; i < _pieceCount; i++)
        {
            var fromLeft = i % 2 == 0;
            var sprite = new Sprite2D
            {
                Texture = ConfettiTextures[_random.RandiRange(0, ConfettiTextures.Length - 1)],
                Position = new Vector2(fromLeft ? -8f : Size.X + 8f, sourceY + _random.RandfRange(-16f, 16f)),
                Rotation = _random.RandfRange(0f, Mathf.Tau),
                Scale = Vector2.One * 0.45f,
                Visible = false,
            };
            AddChild(sprite);

            var distanceBand = (i / 2) % 3;
            var horizontalSpeed = distanceBand switch
            {
                0 => _random.RandfRange(240f, 380f),
                1 => _random.RandfRange(430f, 640f),
                _ => _random.RandfRange(700f, 930f),
            };
            _pieces.Add(new ConfettiPiece
            {
                Sprite = sprite,
                Velocity = new Vector2(fromLeft ? horizontalSpeed : -horizontalSpeed, _random.RandfRange(-860f, -580f)),
                AngularVelocity = _random.RandfRange(2.1f, 7.3f) * (_random.Randf() < 0.5f ? -1f : 1f),
                SpawnDelay = _random.RandfRange(0f, _burstDurationSeconds),
                Lifetime = _random.RandfRange(1.65f, 2.2f),
                TargetScale = _random.RandfRange(0.82f, 1.18f),
                FlutterSpeed = _random.RandfRange(22f, 58f),
                FlutterFrequency = _random.RandfRange(7f, 12f),
                FlutterPhase = _random.RandfRange(0f, Mathf.Tau),
            });
        }

        _playing = true;
        SetProcess(true);
        AudioManager.Instance?.PlaySfx("Collection_ConfettiBurst");
        return true;
    }

    public void Stop()
    {
        ClearPieces();
    }

    private void StopPlayback()
    {
        _playing = false;
        SetProcess(false);
    }

    private void ClearPieces()
    {
        StopPlayback();
        foreach (var piece in _pieces)
        {
            piece.Sprite.Visible = false;
            piece.Sprite.QueueFree();
        }
        _pieces.Clear();
    }

    private static Texture2D[] LoadConfettiTextures()
    {
        var textures = new List<Texture2D>();
        for (var i = 1; i <= 12; i++)
        {
            var path = $"res://Assets/Event/Collection/Confetti/Confetti_{i:00}.png";
            if (ResourceLoader.Exists(path))
                textures.Add(GD.Load<Texture2D>(path));
        }
        return textures.ToArray();
    }

    private sealed class ConfettiPiece
    {
        public Sprite2D Sprite = null!;
        public Vector2 Velocity;
        public float AngularVelocity;
        public float SpawnDelay;
        public float Lifetime;
        public float Age;
        public float TargetScale;
        public float FlutterSpeed;
        public float FlutterFrequency;
        public float FlutterPhase;
    }
}
