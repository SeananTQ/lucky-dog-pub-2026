using System.Text;
using LuckyStats.Core;

namespace LuckyStats.Infrastructure;

public static class CsvExporter
{
    private static readonly UTF8Encoding Utf8Bom = new(true);

    public static Task WriteFactsAsync(string path, IEnumerable<FactRow> rows, CancellationToken cancellationToken) =>
        WriteAsync(path, ["APIName", "DisplayName", "Unit", "GlobalValue", "ExcludedValue", "AnalyzedValue"],
            rows.Select(x => new[] { x.ApiName, x.DisplayName, x.Unit, x.GlobalValue.ToString(), x.ExcludedValue.ToString(), x.AnalyzedValue.ToString() }),
            cancellationToken);

    public static Task WriteMetricsAsync(string path, IEnumerable<MetricResult> rows, CancellationToken cancellationToken) =>
        WriteAsync(path, ["Group", "Key", "DisplayName", "Value", "Formula", "Status"],
            rows.Select(x => new[] { x.Group, x.Key, x.DisplayName, x.Value, x.Formula, x.Status }),
            cancellationToken);

    public static Task WriteChecksAsync(string path, IEnumerable<ValidationResult> rows, CancellationToken cancellationToken) =>
        WriteAsync(path, ["Severity", "Rule", "Message"],
            rows.Select(x => new[] { x.Severity.ToString(), x.Rule, x.Message }),
            cancellationToken);

    private static async Task WriteAsync(
        string path,
        IReadOnlyList<string> headers,
        IEnumerable<IReadOnlyList<string>> rows,
        CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read);
        await using var writer = new StreamWriter(stream, Utf8Bom);
        await writer.WriteLineAsync(string.Join(',', headers.Select(Escape)));
        foreach (var row in rows)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await writer.WriteLineAsync(string.Join(',', row.Select(Escape)));
        }
    }

    private static string Escape(string value)
    {
        if (!value.ContainsAny([',', '"', '\r', '\n']))
            return value;
        return $"\"{value.Replace("\"", "\"\"")}\"";
    }

    private static bool ContainsAny(this string value, ReadOnlySpan<char> characters)
    {
        foreach (var character in characters)
        {
            if (value.Contains(character))
                return true;
        }
        return false;
    }
}
