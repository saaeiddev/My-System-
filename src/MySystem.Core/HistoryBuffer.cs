using System.Globalization;
using System.Text;

namespace MySystem.Core;

/// <summary>A bounded, thread-safe in-memory session history. Persistence is a separate opt-in concern.</summary>
public sealed class HistoryBuffer
{
    private readonly object _gate = new();
    private readonly Queue<TelemetrySnapshot> _samples = new();
    private readonly int _maximumMetricPoints;
    private int _metricPoints;
    public int Capacity { get; }
    public int Count { get { lock (_gate) return _samples.Count; } }

    public HistoryBuffer(int capacity = 3600, int maximumMetricPoints = 1_000_000)
    {
        if (capacity < 1 || capacity > 1_000_000) throw new ArgumentOutOfRangeException(nameof(capacity));
        if (maximumMetricPoints < 1) throw new ArgumentOutOfRangeException(nameof(maximumMetricPoints));
        Capacity = capacity;
        _maximumMetricPoints = maximumMetricPoints;
    }

    public void Add(TelemetrySnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        lock (_gate)
        {
            _samples.Enqueue(snapshot);
            _metricPoints += snapshot.Metrics.Count;
            // Retain the latest sample even if a single unusually large snapshot exceeds the point budget.
            while (_samples.Count > Capacity || _samples.Count > 1 && _metricPoints > _maximumMetricPoints)
                _metricPoints -= _samples.Dequeue().Metrics.Count;
        }
    }

    public IReadOnlyList<TelemetrySnapshot> GetRange(DateTimeOffset start, DateTimeOffset? end = null)
    {
        lock (_gate) return _samples.Where(s => s.Timestamp >= start && (end == null || s.Timestamp <= end))
            .OrderBy(s => s.Timestamp).ToArray();
    }

    public IReadOnlyList<Metric> Select(string metricId, DateTimeOffset start, DateTimeOffset? end = null) =>
        GetRange(start, end).Select(s => s.Find(metricId)).OfType<Metric>().ToArray();

    public void Clear() { lock (_gate) { _samples.Clear(); _metricPoints = 0; } }

    public string ExportCsv(DateTimeOffset? start = null, DateTimeOffset? end = null)
    {
        var csv = new StringBuilder("timestamp,metric_id,name,value,unit,source,availability,detail\r\n");
        foreach (var sample in GetRange(start ?? DateTimeOffset.MinValue, end))
        foreach (var metric in sample.Metrics)
            csv.AppendJoin(',', Escape(metric.Timestamp.ToString("O", CultureInfo.InvariantCulture)), Escape(metric.Id),
                Escape(metric.Name), metric.Value is { } value && double.IsFinite(value) ? value.ToString("R", CultureInfo.InvariantCulture) : "", Escape(metric.Unit),
                Escape(metric.Source), metric.Availability.ToString(), Escape(metric.Detail ?? "")).Append("\r\n");
        return csv.ToString();
    }

    private static string Escape(string value)
    {
        // Prevent spreadsheet formula execution when names originate in hardware/driver metadata.
        if (value.TrimStart().FirstOrDefault() is '=' or '+' or '-' or '@' or '\t' or '\r') value = "'" + value;
        return "\"" + value.Replace("\"", "\"\"") + "\"";
    }
}
