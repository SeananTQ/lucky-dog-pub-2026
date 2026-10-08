using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace LuckyDogRise.Tools;

public partial class StoreDialogueStage : Node2D
{
    [Export] private Node2D _dogsLayer = null!;
    [Export] private PanelContainer _bubble = null!;
    [Export] private Label _bubbleText = null!;
    [Export] private Polygon2D _tail = null!;
    [Export] private PackedScene _dogScene = null!;
    private StoreDialogueProject _project = null!;
    private StoreDialogueTimeline _timeline = null!;
    private readonly List<StoreDialogueDogView> _views = new();
    public IReadOnlyList<StoreDialogueDogView> Dogs => _views;
    public StoreDialogueTimeline Timeline => _timeline;
    public bool BubbleFits { get; private set; } = true;
    public bool LayoutFits { get; private set; } = true;
    public string CurrentSpeaker { get; private set; } = "";

    public void Configure(StoreDialogueProject project, string language)
    {
        _project = project;
        var locale = project.Locales[language];
        _timeline = new StoreDialogueTimeline(locale, project.LeadInSeconds, project.LineGapSeconds, project.TailSeconds);
        foreach (var view in _views) { _dogsLayer.RemoveChild(view); view.QueueFree(); }
        _views.Clear();
        var width = 900 * (float)project.DogScale;
        var total = project.Dogs.Count * width + (project.Dogs.Count - 1) * (float)project.GapPixels;
        var left = (project.CanvasWidth - total) / 2;
        var baseline = project.CanvasHeight - project.NameFontSize - 24 - 75 * (float)project.DogScale;
        LayoutFits = total <= project.CanvasWidth - 16 && baseline - 750 * project.DogScale >= 8;
        for (var i = 0; i < project.Dogs.Count; i++)
        {
            var dog = project.Dogs[i];
            var view = _dogScene.Instantiate<StoreDialogueDogView>();
            _dogsLayer.AddChild(view);
            view.Position = new Vector2(left + width / 2 + i * (width + (float)project.GapPixels), baseline);
            view.Configure(dog, locale.Names.GetValueOrDefault(dog.Id, ""), (float)project.DogScale, project.NameFontSize);
            _views.Add(view);
        }
        // The longest localized name determines one font size for the entire row.
        // Share the largest measured panel bounds as well, including fallback-font height.
        var sharedFontSize = _views.Min(view => view.NameFontSize);
        var requiredBars = _views.Select(view => view.GetRequiredNameBarSize(sharedFontSize)).ToArray();
        var sharedBarSize = new Vector2(requiredBars.Max(size => size.X), requiredBars.Max(size => size.Y));
        foreach (var view in _views) view.ApplySharedNameBarLayout(sharedFontSize, sharedBarSize);
        // Keep an actual pixel margin below the tallest localized name bar.
        baseline = project.CanvasHeight - 12 - _views.Max(view => view.NameBarBottom);
        foreach (var view in _views) view.Position = new Vector2(view.Position.X, baseline);
        LayoutFits = total <= project.CanvasWidth - 16 && baseline - 750 * project.DogScale >= 8;
        _bubbleText.AutoTranslateMode = AutoTranslateModeEnum.Disabled;
        _bubbleText.AddThemeFontSizeOverride("font_size", project.BubbleFontSize);
        Seek(0);
    }

    public void Seek(double seconds, bool loop = false, string auditionDogId = null)
    {
        var moment = _timeline.Sample(seconds, loop);
        CurrentSpeaker = auditionDogId ?? moment.SpeakerDogId ?? "";
        foreach (var dog in _views) dog.SetSpeaking(dog.DogId == CurrentSpeaker);
        var speaker = _views.FirstOrDefault(d => d.DogId == CurrentSpeaker);
        var text = auditionDogId != null ? "" : moment.Text;
        _bubble.Visible = speaker != null && !string.IsNullOrWhiteSpace(text);
        BubbleFits = true;
        if (!_bubble.Visible) return;
        _bubbleText.Text = text;
        var style = _bubble.GetThemeStylebox("panel");
        var padding = style.GetMinimumSize();
        var inset = new Vector2(style.GetContentMargin(Side.Left), style.GetContentMargin(Side.Top));
        var font = _bubbleText.GetThemeFont("font");
        var naturalWidth = text.Split('\n').Max(line => font.GetStringSize(line, fontSize: _project.BubbleFontSize).X);
        var maximumWidth = Math.Min(_project.BubbleWidth, _project.CanvasWidth - 24);
        var width = Mathf.Clamp(Mathf.Ceil(naturalWidth + padding.X), Math.Min(64, maximumWidth), maximumWidth);
        // Shape the actual WordSmart label at its new width before sizing the panel.
        // This also handles long words, fallback fonts, and seeking from long to short
        // lines without relying on the next frame's deferred container layout.
        _bubbleText.Position = inset;
        _bubbleText.Size = new Vector2(Math.Max(1, width - padding.X), 0);
        var textHeight = _bubbleText.GetMinimumSize().Y;
        var height = Mathf.Ceil(textHeight + padding.Y);
        _bubble.Size = new Vector2(width, height);
        _bubbleText.Size = new Vector2(width - padding.X, height - padding.Y);
        var topOfDog = speaker.Position.Y - 750 * (float)_project.DogScale;
        var x = Mathf.Clamp(speaker.Position.X - width / 2, 12, _project.CanvasWidth - width - 12);
        var y = topOfDog - height - 16;
        BubbleFits = y >= 8;
        _bubble.Position = new Vector2(x, Math.Max(8, y));
        // Keep the base inside the rounded panel while the tip stays over this dog's
        // head. Edge-clamped bubbles therefore angle their tail toward the speaker.
        var baseX = Mathf.Clamp(speaker.Position.X - x, width * 0.3f, width * 0.7f);
        _tail.Position = new Vector2(baseX, height - 1);
        var tip = new Vector2(speaker.Position.X, topOfDog) - _bubble.Position - _tail.Position;
        _tail.Polygon = new[] { new Vector2(-9, 0), new Vector2(9, 0), tip };
    }

    public int PickDog(Vector2 point)
    {
        for (var i = 0; i < _views.Count; i++)
            if (_views[i].PickRect.HasPoint(point - _views[i].Position)) return i;
        return -1;
    }

    public IReadOnlyList<string> ValidatePresentation()
    {
        var errors = new List<string>();
        if (!LayoutFits) errors.Add("狗狗超出画布，请增加宽高，或减小数量、大小、间距。");
        foreach (var view in _views)
            if (!view.NameFits) errors.Add("姓名板最多两行，请缩短名字或增大小狗显示尺寸：" + view.DogId);
        // Check each bubble, including long translations, before creating export output.
        var count = (int)Math.Ceiling(_timeline.DurationSeconds * _project.Fps);
        for (var i = 0; i < count; i++)
        {
            Seek((double)i / _project.Fps);
            if (!BubbleFits)
            {
                errors.Add("气泡空间不足，请增加画布高度、加宽气泡、缩小字号或缩短对白。");
                break;
            }
        }
        Seek(0);
        return errors;
    }
}
