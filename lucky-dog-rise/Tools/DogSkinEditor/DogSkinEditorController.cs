using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using DataTables;
using Godot;

namespace LuckyDogRise.Tools;

public partial class DogSkinEditorController : Control
{
    private const string DraftResourcePath = "res://Tools/DogSkinEditor/output/DogSkinCatalogDraft.json";
    private const string CsvResourcePath = "res://Tools/DogSkinEditor/output/DogSkin.csv";
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };
    private static readonly PackedScene DogAreaScene = GD.Load<PackedScene>("res://Scenes/Shared/DogArea.tscn");

    private DogSkinCatalogDraft _catalog = new();
    private DogSkinAssetCatalog _assets = null!;
    private VBoxContainer _rootLayout = null!;
    private PanelContainer _content = null!;
    private Label _status = null!;
    private GridContainer _overviewGrid = null!;
    private ScrollContainer _overviewScroll = null!;
    private Label _overviewSummary = null!;
    private LineEdit _searchInput = null!;
    private OptionButton _headFilter = null!;
    private OptionButton _eyewearFilter = null!;
    private Button _duplicateOnlyFilter = null!;
    private DogSkinDraft _editingDraft = null!;
    private DogVisual _editorPreview = null!;
    private DogSkinIconExportService _iconExportService = null!;
    private EDogReactionTrigger _editorReaction = EDogReactionTrigger.Default;
    private bool _hasUnsavedChanges;
    private readonly List<Label> _dirtyIndicators = new();

    public override void _EnterTree()
    {
        DisplayServer.WindowSetMode(DisplayServer.WindowMode.Windowed);
        DisplayServer.WindowSetFlag(DisplayServer.WindowFlags.Borderless, false);
        DisplayServer.WindowSetFlag(DisplayServer.WindowFlags.Transparent, false);
        DisplayServer.WindowSetFlag(DisplayServer.WindowFlags.AlwaysOnTop, false);
        DisplayServer.WindowSetFlag(DisplayServer.WindowFlags.ResizeDisabled, false);
        GetWindow().TransparentBg = false;
    }

    public override void _Ready()
    {
        Theme = GD.Load<Theme>("res://Tools/DogSkinEditor/DogSkinEditorTheme.tres");
        GetWindow().Title = "Lucky Dog Rise - DogSkin Editor";
        GetWindow().MinSize = new Vector2I(1180, 720);
        var usable = DisplayServer.ScreenGetUsableRect(DisplayServer.WindowGetCurrentScreen()).Size;
        DisplayServer.WindowSetSize(new Vector2I(Math.Min(1480, usable.X), Math.Min(900, usable.Y)));
        CenterWindow();

        _assets = new DogSkinAssetCatalog();
        _iconExportService = new DogSkinIconExportService();
        AddChild(_iconExportService);
        LoadCatalog();
        BuildShell();
        ShowOverview();
    }

    private void CenterWindow()
    {
        var screen = DisplayServer.WindowGetCurrentScreen();
        var rect = DisplayServer.ScreenGetUsableRect(screen);
        var size = DisplayServer.WindowGetSize();
        DisplayServer.WindowSetPosition(rect.Position + (rect.Size - size) / 2);
    }

    private void BuildShell()
    {
        var background = new ColorRect { Color = new Color("202531") };
        SetFullRect(background);
        AddChild(background);

        _rootLayout = new VBoxContainer();
        SetFullRect(_rootLayout, 14);
        _rootLayout.AddThemeConstantOverride("separation", 10);
        AddChild(_rootLayout);

        var toolbar = new HBoxContainer();
        toolbar.AddThemeConstantOverride("separation", 8);
        _rootLayout.AddChild(toolbar);
        toolbar.AddChild(CreateTitle("DogSkin 可视化资产管理器", 22));
        AddSpacer(toolbar);
        toolbar.AddChild(CreateButton("总览", ShowOverview));
        toolbar.AddChild(CreateButton("新建", CreateNewDraft));
        toolbar.AddChild(CreateButton("保存草稿", SaveCatalog));
        toolbar.AddChild(CreateButton("重新载入正式 JSON", ConfirmReloadFromTable));
        toolbar.AddChild(CreateButton("导出完整 CSV", ExportCsv));
        toolbar.AddChild(CreateButton("合成道具图标", ShowIconComposer));

        _status = new Label
        {
            Text = "就绪",
            Modulate = new Color("aeb8c7"),
            TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis,
        };
        _rootLayout.AddChild(_status);

        _content = new PanelContainer
        {
            SizeFlagsVertical = SizeFlags.ExpandFill,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
        };
        _rootLayout.AddChild(_content);
    }

    private void ShowOverview()
    {
        _editingDraft = null;
        _dirtyIndicators.Clear();
        ClearChildren(_content);

        var body = new VBoxContainer();
        body.AddThemeConstantOverride("separation", 10);
        _content.AddChild(body);

        var filterRow = new HBoxContainer();
        filterRow.AddThemeConstantOverride("separation", 8);
        body.AddChild(filterRow);

        _searchInput = new LineEdit
        {
            PlaceholderText = "搜索 ID、中文别名、图标名、目录或素材名",
            CustomMinimumSize = new Vector2(320, 38),
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
        };
        _searchInput.TextSubmitted += _ => RefreshOverviewGrid();
        filterRow.AddChild(_searchInput);
        filterRow.AddChild(CreateButton("筛选", RefreshOverviewGrid));

        _headFilter = CreateFilterOption("全部头型", _catalog.DogSkins.Select(d => d.Head), DisplayAssetName);
        _headFilter.ItemSelected += _ => RefreshOverviewGrid();
        filterRow.AddChild(_headFilter);
        _eyewearFilter = CreateFilterOption("全部眼镜", _catalog.DogSkins.Select(d => DisplayEyewear(d.FixedEyewear)));
        _eyewearFilter.ItemSelected += _ => RefreshOverviewGrid();
        filterRow.AddChild(_eyewearFilter);
        _duplicateOnlyFilter = new Button
        {
            Text = "仅看重复",
            ToggleMode = true,
            CustomMinimumSize = new Vector2(110, 38),
        };
        _duplicateOnlyFilter.Toggled += _ => RefreshOverviewGrid();
        filterRow.AddChild(_duplicateOnlyFilter);

        _overviewSummary = new Label { Modulate = new Color("c7cfda") };
        body.AddChild(_overviewSummary);

        _overviewScroll = new ScrollContainer
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ExpandFill,
        };
        _overviewScroll.Resized += UpdateOverviewColumns;
        body.AddChild(_overviewScroll);
        _overviewGrid = new GridContainer
        {
            Columns = 1,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
        };
        _overviewGrid.AddThemeConstantOverride("h_separation", 10);
        _overviewGrid.AddThemeConstantOverride("v_separation", 10);
        _overviewScroll.AddChild(_overviewGrid);

        RefreshOverviewGrid();
        Callable.From(UpdateOverviewColumns).CallDeferred();
    }

    private void UpdateOverviewColumns()
    {
        if (_overviewGrid == null || _overviewScroll == null)
            return;

        const float cardWidth = 230f;
        const float separation = 10f;
        var availableWidth = Math.Max(cardWidth, _overviewScroll.Size.X - 18f);
        _overviewGrid.Columns = Math.Max(1, Mathf.FloorToInt((availableWidth + separation) / (cardWidth + separation)));
    }

    private void RefreshOverviewGrid()
    {
        if (_overviewGrid == null)
            return;

        ClearChildren(_overviewGrid);
        var query = _searchInput?.Text.Trim() ?? "";
        var head = SelectedFilter(_headFilter, "全部头型");
        var eyewear = SelectedFilter(_eyewearFilter, "全部眼镜");

        var headCounts = _catalog.DogSkins.GroupBy(d => d.Head).ToDictionary(g => g.Key, g => g.Count());
        var eyewearCounts = _catalog.DogSkins.GroupBy(d => DisplayEyewear(d.FixedEyewear)).ToDictionary(g => g.Key, g => g.Count());
        var duplicateGroups = _catalog.DogSkins
            .GroupBy(CombinationKey)
            .Where(group => group.Count() > 1)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<int>)group.Select(draft => draft.Id).OrderBy(id => id).ToArray());

        var visible = _catalog.DogSkins
            .Where(d => string.IsNullOrEmpty(head) || d.Head == head)
            .Where(d => string.IsNullOrEmpty(eyewear) || DisplayEyewear(d.FixedEyewear) == eyewear)
            .Where(d => string.IsNullOrEmpty(query) || SearchText(d).Contains(query, StringComparison.OrdinalIgnoreCase))
            .Where(d => !_duplicateOnlyFilter.ButtonPressed || duplicateGroups.ContainsKey(CombinationKey(d)))
            .OrderBy(d => d.Id)
            .ToArray();

        var duplicateDogCount = duplicateGroups.Values.Sum(ids => ids.Count);
        _overviewSummary.Text = $"共 {_catalog.DogSkins.Count} 只，当前显示 {visible.Length} 只；"
            + $"头型 {_catalog.DogSkins.Select(d => d.Head).Distinct().Count()} 种，"
            + $"眼镜 {_catalog.DogSkins.Select(d => DisplayEyewear(d.FixedEyewear)).Distinct().Count()} 种，"
            + (duplicateGroups.Count == 0
                ? "未发现完全重复。"
                : $"完全重复 {duplicateGroups.Count} 组、涉及 {duplicateDogCount} 只。");

        foreach (var draft in visible)
        {
            duplicateGroups.TryGetValue(CombinationKey(draft), out var duplicateIds);
            _overviewGrid.AddChild(CreateDogCard(draft, headCounts, eyewearCounts, duplicateIds));
        }
    }

    private Control CreateDogCard(
        DogSkinDraft draft,
        IReadOnlyDictionary<string, int> headCounts,
        IReadOnlyDictionary<string, int> eyewearCounts,
        IReadOnlyList<int> duplicateIds)
    {
        var card = new PanelContainer
        {
            CustomMinimumSize = new Vector2(230, 252),
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
        };
        if (duplicateIds != null)
        {
            card.AddThemeStyleboxOverride("panel", new StyleBoxFlat
            {
                BgColor = new Color("2b2926"),
                BorderColor = new Color("d69a43"),
                BorderWidthLeft = 3,
                BorderWidthTop = 1,
                BorderWidthRight = 1,
                BorderWidthBottom = 1,
                CornerRadiusTopLeft = 3,
                CornerRadiusTopRight = 3,
                CornerRadiusBottomRight = 3,
                CornerRadiusBottomLeft = 3,
                ContentMarginLeft = 10,
                ContentMarginTop = 10,
                ContentMarginRight = 10,
                ContentMarginBottom = 10,
            });
        }
        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", 4);
        card.AddChild(box);
        if (duplicateIds != null)
        {
            box.AddChild(new Label
            {
                Text = $"● 完全重复：{string.Join("、", duplicateIds.Select(id => $"#{id}"))}",
                Modulate = new Color("e5a84b"),
                HorizontalAlignment = HorizontalAlignment.Center,
                TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis,
                TooltipText = $"完整视觉配置相同：{string.Join("、", duplicateIds.Select(id => $"#{id}"))}",
            });
        }
        box.AddChild(CreateDogViewport(draft, thumbnail: true));
        box.AddChild(new Label
        {
            Text = $"#{draft.Id}  {draft.Alias}",
            HorizontalAlignment = HorizontalAlignment.Center,
            TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis,
            TooltipText = draft.Alias,
        });
        box.AddChild(new Label
        {
            Text = DisplayAssetName(draft.IconName),
            Modulate = new Color("aeb8c7"),
            HorizontalAlignment = HorizontalAlignment.Center,
            TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis,
            TooltipText = draft.IconName,
        });
        box.AddChild(new Label
        {
            Text = $"{DisplayAssetName(draft.Head)} ×{headCounts.GetValueOrDefault(draft.Head)}\n"
                + $"{DisplayEyewear(draft.FixedEyewear)} ×{eyewearCounts.GetValueOrDefault(DisplayEyewear(draft.FixedEyewear))}",
            Modulate = new Color("aeb8c7"),
            HorizontalAlignment = HorizontalAlignment.Center,
        });

        var buttons = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        buttons.AddChild(CreateButton("编辑", () => ShowEditor(draft)));
        buttons.AddChild(CreateButton("克隆", () => CloneDraft(draft)));
        box.AddChild(buttons);
        return card;
    }

    private void ShowEditor(DogSkinDraft draft)
    {
        _editingDraft = draft;
        _dirtyIndicators.Clear();
        ClearChildren(_content);

        var split = new HSplitContainer
        {
            SizeFlagsVertical = SizeFlags.ExpandFill,
            SplitOffsets = [760],
        };
        _content.AddChild(split);

        var previewColumn = new VBoxContainer { CustomMinimumSize = new Vector2(700, 0) };
        split.AddChild(previewColumn);
        var previewHeader = new HBoxContainer();
        previewHeader.AddChild(CreateTitle($"#{draft.Id} 实际渲染预览", 18));
        AddSpacer(previewHeader);
        var reactionOption = new OptionButton { CustomMinimumSize = new Vector2(230, 38) };
        foreach (var trigger in Enum.GetValues<EDogReactionTrigger>())
        {
            reactionOption.AddItem(trigger.ToString(), (int)trigger);
            if (trigger == _editorReaction)
                reactionOption.Select(reactionOption.ItemCount - 1);
        }
        reactionOption.ItemSelected += index =>
        {
            _editorReaction = (EDogReactionTrigger)reactionOption.GetItemId((int)index);
            ApplyEditorPreviewReaction();
        };
        previewHeader.AddChild(new Label { Text = "表情：", VerticalAlignment = VerticalAlignment.Center });
        previewHeader.AddChild(reactionOption);
        previewColumn.AddChild(previewHeader);

        var preview = CreateDogViewport(draft, thumbnail: false);
        preview.SizeFlagsVertical = SizeFlags.ExpandFill;
        preview.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        previewColumn.AddChild(preview);

        var editorScroll = new ScrollContainer
        {
            CustomMinimumSize = new Vector2(470, 0),
            SizeFlagsVertical = SizeFlags.ExpandFill,
        };
        split.AddChild(editorScroll);
        var form = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        form.AddThemeConstantOverride("separation", 12);
        editorScroll.AddChild(form);
        form.AddChild(CreateTitle("DogSkin 草稿", 18));
        form.AddChild(CreateSaveActions());

        var basicSection = CreateFormSection(form, "基础信息", "编号、策划识别名与素材来源");
        AddIdField(basicSection, draft);
        AddTextField(basicSection, "Alias", draft.Alias, value => draft.Alias = value);
        AddPngNameField(basicSection, "IconName", draft.IconName, value => draft.IconName = value);
        AddChoiceField(basicSection, "FolderPath", draft.FolderPath, _assets.FolderPaths, value =>
        {
            draft.FolderPath = value;
            ShowEditor(draft);
        });

        var defaultSection = CreateFormSection(form, "默认造型", "狗狗常态下使用的主要视觉组合");
        AddHeadPickerField(defaultSection, draft);
        AddChoiceField(defaultSection, "DefaultEars", draft.DefaultEars, _assets.GetFiles(draft.FolderPath, "Ears_"), value => draft.DefaultEars = value);
        AddChoiceField(defaultSection, "DefaultEyes", draft.DefaultEyes, _assets.GetFiles(draft.FolderPath, "Eyes_"), value => draft.DefaultEyes = value);
        AddEyewearPickerField(defaultSection, draft);
        AddTextField(defaultSection, "DefaultTongue", draft.DefaultTongue, value => draft.DefaultTongue = value);

        var bodySection = CreateFormSection(form, "爪子与舌头", "身体部件的常规素材");
        AddChoiceField(bodySection, "Claw_Left_Back", draft.ClawLeftBack, _assets.GetFiles(draft.FolderPath, "Claw_"), value => draft.ClawLeftBack = value);
        AddChoiceField(bodySection, "Claw_Right_Palms", draft.ClawRightPalms, _assets.GetFiles(draft.FolderPath, "Claw_"), value => draft.ClawRightPalms = value);
        AddChoiceField(bodySection, "Tongue_Regular", draft.TongueRegular, _assets.GetFiles(draft.FolderPath, "Tongue_"), value => draft.TongueRegular = value);

        var reactionSection = CreateFormSection(form, "表情素材", "DogReaction 切换时使用的耳朵与眼睛");
        AddChoiceField(reactionSection, "Ears_Happy", draft.EarsHappy, _assets.GetFiles(draft.FolderPath, "Ears_"), value => draft.EarsHappy = value);
        AddChoiceField(reactionSection, "Ears_Plane", draft.EarsPlane, _assets.GetFiles(draft.FolderPath, "Ears_"), value => draft.EarsPlane = value);
        AddChoiceField(reactionSection, "Eyes_Bored", draft.EyesBored, _assets.GetFiles(draft.FolderPath, "Eyes_"), value => draft.EyesBored = value);
        AddChoiceField(reactionSection, "Eyes_Cute", draft.EyesCute, _assets.GetFiles(draft.FolderPath, "Eyes_"), value => draft.EyesCute = value);
        AddChoiceField(reactionSection, "Eyes_Happy", draft.EyesHappy, _assets.GetFiles(draft.FolderPath, "Eyes_"), value => draft.EyesHappy = value);
        AddChoiceField(reactionSection, "Eyes_Lucky", draft.EyesLucky, _assets.GetFiles(draft.FolderPath, "Eyes_"), value => draft.EyesLucky = value);
        AddChoiceField(reactionSection, "Eyes_Neutral", draft.EyesNeutral, _assets.GetFiles(draft.FolderPath, "Eyes_"), value => draft.EyesNeutral = value);
        AddChoiceField(reactionSection, "Eyes_Wink", draft.EyesWink, _assets.GetFiles(draft.FolderPath, "Eyes_"), value => draft.EyesWink = value);

        form.AddChild(CreateSaveActions());
    }

    private HBoxContainer CreateSaveActions()
    {
        var row = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.End };
        row.AddThemeConstantOverride("separation", 8);
        var dirtyIndicator = new Label
        {
            CustomMinimumSize = new Vector2(92, 36),
            VerticalAlignment = VerticalAlignment.Center,
            Modulate = new Color("e5a84b"),
        };
        _dirtyIndicators.Add(dirtyIndicator);
        UpdateDirtyIndicator(dirtyIndicator);
        row.AddChild(dirtyIndicator);
        row.AddChild(CreateButton("放弃并返回", ConfirmDiscardAndReturn));
        row.AddChild(CreateButton("仅保存草稿", SaveCatalog));
        row.AddChild(CreateButton("保存并返回总览", SaveAndReturnToOverview));
        return row;
    }

    private void ConfirmDiscardAndReturn()
    {
        if (!_hasUnsavedChanges)
        {
            ShowOverview();
            return;
        }

        var dialog = new ConfirmationDialog
        {
            Title = "放弃未保存的修改",
            DialogText = "这会重新载入上次保存的草稿，并放弃自上次保存以来的全部修改，包括新建或克隆的狗狗。\n\n确定要放弃并返回总览吗？",
        };
        dialog.GetOkButton().Text = "放弃并返回";
        dialog.GetCancelButton().Text = "继续编辑";
        dialog.Confirmed += () =>
        {
            LoadCatalog();
            SetDirtyState(false);
            ShowOverview();
            SetStatus("已放弃未保存的修改，并重新载入上次保存的草稿。", false);
        };
        dialog.Canceled += dialog.QueueFree;
        dialog.Confirmed += dialog.QueueFree;
        AddChild(dialog);
        dialog.PopupCentered(new Vector2I(600, 250));
    }

    private void SaveAndReturnToOverview()
    {
        SaveCatalog();
        if (!_hasUnsavedChanges)
            ShowOverview();
    }

    private void SetDirtyState(bool dirty)
    {
        _hasUnsavedChanges = dirty;
        foreach (var indicator in _dirtyIndicators)
        {
            if (GodotObject.IsInstanceValid(indicator))
                UpdateDirtyIndicator(indicator);
        }
    }

    private void UpdateDirtyIndicator(Label indicator)
    {
        indicator.Text = _hasUnsavedChanges ? "● 未保存" : "";
    }

    private static VBoxContainer CreateFormSection(VBoxContainer form, string title, string description)
    {
        var panelStyle = new StyleBoxFlat
        {
            BgColor = new Color("202733"),
            BorderColor = new Color("3f4a5b"),
            BorderWidthLeft = 3,
            BorderWidthTop = 1,
            BorderWidthRight = 1,
            BorderWidthBottom = 1,
            CornerRadiusTopLeft = 3,
            CornerRadiusTopRight = 3,
            CornerRadiusBottomRight = 3,
            CornerRadiusBottomLeft = 3,
            ContentMarginLeft = 12,
            ContentMarginTop = 10,
            ContentMarginRight = 12,
            ContentMarginBottom = 12,
        };
        var panel = new PanelContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        panel.AddThemeStyleboxOverride("panel", panelStyle);
        form.AddChild(panel);

        var section = new VBoxContainer();
        section.AddThemeConstantOverride("separation", 7);
        panel.AddChild(section);

        var heading = CreateTitle(title, 17);
        heading.Modulate = new Color("75b8e6");
        section.AddChild(heading);
        section.AddChild(new Label
        {
            Text = description,
            Modulate = new Color("8f9bab"),
        });
        var divider = new ColorRect
        {
            Color = new Color("3f4a5b"),
            CustomMinimumSize = new Vector2(0, 1),
            MouseFilter = MouseFilterEnum.Ignore,
        };
        section.AddChild(divider);
        return section;
    }

    private SubViewportContainer CreateDogViewport(DogSkinDraft draft, bool thumbnail)
    {
        var container = new SubViewportContainer
        {
            Stretch = true,
            CustomMinimumSize = thumbnail ? new Vector2(220, 160) : new Vector2(680, 680),
            MouseFilter = MouseFilterEnum.Ignore,
        };
        var viewport = new SubViewport
        {
            Size = thumbnail ? new Vector2I(220, 160) : new Vector2I(680, 680),
            TransparentBg = false,
            Disable3D = true,
            RenderTargetUpdateMode = SubViewport.UpdateMode.Always,
        };
        container.AddChild(viewport);
        var background = new ColorRect { Color = new Color("aeb28f") };
        SetFullRect(background);
        viewport.AddChild(background);

        // Mirror Main.tscn's table layer without introducing a distracting
        // table texture into the asset editor: palm(1) < mask/table(2) < back(3).
        var tableMask = new ColorRect
        {
            Color = background.Color,
            ZIndex = 2,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        viewport.AddChild(tableMask);

        var dog = DogAreaScene.Instantiate<DogVisual>();
        dog.SetPreviewAppearance(draft.ToAppearanceSpec());
        viewport.AddChild(dog);
        dog.SetHitButtonEnabled(false);
        dog.CallDeferred(nameof(DogVisual.SetHitButtonEnabled), false);
        dog.CallDeferred(nameof(DogVisual.ShowInspectionClaws));
        void UpdatePreviewTransform()
        {
            var size = container.Size;
            if (size.X <= 0f || size.Y <= 0f)
                return;

            // DogArea uses the same 1200x1200 PSD coordinate system as Main.tscn.
            // Frame the dog itself rather than scaling the whole design canvas.
            var sourceFrame = new Rect2(180f, 40f, 840f, 760f);
            var scale = Mathf.Min(size.X / sourceFrame.Size.X, size.Y / sourceFrame.Size.Y);
            var fittedSize = sourceFrame.Size * scale;
            var letterboxOffset = (size - fittedSize) / 2f;
            var gameDogOrigin = new Vector2(610f, 677f);
            dog.Scale = Vector2.One * scale;
            dog.Position = letterboxOffset + (gameDogOrigin - sourceFrame.Position) * scale;

            var tableY = letterboxOffset.Y + (677f - sourceFrame.Position.Y) * scale;
            tableMask.Position = new Vector2(0f, tableY);
            tableMask.Size = new Vector2(size.X, Mathf.Max(0f, size.Y - tableY));
        }
        container.Resized += UpdatePreviewTransform;
        Callable.From(UpdatePreviewTransform).CallDeferred();
        if (!thumbnail)
        {
            _editorPreview = dog;
            ApplyEditorPreviewReaction();
        }

        return container;
    }

    private void AddIdField(VBoxContainer form, DogSkinDraft draft)
    {
        var row = CreateFieldRow("Id");
        var input = new SpinBox
        {
            MinValue = 1,
            MaxValue = 999999,
            Step = 1,
            Value = draft.Id,
            AllowGreater = true,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
        };
        input.ValueChanged += value =>
        {
            draft.Id = (int)value;
            SetDirtyState(true);
            RefreshEditorPreview();
        };
        row.AddChild(input);
        form.AddChild(row);
    }

    private void AddTextField(VBoxContainer form, string label, string value, Action<string> setter)
    {
        var row = CreateFieldRow(label);
        var input = new LineEdit { Text = value ?? "", SizeFlagsHorizontal = SizeFlags.ExpandFill };
        input.TextChanged += text =>
        {
            setter(text);
            SetDirtyState(true);
            RefreshEditorPreview();
        };
        row.AddChild(input);
        form.AddChild(row);
    }

    private void AddPngNameField(VBoxContainer form, string label, string value, Action<string> setter)
    {
        var row = CreateFieldRow(label);
        var input = new LineEdit
        {
            Text = DisplayAssetName(value),
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            TooltipText = value,
        };
        input.TextChanged += text =>
        {
            setter(string.IsNullOrWhiteSpace(text) ? "" : Path.ChangeExtension(text, ".png"));
            input.TooltipText = string.IsNullOrWhiteSpace(text) ? "" : Path.ChangeExtension(text, ".png");
            SetDirtyState(true);
            RefreshEditorPreview();
        };
        row.AddChild(input);
        form.AddChild(row);
    }

    private void AddChoiceField(
        VBoxContainer form,
        string label,
        string value,
        IEnumerable<string> values,
        Action<string> setter,
        string emptyLabel = "")
    {
        var row = CreateFieldRow(label);
        var option = new OptionButton { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        var choices = values.Where(item => item != null).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (!choices.Contains(value ?? "", StringComparer.OrdinalIgnoreCase))
            choices.Insert(0, value ?? "");
        for (var i = 0; i < choices.Count; i++)
        {
            option.AddItem(string.IsNullOrEmpty(choices[i]) ? emptyLabel : DisplayAssetName(choices[i]));
            option.SetItemMetadata(i, choices[i]);
            if (string.Equals(choices[i], value, StringComparison.OrdinalIgnoreCase))
                option.Select(i);
        }
        option.ItemSelected += index =>
        {
            setter(option.GetItemMetadata((int)index).AsString());
            SetDirtyState(true);
            RefreshEditorPreview();
        };
        row.AddChild(option);
        form.AddChild(row);
    }

    private void AddEyewearPickerField(VBoxContainer form, DogSkinDraft draft)
    {
        var row = CreateFieldRow("FixedEyewear");
        var selected = new Label
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            VerticalAlignment = VerticalAlignment.Center,
            TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis,
        };
        void RefreshSelectedLabel()
        {
            var displayName = DisplayEyewear(draft.FixedEyewear);
            var usage = _catalog.DogSkins.Count(item => item.FixedEyewear == draft.FixedEyewear);
            selected.Text = $"{displayName}  ×{usage}";
        }
        RefreshSelectedLabel();
        row.AddChild(selected);
        row.AddChild(CreateButton("查看造型并选择…", () =>
            ShowEyewearPicker(draft, RefreshSelectedLabel)));
        form.AddChild(row);
    }

    private void AddHeadPickerField(VBoxContainer form, DogSkinDraft draft)
    {
        var row = CreateFieldRow("Head");
        var selected = new Label
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            VerticalAlignment = VerticalAlignment.Center,
            TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis,
        };
        void RefreshSelectedLabel()
        {
            var usage = _catalog.DogSkins.Count(item =>
                string.Equals(item.Head, draft.Head, StringComparison.OrdinalIgnoreCase));
            selected.Text = $"{DisplayAssetName(draft.Head)}  ×{usage}";
            selected.TooltipText = $"素材：{draft.FolderPath}\\{draft.Head}";
        }
        RefreshSelectedLabel();
        row.AddChild(selected);
        row.AddChild(CreateButton("查看造型并选择…", () =>
            ShowHeadPicker(draft, RefreshSelectedLabel)));
        form.AddChild(row);
    }

    private void ShowHeadPicker(DogSkinDraft draft, Action selectionChanged)
    {
        var picker = new Window
        {
            Title = "选择狗头",
            Size = new Vector2I(820, 680),
            MinSize = new Vector2I(620, 480),
            Transient = true,
            Exclusive = true,
            Unresizable = false,
        };
        picker.CloseRequested += picker.QueueFree;
        AddChild(picker);

        var margin = new MarginContainer { Theme = Theme };
        margin.AddThemeConstantOverride("margin_left", 12);
        margin.AddThemeConstantOverride("margin_top", 12);
        margin.AddThemeConstantOverride("margin_right", 12);
        margin.AddThemeConstantOverride("margin_bottom", 12);
        SetFullRect(margin);
        picker.AddChild(margin);

        var layout = new VBoxContainer();
        layout.AddThemeConstantOverride("separation", 8);
        margin.AddChild(layout);
        layout.AddChild(CreateTitle("使用当前毛色与装扮实时预览", 20));
        layout.AddChild(new Label
        {
            Text = "候选范围来自当前 FolderPath；保留当前眼镜、耳朵、眼睛和其他 DogSkin 配置。",
            Modulate = new Color("aab2bf"),
        });
        var search = new LineEdit { PlaceholderText = "按狗头素材文件名筛选" };
        layout.AddChild(search);

        var scroll = new ScrollContainer
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ExpandFill,
        };
        layout.AddChild(scroll);
        var grid = new GridContainer
        {
            Columns = 3,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
        };
        grid.AddThemeConstantOverride("h_separation", 8);
        grid.AddThemeConstantOverride("v_separation", 8);
        scroll.AddChild(grid);

        void RebuildChoices()
        {
            ClearChildren(grid);
            var query = search.Text.Trim();
            foreach (var head in _assets.GetFiles(draft.FolderPath, "Head_")
                         .Where(file => string.IsNullOrEmpty(query)
                             || file.Contains(query, StringComparison.OrdinalIgnoreCase)))
            {
                var candidate = draft.CloneWithId(draft.Id);
                candidate.Head = head;
                var card = new PanelContainer
                {
                    CustomMinimumSize = new Vector2(245, 210),
                    SizeFlagsHorizontal = SizeFlags.ExpandFill,
                };
                var cardLayout = new VBoxContainer();
                card.AddChild(cardLayout);
                var preview = CreateDogViewport(candidate, thumbnail: true);
                preview.CustomMinimumSize = new Vector2(220, 145);
                cardLayout.AddChild(preview);
                var usage = _catalog.DogSkins.Count(item =>
                    string.Equals(item.Head, head, StringComparison.OrdinalIgnoreCase));
                var choose = CreateButton($"{DisplayAssetName(head)}  ×{usage}", () =>
                {
                    draft.Head = head;
                    selectionChanged();
                    SetDirtyState(true);
                    RefreshEditorPreview();
                    picker.QueueFree();
                });
                choose.TooltipText = $"素材：{draft.FolderPath}\\{head}";
                cardLayout.AddChild(choose);
                grid.AddChild(card);
            }
        }

        search.TextChanged += _ => RebuildChoices();
        RebuildChoices();
        picker.PopupCentered();
    }

    private void ShowEyewearPicker(DogSkinDraft draft, Action selectionChanged)
    {
        var picker = new Window
        {
            Title = "选择固定眼镜",
            Size = new Vector2I(820, 680),
            MinSize = new Vector2I(620, 480),
            Transient = true,
            Exclusive = true,
            Unresizable = false,
        };
        picker.CloseRequested += picker.QueueFree;
        AddChild(picker);

        var margin = new MarginContainer { Theme = Theme };
        margin.AddThemeConstantOverride("margin_left", 12);
        margin.AddThemeConstantOverride("margin_top", 12);
        margin.AddThemeConstantOverride("margin_right", 12);
        margin.AddThemeConstantOverride("margin_bottom", 12);
        SetFullRect(margin);
        picker.AddChild(margin);

        var layout = new VBoxContainer();
        layout.AddThemeConstantOverride("separation", 8);
        margin.AddChild(layout);
        layout.AddChild(CreateTitle("使用当前狗狗实时试戴", 20));
        layout.AddChild(new Label
        {
            Text = "眼镜不依赖 Item 表、道具图标或中文名称；预览直接使用原始素材和游戏实际渲染。",
            Modulate = new Color("aab2bf"),
        });
        var search = new LineEdit { PlaceholderText = "按素材文件名筛选" };
        layout.AddChild(search);

        var scroll = new ScrollContainer
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ExpandFill,
        };
        layout.AddChild(scroll);
        var grid = new GridContainer
        {
            Columns = 3,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
        };
        grid.AddThemeConstantOverride("h_separation", 8);
        grid.AddThemeConstantOverride("v_separation", 8);
        scroll.AddChild(grid);

        void RebuildChoices()
        {
            ClearChildren(grid);
            var query = search.Text.Trim();
            foreach (var eyewear in _assets.EyewearFiles
                         .Where(file => string.IsNullOrEmpty(query)
                             || DisplayEyewear(file).Contains(query, StringComparison.OrdinalIgnoreCase)))
            {
                var candidate = draft.CloneWithId(draft.Id);
                candidate.FixedEyewear = eyewear;
                var card = new PanelContainer
                {
                    CustomMinimumSize = new Vector2(245, 210),
                    SizeFlagsHorizontal = SizeFlags.ExpandFill,
                };
                var cardLayout = new VBoxContainer();
                card.AddChild(cardLayout);
                var preview = CreateDogViewport(candidate, thumbnail: true);
                preview.CustomMinimumSize = new Vector2(220, 145);
                cardLayout.AddChild(preview);
                var usage = _catalog.DogSkins.Count(item => item.FixedEyewear == eyewear);
                var choose = CreateButton($"{DisplayEyewear(eyewear)}  ×{usage}", () =>
                {
                    draft.FixedEyewear = eyewear;
                    selectionChanged();
                    SetDirtyState(true);
                    RefreshEditorPreview();
                    picker.QueueFree();
                });
                choose.TooltipText = string.IsNullOrEmpty(eyewear)
                    ? "清除固定眼镜"
                    : $"素材：v1\\Eyewear\\{eyewear}";
                cardLayout.AddChild(choose);
                grid.AddChild(card);
            }
        }

        search.TextChanged += _ => RebuildChoices();
        RebuildChoices();
        picker.PopupCentered();
    }

    private HBoxContainer CreateFieldRow(string label)
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 8);
        row.AddChild(new Label { Text = label, CustomMinimumSize = new Vector2(165, 32) });
        return row;
    }

    private void RefreshEditorPreview()
    {
        if (_editingDraft == null || _editorPreview == null)
            return;
        _editorPreview.SetPreviewAppearance(_editingDraft.ToAppearanceSpec());
        ApplyEditorPreviewReaction();
    }

    private void ApplyEditorPreviewReaction()
    {
        if (_editorPreview == null)
            return;

        _editorPreview.ApplyReaction(_editorReaction);
        _editorPreview.ShowInspectionClaws();
    }

    private void CreateNewDraft()
    {
        var nextId = NextId();
        var template = _catalog.DogSkins.FirstOrDefault(draft => draft.Id == 1001)
            ?? _catalog.DogSkins.FirstOrDefault();
        var draft = template?.CloneWithId(nextId) ?? new DogSkinDraft { Id = nextId };
        draft.Alias = $"新狗狗 {nextId}";
        _catalog.DogSkins.Add(draft);
        SetDirtyState(true);
        ShowEditor(draft);
        SetStatus($"已新建 DogSkin #{nextId}，尚未写入草稿文件。", false);
    }

    private void CloneDraft(DogSkinDraft source)
    {
        var draft = source.CloneWithId(NextId());
        draft.Alias = source.Alias + " 副本";
        _catalog.DogSkins.Add(draft);
        SetDirtyState(true);
        ShowEditor(draft);
        SetStatus($"已克隆为 DogSkin #{draft.Id}，尚未写入草稿文件。", false);
    }

    private int NextId() => _catalog.DogSkins.Count == 0 ? 1001 : _catalog.DogSkins.Max(d => d.Id) + 1;

    private void LoadCatalog()
    {
        var path = ProjectSettings.GlobalizePath(DraftResourcePath);
        try
        {
            if (File.Exists(path))
            {
                _catalog = JsonSerializer.Deserialize<DogSkinCatalogDraft>(File.ReadAllText(path), JsonOptions)
                    ?? CreateCatalogFromTable();
                MigrateCatalog();
                return;
            }
        }
        catch (Exception exception)
        {
            GD.PushWarning($"[DogSkin 编辑器] 草稿载入失败。技术详情：{exception.Message}");
        }
        _catalog = CreateCatalogFromTable();
    }

    private void MigrateCatalog()
    {
        if (_catalog.Version >= 2)
            return;

        var aliasesById = LubanData.Tables.TbDogSkin.DataList.ToDictionary(skin => skin.Id, skin => skin.Alias);
        foreach (var draft in _catalog.DogSkins)
        {
            if (string.IsNullOrWhiteSpace(draft.Alias)
                && aliasesById.TryGetValue(draft.Id, out var alias))
            {
                draft.Alias = alias;
            }
        }
        _catalog.Version = 2;
    }

    private static DogSkinCatalogDraft CreateCatalogFromTable()
    {
        return new DogSkinCatalogDraft
        {
            UpdatedAtUtc = DateTime.UtcNow,
            DogSkins = LubanData.Tables.TbDogSkin.DataList
                .Select(DogSkinDraft.FromDogSkin)
                .OrderBy(draft => draft.Id)
                .ToList(),
        };
    }

    private void SaveCatalog()
    {
        var path = ProjectSettings.GlobalizePath(DraftResourcePath);
        try
        {
            _catalog.UpdatedAtUtc = DateTime.UtcNow;
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var tempPath = path + ".tmp";
            File.WriteAllText(tempPath, JsonSerializer.Serialize(_catalog, JsonOptions), new UTF8Encoding(false));
            File.Move(tempPath, path, true);
            SetDirtyState(false);
            SetStatus($"草稿已保存：{path}", false);
        }
        catch (Exception exception)
        {
            GD.PushError($"[DogSkin 编辑器] 草稿保存失败：{exception}");
            SetStatus("草稿保存失败，请查看错误提示。", true);
            ShowMessage("草稿保存失败", DescribeFileOperationError(exception, path, "保存草稿"));
        }
    }

    private void ConfirmReloadFromTable()
    {
        var dialog = new ConfirmationDialog
        {
            Title = "重新载入正式 DogSkin JSON",
            DialogText = "这会用当前 Luban 正式数据替换内存中的全部草稿。已保存的草稿文件会在下次点击“保存草稿”时被覆盖。",
        };
        dialog.Confirmed += () =>
        {
            _catalog = CreateCatalogFromTable();
            SetDirtyState(true);
            ShowOverview();
            SetStatus("已从正式 tbdogskin.json 重新载入。", false);
        };
        dialog.Canceled += dialog.QueueFree;
        dialog.Confirmed += dialog.QueueFree;
        AddChild(dialog);
        dialog.PopupCentered(new Vector2I(560, 220));
    }

    private void ExportCsv()
    {
        var errors = ValidateCatalog();
        if (errors.Count > 0)
        {
            ShowMessage("导出前校验失败", string.Join("\n", errors.Take(24)));
            SetStatus($"CSV 未导出：发现 {errors.Count} 个错误。", true);
            return;
        }

        var path = ProjectSettings.GlobalizePath(CsvResourcePath);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, BuildCsv(), new UTF8Encoding(true));
            SetStatus($"完整 CSV 已导出：{path}", false);
            ShowExportCompleted(path);
        }
        catch (Exception exception)
        {
            GD.PushError($"[DogSkin 编辑器] CSV 导出失败：{exception}");
            SetStatus("CSV 导出失败，请查看错误提示。", true);
            ShowMessage("CSV 导出失败", DescribeFileOperationError(exception, path, "导出 CSV"));
        }
    }

    private void ShowIconComposer()
    {
        var errors = ValidateCatalog(requireDogSkinTableFields: false);
        if (errors.Count > 0)
        {
            ShowMessage("合成前校验失败", string.Join("\n", errors.Take(24)));
            SetStatus($"图标未生成：发现 {errors.Count} 个 DogSkin 数据错误。", true);
            return;
        }

        var window = new DogSkinIconComposerWindow();
        window.Initialize(_iconExportService, _catalog.DogSkins, Theme);
        AddChild(window);
        window.PopupCentered();
    }

    private static string DescribeFileOperationError(Exception exception, string path, string operation)
    {
        var reason = exception switch
        {
            DirectoryNotFoundException => "目标文件夹不存在，且无法自动创建。",
            UnauthorizedAccessException => "没有权限写入目标文件，请检查文件或文件夹权限。",
            PathTooLongException => "目标路径过长，Windows 无法写入该文件。",
            IOException when IsFileSharingViolation(exception) =>
                "目标文件正在被其他程序占用。请先关闭正在打开该文件的 Excel、文本编辑器或其他程序，然后重试。",
            IOException => "Windows 无法完成这次文件写入操作。请检查磁盘空间、路径和文件状态后重试。",
            ArgumentException => "目标文件路径无效，请检查工具的输出路径配置。",
            _ => "发生了未预期的文件写入错误，请查看 Godot 日志获取技术详情。",
        };
        return $"{operation}时发生错误。\n\n{reason}\n\n文件：{path}\n错误代码：0x{exception.HResult:X8}";
    }

    private static bool IsFileSharingViolation(Exception exception)
    {
        var windowsError = exception.HResult & 0xFFFF;
        return windowsError is 32 or 33;
    }

    private List<string> ValidateCatalog(bool requireDogSkinTableFields = true)
    {
        var errors = new List<string>();
        foreach (var duplicate in _catalog.DogSkins.GroupBy(d => d.Id).Where(group => group.Count() > 1))
            errors.Add($"编号（Id）{duplicate.Key} 重复。");

        foreach (var draft in _catalog.DogSkins)
        {
            if (requireDogSkinTableFields && string.IsNullOrWhiteSpace(draft.Alias))
                errors.Add($"#{draft.Id} 缺少中文别名（Alias）。");
            if (requireDogSkinTableFields && string.IsNullOrWhiteSpace(draft.IconName))
                errors.Add($"#{draft.Id} 缺少图标名称（IconName）。");
            if (string.IsNullOrWhiteSpace(draft.FolderPath)) errors.Add($"#{draft.Id} 缺少素材目录（FolderPath）。");
            ValidateDogAsset(errors, draft, nameof(draft.Head), draft.Head);
            ValidateDogAsset(errors, draft, nameof(draft.DefaultEars), draft.DefaultEars);
            ValidateDogAsset(errors, draft, nameof(draft.DefaultEyes), draft.DefaultEyes);
            ValidateDogAsset(errors, draft, nameof(draft.TongueRegular), draft.TongueRegular);
            ValidateDogAsset(errors, draft, nameof(draft.ClawLeftBack), draft.ClawLeftBack);
            ValidateDogAsset(errors, draft, nameof(draft.ClawRightPalms), draft.ClawRightPalms);
            ValidateDogAsset(errors, draft, nameof(draft.EarsHappy), draft.EarsHappy);
            ValidateDogAsset(errors, draft, nameof(draft.EarsPlane), draft.EarsPlane);
            ValidateDogAsset(errors, draft, nameof(draft.EyesBored), draft.EyesBored);
            ValidateDogAsset(errors, draft, nameof(draft.EyesCute), draft.EyesCute);
            ValidateDogAsset(errors, draft, nameof(draft.EyesHappy), draft.EyesHappy);
            ValidateDogAsset(errors, draft, nameof(draft.EyesLucky), draft.EyesLucky);
            ValidateDogAsset(errors, draft, nameof(draft.EyesNeutral), draft.EyesNeutral);
            ValidateDogAsset(errors, draft, nameof(draft.EyesWink), draft.EyesWink);
            if (!_assets.EyewearExists(draft.FixedEyewear))
                errors.Add($"#{draft.Id} 的固定眼镜（FixedEyewear）素材不存在：{draft.FixedEyewear}");
        }
        return errors;
    }

    private void ValidateDogAsset(List<string> errors, DogSkinDraft draft, string field, string value)
    {
        if (!_assets.ResourceExists(draft.FolderPath, value))
            errors.Add($"#{draft.Id} 的{FieldDisplayName(field)}素材不存在：{draft.FolderPath}\\{value}");
    }

    private static string FieldDisplayName(string field) => field switch
    {
        nameof(DogSkinDraft.Head) => "狗头（Head）",
        nameof(DogSkinDraft.DefaultEars) => "默认耳朵（DefaultEars）",
        nameof(DogSkinDraft.DefaultEyes) => "默认眼睛（DefaultEyes）",
        nameof(DogSkinDraft.ClawLeftBack) => "左爪背面（Claw_Left_Back）",
        nameof(DogSkinDraft.ClawRightPalms) => "右爪掌面（Claw_Right_Palms）",
        nameof(DogSkinDraft.TongueRegular) => "常规舌头（Tongue_Regular）",
        nameof(DogSkinDraft.EarsHappy) => "开心耳朵（Ears_Happy）",
        nameof(DogSkinDraft.EarsPlane) => "放平耳朵（Ears_Plane）",
        nameof(DogSkinDraft.EyesBored) => "无聊眼睛（Eyes_Bored）",
        nameof(DogSkinDraft.EyesCute) => "可爱眼睛（Eyes_Cute）",
        nameof(DogSkinDraft.EyesHappy) => "开心眼睛（Eyes_Happy）",
        nameof(DogSkinDraft.EyesLucky) => "幸运眼睛（Eyes_Lucky）",
        nameof(DogSkinDraft.EyesNeutral) => "平静眼睛（Eyes_Neutral）",
        nameof(DogSkinDraft.EyesWink) => "眨眼眼睛（Eyes_Wink）",
        _ => $"字段“{field}”",
    };

    private string BuildCsv()
    {
        var rows = new List<string>
        {
            string.Join(',', CsvHeaders.Select(Csv)),
        };
        rows.AddRange(_catalog.DogSkins.OrderBy(d => d.Id).Select(draft => string.Join(',', new[]
        {
            draft.Id.ToString(), draft.Alias, draft.IconName, draft.DefaultEars, draft.DefaultEyes,
            draft.DefaultTongue, draft.FixedEyewear, draft.FolderPath, draft.Head,
            draft.ClawLeftBack, draft.ClawRightPalms, draft.TongueRegular, draft.EarsHappy,
            draft.EarsPlane, draft.EyesBored, draft.EyesCute, draft.EyesHappy,
            draft.EyesLucky, draft.EyesNeutral, draft.EyesWink,
        }.Select(Csv))));
        return string.Join("\r\n", rows) + "\r\n";
    }

    private static readonly string[] CsvHeaders =
    {
        "Id", "Alias", "IconName", "DefaultEars", "DefaultEyes", "DefaultTongue", "FixedEyewear",
        "FolderPath", "Head", "Claw_Left_Back", "Claw_Right_Palms", "Tongue_Regular",
        "Ears_Happy", "Ears_Plane", "Eyes_Bored", "Eyes_Cute", "Eyes_Happy",
        "Eyes_Lucky", "Eyes_Neutral", "Eyes_Wink",
    };

    private static string Csv(string value)
    {
        value ??= "";
        return value.ContainsAny(',', '"', '\r', '\n') ? $"\"{value.Replace("\"", "\"\"")}\"" : value;
    }

    private void ShowMessage(string title, string text)
    {
        var dialog = new AcceptDialog { Title = title, DialogText = text };
        dialog.Confirmed += dialog.QueueFree;
        dialog.Canceled += dialog.QueueFree;
        AddChild(dialog);
        dialog.PopupCentered(new Vector2I(680, 420));
    }

    private void ShowExportCompleted(string path)
    {
        var dialog = new AcceptDialog
        {
            Title = "导出完成",
            DialogText = $"已导出 {_catalog.DogSkins.Count} 条 DogSkin：\n{path}",
        };
        var openFolderButton = dialog.AddButton("打开所在文件夹", true);
        openFolderButton.Pressed += () =>
        {
            var result = OS.ShellShowInFileManager(path);
            if (result != Error.Ok)
            {
                SetStatus("无法打开 CSV 所在文件夹，请查看错误提示。", true);
                ShowMessage(
                    "打开文件夹失败",
                    $"Windows 文件管理器未能打开该路径：\n{path}\n\n系统错误代码：{(int)result}");
            }
        };
        dialog.Confirmed += dialog.QueueFree;
        dialog.Canceled += dialog.QueueFree;
        AddChild(dialog);
        dialog.PopupCentered(new Vector2I(680, 420));
    }

    private void SetStatus(string text, bool error)
    {
        if (_status == null) return;
        _status.Text = text;
        _status.Modulate = error ? new Color("ff9a91") : new Color("aeb8c7");
    }

    private static Button CreateButton(string text, Action action)
    {
        var button = new Button { Text = text, CustomMinimumSize = new Vector2(0, 36) };
        button.Pressed += action;
        return button;
    }

    private static Label CreateTitle(string text, int size)
    {
        var label = new Label { Text = text, VerticalAlignment = VerticalAlignment.Center };
        label.AddThemeFontSizeOverride("font_size", size);
        return label;
    }

    private static void AddSpacer(Control parent)
    {
        parent.AddChild(new Control { SizeFlagsHorizontal = SizeFlags.ExpandFill });
    }

    private static OptionButton CreateFilterOption(
        string allLabel,
        IEnumerable<string> values,
        Func<string, string> display = null)
    {
        var option = new OptionButton { CustomMinimumSize = new Vector2(210, 38) };
        option.AddItem(allLabel);
        option.SetItemMetadata(0, "");
        foreach (var value in values.Where(value => !string.IsNullOrEmpty(value)).Distinct().OrderBy(value => value))
        {
            option.AddItem(display?.Invoke(value) ?? value);
            option.SetItemMetadata(option.ItemCount - 1, value);
        }
        return option;
    }

    private static string SelectedFilter(OptionButton option, string allLabel)
    {
        if (option == null || option.Selected < 0) return "";
        return option.Selected == 0 ? "" : option.GetItemMetadata(option.Selected).AsString();
    }

    private static string SearchText(DogSkinDraft draft) => string.Join('|', new[]
    {
        draft.Id.ToString(), draft.Alias, draft.IconName, draft.FolderPath, draft.Head,
        draft.DefaultEyes, draft.DefaultEars, draft.FixedEyewear,
    });

    private static string CombinationKey(DogSkinDraft draft) => string.Join('|', new[]
    {
        draft.FolderPath,
        draft.Head,
        draft.DefaultEars,
        draft.DefaultEyes,
        draft.DefaultTongue,
        draft.FixedEyewear,
        draft.ClawLeftBack,
        draft.ClawRightPalms,
        draft.TongueRegular,
        draft.EarsHappy,
        draft.EarsPlane,
        draft.EyesBored,
        draft.EyesCute,
        draft.EyesHappy,
        draft.EyesLucky,
        draft.EyesNeutral,
        draft.EyesWink,
    });

    private static string DisplayEyewear(string value) =>
        string.IsNullOrEmpty(value) ? "（无眼镜）" : DisplayAssetName(value);

    private static string DisplayAssetName(string value) =>
        string.Equals(Path.GetExtension(value), ".png", StringComparison.OrdinalIgnoreCase)
            ? Path.GetFileNameWithoutExtension(value)
            : value;

    private static void ClearChildren(Node node)
    {
        foreach (var child in node.GetChildren())
        {
            node.RemoveChild(child);
            child.QueueFree();
        }
    }

    private static void SetFullRect(Control control, float margin = 0)
    {
        control.SetAnchorsPreset(LayoutPreset.FullRect);
        control.OffsetLeft = margin;
        control.OffsetTop = margin;
        control.OffsetRight = -margin;
        control.OffsetBottom = -margin;
    }
}

internal static class StringExtensions
{
    public static bool ContainsAny(this string value, params char[] characters)
        => value.IndexOfAny(characters) >= 0;
}
