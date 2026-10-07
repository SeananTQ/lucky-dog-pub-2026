#nullable enable
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Godot;

namespace LuckyDogRise.Tools;

/// <summary>
/// Captures deterministic frames supplied by the authoring scene, never the OS
/// window. The callback owns timing (frame / fps) and returns a NEW Image; this
/// exporter takes ownership and disposes it after saving. Invoke on Godot's
/// main thread, as rendering/capture callbacks use its scene tree.
/// </summary>
public static class StoreDialogueExport
{
    public static void ConfigureViewport(SubViewport viewport, StoreDialogueProject project)
    {
        var size = new Vector2I(project.CanvasWidth, project.CanvasHeight);
        var scale = (long)size.X * size.Y * 4 <= 8_388_608 ? 2 : 1;
        viewport.Size2DOverride = size;
        viewport.Size = size * scale;
    }

    public static Image CaptureFrame(SubViewport viewport, StoreDialogueProject project)
    {
        var image = viewport.GetTexture().GetImage();
        // Resize premultiplied pixels before converting to straight alpha.
        if (image.GetWidth() != project.CanvasWidth || image.GetHeight() != project.CanvasHeight)
            image.Resize(project.CanvasWidth, project.CanvasHeight, Image.Interpolation.Bilinear);
        return image;
    }

    public static void ValidateOptions(StoreDialogueExportOptions options) => StoreDialogueExportCore.ValidateOptions(options);
    public static string DiscoverFfmpegPath(string explicitPath = "") => StoreDialogueExportCore.DiscoverFfmpegPath(explicitPath);

    public static Task<StoreDialogueExportResult> ExportAsync(
        StoreDialogueExportOptions options,
        Func<int, CancellationToken, Task<Image>> renderFrame,
        IProgress<StoreDialogueExportProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(renderFrame);
        return StoreDialogueExportCore.ExportAsync(options, async (frame, png, token) =>
        {
            using var image = await renderFrame(frame, token);
            token.ThrowIfCancellationRequested();
            if (image == null || image.IsEmpty() || image.GetWidth() != options.Width || image.GetHeight() != options.Height)
                throw new InvalidDataException($"第 {frame + 1} 帧尺寸必须是 {options.Width}×{options.Height}。");
            image.Convert(Image.Format.Rgba8);
            if (options.InputAlpha == StoreDialogueAlphaMode.Premultiplied)
            {
                var bytes = image.GetData();
                StoreDialogueExportCore.UnpremultiplyRgba(bytes);
                image.SetData(options.Width, options.Height, false, Image.Format.Rgba8, bytes);
            }
            var error = image.SavePng(png);
            if (error != Error.Ok)
                throw new IOException($"保存第 {frame + 1} 帧失败，Godot 错误：{error}。");
        }, progress, cancellationToken);
    }
}
