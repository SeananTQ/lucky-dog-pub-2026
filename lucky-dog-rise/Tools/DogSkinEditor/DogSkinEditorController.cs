using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using DataTables;
using Godot;

namespace LuckyDogRise.Tools;

public partial class DogSkinEditorController : Control
{
    private const string DraftResourcePath = "res://Tools/DogSkinEditor/output/DogSkinCatalogDraft.json";
    private const string CsvResourcePath = "res://Tools/DogSkinEditor/output/DogSkin.csv";
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private static readonly PackedScene DogAreaScene = GD.Load<PackedScene>("res://Scenes/DogArea.tscn");

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
    private DogSkinDraft _editingDraft = null!;
    private DogVisual _editorPreview = null!;
    private EDogReactionTrigger _editorReaction = EDogReactionTrigger.Default;

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
        ClearChildren(_content);

        var body = new VBoxContainer();
        body.AddThemeConstantOverride("separation", 10);
        _content.AddChild(body);

        var filterRow = new HBoxContainer();
        filterRow.AddThemeConstantOverride("separation", 8);
        body.AddChild(filterRow);

        _searchInput = new LineEdit
        {
            PlaceholderText = "搜索 ID、图标名、目录或素材名",
            CustomMinimumSize = new Vector2(320, 38),
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
        };
        _searchInput.TextSubmitted += _ => RefreshOverviewGrid();
        filterRow.AddChild(_searchInput);
        filterRow.AddChild(CreateButton("筛选", RefreshOverviewGrid));

        _headFilter = CreateFilterOption("全部头型", _catalog.DogSkins.Select(d => d.Head));
        _headFilter.ItemSelected += _ => RefreshOverviewGrid();
        filterRow.AddChild(_headFilter);
        _eyewearFilter = CreateFilterOption("全部眼镜", _catalog.DogSkins.Select(d => DisplayEyewear(d.FixedEyewear)));
        _eyewearFilter.ItemSelected += _ => RefreshOverviewGrid();
        filterRow.AddChild(_eyewearFilter);

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
        var duplicateCombinationCount = _catalog.DogSkins
            .GroupBy(CombinationKey)
            .Count(group => group.Count() > 1);

        var visible = _catalog.DogSkins
            .Where(d => string.IsNullOrEmpty(head) || d.Head == head)
            .Where(d => string.IsNullOrEmpty(eyewear) || DisplayEyewear(d.FixedEyewear) == eyewear)
            .Where(d => string.IsNullOrEmpty(query) || SearchText(d).Contains(query, StringComparison.OrdinalIgnoreCase))
            .OrderBy(d => d.Id)
            .ToArray();

        _overviewSummary.Text = $"共 {_catalog.DogSkins.Count} 只，当前显示 {visible.Length} 只；"
            + $"头型 {_catalog.DogSkins.Select(d => d.Head).Distinct().Count()} 种，"
            + $"眼镜 {_catalog.DogSkins.Select(d => DisplayEyewear(d.FixedEyewear)).Distinct().Count()} 种，"
            + $"重复组合 {duplicateCombinationCount} 组。";

        foreach (var draft in visible)
            _overviewGrid.AddChild(CreateDogCard(draft, headCounts, eyewearCounts));
    }

    private Control CreateDogCard(
        DogSkinDraft draft,
        IReadOnlyDictionary<string, int> headCounts,
        IReadOnlyDictionary<string, int> eyewearCounts)
    {
        var card = new PanelContainer
        {
            CustomMinimumSize = new Vector2(230, 252),
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
        };
        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", 4);
        card.AddChild(box);
        box.AddChild(CreateDogViewport(draft, thumbnail: true));
        box.AddChild(new Label
        {
            Text = $"#{draft.Id}  {draft.IconName}",
            HorizontalAlignment = HorizontalAlignment.Center,
            TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis,
        });
        box.AddChild(new Label
        {
            Text = $"{draft.Head} ×{headCounts.GetValueOrDefault(draft.Head)}\n"
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
            _editorPreview?.ApplyReaction(_editorReaction);
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
        form.AddThemeConstantOverride("separation", 7);
        editorScroll.AddChild(form);
        form.AddChild(CreateTitle("DogSkin 草稿", 18));

        AddIdField(form, draft);
        AddTextField(form, "IconName", draft.IconName, value => draft.IconName = value);
        AddChoiceField(form, "FolderPath", draft.FolderPath, _assets.FolderPaths, value =>
        {
            draft.FolderPath = value;
            ShowEditor(draft);
        });
        AddChoiceField(form, "Head", draft.Head, _assets.GetFiles(draft.FolderPath, "Head_"), value => draft.Head = value);
        AddChoiceField(form, "DefaultEars", draft.DefaultEars, _assets.GetFiles(draft.FolderPath, "Ears_"), value => draft.DefaultEars = value);
        AddChoiceField(form, "DefaultEyes", draft.DefaultEyes, _assets.GetFiles(draft.FolderPath, "Eyes_"), value => draft.DefaultEyes = value);
        AddEyewearPickerField(form, draft);
        AddTextField(form, "DefaultTongue", draft.DefaultTongue, value => draft.DefaultTongue = value);
        AddChoiceField(form, "Claw_Left_Back", draft.ClawLeftBack, _assets.GetFiles(draft.FolderPath, "Claw_"), value => draft.ClawLeftBack = value);
        AddChoiceField(form, "Claw_Right_Palms", draft.ClawRightPalms, _assets.GetFiles(draft.FolderPath, "Claw_"), value => draft.ClawRightPalms = value);
        AddChoiceField(form, "Tongue_Regular", draft.TongueRegular, _assets.GetFiles(draft.FolderPath, "Tongue_"), value => draft.TongueRegular = value);
        AddChoiceField(form, "Ears_Happy", draft.EarsHappy, _assets.GetFiles(draft.FolderPath, "Ears_"), value => draft.EarsHappy = value);
        AddChoiceField(form, "Ears_Plane", draft.EarsPlane, _assets.GetFiles(draft.FolderPath, "Ears_"), value => draft.EarsPlane = value);
        AddChoiceField(form, "Eyes_Bored", draft.EyesBored, _assets.GetFiles(draft.FolderPath, "Eyes_"), value => draft.EyesBored = value);
        AddChoiceField(form, "Eyes_Cute", draft.EyesCute, _assets.GetFiles(draft.FolderPath, "Eyes_"), value => draft.EyesCute = value);
        AddChoiceField(form, "Eyes_Happy", draft.EyesHappy, _assets.GetFiles(draft.FolderPath, "Eyes_"), value => draft.EyesHappy = value);
        AddChoiceField(form, "Eyes_Lucky", draft.EyesLucky, _assets.GetFiles(draft.FolderPath, "Eyes_"), value => draft.EyesLucky = value);
        AddChoiceField(form, "Eyes_Neutral", draft.EyesNeutral, _assets.GetFiles(draft.FolderPath, "Eyes_"), value => draft.EyesNeutral = value);
        AddChoiceField(form, "Eyes_Wink", draft.EyesWink, _assets.GetFiles(draft.FolderPath, "Eyes_"), value => draft.EyesWink = value);

        var actionRow = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.End };
        actionRow.AddChild(CreateButton("仅保存草稿", SaveCatalog));
        actionRow.AddChild(CreateButton("保存并返回总览", () =>
        {
            SaveCatalog();
            ShowOverview();
        }));
        form.AddChild(actionRow);
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

        var dog = DogAreaScene.Instantiate<DogVisual>();
        dog.SetPreviewAppearance(draft.ToAppearanceSpec());
        viewport.AddChild(dog);
        dog.SetHitButtonEnabled(false);
        dog.CallDeferred(nameof(DogVisual.SetHitButtonEnabled), false);
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
        }
        container.Resized += UpdatePreviewTransform;
        Callable.From(UpdatePreviewTransform).CallDeferred();
        if (!thumbnail)
        {
            _editorPreview = dog;
            dog.ApplyReaction(_editorReaction);
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
            option.AddItem(string.IsNullOrEmpty(choices[i]) ? emptyLabel : choices[i]);
            option.SetItemMetadata(i, choices[i]);
            if (string.Equals(choices[i], value, StringComparison.OrdinalIgnoreCase))
                option.Select(i);
        }
        option.ItemSelected += index =>
        {
            setter(option.GetItemMetadata((int)index).AsString());
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
        _editorPreview.ApplyReaction(_editorReaction);
    }

    private void CreateNewDraft()
    {
        var nextId = NextId();
        var template = _catalog.DogSkins.FirstOrDefault();
        var draft = template?.CloneWithId(nextId) ?? new DogSkinDraft { Id = nextId };
        draft.IconName = $"DogSkin_{nextId}.png";
        _catalog.DogSkins.Add(draft);
        ShowEditor(draft);
        SetStatus($"已新建 DogSkin #{nextId}，尚未写入草稿文件。", false);
    }

    private void CloneDraft(DogSkinDraft source)
    {
        var draft = source.CloneWithId(NextId());
        draft.IconName = Path.GetFileNameWithoutExtension(source.IconName) + "_Copy.png";
        _catalog.DogSkins.Add(draft);
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
                return;
            }
        }
        catch (Exception exception)
        {
            GD.PushWarning($"[DogSkinEditor] Draft load failed: {exception.Message}");
        }
        _catalog = CreateCatalogFromTable();
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
        try
        {
            _catalog.UpdatedAtUtc = DateTime.UtcNow;
            var path = ProjectSettings.GlobalizePath(DraftResourcePath);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var tempPath = path + ".tmp";
            File.WriteAllText(tempPath, JsonSerializer.Serialize(_catalog, JsonOptions), new UTF8Encoding(false));
            File.Move(tempPath, path, true);
            SetStatus($"草稿已保存：{path}", false);
        }
        catch (Exception exception)
        {
            SetStatus($"草稿保存失败：{exception.Message}", true);
            ShowMessage("草稿保存失败", exception.Message);
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

        try
        {
            var path = ProjectSettings.GlobalizePath(CsvResourcePath);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, BuildCsv(), new UTF8Encoding(true));
            SetStatus($"完整 CSV 已导出：{path}", false);
            ShowMessage("导出完成", $"已导出 {_catalog.DogSkins.Count} 条 DogSkin：\n{path}");
        }
        catch (Exception exception)
        {
            SetStatus($"CSV 导出失败：{exception.Message}", true);
            ShowMessage("CSV 导出失败", exception.Message);
        }
    }

    private List<string> ValidateCatalog()
    {
        var errors = new List<string>();
        foreach (var duplicate in _catalog.DogSkins.GroupBy(d => d.Id).Where(group => group.Count() > 1))
            errors.Add($"Id {duplicate.Key} 重复。 ");

        foreach (var draft in _catalog.DogSkins)
        {
            if (string.IsNullOrWhiteSpace(draft.IconName)) errors.Add($"#{draft.Id} 缺少 IconName。");
            if (string.IsNullOrWhiteSpace(draft.FolderPath)) errors.Add($"#{draft.Id} 缺少 FolderPath。");
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
                errors.Add($"#{draft.Id} FixedEyewear 不存在：{draft.FixedEyewear}");
        }
        return errors;
    }

    private void ValidateDogAsset(List<string> errors, DogSkinDraft draft, string field, string value)
    {
        if (!_assets.ResourceExists(draft.FolderPath, value))
            errors.Add($"#{draft.Id} {field} 不存在：{draft.FolderPath}\\{value}");
    }

    private string BuildCsv()
    {
        var rows = new List<string>
        {
            string.Join(',', CsvHeaders.Select(Csv)),
        };
        rows.AddRange(_catalog.DogSkins.OrderBy(d => d.Id).Select(draft => string.Join(',', new[]
        {
            draft.Id.ToString(), draft.IconName, draft.DefaultEars, draft.DefaultEyes,
            draft.DefaultTongue, draft.FixedEyewear, draft.FolderPath, draft.Head,
            draft.ClawLeftBack, draft.ClawRightPalms, draft.TongueRegular, draft.EarsHappy,
            draft.EarsPlane, draft.EyesBored, draft.EyesCute, draft.EyesHappy,
            draft.EyesLucky, draft.EyesNeutral, draft.EyesWink,
        }.Select(Csv))));
        return string.Join("\r\n", rows) + "\r\n";
    }

    private static readonly string[] CsvHeaders =
    {
        "Id", "IconName", "DefaultEars", "DefaultEyes", "DefaultTongue", "FixedEyewear",
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

    private static OptionButton CreateFilterOption(string allLabel, IEnumerable<string> values)
    {
        var option = new OptionButton { CustomMinimumSize = new Vector2(210, 38) };
        option.AddItem(allLabel);
        foreach (var value in values.Where(value => !string.IsNullOrEmpty(value)).Distinct().OrderBy(value => value))
            option.AddItem(value);
        return option;
    }

    private static string SelectedFilter(OptionButton option, string allLabel)
    {
        if (option == null || option.Selected < 0) return "";
        var value = option.GetItemText(option.Selected);
        return value == allLabel ? "" : value;
    }

    private static string SearchText(DogSkinDraft draft) => string.Join('|', new[]
    {
        draft.Id.ToString(), draft.IconName, draft.FolderPath, draft.Head,
        draft.DefaultEyes, draft.DefaultEars, draft.FixedEyewear,
    });

    private static string CombinationKey(DogSkinDraft draft) => string.Join('|', new[]
    {
        draft.FolderPath, draft.Head, draft.DefaultEyes, draft.DefaultEars, draft.FixedEyewear,
    });

    private static string DisplayEyewear(string value) => string.IsNullOrEmpty(value) ? "（无眼镜）" : value;

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
