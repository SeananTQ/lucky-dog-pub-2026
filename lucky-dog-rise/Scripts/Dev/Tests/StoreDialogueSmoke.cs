using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Threading.Tasks;
using DataTables;
using Godot;
using LuckyDogRise.Tools;

namespace LuckyDogRise;

/// <summary>
/// Runtime checks for authoring isolation and deterministic dialogue presentation.
/// Pass --dialogue-render-smoke with a rendering driver and FFMPEG_PATH to also
/// inspect RGBA pixels and export the actual five-dog example in WebM and WebP.
/// </summary>
public partial class StoreDialogueSmoke : Node
{
    [Export] private SubViewport _viewport = null!;
    [Export] private StoreDialogueStage _stage = null!;
    [Export] private PackedScene _appearanceEditorScene = null!;
    [Export] private PackedScene _controllerScene = null!;
    [Export] private ColorRect _alphaProbe = null!;

    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private const string CatalogPath = "res://Tools/DogSkinEditor/EditorFiles/output/DogSkinCatalogDraft.json";

    public override async void _Ready()
    {
        try
        {
            var renderRequested = OS.GetCmdlineUserArgs().Contains("--dialogue-render-smoke");
            var headless = DisplayServer.GetName() == "headless";
            Check(!renderRequested || !headless,
                "--dialogue-render-smoke requires a rendering driver; headless only verifies scene state");

            var catalogBefore = FileFingerprint(ProjectSettings.GlobalizePath(CatalogPath));
            var project = CreateProject();
            var formalSkin = LubanData.Tables.TbDogSkin.GetOrDefault(project.Dogs[0].Appearance.Id);
            var originalHead = formalSkin.Head;
            var originalEyes = formalSkin.DefaultEyes;
            Check(project.Validate("zh-CN", forExport: true).Count == 0, "smoke project is invalid");
            _viewport.Size = new Vector2I(project.CanvasWidth, project.CanvasHeight);
            _stage.Configure(project, "zh-CN");
            await NextFrame();
            Check(_stage.Dogs.Count == 3, "three independent dogs should be instantiated");
            Check(_stage.LayoutFits, "smoke layout must fit inside its render canvas");
            Check(_stage.ValidatePresentation().Count == 0, "smoke names and bubbles must fit");

            CheckIdle(project);
            var firstSpeakingTime = project.LeadInSeconds + 0.1;
            _stage.Seek(firstSpeakingTime);
            CheckSpeaking(project, 0);
            Check(GetField<Label>(_stage, "_bubbleText").Text == "什么时候能推出新打牌模式呀？", "Chinese dialogue was not displayed");

            var secondSpeakingTime = project.LeadInSeconds + project.Locales["zh-CN"].Lines[0].DurationSeconds
                + project.LineGapSeconds + 0.1;
            _stage.Seek(secondSpeakingTime);
            CheckSpeaking(project, 1);
            CheckPose(_stage.Dogs[0], project.Dogs[0], speaking: false);
            _stage.Seek(_stage.Timeline.DurationSeconds);
            CheckIdle(project, seek: false);
            _stage.Seek(firstSpeakingTime + _stage.Timeline.DurationSeconds, loop: true);
            CheckSpeaking(project, 0);

            _stage.Configure(project, "en");
            _stage.Seek(firstSpeakingTime);
            Check(GetField<Label>(_stage, "_bubbleText").Text == "When is the new card mode coming?", "English dialogue was not displayed");
            foreach (var view in _stage.Dogs)
            {
                var label = GetField<Label>(view, "_name");
                Check(label.Text == project.Locales["en"].Names[view.DogId], "tool names must switch with the selected locale");
                Check(label.AutoTranslateMode == Node.AutoTranslateModeEnum.Disabled, "tool names must bypass game translation lookup");
            }
            Check(GetField<Label>(_stage, "_bubbleText").AutoTranslateMode == Node.AutoTranslateModeEnum.Disabled,
                "tool dialogue must bypass game translation lookup");

            await CheckBubbleLayout(project);
            await CheckNameBarAlignment(project);

            CheckJsonIsolation(project);
            await CheckAppearanceEditorIsolation(project.Dogs[0].Appearance);
            Check(formalSkin.Head == originalHead && formalSkin.DefaultEyes == originalEyes,
                "authoring edits must not mutate formal DogSkin data");
            Check(FileFingerprint(ProjectSettings.GlobalizePath(CatalogPath)) == catalogBefore,
                "authoring edits must not save or rewrite the skin catalog");

            if (renderRequested)
            {
                _stage.Configure(project, "zh-CN");
                _stage.Seek(firstSpeakingTime);
                await CheckRenderedFrame(project);
                GD.Print("StoreDialogueSmoke RENDER PASS: transparent RGBA canvas, dog, name bar, and bubble pixels verified.");
            }
            else
            {
                GD.Print("StoreDialogueSmoke: pixel rendering not requested; no visual verification claimed.");
            }

            await CheckMainController(renderRequested && !OS.GetCmdlineUserArgs().Contains("--dialogue-no-export"));
            if (OS.GetCmdlineUserArgs().Contains("--dialogue-check-saved"))
                await CheckSavedTranslations(renderRequested);

            GD.Print("StoreDialogueSmoke PASS: idle/speaking, fixed back paws, full chin, locale text, JSON and appearance edit isolation.");
            GetTree().Quit(0);
        }
        catch (Exception exception)
        {
            GD.PushError("StoreDialogueSmoke FAIL: " + exception);
            GetTree().Quit(1);
        }
    }

    private static StoreDialogueProject CreateProject()
    {
        var project = new StoreDialogueProject
        {
            CanvasWidth = 1170,
            CanvasHeight = 430,
            DogScale = 0.25,
            GapPixels = 20,
            LeadInSeconds = 0.25,
            LineGapSeconds = 0.125,
            TailSeconds = 0.25,
        };
        foreach (var skin in LubanData.Tables.TbDogSkin.DataList.OrderBy(skin => skin.Id).Take(3))
        {
            var appearance = DogSkinDraft.FromDogSkin(skin);
            project.Dogs.Add(new StoreDialogueDog
            {
                Id = "smoke-" + skin.Id,
                Appearance = appearance,
                Idle = new StoreDialoguePose(),
                Speaking = new StoreDialoguePose { Eyes = appearance.EyesSignaling, Ears = appearance.EarsDrooped },
            });
        }
        Check(project.Dogs.Count == 3, "formal catalog requires three dog skins for the smoke fixture");
        var chinese = new StoreDialogueLocale();
        var english = new StoreDialogueLocale();
        var chineseNames = new[] { "玩家", "策划", "程序员" };
        var englishNames = new[] { "Player", "Designer", "Programmer" };
        var chineseLines = new[] { "什么时候能推出新打牌模式呀？", "我们正在加班赶工啦", "奋斗！" };
        var englishLines = new[] { "When is the new card mode coming?", "We're working hard on it!", "Let's go!" };
        for (var index = 0; index < project.Dogs.Count; index++)
        {
            var id = project.Dogs[index].Id;
            chinese.Names[id] = chineseNames[index];
            english.Names[id] = englishNames[index];
            chinese.Lines.Add(new StoreDialogueLine { DogId = id, Text = chineseLines[index], DurationSeconds = 0.75 });
            english.Lines.Add(new StoreDialogueLine { DogId = id, Text = englishLines[index], DurationSeconds = 0.75 });
        }
        project.Locales["zh-CN"] = chinese;
        project.Locales["en"] = english;
        return project;
    }

    private void CheckIdle(StoreDialogueProject project, bool seek = true)
    {
        if (seek) _stage.Seek(0);
        Check(_stage.CurrentSpeaker == "" && !_stage.Dogs.Any(dog => dog.IsSpeaking), "idle must have no speaker");
        Check(!GetField<PanelContainer>(_stage, "_bubble").Visible, "idle must hide the dialogue bubble");
        for (var index = 0; index < _stage.Dogs.Count; index++) CheckPose(_stage.Dogs[index], project.Dogs[index], speaking: false);
    }

    private async Task CheckBubbleLayout(StoreDialogueProject source)
    {
        var project = source.Clone();
        project.CanvasWidth = 720;
        project.CanvasHeight = 600;
        project.BubbleWidth = 380;
        var french = new StoreDialogueLocale();
        foreach (var dog in project.Dogs) french.Names[dog.Id] = dog.Id;
        const string longText = "Quand pourrons-nous découvrir le nouveau mode de jeu de cartes ? Nous avons vraiment hâte !";
        french.Lines.Add(new StoreDialogueLine { DogId = project.Dogs[0].Id, Text = "Oui !", DurationSeconds = 1 });
        french.Lines.Add(new StoreDialogueLine { DogId = project.Dogs[0].Id, Text = longText, DurationSeconds = 1 });
        french.Lines.Add(new StoreDialogueLine { DogId = project.Dogs[^1].Id, Text = longText, DurationSeconds = 1 });
        french.Lines.Add(new StoreDialogueLine { DogId = project.Dogs[0].Id, Text = new string('W', 60), DurationSeconds = 1 });
        project.Locales["fr"] = french;
        _stage.Configure(project, "fr");
        var bubble = GetField<PanelContainer>(_stage, "_bubble");
        var label = GetField<Label>(_stage, "_bubbleText");
        var tail = GetField<Polygon2D>(_stage, "_tail");
        var first = project.LeadInSeconds + 0.1;
        _stage.Seek(first);
        var shortSize = bubble.Size;
        Check(shortSize.X < project.BubbleWidth && label.GetLineCount() == 1,
            "short dialogue must use a compact, single-line bubble");

        for (var index = 1; index <= 3; index++)
        {
            _stage.Seek(first + index * (1 + project.LineGapSeconds));
            Check(_stage.BubbleFits && bubble.Size.X <= project.BubbleWidth && bubble.Size.Y > shortSize.Y,
                "long translated dialogue must grow vertically within the configured maximum width");
            Check(label.GetLineCount() > 1 && label.GetVisibleLineCount() == label.GetLineCount(),
                "French text and unbroken words must wrap without hiding lines");
            var speaker = index == 2 ? _stage.Dogs[^1] : _stage.Dogs[0];
            var tip = bubble.Position + tail.Position + tail.Polygon[2];
            Check(Mathf.IsEqualApprox(tip.X, speaker.Position.X)
                && Mathf.IsEqualApprox(tip.Y, speaker.Position.Y - 750 * (float)project.DogScale),
                "the tail tip must remain over the speaking dog after clamping the bubble");
            Check(index == 2 ? tail.Polygon[2].X > 0 : tail.Polygon[2].X < 0,
                "left/right edge bubbles must angle their tail toward the speaker");
            Check(bubble.Position.X >= 12 && bubble.Position.X + bubble.Size.X <= project.CanvasWidth - 12,
                "edge bubbles must stay within the output canvas");
        }

        _stage.Seek(first);
        Check(bubble.Size.IsEqualApprox(shortSize), "seeking from long text back to short text must shrink immediately");
        await NextFrame();
        Check(bubble.Size.IsEqualApprox(shortSize), "deferred container layout must preserve deterministic bubble bounds");
        _stage.Configure(source, "en");
        GD.Print("StoreDialogueSmoke BUBBLE PASS: adaptive width, French/long-word wrapping, angled tails, deterministic seeking.");
    }

    private async Task CheckNameBarAlignment(StoreDialogueProject source)
    {
        var project = source.Clone();
        project.CanvasWidth = 720;
        project.DogScale = 0.14;
        project.NameFontSize = 18;
        var names = new[] { "美女玩家", "打喷嚏的策划", "没洗头的美术" };
        for (var i = 0; i < project.Dogs.Count; i++)
            project.Locales["zh-CN"].Names[project.Dogs[i].Id] = names[i];
        _stage.Configure(project, "zh-CN");
        await NextFrame();
        var expectedSize = GetField<PanelContainer>(_stage.Dogs[0], "_bar").Size;
        var expectedWidth = expectedSize.X;
        var sharedFontSize = GetField<Label>(_stage.Dogs[0], "_name").GetThemeFontSize("font_size");
        foreach (var dog in _stage.Dogs)
        {
            var bar = GetField<PanelContainer>(dog, "_bar");
            var label = GetField<Label>(dog, "_name");
            Check(bar.Size.IsEqualApprox(expectedSize) && label.GetThemeFontSize("font_size") == sharedFontSize,
                "four- and six-character names must share identical bar width, height, and font size");
            Check(Mathf.IsEqualApprox(bar.Size.X, expectedWidth) && Mathf.IsZeroApprox(bar.Position.X + bar.Size.X / 2),
                "long names must not expand the name bar or shift its center away from the dog");
            Check(Mathf.IsEqualApprox(label.Position.X + label.Size.X / 2, bar.Size.X / 2)
                && label.HorizontalAlignment == HorizontalAlignment.Center && dog.NameFits,
                "localized names must fit and stay centered inside the name bar");
        }
        Check(GetField<Label>(_stage.Dogs[1], "_name").GetThemeFontSize("font_size") < project.NameFontSize,
            "six-character names must use a smaller font when the dog is scaled down");

        var reused = _stage.Dogs[0];
        reused.Configure(project.Dogs[0], new string('W', 200), (float)project.DogScale, project.NameFontSize);
        await NextFrame();
        var reusedBar = GetField<PanelContainer>(reused, "_bar");
        Check(!reused.NameFits && Mathf.IsEqualApprox(reusedBar.Size.X, expectedWidth)
            && Mathf.IsZeroApprox(reusedBar.Position.X + reusedBar.Size.X / 2),
            "an unfit name must stay within the fixed centered bar and remain invalid for export");
        reused.Configure(project.Dogs[0], "玩家", (float)project.DogScale, project.NameFontSize);
        await NextFrame();
        Check(reused.NameFits && Mathf.IsEqualApprox(reusedBar.Size.X, expectedWidth),
            "shortening a name must restore its normal sizing without stale minimum widths");

        foreach (var dog in project.Dogs) project.Locales["zh-CN"].Names[dog.Id] = "玩家";
        _stage.Configure(project, "zh-CN");
        await NextFrame();
        Check(_stage.Dogs.All(dog => dog.NameFontSize == project.NameFontSize
            && dog.NameBarSize.IsEqualApprox(_stage.Dogs[0].NameBarSize)),
            "shortening the longest name must restore the configured font size uniformly across the row");

        project.DogScale = 0.11;
        _stage.Configure(project, "en");
        await NextFrame();
        Check(_stage.Dogs.All(dog => dog.NameFontSize == _stage.Dogs[0].NameFontSize
            && dog.NameBarSize.IsEqualApprox(_stage.Dogs[0].NameBarSize) && dog.NameFits),
            "changing language and dog scale must recompute one shared name-bar layout");
        _stage.Configure(source, "en");
        GD.Print("StoreDialogueSmoke NAME BAR PASS: shared width/height/font size, centering, language/scale and name edits.");
    }

    private void CheckSpeaking(StoreDialogueProject project, int index)
    {
        Check(_stage.CurrentSpeaker == project.Dogs[index].Id, "timeline picked the wrong speaker");
        Check(_stage.Dogs.Count(dog => dog.IsSpeaking) == 1 && _stage.Dogs[index].IsSpeaking, "exactly the current dog should speak");
        Check(GetField<PanelContainer>(_stage, "_bubble").Visible && _stage.BubbleFits, "speaker bubble should be visible and fit");
        for (var dogIndex = 0; dogIndex < _stage.Dogs.Count; dogIndex++)
            CheckPose(_stage.Dogs[dogIndex], project.Dogs[dogIndex], dogIndex == index);
    }

    private static void CheckPose(StoreDialogueDogView view, StoreDialogueDog data, bool speaking)
    {
        var visual = view.Visual;
        var pose = speaking ? data.Speaking : data.Idle;
        var eyes = string.IsNullOrWhiteSpace(pose.Eyes) ? data.Appearance.DefaultEyes : pose.Eyes;
        var ears = string.IsNullOrWhiteSpace(pose.Ears) ? data.Appearance.DefaultEars : pose.Ears;
        CheckTexture(visual.GetNode<Sprite2D>("HeadRoot/Eyes"), eyes, "eyes");
        CheckTexture(visual.GetNode<Sprite2D>("HeadRoot/Ears"), ears, "ears");
        var head = visual.GetNode<Sprite2D>("HeadRoot/Head");
        Check(!GetField<bool>(visual, "_headClippingEnabled")
            && !((ShaderMaterial)head.Material).GetShaderParameter("clip_enabled").AsBool(), "dialogue dog must retain its full chin");
        Check(!visual.IsProcessing(), "timeline dog must not run autonomous gameplay animation");
        foreach (var name in new[] { "ClawLeft", "ClawRight" })
        {
            var claw = visual.GetNode<Node2D>(name);
            Check(claw.Visible && claw.GetNode<Sprite2D>("Claw_Back_Left").Visible
                && !claw.GetNode<Sprite2D>("Claw_Palm_Left").Visible, "both paws must remain in the fixed back pose");
        }
    }

    private static void CheckTexture(Sprite2D sprite, string expectedFile, string part)
    {
        Check(sprite.Texture != null && sprite.Texture.ResourcePath.Replace('\\', '/').EndsWith('/' + expectedFile, StringComparison.OrdinalIgnoreCase),
            $"{part} texture did not switch to {expectedFile}");
    }

    private async Task CheckAppearanceEditorIsolation(DogSkinDraft original)
    {
        var originalEyes = original.DefaultEyes;
        var window = GetWindow();
        var windowTitle = window.Title;
        var windowSize = window.Size;
        var windowTransparent = window.TransparentBg;
        var canceled = false;
        var cancelEditor = _appearanceEditorScene.Instantiate<DogSkinEditorController>();
        cancelEditor.BeginEdit(original, _ => throw new InvalidOperationException("cancel must not apply"), () => canceled = true);
        AddChild(cancelEditor);
        await NextFrame();
        Check(GetField<DogSkinCatalogDraft>(cancelEditor, "_catalog").DogSkins.Count == 0, "embedded editor must not load the skin catalog");
        Check(window.Title == windowTitle && window.Size == windowSize && window.TransparentBg == windowTransparent,
            "embedded editor must not reconfigure its host window");
        GetField<DogSkinDraft>(cancelEditor, "_editingDraft").DefaultEyes = "cancel-only.png";
        cancelEditor.CancelAppearanceEdit();
        Check(canceled && original.DefaultEyes == originalEyes, "cancel must discard isolated appearance edits");
        await NextFrame();

        DogSkinDraft applied = null;
        var applyEditor = _appearanceEditorScene.Instantiate<DogSkinEditorController>();
        AddChild(applyEditor);
        applyEditor.BeginEdit(original, value => applied = value, () => throw new InvalidOperationException("apply must not cancel"));
        var working = GetField<DogSkinDraft>(applyEditor, "_editingDraft");
        Check(!ReferenceEquals(working, original), "embedded editor must clone its input");
        working.DefaultEyes = original.EyesHappy;
        applyEditor.ApplyAppearanceEdit();
        Check(applied != null && !ReferenceEquals(applied, original) && !ReferenceEquals(applied, working), "apply must return a fresh isolated draft");
        Check(applied.DefaultEyes == original.EyesHappy && applied.Id == original.Id && original.DefaultEyes == originalEyes,
            "apply must preserve identity and leave the source appearance unchanged");
        working.DefaultEyes = "after-apply.png";
        Check(applied.DefaultEyes == original.EyesHappy, "later editor changes must not mutate the applied result");
        await NextFrame();
    }

    private static void CheckJsonIsolation(StoreDialogueProject project)
    {
        var clone = project.Clone();
        clone.Dogs[0].Appearance.Head = "clone-only.png";
        clone.Dogs[0].Speaking.Eyes = "clone-only.png";
        clone.Locales["zh-CN"].Names[clone.Dogs[0].Id] = "仅限副本";
        Check(project.Dogs[0].Appearance.Head != clone.Dogs[0].Appearance.Head
            && project.Dogs[0].Speaking.Eyes != clone.Dogs[0].Speaking.Eyes
            && project.Locales["zh-CN"].Names[project.Dogs[0].Id] == "玩家", "project clone must isolate nested appearance, pose and locale data");

        var output = SmokeOutputDirectory();
        Directory.CreateDirectory(output);
        var path = Path.Combine(output, "smoke-project.json");
        project.Save(path);
        var loaded = StoreDialogueProject.Load(path);
        Check(loaded.Dogs.Count == project.Dogs.Count && loaded.Locales["zh-CN"].Names[project.Dogs[0].Id] == "玩家"
            && loaded.Locales["en"].Lines[0].Text == project.Locales["en"].Lines[0].Text, "JSON roundtrip lost names or translated dialogue");
        var csvPath = Path.Combine(output, "smoke-project.csv");
        var csvRejected = false;
        try { project.Save(csvPath); }
        catch (ArgumentException) { csvRejected = true; }
        Check(csvRejected && !File.Exists(csvPath), "authoring project must reject CSV output");
    }

    private async Task CheckRenderedFrame(StoreDialogueProject project)
    {
        _alphaProbe.Show();
        await NextFrame();
        await NextFrame();
        using (var probe = _viewport.GetTexture().GetImage())
        {
            var pixel = probe.GetPixel(12, 12);
            Check(Math.Abs(pixel.A - 0.5) < 0.03 && Math.Abs(pixel.R - 0.5) < 0.03
                && Math.Abs(pixel.G - 0.5) < 0.03 && Math.Abs(pixel.B - 0.5) < 0.03,
                $"transparent viewport must produce premultiplied RGBA for export conversion; probe={pixel}");
            GD.Print("StoreDialogueSmoke alpha probe (expected premultiplied white): " + pixel);
        }
        _alphaProbe.Hide();
        // Let layout and the render thread observe the same fixed timeline frame.
        await NextFrame();
        await NextFrame();
        using var image = _viewport.GetTexture().GetImage();
        Check(image != null && !image.IsEmpty() && image.GetWidth() == project.CanvasWidth
            && image.GetHeight() == project.CanvasHeight, "render viewport returned no correctly sized image");
        Check(image.GetPixel(0, 0).A == 0 && image.GetPixel(image.GetWidth() - 1, 0).A == 0
            && image.GetPixel(0, image.GetHeight() - 1).A == 0, "empty viewport corners must preserve zero alpha");
        var speaker = _stage.Dogs[0];
        var scale = (float)project.DogScale;
        var dogArea = new Rect2(speaker.Position + new Vector2(-400 * scale, -650 * scale), new Vector2(800 * scale, 600 * scale));
        var nameBar = GetField<PanelContainer>(speaker, "_bar");
        var bubble = GetField<PanelContainer>(_stage, "_bubble");
        var nameArea = new Rect2(speaker.Position + nameBar.Position, nameBar.Size);
        var bubbleArea = new Rect2(bubble.Position, bubble.Size);
        Check(CountOpaque(image, dogArea) > 500, "dog region must contain rendered dog pixels");
        Check(CountOpaque(image, nameArea) > 100 && CountOpaque(image, nameArea, darkOnly: true) > 10,
            "name region must contain the bar and dark text pixels");
        Check(CountOpaque(image, bubbleArea) > 100 && CountOpaque(image, bubbleArea, expectedColor: Color.FromHtml("683a32")) > 10,
            "bubble region must contain the bubble and requested #683A32 text pixels");
        var path = Path.Combine(SmokeOutputDirectory(), "dialogue-transparent.png");
        Check(image.SavePng(path) == Error.Ok, "failed to save transparent smoke image");
        GD.Print("StoreDialogueSmoke render artifact: " + path);
    }

    private async Task CheckMainController(bool exportRequested)
    {
        var preferencesPath = Path.GetFullPath(ProjectSettings.GlobalizePath("res://../.local-build/store-dialogue/preferences.json"));
        var existingPreferences = File.Exists(preferencesPath) ? File.ReadAllBytes(preferencesPath) : null;
        StoreDialogueController controller = null;
        try
        {
            controller = _controllerScene.Instantiate<StoreDialogueController>();
            AddChild(controller);
            var example = StoreDialogueController.CreateExample();
            controller.ApplyProject(example, "zh-CN");
            await NextFrame();
            Check(controller.Project.Dogs.Count == 5 && controller.Stage.Dogs.Count == 5, "main example must initialize five dogs");
            Check(example.Dogs.Select(dog => example.Locales["zh-CN"].Names[dog.Id])
                .SequenceEqual(new[] { "玩家1", "玩家2", "策划", "美术", "程序" }), "reference cast order must match the requested roles");
            Check(example.Dogs.Select(dog => dog.Appearance.Id).SequenceEqual(new[] { 1011, 1011, 1013, 1032, 1004 })
                && example.Dogs.Select(dog => dog.HeadwearItemId).SequenceEqual(new[] { 2030, 2030, 2005, 2017, 2004 }),
                "reference skins and hats must match, with the programmer left as a placeholder");
            Check(!ReferenceEquals(example.Dogs[0].Appearance, example.Dogs[1].Appearance)
                && example.Dogs[0].Id != example.Dogs[1].Id, "the two players must remain independently editable");
            Check(example.Locales.Values.All(value => value.Lines.Select(line => line.DogId)
                .SequenceEqual(new[] { 0, 2, 3, 4 }.Select(index => example.Dogs[index].Id))),
                "dialogue must follow role identities after reordering");
            Check(!ReferenceEquals(controller.Project, example)
                && !ReferenceEquals(controller.Project.Dogs[0].Appearance, example.Dogs[0].Appearance), "main controller must copy an opened project");
            Check(controller.Stage.ValidatePresentation().Count == 0, "main five-dog example must fit the canvas");
            Check(controller.Stage.Dogs.All(dog => dog.Position.Y + dog.NameBarBottom <= example.CanvasHeight - 10),
                "localized name bars must retain bottom padding without clipping");
            var captureViewport = GetField<SubViewport>(controller, "_viewport");
            var logicalSize = new Vector2I(example.CanvasWidth, example.CanvasHeight);
            Check(captureViewport.Size == logicalSize * 2 && captureViewport.Size2DOverride == logicalSize
                && captureViewport.Size2DOverrideStretch && captureViewport.Oversampling,
                "small canvases must use high resolution rendering with logical layout and font oversampling");
            if (DisplayServer.GetName() != "headless")
            {
                await NextFrame();
                using var capture = controller.CaptureExportFrame();
                Check(capture.GetSize() == logicalSize, "supersampling must not change export dimensions");
                Check(capture.GetPixel(0, 0).A == 0 && capture.GetPixel(logicalSize.X - 1, logicalSize.Y - 1).A == 0,
                    "downsampling must retain the transparent canvas");
                foreach (var view in controller.Stage.Dogs)
                {
                    var bar = GetField<PanelContainer>(view, "_bar");
                    Check(CountOpaque(capture, new Rect2(view.Position + bar.Position, bar.Size), darkOnly: true) > 10,
                        "supersampled localized name bars must retain visible lettering at export resolution");
                }
                Check(capture.SavePng(Path.Combine(SmokeOutputDirectory(), "reference-cast-premultiplied.png")) == Error.Ok,
                    "reference render must be saved for inspection");
                GD.Print("StoreDialogueSmoke CAPTURE PASS: 2x render, original output dimensions, alpha and localized name bars.");
            }
            example.Save(Path.Combine(SmokeOutputDirectory(), "reference-cast.json"));

            var firstId = controller.Project.Dogs[0].Id;
            var name = GetField<LineEdit>(controller, "_name");
            name.Text = "冒烟测试玩家";
            name.EmitSignal(LineEdit.SignalName.TextChanged, name.Text);
            Check(controller.Project.Locales["zh-CN"].Names[firstId] == name.Text
                && GetField<Label>(controller.Stage.Dogs[0], "_name").Text == name.Text,
                "name TextChanged callback must update project and stage");
            Check(example.Locales["zh-CN"].Names[firstId] == "玩家1", "name callback must not mutate the caller's project");

            var locale = GetField<OptionButton>(controller, "_locale");
            var englishIndex = Enumerable.Range(0, locale.ItemCount).First(index => locale.GetItemText(index) == "en");
            locale.Select(englishIndex);
            locale.EmitSignal(OptionButton.SignalName.ItemSelected, (long)englishIndex);
            Check(name.Text == "Player 1" && GetField<Label>(controller.Stage.Dogs[0], "_name").Text == "Player 1",
                "language selection callback must refresh localized names");
            GetField<Button>(controller, "_speakPreview").EmitSignal(BaseButton.SignalName.Pressed);
            Check(controller.Stage.CurrentSpeaker == firstId && controller.Stage.Dogs[0].IsSpeaking,
                "Speaking preview button must audition the selected dog");
            GetField<Button>(controller, "_idlePreview").EmitSignal(BaseButton.SignalName.Pressed);
            Check(!controller.Stage.Dogs.Any(dog => dog.IsSpeaking), "Idle preview button must restore all dogs");

            var speakingActions = GetField<OptionButton>(controller, "_speakReaction");
            var actionIds = LubanData.Tables.TbDogReaction.DataList.Select(row => (int)row.DogReactionTrigger).Distinct().OrderBy(id => id);
            Check(Enumerable.Range(0, speakingActions.ItemCount).Select(speakingActions.GetItemId).SequenceEqual(actionIds),
                "dialogue actions must use the same formal reaction list as store capture");
            var actionIndex = speakingActions.GetItemIndex((int)EDogReactionTrigger.Hello);
            Check(speakingActions.GetItemText(actionIndex) == "1009  Hello", "actions must show reaction IDs and names, not asset filenames");
            speakingActions.Select(actionIndex);
            speakingActions.EmitSignal(OptionButton.SignalName.ItemSelected, (long)actionIndex);
            GetField<Button>(controller, "_speakPreview").EmitSignal(BaseButton.SignalName.Pressed);
            Check(controller.Project.Dogs[0].Speaking.ReactionId == (int)EDogReactionTrigger.Hello,
                "speaking action selection must persist the reaction ID");
            await CheckExpression(controller.Stage.Dogs[0], EDogReactionTrigger.Hello,
                controller.Project.Dogs[0].Appearance.EyesSignaling, controller.Project.Dogs[0].Appearance.EarsNormal);

            var idleActions = GetField<OptionButton>(controller, "_idleReaction");
            var idleIndex = idleActions.GetItemIndex((int)EDogReactionTrigger.Bored);
            idleActions.Select(idleIndex);
            idleActions.EmitSignal(OptionButton.SignalName.ItemSelected, (long)idleIndex);
            Check(controller.Project.Dogs[0].Idle.ReactionId == (int)EDogReactionTrigger.Bored,
                "idle action selection must be independent from speaking");
            await CheckExpression(controller.Stage.Dogs[0], EDogReactionTrigger.Bored,
                controller.Project.Dogs[0].Appearance.EyesBored, controller.Project.Dogs[0].Appearance.EarsDrooped);

            var actionPath = Path.Combine(SmokeOutputDirectory(), "reaction-project.json");
            controller.Project.Save(actionPath);
            var savedActions = StoreDialogueProject.Load(actionPath);
            Check(savedActions.Dogs[0].Idle.ReactionId == (int)EDogReactionTrigger.Bored
                && savedActions.Dogs[0].Speaking.ReactionId == (int)EDogReactionTrigger.Hello,
                "JSON roundtrip must preserve both chosen actions");

            actionIndex = speakingActions.GetItemIndex((int)EDogReactionTrigger.InteractionHint);
            speakingActions.Select(actionIndex);
            speakingActions.EmitSignal(OptionButton.SignalName.ItemSelected, (long)actionIndex);
            await CheckExpression(controller.Stage.Dogs[0], EDogReactionTrigger.InteractionHint,
                controller.Project.Dogs[0].Appearance.EyesSignaling, controller.Project.Dogs[0].Appearance.EarsNormal);

            // A preview-only entry point must leave normal gameplay reactions intact.
            var visual = controller.Stage.Dogs[0].Visual;
            visual.ApplyReaction(EDogReactionTrigger.Hello);
            Check(visual.IsInteractionHintPlaying, "normal Hello must still animate in gameplay");
            visual.ApplyPreviewExpression(EDogReactionTrigger.Hello);
            Check(!visual.IsInteractionHintPlaying, "tool expression preview must cancel time-driven animation");

            var legacy = example.Clone();
            legacy.Dogs[0].Speaking = new StoreDialoguePose
            {
                Eyes = legacy.Dogs[0].Appearance.EyesBored, Ears = legacy.Dogs[0].Appearance.EarsDrooped
            };
            controller.ApplyProject(legacy, "zh-CN");
            GetField<Button>(controller, "_speakPreview").EmitSignal(BaseButton.SignalName.Pressed);
            Check(speakingActions.GetSelectedId() == 0, "legacy custom poses must remain recognizable until an action is chosen");
            CheckTexture(controller.Stage.Dogs[0].Visual.GetNode<Sprite2D>("HeadRoot/Eyes"),
                legacy.Dogs[0].Appearance.EyesBored, "legacy eyes preservation");
            actionIndex = speakingActions.GetItemIndex((int)EDogReactionTrigger.Excited);
            speakingActions.Select(actionIndex);
            speakingActions.EmitSignal(OptionButton.SignalName.ItemSelected, (long)actionIndex);
            Check(controller.Project.Dogs[0].Speaking.ReactionId == (int)EDogReactionTrigger.Excited
                && controller.Project.Dogs[0].Speaking.Eyes == "" && controller.Project.Dogs[0].Speaking.Ears == ""
                && speakingActions.GetItemIndex(0) == -1,
                "choosing an action must replace legacy overrides and remove the temporary compatibility option");
            await CheckExpression(controller.Stage.Dogs[0], EDogReactionTrigger.Excited,
                legacy.Dogs[0].Appearance.EyesHappy, legacy.Dogs[0].Appearance.EarsNormal);
            GD.Print("StoreDialogueSmoke ACTION PASS: formal selectors, idle/speaking, AssetRef, fixed paws, JSON and legacy compatibility.");

            GetField<SpinBox>(controller, "_count").Value = 4;
            Check(controller.Project.Dogs.Count == 4 && controller.Stage.Dogs.Count == 4,
                "count value callback must rebuild the row");
            GetField<SpinBox>(controller, "_gap").Value = 16;
            Check(controller.Project.GapPixels == 16, "gap value callback must update spacing");
            GetField<SpinBox>(controller, "_count").Value = 5;
            Check(controller.Project.Dogs.Count == 5 && controller.Stage.Dogs.Count == 5,
                "count value callback must add independent dogs");

            var originalEyes = controller.Project.Dogs[0].Appearance.DefaultEyes;
            GetField<Button>(controller, "_editAppearance").EmitSignal(BaseButton.SignalName.Pressed);
            var editor = GetField<DogSkinEditorController>(controller, "_appearanceEditor");
            Check(editor.EmbeddedAppearanceEditor && editor.GetParent() == GetField<Control>(controller, "_overlay"),
                "appearance button must open the isolated shared editor overlay");
            GetField<DogSkinDraft>(editor, "_editingDraft").DefaultEyes = "canceled-main-edit.png";
            editor.CancelAppearanceEdit();
            await NextFrame();
            Check(controller.Project.Dogs[0].Appearance.DefaultEyes == originalEyes,
                "main appearance cancel must not change the selected dog");

            controller.ApplyProject(example, "zh-CN");
            GetField<HSlider>(controller, "_scrub").Value = example.LeadInSeconds + 0.25;
            Check(controller.Stage.CurrentSpeaker == firstId, "scrub value callback must seek the timeline");
            GetField<Button>(controller, "_play").EmitSignal(BaseButton.SignalName.Pressed);
            Check(GetField<bool>(controller, "_playing"), "play callback must start playback");
            GetField<Button>(controller, "_stop").EmitSignal(BaseButton.SignalName.Pressed);
            Check(!GetField<bool>(controller, "_playing"), "stop callback must end playback");
            GD.Print("StoreDialogueSmoke UI PASS: five-dog reference cast, name/language/pose/count/gap callbacks, editor overlay, seek/play/stop.");

            if (exportRequested)
            {
                var output = Path.Combine(SmokeOutputDirectory(), "exports", DateTime.Now.ToString("yyyyMMdd-HHmmss-fff"));
                Directory.CreateDirectory(output);
                GetField<LineEdit>(controller, "_exportFolder").Text = output;
                GetField<LineEdit>(controller, "_ffmpeg").Text = System.Environment.GetEnvironmentVariable("FFMPEG_PATH") ?? "";
                var format = GetField<OptionButton>(controller, "_format");
                format.Select(format.GetItemIndex((int)StoreDialogueExportFormat.WebM));
                await RunControllerExport(controller, allLanguages: true);
                var webm = Directory.GetFiles(output, "*.webm");
                Check(webm.Length == example.Locales.Count && webm.All(path => new FileInfo(path).Length > 0),
                    "all-language export must create one nonempty WebM per language");
                Check(webm.Any(path => Path.GetFileName(path).StartsWith("dialogue-zh-CN-", StringComparison.Ordinal))
                    && webm.Any(path => Path.GetFileName(path).StartsWith("dialogue-en-", StringComparison.Ordinal)),
                    "WebM exports must retain distinct language filenames");
                format.Select(format.GetItemIndex((int)StoreDialogueExportFormat.WebP));
                await RunControllerExport(controller, allLanguages: false);
                var webp = Directory.GetFiles(output, "*.webp");
                Check(webp.Length == 1 && new FileInfo(webp[0]).Length > 0, "current-language export must create a nonempty WebP");
                GD.Print("StoreDialogueSmoke EXPORT PASS: actual five-dog controller exported bilingual WebM and current-locale WebP to " + output);
            }
        }
        finally
        {
            // The real export button persists machine-local options. Preserve any
            // author preferences exactly instead of leaving smoke paths behind.
            if (existingPreferences != null)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(preferencesPath)!);
                File.WriteAllBytes(preferencesPath, existingPreferences);
            }
            else if (File.Exists(preferencesPath)) File.Delete(preferencesPath);
            if (GodotObject.IsInstanceValid(controller)) controller.QueueFree();
            await NextFrame();
        }
    }

    private async Task CheckSavedTranslations(bool render)
    {
        var controller = _controllerScene.Instantiate<StoreDialogueController>();
        AddChild(controller);
        try
        {
            var project = StoreDialogueProject.Load(GetField<string>(controller, "_projectPath"));
            foreach (var language in project.Locales.Keys)
            {
                controller.ApplyProject(project, language);
                var errors = project.Validate(language, forExport: true).Concat(controller.Stage.ValidatePresentation()).ToArray();
                Check(errors.Length == 0, language + " saved translation does not fit: " + string.Join("; ", errors));
                Check(GetField<Label>(controller.Stage, "_bubbleText").GetThemeColor("font_color").ToHtml(false) == "683a32",
                    "speech text must resolve to the requested #683A32 color");
                if (render)
                {
                    controller.Stage.Seek(project.LeadInSeconds + 0.1);
                    await NextFrame();
                    await NextFrame();
                    using var image = controller.CaptureExportFrame();
                    image.Convert(Image.Format.Rgba8);
                    var bytes = image.GetData();
                    StoreDialogueExportCore.UnpremultiplyRgba(bytes);
                    image.SetData(image.GetWidth(), image.GetHeight(), false, Image.Format.Rgba8, bytes);
                    var safeLanguage = System.Text.RegularExpressions.Regex.Replace(language, "[^a-zA-Z0-9_-]", "_");
                    var path = Path.Combine(SmokeOutputDirectory(), "localized-preview-" + safeLanguage + ".png");
                    Check(image.SavePng(path) == Error.Ok, "could not save localized preview");
                    GD.Print("StoreDialogueSmoke localized preview: " + path);
                }
                GD.Print("StoreDialogueSmoke LOCALIZATION PASS: " + language + " names, all dialogue bubbles and speech color.");
            }
        }
        finally
        {
            controller.QueueFree();
            await NextFrame();
        }
    }

    private async Task CheckExpression(StoreDialogueDogView view, EDogReactionTrigger trigger, string eyes, string ears)
    {
        Check(view.Visual.CurrentReaction == trigger, "preview must use the selected reaction");
        CheckTexture(view.Visual.GetNode<Sprite2D>("HeadRoot/Eyes"), eyes, "action eyes");
        CheckTexture(view.Visual.GetNode<Sprite2D>("HeadRoot/Ears"), ears, "action ears");
        await NextFrame();
        Check(!view.Visual.IsInteractionHintPlaying, "capture reactions must not run autonomous paw or tongue tweens");
        foreach (var part in new[] { "ClawLeft", "ClawRight" })
        {
            var claw = view.Visual.GetNode<Node2D>(part);
            Check(claw.GetNode<Sprite2D>("Claw_Back_Left").Visible
                && !claw.GetNode<Sprite2D>("Claw_Palm_Left").Visible && Mathf.IsZeroApprox(claw.Rotation),
                "reaction selection must leave both paws on their backs without waving");
        }
    }

    private async Task RunControllerExport(StoreDialogueController controller, bool allLanguages)
    {
        controller.GetType().GetMethod("StartExport", Private)!.Invoke(controller, new object[] { allLanguages });
        var start = DateTime.UtcNow;
        while (GetField<bool>(controller, "_busy"))
        {
            if ((DateTime.UtcNow - start).TotalSeconds > 300)
            {
                GetField<Button>(controller, "_cancelExport").EmitSignal(BaseButton.SignalName.Pressed);
                throw new TimeoutException("controller export exceeded five minutes");
            }
            await NextFrame();
        }
        var status = GetField<Label>(controller, "_status").Text;
        Check(status.StartsWith("导出完成", StringComparison.Ordinal), "controller export failed: " + status);
        GD.Print("StoreDialogueSmoke export completion: " + status.Replace('\n', ' '));
    }

    private static int CountOpaque(Image image, Rect2 area, bool darkOnly = false, Color? expectedColor = null)
    {
        var count = 0;
        for (var y = Math.Max(0, (int)area.Position.Y); y < Math.Min(image.GetHeight(), (int)area.End.Y); y++)
            for (var x = Math.Max(0, (int)area.Position.X); x < Math.Min(image.GetWidth(), (int)area.End.X); x++)
            {
                var pixel = image.GetPixel(x, y);
                var matchesColor = !expectedColor.HasValue ||
                    (Mathf.Abs(pixel.R - expectedColor.Value.R) < 0.01f && Mathf.Abs(pixel.G - expectedColor.Value.G) < 0.01f
                        && Mathf.Abs(pixel.B - expectedColor.Value.B) < 0.01f);
                if (pixel.A > 0.5 && matchesColor && (!darkOnly || (pixel.R < 0.25 && pixel.G < 0.25 && pixel.B < 0.25))) count++;
            }
        return count;
    }

    private async Task NextFrame() => await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

    private static string SmokeOutputDirectory() =>
        Path.GetFullPath(Path.Combine(ProjectSettings.GlobalizePath("res://"), "..", ".local-build", "store-dialogue-smoke"));

    private static string FileFingerprint(string path) => File.Exists(path)
        ? Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))) : "missing";

    private static T GetField<T>(object target, string name) =>
        (T)(target.GetType().GetField(name, Private)?.GetValue(target)
            ?? throw new InvalidOperationException("Missing runtime field: " + name));

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
