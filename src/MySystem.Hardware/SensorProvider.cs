using LibreHardwareMonitor.Hardware;
using MySystem.Core;

namespace MySystem.Hardware;

/// <summary>Created only after the user opts in. No driver is installed and no elevation is requested.</summary>
public sealed class SensorProvider : ITelemetryProvider
{
    private Computer? _computer;
    private DateTimeOffset _retryAt;
    private string? _error;
    public string Name => "LibreHardwareMonitorLib 0.9.6";

    public IReadOnlyList<Metric> Collect(DateTimeOffset timestamp)
    {
        if (_computer == null && timestamp >= _retryAt)
        {
            _retryAt = timestamp.AddMinutes(1);
            try
            {
                var computer = new Computer { IsCpuEnabled = true, IsGpuEnabled = true, IsMotherboardEnabled = true,
                    IsStorageEnabled = true, IsMemoryEnabled = false, IsNetworkEnabled = false };
                try { computer.Open(); _computer = computer; _error = null; }
                catch { computer.Close(); throw; }
            }
            catch (Exception error) { _error = error.Message; }
        }
        var metrics = new List<Metric>();
        var gpus = new List<(IHardware Hardware, ISensor[] Sensors)>();
        if (_computer != null)
        {
            foreach (var hardware in _computer.Hardware)
            {
                try
                {
                    Update(hardware, metrics, timestamp);
                    var sensors = Flatten(hardware).SelectMany(h => h.Sensors).ToArray();
                    if (hardware.HardwareType == HardwareType.Cpu)
                    {
                        Alias(metrics, sensors, "cpu.temperature", "CPU package temperature", SensorType.Temperature, "°C", timestamp,
                            s => s.Name.Contains("Package", StringComparison.OrdinalIgnoreCase) || s.Name.Contains("Tctl", StringComparison.OrdinalIgnoreCase));
                        Alias(metrics, sensors, "cpu.power", "CPU package power", SensorType.Power, "W", timestamp,
                            s => s.Name.Contains("Package", StringComparison.OrdinalIgnoreCase));
                        Alias(metrics, sensors, "cpu.clock", "Measured CPU core clock", SensorType.Clock, "MHz", timestamp,
                            s => s.Name.Contains("Core", StringComparison.OrdinalIgnoreCase));
                    }
                    if (hardware.HardwareType is HardwareType.GpuNvidia or HardwareType.GpuAmd or HardwareType.GpuIntel)
                        gpus.Add((hardware, sensors));
                }
                catch (Exception error)
                {
                    _error = error.Message;
                    metrics.Add(Metric.Missing("sensor." + Uri.EscapeDataString(hardware.Identifier.ToString()), hardware.Name,
                        "", timestamp, Name, error.Message, MetricAvailability.Error));
                }
            }
        }
        foreach (var (hardware, sensors) in gpus)
        {
            string prefix = "gpu.device." + Uri.EscapeDataString(hardware.Identifier.ToString());
            AddGpu(metrics, sensors, prefix, hardware.Name, timestamp);
        }
        // Stable hardware enumeration order makes aggregate temperature and VRAM describe the same first adapter.
        if (gpus.Count > 0) AddGpu(metrics, gpus[0].Sensors, "gpu", gpus[0].Hardware.Name, timestamp);
        foreach (var (id, name, unit) in new[] {
            ("cpu.temperature", "CPU package temperature", "°C"), ("cpu.power", "CPU package power", "W"),
            ("gpu.temperature", "GPU temperature", "°C"), ("gpu.memory.total", "GPU dedicated memory capacity", "B") })
            if (metrics.All(m => m.Id != id)) metrics.Add(Metric.Missing(id, name, unit, timestamp, Name,
                _error ?? "N/A — Sensor Not Available. Hardware, driver, access permissions, or the optional PawnIO component may not support this sensor."));
        return metrics;
    }

    private void AddGpu(List<Metric> metrics, ISensor[] sensors, string prefix, string name, DateTimeOffset timestamp)
    {
        Alias(metrics, sensors, prefix + ".temperature", name + " temperature", SensorType.Temperature, "°C", timestamp,
            s => !s.Name.Contains("Hot", StringComparison.OrdinalIgnoreCase) && !s.Name.Contains("Memory", StringComparison.OrdinalIgnoreCase));
        Alias(metrics, sensors, prefix + ".hotspot", name + " hotspot", SensorType.Temperature, "°C", timestamp,
            s => s.Name.Contains("Hot", StringComparison.OrdinalIgnoreCase));
        Alias(metrics, sensors, prefix + ".usage", name + " GPU core utilization", SensorType.Load, "%", timestamp,
            s => s.Name.Contains("Core", StringComparison.OrdinalIgnoreCase) || s.Name.Contains("D3D 3D", StringComparison.OrdinalIgnoreCase));
        Alias(metrics, sensors, prefix + ".clock", name + " GPU clock", SensorType.Clock, "MHz", timestamp,
            s => s.Name.Contains("Core", StringComparison.OrdinalIgnoreCase));
        Alias(metrics, sensors, prefix + ".memory.clock", name + " memory clock", SensorType.Clock, "MHz", timestamp,
            s => s.Name.Contains("Memory", StringComparison.OrdinalIgnoreCase));
        Alias(metrics, sensors, prefix + ".power", name + " GPU power", SensorType.Power, "W", timestamp,
            s => s.Name.Contains("Package", StringComparison.OrdinalIgnoreCase) || s.Name.Contains("Power", StringComparison.OrdinalIgnoreCase));
        Alias(metrics, sensors, prefix + ".fan", name + " fan speed", SensorType.Fan, "RPM", timestamp, _ => true);
        MemoryAlias(metrics, sensors, prefix + ".memory.used", name + " GPU memory used", "Used", timestamp);
        MemoryAlias(metrics, sensors, prefix + ".memory.total", name + " GPU memory total", "Total", timestamp);
    }

    private void MemoryAlias(List<Metric> metrics, ISensor[] sensors, string id, string name, string match, DateTimeOffset timestamp)
    {
        var sensor = sensors.FirstOrDefault(s => s.SensorType is SensorType.SmallData or SensorType.Data &&
            s.Name.Contains("Memory", StringComparison.OrdinalIgnoreCase) && s.Name.Contains(match, StringComparison.OrdinalIgnoreCase) && s.Value.HasValue);
        if (sensor?.Value is { } value && float.IsFinite(value) && value >= 0)
            metrics.Add(new(id, name, value * (sensor.SensorType == SensorType.SmallData ? 1024d * 1024 : 1024d * 1024 * 1024),
                "B", timestamp, Name + " / " + sensor.Identifier, MetricAvailability.Available));
    }

    private void Alias(List<Metric> metrics, ISensor[] sensors, string id, string name, SensorType type, string unit,
        DateTimeOffset timestamp, Func<ISensor, bool> predicate)
    {
        var sensor = sensors.FirstOrDefault(s => s.SensorType == type && predicate(s) && s.Value.HasValue);
        if (sensor?.Value is { } value && float.IsFinite(value))
            metrics.Add(new(id, name, value, unit, timestamp, Name + " / " + sensor.Identifier, MetricAvailability.Available));
    }

    private void Update(IHardware hardware, List<Metric> metrics, DateTimeOffset timestamp)
    {
        hardware.Update();
        foreach (var sensor in hardware.Sensors)
        {
            var value = sensor.Value;
            metrics.Add(new("sensor." + Uri.EscapeDataString(sensor.Identifier.ToString()), hardware.Name + " / " + sensor.Name,
                value.HasValue && float.IsFinite(value.Value) ? value.Value : null, Unit(sensor.SensorType), timestamp,
                Name + " / " + sensor.Identifier, value.HasValue && float.IsFinite(value.Value) ? MetricAvailability.Available : MetricAvailability.Unavailable,
                value.HasValue ? null : "N/A — Sensor Not Available"));
        }
        foreach (var child in hardware.SubHardware) Update(child, metrics, timestamp);
    }
    private static IEnumerable<IHardware> Flatten(IHardware hardware)
    {
        yield return hardware;
        foreach (var child in hardware.SubHardware)
        foreach (var nested in Flatten(child)) yield return nested;
    }
    private static string Unit(SensorType type) => type switch
    {
        SensorType.Temperature => "°C", SensorType.Load or SensorType.Level or SensorType.Control => "%",
        SensorType.Fan => "RPM", SensorType.Clock => "MHz", SensorType.Power => "W", SensorType.Voltage => "V",
        SensorType.Data => "GiB", SensorType.SmallData => "MiB", SensorType.Throughput => "B/s", _ => type.ToString()
    };
    public void Dispose() { _computer?.Close(); _computer = null; }
}
