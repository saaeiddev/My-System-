using System.Text;
using System.Text.Json;
using MySystem.Core;

namespace MySystem.Infrastructure;

/// <summary>Local JSONL diagnostics, capped at three 2 MiB files. No telemetry upload exists.</summary>
public sealed class DiagnosticLog
{
    private readonly string _directory;
    private readonly object _gate = new();
    private const long Limit = 2 * 1024 * 1024;
    public DiagnosticLog(string? directory = null) => _directory = Path.Combine(directory ?? AppPaths.DataDirectory, "diagnostics");

    public void Write(string level, string message, Exception? error = null)
    {
        lock (_gate)
        {
            try
            {
                Directory.CreateDirectory(_directory);
                string path = Path.Combine(_directory, "application.jsonl");
                if (File.Exists(path) && new FileInfo(path).Length >= Limit)
                {
                    File.Delete(Path.Combine(_directory, "application.2.jsonl"));
                    if (File.Exists(Path.Combine(_directory, "application.1.jsonl")))
                        File.Move(Path.Combine(_directory, "application.1.jsonl"), Path.Combine(_directory, "application.2.jsonl"));
                    File.Move(path, Path.Combine(_directory, "application.1.jsonl"));
                }
                string LimitText(string? text) => text is null ? "" : text[..Math.Min(text.Length, 8192)];
                var record = new { timestamp = DateTimeOffset.UtcNow, level = LimitText(level), message = LimitText(message),
                    exception = error?.GetType().FullName, detail = LimitText(error?.Message) };
                File.AppendAllText(path, JsonSerializer.Serialize(record, JsonOptions.Compact) + Environment.NewLine, Encoding.UTF8);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* Diagnostics must never take down monitoring. */ }
        }
    }
}

/// <summary>Opt-in metric snapshots only: excludes process executable paths and hardware inventory.</summary>
public sealed class HistoryArchive
{
    private readonly string _directory;
    private readonly object _gate = new();
    private DateTimeOffset _nextCleanup = DateTimeOffset.MinValue;
    private const long MaxDailyBytes = 20 * 1024 * 1024;
    private const long MaxTotalBytes = 100 * 1024 * 1024;
    public string? LastError { get; private set; }
    public HistoryArchive(string? directory = null) => _directory = Path.Combine(directory ?? AppPaths.DataDirectory, "history");

    public void Append(TelemetrySnapshot snapshot, AppSettings settings)
    {
        if (!settings.RetainHistory) return;
        lock (_gate)
        {
            try
            {
                LastError = null;
                Directory.CreateDirectory(_directory);
                DateTimeOffset now = DateTimeOffset.UtcNow;
                if (now >= _nextCleanup)
                {
                    Cleanup(Math.Clamp(settings.RetentionDays, 1, 30), now);
                    _nextCleanup = now.AddMinutes(1);
                }
                string path = Path.Combine(_directory, $"{now:yyyy-MM-dd}.jsonl");
                if (File.Exists(path) && new FileInfo(path).Length >= MaxDailyBytes)
                { LastError = "Today's history reached the 20 MiB cap. Live history continues in memory."; return; }
                var sanitized = snapshot with { Processes = Array.Empty<ProcessSample>(), Inventory = Array.Empty<InventoryItem>() };
                File.AppendAllText(path, JsonSerializer.Serialize(sanitized, JsonOptions.Compact) + Environment.NewLine, Encoding.UTF8);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
            { LastError = ex.Message; }
        }
    }

    private void Cleanup(int retentionDays, DateTimeOffset now)
    {
        var files = new DirectoryInfo(_directory).EnumerateFiles("*.jsonl").OrderByDescending(f => f.Name, StringComparer.Ordinal).ToArray();
        long total = 0;
        foreach (var file in files)
        {
            bool expired = DateOnly.TryParseExact(Path.GetFileNameWithoutExtension(file.Name), "yyyy-MM-dd",
                System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var day)
                && day < DateOnly.FromDateTime(now.UtcDateTime).AddDays(1 - retentionDays);
            if (expired || total + file.Length > MaxTotalBytes) file.Delete();
            else total += file.Length;
        }
    }

    public IReadOnlyList<TelemetrySnapshot> Read(DateTimeOffset start, DateTimeOffset end, int maximumSnapshots = 3600)
    {
        maximumSnapshots = Math.Clamp(maximumSnapshots, 1, 100_000);
        var rows = new List<TelemetrySnapshot>();
        lock (_gate)
        {
            if (!Directory.Exists(_directory)) return rows;
            try
            {
                foreach (string path in Directory.EnumerateFiles(_directory, "*.jsonl").OrderBy(p => p, StringComparer.Ordinal))
                {
                    foreach (string line in File.ReadLines(path))
                    {
                        if (line.Length > 2 * 1024 * 1024) continue;
                        try
                        {
                            var snapshot = JsonSerializer.Deserialize<TelemetrySnapshot>(line, JsonOptions.Compact);
                            if (snapshot is not null && snapshot.Timestamp >= start && snapshot.Timestamp <= end)
                            { rows.Add(snapshot); if (rows.Count >= maximumSnapshots) return rows; }
                        }
                        catch (JsonException) { /* Tolerate a truncated last record after power loss. */ }
                    }
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { LastError = ex.Message; }
        }
        return rows;
    }

    public void Clear()
    {
        lock (_gate)
        {
            if (!Directory.Exists(_directory)) return;
            foreach (string path in Directory.EnumerateFiles(_directory, "*.jsonl")) File.Delete(path);
        }
    }
}
