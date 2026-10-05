using LuckyDogRise.Tools;

var checks = 0;
var locale = new StoreDialogueLocale
{
    Lines = new()
    {
        new() { DogId = "player", Text = "什么时候能推出新打牌模式呀？", DurationSeconds = 2 },
        new() { DogId = "designer", Text = "我们正在加班赶工啦", DurationSeconds = 3 },
        new() { DogId = "artist", Text = "努力！", DurationSeconds = 1 },
        new() { DogId = "programmer", Text = "奋斗！", DurationSeconds = 1 },
    },
};
var timeline = new StoreDialogueTimeline(locale, .5, .25, 1);
Check(timeline.DurationSeconds == 9.25, "duration includes lead, three gaps, and tail exactly once");
Check(timeline.GetFrameCount(30) == 278, "partial last frame rounds up without a duplicate loop frame");
Check(!timeline.Sample(-3).IsSpeaking && timeline.Sample(-3).CycleSeconds == 0, "negative playhead clamps to the start");
Check(!timeline.Sample(0).IsSpeaking, "lead-in starts idle");
ExpectSpeaker(.5, "player", 0);
ExpectSpeaker(2.499, "player", 0);
Check(!timeline.Sample(2.5).IsSpeaking, "speaker clears at the exact end of a line");
Check(timeline.Sample(2.5).SpeakerDogId == "" && timeline.Sample(2.5).Text == "", "idle carries no stale bubble or speaker");
ExpectSpeaker(2.75, "designer", 1);
Check(timeline.Sample(3).LocalSeconds == .25, "line-local time resets for each speaker");
ExpectSpeaker(6, "artist", 2);
ExpectSpeaker(7.25, "programmer", 3);
Check(!timeline.Sample(8.25).IsSpeaking && !timeline.Sample(8.25).IsComplete, "tail restores every dog to idle before completion");
Check(timeline.Sample(9.25).IsComplete && !timeline.Sample(9.25).IsSpeaking, "clamped endpoint completes idle");
Check(timeline.Sample(20).CycleSeconds == 9.25, "export samples beyond the endpoint cannot start another cycle");
Check(timeline.Sample(9.25, loop: true).CycleSeconds == 0 && !timeline.Sample(9.25, loop: true).IsComplete,
    "loop endpoint becomes the next lead-in");
Check(timeline.Sample(9.75, loop: true).SpeakerDogId == "player", "preview repeats the same speaker order");

// The timeline is an immutable snapshot so UI edits cannot alter an export in progress.
locale.Lines[0].DogId = "changed";
locale.Lines[0].Text = "changed";
locale.Lines[0].DurationSeconds = 30;
locale.Lines.Clear();
Check(timeline.Sample(.5).SpeakerDogId == "player" && timeline.DurationSeconds == 9.25,
    "later authoring edits do not mutate sampled output");

var adjacent = new StoreDialogueTimeline(new StoreDialogueLocale
{
    Lines = new() { new() { DogId = "a", DurationSeconds = 1 }, new() { DogId = "b", DurationSeconds = 1 } },
}, 0, 0, 0);
Check(adjacent.Sample(1).SpeakerDogId == "b", "zero gap transitions directly to the next speaker at the boundary");
Check(adjacent.Sample(2).IsComplete && !adjacent.Sample(2).IsSpeaking, "zero-tail export still completes idle");
Check(adjacent.Sample(2, loop: true).SpeakerDogId == "a", "zero-lead loop starts first speaker immediately");
var empty = new StoreDialogueTimeline(new StoreDialogueLocale(), 0, 0, 0);
Check(empty.Sample(0, loop: true).IsComplete && empty.GetFrameCount(30) == 0, "empty timeline avoids modulo-zero");

foreach (var invalidTime in new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity })
    Throws<ArgumentOutOfRangeException>(() => timeline.Sample(invalidTime), "non-finite playheads are rejected");
foreach (var invalidDuration in new[] { 0, -1, double.NaN, double.PositiveInfinity })
    Throws<ArgumentOutOfRangeException>(() => new StoreDialogueTimeline(new StoreDialogueLocale
    { Lines = new() { new() { DurationSeconds = invalidDuration } } }, 0, 0, 0), "invalid per-line duration is rejected");
Throws<ArgumentOutOfRangeException>(() => new StoreDialogueTimeline(new StoreDialogueLocale(), -1, 0, 0), "negative lead is rejected");
Throws<ArgumentOutOfRangeException>(() => timeline.GetFrameCount(0), "zero fps is rejected");

Check(StoreDialogueExportLimits.Validate(1170, 430, 30, timeline.DurationSeconds).Count == 0,
    "default six-dog composition fits rendering limits");
foreach (var dimensions in new[] { (0, 430), (int.MaxValue, int.MaxValue), (4096, 2160) })
    Check(StoreDialogueExportLimits.Validate(dimensions.Item1, dimensions.Item2, 30, 10).Count > 0,
        "invalid or oversized frame buffer is rejected before allocation");
Check(StoreDialogueExportLimits.Validate(4096, 2048, 60, 60).Count > 0, "total frame workload is bounded");
Check(StoreDialogueExportLimits.Validate(1170, 430, 30, double.NaN).Count > 0, "NaN duration cannot bypass export limits");
Check(StoreDialogueExportLimits.Validate(1170, 430, 0, 10).Count > 0, "invalid fps cannot bypass export limits");

Console.WriteLine($"StoreDialogueTimeline: {checks} timing, snapshot, loop, and export limit checks passed.");

void ExpectSpeaker(double time, string dogId, int line)
{
    var sample = timeline.Sample(time);
    Check(sample.IsSpeaking && sample.SpeakerDogId == dogId && sample.LineIndex == line, $"speaker at t={time}");
}

void Check(bool condition, string message)
{
    if (!condition) throw new Exception(message);
    checks++;
}

void Throws<T>(Action action, string message) where T : Exception
{
    try { action(); }
    catch (T) { checks++; return; }
    throw new Exception(message);
}
