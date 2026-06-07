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
    private Node2D _clawLeft = null!;
    private Node2D _clawRight = null!;
    private Sprite2D _eyewear = null!;
    private Sprite2D _headwear = null!;
    private Button _hitButton = null!;

    private const string BasePath = "res://Assets/Shiba/Red/";
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
        _clawLeft = GetNode<Node2D>("ClawLeft");
        _clawRight = GetNode<Node2D>("ClawRight");
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

        // 狗身体层级：背景(0) < 狗(1) < 桌子(2)
        _head.ZIndex = 1;
        _eyes.ZIndex = 1;
        _ears.ZIndex = 1;
        _eyewear.ZIndex = 1;
        _headwear.ZIndex = 1;

        ShowClawBack();
        _eyewear.Visible = false;
        _headwear.Visible = false;
    }

    public void ShowSignal(DogSignal signal)
    {
        var textures = SignalTextures[signal];
        _eyes.Texture = GD.Load<Texture2D>(BasePath + textures[0]);
        _ears.Texture = GD.Load<Texture2D>(BasePath + textures[1]);
        ShowClawPalm();

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

    public void SetEyewear(string fileName, Vector2 position)
    {
        _eyewear.Visible = true;
        _eyewear.Texture = GD.Load<Texture2D>($"res://Assets/Eyewear/{fileName}");
        _eyewear.Position = position;
    }

    public void SetHeadwear(string fileName, Vector2 position)
    {
        _headwear.Visible = true;
        _headwear.Texture = GD.Load<Texture2D>($"res://Assets/Headwear/{fileName}");
        _headwear.Position = position;
    }

    public void ResetAccessories()
    {
        _eyewear.Visible = false;
        _headwear.Visible = false;
    }

    public void SwapEyewearTexture(string fileName)
    {
        _eyewear.Visible = true;
        _eyewear.Texture = GD.Load<Texture2D>($"res://Assets/Eyewear/{fileName}");
    }

    public void SwapHeadwearTexture(string fileName)
    {
        _headwear.Visible = true;
        _headwear.Texture = GD.Load<Texture2D>($"res://Assets/Headwear/{fileName}");
    }

    // 手心（掌心朝上，被桌子挡住）
    // 层级：背景(0) < 狗头(1) < 爪子(1) < 桌子(2)
    public void ShowClawPalm()
    {
        _clawLeft.Visible = true;
        _clawRight.Visible = true;
        _clawLeft.ZAsRelative = false;
        _clawRight.ZAsRelative = false;
        _clawLeft.ZIndex = 1;
        _clawRight.ZIndex = 1;
        SetClawState(_clawLeft, "Palm");
        SetClawState(_clawRight, "Palm");
    }

    // 手背（手背朝上，挡住桌子）
    // 层级：背景(0) < 狗头(1) < 桌子(2) < 爪子(3)
    public void ShowClawBack()
    {
        _clawLeft.Visible = true;
        _clawRight.Visible = true;
        _clawLeft.ZAsRelative = false;
        _clawRight.ZAsRelative = false;
        _clawLeft.ZIndex = 3;
        _clawRight.ZIndex = 3;
        SetClawState(_clawLeft, "Back");
        SetClawState(_clawRight, "Back");
    }

    public void HideClaw()
    {
        _clawLeft.Visible = false;
        _clawRight.Visible = false;
    }

    // 摇手拒绝动画（双手旋转摆动）
    public void ShakePaw()
    {
        float angle = 0.25f; // ~15度
        var tween = CreateTween().SetParallel(true);
        tween.TweenProperty(_clawLeft, "rotation", angle, 0.06);
        tween.TweenProperty(_clawRight, "rotation", -angle, 0.06);
        tween.Chain().TweenProperty(_clawLeft, "rotation", -angle, 0.06);
        tween.TweenProperty(_clawRight, "rotation", angle, 0.06);
        tween.Chain().TweenProperty(_clawLeft, "rotation", angle * 0.6f, 0.05);
        tween.TweenProperty(_clawRight, "rotation", -angle * 0.6f, 0.05);
        tween.Chain().TweenProperty(_clawLeft, "rotation", 0f, 0.04);
        tween.TweenProperty(_clawRight, "rotation", 0f, 0.04);
    }

    private void SetClawState(Node2D claw, string state)
    {
        var back = claw.GetNode<Sprite2D>("Claw_Back_Left");
        var palm = claw.GetNode<Sprite2D>("Claw_Palm_Left");
        back.Visible = state == "Back";
        palm.Visible = state == "Palm";
    }
}
