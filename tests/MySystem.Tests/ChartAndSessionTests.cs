using MySystem.Core;
using Xunit;

namespace MySystem.Tests;
public class ChartAndSessionTests
{
    static Metric M(int i, double? value, MetricAvailability status = MetricAvailability.Available) => new("cpu.usage", "CPU", value, "%", DateTimeOffset.UnixEpoch.AddSeconds(i), "isolated test fixture", status);
    [Fact]
    public void ReducedSeriesRetainsSpikesWithoutUnboundedGeometry()
    {
        var input = Enumerable.Range(0, 10000).Select(i => M(i, i == 5123 ? 100 : 10)).ToArray();
        var output = ChartSeries.Reduce(input, 900);
        Assert.InRange(output.Count, 1, 900);
        Assert.Contains(output, p => p.Value == 100);
        Assert.Equal(output.OrderBy(p => p.Timestamp), output);
    }

    [Fact]
    public void ReducedSeriesKeepsUnavailableGaps()
    {
        var input = Enumerable.Range(0, 10000).Select(i => M(i, i == 5000 ? null : 10, i == 5000 ? MetricAvailability.Unavailable : MetricAvailability.Available)).ToArray();
        Assert.Contains(ChartSeries.Reduce(input), p => p.Availability == MetricAvailability.Unavailable);
    }

    [Fact]
    public void SessionStatisticsExcludeMissingAndMismatchedUnits()
    {
        var summary = SessionAnalysis.Summarize(new[] { M(0, 10), M(1, 30), M(2, null, MetricAvailability.Unavailable), M(3, double.NaN), M(4, 999)with { Unit = "MHz" } });
        Assert.NotNull(summary);
        Assert.Equal(2, summary.Count);
        Assert.Equal(20, summary.Mean);
        Assert.Equal(10, summary.Minimum);
        Assert.Equal(30, summary.Maximum);
    }

    [Fact]
    public void MissingSessionDoesNotProduceAnInventedMean() => Assert.Null(SessionAnalysis.Summarize(new[] { M(0, null, MetricAvailability.Unavailable) }));
}
