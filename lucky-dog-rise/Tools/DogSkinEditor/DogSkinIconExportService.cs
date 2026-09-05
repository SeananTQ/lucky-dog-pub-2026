using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using DataTables;
using Godot;

namespace LuckyDogRise.Tools;

public sealed record DogSkinIconExportResult(
    string OutputDirectory,
    string ItemIconDirectory,
    string ItemPatchCsvPath,
    int IconCount,
    int ItemRowCount);

/// <summary>
/// Uses the runtime DogVisual to compose review-only dog item icons. Nothing is
/// written to Assets; accepted files are copied there manually after review.
/// </summary>
public partial class DogSkinIconExportService : Node
{
    public const string OutputResourceDirectory = "res://Tools/DogSkinEditor/output/IconComposer";
    public const string ItemIconResourceDirectory = OutputResourceDirectory + "/ItemIcon";
    public const string ItemPatchCsvResourcePath = OutputResourceDirectory + "/tbitem_dog_icon_patch.csv";
    public const string CardboardResourcePath = "res://Tools/DogSkinEditor/Assets/IconComposer/CardboardBox.png";

    private const int SourceCanvasSize = 1200;
    private const int OutputCanvasSize = 256;
    private const int SafeMargin = 8;
    private const int SafeContentSize = OutputCanvasSize - SafeMargin * 2;
    private const int DogTopMargin = 16;
    private const int TableOcclusionY = 677;
    private static readonly Vector2 DogCanvasOrigin = new(586f, 677f);
    private static readonly PackedScene DogAreaScene = GD.Load<PackedScene>("res://Scenes/Shared/DogArea.tscn");

    public async Task<Image> ComposeIconAsync(DogSkinDraft draft, float cardboardOffsetX)
    {
        ArgumentNullException.ThrowIfNull(draft);

        var cardboard = LoadSourceImage(CardboardResourcePath);
        if (cardboard.GetWidth() != SourceCanvasSize || cardboard.GetHeight() != SourceCanvasSize)
        {
            throw new InvalidDataException(
                $"纸箱素材必须是 {SourceCanvasSize}×{SourceCanvasSize}，当前为 "
                + $"{cardboard.GetWidth()}×{cardboard.GetHeight()}。");
        }

        var viewport = new SubViewport
        {
            Size = new Vector2I(SourceCanvasSize, SourceCanvasSize),
            TransparentBg = true,
            Disable3D = true,
            RenderTargetUpdateMode = SubViewport.UpdateMode.Always,
        };
        AddChild(viewport);

        try
        {
            var dog = DogAreaScene.Instantiate<DogVisual>();
            dog.Position = DogCanvasOrigin;
            dog.SetPreviewAppearance(draft.ToAppearanceSpec());
            viewport.AddChild(dog);

            var cardboardBack = new Sprite2D
            {
                Texture = ImageTexture.CreateFromImage(cardboard),
                Position = new Vector2(SourceCanvasSize / 2f + cardboardOffsetX, SourceCanvasSize / 2f),
                ZAsRelative = false,
                ZIndex = -1,
                Visible = false,
            };
            viewport.AddChild(cardboardBack);

            var cardboardFrontImage = (Image)cardboard.Duplicate();
            cardboardFrontImage.FillRect(
                new Rect2I(0, 0, SourceCanvasSize, TableOcclusionY),
                Colors.Transparent);
            var cardboardFront = new Sprite2D
            {
                Texture = ImageTexture.CreateFromImage(cardboardFrontImage),
                Position = new Vector2(SourceCanvasSize / 2f + cardboardOffsetX, SourceCanvasSize / 2f),
                ZAsRelative = false,
                // Same role as Main.tscn's Table: palm(1) < front(2) < back paw(3).
                ZIndex = 2,
                Visible = false,
            };
            viewport.AddChild(cardboardFront);

            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            dog.SetHitButtonEnabled(false);
            dog.ApplyReaction(EDogReactionTrigger.Default);
            dog.ShowInspectionClaws();

            await WaitForViewportFramesAsync();
            var dogOnly = viewport.GetTexture().GetImage();
            var dogBounds = dogOnly.GetUsedRect();
            if (dogBounds.Size.X <= 0 || dogBounds.Size.Y <= 0)
                throw new InvalidDataException($"DogSkin #{draft.Id} 没有渲染出可见像素。");

            cardboardBack.Visible = true;
            cardboardFront.Visible = true;
            await WaitForViewportFramesAsync();
            var combined = viewport.GetTexture().GetImage();
            return FitToItemIcon(combined, dogBounds);
        }
        finally
        {
            viewport.QueueFree();
        }
    }

    private async Task WaitForViewportFramesAsync()
    {
        // FramePostDraw can remain unsignalled when this standalone tool scene
        // is launched outside the editor bridge. Two process frames give the
        // RenderingServer time to submit and finish the SubViewport update.
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    }

    public async Task<DogSkinIconExportResult> ExportAllAsync(
        IReadOnlyCollection<DogSkinDraft> drafts,
        float cardboardOffsetX,
        Action<int, int, DogSkinDraft> progress = null)
    {
        ArgumentNullException.ThrowIfNull(drafts);
        var duplicateId = drafts.GroupBy(draft => draft.Id).FirstOrDefault(group => group.Count() > 1);
        if (duplicateId != null)
            throw new InvalidDataException($"DogSkin 编号 {duplicateId.Key} 重复，无法安全生成图标。");

        var outputDirectory = ProjectSettings.GlobalizePath(OutputResourceDirectory);
        var itemIconDirectory = ProjectSettings.GlobalizePath(ItemIconResourceDirectory);
        var itemPatchCsvPath = ProjectSettings.GlobalizePath(ItemPatchCsvResourcePath);
        Directory.CreateDirectory(itemIconDirectory);

        var orderedDrafts = drafts.OrderBy(draft => draft.Id).ToArray();
        for (var index = 0; index < orderedDrafts.Length; index++)
        {
            var draft = orderedDrafts[index];
            progress?.Invoke(index + 1, orderedDrafts.Length, draft);
            var image = await ComposeIconAsync(draft, cardboardOffsetX);
            var iconPath = Path.Combine(itemIconDirectory, IconFileName(draft.Id));
            var error = image.SavePng(iconPath);
            if (error != Error.Ok)
                throw new IOException($"保存 DogSkin #{draft.Id} 图标失败，Godot 错误代码：{(int)error}。");
        }

        var patchRows = BuildItemPatchRows(orderedDrafts);
        File.WriteAllText(itemPatchCsvPath, BuildItemPatchCsv(patchRows), new UTF8Encoding(true));
        return new DogSkinIconExportResult(
            outputDirectory,
            itemIconDirectory,
            itemPatchCsvPath,
            orderedDrafts.Length,
            patchRows.Count);
    }

    public static string IconFileName(int dogSkinId) => $"Dog_{dogSkinId}.png";

    private static Image LoadSourceImage(string resourcePath)
    {
        var path = ProjectSettings.GlobalizePath(resourcePath);
        if (!File.Exists(path))
            throw new FileNotFoundException("找不到图标合成所需的纸箱素材。", path);

        var image = Image.LoadFromFile(path);
        if (image == null || image.IsEmpty())
            throw new InvalidDataException($"无法读取纸箱素材：{path}");
        return image;
    }

    private static Image FitToItemIcon(Image combined, Rect2I dogBounds)
    {
        var availableDogHeight = OutputCanvasSize - DogTopMargin - SafeMargin;
        var scale = Math.Min(
            SafeContentSize / (float)dogBounds.Size.X,
            availableDogHeight / (float)dogBounds.Size.Y);
        var scaledWidth = Math.Max(1, Mathf.RoundToInt(combined.GetWidth() * scale));
        var scaledHeight = Math.Max(1, Mathf.RoundToInt(combined.GetHeight() * scale));
        combined.Resize(scaledWidth, scaledHeight, Image.Interpolation.Lanczos);

        var dogCenterX = (dogBounds.Position.X + dogBounds.Size.X / 2f) * scale;
        var cropX = Mathf.RoundToInt(dogCenterX - OutputCanvasSize / 2f);
        var cropY = Mathf.RoundToInt(dogBounds.Position.Y * scale - DogTopMargin);

        var output = Image.CreateEmpty(OutputCanvasSize, OutputCanvasSize, false, Image.Format.Rgba8);
        output.Fill(Colors.Transparent);
        BlitClipped(combined, output, cropX, cropY);

        // The outer eight pixels are always transparent. This is the safety
        // margin used by the rounded rarity frame in the item cell.
        output.FillRect(new Rect2I(0, 0, OutputCanvasSize, SafeMargin), Colors.Transparent);
        output.FillRect(new Rect2I(0, OutputCanvasSize - SafeMargin, OutputCanvasSize, SafeMargin), Colors.Transparent);
        output.FillRect(new Rect2I(0, SafeMargin, SafeMargin, SafeContentSize), Colors.Transparent);
        output.FillRect(new Rect2I(OutputCanvasSize - SafeMargin, SafeMargin, SafeMargin, SafeContentSize), Colors.Transparent);
        UnpremultiplyAlpha(output);
        return output;
    }

    /// <summary>
    /// SubViewport pixels use premultiplied alpha. PNG readers expect straight
    /// alpha, so saving the captured RGB values directly produces dark fringes
    /// when the icon is composited over a light rarity plate.
    /// </summary>
    private static void UnpremultiplyAlpha(Image image)
    {
        if (image.GetFormat() != Image.Format.Rgba8)
            image.Convert(Image.Format.Rgba8);

        var data = image.GetData();
        for (var offset = 0; offset < data.Length; offset += 4)
        {
            var alpha = data[offset + 3];
            if (alpha is 0 or 255)
                continue;

            data[offset] = UnpremultiplyChannel(data[offset], alpha);
            data[offset + 1] = UnpremultiplyChannel(data[offset + 1], alpha);
            data[offset + 2] = UnpremultiplyChannel(data[offset + 2], alpha);
        }

        image.SetData(image.GetWidth(), image.GetHeight(), false, Image.Format.Rgba8, data);
    }

    private static byte UnpremultiplyChannel(byte channel, byte alpha)
    {
        return (byte)Math.Min(255, (channel * 255 + alpha / 2) / alpha);
    }

    private static void BlitClipped(Image source, Image target, int cropX, int cropY)
    {
        var sourceX = Math.Max(0, cropX);
        var sourceY = Math.Max(0, cropY);
        var targetX = Math.Max(0, -cropX);
        var targetY = Math.Max(0, -cropY);
        var width = Math.Min(OutputCanvasSize - targetX, source.GetWidth() - sourceX);
        var height = Math.Min(OutputCanvasSize - targetY, source.GetHeight() - sourceY);
        if (width <= 0 || height <= 0)
            return;

        target.BlitRect(
            source,
            new Rect2I(sourceX, sourceY, width, height),
            new Vector2I(targetX, targetY));
    }

    private static List<ItemPatchRow> BuildItemPatchRows(IReadOnlyCollection<DogSkinDraft> drafts)
    {
        var draftsById = drafts.ToDictionary(draft => draft.Id);
        var rows = new List<ItemPatchRow>();
        foreach (var item in LubanData.Tables.TbItem.DataList.Where(item => item.SkinId > 0).OrderBy(item => item.Id))
        {
            if (!draftsById.TryGetValue(item.SkinId, out var draft))
            {
                throw new InvalidDataException(
                    $"Item #{item.Id} 引用了不存在的 DogSkin #{item.SkinId}，无法生成 tbitem 补丁 CSV。");
            }

            rows.Add(new ItemPatchRow(
                item.Id,
                item.SkinId,
                EnsureTrailingBackslash(draft.FolderPath),
                $"v0\\ItemIcon\\{IconFileName(draft.Id)}"));
        }
        return rows;
    }

    private static string BuildItemPatchCsv(IEnumerable<ItemPatchRow> rows)
    {
        var lines = new List<string> { "Id,SkinId,AssetPathList,IconPath" };
        lines.AddRange(rows.Select(row => string.Join(',', new[]
        {
            row.Id.ToString(),
            row.SkinId.ToString(),
            Csv(row.AssetPathList),
            Csv(row.IconPath),
        })));
        return string.Join("\r\n", lines) + "\r\n";
    }

    private static string EnsureTrailingBackslash(string value)
    {
        value = (value ?? "").Trim().TrimEnd('\\', '/');
        return string.IsNullOrEmpty(value) ? "" : value + "\\";
    }

    private static string Csv(string value)
    {
        value ??= "";
        return value.IndexOfAny([',', '"', '\r', '\n']) >= 0
            ? $"\"{value.Replace("\"", "\"\"")}\""
            : value;
    }

    private sealed record ItemPatchRow(int Id, int SkinId, string AssetPathList, string IconPath);
}
