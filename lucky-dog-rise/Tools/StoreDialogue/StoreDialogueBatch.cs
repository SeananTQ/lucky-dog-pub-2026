#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
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
                throw new InvalidOperationException("素材预览和视频导出需要渲染驱动，请使用后台窗口运行而非 --headless。");
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
        var previewOnly = job.TryGetProperty("PreviewOnly", out var previewFlag) && previewFlag.GetBoolean();
        var validationOnly = job.TryGetProperty("ValidationOnly", out var validationFlag) && validationFlag.GetBoolean();
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
        StoreDialogueExport.ConfigureViewport(_viewport, project);
        var checks = new List<object>();
        var validationErrors = new List<string>();
        var previewCards = new StringBuilder();
        if (previewOnly)
        {
            Directory.CreateDirectory(Path.Combine(output, "previews"));
            project.Save(Path.Combine(output, "source.json"));
        }
        foreach (var language in languages)
        {
            var errors = project.Validate(language.Locale, true).ToList();
            errors.AddRange(StoreDialogueController.ValidateAssets(project));
            if (errors.Count > 0) throw new InvalidDataException(language.Locale + ": " + string.Join("; ", errors));
            _stage.Configure(project, language.Locale);
            errors.AddRange(_stage.ValidatePresentation());
            if (_stage.Timeline.DurationSeconds > 12) errors.Add("Steam 动画最长 12 秒。");
            await NextFrame();
            await NextFrame();
            var nameFont = _stage.Dogs[0].GetNode<Label>("NameBar/Name").GetThemeFont("font");
            var bubbleFont = _stage.GetNode<Label>("Bubble/Text").GetThemeFont("font");
            var locale = project.Locales[language.Locale];
            var missingNameGlyphs = string.Concat(string.Concat(locale.Names.Values)
                .Where(c => !char.IsWhiteSpace(c) && !nameFont.HasChar(c)).Distinct());
            var missingBubbleGlyphs = string.Concat(string.Concat(locale.Lines.Select(line => line.Text))
                .Where(c => !char.IsWhiteSpace(c) && !bubbleFont.HasChar(c)).Distinct());
            if (missingNameGlyphs.Length > 0 || missingBubbleGlyphs.Length > 0)
                errors.Add("字体缺少字符：" + missingNameGlyphs + missingBubbleGlyphs);
            var bars = _stage.Dogs.Select(view =>
            {
                var panel = view.GetNode<PanelContainer>("NameBar");
                var label = view.GetNode<Label>("NameBar/Name");
                var style = (StyleBoxFlat)panel.GetThemeStylebox("panel");
                // The bottom border is a bevel/shadow, outside the light face.
                var faceCenterY = (style.BorderWidthTop + panel.Size.Y - style.BorderWidthBottom) / 2;
                var textCenterY = label.Position.Y + label.Size.Y / 2;
                return new
                {
                    dogId = view.DogId, text = label.Text, fontSize = view.NameFontSize, fits = view.NameFits,
                    lineSpacing = label.GetThemeConstant("line_spacing"), labelWidth = label.Size.X,
                    width = view.NameBarSize.X, height = view.NameBarSize.Y,
                    labelHeight = label.Size.Y, lineHeight = label.GetLineHeight(),
                    minHeight = label.CustomMinimumSize.Y,
                    lines = label.GetLineCount(), visibleLines = label.GetVisibleLineCount(),
                    faceCenterY, textCenterY, verticalCenterError = textCenterY - faceCenterY,
                };
            }).ToArray();
            if (bars.Any(bar => Math.Abs(bar.verticalCenterError) > 0.01f))
                errors.Add("姓名板文字未按浅色区域垂直居中。");
            if (bars.Any(bar => bar.lines > StoreDialogueDogView.MaximumNameLines))
                errors.Add("姓名板超过两行，必须先缩短昵称再生成视频。");
            if (bars.Any(bar => bar.fontSize != bars[0].fontSize || bar.width != bars[0].width || bar.height != bars[0].height))
                errors.Add("同一排姓名板的字号或尺寸不一致。");
            if (bars.Any(bar => bar.lines != bar.visibleLines))
                errors.Add("姓名板存在不可见的文字行。");
            if (previewOnly)
            {
                var preview = "previews/" + stem + "-" + language.SteamLanguage + ".png";
                // ValidatePresentation ends at time zero: a still of every idle dog,
                // measured by the same engine and scene as the eventual video.
                SavePreview(project, Path.Combine(output, preview), cropToContent: true);
                previewCards.Append($"<section id='{Html(language.Locale)}'><h2>{Html(language.DisplayName)} <small>{Html(language.Locale)}</small></h2>");
                previewCards.Append($"<p class='{(errors.Count == 0 ? "pass" : "fail")}'>{(errors.Count == 0 ? "通过 · 所有昵称最多两行" : Html(string.Join("；", errors)))}</p>");
                previewCards.Append($"<div class='scroll'><div class='canvas' style='width:{project.CanvasWidth}px'><img width='{project.CanvasWidth}' src='{preview}' alt='{Html(language.DisplayName)}姓名板实际效果'></div></div>");
                previewCards.Append($"<p class='metrics'>字号 {bars[0].fontSize}px · 姓名板 {bars[0].width:0.##} × {bars[0].height:0.##}px · 额外行间距 {bars[0].lineSpacing}px</p><table><thead><tr><th>昵称</th><th>行数</th></tr></thead><tbody>");
                foreach (var bar in bars) previewCards.Append($"<tr><td lang='{Html(language.Locale)}'>{Html(bar.text)}</td><td>{bar.lines}</td></tr>");
                previewCards.Append("</tbody></table></section>");
            }
            checks.Add(new { locale = language.Locale, steamLanguage = language.SteamLanguage,
                displayName = language.DisplayName, passed = errors.Count == 0,
                missingNameGlyphs, missingBubbleGlyphs, bars, errors });
            validationErrors.AddRange(errors.Select(error => language.Locale + ": " + error));
            GD.Print($"StoreDialogueBatch {(errors.Count == 0 ? "VALIDATED" : "INVALID")} {language.Locale}: "
                + (errors.Count == 0 ? "names, assets, fonts and all speech bubbles fit." : string.Join("; ", errors)));
        }

        Directory.CreateDirectory(output);
        File.WriteAllText(Path.Combine(output, "layout-validation.json"),
            JsonSerializer.Serialize(new { passed = validationErrors.Count == 0, maximumNameLines = StoreDialogueDogView.MaximumNameLines, checks }, JsonOptions), new UTF8Encoding(false));
        if (previewOnly)
            WriteNicknamePage(output, previewCards.ToString(), languages.Length, validationErrors.Count == 0);
        if (validationErrors.Count > 0) throw new InvalidDataException(string.Join("\n", validationErrors));
        if (previewOnly || validationOnly) return;
        // Static checks and still previews have no FFmpeg dependency. Resolve the
        // encoder only after every language passes, before any video rendering.
        var ffmpeg = job.TryGetProperty("FfmpegPath", out var ffmpegPath) ? ffmpegPath.GetString() ?? "" : "";
        var encoder = StoreDialogueExport.DiscoverFfmpegPath(ffmpeg);
        var webmCrf = job.TryGetProperty("WebmCrf", out var crfValue) ? crfValue.GetInt32() : 18;
        var webmLossless = job.TryGetProperty("WebmLossless", out var losslessValue) && losslessValue.GetBoolean();
        if (webmCrf is < 0 or > 63) throw new ArgumentException("WebmCrf must be between 0 and 63.");
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
                width = project.CanvasWidth, height = project.CanvasHeight, framesPerSecond = project.Fps,
                webmCrf = webmLossless ? 0 : webmCrf, webmLossless, pixelFormat = "yuva420p", assets,
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
            SavePreview(project, Path.Combine(output, "previews", previewName));
            var options = new StoreDialogueExportOptions
            {
                OutputPath = Path.Combine(output, filename), Format = StoreDialogueExportFormat.WebM,
                Width = project.CanvasWidth, Height = project.CanvasHeight, FramesPerSecond = project.Fps,
                FrameCount = frameCount, FfmpegPath = encoder, InputAlpha = StoreDialogueAlphaMode.Premultiplied,
                WebmCrf = webmCrf, WebmLossless = webmLossless,
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

    private void SavePreview(StoreDialogueProject project, string path, bool cropToContent = false)
    {
        using var image = StoreDialogueExport.CaptureFrame(_viewport, project);
        image.Convert(Image.Format.Rgba8);
        var pixels = image.GetData();
        StoreDialogueExportCore.UnpremultiplyRgba(pixels);
        image.SetData(project.CanvasWidth, project.CanvasHeight, false, Image.Format.Rgba8, pixels);
        // Retain the exact export width and pixel scale, remove unused space above
        // the idle cast only. No browser font or CSS approximation of the name bars.
        var top = cropToContent ? Math.Max(0, image.GetUsedRect().Position.Y - 8) : 0;
        using var cropped = image.GetRegion(new Rect2I(0, top, project.CanvasWidth, project.CanvasHeight - top));
        if (cropped.SavePng(path) != Error.Ok) throw new IOException("保存语言预览图失败。");
    }

    private static string Html(string value) => WebUtility.HtmlEncode(value);

    private static void WriteNicknamePage(string output, string cards, int count, bool passed)
    {
        const string styles = """
            :root{color-scheme:dark;font-family:system-ui,'Microsoft YaHei',sans-serif;background:#202a35;color:#e3e9ee}
            body{max-width:800px;margin:36px auto;padding:0 24px;line-height:1.5}h1{font-size:26px}h2{font-size:20px;margin:0}
            p{color:#abbcc8}a{color:#a9d5ed}small{font-weight:400;color:#90a3b1}section{margin:32px 0 46px;border-top:1px solid #41505d;padding-top:20px}
            .pass{color:#99c5ac}.fail{color:#f0b0a3}.scroll{overflow-x:auto}.canvas{background-color:#334451;background-image:conic-gradient(#405362 25%,transparent 0 50%,#405362 0 75%,transparent 0);background-size:24px 24px}
            img{display:block;max-width:none;height:auto}.metrics{font-size:13px}table{border-collapse:collapse;width:100%;font-size:14px}th,td{padding:6px 12px;text-align:left;border-bottom:1px solid #33434f}th:last-child,td:last-child{width:60px;text-align:center}
            """;
        var html = "<!doctype html><html lang='zh-CN'><meta charset='utf-8'><meta name='viewport' content='width=device-width,initial-scale=1'>"
            + "<title>商店对白 · 昵称校验</title><style>" + styles + "</style><body><h1>商店对白 · 昵称校验</h1>"
            + $"<p>{count} 种语言 · {(passed ? "排版校验通过" : "存在待修正项")} · 每块姓名板最多两行</p>"
            + "<p>以下为 Godot 实际字体和姓名板的静态输出，按视频原始像素尺寸显示。检查换行、缺字、裁切及同排字号与尺寸一致性。排版通过不代表译文已经由母语者校对。</p>"
            + "<p><a href='source.json'>本次完整方案</a> · <a href='layout-validation.json'>排版校验结果</a></p>"
            + "<!-- nickname-review-notes -->" + cards + "</body></html>";
        File.WriteAllText(Path.Combine(output, "index.html"), html, new UTF8Encoding(false));
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
