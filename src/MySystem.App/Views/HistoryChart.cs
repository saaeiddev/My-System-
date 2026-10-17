using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using MySystem.Core;
using Windows.Foundation;

namespace MySystem.App.Views;
public sealed class HistoryChart : Grid
{
    readonly Canvas plot = new();
    readonly TextBlock caption = new()
    {
        FontSize = 12,
        Margin = new(8),
        IsHitTestVisible = false
    };
    IReadOnlyList<Metric> points = Array.Empty<Metric>();
    DateTimeOffset? zoomStart, zoomEnd;
    double? dragStart;
    DateTimeOffset start, end;
    public HistoryChart()
    {
        Height = 220;
        MinWidth = 180;
        IsTabStop = true;
        Background = new SolidColorBrush(Windows.UI.Color.FromArgb(16, 80, 150, 140));
        Children.Add(plot);
        Children.Add(caption);
        SizeChanged += (_, _) => Draw();
        PointerPressed += (_, e) =>
        {
            dragStart = e.GetCurrentPoint(this).Position.X;
            CapturePointer(e.Pointer);
        };
        PointerReleased += (_, e) =>
        {
            double x = e.GetCurrentPoint(this).Position.X;
            if (dragStart is double first && Math.Abs(first - x) > 10 && points.Count > 1)
            {
                zoomStart = AtX(Math.Min(first, x));
                zoomEnd = AtX(Math.Max(first, x));
                Draw();
            }

            dragStart = null;
            ReleasePointerCaptures();
        };
        DoubleTapped += (_, _) => ResetZoom();
        KeyDown += (_, e) =>
        {
            if (e.Key == Windows.System.VirtualKey.Escape)
                ResetZoom();
        };
        PointerMoved += (_, e) =>
        {
            if (points.Count == 0)
                return;
            var target = AtX(e.GetCurrentPoint(this).Position.X);
            int lo = 0, hi = points.Count - 1;
            while (lo < hi)
            {
                int mid = (lo + hi) / 2;
                if (points[mid].Timestamp < target)
                    lo = mid + 1;
                else
                    hi = mid;
            }

            var p = points[lo];
            caption.Text = $"{p.Timestamp.ToLocalTime():HH:mm:ss.fff} • {p.Value?.ToString("0.##") ?? "N/A"} {p.Unit}";
        };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(this, "History. Drag to zoom; double click or Escape resets. Hover inspects measured timestamp and value.");
    }

    DateTimeOffset AtX(double x) => start.AddTicks((long)((end - start).Ticks * Math.Clamp(x / Math.Max(1, ActualWidth), 0, 1)));
    public void ResetZoom()
    {
        zoomStart = zoomEnd = null;
        Draw();
    }

    public void SetPoints(IReadOnlyList<Metric> values)
    {
        points = values;
        Draw();
    }

    void Draw()
    {
        plot.Children.Clear();
        if (points.Count == 0)
        {
            caption.Text = "No measurements in this range";
            return;
        }

        start = zoomStart ?? points[0].Timestamp;
        end = zoomEnd ?? points[^1].Timestamp;
        var visible = points.Where(p => p.Timestamp >= start && p.Timestamp <= end).ToArray();
        double w = Math.Max(1, ActualWidth), h = Math.Max(1, ActualHeight - 45);
        double max = Math.Max(1, visible.Where(p => p.Value is { } v && double.IsFinite(v)).Select(p => p.Value!.Value).DefaultIfEmpty(1).Max());
        if (points[0].Unit == "%")
            max = 100;
        for (int n = 1; n <= 4; n++)
            plot.Children.Add(new Line { X1 = 0, X2 = w, Y1 = 35 + h * n / 4, Y2 = 35 + h * n / 4, Stroke = new SolidColorBrush(Windows.UI.Color.FromArgb(25, 150, 180, 180)), StrokeThickness = 1 });
        Polyline? line = null;
        foreach (var p in ChartSeries.Reduce(visible))
        {
            if (p.Value is not double value || !double.IsFinite(value) || p.Availability != MetricAvailability.Available)
            {
                line = null;
                continue;
            }

            if (line is null)
            {
                line = new()
                {
                    Stroke = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 65, 219, 193)),
                    StrokeThickness = 2
                };
                plot.Children.Add(line);
            }

            double x = (p.Timestamp - start).Ticks / (double)Math.Max(1, (end - start).Ticks) * w;
            line.Points.Add(new Point(x, 35 + h - Math.Clamp(value / max, 0, 1) * h));
        }

        caption.Text = $"{start.ToLocalTime():HH:mm:ss} – {end.ToLocalTime():HH:mm:ss} • 0–{max:0.##} {points[0].Unit} • drag to zoom";
    }
}
