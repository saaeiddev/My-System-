using System.ComponentModel;
using System.Runtime.InteropServices;
using MySystem.Core;

namespace MySystem.Hardware;

public sealed class MemoryProvider : ITelemetryProvider
{
    public string Name => "GlobalMemoryStatusEx";
    public IReadOnlyList<Metric> Collect(DateTimeOffset timestamp)
    {
        var status = new NativeMethods.MemoryStatus { Length = (uint)Marshal.SizeOf<NativeMethods.MemoryStatus>() };
        if (!NativeMethods.GlobalMemoryStatusEx(ref status)) throw new Win32Exception();
        var used = status.TotalPhysical - status.AvailablePhysical;
        return [
            new("memory.total", "Usable physical memory", status.TotalPhysical, "B", timestamp, Name, MetricAvailability.Available),
            new("memory.available", "Available physical memory", status.AvailablePhysical, "B", timestamp, Name, MetricAvailability.Available),
            new("memory.used", "Used physical memory", used, "B", timestamp, Name, MetricAvailability.Available),
            new("memory.usage", "Physical memory usage", status.TotalPhysical == 0 ? null : 100d * used / status.TotalPhysical, "%", timestamp, Name,
                status.TotalPhysical == 0 ? MetricAvailability.Unavailable : MetricAvailability.Available)
        ];
    }
    public void Dispose() { }
}
