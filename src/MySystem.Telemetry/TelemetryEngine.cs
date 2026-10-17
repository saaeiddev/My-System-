using System.Diagnostics;
using MySystem.Core;
using MySystem.Hardware;

namespace MySystem.Telemetry;

/// <summary>Serial background collection; rendering frequency is independent of acquisition frequency.</summary>
public sealed class TelemetryEngine : IDisposable, IAsyncDisposable
{
    private readonly ITelemetryProvider[] _providers;
    private readonly ProcessProvider _processes = new();
    private readonly HardwareInventory _inventoryProvider = new();
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly CancellationTokenSource _shutdown = new();
    private readonly bool _enableSensors;
    private IReadOnlyList<InventoryItem> _inventory = [new("System", "Inventory", "Reading Windows hardware inventory…")];
    private Task<IReadOnlyList<InventoryItem>>? _inventoryTask;
    private DateTimeOffset _nextInventory;
    private volatile bool _disposed;

    public TelemetryEngine(bool enableSensors = false)
    {
        _enableSensors = enableSensors;
        var providers = new List<ITelemetryProvider> { new CpuProvider(), new MemoryProvider(), new GpuCounterProvider(), new StorageProvider(), new NetworkProvider() };
        if (enableSensors) providers.Add(new SensorProvider());
        _providers = providers.ToArray();
    }

    public async Task<TelemetrySnapshot> CollectAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _shutdown.Token);
        await _gate.WaitAsync(linked.Token).ConfigureAwait(false);
        try
        {
            return await Task.Run(() => Collect(linked.Token), linked.Token).ConfigureAwait(false);
        }
        finally { _gate.Release(); }
    }

    private TelemetrySnapshot Collect(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var now = DateTimeOffset.UtcNow;
        var metrics = new Dictionary<string, Metric>(StringComparer.Ordinal);
        foreach (var provider in _providers)
        {
            token.ThrowIfCancellationRequested();
            try
            {
                foreach (var point in provider.Collect(now))
                {
                    // A failed optional sensor cannot replace a functioning operating-system counter.
                    if (!metrics.TryGetValue(point.Id, out var previous) || point.Availability == MetricAvailability.Available || previous.Availability != MetricAvailability.Available)
                        metrics[point.Id] = point;
                }
            }
            catch (Exception error)
            {
                Trace.TraceWarning("Telemetry provider {0}: {1}", provider.Name, error.Message);
                metrics["provider." + provider.Name + ".status"] = Metric.Missing("provider." + provider.Name + ".status", provider.Name,
                    "", now, provider.Name, error.Message, MetricAvailability.Error);
            }
        }
        foreach (var (id, name, unit) in RequiredMetrics)
        {
            if (metrics.ContainsKey(id)) continue;
            bool sensor = id is "cpu.temperature" or "cpu.power" or "gpu.temperature" or "gpu.memory.total";
            metrics[id] = Metric.Missing(id, name, unit, now, sensor ? "Hardware sensors" : "Windows telemetry",
                sensor && !_enableSensors ? "N/A — Sensor Not Available. Optional hardware sensor access is disabled in Settings." :
                "N/A — Sensor Not Available. The device, driver, counter, or current permissions do not expose this measurement.");
        }
        IReadOnlyList<ProcessSample> processes;
        try { processes = _processes.Collect(); }
        catch (Exception error) { Trace.TraceWarning("Process enumeration: {0}", error.Message); processes = []; }
        if (_inventoryTask?.IsCompleted == true)
        {
            try { _inventory = _inventoryTask.GetAwaiter().GetResult(); }
            catch (OperationCanceledException) { }
            catch (Exception error) { _inventory = [new("System", "Inventory unavailable", error.Message)]; }
            _inventoryTask = null;
        }
        if (_inventoryTask == null && now >= _nextInventory)
        {
            _nextInventory = now.AddSeconds(60);
            _inventoryTask = Task.Run(() => _inventoryProvider.Collect(_shutdown.Token), _shutdown.Token);
        }
        token.ThrowIfCancellationRequested();
        return new(now, metrics.Values.ToArray(), processes, _inventory);
    }

    private static readonly (string Id, string Name, string Unit)[] RequiredMetrics = [
        ("cpu.usage", "CPU utilization", "%"), ("cpu.clock", "CPU frequency", "MHz"),
        ("cpu.temperature", "CPU package temperature", "°C"), ("cpu.power", "CPU package power", "W"),
        ("memory.used", "Used physical memory", "B"), ("memory.available", "Available physical memory", "B"),
        ("memory.total", "Usable physical memory", "B"), ("memory.usage", "Physical memory usage", "%"),
        ("gpu.usage", "GPU utilization", "%"), ("gpu.temperature", "GPU temperature", "°C"),
        ("gpu.memory.used", "Dedicated GPU memory used", "B"), ("gpu.memory.total", "Dedicated GPU memory capacity", "B"),
        ("disk.read", "Disk read throughput", "B/s"), ("disk.write", "Disk write throughput", "B/s"),
        ("disk.activity", "Disk activity", "%"), ("disk.free.percent", "Disk free space", "%"),
        ("network.download", "Network download", "B/s"), ("network.upload", "Network upload", "B/s") ];

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _shutdown.Cancel();
        _gate.Wait();
        try { foreach (var provider in _providers) { try { provider.Dispose(); } catch { } } }
        finally { _gate.Release(); }
        // Inventory is read-only and cooperative; do not block the UI waiting for a WMI timeout.
        if (_inventoryTask is { } inventory)
            _ = inventory.ContinueWith(task => { _ = task.Exception; _shutdown.Dispose(); }, CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        else _shutdown.Dispose();
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        _shutdown.Cancel();
        await _gate.WaitAsync().ConfigureAwait(false);
        try { foreach (var provider in _providers) { try { provider.Dispose(); } catch { } } }
        finally { _gate.Release(); }
        if (_inventoryTask != null) { try { await _inventoryTask.ConfigureAwait(false); } catch { } }
        _shutdown.Dispose();
    }
}
