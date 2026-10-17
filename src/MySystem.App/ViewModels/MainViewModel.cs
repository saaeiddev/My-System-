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
    readonly DiagnosticLog log = new();
    public HistoryArchive Archive { get; } = new();
    public AppSettings Settings { get; }
    public TelemetrySnapshot? Current { get; private set; }
    public HistoryBuffer History { get; } = new(14400);
    public AlertEngine Alerts { get; } = new();
    public PresentMonCapture Gaming { get; } = new(Path.Combine(AppContext.BaseDirectory,"Tools","PresentMon.exe"));
    public event PropertyChangedEventHandler? PropertyChanged;
    public event Action<TelemetrySnapshot>? Updated;
    public event Action<IReadOnlyList<AlertEvent>>? Alerted;
    public string Status { get; private set; } = "Starting real system providers…";
    public MainViewModel(SettingsStore store)
    {
        this.store=store; Settings=store.Load(); loop=Task.Run(PollAsync);
    }
    public void Save() { store.Save(Settings); PropertyChanged?.Invoke(this,new(nameof(Settings))); }
    public IEnumerable<AlertRule> Rules() => new[] {
        new AlertRule("cpu-temp","CPU temperature","cpu.temperature",Settings.CpuTemperatureThreshold,TimeSpan.FromSeconds(5),TimeSpan.FromSeconds(Settings.AlertCooldownSeconds),Settings.AlertsEnabled),
        new AlertRule("gpu-temp","GPU temperature","gpu.temperature",Settings.GpuTemperatureThreshold,TimeSpan.FromSeconds(5),TimeSpan.FromSeconds(Settings.AlertCooldownSeconds),Settings.AlertsEnabled),
        new AlertRule("memory","High physical RAM use","memory.usage",Settings.RamThreshold,TimeSpan.FromSeconds(15),TimeSpan.FromSeconds(Settings.AlertCooldownSeconds),Settings.AlertsEnabled),
        new AlertRule("cpu-load","Sustained CPU load","cpu.usage",Settings.CpuLoadThreshold,TimeSpan.FromSeconds(30),TimeSpan.FromSeconds(Settings.AlertCooldownSeconds),Settings.AlertsEnabled),
        new AlertRule("gpu-load","Sustained GPU load","gpu.usage",Settings.GpuLoadThreshold,TimeSpan.FromSeconds(30),TimeSpan.FromSeconds(Settings.AlertCooldownSeconds),Settings.AlertsEnabled),
        new AlertRule("storage","Low available storage","disk.free.percent",Settings.StorageFreeThreshold,TimeSpan.FromSeconds(15),TimeSpan.FromSeconds(Settings.AlertCooldownSeconds),Settings.AlertsEnabled,AlertComparison.Below)
    };
    async Task PollAsync()
    {
        while(!shutdown.IsCancellationRequested)
        {
            try
            {
                if(engine is null || lastSensors!=Settings.EnableSensors) { engine?.Dispose(); lastSensors=Settings.EnableSensors; engine=new(lastSensors); }
                var sample=await engine.CollectAsync(shutdown.Token);
                Current=sample; History.Add(sample with { Processes = Array.Empty<ProcessSample>(), Inventory = Array.Empty<InventoryItem>() }); Archive.Append(sample,Settings);
                var alerts=Alerts.Evaluate(sample,Rules()); if(alerts.Count>0) Alerted?.Invoke(alerts);
                Status=$"Live • {sample.Timestamp.ToLocalTime():HH:mm:ss} • {sample.Metrics.Count(m=>m.Availability==MetricAvailability.Available)} available measurements";
                Updated?.Invoke(sample);
            }
            catch(OperationCanceledException) { break; }
            catch(Exception ex) { Status=$"Provider recovery: {ex.Message}"; log.Write("warning",Status,ex); }
            try { await Task.Delay(TimeSpan.FromSeconds(Math.Clamp(Settings.RefreshSeconds,.5,10)),shutdown.Token); }
            catch(OperationCanceledException) { break; }
        }
        engine?.Dispose();
    }
    public void Dispose() { shutdown.Cancel(); Gaming.Dispose(); /* Provider cleanup is owned by the polling loop. */ }
}
