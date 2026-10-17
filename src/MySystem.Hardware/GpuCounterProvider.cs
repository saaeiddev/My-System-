using System.Diagnostics;
using System.Text.RegularExpressions;
using MySystem.Core;

namespace MySystem.Hardware;

/// <summary>WDDM counters work without low-level hardware access. Utilization follows the busiest-engine convention.</summary>
public sealed partial class GpuCounterProvider : ITelemetryProvider
{
    private sealed record Entry(string Adapter, string Engine, PerformanceCounter Counter)
    { public bool Primed { get; set; } }
    private readonly Dictionary<string, Entry> _engines = new();
    private readonly Dictionary<string, PerformanceCounter> _memory = new();
    private DateTimeOffset _nextDiscovery;
    private string? _error;
    public string Name => "Windows WDDM GPU Engine / GPU Adapter Memory counters";

    [GeneratedRegex(@"luid_(.+?)_phys_(\d+)_eng_(\d+)_engtype_(.+)$")]
    private static partial Regex EngineName();

    public IReadOnlyList<Metric> Collect(DateTimeOffset timestamp)
    {
        if (timestamp >= _nextDiscovery) Discover(timestamp);
        var metrics = new List<Metric>();
        var engines = new Dictionary<(string Adapter, string Engine), double>();
        foreach (var item in _engines.Values)
        {
            try
            {
                double value = item.Counter.NextValue();
                if (item.Primed && double.IsFinite(value))
                {
                    var key = (item.Adapter, item.Engine);
                    engines[key] = engines.GetValueOrDefault(key) + Math.Max(0, value);
                }
                item.Primed = true;
            }
            catch { _nextDiscovery = timestamp.AddSeconds(2); }
        }
        var adapters = engines.GroupBy(pair => pair.Key.Adapter)
            .Select(group => (Id: group.Key, Usage: Math.Clamp(group.Max(pair => pair.Value), 0, 100))).ToArray();
        foreach (var adapter in adapters)
            metrics.Add(new("gpu.adapter." + Uri.EscapeDataString(adapter.Id) + ".usage", "GPU " + adapter.Id + " busiest engine",
                adapter.Usage, "%", timestamp, Name, MetricAvailability.Available,
                "Busiest WDDM engine, summing process contexts on each engine. Adapter id is the Windows LUID."));
        var selected = adapters.OrderByDescending(a => a.Usage).FirstOrDefault();
        metrics.Add(new("gpu.usage", "Busiest GPU engine", adapters.Length > 0 ? selected.Usage : null, "%", timestamp, Name,
            adapters.Length > 0 ? MetricAvailability.Available : _engines.Count > 0 ? MetricAvailability.WarmingUp : MetricAvailability.Unavailable,
            adapters.Length > 0 ? "Busiest engine across detected adapters; inspect per-adapter readings for multiple GPUs." : _error ?? "WDDM GPU counters are unavailable or require a second sample."));
        var memory = new List<double>();
        foreach (var (name, counter) in _memory)
        {
            try
            {
                double value = counter.NextValue();
                if (!double.IsFinite(value) || value < 0) continue;
                memory.Add(value);
                metrics.Add(new("gpu.adapter." + Uri.EscapeDataString(name) + ".memory.used", "GPU " + name + " dedicated memory used",
                    value, "B", timestamp, Name, MetricAvailability.Available));
            }
            catch { _nextDiscovery = timestamp.AddSeconds(2); }
        }
        if (memory.Count > 0)
            metrics.Add(new("gpu.memory.used", "Dedicated GPU memory used (all adapters)", memory.Sum(), "B", timestamp, Name,
                MetricAvailability.Available, "WDDM dedicated allocation usage, summed across adapters. Shared system memory is excluded."));
        return metrics;
    }

    private void Discover(DateTimeOffset timestamp)
    {
        _nextDiscovery = timestamp.AddSeconds(15);
        try
        {
            var names = new PerformanceCounterCategory("GPU Engine").GetInstanceNames().ToHashSet();
            foreach (var old in _engines.Keys.Where(k => !names.Contains(k)).ToArray()) { _engines[old].Counter.Dispose(); _engines.Remove(old); }
            foreach (var name in names.Where(n => !_engines.ContainsKey(n)))
            {
                var match = EngineName().Match(name);
                if (!match.Success) continue;
                string adapter = "luid_" + match.Groups[1].Value + "_phys_" + match.Groups[2].Value;
                _engines[name] = new(adapter, match.Groups[3].Value, new("GPU Engine", "Utilization Percentage", name, true));
            }
            var memories = new PerformanceCounterCategory("GPU Adapter Memory").GetInstanceNames().ToHashSet();
            foreach (var old in _memory.Keys.Where(k => !memories.Contains(k)).ToArray()) { _memory[old].Dispose(); _memory.Remove(old); }
            foreach (var name in memories.Where(n => !_memory.ContainsKey(n)))
                _memory[name] = new("GPU Adapter Memory", "Dedicated Usage", name, true);
            _error = null;
        }
        catch (Exception error) { _error = error.Message; }
    }

    public void Dispose()
    {
        foreach (var item in _engines.Values) item.Counter.Dispose();
        foreach (var item in _memory.Values) item.Dispose();
        _engines.Clear(); _memory.Clear();
    }
}
