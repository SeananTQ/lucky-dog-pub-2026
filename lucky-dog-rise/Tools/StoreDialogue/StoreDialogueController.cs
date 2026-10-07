using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using DataTables;
using Godot;
using FileAccess = System.IO.File;

namespace LuckyDogRise.Tools;

public partial class StoreDialogueController : Control
{
    [Export] private VBoxContainer _layout = null!;
    [Export] private TabContainer _settings = null!;
    [Export] private Button _new = null!, _open = null!, _save = null!, _saveAs = null!, _back = null!;
    [Export] private OptionButton _locale = null!;
    [Export] private TextureRect _preview = null!;
    [Export] private ColorRect _leftGuide = null!, _rightGuide = null!;
    [Export] private Button _play = null!, _stop = null!;
    [Export] private Label _time = null!, _status = null!;
    [Export] private HSlider _scrub = null!;
    [Export] private ProgressBar _progress = null!;
    [Export] private SpinBox _width = null!, _height = null!, _count = null!, _scale = null!, _gap = null!;
    [Export] private SpinBox _nameSize = null!, _bubbleSize = null!, _bubbleWidth = null!;
    [Export] private SpinBox _lead = null!, _lineGap = null!, _tail = null!, _fps = null!;
    [Export] private ItemList _dogList = null!, _lines = null!;
    [Export] private LineEdit _name = null!, _localeCode = null!, _exportFolder = null!, _ffmpeg = null!;
    [Export] private OptionButton _hat = null!, _idleReaction = null!, _speakReaction = null!;
    [Export] private Button _editAppearance = null!, _idlePreview = null!, _speakPreview = null!, _addLocale = null!;
    [Export] private Button _addLine = null!, _removeLine = null!, _upLine = null!, _downLine = null!;
    [Export] private OptionButton _speaker = null!, _format = null!;
    [Export] private TextEdit _dialogue = null!;
    [Export] private SpinBox _duration = null!;
    [Export] private Button _chooseFolder = null!, _chooseFfmpeg = null!, _export = null!, _exportAll = null!, _cancelExport = null!;
    [Export] private SubViewport _viewport = null!;
    [Export] private StoreDialogueStage _stage = null!;
    [Export] private Control _overlay = null!;
    [Export] private FileDialog _fileDialog = null!;
    [Export] private ConfirmationDialog _confirm = null!;

    private StoreDialogueProject _project = null!;
    private const float ReferenceWidth = 720f;
    private readonly DogSkinAssetCatalog _assets = new();
    private string _language = "zh-CN";
    private string _projectPath = "";
    private string _preferencesPath = "";
    private int _dogIndex, _lineIndex;
    private bool _refreshing, _dirty, _playing, _busy, _previousAutoQuit;
    private double _seconds;
    private CancellationTokenSource _exportCancellation;
    private Action<string> _fileAction;
    private Action _confirmedAction;
    private DogSkinEditorController _appearanceEditor;
    public StoreDialogueProject Project => _project;
    public StoreDialogueStage Stage => _stage;

    public override void _Ready()
    {
        var window = GetWindow();
        window.Mode = Window.ModeEnum.Windowed;
        window.Borderless = false;
        window.Transparent = false;
        window.TransparentBg = false;
        window.AlwaysOnTop = false;
        window.Unresizable = false;
        window.Title = "Lucky Dog Rise - 商店页对话视频";
        _previousAutoQuit = GetTree().AutoAcceptQuit;
        GetTree().AutoAcceptQuit = false;
        window.CloseRequested += OnCloseRequested;
        _preview.Texture = _viewport.GetTexture();
        var home = Path.GetFullPath(ProjectSettings.GlobalizePath("res://../.local-build/store-dialogue"));
        _projectPath = Path.Combine(home, "dialogue.json");
        _preferencesPath = Path.Combine(home, "preferences.json");
        _exportFolder.Text = Path.Combine(home, "exports");
        _format.AddItem("透明 WebM 视频", (int)StoreDialogueExportFormat.WebM);
        _format.AddItem("透明动态 WebP", (int)StoreDialogueExportFormat.WebP);
        _format.AddItem("透明 PNG 帧序列", (int)StoreDialogueExportFormat.PngSequence);
        LoadPreferences();
        ConnectControls();
        try
        {
            ApplyProject(FileAccess.Exists(_projectPath) ? StoreDialogueProject.Load(_projectPath) : CreateExample(), "zh-CN");
            SetStatus("就绪。调整方案后保存 JSON；新增语言的名字与对白需独立填写。");
        }
        catch (Exception e)
        {
            ApplyProject(CreateExample(), "zh-CN");
            SetStatus("原方案加载失败，已显示临时示例，原文件未改动：" + e.Message);
            _projectPath = Path.Combine(home, "recovered-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".json");
        }
    }

    private void ConnectControls()
    {
        _new.Pressed += () => GuardUnsaved(() =>
        {
            ApplyProject(CreateExample(), "zh-CN");
            _projectPath = Path.Combine(Path.GetDirectoryName(_preferencesPath), "dialogue-" + DateTime.Now.ToString("yyyyMMdd-HHmmss-fff") + ".json");
            _dirty = true; SetStatus("已创建新的临时示例方案。");
        });
        _open.Pressed += () => GuardUnsaved(() => ChooseFile(FileDialog.FileModeEnum.OpenFile, new[] { "*.json ; 对话方案" }, path =>
        {
            ApplyProject(StoreDialogueProject.Load(path), _language);
            _projectPath = path;
            SetStatus("已打开：" + path);
        }));
        _save.Pressed += () => TryAction(() => SaveProject(_projectPath));
        _saveAs.Pressed += () => ChooseFile(FileDialog.FileModeEnum.SaveFile, new[] { "*.json ; 对话方案" }, SaveProject);
        _back.Pressed += () => GuardUnsaved(() => GetTree().ChangeSceneToFile("res://Tools/DogSkinEditor/DogSkinEditor.tscn"));
        _confirm.Confirmed += () => { var action = _confirmedAction; _confirmedAction = null; action?.Invoke(); };
        _fileDialog.FileSelected += path => InvokeFileAction(path);
        _fileDialog.DirSelected += path => InvokeFileAction(path);
        _fileDialog.Canceled += () => _fileAction = null;
        _locale.ItemSelected += i => { if (!_refreshing && !_busy) SetLanguage(_locale.GetItemText((int)i)); };
        _play.Pressed += () => { if (!_busy && _stage.Timeline.DurationSeconds > 0) { _seconds = 0; _playing = true; } };
        _stop.Pressed += Stop;
        _scrub.ValueChanged += value =>
        {
            if (_refreshing || _busy) return;
            _playing = false; _seconds = value; Seek();
        };
        _preview.Resized += UpdateReferenceGuides;
        _preview.GuiInput += @event =>
        {
            if (_busy || @event is not InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left } click) return;
            var canvas = GetPreviewCanvasRect();
            if (!canvas.HasPoint(click.Position)) return;
            var scale = canvas.Size.X / _project.CanvasWidth;
            var index = _stage.PickDog((click.Position - canvas.Position) / scale);
            if (index >= 0) { _dogIndex = index; RefreshDogEditor(); _settings.CurrentTab = 1; }
        };
        foreach (var spin in new[] { _width, _height, _count, _scale, _gap, _nameSize, _bubbleSize, _bubbleWidth, _lead, _lineGap, _tail, _fps })
            spin.ValueChanged += _ => { if (!_refreshing && !_busy) UpdateLayout(); };
        _dogList.ItemSelected += i => { if (!_refreshing && !_busy) { _dogIndex = (int)i; RefreshDogEditor(); } };
        _name.TextChanged += value =>
        {
            if (_refreshing || _busy) return;
            CurrentLocale.Names[CurrentDog.Id] = value;
            Changed();
            RefreshListsOnly();
        };
        _hat.ItemSelected += i => { if (!_refreshing && !_busy) { CurrentDog.HeadwearItemId = _hat.GetItemId((int)i); Changed(); } };
        WireReaction(_idleReaction, () => CurrentDog.Idle);
        WireReaction(_speakReaction, () => CurrentDog.Speaking, speaking: true);
        _editAppearance.Pressed += EditAppearance;
        _idlePreview.Pressed += Stop;
        _speakPreview.Pressed += () =>
        {
            if (_busy) return;
            _playing = false;
            _stage.Seek(0, auditionDogId: CurrentDog.Id);
            SetStatus("预览当前小狗的说话动作表情；爪子保持手背。");
        };
        _addLocale.Pressed += AddLocale;
        _lines.ItemSelected += i => { if (!_refreshing && !_busy) { _lineIndex = (int)i; RefreshLineEditor(); } };
        _addLine.Pressed += () =>
        {
            CurrentLocale.Lines.Add(new StoreDialogueLine { DogId = CurrentDog.Id, Text = "", DurationSeconds = 2 });
            _lineIndex = CurrentLocale.Lines.Count - 1; Changed(); RefreshListsOnly(); RefreshLineEditor();
        };
        _removeLine.Pressed += () =>
        {
            if (CurrentLine == null) return;
            CurrentLocale.Lines.RemoveAt(_lineIndex); _lineIndex = Math.Max(0, _lineIndex - 1);
            Changed(); RefreshListsOnly(); RefreshLineEditor();
        };
        _upLine.Pressed += () => MoveLine(-1);
        _downLine.Pressed += () => MoveLine(1);
        _speaker.ItemSelected += i =>
        {
            if (_refreshing || CurrentLine == null || _busy) return;
            CurrentLine.DogId = _project.Dogs[(int)i].Id; Changed(); RefreshListsOnly();
        };
        _dialogue.TextChanged += () =>
        {
            if (_refreshing || CurrentLine == null || _busy) return;
            CurrentLine.Text = _dialogue.Text; Changed(); RefreshListsOnly();
        };
        _duration.ValueChanged += value =>
        {
            if (_refreshing || CurrentLine == null || _busy) return;
            CurrentLine.DurationSeconds = value; Changed(); RefreshListsOnly();
        };
        _chooseFolder.Pressed += () => ChooseFile(FileDialog.FileModeEnum.OpenDir, Array.Empty<string>(), path => _exportFolder.Text = path);
        _chooseFfmpeg.Pressed += () => ChooseFile(FileDialog.FileModeEnum.OpenFile, new[] { "*.exe ; FFmpeg" }, path => _ffmpeg.Text = path);
        _export.Pressed += () => StartExport(false);
        _exportAll.Pressed += () => StartExport(true);
        _cancelExport.Pressed += () => _exportCancellation?.Cancel();
    }

    private StoreDialogueDog CurrentDog => _project.Dogs[_dogIndex];
    private StoreDialogueLocale CurrentLocale => _project.Locales[_language];
    private StoreDialogueLine CurrentLine => _lineIndex >= 0 && _lineIndex < CurrentLocale.Lines.Count ? CurrentLocale.Lines[_lineIndex] : null;

    public void ApplyProject(StoreDialogueProject project, string language)
    {
        var candidateLanguage = project.Locales.ContainsKey(language) ? language : project.Locales.Keys.First();
        var errors = project.Validate(candidateLanguage).Concat(ValidateAssets(project)).ToArray();
        if (errors.Length > 0) throw new InvalidDataException(string.Join("\n", errors));
        _project = project.Clone();
        _language = _project.Locales.ContainsKey(language) ? language : _project.Locales.Keys.First();
        _dogIndex = _lineIndex = 0; _playing = false; _seconds = 0; _dirty = false;
        RefreshAll();
    }

    public void SetLanguage(string language)
    {
        if (!_project.Locales.ContainsKey(language)) return;
        _language = language; _lineIndex = 0; _seconds = 0; _playing = false;
        RefreshAll();
    }

    private void RefreshAll()
    {
        _refreshing = true;
        _locale.Clear();
        foreach (var code in _project.Locales.Keys) _locale.AddItem(code);
        _locale.Select(Array.IndexOf(_project.Locales.Keys.ToArray(), _language));
        _width.Value = _project.CanvasWidth; _height.Value = _project.CanvasHeight; _count.Value = _project.Dogs.Count;
        _scale.Value = _project.DogScale; _gap.Value = _project.GapPixels;
        _nameSize.Value = _project.NameFontSize; _bubbleSize.Value = _project.BubbleFontSize; _bubbleWidth.Value = _project.BubbleWidth;
        _lead.Value = _project.LeadInSeconds; _lineGap.Value = _project.LineGapSeconds; _tail.Value = _project.TailSeconds; _fps.Value = _project.Fps;
        _refreshing = false;
        RefreshListsOnly(); RefreshDogEditor(); RefreshLineEditor(); RebuildStage();
    }

    private void RefreshListsOnly()
    {
        _refreshing = true;
        _dogList.Clear(); _speaker.Clear(); _lines.Clear();
        for (var i = 0; i < _project.Dogs.Count; i++)
        {
            var dog = _project.Dogs[i];
            var label = $"{i + 1}. {CurrentLocale.Names.GetValueOrDefault(dog.Id, "")}";
            _dogList.AddItem(label); _speaker.AddItem(label);
        }
        _dogIndex = Math.Clamp(_dogIndex, 0, _project.Dogs.Count - 1);
        _dogList.Select(_dogIndex);
        foreach (var line in CurrentLocale.Lines)
            _lines.AddItem($"{CurrentLocale.Names.GetValueOrDefault(line.DogId, "未命名")} · {line.DurationSeconds:0.0}s · {line.Text.Replace('\n', ' ')}");
        if (CurrentLine != null)
        {
            _lines.Select(_lineIndex);
            _speaker.Select(_project.Dogs.FindIndex(d => d.Id == CurrentLine.DogId));
        }
        _refreshing = false;
    }

    private void RefreshDogEditor()
    {
        _refreshing = true;
        _dogList.Select(_dogIndex);
        _name.Text = CurrentLocale.Names.GetValueOrDefault(CurrentDog.Id, "");
        _hat.Clear(); _hat.AddItem("（无帽子）", 0);
        foreach (var hat in LubanData.Tables.TbItem.DataList.Where(i => i.ItemType == EItemType.Headwear).OrderBy(i => i.Id))
            _hat.AddItem($"{hat.Id} · {Path.GetFileNameWithoutExtension(hat.AssetPathList.FirstOrDefault()?.Replace('\\', '/') ?? "")}", hat.Id);
        _hat.Select(Math.Max(0, _hat.GetItemIndex(CurrentDog.HeadwearItemId)));
        FillReactions(_idleReaction, CurrentDog.Idle);
        FillReactions(_speakReaction, CurrentDog.Speaking);
        _refreshing = false;
    }

    private static void FillReactions(OptionButton option, StoreDialoguePose pose)
    {
        option.Clear();
        var legacy = !pose.ReactionId.HasValue && (!string.IsNullOrEmpty(pose.Eyes) || !string.IsNullOrEmpty(pose.Ears));
        if (legacy) option.AddItem("旧方案表情（选择动作后替换）", 0);
        foreach (var trigger in LubanData.Tables.TbDogReaction.DataList.Select(row => row.DogReactionTrigger).Distinct().OrderBy(value => (int)value))
            option.AddItem($"{(int)trigger}  {trigger}", (int)trigger);
        option.Select(option.GetItemIndex(legacy ? 0 : pose.ReactionId ?? (int)EDogReactionTrigger.Default));
    }

    private void WireReaction(OptionButton option, Func<StoreDialoguePose> pose, bool speaking = false)
    {
        option.ItemSelected += i =>
        {
            if (_refreshing || _busy || option.GetItemId((int)i) <= 0) return;
            var selected = pose();
            selected.ReactionId = option.GetItemId((int)i);
            selected.Eyes = ""; selected.Ears = "";
            Changed();
            RefreshDogEditor();
            _stage.Seek(0, auditionDogId: speaking ? CurrentDog.Id : "");
        };
    }

    private void RefreshLineEditor()
    {
        if (_busy) return;
        _refreshing = true;
        var line = CurrentLine;
        _dialogue.Editable = line != null; _duration.Editable = line != null; _speaker.Disabled = line == null;
        _dialogue.Text = line?.Text ?? "";
        _duration.Value = line?.DurationSeconds ?? 2;
        if (line != null) _speaker.Select(_project.Dogs.FindIndex(d => d.Id == line.DogId));
        _refreshing = false;
    }

    private void UpdateLayout()
    {
        var count = (int)_count.Value;
        while (_project.Dogs.Count < count)
        {
            var dog = CreateDog(_project.Dogs.Count);
            _project.Dogs.Add(dog);
            foreach (var pair in _project.Locales)
                pair.Value.Names[dog.Id] = pair.Key == "zh-CN" ? $"小狗 {_project.Dogs.Count}" : "";
        }
        if (_project.Dogs.Count > count)
        {
            _project.Dogs.RemoveRange(count, _project.Dogs.Count - count);
            _project.PruneRemovedDogs();
        }
        _project.CanvasWidth = (int)_width.Value; _project.CanvasHeight = (int)_height.Value;
        _project.DogScale = _scale.Value; _project.GapPixels = _gap.Value;
        _project.NameFontSize = (int)_nameSize.Value; _project.BubbleFontSize = (int)_bubbleSize.Value; _project.BubbleWidth = (int)_bubbleWidth.Value;
        _project.LeadInSeconds = _lead.Value; _project.LineGapSeconds = _lineGap.Value; _project.TailSeconds = _tail.Value; _project.Fps = (int)_fps.Value;
        Changed(); RefreshListsOnly(); RefreshDogEditor(); RefreshLineEditor();
    }

    private void AddLocale()
    {
        var code = _localeCode.Text.Trim();
        if (code.Length > 40 || !Regex.IsMatch(code, @"^[A-Za-z]{2,3}(?:-[A-Za-z0-9]{2,8})*$")) { SetStatus("请填写不超过 40 字符的语言代码，例如 en、zh-CN、ja。"); return; }
        if (_project.Locales.Count >= 64) { SetStatus("一个方案最多支持 64 种语言。"); return; }
        if (_project.Locales.ContainsKey(code)) { SetStatus("这个语言已存在。"); return; }
        var locale = new StoreDialogueLocale();
        foreach (var dog in _project.Dogs) locale.Names[dog.Id] = "";
        foreach (var line in CurrentLocale.Lines)
            locale.Lines.Add(new StoreDialogueLine { DogId = line.DogId, Text = "", DurationSeconds = line.DurationSeconds });
        _project.Locales.Add(code, locale); _dirty = true; SetLanguage(code);
        SetStatus("已新增空白翻译，请填写每只小狗的名字与各句对白。");
    }

    private void MoveLine(int delta)
    {
        var target = _lineIndex + delta;
        if (CurrentLine == null || target < 0 || target >= CurrentLocale.Lines.Count) return;
        (CurrentLocale.Lines[_lineIndex], CurrentLocale.Lines[target]) = (CurrentLocale.Lines[target], CurrentLocale.Lines[_lineIndex]);
        _lineIndex = target; Changed(); RefreshListsOnly(); RefreshLineEditor();
    }

    private void EditAppearance()
    {
        if (_busy || GodotObject.IsInstanceValid(_appearanceEditor)) return;
        Stop();
        var selected = CurrentDog;
        _appearanceEditor = GD.Load<PackedScene>("res://Tools/DogSkinEditor/DogAppearanceEditor.tscn").Instantiate<DogSkinEditorController>();
        _appearanceEditor.BeginEdit(selected.Appearance, appearance =>
        {
            var candidate = _project.Clone();
            var edited = candidate.Dogs.Single(dog => dog.Id == selected.Id);
            edited.Appearance = appearance;
            foreach (var pose in new[] { edited.Idle, edited.Speaking })
            {
                if (!_assets.ResourceExists(appearance.FolderPath, pose.Eyes)) pose.Eyes = "";
                if (!_assets.ResourceExists(appearance.FolderPath, pose.Ears)) pose.Ears = "";
            }
            var errors = ValidateAssets(candidate).ToArray();
            if (errors.Length > 0)
            {
                _appearanceEditor = null;
                SetStatus("外观未应用，请重新选择有效素材：\n" + string.Join("\n", errors));
                return;
            }
            selected.Appearance = edited.Appearance;
            selected.Idle = edited.Idle; selected.Speaking = edited.Speaking;
            Changed(); RefreshDogEditor(); _appearanceEditor = null;
        }, () => _appearanceEditor = null);
        _overlay.AddChild(_appearanceEditor);
    }

    private void Changed() { _dirty = true; _playing = false; _seconds = 0; RebuildStage(); }

    private Rect2 GetPreviewCanvasRect()
    {
        var canvasSize = new Vector2(_project.CanvasWidth, _project.CanvasHeight);
        var scale = Mathf.Min(_preview.Size.X / canvasSize.X, _preview.Size.Y / canvasSize.Y);
        var displaySize = canvasSize * scale;
        return new Rect2((_preview.Size - displaySize) / 2, displaySize);
    }

    private void UpdateReferenceGuides()
    {
        if (_project == null) return;
        var canvas = GetPreviewCanvasRect();
        var halfWidth = ReferenceWidth * canvas.Size.X / _project.CanvasWidth / 2;
        var center = canvas.GetCenter().X;
        // Guide centers stay 720 output pixels apart, including when the canvas is narrower.
        _leftGuide.Position = new Vector2(center - halfWidth - 1, canvas.Position.Y);
        _rightGuide.Position = new Vector2(center + halfWidth - 1, canvas.Position.Y);
        _leftGuide.Size = _rightGuide.Size = new Vector2(2, canvas.Size.Y);
    }

    private void RebuildStage()
    {
        StoreDialogueExport.ConfigureViewport(_viewport, _project);
        UpdateReferenceGuides();
        _stage.Configure(_project, _language);
        _scrub.MaxValue = _stage.Timeline.DurationSeconds;
        Seek();
        if (!_stage.LayoutFits) SetStatus("当前排布超出画布，请减小大小 / 间距 / 数量，或增加画布尺寸。");
    }

    public override void _Process(double delta)
    {
        if (!_playing || _busy) return;
        if (_stage.Timeline.DurationSeconds <= 0) { Stop(); return; }
        _seconds = (_seconds + delta) % _stage.Timeline.DurationSeconds;
        Seek();
    }

    private void Seek()
    {
        _stage.Seek(_seconds);
        _refreshing = true; _scrub.Value = _seconds; _refreshing = false;
        _time.Text = $"{_seconds:0.00} / {_stage.Timeline.DurationSeconds:0.00}s";
    }

    public Image CaptureExportFrame()
    {
        return StoreDialogueExport.CaptureFrame(_viewport, _project);
    }

    private void Stop()
    {
        if (_busy) return;
        _playing = false; _seconds = 0;
        _stage.Seek(0, auditionDogId: "");
        _refreshing = true; _scrub.Value = 0; _refreshing = false;
    }

    public void SaveProject(string path)
    {
        _project.Save(path);
        _projectPath = Path.GetFullPath(path); _dirty = false; SavePreferences();
        SetStatus("已保存工具专用方案：" + _projectPath);
    }

    private void ChooseFile(FileDialog.FileModeEnum mode, string[] filters, Action<string> action)
    {
        if (_busy) return;
        _fileAction = action; _fileDialog.FileMode = mode; _fileDialog.Filters = filters;
        _fileDialog.CurrentDir = Path.GetDirectoryName(_projectPath);
        _fileDialog.CurrentFile = mode == FileDialog.FileModeEnum.SaveFile ? Path.GetFileName(_projectPath) : "";
        _fileDialog.PopupCenteredRatio(.8f);
    }

    private void InvokeFileAction(string path)
    {
        var action = _fileAction; _fileAction = null;
        TryAction(() => action?.Invoke(path));
    }
    private void TryAction(Action action) { try { action(); } catch (Exception e) { SetStatus(e.Message); } }
    private void SetStatus(string message)
    {
        if (IsInsideTree() && GodotObject.IsInstanceValid(_status)) _status.Text = message;
    }
    private void GuardUnsaved(Action action)
    {
        if (_busy) { SetStatus("请先取消导出或等待完成。"); return; }
        if (!_dirty) { action(); return; }
        _confirmedAction = action; _confirm.PopupCentered();
    }

    private void LoadPreferences()
    {
        try
        {
            if (FileAccess.Exists(_preferencesPath))
            {
                var settings = JsonSerializer.Deserialize<Dictionary<string, string>>(FileAccess.ReadAllText(_preferencesPath));
                _ffmpeg.Text = settings.GetValueOrDefault("ffmpeg", "");
                _exportFolder.Text = settings.GetValueOrDefault("output", _exportFolder.Text);
                _projectPath = settings.GetValueOrDefault("project", _projectPath);
            }
            if (string.IsNullOrEmpty(_ffmpeg.Text)) _ffmpeg.Text = StoreDialogueExport.DiscoverFfmpegPath();
        }
        catch { /* Machine-local encoder settings are optional. */ }
    }
    private void SavePreferences()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_preferencesPath));
        FileAccess.WriteAllText(_preferencesPath, JsonSerializer.Serialize(new Dictionary<string, string>
        {
            ["ffmpeg"] = _ffmpeg.Text, ["output"] = _exportFolder.Text, ["project"] = _projectPath
        }));
    }

    private async void StartExport(bool allLanguages)
    {
        if (_busy) return;
        var originalLanguage = _language;
        var outputs = new List<string>();
        try
        {
            Stop();
            var languages = allLanguages ? _project.Locales.Keys.ToArray() : new[] { _language };
            // Validate every requested language before producing any files.
            foreach (var code in languages)
            {
                var errors = _project.Validate(code, true).ToList();
                errors.AddRange(ValidateAssets(_project));
                if (errors.Count > 0) throw new InvalidOperationException(code + "：\n" + string.Join("\n", errors));
                _stage.Configure(_project, code);
                errors.AddRange(_stage.ValidatePresentation());
                if (_stage.Timeline.DurationSeconds > 12) errors.Add("Steam 商店页动画最长 12 秒，请缩短对白停留或间隔。");
                if (errors.Count > 0) throw new InvalidOperationException(code + "：\n" + string.Join("\n", errors));
            }
            var folder = _exportFolder.Text.Trim();
            if (!Path.IsPathFullyQualified(folder)) throw new InvalidOperationException("请选择完整的输出文件夹路径。");
            var format = (StoreDialogueExportFormat)_format.GetSelectedId();
            var encoder = format == StoreDialogueExportFormat.PngSequence ? "" : StoreDialogueExport.DiscoverFfmpegPath(_ffmpeg.Text);
            SavePreferences();
            _busy = true; _exportCancellation = new CancellationTokenSource(); SetBusy(true);
            var snapshot = _project.Clone();
            foreach (var code in languages)
            {
                var suffix = format switch { StoreDialogueExportFormat.WebM => ".webm", StoreDialogueExportFormat.WebP => ".webp", _ => "-frames" };
                var safeCode = Regex.Replace(code, @"[^a-zA-Z0-9_-]", "_");
                var target = Path.Combine(folder, "dialogue-" + safeCode + "-" + DateTime.Now.ToString("yyyyMMdd-HHmmss-fff") + suffix);
                _stage.Configure(snapshot, code);
                var count = _stage.Timeline.GetFrameCount(snapshot.Fps);
                var options = new StoreDialogueExportOptions
                {
                    OutputPath = target, Format = format, Width = snapshot.CanvasWidth, Height = snapshot.CanvasHeight,
                    FramesPerSecond = snapshot.Fps, FrameCount = count, FfmpegPath = encoder,
                    InputAlpha = StoreDialogueAlphaMode.Premultiplied
                };
                await StoreDialogueExport.ExportAsync(options, async (frame, token) =>
                {
                    token.ThrowIfCancellationRequested();
                    _stage.Seek((double)frame / snapshot.Fps);
                    // Render two process frames: the first updates layout, the second completes the draw.
                    await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                    await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                    token.ThrowIfCancellationRequested();
                    return CaptureExportFrame();
                }, new InlineProgress(value =>
                {
                    if (!IsInsideTree()) return;
                    _progress.Value = 100.0 * value.CompletedFrames / value.TotalFrames;
                    SetStatus($"{code} · {value.Stage} {value.CompletedFrames}/{value.TotalFrames}");
                }), _exportCancellation.Token);
                outputs.Add(target);
            }
            SetStatus("导出完成：\n" + string.Join("\n", outputs));
        }
        catch (OperationCanceledException) { SetStatus("导出已取消。" + (outputs.Count > 0 ? "\n已完成：" + string.Join("\n", outputs) : "")); }
        catch (Exception e) { SetStatus("导出未完成：" + e.Message + (outputs.Count > 0 ? "\n已完成：" + string.Join("\n", outputs) : "")); }
        finally
        {
            _exportCancellation?.Dispose(); _exportCancellation = null;
            _busy = false;
            if (IsInsideTree())
            {
                SetBusy(false);
                _stage.Configure(_project, originalLanguage);
                _seconds = 0; Seek();
            }
        }
    }

    private sealed class InlineProgress : IProgress<StoreDialogueExportProgress>
    {
        private readonly Action<StoreDialogueExportProgress> _action;
        public InlineProgress(Action<StoreDialogueExportProgress> action) => _action = action;
        public void Report(StoreDialogueExportProgress value) => _action(value);
    }

    private void SetBusy(bool busy)
    {
        foreach (var node in _layout.FindChildren("*", "", true, false))
        {
            if (node is BaseButton button) button.Disabled = busy;
            if (node is LineEdit input) input.Editable = !busy;
            if (node is TextEdit text) text.Editable = !busy;
            if (node is SpinBox spin) spin.Editable = !busy;
        }
        _cancelExport.Disabled = !busy;
        _scrub.Editable = !busy;
        _dogList.MouseFilter = busy ? MouseFilterEnum.Ignore : MouseFilterEnum.Stop;
        _lines.MouseFilter = busy ? MouseFilterEnum.Ignore : MouseFilterEnum.Stop;
        if (!busy) RefreshLineEditor();
    }

    private void OnCloseRequested() => GuardUnsaved(() => GetTree().Quit());
    public override void _ExitTree()
    {
        _exportCancellation?.Cancel();
        GetWindow().CloseRequested -= OnCloseRequested;
        GetTree().AutoAcceptQuit = _previousAutoQuit;
    }

    private static StoreDialogueDog CreateDog(int index)
    {
        var skins = LubanData.Tables.TbDogSkin.DataList.OrderBy(s => s.Id).ToArray();
        var skin = skins[index % skins.Length];
        var hats = LubanData.Tables.TbItem.DataList.Where(i => i.ItemType == EItemType.Headwear).OrderBy(i => i.Id).ToArray();
        return new StoreDialogueDog
        {
            Appearance = DogSkinDraft.FromDogSkin(skin),
            HeadwearItemId = hats.Length > 0 ? hats[index % hats.Length].Id : 0,
            Speaking = new StoreDialoguePose { ReactionId = (int)EDogReactionTrigger.Excited }
        };
    }

    internal static IEnumerable<string> ValidateAssets(StoreDialogueProject project)
    {
        var assets = new DogSkinAssetCatalog();
        foreach (var dog in project.Dogs)
        {
            var skin = dog.Appearance;
            foreach (var pose in new[] { dog.Idle, dog.Speaking })
                if (pose.ReactionId is int id && LubanData.Tables.TbDogReaction.GetOrDefault(id) == null)
                    yield return "表情动作不存在：" + id;
            foreach (var file in new[] { skin.Head, skin.DefaultEyes, skin.DefaultEars, skin.ClawLeftBack, skin.TongueRegular }
                .Concat(new[] { skin.DefaultNose, skin.DefaultMouse, dog.Idle.Eyes, dog.Idle.Ears, dog.Speaking.Eyes, dog.Speaking.Ears }
                .Where(value => !string.IsNullOrWhiteSpace(value))))
                if (!assets.ResourceExists(skin.FolderPath, file))
                    yield return $"小狗素材不存在：{skin.FolderPath}/{file}";
            if (!assets.EyewearExists(skin.FixedEyewear)) yield return "眼镜素材不存在：" + skin.FixedEyewear;
            if (dog.HeadwearItemId != 0)
            {
                var item = LubanData.Tables.TbItem.GetOrDefault(dog.HeadwearItemId);
                if (item == null || item.ItemType != EItemType.Headwear || item.AssetPathList.Count == 0)
                    yield return "帽子不存在：" + dog.HeadwearItemId;
                else if (!ResourceLoader.Exists(PlayerInventory.ToResPath(item.AssetPathList[0])))
                    yield return "帽子素材不存在：" + item.AssetPathList[0];
            }
        }
    }

    public static StoreDialogueProject CreateExample()
    {
        var project = new StoreDialogueProject();
        project.Dogs.Add(CreateReferenceDog(1011, 2030));
        project.Dogs.Add(CreateReferenceDog(1011, 2030));
        project.Dogs.Add(CreateReferenceDog(1013, 2005));
        var artist = CreateReferenceDog(1032, 2017);
        artist.Idle = new StoreDialoguePose { ReactionId = (int)EDogReactionTrigger.Bored };
        project.Dogs.Add(artist);
        // Keep the existing programmer placeholder until its appearance is specified.
        project.Dogs.Add(CreateDog(3));
        var chinese = new StoreDialogueLocale();
        var english = new StoreDialogueLocale();
        var names = new[] { "玩家1", "玩家2", "策划", "美术", "程序" };
        var englishNames = new[] { "Player 1", "Player 2", "Designer", "Artist", "Programmer" };
        for (var i = 0; i < project.Dogs.Count; i++)
        {
            chinese.Names[project.Dogs[i].Id] = names[i];
            english.Names[project.Dogs[i].Id] = englishNames[i];
        }
        var text = new[] { "什么时候能推出新打牌模式呀？", "我们正在加班赶工啦", "努力！", "奋斗！" };
        var en = new[] { "When will the new card game mode be ready?", "We're working overtime on it!", "Giving it our all!", "Working hard!" };
        var durations = new[] { 3.0, 3.0, 1.5, 1.5 };
        var speakerIndices = new[] { 0, 2, 3, 4 };
        for (var i = 0; i < text.Length; i++)
        {
            chinese.Lines.Add(new StoreDialogueLine { DogId = project.Dogs[speakerIndices[i]].Id, Text = text[i], DurationSeconds = durations[i] });
            english.Lines.Add(new StoreDialogueLine { DogId = project.Dogs[speakerIndices[i]].Id, Text = en[i], DurationSeconds = durations[i] });
        }
        project.Locales["zh-CN"] = chinese; project.Locales["en"] = english;
        return project;
    }

    private static StoreDialogueDog CreateReferenceDog(int skinId, int headwearId)
    {
        var skin = LubanData.Tables.TbDogSkin.GetOrDefault(skinId)
            ?? throw new InvalidOperationException($"参考造型缺少 DogSkin {skinId}。");
        return new StoreDialogueDog
        {
            Appearance = DogSkinDraft.FromDogSkin(skin),
            HeadwearItemId = headwearId,
            Speaking = new StoreDialoguePose { ReactionId = (int)EDogReactionTrigger.Excited }
        };
    }
}
