#if DEBUG && !DEMO_BUILD && !RECORDING_BUILD
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;

namespace LuckyDogRise.Rooms;

// A visual fixture only: no RoomClient, platform, inventory or saved settings.
public partial class RoomNameBarLocalizationLab : Control
{
    [Export] private Control _gridArea = null!;
    [Export] private GridContainer _grid = null!;
    [Export] private Control _template = null!;
    [Export] private OptionButton _scaleOption = null!;
    [Export] private CheckButton _crown = null!;
    [Export] private Label _summary = null!;
    [Export] private Label _pageLabel = null!;
    [Export] private Button _previous = null!;
    [Export] private Button _next = null!;
    [Export] private Button _capture = null!;
    [Export] private Button _fit = null!;
    [Export] private Button _minimize = null!;
    [Export] private Button _close = null!;
    [Export] private Label _title = null!;
    [Export] private int _reservedLanguages = 31;
    [Export] private int _feedbackSpace = 200;
    [Export] private Vector2I _minimumWindowSize = new(800, 640);

    private static readonly string[] Keys = { "Rooms_CompanionCoder", "Rooms_CompanionArtist", "Rooms_CompanionDesigner" };
    private static readonly string[] Jobs = { "程序员", "美术", "策划" };
    private static readonly float[] Scales = { 0.5f, 1, 2, 3, 4 };
    private static readonly Color Normal = new("a4c5ce"), Warning = new("e1bf84"), Failure = new("eea4a4");
    private readonly List<Sample> _samples = new();
    private readonly Dictionary<int, Font> _fonts = new();
    private readonly Dictionary<float, StyleBoxFlat> _styles = new();
    private Font _sourceFont;
    private StyleBoxFlat _sourceStyle;
    private Vector2 _barSize;
    private int _page, _pageSize = 1, _knownLanguages;
    private bool _queued, _validating;
    private float ScaleFactor => Scales[_scaleOption.Selected];
    private int PageCount => Math.Max(1, (_samples.Count + _pageSize - 1) / _pageSize);
    private string OutputDirectory => ProjectSettings.GlobalizePath("res://../.local-build/room-namebar-localization");

    private sealed record Sample(string Locale, string[] Texts, Control Root, Label Heading,
        PanelContainer[] Bars, Label[] Names, Label Status);

    public override void _Ready()
    {
        if (BuildInfo.Channel != BuildChannel.Dev || BuildInfo.IsDebugDemo || OS.HasFeature("lucky_demo"))
        {
            GetTree().Quit(1);
            return;
        }
        GetWindow().Title = "小狗来啦 · 计数器昵称多语言检查";
        GetWindow().ContentScaleMode = Window.ContentScaleModeEnum.Disabled;
        GetWindow().ContentScaleSize = Vector2I.Zero;
        GetWindow().MinSize = _minimumWindowSize;
        GetViewport().TransparentBg = false;
        FitWindow();
        foreach (float scale in Scales) _scaleOption.AddItem($"{scale:0.0}x");
        _scaleOption.Select(1);
        var prototype = _template.GetNode<PanelContainer>("Coder");
        _barSize = prototype.Size;
        _sourceFont = prototype.GetNode<Label>("Name").GetThemeFont("font");
        _sourceStyle = (StyleBoxFlat)prototype.GetThemeStylebox("panel");
        Populate();
        _scaleOption.ItemSelected += _ => { _page = 0; QueueLayout(); };
        _crown.Toggled += _ => QueueLayout();
        _previous.Pressed += () => ChangePage(-1);
        _next.Pressed += () => ChangePage(1);
        _capture.Pressed += CapturePage;
        _fit.Pressed += FitWindow;
        _minimize.Pressed += () => GetWindow().Mode = Window.ModeEnum.Minimized;
        _close.Pressed += () => GetTree().Quit();
        _title.GuiInput += input =>
        {
            if (input is InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true } mouse)
            {
                if (mouse.DoubleClick) FitWindow();
                else DisplayServer.WindowStartDrag();
            }
        };
        _gridArea.Resized += QueueLayout;
        QueueLayout();
        if (OS.GetCmdlineUserArgs().Contains("--room-namebar-smoke")) RunValidation();
    }

    private void FitWindow()
    {
        var usable = DisplayServer.ScreenGetUsableRect(GetWindow().CurrentScreen);
        if (usable.Size.X <= 0 || usable.Size.Y <= 0) return;
        GetWindow().Mode = Window.ModeEnum.Windowed;
        GetWindow().Position = usable.Position;
        GetWindow().Size = new Vector2I(usable.Size.X,
            Math.Max(_minimumWindowSize.Y, usable.Size.Y - _feedbackSpace));
    }

    private void Populate()
    {
        // Read the catalog's locale columns instead of maintaining a second list.
        // Fetch exact imported messages like the game, never an English fallback.
        using var file = Godot.FileAccess.Open("res://Scenes/Rooms/InGameRoomText.csv", Godot.FileAccess.ModeFlags.Read);
        var locales = file.GetCsvLine().Skip(1).Where(column => !column.StartsWith('_')).ToArray();
        _knownLanguages = locales.Length;
        for (int index = 0; index < Math.Max(_reservedLanguages, locales.Length); index++)
        {
            string locale = index < locales.Length ? locales[index] : "";
            string[] texts = new string[Keys.Length];
            if (locale.Length > 0)
            {
                string path = $"res://Scenes/Rooms/InGameRoomText.{(locale == "es_419" ? "es" : locale)}.translation";
                var translation = ResourceLoader.Exists(path) ? GD.Load<Translation>(path) : null;
                for (int job = 0; job < Keys.Length; job++)
                    texts[job] = translation?.GetMessage(Keys[job]).ToString() ?? "";
            }
            var root = (Control)_template.Duplicate();
            root.Name = $"Locale_{(locale.Length > 0 ? locale : $"Reserved{index + 1}")}";
            var bars = new[] { "Coder", "Artist", "Designer" }.Select(name => root.GetNode<PanelContainer>(name)).ToArray();
            var sample = new Sample(locale, texts, root, root.GetNode<Label>("Locale"), bars,
                bars.Select(bar => bar.GetNode<Label>("Name")).ToArray(), root.GetNode<Label>("Status"));
            sample.Heading.Text = locale.Length > 0
                ? $"{index + 1:00} · {locale}  {L10n.GetDisplayName(locale)}" : $"{index + 1:00} · 预留语言（未配置）";
            root.Show();
            _grid.AddChild(root);
            _samples.Add(sample);
        }
    }

    private void QueueLayout()
    {
        if (_queued) return;
        _queued = true;
        Callable.From(() => { _queued = false; Layout(); }).CallDeferred();
    }

    private void Layout()
    {
        float scale = ScaleFactor;
        int oversampling = Math.Max(1, Mathf.CeilToInt(scale));
        if (!_fonts.TryGetValue(oversampling, out var font))
        {
            font = (Font)_sourceFont.Duplicate();
            if (font is FontFile file) file.Oversampling = oversampling;
            else if (font is SystemFont system) system.Oversampling = oversampling;
            _fonts[oversampling] = font;
        }
        if (!_styles.TryGetValue(scale, out var style))
        {
            style = (StyleBoxFlat)_sourceStyle.Duplicate();
            style.AntiAliasingSize = _sourceStyle.AntiAliasingSize / scale;
            _styles[scale] = style;
        }
        float cellWidth = Math.Max(208, _barSize.X * scale + 24);
        float barsBottom = 32 + _barSize.Y * scale * 3 + 16;
        var cellSize = new Vector2(cellWidth, barsBottom + 28);
        int horizontalGap = _grid.GetThemeConstant("h_separation");
        int verticalGap = _grid.GetThemeConstant("v_separation");
        int columns = Math.Clamp((int)((_gridArea.Size.X + horizontalGap) / (cellWidth + horizontalGap)), 1, 8);
        int rows = Math.Max(1, (int)((_gridArea.Size.Y + verticalGap) / (cellSize.Y + verticalGap)));
        _pageSize = columns * rows;
        _page = Math.Clamp(_page, 0, PageCount - 1);
        _grid.Columns = columns;
        int missing = 0, shrunk = 0, issues = 0;
        foreach (var sample in _samples)
        {
            sample.Root.CustomMinimumSize = cellSize;
            sample.Root.Size = cellSize;
            sample.Heading.Size = new Vector2(cellWidth - 12, 28);
            sample.Status.Position = new Vector2(12, barsBottom + 4);
            sample.Status.Size = new Vector2(cellWidth - 24, 24);
            int sampleIssues = 0, sampleMissing = 0;
            var sizes = new List<int>();
            for (int job = 0; job < 3; job++)
            {
                var bar = sample.Bars[job];
                var label = sample.Names[job];
                bool absent = string.IsNullOrWhiteSpace(sample.Texts[job]) || sample.Texts[job] == Keys[job];
                label.Text = absent ? $"{Jobs[job]} · 缺译文" : (_crown.ButtonPressed ? "♛ " : "") + sample.Texts[job];
                bar.Position = new Vector2(12, 32 + job * (_barSize.Y * scale + 8));
                bar.Size = _barSize;
                bar.Scale = Vector2.One * scale;
                bar.AddThemeStyleboxOverride("panel", style);
                label.AddThemeFontOverride("font", font);
                float width = _barSize.X - style.GetContentMargin(Side.Left) - style.GetContentMargin(Side.Right);
                int fontSize = RoomDogView.NameFontSize(font, label.Text, width, true);
                label.AddThemeFontSizeOverride("font_size", fontSize);
                sizes.Add(fontSize);
                if (absent) { missing++; sampleMissing++; }
                else
                {
                    if (fontSize < 14) shrunk++;
                    if (BadText(sample, job, width)) { issues++; sampleIssues++; }
                }
                bar.TooltipText = $"{Jobs[job]}：{label.Text}\n字号 {fontSize}px / 可用宽度 {width:0.#}px";
                // Do not tint or resize the real bar to hide a failed case.
            }
            sample.Status.Text = sampleMissing > 0 ? $"缺译文 {sampleMissing}/3"
                : $"{string.Join(" / ", sizes)} px · {(sampleIssues > 0 ? "需检查" : sizes.Any(size => size < 14) ? "已缩字" : "完整")}";
            sample.Status.AddThemeColorOverride("font_color", sampleIssues > 0 ? Failure : sampleMissing > 0 ? Warning : Normal);
        }
        _summary.Text = $"{_knownLanguages} 种语言 / {_samples.Count} 个位置 / {_samples.Count * 3} 条　缺译文 {missing}　缩字 {shrunk}　溢出或换行 {issues}";
        ShowPage();
    }

    private static bool BadText(Sample sample, int job, float width)
    {
        var label = sample.Names[job];
        var font = label.GetThemeFont("font");
        int size = label.GetThemeFontSize("font_size");
        return label.Text.Contains('\n') || label.Text.Contains('\r') || label.Text.Contains("\\n")
            || font.GetStringSize(label.Text, HorizontalAlignment.Left, -1, size).X > width + 0.5f;
    }

    private void ChangePage(int offset)
    {
        _page = Math.Clamp(_page + offset, 0, PageCount - 1);
        ShowPage();
    }

    private void ShowPage()
    {
        for (int index = 0; index < _samples.Count; index++)
            _samples[index].Root.Visible = index >= _page * _pageSize && index < (_page + 1) * _pageSize;
        _grid.Size = Vector2.Zero;
        _grid.QueueSort();
        _previous.Disabled = _page == 0;
        _next.Disabled = _page == PageCount - 1;
        _pageLabel.Text = $"第 {_page + 1} / {PageCount} 页　{_page * _pageSize + 1}–{Math.Min((_page + 1) * _pageSize, _samples.Count)}";
    }

    public override void _UnhandledKeyInput(InputEvent input)
    {
        if (input is not InputEventKey { Pressed: true, Echo: false } key || _validating) return;
        if (key.Keycode == Key.Escape) GetTree().Quit();
        if (key.Keycode is Key.Pageup or Key.Left) ChangePage(-1);
        if (key.Keycode is Key.Pagedown or Key.Right) ChangePage(1);
    }

    public async void CapturePage()
    {
        if (_validating) return;
        await Frames();
        string path = SaveImage($"manual-{DateTime.Now:yyyyMMdd-HHmmss}-page{_page + 1}");
        _summary.Text = $"截图已保存：{path}";
    }

    private async Task Frames()
    {
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    }

    private string SaveImage(string name)
    {
        System.IO.Directory.CreateDirectory(OutputDirectory);
        string path = System.IO.Path.Combine(OutputDirectory, name + ".png");
        var result = GetViewport().GetTexture().GetImage().SavePng(path);
        if (result != Error.Ok) throw new InvalidOperationException($"Screenshot failed: {result}");
        return path;
    }

    private async void RunValidation()
    {
        _validating = true;
        try
        {
            await Frames();
            int checkedBars = 0;
            foreach (bool crown in new[] { false, true })
            foreach (int scaleIndex in Enumerable.Range(0, Scales.Length))
            {
                _crown.ButtonPressed = crown;
                _scaleOption.Select(scaleIndex);
                Layout();
                await Frames();
                var seen = new HashSet<Sample>();
                for (int page = 0; page < PageCount; page++)
                {
                    _page = page;
                    ShowPage();
                    await Frames();
                    foreach (var sample in _samples.Where(sample => sample.Root.Visible))
                    {
                        if (!seen.Add(sample)) throw new InvalidOperationException("Page repeats a language.");
                        if (!_gridArea.GetGlobalRect().Grow(0.5f).Encloses(sample.Root.GetGlobalRect()))
                            throw new InvalidOperationException($"Cell outside viewport: {sample.Heading.Text}");
                        for (int job = 0; job < 3; job++)
                        {
                            var label = sample.Names[job];
                            var bar = sample.Bars[job];
                            if (BadText(sample, job, label.Size.X) || label.GetLineCount() != 1
                                || label.GetLineHeight() > label.Size.Y + 0.5f
                                || !bar.GetGlobalRect().Grow(0.5f).Encloses(label.GetGlobalRect()))
                                throw new InvalidOperationException($"Clipped bar: {sample.Heading.Text}, {label.Text}, {ScaleFactor}x");
                            if (sample.Locale.Length > 0 && string.IsNullOrWhiteSpace(sample.Texts[job]))
                                throw new InvalidOperationException($"Missing supported translation: {sample.Locale}, {Keys[job]}");
                            checkedBars++;
                        }
                    }
                    if (ScaleFactor is 1 or 2 && DisplayServer.GetName() != "headless")
                    {
                        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                        SaveImage($"{ScaleFactor:0}x-{(crown ? "host" : "member")}-page{page + 1}");
                    }
                }
                if (seen.Count != _samples.Count) throw new InvalidOperationException("Pagination omitted languages.");
            }
            // Exercise resize-driven pagination as well as scale-driven pagination.
            GetWindow().Size = _minimumWindowSize;
            await Frames();
            Layout();
            await Frames();
            if (_pageSize < 1 || _page >= PageCount) throw new InvalidOperationException("Invalid compact pagination.");
            foreach (var sample in _samples.Where(sample => sample.Root.Visible))
                if (!_gridArea.GetGlobalRect().Grow(0.5f).Encloses(sample.Root.GetGlobalRect()))
                    throw new InvalidOperationException("Compact window clips a scaled language cell.");
            _page = 0;
            ShowPage();
            _next.EmitSignal(BaseButton.SignalName.Pressed);
            if (_page != 1) throw new InvalidOperationException("Next-page button did not advance.");
            _previous.EmitSignal(BaseButton.SignalName.Pressed);
            if (_page != 0) throw new InvalidOperationException("Previous-page button did not return.");
            await Frames();
            if (DisplayServer.GetName() != "headless")
            {
                await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                SaveImage("compact-4x-host");
            }
            GD.Print($"[RoomNameBarLab] PASS: {checkedBars} bars; {_knownLanguages} translated / {_samples.Count} slots; all scales, crowns and pages.");
            GetTree().Quit();
        }
        catch (Exception error)
        {
            GD.PushError($"[RoomNameBarLab] FAIL: {error}");
            GetTree().Quit(1);
        }
    }
}
#endif
