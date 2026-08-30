using System.Windows;
using System.Windows.Media;

namespace LuckyStats.Desktop;

public sealed record ChartPoint(DateTimeOffset At, double Value);

public sealed class TrendChart : FrameworkElement
{
    public static readonly DependencyProperty PointsProperty = DependencyProperty.Register(
        nameof(Points), typeof(IReadOnlyList<ChartPoint>), typeof(TrendChart),
        new FrameworkPropertyMetadata(Array.Empty<ChartPoint>(), FrameworkPropertyMetadataOptions.AffectsRender));

    public IReadOnlyList<ChartPoint> Points
    {
        get => (IReadOnlyList<ChartPoint>)GetValue(PointsProperty);
        set => SetValue(PointsProperty, value);
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);
        var width = ActualWidth;
        var height = ActualHeight;
        drawingContext.DrawRoundedRectangle(Brushes.White, new Pen(new SolidColorBrush(Color.FromRgb(216, 222, 228)), 1),
            new Rect(0, 0, width, height), 4, 4);
        if (width < 120 || height < 100)
            return;

        const double left = 58;
        const double right = 18;
        const double top = 20;
        const double bottom = 38;
        var plot = new Rect(left, top, width - left - right, height - top - bottom);
        var axisPen = new Pen(new SolidColorBrush(Color.FromRgb(140, 150, 158)), 1);
        var gridPen = new Pen(new SolidColorBrush(Color.FromRgb(232, 236, 239)), 1);
        drawingContext.DrawLine(axisPen, plot.BottomLeft, plot.TopLeft);
        drawingContext.DrawLine(axisPen, plot.BottomLeft, plot.BottomRight);

        var typeface = new Typeface("Segoe UI");
        if (Points.Count == 0)
        {
            DrawText(drawingContext, "尚无历史快照", typeface, 14, Brushes.Gray,
                new Point(plot.Left + plot.Width / 2 - 45, plot.Top + plot.Height / 2));
            return;
        }

        var min = Points.Min(x => x.Value);
        var max = Points.Max(x => x.Value);
        if (Math.Abs(max - min) < 0.001)
        {
            min = Math.Max(0, min - 1);
            max += 1;
        }
        else
        {
            var padding = (max - min) * 0.08;
            min = Math.Max(0, min - padding);
            max += padding;
        }

        for (var i = 0; i <= 4; i++)
        {
            var y = plot.Bottom - plot.Height * i / 4;
            drawingContext.DrawLine(gridPen, new Point(plot.Left, y), new Point(plot.Right, y));
            var value = min + (max - min) * i / 4;
            DrawText(drawingContext, value.ToString("0.#"), typeface, 11, Brushes.DimGray, new Point(6, y - 8));
        }

        var start = Points.First().At;
        var end = Points.Last().At;
        var span = Math.Max(1, (end - start).TotalSeconds);
        var geometry = new StreamGeometry();
        using (var context = geometry.Open())
        {
            for (var i = 0; i < Points.Count; i++)
            {
                var point = Points[i];
                var x = plot.Left + plot.Width * (point.At - start).TotalSeconds / span;
                if (Points.Count == 1)
                    x = plot.Left + plot.Width / 2;
                var y = plot.Bottom - plot.Height * (point.Value - min) / (max - min);
                if (i == 0)
                    context.BeginFigure(new Point(x, y), false, false);
                else
                    context.LineTo(new Point(x, y), true, false);
            }
        }
        geometry.Freeze();
        var linePen = new Pen(new SolidColorBrush(Color.FromRgb(45, 106, 101)), 2.5);
        drawingContext.DrawGeometry(null, linePen, geometry);

        foreach (var point in Points)
        {
            var x = Points.Count == 1
                ? plot.Left + plot.Width / 2
                : plot.Left + plot.Width * (point.At - start).TotalSeconds / span;
            var y = plot.Bottom - plot.Height * (point.Value - min) / (max - min);
            drawingContext.DrawEllipse(new SolidColorBrush(Color.FromRgb(45, 106, 101)), null, new Point(x, y), 3.5, 3.5);
        }

        DrawText(drawingContext, start.LocalDateTime.ToString("MM-dd HH:mm"), typeface, 11, Brushes.DimGray,
            new Point(plot.Left, plot.Bottom + 8));
        var endText = end.LocalDateTime.ToString("MM-dd HH:mm");
        DrawText(drawingContext, endText, typeface, 11, Brushes.DimGray,
            new Point(plot.Right - 70, plot.Bottom + 8));
    }

    private static void DrawText(
        DrawingContext context,
        string text,
        Typeface typeface,
        double size,
        Brush brush,
        Point origin)
    {
        var formatted = new FormattedText(text, System.Globalization.CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight, typeface, size, brush, 1.0);
        context.DrawText(formatted, origin);
    }
}
