#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace LuckyDogRise.Tools;

public enum StoreDialogueExportFormat { WebM, WebP, PngSequence }
public enum StoreDialogueAlphaMode { Straight, Premultiplied }

public sealed record StoreDialogueExportOptions
{
    public string OutputPath { get; init; } = "";
    public StoreDialogueExportFormat Format { get; init; } = StoreDialogueExportFormat.WebM;
    public int Width { get; init; }
    public int Height { get; init; }
    public int FramesPerSecond { get; init; } = 30;
    public int FrameCount { get; init; }
    public string FfmpegPath { get; init; } = "";
    public int WebmCrf { get; init; } = 18;
    public bool WebmLossless { get; init; }
    // PNG stores straight alpha. Raw Godot transparent viewport captures may
    // be premultiplied; the Image adapter converts them when explicitly asked.
    public StoreDialogueAlphaMode InputAlpha { get; init; } = StoreDialogueAlphaMode.Straight;
}

public sealed record StoreDialogueExportProgress(string Stage, int CompletedFrames, int TotalFrames);
public sealed record StoreDialogueExportResult(
    string OutputPath, StoreDialogueExportFormat Format, int FrameCount, int FramesPerSecond)
{
    public double DurationSeconds => (double)FrameCount / FramesPerSecond;
}

/// <summary>
/// Engine-independent file/encoder pipeline. Its frame callback must write one
/// straight-alpha RGBA PNG to the supplied private staging path. No shell,
/// player state, catalog files or imported game translations are involved.
/// </summary>
public static class StoreDialogueExportCore
{
    public static void ValidateOptions(StoreDialogueExportOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (options.WebmCrf is < 0 or > 63)
            throw new ArgumentException("WebM CRF must be between 0 and 63.", nameof(options));
        if (string.IsNullOrWhiteSpace(options.OutputPath) || !Path.IsPathFullyQualified(options.OutputPath))
            throw new ArgumentException("导出位置必须是完整路径。", nameof(options));
        if (!Enum.IsDefined(options.Format) || !Enum.IsDefined(options.InputAlpha))
            throw new ArgumentException("不支持的导出格式或透明通道类型。", nameof(options));
        if (options.Width <= 0 || options.Height <= 0 || options.Width > 8192 || options.Height > 8192)
            throw new ArgumentException("导出宽高必须在 1～8192 像素之间。", nameof(options));
        if (options.FramesPerSecond < 1 || options.FramesPerSecond > 120 || options.FrameCount < 1)
            throw new ArgumentException("帧率必须在 1～120 之间，且至少包含一帧。", nameof(options));

        var output = Path.GetFullPath(options.OutputPath);
        if (string.IsNullOrEmpty(Path.GetFileName(output)))
            throw new ArgumentException("请指定导出文件名或新的帧序列文件夹。", nameof(options));
        if (File.Exists(output) || Directory.Exists(output))
            throw new IOException("导出位置已经存在，请选择新文件名，避免覆盖已有作品。");
        var expectedExtension = options.Format switch
        {
            StoreDialogueExportFormat.WebM => ".webm",
            StoreDialogueExportFormat.WebP => ".webp",
            _ => "",
        };
        if (expectedExtension.Length > 0 &&
            !string.Equals(Path.GetExtension(output), expectedExtension, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException($"所选格式的文件名必须以 {expectedExtension} 结尾。", nameof(options));
    }

    /// <summary>Explicit executable, then FFMPEG_PATH, then PATH. Never silently ignores a bad explicit path.</summary>
    public static string DiscoverFfmpegPath(string explicitPath = "")
    {
        if (!string.IsNullOrWhiteSpace(explicitPath)) return ResolveExecutable(explicitPath);
        var configured = Environment.GetEnvironmentVariable("FFMPEG_PATH");
        if (!string.IsNullOrWhiteSpace(configured)) return ResolveExecutable(configured);
        var executableName = OperatingSystem.IsWindows() ? "ffmpeg.exe" : "ffmpeg";
        foreach (var directory in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator))
        {
            if (string.IsNullOrWhiteSpace(directory)) continue;
            try
            {
                var candidate = Path.Combine(directory.Trim().Trim('"'), executableName);
                if (File.Exists(candidate)) return Path.GetFullPath(candidate);
            }
            catch (ArgumentException) { }
        }
        throw new FileNotFoundException("未找到 FFmpeg。请在工具中指定 ffmpeg 可执行文件，或选择 PNG 帧序列导出。");
    }

    private static string ResolveExecutable(string path)
    {
        var resolved = Path.GetFullPath(path.Trim().Trim('"'));
        if (!File.Exists(resolved))
            throw new FileNotFoundException("指定的 FFmpeg 可执行文件不存在。", resolved);
        return resolved;
    }

    public static async Task<StoreDialogueExportResult> ExportAsync(
        StoreDialogueExportOptions options,
        Func<int, string, CancellationToken, Task> writePngFrame,
        IProgress<StoreDialogueExportProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ValidateOptions(options);
        ArgumentNullException.ThrowIfNull(writePngFrame);
        cancellationToken.ThrowIfCancellationRequested();
        // Fail before rendering a potentially long sequence when the selected
        // encoder is unavailable. PNG sequences do not require FFmpeg.
        var ffmpeg = options.Format == StoreDialogueExportFormat.PngSequence
            ? "" : DiscoverFfmpegPath(options.FfmpegPath);
        var output = Path.GetFullPath(options.OutputPath);
        var parent = Path.GetDirectoryName(output)!;
        Directory.CreateDirectory(parent);
        var staging = Path.Combine(parent, ".store-dialogue-export-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(staging);
        var frames = Path.Combine(staging, "frames");
        Directory.CreateDirectory(frames);

        try
        {
            for (var frame = 0; frame < options.FrameCount; frame++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var png = Path.Combine(frames, $"frame-{frame:D6}.png");
                await writePngFrame(frame, png, cancellationToken);
                if (!File.Exists(png) || new FileInfo(png).Length == 0)
                    throw new IOException($"第 {frame + 1} 帧没有生成有效 PNG。");
                progress?.Report(new StoreDialogueExportProgress("正在渲染", frame + 1, options.FrameCount));
            }
            cancellationToken.ThrowIfCancellationRequested();
            if (options.Format == StoreDialogueExportFormat.PngSequence)
            {
                // Move fails if another operation created the destination in
                // the meantime; no overwrite is allowed, including this race.
                var manifest = System.Text.Json.JsonSerializer.Serialize(new
                {
                    width = options.Width, height = options.Height,
                    framesPerSecond = options.FramesPerSecond, frameCount = options.FrameCount,
                    durationSeconds = (double)options.FrameCount / options.FramesPerSecond,
                    alpha = "straight", pattern = "frame-%06d.png",
                }, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
                await File.WriteAllTextAsync(Path.Combine(frames, "sequence.json"), manifest, cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                Directory.Move(frames, output);
            }
            else
            {
                progress?.Report(new StoreDialogueExportProgress("正在编码", options.FrameCount, options.FrameCount));
                var encoded = Path.Combine(staging, "encoded" + Path.GetExtension(output).ToLowerInvariant());
                await RunEncoderAsync(ffmpeg, BuildEncoderArguments(options, frames, encoded), cancellationToken);
                if (!File.Exists(encoded) || new FileInfo(encoded).Length == 0)
                    throw new IOException("FFmpeg 没有生成视频文件。");
                cancellationToken.ThrowIfCancellationRequested();
                File.Move(encoded, output, overwrite: false);
            }
            progress?.Report(new StoreDialogueExportProgress("导出完成", options.FrameCount, options.FrameCount));
            return new StoreDialogueExportResult(output, options.Format, options.FrameCount, options.FramesPerSecond);
        }
        finally
        {
            // Only the directory created by this invocation is ever removed.
            // Check its absolute parent/prefix again before recursive cleanup.
            if (Directory.Exists(staging) &&
                string.Equals(Path.GetDirectoryName(Path.GetFullPath(staging)), parent, StringComparison.OrdinalIgnoreCase) &&
                Path.GetFileName(staging).StartsWith(".store-dialogue-export-", StringComparison.Ordinal) &&
                (File.GetAttributes(staging) & FileAttributes.ReparsePoint) == 0)
            {
                try { Directory.Delete(staging, recursive: true); }
                catch (IOException) { /* Do not replace the original export error. */ }
                catch (UnauthorizedAccessException) { }
            }
        }
    }

    public static IReadOnlyList<string> BuildEncoderArguments(
        StoreDialogueExportOptions options, string frameDirectory, string encodedPath)
    {
        var args = new List<string>
        {
            "-hide_banner", "-loglevel", "error", "-nostdin", "-n",
            "-framerate", options.FramesPerSecond.ToString(CultureInfo.InvariantCulture),
            "-start_number", "0", "-i", Path.Combine(frameDirectory, "frame-%06d.png"),
            "-frames:v", options.FrameCount.ToString(CultureInfo.InvariantCulture), "-an",
        };
        switch (options.Format)
        {
            case StoreDialogueExportFormat.WebM:
                // Alpha is stored by libvpx as a second VP9 stream. FFmpeg's
                // native VP9 decoder may ignore it; use libvpx-vp9 to validate.
                args.AddRange(new[] { "-c:v", "libvpx-vp9", "-pix_fmt", "yuva420p", "-b:v", "0",
                    "-crf", (options.WebmLossless ? 0 : options.WebmCrf).ToString(CultureInfo.InvariantCulture),
                    "-auto-alt-ref", "0", "-metadata:s:v:0", "alpha_mode=1" });
                // Lossless refers to VP9 encoding after conversion to YUVA 4:2:0.
                if (options.WebmLossless) args.AddRange(new[] { "-lossless", "1" });
                break;
            case StoreDialogueExportFormat.WebP:
                args.AddRange(new[] { "-c:v", "libwebp_anim", "-pix_fmt", "bgra", "-lossless", "1",
                    "-compression_level", "4", "-loop", "0" });
                break;
            default:
                throw new ArgumentException("PNG 序列不需要编码。", nameof(options));
        }
        args.Add(encodedPath);
        return args;
    }

    private static async Task RunEncoderAsync(string ffmpeg, IReadOnlyList<string> arguments, CancellationToken cancellationToken)
    {
        var start = new ProcessStartInfo(ffmpeg)
        {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardError = true, RedirectStandardOutput = true,
        };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        using var process = new Process { StartInfo = start };
        try
        {
            if (!process.Start()) throw new IOException("无法启动 FFmpeg。");
        }
        catch (System.ComponentModel.Win32Exception startError)
        {
            throw new IOException("无法启动 FFmpeg，请检查可执行文件路径。", startError);
        }
        var errorTask = process.StandardError.ReadToEndAsync();
        var outputTask = process.StandardOutput.ReadToEndAsync();
        try
        {
            await process.WaitForExitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
            catch (InvalidOperationException) { }
            await process.WaitForExitAsync(CancellationToken.None);
            await Task.WhenAll(errorTask, outputTask);
            throw;
        }
        var error = await errorTask;
        await outputTask;
        if (process.ExitCode != 0)
        {
            if (error.Length > 4000) error = error[^4000..];
            throw new IOException($"FFmpeg 编码失败（{process.ExitCode}）：{error.Trim()}");
        }
    }

    /// <summary>Convert an RGBA8 premultiplied buffer to PNG's straight alpha convention.</summary>
    public static void UnpremultiplyRgba(Span<byte> rgba)
    {
        if (rgba.Length % 4 != 0) throw new ArgumentException("RGBA 数据长度必须是 4 的倍数。", nameof(rgba));
        for (var index = 0; index < rgba.Length; index += 4)
        {
            var alpha = rgba[index + 3];
            for (var channel = 0; channel < 3; channel++)
                rgba[index + channel] = alpha == 0 ? (byte)0
                    : (byte)Math.Min(255, (rgba[index + channel] * 255 + alpha / 2) / alpha);
        }
    }
}
