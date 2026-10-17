using System.Diagnostics;
using MySystem.Core;

namespace MySystem.Hardware;

public sealed class ProcessProvider
{
    private sealed record Previous(long StartTicks, TimeSpan Cpu, long SampleTick);
    private Dictionary<int, Previous> _previous = new();
    private readonly Dictionary<int, (long StartTicks, string? Path)> _paths = new();

    public IReadOnlyList<ProcessSample> Collect()
    {
        var result = new List<ProcessSample>();
        var next = new Dictionary<int, Previous>();
        var processes = Process.GetProcesses();
        foreach (var process in processes)
        {
            using (process)
            {
                try
                {
                    var id = process.Id;
                    var name = process.ProcessName;
                    var memory = process.WorkingSet64;
                    double? cpu = null;
                    string? path = null;
                    try
                    {
                        long start = process.StartTime.ToUniversalTime().Ticks;
                        var elapsed = process.TotalProcessorTime;
                        long tick = Stopwatch.GetTimestamp();
                        if (_previous.TryGetValue(id, out var previous) && previous.StartTicks == start)
                        {
                            var seconds = (tick - previous.SampleTick) / (double)Stopwatch.Frequency;
                            var delta = (elapsed - previous.Cpu).TotalSeconds;
                            if (seconds is > 0 and <= 30 && delta >= 0)
                                cpu = Math.Clamp(100 * delta / seconds / Environment.ProcessorCount, 0, 100);
                        }
                        next[id] = new(start, elapsed, tick);
                        if (_paths.TryGetValue(id, out var cached) && cached.StartTicks == start) path = cached.Path;
                        else
                        {
                            try { path = process.MainModule?.FileName; } catch { }
                            _paths[id] = (start, path);
                        }
                    }
                    catch { /* A protected process can still expose its name and working set. */ }
                    result.Add(new(id, name, cpu, memory, path));
                }
                catch { /* Process exited while enumerating, or Windows denied access. */ }
            }
        }
        _previous = next;
        foreach (var id in _paths.Keys.Where(id => !next.ContainsKey(id)).ToArray()) _paths.Remove(id);
        return result.OrderByDescending(p => p.CpuPercent ?? -1).ThenByDescending(p => p.WorkingSetBytes).ToArray();
    }
}
