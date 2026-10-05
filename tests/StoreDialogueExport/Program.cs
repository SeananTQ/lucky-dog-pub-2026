using System.Buffers.Binary;
using System.Diagnostics;
using System.IO.Compression;
using System.Text;
using LuckyDogRise.Tools;

var testRoot = Path.Combine(Path.GetTempPath(), "store-dialogue-export-tests-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(testRoot);
var count = 0;
try
{
    var options = new StoreDialogueExportOptions
    {
        OutputPath = Path.Combine(testRoot, "transparent frames 中文"),
        Format = StoreDialogueExportFormat.PngSequence,
        Width = 16, Height = 8, FramesPerSecond = 10, FrameCount = 5,
    };
    var order = new List<int>();
    async Task Render(int frame, string path, CancellationToken token)
    {
        order.Add(frame);
        await File.WriteAllBytesAsync(path, MakePng(options.Width, options.Height, frame), token);
    }
    var result = await StoreDialogueExportCore.ExportAsync(options, Render);
    Check(order.SequenceEqual(Enumerable.Range(0, 5)), "frames rendered once, in exact order");
    Check(result.DurationSeconds == 0.5, "duration equals frame count / frame rate");
    Check(Directory.GetFiles(result.OutputPath, "*.png").Length == 5, "all frames published");
    Check(File.Exists(Path.Combine(result.OutputPath, "sequence.json")), "sequence carries fps/duration metadata");
    Check(!Directory.GetDirectories(testRoot, ".store-dialogue-export-*").Any(), "successful export cleans staging");
    await Throws<IOException>(() => StoreDialogueExportCore.ExportAsync(options, Render), "never overwrites existing sequence");
    Check(order.Count == 5, "overwrite rejection happens before rendering");

    await Throws<ArgumentException>(() => StoreDialogueExportCore.ExportAsync(options with { OutputPath = "relative.webm" }, Render), "reject relative paths");
    await Throws<ArgumentException>(() => StoreDialogueExportCore.ExportAsync(options with { OutputPath = Path.Combine(testRoot, "wrong.csv"), Format = StoreDialogueExportFormat.WebM }, Render), "reject format mismatch");
    await Throws<ArgumentException>(() => StoreDialogueExportCore.ExportAsync(options with { OutputPath = Path.Combine(testRoot, "bad"), FramesPerSecond = 0 }, Render), "reject zero fps");
    await Throws<FileNotFoundException>(() => StoreDialogueExportCore.ExportAsync(options with { OutputPath = Path.Combine(testRoot, "missing.webm"), Format = StoreDialogueExportFormat.WebM, FfmpegPath = Path.Combine(testRoot, "missing-encoder.exe") }, Render), "missing encoder fails before rendering");
    Check(order.Count == 5, "invalid options never call render");

    using var cancellation = new CancellationTokenSource();
    var cancelledPath = Path.Combine(testRoot, "cancelled");
    await Throws<OperationCanceledException>(() => StoreDialogueExportCore.ExportAsync(options with { OutputPath = cancelledPath }, async (frame, path, token) =>
    {
        await Render(frame, path, token);
        cancellation.Cancel();
    }, cancellationToken: cancellation.Token), "cancel interrupts frame generation");
    Check(!Directory.Exists(cancelledPath), "cancel never publishes a partial sequence");
    Check(!Directory.GetDirectories(testRoot, ".store-dialogue-export-*").Any(), "cancel cleans staging");
    await Throws<InvalidOperationException>(() => StoreDialogueExportCore.ExportAsync(options with { OutputPath = Path.Combine(testRoot, "failed") }, (_, _, _) => throw new InvalidOperationException("render failed")), "render errors propagate");
    Check(!Directory.GetDirectories(testRoot, ".store-dialogue-export-*").Any(), "render error cleans staging");

    var buffer = new byte[] { 64, 32, 0, 128, 10, 20, 30, 0, 25, 50, 75, 255 };
    StoreDialogueExportCore.UnpremultiplyRgba(buffer);
    Check(buffer.SequenceEqual(new byte[] { 128, 64, 0, 128, 0, 0, 0, 0, 25, 50, 75, 255 }), "unpremultiply handles translucent/zero/opaque alpha");

    // Optional integration uses a user-specified/local encoder, never downloads
    // dependencies. PNG/unit coverage always runs without FFmpeg.
    var ffmpeg = args.Length > 0 ? args[0] : Environment.GetEnvironmentVariable("FFMPEG_PATH");
    if (!string.IsNullOrWhiteSpace(ffmpeg))
    {
        var webmOptions = options with
        {
            OutputPath = Path.Combine(testRoot, "transparent video 中文.webm"),
            Format = StoreDialogueExportFormat.WebM, FfmpegPath = ffmpeg,
        };
        await StoreDialogueExportCore.ExportAsync(webmOptions, Render);
        await Throws<IOException>(() => StoreDialogueExportCore.ExportAsync(webmOptions, Render), "never overwrites movie");
        var decoded = await Run(ffmpeg, "-hide_banner", "-loglevel", "error", "-c:v", "libvpx-vp9", "-i", webmOptions.OutputPath,
            "-f", "rawvideo", "-pix_fmt", "rgba", "-");
        Check(decoded.ExitCode == 0, "libvpx alpha decode succeeds: " + decoded.Error);
        var bytesPerFrame = options.Width * options.Height * 4;
        Check(decoded.Bytes.Length == bytesPerFrame * options.FrameCount, "encoded dimensions and frame count round-trip");
        for (var frame = 0; frame < options.FrameCount; frame++)
        {
            var frameOffset = frame * bytesPerFrame;
            Check(decoded.Bytes[frameOffset + 3] == 0, "fully transparent pixel round-trips");
            Check(Math.Abs(decoded.Bytes[frameOffset + 6 * 4 + 3] - 128) <= 2, "partial alpha round-trips");
            Check(decoded.Bytes[frameOffset + 12 * 4 + 3] == 255, "opaque alpha round-trips");
            Check(Math.Abs(decoded.Bytes[frameOffset + 12 * 4] - (100 + frame * 20)) < 12, "straight RGB remains bright (no premultiplied darkening)");
        }
        var webpOptions = webmOptions with { OutputPath = Path.Combine(testRoot, "transparent animation.webp"), Format = StoreDialogueExportFormat.WebP };
        await StoreDialogueExportCore.ExportAsync(webpOptions, Render);
        var webp = await File.ReadAllBytesAsync(webpOptions.OutputPath);
        Check(Encoding.ASCII.GetString(webp, 0, 4) == "RIFF" && Encoding.ASCII.GetString(webp, 8, 4) == "WEBP", "WebP container created");
        var animationFrames = 0;
        var animationDuration = 0;
        var alphaFlag = false;
        var animationFlag = false;
        for (var offset = 12; offset + 8 <= webp.Length;)
        {
            var tag = Encoding.ASCII.GetString(webp, offset, 4);
            var length = BinaryPrimitives.ReadInt32LittleEndian(webp.AsSpan(offset + 4, 4));
            if (tag == "VP8X")
            {
                alphaFlag = (webp[offset + 8] & 0x10) != 0;
                animationFlag = (webp[offset + 8] & 0x02) != 0;
            }
            if (tag == "ANMF")
            {
                animationFrames++;
                var duration = offset + 8 + 12;
                animationDuration += webp[duration] | webp[duration + 1] << 8 | webp[duration + 2] << 16;
            }
            offset += 8 + length + (length & 1);
        }
        Check(alphaFlag && animationFlag, "animated WebP advertises alpha");
        Check(animationFrames == options.FrameCount, "WebP contains every distinct frame");
        Check(animationDuration == 500, "WebP duration is 500ms at 10fps");

        // Canvas width is user-configurable; VP9 must retain an odd width too.
        var oddOptions = webmOptions with { OutputPath = Path.Combine(testRoot, "odd.webm"), Width = 17, FrameCount = 1 };
        await StoreDialogueExportCore.ExportAsync(oddOptions, (_, path, token) => File.WriteAllBytesAsync(path, MakePng(17, options.Height, 0), token));
        var oddDecoded = await Run(ffmpeg, "-hide_banner", "-loglevel", "error", "-c:v", "libvpx-vp9", "-i", oddOptions.OutputPath,
            "-f", "rawvideo", "-pix_fmt", "rgba", "-");
        Check(oddDecoded.ExitCode == 0 && oddDecoded.Bytes.Length == 17 * options.Height * 4, "odd canvas width retained without padding/cropping");

        var invalidOptions = webmOptions with { OutputPath = Path.Combine(testRoot, "invalid.webm"), FrameCount = 1 };
        await Throws<IOException>(() => StoreDialogueExportCore.ExportAsync(invalidOptions, (_, path, token) => File.WriteAllTextAsync(path, "invalid PNG", token)), "encoder rejection propagates");
        Check(!File.Exists(invalidOptions.OutputPath), "failed encoder publishes no video");
        Check(!Directory.GetDirectories(testRoot, ".store-dialogue-export-*").Any(), "encoder error cleans staging");

        using var encoderCancellation = new CancellationTokenSource();
        var encodeCancelledOptions = webmOptions with { OutputPath = Path.Combine(testRoot, "encoder-cancel.webm") };
        var cancelOnEncoding = new SynchronousProgress(value =>
        {
            if (value.Stage == "正在编码") encoderCancellation.Cancel();
        });
        await Throws<OperationCanceledException>(() => StoreDialogueExportCore.ExportAsync(encodeCancelledOptions, Render, cancelOnEncoding, encoderCancellation.Token), "cancel at encode boundary terminates process");
        Check(!File.Exists(encodeCancelledOptions.OutputPath), "encoder cancellation publishes no video");
        Check(!Directory.GetDirectories(testRoot, ".store-dialogue-export-*").Any(), "encoder cancellation cleans staging");
        Console.WriteLine("Real FFmpeg WebM RGBA round-trip and animated WebP container checks passed.");
    }
    else Console.WriteLine("FFmpeg integration not run: supply executable path argument or FFMPEG_PATH.");
    Console.WriteLine($"StoreDialogueExport: {count} checks passed.");
}
finally
{
    // This test owns this newly created GUID directory only.
    if (Directory.Exists(testRoot) && Path.GetFileName(testRoot).StartsWith("store-dialogue-export-tests-", StringComparison.Ordinal))
        Directory.Delete(testRoot, recursive: true);
}

void Check(bool value, string message)
{
    if (!value) throw new Exception(message);
    count++;
}
async Task Throws<T>(Func<Task> operation, string message) where T : Exception
{
    try { await operation(); }
    catch (T) { count++; return; }
    throw new Exception(message);
}

static byte[] MakePng(int width, int height, int frame)
{
    using var raw = new MemoryStream();
    for (var y = 0; y < height; y++)
    {
        raw.WriteByte(0);
        for (var x = 0; x < width; x++)
        {
            raw.WriteByte((byte)(100 + frame * 20));
            raw.WriteByte(60);
            raw.WriteByte(20);
            raw.WriteByte(x < 4 ? (byte)0 : x < 10 ? (byte)128 : (byte)255);
        }
    }
    using var compressed = new MemoryStream();
    using (var zlib = new ZLibStream(compressed, CompressionLevel.Fastest, leaveOpen: true)) zlib.Write(raw.ToArray());
    using var png = new MemoryStream();
    png.Write(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 });
    var header = new byte[13];
    BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(0, 4), width);
    BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4, 4), height);
    header[8] = 8;
    header[9] = 6;
    Chunk("IHDR", header);
    Chunk("IDAT", compressed.ToArray());
    Chunk("IEND", Array.Empty<byte>());
    return png.ToArray();

    void Chunk(string type, byte[] data)
    {
        var length = new byte[4];
        BinaryPrimitives.WriteInt32BigEndian(length, data.Length);
        png.Write(length);
        var typeBytes = Encoding.ASCII.GetBytes(type);
        png.Write(typeBytes);
        png.Write(data);
        var crc = 0xffffffffu;
        foreach (var value in typeBytes.Concat(data))
        {
            crc ^= value;
            for (var i = 0; i < 8; i++) crc = (crc >> 1) ^ ((crc & 1) != 0 ? 0xedb88320u : 0);
        }
        BinaryPrimitives.WriteUInt32BigEndian(length, ~crc);
        png.Write(length);
    }
}

static async Task<(int ExitCode, byte[] Bytes, string Error)> Run(string executable, params string[] arguments)
{
    var info = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
    foreach (var argument in arguments) info.ArgumentList.Add(argument);
    using var process = Process.Start(info)!;
    using var data = new MemoryStream();
    var output = process.StandardOutput.BaseStream.CopyToAsync(data);
    var error = process.StandardError.ReadToEndAsync();
    await process.WaitForExitAsync();
    await output;
    return (process.ExitCode, data.ToArray(), await error);
}

sealed class SynchronousProgress(Action<StoreDialogueExportProgress> report) : IProgress<StoreDialogueExportProgress>
{
    public void Report(StoreDialogueExportProgress value) => report(value);
}
