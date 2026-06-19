using Godot;
using System.Collections.Generic;

namespace LuckyDogRise;

public partial class ItemAreaController : Node2D
{
    private const string ReferenceTreatFileName = "Whisky.png";

    private Sprite2D _treatSprite = null!;
    private readonly Dictionary<string, Vector2> _localCache = new();
    private Vector2 _referenceLocalPosition;
    private bool _cacheBuilt;

    public override void _Ready()
    {
        _treatSprite = GetNodeOrNull<Sprite2D>("Treat") ?? CreateTreatSprite();
        _referenceLocalPosition = _treatSprite.Position;
        BuildPositionCache();
    }

    private Sprite2D CreateTreatSprite()
    {
        var sprite = new Sprite2D();
        sprite.Name = "Treat";
        AddChild(sprite);
        return sprite;
    }

    public void SetTreat(Texture2D texture, string fileName)
    {
        _treatSprite.Texture = texture;
        _treatSprite.Visible = true;
        _treatSprite.Position = _localCache.GetValueOrDefault(fileName, _referenceLocalPosition);
    }

    public void ClearTreat()
    {
        _treatSprite.Visible = false;
    }

    /// <summary>
    /// 以 ItemArea.tscn 中预览 Whisky 的手调位置作为基础偏移。
    /// Sprite2D.Position 控制的是图片中心点，所以每种酒再根据
    /// PSD 中图片中心点相对 Whisky 图片中心点的差值进行偏移。
    /// </summary>
    private void BuildPositionCache()
    {
        if (_cacheBuilt) return;
        _cacheBuilt = true;

        using var file = FileAccess.Open("res://Assets/v1/layer_index.json", FileAccess.ModeFlags.Read);
        if (file == null) return;

        var json = new Json();
        if (json.Parse(file.GetAsText()) != Error.Ok) return;

        var layers = json.Data.AsGodotDictionary()["layers"].AsGodotArray();
        Vector2 referenceCenter = Vector2.Zero;
        bool foundReference = false;

        foreach (var layer in layers)
        {
            var d = layer.AsGodotDictionary();
            var fileName = d["file"].AsString().Split('/')[^1];
            if (fileName != ReferenceTreatFileName) continue;

            referenceCenter = ReadCenter(d);
            foundReference = true;
            break;
        }

        if (!foundReference) return;

        foreach (var layer in layers)
        {
            var d = layer.AsGodotDictionary();
            var fileOnly = d["file"].AsString().Split('/')[^1];

            var centerDelta = ReadCenter(d) - referenceCenter;

            _localCache[fileOnly] = _referenceLocalPosition + centerDelta;
        }
    }

    private static Vector2 ReadCenter(Godot.Collections.Dictionary d)
    {
        var x = (float)d["x"].AsDouble();
        var y = (float)d["y"].AsDouble();
        var w = ReadDim(d, "w", "width");
        var h = ReadDim(d, "h", "height");
        return new Vector2(x + w / 2f, y + h / 2f);
    }

    private static float ReadDim(Godot.Collections.Dictionary d, string shortKey, string longKey)
    {
        return d.ContainsKey(shortKey)
            ? (float)d[shortKey].AsDouble()
            : (float)d[longKey].AsDouble();
    }
}
