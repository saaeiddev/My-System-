using System.Globalization;
using System.Text;

namespace MySystem.Gaming;

public sealed record PresentedFrame(int ProcessId, string SwapChain, double FrameTimeMs)
{
    public long? QpcTimestamp { get; init; }
}

/// <summary>Parses the explicitly requested PresentMon v1 CSV schema; diagnostics are never treated as data.</summary>
public sealed class PresentMonCsvParser
{
    private Dictionary<string, int>? _columns;

    public PresentedFrame? Parse(string line)
    {
        if (line.Length > 65_536) return null;
        var values = SplitCsv(line.TrimStart('\uFEFF'));
        if (values is null) return null;
        if (values.Any(s => s.Equals("MsBetweenPresents", StringComparison.OrdinalIgnoreCase)) &&
            values.Any(s => s.Equals("ProcessID", StringComparison.OrdinalIgnoreCase)))
        {
            _columns = new(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < values.Count; i++) _columns[values[i].Trim()] = i;
            return null;
        }
        if (_columns is null || !TryGet(values, "ProcessID", out var process) ||
            !TryGet(values, "MsBetweenPresents", out var interval) ||
            !TryGet(values, "SwapChainAddress", out var chain) ||
            !int.TryParse(process, NumberStyles.None, CultureInfo.InvariantCulture, out int pid) || pid <= 0 ||
            !double.TryParse(interval, NumberStyles.Float, CultureInfo.InvariantCulture, out double ms) ||
            !double.IsFinite(ms) || ms <= 0) return null;
        long? qpc = TryGet(values, "QPCTime", out string timestamp) &&
            long.TryParse(timestamp, NumberStyles.None, CultureInfo.InvariantCulture, out long ticks) && ticks > 0 ? ticks : null;
        return new(pid, chain, ms) { QpcTimestamp = qpc };
    }

    private bool TryGet(IReadOnlyList<string> values, string name, out string value)
    {
        value = "";
        if (_columns is null || !_columns.TryGetValue(name, out int i) || i >= values.Count) return false;
        value = values[i].Trim();
        return true;
    }

    public static IReadOnlyList<string>? SplitCsv(string line)
    {
        var fields = new List<string>();
        var field = new StringBuilder();
        bool quoted = false;
        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];
            if (c == '"')
            {
                if (quoted && i + 1 < line.Length && line[i + 1] == '"') { field.Append('"'); i++; }
                else quoted = !quoted;
            }
            else if (c == ',' && !quoted) { fields.Add(field.ToString()); field.Clear(); }
            else field.Append(c);
        }
        if (quoted) return null;
        fields.Add(field.ToString());
        return fields;
    }
}
