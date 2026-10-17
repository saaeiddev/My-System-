namespace MySystem.Core;
public static class ChartSeries
{
    /// <summary>Bounds drawing work while retaining each bucket's extrema and measurement gaps.</summary>
    public static IReadOnlyList<Metric> Reduce(IReadOnlyList<Metric> values, int budget = 900)
    {
        if (budget < 6)
            throw new ArgumentOutOfRangeException(nameof(budget));
        if (values.Count <= budget)
            return values;
        int bucketSize = (int)Math.Ceiling(values.Count / (double)(budget / 3));
        var result = new List<Metric>(budget);
        for (int offset = 0; offset < values.Count; offset += bucketSize)
        {
            var bucket = values.Skip(offset).Take(bucketSize).ToArray();
            var gap = bucket.FirstOrDefault(p => p.Availability != MetricAvailability.Available || p.Value is null || !double.IsFinite(p.Value.Value));
            if (gap != null)
            {
                result.Add(gap);
                continue;
            }

            var min = bucket.MinBy(p => p.Value)!;
            var max = bucket.MaxBy(p => p.Value)!;
            result.AddRange(new[] { min, max }.Distinct().OrderBy(p => p.Timestamp));
        }

        return result;
    }
}

public sealed record MetricSummary(string Id, string Unit, int Count, double Minimum, double Maximum, double Mean);
public static class SessionAnalysis
{
    public static MetricSummary? Summarize(IEnumerable<Metric> points)
    {
        var valid = points.Where(p => p.Availability == MetricAvailability.Available && p.Value is { } v && double.IsFinite(v)).ToArray();
        if (valid.Length == 0)
            return null;
        var first = valid[0];
        valid = valid.Where(p => p.Id == first.Id && p.Unit == first.Unit).ToArray();
        return new(first.Id, first.Unit, valid.Length, valid.Min(p => p.Value!.Value), valid.Max(p => p.Value!.Value), valid.Average(p => p.Value!.Value));
    }
}
