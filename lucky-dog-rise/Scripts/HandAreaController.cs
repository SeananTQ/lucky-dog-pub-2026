using Godot;
using System.Collections.Generic;

namespace LuckyDogRise;

public partial class HandAreaController : Node2D, IInteractionHintTarget
{
    [Signal]
    public delegate void HandKnockedEventHandler();

    [Signal]
    public delegate void FirstKnockLandedEventHandler();

    private const float KnockAngle = 0.08f;
    private const float KnockDownTime = 0.1f;
    private const float KnockUpTime = 0.12f;
    private const float KnockIntervalTime = 0.05f;

    public bool Enabled { get; set; }

    // PSD → HandArea 本地坐标转换参数（不同部位独立偏移）
    private const float OffsetX = -50f;
    private const float OffsetY = 1127f;
    private const float OffsetXAcc = -50f;
    private const float OffsetYAcc = 1127f;

    private Button _hitButton = null!;
    private Sprite2D _arm = null!;
    private Sprite2D _clothes = null!;
    private Sprite2D _accessory = null!;
    private bool _isKnocking;
    private Tween _hintTween;
    private Dictionary<string, Vector2> _positionCache = null!;
    private Vector2 _restPosition;

    public bool CanPlayInteractionHint => Enabled && !_isKnocking;
    public bool IsInteractionHintPlaying => _hintTween?.IsRunning() ?? false;

    public override void _Ready()
    {
        _hitButton = GetNode<Button>("HitButton");
        _arm = GetNode<Sprite2D>("Arm");
        _clothes = GetNode<Sprite2D>("Clothes");
        _accessory = GetNode<Sprite2D>("Accessory");
        _restPosition = Position;
        _hitButton.Pressed += OnHitPressed;
        EnsurePositionCache();
    }

    private void OnHitPressed()
    {
        if (!Enabled || _isKnocking) return;
        EmitSignal(SignalName.HandKnocked);
        PlayKnockAnimation();
    }

    private void PlayKnockAnimation()
    {
        ResetHintAnimation();
        _isKnocking = true;
        AudioManager.Instance.PlaySfx("Player_HandKnockOnTable");

        var tween = CreateTween();
        tween.TweenProperty(this, "rotation", KnockAngle, KnockDownTime)
            .SetEase(Tween.EaseType.In)
            .SetTrans(Tween.TransitionType.Quad);
        tween.TweenCallback(Callable.From(() => EmitSignal(SignalName.FirstKnockLanded)));
        tween.TweenProperty(this, "rotation", 0f, KnockUpTime)
            .SetEase(Tween.EaseType.Out)
            .SetTrans(Tween.TransitionType.Bounce);
        tween.TweenInterval(KnockIntervalTime);
        tween.TweenProperty(this, "rotation", KnockAngle, KnockDownTime)
            .SetEase(Tween.EaseType.In)
            .SetTrans(Tween.TransitionType.Quad);
        tween.TweenProperty(this, "rotation", 0f, KnockUpTime)
            .SetEase(Tween.EaseType.Out)
            .SetTrans(Tween.TransitionType.Bounce);
        tween.TweenCallback(Callable.From(() => _isKnocking = false));
    }

    /// <summary>
    /// 提示确认：手臂贴桌面左右擦两次，不播放敲击声，也不触发补牌。
    /// </summary>
    public void PlayInteractionHint()
    {
        if (!CanPlayInteractionHint)
            return;

        ResetHintAnimation();
        AudioManager.Instance.PlaySfx("Player_HandRubOnTable");
        _hintTween = CreateTween();
        _hintTween.TweenProperty(this, "position:x", _restPosition.X - 10f, 0.11f)
            .SetEase(Tween.EaseType.InOut)
            .SetTrans(Tween.TransitionType.Quad);
        _hintTween.TweenProperty(this, "position:x", _restPosition.X, 0.12f)
            .SetEase(Tween.EaseType.InOut)
            .SetTrans(Tween.TransitionType.Quad);
        _hintTween.TweenProperty(this, "position:x", _restPosition.X - 8f, 0.10f)
            .SetEase(Tween.EaseType.InOut)
            .SetTrans(Tween.TransitionType.Quad);
        _hintTween.TweenProperty(this, "position:x", _restPosition.X, 0.11f)
            .SetEase(Tween.EaseType.InOut)
            .SetTrans(Tween.TransitionType.Quad);
    }

    private void ResetHintAnimation()
    {
        _hintTween?.Kill();
        Position = _restPosition;
        Rotation = 0f;
    }

    public void SetArm(Texture2D texture, string fileName)
    {
        _arm.Texture = texture;
        var pos = GetScenePosition(fileName);
        if (pos != Vector2.Zero) _arm.Position = pos;
    }

    public void SetClothes(Texture2D texture, string fileName)
    {
        if (texture != null)
        {
            _clothes.Texture = texture;
            _clothes.Visible = true;
            var pos = GetScenePosition(fileName);
            if (pos != Vector2.Zero) _clothes.Position = pos;
        }
        else
        {
            _clothes.Visible = false;
        }
    }

    public void SetAccessory(Texture2D texture, string fileName)
    {
        if (texture != null)
        {
            _accessory.Texture = texture;
            _accessory.Visible = true;
            var pos = GetScenePosition(fileName);
            if (pos != Vector2.Zero) _accessory.Position = pos;
        }
        else
        {
            _accessory.Visible = false;
        }
    }

    private Vector2 GetScenePosition(string fileName)
    {
        if (_positionCache.TryGetValue(fileName, out var pos))
            return pos;

        GD.PushWarning($"[HandArea] Position not found for: {fileName}");
        return Vector2.Zero;
    }

    private void EnsurePositionCache()
    {
        if (_positionCache != null) return;
        _positionCache = new Dictionary<string, Vector2>();

        using var file = FileAccess.Open("res://Assets/v1/layer_index.json", FileAccess.ModeFlags.Read);
        if (file == null) return;
        ParseLayerJson(file.GetAsText());
    }

    private void ParseLayerJson(string jsonText)
    {
        var json = new Json();
        if (json.Parse(jsonText) != Error.Ok) return;

        var layers = json.Data.AsGodotDictionary()["layers"].AsGodotArray();
        foreach (var layer in layers)
        {
            var d = layer.AsGodotDictionary();
            var name = d["file"].AsString();
            var fileOnly = name.Split('/')[^1];
            var w = d.ContainsKey("w") ? (float)d["w"].AsDouble() : (float)d["width"].AsDouble();
            var h = d.ContainsKey("h") ? (float)d["h"].AsDouble() : (float)d["height"].AsDouble();
            var cx = (float)d["x"].AsDouble() + w / 2f;
            var cy = (float)d["y"].AsDouble() + h / 2f;

            var pos = name.Contains("Accessory")
                ? new Vector2(cx - OffsetXAcc, cy - OffsetYAcc)
                : new Vector2(cx - OffsetX, cy - OffsetY);

            _positionCache[fileOnly] = pos;
        }
    }
}
