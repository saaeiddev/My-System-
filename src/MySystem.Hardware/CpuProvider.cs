using System.ComponentModel;
using System.Diagnostics;
using MySystem.Core;

namespace MySystem.Hardware;

public sealed class CpuProvider : ITelemetryProvider
{
    private ulong _idle, _kernel, _user;
    private DateTimeOffset? _last;
    private readonly List<(string Instance, PerformanceCounter Counter)> _cores = new();
    private PerformanceCounter? _frequency;
    private PerformanceCounter? _total;
    private bool _countersInitialized;
    private bool _counterPrimed;
    private DateTimeOffset _retryAt;
    public string Name => "CPU";

    public IReadOnlyList<Metric> Collect(DateTimeOffset timestamp)
    {
        var metrics = new List<Metric>();
        if (!_countersInitialized && timestamp >= _retryAt) InitializeCounters(timestamp);
        try
        {
            if (!NativeMethods.GetSystemTimes(out var idle, out var kernel, out var user))
                throw new Win32Exception();
            bool continuous = _last != null && timestamp > _last && timestamp - _last <= TimeSpan.FromSeconds(30);
            ulong totalDelta = kernel.Value >= _kernel && user.Value >= _user ? kernel.Value - _kernel + user.Value - _user : 0;
            ulong idleDelta = idle.Value >= _idle ? idle.Value - _idle : ulong.MaxValue;
            double? usage = continuous && totalDelta > 0 && idleDelta <= totalDelta ? 100d * (totalDelta - idleDelta) / totalDelta : null;
            // GetSystemTimes is scoped to a processor group on machines with >64 CPUs; use the all-group counter there.
            if (Environment.ProcessorCount > 64)
            {
                float? counterValue = _total?.NextValue();
                usage = continuous && _counterPrimed && counterValue is { } number && float.IsFinite(number) ? Math.Clamp(number, 0, 100) : null;
            }
            metrics.Add(new("cpu.usage", "CPU utilization", usage, "%", timestamp,
                Environment.ProcessorCount > 64 ? "Windows Processor Information performance counter" : "GetSystemTimes kernel/user/idle deltas",
                usage.HasValue ? MetricAvailability.Available : MetricAvailability.WarmingUp,
                usage.HasValue ? null : "Requires two consecutive samples. All-group counter required on systems with more than 64 logical processors."));
            _idle = idle.Value; _kernel = kernel.Value; _user = user.Value; _last = timestamp;
        }
        catch (Exception error)
        {
            metrics.Add(Metric.Missing("cpu.usage", "CPU utilization", "%", timestamp, Name, error.Message, MetricAvailability.Error));
        }
        foreach (var (instance, counter) in _cores)
        {
            try
            {
                var value = counter.NextValue();
                metrics.Add(new($"cpu.core.{instance.Replace(',', '.')}.usage", $"Logical processor {instance}",
                    _counterPrimed && float.IsFinite(value) ? Math.Clamp(value, 0, 100) : null, "%", timestamp,
                    "Windows Processor Information performance counter", _counterPrimed ? MetricAvailability.Available : MetricAvailability.WarmingUp));
            }
            catch (Exception error)
            {
                metrics.Add(Metric.Missing($"cpu.core.{instance.Replace(',', '.')}.usage", $"Logical processor {instance}", "%", timestamp,
                    "Windows performance counter", error.Message, MetricAvailability.Error));
            }
        }
        try
        {
            if (_frequency is { } frequency)
            {
                var value = frequency.NextValue();
                if (float.IsFinite(value) && value > 0)
                    metrics.Add(new("cpu.clock", "Reported CPU frequency", value, "MHz", timestamp,
                        "Processor Information / Processor Frequency", MetricAvailability.Available,
                        "Windows-reported frequency; a driver may report nominal frequency rather than instantaneous core clocks."));
            }
        }
        catch { /* Optional clock counter; a hardware sensor can provide the value. */ }
        _counterPrimed = _countersInitialized;
        return metrics;
    }

    private void InitializeCounters(DateTimeOffset now)
    {
        _retryAt = now.AddMinutes(1);
        try
        {
            var category = new PerformanceCounterCategory("Processor Information");
            foreach (var name in category.GetInstanceNames().Where(n => !n.Contains("Total", StringComparison.OrdinalIgnoreCase)).Order())
                _cores.Add((name, new PerformanceCounter("Processor Information", "% Processor Time", name, true)));
            _total = new("Processor Information", "% Processor Time", "_Total", true);
            _frequency = new("Processor Information", "Processor Frequency", "_Total", true);
            _countersInitialized = true;
        }
        catch { DisposeCounters(); }
    }

    private void DisposeCounters()
    {
        foreach (var item in _cores) item.Counter.Dispose();
        _cores.Clear(); _total?.Dispose(); _total = null; _frequency?.Dispose(); _frequency = null;
    }
    public void Dispose() => DisposeCounters();
}
