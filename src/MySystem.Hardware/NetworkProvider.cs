using System.Diagnostics;
using System.Net.NetworkInformation;
using MySystem.Core;

namespace MySystem.Hardware;

public sealed class NetworkProvider : ITelemetryProvider
{
    private sealed record Counters(long Received, long Sent, long Tick);
    private Dictionary<string, Counters> _previous = new();
    public string Name => "NetworkInterface.GetIPStatistics byte deltas";

    public IReadOnlyList<Metric> Collect(DateTimeOffset timestamp)
    {
        var metrics = new List<Metric>();
        var current = new Dictionary<string, Counters>();
        var rates = new List<(double Down, double Up)>();
        var adapters = NetworkInterface.GetAllNetworkInterfaces()
            .Where(n => n.NetworkInterfaceType != NetworkInterfaceType.Loopback && n.OperationalStatus == OperationalStatus.Up).ToArray();
        foreach (var adapter in adapters)
        {
            string id = "network.adapter." + Uri.EscapeDataString(adapter.Id);
            try
            {
                var stats = adapter.GetIPStatistics();
                var tick = Stopwatch.GetTimestamp();
                current[adapter.Id] = new(stats.BytesReceived, stats.BytesSent, tick);
                double? download = null, upload = null;
                if (_previous.TryGetValue(adapter.Id, out var previous))
                {
                    var seconds = (tick - previous.Tick) / (double)Stopwatch.Frequency;
                    if (seconds is > 0 and <= 30 && stats.BytesReceived >= previous.Received && stats.BytesSent >= previous.Sent)
                    {
                        download = (stats.BytesReceived - previous.Received) / seconds;
                        upload = (stats.BytesSent - previous.Sent) / seconds;
                        rates.Add((download.Value, upload.Value));
                    }
                }
                var availability = download.HasValue ? MetricAvailability.Available : MetricAvailability.WarmingUp;
                metrics.Add(new(id + ".download", adapter.Name + " download", download, "B/s", timestamp, Name, availability));
                metrics.Add(new(id + ".upload", adapter.Name + " upload", upload, "B/s", timestamp, Name, availability));
                metrics.Add(new(id + ".received", adapter.Name + " received since adapter reset", stats.BytesReceived, "B", timestamp, Name, MetricAvailability.Available));
                metrics.Add(new(id + ".sent", adapter.Name + " sent since adapter reset", stats.BytesSent, "B", timestamp, Name, MetricAvailability.Available));
                metrics.Add(new(id + ".link.speed", adapter.Name + " negotiated link speed", adapter.Speed > 0 ? adapter.Speed : null,
                    "bit/s", timestamp, "NetworkInterface.Speed", adapter.Speed > 0 ? MetricAvailability.Available : MetricAvailability.Unavailable));
            }
            catch (Exception error)
            {
                metrics.Add(Metric.Missing(id + ".download", adapter.Name + " download", "B/s", timestamp, Name, error.Message, MetricAvailability.Error));
            }
        }
        _previous = current;
        bool measured = rates.Count > 0;
        var status = measured ? MetricAvailability.Available : adapters.Length > 0 ? MetricAvailability.WarmingUp : MetricAvailability.Unavailable;
        string? detail = adapters.Length == 0 ? "No connected network adapter is available." :
            !measured ? "Waiting for a second byte-counter sample." : "Sum across active adapters; VPN/virtual adapters may count the same traffic twice. Inspect individual adapters for interface rates.";
        metrics.Add(new("network.download", "All active adapters download", measured ? rates.Sum(r => r.Down) : null, "B/s", timestamp, Name, status, detail));
        metrics.Add(new("network.upload", "All active adapters upload", measured ? rates.Sum(r => r.Up) : null, "B/s", timestamp, Name, status, detail));
        return metrics;
    }
    public void Dispose() { }
}
