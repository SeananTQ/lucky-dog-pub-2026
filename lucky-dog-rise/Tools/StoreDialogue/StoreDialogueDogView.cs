using System;
using Godot;
using DataTables;

namespace LuckyDogRise.Tools;

/// <summary>Pure tool presentation: no GameData, platform identity or equipment writes.</summary>
public partial class StoreDialogueDogView : Node2D
{
    [Export] private DogVisual _dog = null!;
    [Export] private PanelContainer _bar = null!;
    [Export] private Label _name = null!;
    private StoreDialogueDog _data = null!;
    private bool? _speaking;
    public string DogId => _data.Id;
    public Rect2 PickRect { get; private set; }
    public bool IsSpeaking => _speaking == true;
    public DogVisual Visual => _dog;
    public bool NameFits { get; private set; }
    public int NameFontSize => _name.GetThemeFontSize("font_size");
    public Vector2 NameBarSize => _bar.Size;
    public float NameBarBottom => _bar.Position.Y + Math.Max(_bar.Size.Y, _bar.GetCombinedMinimumSize().Y);

    public void Configure(StoreDialogueDog data, string displayName, float scale, int nameFontSize)
    {
        _data = data;
        _dog.Scale = Vector2.One * scale;
        // These characters float above their own name bars, with the full chin visible.
        _dog.SetHeadClippingEnabled(false);
        _dog.SetHitButtonEnabled(false);
        _dog.SetPreviewHeadwear(LubanData.Tables.TbItem.GetOrDefault(data.HeadwearItemId));
        _dog.SetProcess(false);
        _name.Text = displayName;
        _name.AutoTranslateMode = AutoTranslateModeEnum.Disabled;
        var barWidth = Math.Max(80, 800 * scale);
        var textWidth = barWidth - _bar.GetThemeStylebox("panel").GetMinimumSize().X - 4;
        var font = _name.GetThemeFont("font");
        var size = nameFontSize;
        while (size > 8 && font.GetStringSize(displayName, fontSize: size).X > textWidth) size--;
        NameFits = font.GetStringSize(displayName, fontSize: size).X <= textWidth;
        _name.AddThemeFontSizeOverride("font_size", size);
        // ClipText keeps the label's unshrunk minimum width from expanding the panel.
        // Size only after choosing the final font, then center the actual panel.
        // CJK fallback fonts can be taller than the nominal font size.
        _bar.Size = new Vector2(barWidth, Math.Max(nameFontSize + 12, _bar.GetCombinedMinimumSize().Y));
        _bar.Position = new Vector2(-_bar.Size.X / 2, 75 * scale + 7);
        PickRect = new Rect2(-450 * scale, -750 * scale, 900 * scale, 850 * scale + nameFontSize + 20);
        _speaking = null;
        SetSpeaking(false);
    }

    public void ApplySharedNameBarLayout(int fontSize, Vector2 barSize)
    {
        _name.AddThemeFontSizeOverride("font_size", fontSize);
        var textWidth = barSize.X - _bar.GetThemeStylebox("panel").GetMinimumSize().X - 4;
        NameFits = _name.GetThemeFont("font").GetStringSize(_name.Text, fontSize: fontSize).X <= textWidth;
        _bar.Size = barSize;
        _bar.Position = new Vector2(-_bar.Size.X / 2, _bar.Position.Y);
    }

    public void SetSpeaking(bool speaking)
    {
        if (_speaking == speaking) return;
        _speaking = speaking;
        var pose = speaking ? _data.Speaking : _data.Idle;
        var spec = _data.Appearance.ToAppearanceSpec();
        if (!pose.ReactionId.HasValue)
        {
            if (!string.IsNullOrWhiteSpace(pose.Eyes)) spec.DefaultEyes = pose.Eyes;
            if (!string.IsNullOrWhiteSpace(pose.Ears)) spec.DefaultEars = pose.Ears;
        }
        _dog.SetPreviewAppearance(spec);
        _dog.ApplyPreviewExpression((EDogReactionTrigger)(pose.ReactionId ?? (int)EDogReactionTrigger.Default));
        // Some shared sprites have absolute Z. Normalize only this tool's instances.
        foreach (var part in _dog.FindChildren("*", "CanvasItem", true, false))
            if (part is CanvasItem item) item.ZAsRelative = true;
    }
}
