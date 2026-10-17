using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using MySystem.App.Views;
using MySystem.Core;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace MySystem.App;
public sealed partial class MainWindow
{
    ComboBox? historySource;
    TextBlock? comparisonText;
    DatePicker? retainedDate;
    IReadOnlyList<TelemetrySnapshot> retained = Array.Empty<TelemetrySnapshot>();
    IReadOnlyList<TelemetrySnapshot> baseline = Array.Empty<TelemetrySnapshot>();
    void BuildHistory()
    {
        var controls = Buttons();
        historyMetric = new ComboBox
        {
            Width = 230,
            ItemsSource = vm.Current?.Metrics.Select(m => m.Id).Distinct().Order().ToArray(),
            SelectedItem = "cpu.usage"
        };
        historyRange = new ComboBox
        {
            Width = 140,
            ItemsSource = new[]
            {
                "60 seconds",
                "5 minutes",
                "30 minutes",
                "1 hour"
            },
            SelectedIndex = 0
        };
        historySource = new ComboBox
        {
            Width = 160,
            ItemsSource = new[]
            {
                "Live session",
                "Retained day"
            },
            SelectedIndex = 0
        };
        controls.Children.Add(historyMetric);
        controls.Children.Add(historyRange);
        controls.Children.Add(historySource);
        controls.Children.Add(Button("Pause / resume", () => historyPaused = !historyPaused));
        var dates = Buttons();
        retainedDate = new DatePicker
        {
            Header = "Retained local date",
            Date = DateTimeOffset.Now
        };
        dates.Children.Add(retainedDate);
        dates.Children.Add(Button("Load day", async () =>
        {
            var start = new DateTimeOffset(retainedDate.Date.LocalDateTime.Date);
            var end = new DateTimeOffset(start.LocalDateTime.AddDays(1));
            retained = await Task.Run(() => vm.Archive.Read(start, end, 14400));
            historySource.SelectedIndex = 1;
            historyPaused = false;
            UpdateHistory();
            status.Text = $"Loaded {retained.Count} retained samples. Retention may be limited by the configured time and storage caps.";
        }));
        var exports = Buttons();
        exports.Children.Add(Button("Export CSV", async () =>
        {
            var samples = VisibleSnapshots();
            var buffer = new HistoryBuffer(14400);
            foreach (var s in samples)
                buffer.Add(s);
            await Export("MySystem-history", ".csv", await Task.Run(() => buffer.ExportCsv()));
        }));
        exports.Children.Add(Button("Export session JSON", async () =>
        {
            var samples = VisibleSnapshots();
            await Export("MySystem-session", ".json", await Task.Run(() => JsonSerializer.Serialize(samples)));
        }));
        exports.Children.Add(Button("Use range as baseline", () =>
        {
            baseline = VisibleSnapshots();
            UpdateHistory();
        }));
        exports.Children.Add(Button("Load baseline JSON", LoadBaselineAsync));
        historyChart = new HistoryChart
        {
            Height = 330
        };
        page.Children.Add(historyChart);
        comparisonText = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap
        };
        page.Children.Add(comparisonText);
        historyMetric.SelectionChanged += (_, _) =>
        {
            historyChart.ResetZoom();
            UpdateHistory();
        };
        historyRange.SelectionChanged += (_, _) =>
        {
            historyChart.ResetZoom();
            UpdateHistory();
        };
        historySource.SelectionChanged += (_, _) =>
        {
            historyChart.ResetZoom();
            UpdateHistory();
        };
        Note("Drag to zoom, double click or Escape to reset. Hover shows measured timestamps. Baseline comparison uses sample means, excludes unavailable readings and only compares matching units. A retained day shows stored samples; logging is off by default.");
    }

    IReadOnlyList<TelemetrySnapshot> VisibleSnapshots()
    {
        if (historySource?.SelectedIndex == 1)
            return retained;
        double minutes = historyRange?.SelectedIndex switch
        {
            1 => 5,
            2 => 30,
            3 => 60,
            _ => 1
        };
        return vm.History.GetRange(DateTimeOffset.UtcNow.AddMinutes(-minutes));
    }

    void UpdateHistory()
    {
        if (historyPaused || historyChart is null || historyMetric?.SelectedItem is not string id)
            return;
        var current = VisibleSnapshots().Select(s => s.Find(id)).OfType<Metric>().ToArray();
        historyChart.SetPoints(current);
        var now = SessionAnalysis.Summarize(current);
        var prior = SessionAnalysis.Summarize(baseline.Select(s => s.Find(id)).OfType<Metric>());
        if (comparisonText is null)
            return;
        comparisonText.Text = now is null ? "No supported measurements in this range." : $"Current: {now.Count} samples • mean {now.Mean:0.##} • min {now.Minimum:0.##} • max {now.Maximum:0.##} {now.Unit}";
        if (prior is not null && now is not null && prior.Unit == now.Unit)
            comparisonText.Text += $"\nBaseline: {prior.Count} samples • mean {prior.Mean:0.##} • min {prior.Minimum:0.##} • max {prior.Maximum:0.##} {prior.Unit}\nMean difference: {now.Mean - prior.Mean:+0.##;-0.##;0} {now.Unit}";
        else if (baseline.Count > 0)
            comparisonText.Text += "\nBaseline has no comparable available measurements for this metric/unit.";
    }

    async Task LoadBaselineAsync()
    {
        var picker = new FileOpenPicker();
        picker.FileTypeFilter.Add(".json");
        InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(this));
        var file = await picker.PickSingleFileAsync();
        if (file is null)
            return;
        if (new FileInfo(file.Path).Length > 32 * 1024 * 1024)
            throw new InvalidDataException("Session files are limited to 32 MiB.");
        var parsed = await Task.Run(() => JsonSerializer.Deserialize<List<TelemetrySnapshot>>(File.ReadAllText(file.Path), new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? []);
        if (parsed.Count > 14400 || parsed.Any(s => s is null || s.Metrics is null || s.Metrics.Count > 4096 || s.Metrics.Any(m => m is null || m.Id is null || m.Unit is null)))
            throw new InvalidDataException("Invalid or oversized session data.");
        baseline = parsed;
        UpdateHistory();
    }
}
