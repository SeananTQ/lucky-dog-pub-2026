using Godot;
using System;
using System.Collections.Generic;

namespace LuckyDogRise;

public partial class DogVisual : Node2D
{
    private Sprite2D _head = null!;
    private Sprite2D _eyes = null!;
    private Sprite2D _ears = null!;
    private Sprite2D _claw = null!;
    private Sprite2D _eyewear = null!;
    private Sprite2D _headwear = null!;

    // Default Shiba color: Red
    private const string BasePath = "res://Assets/Shiba/Red/";

    private static readonly Dictionary<DogSignal, string[]> SignalTextures = new()
    {
        { DogSignal.Bored, new[] { "Eyes_Bored.png", "Ears_Plane.png" } },
        { DogSignal.Happy, new[] { "Eyes_Happy.png", "Ears_Happy.png" } },
        { DogSignal.LuckyEye, new[] { "Eyes_Lucky.png", "Ears_Happy.png" } },
        { DogSignal.TopTier, new[] { "Eyes_Lucky.png", "Ears_Happy.png" } },
    };

    public override void _Ready()
    {
        _head = GetNode<Sprite2D>("Head");
        _eyes = GetNode<Sprite2D>("Eyes");
        _ears = GetNode<Sprite2D>("Ears");
        _claw = GetNode<Sprite2D>("Claw");
        _eyewear = GetNode<Sprite2D>("Eyewear");
        _headwear = GetNode<Sprite2D>("Headwear");

        ResetAppearance();
    }

    // PSD center positions (x + w/2, y + h/2)
    private static readonly Vector2 HeadPos = new(587, 516);
    private static readonly Vector2 EyesNeutralPos = new(583, 385);
    private static readonly Vector2 EarsPos = new(588, 357);
    private static readonly Vector2 ClawPos = new(577, 622);
    private static readonly Vector2 SunglassesPos = new(586, 415);
    private static readonly Vector2 LuckyCapPos = new(587, 222);

    public void ResetAppearance()
    {
        _head.Texture = GD.Load<Texture2D>(BasePath + "Head_Chubby.png");
        _head.Position = HeadPos;
        _eyes.Texture = GD.Load<Texture2D>(BasePath + "Eyes_Neutral.png");
        _eyes.Position = EyesNeutralPos;
        _ears.Texture = GD.Load<Texture2D>(BasePath + "Ears_Plane.png");
        _ears.Position = EarsPos;
        _claw.Texture = GD.Load<Texture2D>(BasePath + "Claw_Lucky.png");
        _claw.Position = ClawPos;
        _claw.Visible = false;
        _eyewear.Visible = false;
        _headwear.Visible = false;
    }

    public void ShowSignal(DogSignal signal)
    {
        var textures = SignalTextures[signal];
        _eyes.Texture = GD.Load<Texture2D>(BasePath + textures[0]);
        _ears.Texture = GD.Load<Texture2D>(BasePath + textures[1]);
        _claw.Visible = true;

        // TopTier: also show headwear (Lucky cap)
        if (signal == DogSignal.TopTier)
        {
            _headwear.Visible = true;
            _headwear.Texture = GD.Load<Texture2D>("res://Assets/Headwear/Lucky.png");
            _headwear.Position = LuckyCapPos;
        }
    }

    public void ShowSunglasses()
    {
        _eyewear.Visible = true;
        _eyewear.Texture = GD.Load<Texture2D>("res://Assets/Eyewear/Sunglasses_Blade.png");
        _eyewear.Position = SunglassesPos;
    }
}
