namespace MySystem.Core;

public enum MetricAvailability { Available, Unavailable, Error, WarmingUp }

public sealed record Metric(string Id, string Name, double? Value, string Unit,
    DateTimeOffset Timestamp, string Source, MetricAvailability Availability, string? Detail = null)
{
    public static Metric Missing(string id, string name, string unit, DateTimeOffset timestamp,
        string source, string detail, MetricAvailability availability = MetricAvailability.Unavailable) =>
        new(id, name, null, unit, timestamp, source, availability, detail);
}

public sealed record ProcessSample(int Id, string Name, double? CpuPercent, long WorkingSetBytes, string? Path);
public sealed record InventoryItem(string Category, string Name, string Value);
public sealed record TelemetrySnapshot(DateTimeOffset Timestamp, IReadOnlyList<Metric> Metrics,
    IReadOnlyList<ProcessSample> Processes, IReadOnlyList<InventoryItem> Inventory)
{
    public Metric? Find(string id) => Metrics.FirstOrDefault(metric => metric.Id == id);
}

public interface ITelemetryProvider : IDisposable
{
    string Name { get; }
    IReadOnlyList<Metric> Collect(DateTimeOffset timestamp);
}
