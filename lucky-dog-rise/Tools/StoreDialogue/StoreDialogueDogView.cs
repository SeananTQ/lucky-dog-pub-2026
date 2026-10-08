using System;
using Godot;
using DataTables;

namespace LuckyDogRise.Tools;

/// <summary>Pure tool presentation: no GameData, platform identity or equipment writes.</summary>
public partial class StoreDialogueDogView : Node2D
{
    public const int MaximumNameLines = 2;
    [Export] private DogVisual _dog = null!;
    [Export] private PanelContainer _bar = null!;
    [Export] private Label _name = null!;
    private StoreDialogueDog _data = null!;
    private bool? _speaking;
    private float _minimumBarHeight;
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
        var textWidth = barWidth - _bar.GetThemeStylebox("panel").GetMinimumSize().X;
        var font = _name.GetThemeFont("font");
        _minimumBarHeight = nameFontSize + 12;
        _name.SizeFlagsVertical = Control.SizeFlags.Fill;
        _name.CustomMinimumSize = Vector2.Zero;
        _name.AutowrapMode = TextServer.AutowrapMode.Off;
        var size = nameFontSize;
        while (size > 8 && font.GetStringSize(displayName, fontSize: size).X > textWidth) size--;
        if (font.GetStringSize(displayName, fontSize: size).X > textWidth)
        {
            _name.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            size = nameFontSize;
            while (size > 8 && MeasureName(font, size, textWidth).Lines > MaximumNameLines) size--;
        }
        _name.AddThemeFontSizeOverride("font_size", size);
        var measured = MeasureName(font, size, textWidth);
        NameFits = measured.Lines <= MaximumNameLines && measured.Width <= textWidth + 0.1f;
        _name.CustomMinimumSize = new Vector2(0, measured.Height);
        var barHeight = Math.Max(nameFontSize + 12,
            measured.Height + _bar.GetThemeStylebox("panel").GetMinimumSize().Y);
        _bar.Size = new Vector2(barWidth, barHeight);
        _bar.Position = new Vector2(-_bar.Size.X / 2, 75 * scale + 7);
        PickRect = new Rect2(-450 * scale, -750 * scale, 900 * scale, 850 * scale + nameFontSize + 20);
        _speaking = null;
        SetSpeaking(false);
    }

    public void ApplySharedNameBarLayout(int fontSize, Vector2 barSize)
    {
        _name.AddThemeFontSizeOverride("font_size", fontSize);
        var textWidth = barSize.X - _bar.GetThemeStylebox("panel").GetMinimumSize().X;
        var measured = MeasureName(_name.GetThemeFont("font"), fontSize, textWidth);
        NameFits = measured.Lines <= MaximumNameLines && measured.Width <= textWidth + 0.1f;
        _name.CustomMinimumSize = new Vector2(0, measured.Height);
        _bar.Size = barSize;
        _bar.Position = new Vector2(-_bar.Size.X / 2, _bar.Position.Y);
    }

    public Vector2 GetRequiredNameBarSize(int fontSize)
    {
        var padding = _bar.GetThemeStylebox("panel").GetMinimumSize();
        var measured = MeasureName(_name.GetThemeFont("font"), fontSize, _bar.Size.X - padding.X);
        return new Vector2(_bar.Size.X, Math.Max(_minimumBarHeight, measured.Height + padding.Y));
    }

    private (int Lines, float Width, float Height) MeasureName(Font font, int size, float width)
    {
        // Use the actual Label for line count and height, at the same width its
        // PanelContainer will allocate. Separate paragraph estimates used to
        // overcount lines and made every bar in the row unnecessarily tall.
        _name.CustomMinimumSize = Vector2.Zero;
        _name.AddThemeFontSizeOverride("font_size", size);
        _name.Size = new Vector2(width, 0);
        var lines = _name.GetLineCount();
        // Wrapped Label minimum height is intentionally only 1 px; its per-line
        // metrics report the actual shaped height, including fallback fonts.
        var height = Math.Max(0, lines - 1) * _name.GetThemeConstant("line_spacing");
        for (var line = 0; line < lines; line++) height += _name.GetLineHeight(line);
        using var paragraph = new TextParagraph();
        paragraph.Width = width;
        paragraph.BreakFlags = TextServer.LineBreakFlag.Mandatory
            | TextServer.LineBreakFlag.TrimStartEdgeSpaces | TextServer.LineBreakFlag.TrimEndEdgeSpaces;
        if (_name.AutowrapMode != TextServer.AutowrapMode.Off)
            paragraph.BreakFlags |= TextServer.LineBreakFlag.WordBound | TextServer.LineBreakFlag.Adaptive;
        paragraph.AddString(_name.Text, font, size);
        var measuredWidth = 0f;
        for (var line = 0; line < paragraph.GetLineCount(); line++)
            measuredWidth = Math.Max(measuredWidth, paragraph.GetLineSize(line).X);
        return (lines, measuredWidth, Mathf.Ceil(height));
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
