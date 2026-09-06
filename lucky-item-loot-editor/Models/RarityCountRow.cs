using System.Windows;
using System.Windows.Media;

namespace LuckyItemLootEditor.Models;

public sealed class RarityCountRow
{
    public RarityCountRow(
        string label,
        int candidateCount,
        double probability,
        double barRatio,
        Brush barBrush,
        bool hasConfigurationError)
    {
        Label = label;
        CandidateCount = candidateCount;
        Probability = probability;
        var normalizedRatio = Math.Clamp(barRatio, 0, 1);
        FilledBarLength = new GridLength(normalizedRatio, GridUnitType.Star);
        EmptyBarLength = new GridLength(1 - normalizedRatio, GridUnitType.Star);
        BarBrush = barBrush;
        HasConfigurationError = hasConfigurationError;
    }

    public string Label { get; }
    public int CandidateCount { get; }
    public double Probability { get; }
    public GridLength FilledBarLength { get; }
    public GridLength EmptyBarLength { get; }
    public Brush BarBrush { get; }
    public bool HasConfigurationError { get; }
    public string Summary => HasConfigurationError
        ? $"异常 · {Probability:P1}"
        : $"{CandidateCount}件 · {Probability:P1}";
    public Brush SummaryForeground => HasConfigurationError ? Brushes.Firebrick : Brushes.DimGray;
}
