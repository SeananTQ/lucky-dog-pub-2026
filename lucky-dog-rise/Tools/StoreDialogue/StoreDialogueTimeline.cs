using System;
using System.Collections.Generic;

namespace LuckyDogRise.Tools;

public sealed class StoreDialogueLocale
{
    public Dictionary<string, string> Names { get; set; } = new(StringComparer.Ordinal);
    public List<StoreDialogueLine> Lines { get; set; } = new();
}

public sealed class StoreDialogueLine
{
    public string DogId { get; set; } = "";
    public string Text { get; set; } = "";
    public double DurationSeconds { get; set; } = 2;
}

public readonly record struct StoreDialogueMoment(
    int LineIndex,
    string SpeakerDogId,
    string Text,
    double LocalSeconds,
    double CycleSeconds,
    bool IsComplete)
{
    public bool IsSpeaking => LineIndex >= 0;
}

/// <summary>
/// Pure, immutable timing snapshot. Preview and exported frames sample the same schedule;
/// no game clocks, timers, player state, or localization services are involved.
/// </summary>
public sealed class StoreDialogueTimeline
{
    private readonly List<Segment> _segments = new();
    public double DurationSeconds { get; }

    public StoreDialogueTimeline(StoreDialogueLocale locale, double leadInSeconds,
        double lineGapSeconds, double tailSeconds)
    {
        ArgumentNullException.ThrowIfNull(locale);
        ArgumentNullException.ThrowIfNull(locale.Lines);
        RequireTime(leadInSeconds, nameof(leadInSeconds));
        RequireTime(lineGapSeconds, nameof(lineGapSeconds));
        RequireTime(tailSeconds, nameof(tailSeconds));

        var cursor = leadInSeconds;
        for (var i = 0; i < locale.Lines.Count; i++)
        {
            var line = locale.Lines[i]
                ?? throw new ArgumentException("对白不能为 null。", nameof(locale));
            RequireTime(line.DurationSeconds, nameof(line.DurationSeconds), positive: true);
            var end = cursor + line.DurationSeconds;
            _segments.Add(new Segment(i, line.DogId ?? "", line.Text ?? "", cursor, end));
            cursor = end;
            if (i + 1 < locale.Lines.Count) cursor += lineGapSeconds;
        }
        DurationSeconds = cursor + tailSeconds;
        if (!double.IsFinite(DurationSeconds))
            throw new ArgumentOutOfRangeException(nameof(locale), "对白总时长超出范围。");
    }

    public StoreDialogueMoment Sample(double timeSeconds, bool loop = false)
    {
        if (!double.IsFinite(timeSeconds)) throw new ArgumentOutOfRangeException(nameof(timeSeconds));
        var time = Math.Max(0, timeSeconds);
        if (DurationSeconds <= 0)
            return Idle(0, true);
        if (loop) time %= DurationSeconds;
        else if (time >= DurationSeconds) return Idle(DurationSeconds, true);

        foreach (var segment in _segments)
        {
            // Half-open intervals switch directly at the boundary, including a zero gap.
            if (time >= segment.Start && time < segment.End)
                return new StoreDialogueMoment(segment.Index, segment.DogId, segment.Text,
                    time - segment.Start, time, false);
        }
        return Idle(time, false);
    }

    public int GetFrameCount(int fps)
    {
        if (fps is < 1 or > 60) throw new ArgumentOutOfRangeException(nameof(fps));
        return checked((int)Math.Ceiling(DurationSeconds * fps));
    }

    private static StoreDialogueMoment Idle(double time, bool complete) => new(-1, "", "", 0, time, complete);

    private static void RequireTime(double value, string name, bool positive = false)
    {
        if (!double.IsFinite(value) || (positive ? value <= 0 : value < 0))
            throw new ArgumentOutOfRangeException(name);
    }

    private readonly record struct Segment(int Index, string DogId, string Text, double Start, double End);
}

/// <summary>Bounds the render surface, frame count, and worst-case raw frame storage.</summary>
public static class StoreDialogueExportLimits
{
    public const int MaximumWidth = 4096;
    public const int MaximumHeight = 2160;
    public const int MaximumFps = 60;
    public const double MaximumDurationSeconds = 60;
    public const long MaximumFramePixels = 8_388_608;
    public const long MaximumTotalFramePixels = 2_147_483_648;

    public static IReadOnlyList<string> Validate(int width, int height, int fps, double durationSeconds)
    {
        var errors = new List<string>();
        if (width is < 64 or > MaximumWidth) errors.Add($"画布宽度须为 64～{MaximumWidth} 像素。");
        if (height is < 64 or > MaximumHeight) errors.Add($"画布高度须为 64～{MaximumHeight} 像素。");
        if (fps is < 1 or > MaximumFps) errors.Add($"帧率须为 1～{MaximumFps}。");
        if (!double.IsFinite(durationSeconds) || durationSeconds <= 0 || durationSeconds > MaximumDurationSeconds)
            errors.Add($"总时长须大于 0 且不超过 {MaximumDurationSeconds} 秒。");
        if (errors.Count > 0) return errors;

        var pixels = (long)width * height;
        if (pixels > MaximumFramePixels) errors.Add("单帧画布过大，请减小宽度或高度。");
        var frames = (long)Math.Ceiling(durationSeconds * fps);
        if (pixels * frames > MaximumTotalFramePixels)
            errors.Add("本次导出的像素总量过大，请减小尺寸、帧率或时长。");
        return errors;
    }
}
