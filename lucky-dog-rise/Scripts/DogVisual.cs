using Godot;
using System.Collections.Generic;

namespace LuckyDogRise;

public partial class DogVisual : Node2D
{
    [Signal]
    public delegate void DogClickedEventHandler();

    private Sprite2D _head = null!;
    private Sprite2D _eyes = null!;
    private Sprite2D _ears = null!;
    private Sprite2D _claw = null!;
    private Sprite2D _eyewear = null!;
    private Sprite2D _headwear = null!;
    private Button _hitButton = null!;

    private const string BasePath = "res://Assets/Shiba/Red/";

    // PSD center positions
    private static readonly Vector2 SunglassesPos = new(586, 415);
    private static readonly Vector2 LuckyCapPos = new(587, 222);

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
        _hitButton = GetNode<Button>("HitButton");
        _hitButton.Pressed += () => EmitSignal(SignalName.DogClicked);
    }

    public void ResetAppearance()
    {
        _head.Texture = GD.Load<Texture2D>(BasePath + "Head_Chubby.png");
        _eyes.Texture = GD.Load<Texture2D>(BasePath + "Eyes_Cute.png");
        _ears.Texture = GD.Load<Texture2D>(BasePath + "Ears_Happy.png");
        _claw.Texture = GD.Load<Texture2D>(BasePath + "Claw_Lucky.png");
        _claw.Visible = true;
        _eyewear.Visible = false;
        _headwear.Visible = false;
    }

    public void ShowSignal(DogSignal signal)
    {
        var textures = SignalTextures[signal];
        _eyes.Texture = GD.Load<Texture2D>(BasePath + textures[0]);
        _ears.Texture = GD.Load<Texture2D>(BasePath + textures[1]);
        _claw.Visible = true;

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
