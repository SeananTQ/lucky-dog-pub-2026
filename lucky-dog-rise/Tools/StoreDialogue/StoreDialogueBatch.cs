#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Godot;

namespace LuckyDogRise.Tools;

/// <summary>Runs a saved export job without the authoring UI or game state.</summary>
public partial class StoreDialogueBatch : Node
{
    [Export] private SubViewport _viewport = null!;
    [Export] private StoreDialogueStage _stage = null!;
    private readonly CancellationTokenSource _cancellation = new();
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public override async void _Ready()
    {
        try
        {
            var args = OS.GetCmdlineUserArgs();
            if (!args.Contains("--disable-steam"))
                throw new InvalidOperationException("后台素材导出必须带 --disable-steam。");
            if (DisplayServer.GetName() == "headless")
                throw new InvalidOperationException("视频导出需要渲染驱动，请使用后台窗口运行而非 --headless。");
            const string prefix = "--dialogue-job=";
            var jobPath = args.SingleOrDefault(arg => arg.StartsWith(prefix, StringComparison.Ordinal))?[prefix.Length..];
            if (string.IsNullOrWhiteSpace(jobPath)) throw new ArgumentException("缺少 --dialogue-job=<完整 JSON 路径>。");
            using var job = JsonDocument.Parse(File.ReadAllText(jobPath));
            await RunJob(job.RootElement);
            GD.Print("StoreDialogueBatch PASS");
            GetTree().Quit();
        }
        catch (Exception error)
        {
            GD.PushError("StoreDialogueBatch FAILED: " + error);
            GetTree().Quit(1);
        }
    }

    private async Task RunJob(JsonElement job)
    {
        var source = job.GetProperty("ProjectPath").GetString() ?? "";
        var output = job.GetProperty("OutputDirectory").GetString() ?? "";
        var stem = job.GetProperty("FileStem").GetString() ?? "";
        var ffmpeg = job.GetProperty("FfmpegPath").GetString() ?? "";
        if (!Path.IsPathFullyQualified(source) || !Path.IsPathFullyQualified(output))
            throw new ArgumentException("方案和输出目录必须是完整路径。");
        output = Path.GetFullPath(output);
        if (!Regex.IsMatch(stem, "^[A-Za-z0-9][A-Za-z0-9_-]{0,100}$"))
            throw new ArgumentException("文件前缀只支持字母、数字、下划线和连字符。");
        var languages = job.GetProperty("Languages").EnumerateArray().Select(row => new Language(
            row.GetProperty("Locale").GetString() ?? "",
            row.GetProperty("SteamLanguage").GetString() ?? "",
            row.GetProperty("DisplayName").GetString() ?? "")).ToArray();
        if (languages.Length is < 1 or > 64
            || languages.Select(x => x.Locale).Distinct().Count() != languages.Length
            || languages.Select(x => x.SteamLanguage).Distinct(StringComparer.OrdinalIgnoreCase).Count() != languages.Length)
            throw new ArgumentException("请选择 1～64 种不重复的语言及 Steam 语言标识。");
        foreach (var language in languages)
            if (!Regex.IsMatch(language.SteamLanguage, "^[a-z]+$") || string.IsNullOrWhiteSpace(language.DisplayName))
                throw new ArgumentException("Steam 语言标识或语言名称无效。");
        if (File.Exists(output) || (Directory.Exists(output) && Directory.EnumerateFileSystemEntries(output).Any()))
            throw new IOException("导出目录必须为空，避免覆盖已有批次。");

        var project = StoreDialogueProject.Load(source);
        var encoder = StoreDialogueExport.DiscoverFfmpegPath(ffmpeg);
        StoreDialogueExport.ConfigureViewport(_viewport, project);
        foreach (var language in languages)
        {
            var errors = project.Validate(language.Locale, true).ToList();
            errors.AddRange(StoreDialogueController.ValidateAssets(project));
            if (errors.Count > 0) throw new InvalidDataException(language.Locale + ": " + string.Join("; ", errors));
            _stage.Configure(project, language.Locale);
            errors.AddRange(_stage.ValidatePresentation());
            if (_stage.Timeline.DurationSeconds > 12) errors.Add("Steam 动画最长 12 秒。");
            if (errors.Count > 0) throw new InvalidDataException(language.Locale + ": " + string.Join("; ", errors));
            await NextFrame();
            GD.Print($"StoreDialogueBatch VALIDATED {language.Locale}: names, assets and all speech bubbles fit.");
        }

        Directory.CreateDirectory(output);
        Directory.CreateDirectory(Path.Combine(output, "previews"));
        project.Save(Path.Combine(output, "source.json"));
        var sourceHash = Fingerprint(Path.Combine(output, "source.json"));
        var created = DateTime.UtcNow;
        var assets = new List<object>();
        void SaveManifest(bool complete)
        {
            var manifest = new
            {
                version = 1, complete, generatedAtUtc = created, sourceProject = "source.json", sourceSha256 = sourceHash,
                width = project.CanvasWidth, height = project.CanvasHeight, framesPerSecond = project.Fps, assets,
            };
            var target = Path.Combine(output, "manifest.json");
            File.WriteAllText(target + ".tmp", JsonSerializer.Serialize(manifest, JsonOptions), new UTF8Encoding(false));
            File.Move(target + ".tmp", target, true);
        }
        SaveManifest(false);
        foreach (var language in languages)
        {
            _stage.Configure(project, language.Locale);
            var filename = stem + "-" + language.SteamLanguage + ".webm";
            var previewName = stem + "-" + language.SteamLanguage + ".png";
            var frameCount = _stage.Timeline.GetFrameCount(project.Fps);
            _stage.Seek(project.LeadInSeconds + 0.1);
            await NextFrame();
            await NextFrame();
            using (var image = StoreDialogueExport.CaptureFrame(_viewport, project))
            {
                image.Convert(Image.Format.Rgba8);
                var pixels = image.GetData();
                StoreDialogueExportCore.UnpremultiplyRgba(pixels);
                image.SetData(project.CanvasWidth, project.CanvasHeight, false, Image.Format.Rgba8, pixels);
                if (image.SavePng(Path.Combine(output, "previews", previewName)) != Error.Ok)
                    throw new IOException("保存语言预览图失败。");
            }
            var options = new StoreDialogueExportOptions
            {
                OutputPath = Path.Combine(output, filename), Format = StoreDialogueExportFormat.WebM,
                Width = project.CanvasWidth, Height = project.CanvasHeight, FramesPerSecond = project.Fps,
                FrameCount = frameCount, FfmpegPath = encoder, InputAlpha = StoreDialogueAlphaMode.Premultiplied,
            };
            var result = await StoreDialogueExport.ExportAsync(options, async (frame, token) =>
            {
                token.ThrowIfCancellationRequested();
                _stage.Seek((double)frame / project.Fps);
                await NextFrame();
                await NextFrame();
                token.ThrowIfCancellationRequested();
                return StoreDialogueExport.CaptureFrame(_viewport, project);
            }, new BatchProgress(language.Locale, project.Fps), _cancellation.Token);
            assets.Add(new
            {
                locale = language.Locale, steamLanguage = language.SteamLanguage, displayName = language.DisplayName,
                file = filename, preview = "previews/" + previewName, frameCount, durationSeconds = result.DurationSeconds,
                sha256 = Fingerprint(result.OutputPath),
            });
            SaveManifest(false);
            GD.Print($"StoreDialogueBatch EXPORTED {language.Locale}: {filename} ({result.DurationSeconds:0.###}s)");
        }
        SaveManifest(true);
    }

    private static string Fingerprint(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
    private async Task NextFrame() => await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    public override void _ExitTree() => _cancellation.Cancel();
    private sealed record Language(string Locale, string SteamLanguage, string DisplayName);
    private sealed class BatchProgress(string locale, int fps) : IProgress<StoreDialogueExportProgress>
    {
        public void Report(StoreDialogueExportProgress value)
        {
            if (value.CompletedFrames % (fps * 2) == 0 || value.CompletedFrames == value.TotalFrames)
                GD.Print($"StoreDialogueBatch {locale}: {value.Stage} {value.CompletedFrames}/{value.TotalFrames}");
        }
    }
}
