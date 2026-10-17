using System.Diagnostics;
using MySystem.Core;

namespace MySystem.Hardware;

public sealed class StorageProvider : ITelemetryProvider
{
    private sealed class CounterSet : IDisposable
    {
        public readonly PerformanceCounter Read, Write, Idle;
        public bool Primed;
        public CounterSet(string instance)
        {
            Read = new("LogicalDisk", "Disk Read Bytes/sec", instance, true);
            Write = new("LogicalDisk", "Disk Write Bytes/sec", instance, true);
            Idle = new("LogicalDisk", "% Idle Time", instance, true);
        }
        public void Dispose() { Read.Dispose(); Write.Dispose(); Idle.Dispose(); }
    }
    private readonly Dictionary<string, CounterSet> _counters = new();
    public string Name => "Windows LogicalDisk performance counters / DriveInfo";

    public IReadOnlyList<Metric> Collect(DateTimeOffset timestamp)
    {
        var metrics = new List<Metric>();
        var present = new HashSet<string>();
        var reads = new List<double>(); var writes = new List<double>(); var activities = new List<double>();
        double total = 0, free = 0;
        foreach (var drive in DriveInfo.GetDrives().Where(d => d.DriveType == DriveType.Fixed))
        {
            var name = drive.Name.TrimEnd('\\');
            present.Add(name);
            string id = "disk.volume." + Uri.EscapeDataString(name);
            try
            {
                if (!drive.IsReady) continue;
                long size = drive.TotalSize, available = drive.TotalFreeSpace;
                total += size; free += available;
                metrics.Add(new(id + ".total", name + " capacity", size, "B", timestamp, "DriveInfo", MetricAvailability.Available));
                metrics.Add(new(id + ".free", name + " free space", available, "B", timestamp, "DriveInfo", MetricAvailability.Available));
                metrics.Add(new(id + ".used", name + " used space", size - available, "B", timestamp, "DriveInfo", MetricAvailability.Available));
                metrics.Add(new(id + ".free.percent", name + " free space", size > 0 ? 100d * available / size : null,
                    "%", timestamp, "DriveInfo", size > 0 ? MetricAvailability.Available : MetricAvailability.Unavailable));
                if (!_counters.TryGetValue(name, out var counters)) _counters[name] = counters = new(name);
                double read = counters.Read.NextValue(), write = counters.Write.NextValue(), idle = counters.Idle.NextValue();
                var state = counters.Primed ? MetricAvailability.Available : MetricAvailability.WarmingUp;
                double? readValue = counters.Primed && double.IsFinite(read) ? Math.Max(0, read) : null;
                double? writeValue = counters.Primed && double.IsFinite(write) ? Math.Max(0, write) : null;
                double? activity = counters.Primed && double.IsFinite(idle) ? Math.Clamp(100 - idle, 0, 100) : null;
                metrics.Add(new(id + ".read", name + " read rate", readValue, "B/s", timestamp, Name, state));
                metrics.Add(new(id + ".write", name + " write rate", writeValue, "B/s", timestamp, Name, state));
                metrics.Add(new(id + ".activity", name + " activity", activity, "%", timestamp, Name, state));
                if (readValue is { } r) reads.Add(r);
                if (writeValue is { } w) writes.Add(w);
                if (activity is { } a) activities.Add(a);
                counters.Primed = true;
            }
            catch (Exception error)
            {
                if (_counters.Remove(name, out var old)) old.Dispose();
                metrics.Add(Metric.Missing(id + ".read", name + " read rate", "B/s", timestamp, Name, error.Message, MetricAvailability.Error));
            }
        }
        foreach (var name in _counters.Keys.Where(k => !present.Contains(k)).ToArray()) { _counters[name].Dispose(); _counters.Remove(name); }
        metrics.Add(new("disk.read", "Volume read throughput", reads.Count > 0 ? reads.Sum() : null, "B/s", timestamp, Name,
            reads.Count > 0 ? MetricAvailability.Available : MetricAvailability.Unavailable, "Sum of measured fixed volumes."));
        metrics.Add(new("disk.write", "Volume write throughput", writes.Count > 0 ? writes.Sum() : null, "B/s", timestamp, Name,
            writes.Count > 0 ? MetricAvailability.Available : MetricAvailability.Unavailable, "Sum of measured fixed volumes."));
        metrics.Add(new("disk.activity", "Busiest volume activity", activities.Count > 0 ? activities.Max() : null, "%", timestamp, Name,
            activities.Count > 0 ? MetricAvailability.Available : MetricAvailability.Unavailable, "Maximum of 100 minus measured idle time across volumes."));
        metrics.Add(new("disk.free.percent", "Fixed-volume free space", total > 0 ? free / total * 100 : null, "%", timestamp, "DriveInfo",
            total > 0 ? MetricAvailability.Available : MetricAvailability.Unavailable, "Capacity-weighted free space across fixed volumes."));
        return metrics;
    }
    public void Dispose() { foreach (var value in _counters.Values) value.Dispose(); _counters.Clear(); }
}
