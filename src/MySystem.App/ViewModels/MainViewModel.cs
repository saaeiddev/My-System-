using System.ComponentModel;
using MySystem.Core;
using MySystem.Telemetry;
using MySystem.Gaming;
using MySystem.Infrastructure;

namespace MySystem.App.ViewModels;
public sealed class MainViewModel : INotifyPropertyChanged, IDisposable
{
    readonly SettingsStore store;
    readonly CancellationTokenSource shutdown = new();
    TelemetryEngine? engine;
    bool lastSensors;
    readonly Task loop;
    AppSettings runtimeSettings;
    int disposed;
    readonly DiagnosticLog log = new();
    public HistoryArchive Archive { get; } = new();
    public AppSettings Settings { get; }
    public TelemetrySnapshot? Current { get; private set; }
    public HistoryBuffer History { get; } = new(14400);
    public AlertEngine Alerts { get; } = new();
    public PresentMonCapture Gaming { get; } = new(Path.Combine(AppContext.BaseDirectory, "Tools", "PresentMon.exe"));

    public event PropertyChangedEventHandler? PropertyChanged;
    public event Action<TelemetrySnapshot>? Updated;
    public event Action<IReadOnlyList<AlertEvent>>? Alerted;
    public string Status { get; private set; } = "Starting real system providers…";

    public MainViewModel(SettingsStore store)
    {
        this.store = store;
        Settings = store.Load();
        runtimeSettings = CloneSettings(Settings);
        loop = Task.Run(PollAsync);
    }

    static AppSettings CloneSettings(AppSettings settings) => System.Text.Json.JsonSerializer.Deserialize<AppSettings>(System.Text.Json.JsonSerializer.Serialize(settings))!;
    public void Save()
    {
        store.Save(Settings);
        Interlocked.Exchange(ref runtimeSettings, CloneSettings(Settings));
        PropertyChanged?.Invoke(this, new(nameof(Settings)));
    }

    public IEnumerable<AlertRule> Rules() => Rules(Volatile.Read(ref runtimeSettings));
    static IEnumerable<AlertRule> Rules(AppSettings options) => new[]
    {
        new AlertRule("cpu-temp", "CPU temperature", "cpu.temperature", options.CpuTemperatureThreshold, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(options.AlertCooldownSeconds), options.AlertsEnabled && options.CpuTemperatureAlert),
        new AlertRule("gpu-temp", "GPU temperature", "gpu.temperature", options.GpuTemperatureThreshold, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(options.AlertCooldownSeconds), options.AlertsEnabled && options.GpuTemperatureAlert),
        new AlertRule("memory", "High physical RAM use", "memory.usage", options.RamThreshold, TimeSpan.FromSeconds(15), TimeSpan.FromSeconds(options.AlertCooldownSeconds), options.AlertsEnabled && options.MemoryAlert),
        new AlertRule("cpu-load", "Sustained CPU load", "cpu.usage", options.CpuLoadThreshold, TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(options.AlertCooldownSeconds), options.AlertsEnabled && options.CpuLoadAlert),
        new AlertRule("gpu-load", "Sustained GPU load", "gpu.usage", options.GpuLoadThreshold, TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(options.AlertCooldownSeconds), options.AlertsEnabled && options.GpuLoadAlert),
        new AlertRule("storage", "Low available storage", "disk.free.percent", options.StorageFreeThreshold, TimeSpan.FromSeconds(15), TimeSpan.FromSeconds(options.AlertCooldownSeconds), options.AlertsEnabled && options.StorageAlert, AlertComparison.Below)
    };
    async Task PollAsync()
    {
        while (!shutdown.IsCancellationRequested)
        {
            try
            {
                var options = Volatile.Read(ref runtimeSettings);
                if (engine is null || lastSensors != options.EnableSensors)
                {
                    engine?.Dispose();
                    lastSensors = options.EnableSensors;
                    engine = new(lastSensors);
                }

                var sample = await engine.CollectAsync(shutdown.Token);
                Current = sample;
                History.Add(sample with { Processes = Array.Empty<ProcessSample>(), Inventory = Array.Empty<InventoryItem>() });
                Archive.Append(sample, options);
                var alerts = Alerts.Evaluate(sample, Rules(options));
                if (alerts.Count > 0)
                    Alerted?.Invoke(alerts);
                Status = $"Live • {sample.Timestamp.ToLocalTime():HH:mm:ss} • {sample.Metrics.Count(m => m.Availability == MetricAvailability.Available)} available measurements";
                Updated?.Invoke(sample);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                Status = $"Provider recovery: {ex.Message}";
                log.Write("warning", Status, ex);
            }

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(Math.Clamp(Volatile.Read(ref runtimeSettings).RefreshSeconds, .5, 10)), shutdown.Token);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        engine?.Dispose();
    }

    public async Task ShutdownAsync()
    {
        Dispose();
        try
        {
            await Task.WhenAll(loop, Gaming.ShutdownAsync()).WaitAsync(TimeSpan.FromSeconds(8));
        }
        catch (TimeoutException)
        {
            log.Write("warning", "Shutdown exceeded eight seconds; operating system will release remaining handles.");
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0)
            return;
        shutdown.Cancel();
        Gaming.Dispose();
    }
}
