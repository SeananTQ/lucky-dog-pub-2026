using System.Windows.Media;

namespace LuckyItemLootEditor.Models;

public sealed class RarityCountRow
{
    public RarityCountRow(string label, int count, double barWidth, Brush barBrush)
    {
        Label = label;
        Count = count;
        BarWidth = barWidth;
        BarBrush = barBrush;
    }

    public string Label { get; }
    public int Count { get; }
    public double BarWidth { get; }
    public Brush BarBrush { get; }
}
