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

    private const string V1ResourcePrefix = "res://Assets/v1/";

    private Button _hitButton = null!;
    private Sprite2D _arm = null!;
    private Sprite2D _clothes = null!;
    private Sprite2D _accessory = null!;
    private bool _isKnocking;
    private Tween _hintTween;
    private Dictionary<string, Vector2> _psdCenterCache = null!;
    private Vector2 _armReferenceLocalPosition;
    private Vector2 _clothesReferenceLocalPosition;
    private Vector2 _accessoryReferenceLocalPosition;
    private string _armReferenceLayerPath = "";
    private string _clothesReferenceLayerPath = "";
    private string _accessoryReferenceLayerPath = "";
    private Vector2 _restPosition;

    public bool CanPlayInteractionHint => Enabled && !_isKnocking;
    public bool IsInteractionHintPlaying => _hintTween?.IsRunning() ?? false;

    public override void _Ready()
    {
        _hitButton = GetNode<Button>("HitButton");
        _arm = GetNode<Sprite2D>("Arm");
        _clothes = GetNode<Sprite2D>("Clothes");
        _accessory = GetNode<Sprite2D>("Accessory");
        _armReferenceLocalPosition = _arm.Position;
        _clothesReferenceLocalPosition = _clothes.Position;
        _accessoryReferenceLocalPosition = _accessory.Position;
        _armReferenceLayerPath = GetLayerPath(_arm.Texture);
        _clothesReferenceLayerPath = GetLayerPath(_clothes.Texture);
        _accessoryReferenceLayerPath = GetLayerPath(_accessory.Texture);
        _restPosition = Position;
        _hitButton.Pressed += OnHitPressed;
        EnsurePsdCenterCache();
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
        if (TryGetScenePosition(texture, fileName, _armReferenceLayerPath, _armReferenceLocalPosition, out var pos))
            _arm.Position = pos;
    }

    public void SetClothes(Texture2D texture, string fileName)
    {
        if (texture != null)
        {
            _clothes.Texture = texture;
            _clothes.Visible = true;
            if (TryGetScenePosition(texture, fileName, _clothesReferenceLayerPath, _clothesReferenceLocalPosition, out var pos))
                _clothes.Position = pos;
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
            if (TryGetScenePosition(texture, fileName, _accessoryReferenceLayerPath, _accessoryReferenceLocalPosition, out var pos))
                _accessory.Position = pos;
        }
        else
        {
            _accessory.Visible = false;
        }
    }

    private bool TryGetScenePosition(
        Texture2D texture,
        string fileName,
        string referenceLayerPath,
        Vector2 referenceLocalPosition,
        out Vector2 position)
    {
        position = referenceLocalPosition;
        var currentLayerPath = GetLayerPath(texture);
        if (!_psdCenterCache.TryGetValue(currentLayerPath, out var currentCenter))
        {
            GD.PushWarning($"[HandArea] PSD center not found for: {fileName} ({currentLayerPath})");
            return false;
        }

        if (!_psdCenterCache.TryGetValue(referenceLayerPath, out var referenceCenter))
        {
            GD.PushWarning($"[HandArea] Reference PSD center not found for: {referenceLayerPath}");
            return false;
        }

        position = referenceLocalPosition + currentCenter - referenceCenter;
        return true;
    }

    private void EnsurePsdCenterCache()
    {
        if (_psdCenterCache != null) return;
        _psdCenterCache = new Dictionary<string, Vector2>();

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
            var layerPath = d["file"].AsString().Replace('\\', '/');
            var w = d.ContainsKey("w") ? (float)d["w"].AsDouble() : (float)d["width"].AsDouble();
            var h = d.ContainsKey("h") ? (float)d["h"].AsDouble() : (float)d["height"].AsDouble();
            var cx = (float)d["x"].AsDouble() + w / 2f;
            var cy = (float)d["y"].AsDouble() + h / 2f;
            _psdCenterCache[layerPath] = new Vector2(cx, cy);
        }
    }

    private static string GetLayerPath(Texture2D texture)
    {
        if (texture == null)
            return "";

        var resourcePath = texture.ResourcePath.Replace('\\', '/');
        return resourcePath.StartsWith(V1ResourcePrefix)
            ? resourcePath[V1ResourcePrefix.Length..]
            : resourcePath;
    }
}
