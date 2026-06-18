using Godot;
using System.Collections.Generic;

namespace LuckyDogRise;

public partial class ItemAreaController : Node2D
{
    private Sprite2D _treatSprite = null!;
    private readonly Dictionary<string, Vector2> _localCache = new();
    private bool _cacheBuilt;

    public override void _Ready()
    {
        _treatSprite = GetNodeOrNull<Sprite2D>("Treat") ?? CreateTreatSprite();
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
        _treatSprite.Position = _localCache.GetValueOrDefault(fileName, Vector2.Zero);
    }

    public void ClearTreat()
    {
        _treatSprite.Visible = false;
    }

    /// <summary>
    /// 以预览酒（.tscn 中手动对准 0,0 的那张图）的 PSD 底部中心为参考点，
    /// 计算所有 treat 相对于参考点的本地偏移。
    /// ItemArea.Position 不参与公式 —— 你在编辑器里挪它来控制所有 treat 的整体位置。
    /// </summary>
    private void BuildPositionCache()
    {
        if (_cacheBuilt) return;
        _cacheBuilt = true;

        using var file = FileAccess.Open("res://Assets/v1/layer_index.json", FileAccess.ModeFlags.Read)
            ?? FileAccess.Open("res://Assets/layer_index.json", FileAccess.ModeFlags.Read);
        if (file == null) return;

        var json = new Json();
        if (json.Parse(file.GetAsText()) != Error.Ok) return;

        // 第一遍：找到预览酒的 PSD 底部中心，作为参考锚点
        var layers = json.Data.AsGodotDictionary()["layers"].AsGodotArray();
        Vector2 refBottom = Vector2.Zero;
        bool foundRef = false;

        foreach (var layer in layers)
        {
            var d = layer.AsGodotDictionary();
            var fileName = d["file"].AsString().Split('/')[^1];
            if (fileName == "Whisky.png")
            {
                var x = (float)d["x"].AsDouble();
                var y = (float)d["y"].AsDouble();
                var w = ReadDim(d, "w", "width");
                refBottom = new Vector2(x + w / 2f, y + ReadDim(d, "h", "height"));
                foundRef = true;
                break;
            }
        }

        if (!foundRef) return;

        // 第二遍：以参考点为基准，算每个 treat 的本地偏移
        foreach (var layer in layers)
        {
            var d = layer.AsGodotDictionary();
            var fileOnly = d["file"].AsString().Split('/')[^1];

            var x = (float)d["x"].AsDouble();
            var y = (float)d["y"].AsDouble();
            var w = ReadDim(d, "w", "width");
            var h = ReadDim(d, "h", "height");
            var bottom = new Vector2(x + w / 2f, y + h);

            _localCache[fileOnly] = bottom - refBottom;
        }
    }

    private static float ReadDim(Godot.Collections.Dictionary d, string shortKey, string longKey)
    {
        return d.ContainsKey(shortKey)
            ? (float)d[shortKey].AsDouble()
            : (float)d[longKey].AsDouble();
    }
}
