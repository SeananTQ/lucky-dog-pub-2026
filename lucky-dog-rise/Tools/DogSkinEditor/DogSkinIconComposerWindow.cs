using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using Godot;

namespace LuckyDogRise.Tools;

public partial class DogSkinIconComposerWindow : Window
{
    private const string SettingsResourcePath = "res://Tools/DogSkinEditor/output/IconComposer/settings.json";
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private DogSkinIconExportService _exportService = null!;
    private IReadOnlyList<DogSkinDraft> _drafts = Array.Empty<DogSkinDraft>();
    private OptionButton _dogOption = null!;
    private SpinBox _offsetInput = null!;
    private TextureRect _preview = null!;
    private TextureRect _rarePreviewIcon = null!;
    private TextureRect _epicPreviewIcon = null!;
    private Label _status = null!;
    private Button _refreshButton = null!;
    private Button _exportButton = null!;
    private int _previewRequestVersion;
    private bool _busy;

    public void Initialize(
        DogSkinIconExportService exportService,
        IReadOnlyList<DogSkinDraft> drafts,
        Theme editorTheme)
    {
        _exportService = exportService;
        _drafts = drafts.OrderBy(draft => draft.Id).ToArray();

        Title = "DogSkin 道具图标合成";
        Size = new Vector2I(1040, 760);
        MinSize = new Vector2I(820, 620);
        Transient = true;
        Exclusive = true;
        Unresizable = false;
        CloseRequested += QueueFree;

        var settings = LoadSettings();
        var margin = new MarginContainer { Theme = editorTheme };
        margin.AddThemeConstantOverride("margin_left", 14);
        margin.AddThemeConstantOverride("margin_top", 14);
        margin.AddThemeConstantOverride("margin_right", 14);
        margin.AddThemeConstantOverride("margin_bottom", 14);
        SetFullRect(margin);
        AddChild(margin);

        var root = new VBoxContainer();
        root.AddThemeConstantOverride("separation", 10);
        margin.AddChild(root);

        var title = new Label { Text = "DogSkin 道具图标合成", VerticalAlignment = VerticalAlignment.Center };
        title.AddThemeFontSizeOverride("font_size", 22);
        root.AddChild(title);
        root.AddChild(new Label
        {
            Text = "图片只输出到工具目录。审阅通过后，再手动复制到 Assets/v0/ItemIcon。",
            Modulate = new Color("aeb8c7"),
        });

        var controls = new HBoxContainer();
        controls.AddThemeConstantOverride("separation", 8);
        root.AddChild(controls);
        controls.AddChild(new Label { Text = "预览狗狗：", VerticalAlignment = VerticalAlignment.Center });
        _dogOption = new OptionButton { CustomMinimumSize = new Vector2(260, 38) };
        foreach (var draft in _drafts)
            _dogOption.AddItem($"#{draft.Id}  {draft.Alias}", draft.Id);
        _dogOption.ItemSelected += _ => RefreshPreviewAsync();
        controls.AddChild(_dogOption);

        controls.AddChild(new Label { Text = "纸箱水平偏移：", VerticalAlignment = VerticalAlignment.Center });
        _offsetInput = new SpinBox
        {
            MinValue = -300,
            MaxValue = 300,
            Step = 1,
            Value = settings.CardboardOffsetX,
            AllowGreater = true,
            AllowLesser = true,
            Suffix = " px（1200 画布）",
            CustomMinimumSize = new Vector2(220, 38),
        };
        _offsetInput.ValueChanged += _ => RefreshPreviewAsync();
        controls.AddChild(_offsetInput);
        _refreshButton = CreateButton("刷新预览", RefreshPreviewAsync);
        controls.AddChild(_refreshButton);

        var previews = new HBoxContainer
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
        };
        previews.AddThemeConstantOverride("separation", 12);
        root.AddChild(previews);

        var smallPreviewPanel = new PanelContainer
        {
            CustomMinimumSize = new Vector2(230, 0),
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
        };
        previews.AddChild(smallPreviewPanel);
        var smallPreviewColumn = new VBoxContainer
        {
            Alignment = BoxContainer.AlignmentMode.Center,
        };
        smallPreviewColumn.AddThemeConstantOverride("separation", 12);
        smallPreviewPanel.AddChild(smallPreviewColumn);
        var smallPreviewTitle = new Label
        {
            Text = "背包实机尺寸预览",
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        smallPreviewTitle.AddThemeFontSizeOverride("font_size", 16);
        smallPreviewColumn.AddChild(smallPreviewTitle);

        var rarityPreviews = new HBoxContainer
        {
            Alignment = BoxContainer.AlignmentMode.Center,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        };
        rarityPreviews.AddThemeConstantOverride("separation", 12);
        smallPreviewColumn.AddChild(rarityPreviews);
        rarityPreviews.AddChild(CreateRarityPreview("Rare", "蓝色 · 稀有", out _rarePreviewIcon));
        rarityPreviews.AddChild(CreateRarityPreview("Epic", "紫色 · 史诗", out _epicPreviewIcon));

        smallPreviewColumn.AddChild(new Label
        {
            Text = "90×90 · 与背包道具格层级一致",
            HorizontalAlignment = HorizontalAlignment.Center,
            Modulate = new Color("aeb8c7"),
        });

        var detailPanel = new PanelContainer
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
        };
        previews.AddChild(detailPanel);
        var detailColumn = new VBoxContainer();
        detailColumn.AddThemeConstantOverride("separation", 6);
        detailPanel.AddChild(detailColumn);
        var detailTitle = new Label
        {
            Text = "原图细节预览（不含道具框）",
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        detailTitle.AddThemeFontSizeOverride("font_size", 16);
        detailColumn.AddChild(detailTitle);
        _preview = new TextureRect
        {
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            TextureFilter = CanvasItem.TextureFilterEnum.Linear,
            CustomMinimumSize = new Vector2(480, 480),
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        detailColumn.AddChild(_preview);

        _status = new Label
        {
            Text = "准备生成预览……",
            Modulate = new Color("aeb8c7"),
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        };
        root.AddChild(_status);

        var actions = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.End };
        actions.AddThemeConstantOverride("separation", 8);
        root.AddChild(actions);
        actions.AddChild(CreateButton("关闭", QueueFree));
        _exportButton = CreateButton("生成全部图标与 tbitem CSV", ExportAllAsync);
        actions.AddChild(_exportButton);

        Callable.From(RefreshPreviewAsync).CallDeferred();
    }

    private async void RefreshPreviewAsync()
    {
        if (_busy || _drafts.Count == 0 || _dogOption == null || _dogOption.Selected < 0)
            return;

        var requestVersion = ++_previewRequestVersion;
        var draft = _drafts[_dogOption.Selected];
        SetStatus($"正在生成 DogSkin #{draft.Id} 预览……", false);
        _refreshButton.Disabled = true;
        try
        {
            var image = await _exportService.ComposeIconAsync(draft, (float)_offsetInput.Value);
            if (requestVersion != _previewRequestVersion || !IsInstanceValid(this))
                return;

            var texture = ImageTexture.CreateFromImage(image);
            _preview.Texture = texture;
            _rarePreviewIcon.Texture = texture;
            _epicPreviewIcon.Texture = texture;
            SetStatus(
                $"预览：{DogSkinIconExportService.IconFileName(draft.Id)}。"
                + "外围 8 像素已清空；纸箱水平偏移为全局参数。",
                false);
        }
        catch (Exception exception)
        {
            GD.PushError($"[DogSkin 图标合成] 预览生成失败：{exception}");
            SetStatus(DescribeError(exception, "生成预览"), true);
        }
        finally
        {
            if (IsInstanceValid(_refreshButton))
                _refreshButton.Disabled = false;
        }
    }

    private async void ExportAllAsync()
    {
        if (_busy || _drafts.Count == 0)
            return;

        _busy = true;
        _previewRequestVersion++;
        SetControlsDisabled(true);
        var offset = (float)_offsetInput.Value;
        try
        {
            SaveSettings(new IconComposerSettings { CardboardOffsetX = offset });
            var result = await _exportService.ExportAllAsync(
                _drafts,
                offset,
                (current, total, draft) => SetStatus(
                    $"正在生成 {current}/{total}：DogSkin #{draft.Id} {draft.Alias}",
                    false));

            SetStatus(
                $"已生成 {result.IconCount} 张图标和 {result.ItemRowCount} 条 tbitem 补丁数据。",
                false);
            ShowCompleted(result);
        }
        catch (Exception exception)
        {
            GD.PushError($"[DogSkin 图标合成] 批量生成失败：{exception}");
            SetStatus(DescribeError(exception, "批量生成"), true);
            ShowMessage("图标生成失败", DescribeError(exception, "批量生成"));
        }
        finally
        {
            _busy = false;
            SetControlsDisabled(false);
        }
    }

    private void ShowCompleted(DogSkinIconExportResult result)
    {
        var dialog = new AcceptDialog
        {
            Title = "图标与 CSV 已生成",
            DialogText = $"已生成 {result.IconCount} 张 DogSkin 图标。\n"
                + $"tbitem 补丁 CSV 共 {result.ItemRowCount} 行。\n\n"
                + $"图标：{result.ItemIconDirectory}\n"
                + $"CSV：{result.ItemPatchCsvPath}\n\n"
                + "确认图标后，请手动复制到项目的 Assets/v0/ItemIcon。",
        };
        var openFolderButton = dialog.AddButton("打开输出文件夹", true);
        openFolderButton.Pressed += () => OpenOutputFolder(result.OutputDirectory);
        dialog.Confirmed += dialog.QueueFree;
        dialog.Canceled += dialog.QueueFree;
        AddChild(dialog);
        dialog.PopupCentered(new Vector2I(760, 420));
    }

    private void OpenOutputFolder(string path)
    {
        var result = OS.ShellShowInFileManager(path);
        if (result == Error.Ok)
            return;

        ShowMessage(
            "打开文件夹失败",
            $"Windows 文件管理器未能打开该路径：\n{path}\n\n系统错误代码：{(int)result}");
    }

    private static IconComposerSettings LoadSettings()
    {
        var path = ProjectSettings.GlobalizePath(SettingsResourcePath);
        try
        {
            return File.Exists(path)
                ? JsonSerializer.Deserialize<IconComposerSettings>(File.ReadAllText(path), JsonOptions)
                    ?? new IconComposerSettings()
                : new IconComposerSettings();
        }
        catch (Exception exception)
        {
            GD.PushWarning($"[DogSkin 图标合成] 配置读取失败，将使用默认值。技术详情：{exception.Message}");
            return new IconComposerSettings();
        }
    }

    private static void SaveSettings(IconComposerSettings settings)
    {
        var path = ProjectSettings.GlobalizePath(SettingsResourcePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var tempPath = path + ".tmp";
        File.WriteAllText(tempPath, JsonSerializer.Serialize(settings, JsonOptions), new UTF8Encoding(false));
        File.Move(tempPath, path, true);
    }

    private void SetControlsDisabled(bool disabled)
    {
        _dogOption.Disabled = disabled;
        _offsetInput.Editable = !disabled;
        _refreshButton.Disabled = disabled;
        _exportButton.Disabled = disabled;
    }

    private void SetStatus(string text, bool error)
    {
        if (_status == null)
            return;
        _status.Text = text;
        _status.Modulate = error ? new Color("ff9a91") : new Color("aeb8c7");
    }

    private void ShowMessage(string title, string text)
    {
        var dialog = new AcceptDialog { Title = title, DialogText = text };
        dialog.Confirmed += dialog.QueueFree;
        dialog.Canceled += dialog.QueueFree;
        AddChild(dialog);
        dialog.PopupCentered(new Vector2I(720, 360));
    }

    private static string DescribeError(Exception exception, string action) => exception switch
    {
        FileNotFoundException => $"{action}失败：找不到纸箱素材。请检查 CardboardBox.png 是否仍在工具素材目录。",
        UnauthorizedAccessException => $"{action}失败：没有权限写入工具输出目录。",
        IOException => $"{action}失败：Windows 无法读写输出文件。请关闭占用 CSV 或 PNG 的程序后重试。",
        InvalidDataException => $"{action}失败：{exception.Message}",
        _ => $"{action}失败。请查看 Godot 日志获取技术详情。",
    };

    private static Button CreateButton(string text, Action action)
    {
        var button = new Button { Text = text, CustomMinimumSize = new Vector2(0, 36) };
        button.Pressed += action;
        return button;
    }

    private static TextureRect CreateSmallPreviewLayer()
    {
        var layer = new TextureRect
        {
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            TextureFilter = CanvasItem.TextureFilterEnum.Linear,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        SetFullRect(layer);
        return layer;
    }

    private static VBoxContainer CreateRarityPreview(
        string rarityName,
        string label,
        out TextureRect iconLayer)
    {
        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", 5);

        var cell = new Control
        {
            CustomMinimumSize = new Vector2(90, 90),
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        column.AddChild(cell);

        var plate = CreateSmallPreviewLayer();
        plate.Texture = GD.Load<Texture2D>($"res://Assets/UI/ItemUI/Plate_{rarityName}.png");
        cell.AddChild(plate);

        iconLayer = CreateSmallPreviewLayer();
        cell.AddChild(iconLayer);

        var frame = CreateSmallPreviewLayer();
        frame.Texture = GD.Load<Texture2D>($"res://Assets/UI/ItemUI/Frame_{rarityName}.png");
        cell.AddChild(frame);

        column.AddChild(new Label
        {
            Text = label,
            HorizontalAlignment = HorizontalAlignment.Center,
            Modulate = new Color("aeb8c7"),
        });
        return column;
    }

    private static void SetFullRect(Control control)
    {
        control.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        control.OffsetLeft = 0;
        control.OffsetTop = 0;
        control.OffsetRight = 0;
        control.OffsetBottom = 0;
    }

    private sealed class IconComposerSettings
    {
        public float CardboardOffsetX { get; set; }
    }
}
