using System.Globalization;
using MySystem.Core;
using Xunit;

namespace MySystem.Tests;

public sealed class TelemetryTests
{
    private static readonly DateTimeOffset Origin = DateTimeOffset.Parse("2026-01-01T12:00:00Z", CultureInfo.InvariantCulture);
    private static TelemetrySnapshot Sample(int seconds, double? value, MetricAvailability status = MetricAvailability.Available) =>
        new(Origin.AddSeconds(seconds), [new("cpu.usage", "CPU utilization", value, "%", Origin.AddSeconds(seconds), "Test fixture", status)], [], []);

    [Fact]
    public void HistoryRetainsOnlyTheConfiguredCapacity()
    {
        var buffer = new HistoryBuffer(3);
        for (int i = 0; i < 10; i++) buffer.Add(Sample(i, i));
        Assert.Equal(3, buffer.Count);
        Assert.Equal(new[] { 7d, 8d, 9d }, buffer.Select("cpu.usage", Origin).Select(m => m.Value!.Value));
    }

    [Fact]
    public void HistoryRangeUsesInclusiveTimestampsAndPreservesUnavailableValues()
    {
        var buffer = new HistoryBuffer();
        buffer.Add(Sample(0, 80)); buffer.Add(Sample(1, null, MetricAvailability.Unavailable)); buffer.Add(Sample(2, 90));
        var range = buffer.GetRange(Origin.AddSeconds(1), Origin.AddSeconds(2));
        Assert.Equal(2, range.Count);
        Assert.Null(range[0].Find("cpu.usage")!.Value);
        Assert.Null(range[0].Find("unknown"));
    }

    [Fact]
    public void CsvKeepsMissingValueEmptyAndEscapesFormulaText()
    {
        var history = new HistoryBuffer();
        history.Add(new(Origin, [new("x", "=HYPERLINK(\"x\")", null, "°C", Origin, "Fixture", MetricAvailability.Unavailable, "sensor, unavailable")], [], []));
        string csv = history.ExportCsv();
        Assert.Contains("\"'=HYPERLINK(\"\"x\"\")\",,", csv);
        Assert.Contains("\"sensor, unavailable\"", csv);
        Assert.DoesNotContain(",0,", csv);
    }

    [Fact]
    public void SustainedAlertTriggersOnlyOnceThenClearsAndHonorsCooldown()
    {
        var engine = new AlertEngine();
        var rules = new[] { new AlertRule("cpu", "CPU load", "cpu.usage", 80, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(30)) };
        Assert.Empty(engine.Evaluate(Sample(0, 90), rules));
        Assert.Empty(engine.Evaluate(Sample(4, 90), rules));
        Assert.False(Assert.Single(engine.Evaluate(Sample(5, 90), rules)).IsCleared);
        Assert.Empty(engine.Evaluate(Sample(10, 90), rules));
        Assert.True(Assert.Single(engine.Evaluate(Sample(11, 50), rules)).IsCleared);
        Assert.Empty(engine.Evaluate(Sample(12, 90), rules));
        Assert.Empty(engine.Evaluate(Sample(20, 90), rules));
        Assert.False(Assert.Single(engine.Evaluate(Sample(35, 90), rules)).IsCleared);
    }

    [Fact]
    public void MissingSampleBreaksSustainedThreshold()
    {
        var engine = new AlertEngine();
        var rules = new[] { new AlertRule("cpu", "CPU load", "cpu.usage", 80, TimeSpan.FromSeconds(5), TimeSpan.Zero) };
        engine.Evaluate(Sample(0, 90), rules);
        engine.Evaluate(Sample(4, null, MetricAvailability.Unavailable), rules);
        Assert.Empty(engine.Evaluate(Sample(6, 90), rules));
        Assert.Single(engine.Evaluate(Sample(11, 90), rules));
    }

    [Fact]
    public void DisabledAndNonFiniteMetricsNeverTriggerAlerts()
    {
        var engine = new AlertEngine();
        var rule = new AlertRule("cpu", "CPU load", "cpu.usage", 80, TimeSpan.Zero, TimeSpan.Zero);
        Assert.Empty(engine.Evaluate(Sample(0, double.NaN), [rule]));
        Assert.Empty(engine.Evaluate(Sample(1, double.PositiveInfinity), [rule]));
        Assert.Empty(engine.Evaluate(Sample(2, 100), [rule with { Enabled = false }]));
    }

    [Fact]
    public void ChangingThresholdResetsItsPreviousSustainedPeriod()
    {
        var engine = new AlertEngine();
        var rule = new AlertRule("cpu", "CPU load", "cpu.usage", 80, TimeSpan.FromSeconds(5), TimeSpan.Zero);
        engine.Evaluate(Sample(0, 95), [rule]);
        Assert.Empty(engine.Evaluate(Sample(5, 95), [rule with { Threshold = 90 }]));
    }

    [Fact]
    public void BelowThresholdRulesTriggerForLowStorage()
    {
        var engine = new AlertEngine();
        var rule = new AlertRule("low", "Low metric", "cpu.usage", 10, TimeSpan.Zero, TimeSpan.Zero, Comparison: AlertComparison.Below);
        Assert.Single(engine.Evaluate(Sample(0, 5), [rule]));
        Assert.True(Assert.Single(engine.Evaluate(Sample(1, 15), [rule])).IsCleared);
    }

    [Fact]
    public void FrameStatisticsUseTimeWeightedFpsAndSlowestOnePercentMean()
    {
        var frames = Enumerable.Repeat(10d, 198).Concat([30d, 50d]);
        var result = FrameStatistics.Calculate(frames);
        Assert.Equal(200, result.FrameCount);
        Assert.Equal(1000 / 10.3, result.AverageFps!.Value, 6);
        Assert.Equal(25, result.OnePercentLowFps!.Value);
        Assert.Equal(10, result.P99FrameTimeMs!.Value);
    }

    [Fact]
    public void InvalidFrameIntervalsDoNotInventFps()
    {
        var empty = FrameStatistics.Calculate([0, -1, double.NaN, double.PositiveInfinity]);
        Assert.Equal(0, empty.FrameCount);
        Assert.Null(empty.AverageFps);
        Assert.Null(empty.OnePercentLowFps);
        Assert.Equal(50, FrameStatistics.Calculate([20]).AverageFps);
    }
}
