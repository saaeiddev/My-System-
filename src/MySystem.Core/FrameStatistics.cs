namespace MySystem.Core;

public sealed record FrameStatistics(int FrameCount, double? AverageFps, double? OnePercentLowFps,
    double? AverageFrameTimeMs, double? P99FrameTimeMs)
{
    /// <summary>Average FPS = 1000 / mean frame time. 1% low = 1000 / mean of slowest ceil(N*0.01) frames.</summary>
    public static FrameStatistics Calculate(IEnumerable<double> frameTimesMs)
    {
        var values = frameTimesMs.Where(v => double.IsFinite(v) && v > 0).OrderBy(v => v).ToArray();
        if (values.Length == 0) return new(0, null, null, null, null);
        double average = values.Average();
        int tailCount = Math.Max(1, (int)Math.Ceiling(values.Length * 0.01));
        return new(values.Length, 1000 / average, 1000 / values.TakeLast(tailCount).Average(),
            average, values[Math.Max(0, (int)Math.Ceiling(values.Length * 0.99) - 1)]);
    }
}
