using System.Globalization;
using System.Text;

namespace MySystem.Core;

/// <summary>A bounded, thread-safe in-memory session history. Persistence is a separate opt-in concern.</summary>
public sealed class HistoryBuffer
{
    private readonly object _gate = new();
    private readonly Queue<TelemetrySnapshot> _samples = new();
    public int Capacity { get; }
    public int Count { get { lock (_gate) return _samples.Count; } }

    public HistoryBuffer(int capacity = 3600)
    {
        if (capacity < 1 || capacity > 1_000_000) throw new ArgumentOutOfRangeException(nameof(capacity));
        Capacity = capacity;
    }

    public void Add(TelemetrySnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        lock (_gate)
        {
            _samples.Enqueue(snapshot);
            while (_samples.Count > Capacity) _samples.Dequeue();
        }
    }

    public IReadOnlyList<TelemetrySnapshot> GetRange(DateTimeOffset start, DateTimeOffset? end = null)
    {
        lock (_gate) return _samples.Where(s => s.Timestamp >= start && (end == null || s.Timestamp <= end))
            .OrderBy(s => s.Timestamp).ToArray();
    }

    public IReadOnlyList<Metric> Select(string metricId, DateTimeOffset start, DateTimeOffset? end = null) =>
        GetRange(start, end).Select(s => s.Find(metricId)).OfType<Metric>().ToArray();

    public void Clear() { lock (_gate) _samples.Clear(); }

    public string ExportCsv(DateTimeOffset? start = null, DateTimeOffset? end = null)
    {
        var csv = new StringBuilder("timestamp,metric_id,name,value,unit,source,availability,detail\r\n");
        foreach (var sample in GetRange(start ?? DateTimeOffset.MinValue, end))
        foreach (var metric in sample.Metrics)
            csv.AppendJoin(',', Escape(metric.Timestamp.ToString("O", CultureInfo.InvariantCulture)), Escape(metric.Id),
                Escape(metric.Name), metric.Value?.ToString("R", CultureInfo.InvariantCulture) ?? "", Escape(metric.Unit),
                Escape(metric.Source), metric.Availability.ToString(), Escape(metric.Detail ?? "")).Append("\r\n");
        return csv.ToString();
    }

    private static string Escape(string value)
    {
        // Prevent spreadsheet formula execution when names originate in hardware/driver metadata.
        if (value.Length > 0 && "=+-@\t\r".Contains(value[0])) value = "'" + value;
        return "\"" + value.Replace("\"", "\"\"") + "\"";
    }
}
