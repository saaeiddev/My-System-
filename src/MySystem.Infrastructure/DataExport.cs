using System.Globalization;
using System.Text;
using System.Text.Json;
using MySystem.Core;

namespace MySystem.Infrastructure;

public static class DataExport
{
    public static void WriteSnapshotJson(string path, TelemetrySnapshot snapshot)
        => AtomicFile.Write(path, JsonSerializer.Serialize(snapshot, JsonOptions.Indented));

    public static void WriteInventoryJson(string path, IEnumerable<InventoryItem> inventory)
        => AtomicFile.Write(path, JsonSerializer.Serialize(new { exportedAt = DateTimeOffset.UtcNow, specifications = inventory.ToArray() }, JsonOptions.Indented));

    public static void WriteInventoryText(string path, IEnumerable<InventoryItem> inventory)
    {
        var text = new StringBuilder($"My System — hardware specifications{Environment.NewLine}Exported UTC: {DateTimeOffset.UtcNow:O}{Environment.NewLine}{Environment.NewLine}");
        foreach (var group in inventory.GroupBy(x => x.Category))
        {
            text.AppendLine(group.Key);
            foreach (var item in group) text.AppendLine($"  {item.Name}: {item.Value}");
            text.AppendLine();
        }
        AtomicFile.Write(path, text.ToString());
    }

    public static void WriteMetricsCsv(string path, IEnumerable<Metric> metrics)
        => AtomicFile.Write(path, MetricsCsv(metrics));

    public static void WriteSnapshotsCsv(string path, IEnumerable<TelemetrySnapshot> snapshots)
        => WriteMetricsCsv(path, snapshots.SelectMany(s => s.Metrics));

    public static string MetricsCsv(IEnumerable<Metric> metrics)
    {
        var text = new StringBuilder("timestamp_utc,metric_id,name,value,unit,source,availability,detail\r\n");
        foreach (var metric in metrics)
        {
            text.Append(Csv(metric.Timestamp.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture))).Append(',')
                .Append(Csv(metric.Id)).Append(',').Append(Csv(metric.Name)).Append(',')
                .Append(metric.Value is { } value && double.IsFinite(value) ? value.ToString("R", CultureInfo.InvariantCulture) : "").Append(',')
                .Append(Csv(metric.Unit)).Append(',').Append(Csv(metric.Source)).Append(',')
                .Append(Csv(metric.Availability.ToString())).Append(',').Append(Csv(metric.Detail ?? "")).Append("\r\n");
        }
        return text.ToString();
    }

    private static string Csv(string value)
    {
        // Protect spreadsheet users from formula interpretation in device- and process-provided text.
        if (value.TrimStart().FirstOrDefault() is '=' or '+' or '-' or '@' or '\t' or '\r') value = "'" + value;
        return "\"" + value.Replace("\"", "\"\"") + "\"";
    }
}
