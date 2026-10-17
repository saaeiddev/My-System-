using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.Web.WebView2.Core;
using MySystem.App.Services;
using MySystem.App.ViewModels;
using MySystem.App.Views;
using MySystem.Core;
using MySystem.Infrastructure;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace MySystem.App;
public sealed partial class MainWindow : Window
{
    readonly MainViewModel vm;
    readonly Grid root = new();
    readonly NavigationView nav = new()
    {
        IsBackButtonVisible = NavigationViewBackButtonVisible.Collapsed,
        PaneDisplayMode = NavigationViewPaneDisplayMode.Left,
        IsSettingsVisible = false,
        OpenPaneLength = 210
    };
    readonly StackPanel page = new()
    {
        Spacing = 16,
        Padding = new(26)
    };
    readonly TextBlock status = new()
    {
        FontSize = 12,
        Opacity = .7,
        Margin = new(26, 8, 26, 12)
    };
    readonly WebView2 scene = new()
    {
        MinHeight = 340
    };
    readonly Border sceneBox = new()
    {
        CornerRadius = new(12),
        MinHeight = 340
    };
    readonly Dictionary<string, TextBlock> values = new();
    readonly List<(HistoryChart chart, string metric, double minutes)> charts = new();
    readonly Dictionary<string, (double min, double max)> extrema = new();
    ListView? rows;
    TextBox? processSearch;
    ComboBox? processSort;
    ComboBox? gameSelect;
    TextBlock? gamingStatus;
    HistoryChart? gamingChart;
    TextBlock? gamingValues;
    HistoryChart? historyChart;
    ComboBox? historyMetric;
    ComboBox? historyRange;
    bool historyPaused, sceneReady, closed, shutdownReady, minimized;
    string section = "Overview";
    TrayService? tray;
    OverlayWindow? overlay;
    readonly string[] sections =
    {
        "Overview",
        "CPU",
        "GPU",
        "Memory",
        "Thermals",
        "Gaming",
        "Storage",
        "Network",
        "Processes",
        "System Information",
        "Performance History",
        "Settings"
    };
    public MainWindow(MainViewModel vm, string? smokeDirectory = null)
    {
        this.vm = vm;
        Title = "My System";
        AppWindow.Resize(new Windows.Graphics.SizeInt32(1440, 960));
        SystemBackdrop = new MicaBackdrop();
        root.RowDefinitions.Add(new() { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new() { Height = GridLength.Auto });
        root.Children.Add(nav);
        Grid.SetRow(status, 1);
        root.Children.Add(status);
        Content = root;
        root.RequestedTheme = vm.Settings.Appearance switch
        {
            "light" => ElementTheme.Light,
            "system" => ElementTheme.Default,
            _ => ElementTheme.Dark
        };
        var brand = new StackPanel
        {
            Spacing = 4,
            Margin = new(16, 20, 16, 22)
        };
        brand.Children.Add(new TextBlock { Text = "MY SYSTEM", FontSize = 21, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
        brand.Children.Add(new TextBlock { Text = "HARDWARE / LIVE", FontSize = 10, CharacterSpacing = 200, Opacity = .6 });
        nav.PaneHeader = brand;
        foreach (var name in sections)
            nav.MenuItems.Add(new NavigationViewItem { Content = name, Tag = name, Icon = new FontIcon { Glyph = Glyph(name) } });
        nav.Content = new ScrollViewer
        {
            Content = page,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
        };
        nav.SelectionChanged += (_, e) =>
        {
            if (e.SelectedItem is NavigationViewItem item)
                Navigate((string)item.Tag);
        };
        sceneBox.Child = scene;
        vm.Updated += OnSnapshot;
        vm.Alerted += OnAlerts;
        Closed += (_, _) =>
        {
            closed = true;
            vm.Updated -= OnSnapshot;
            vm.Alerted -= OnAlerts;
            tray?.Dispose();
            overlay?.Close();
            scene.Close();
            vm.Dispose();
        };
        Activated += (_, e) =>
        {
            if (e.WindowActivationState != WindowActivationState.Deactivated)
            {
                minimized = false;
                SendSceneConfig();
                if (vm.Current != null)
                {
                    UpdatePage(vm.Current);
                    SendTelemetry(vm.Current);
                }
            }

            if (tray is null)
            {
                tray = new(WindowNative.GetWindowHandle(this), ShowWindow, () => Close(), ToggleOverlay, OnMinimized);
                tray.Start();
            }
        };
        root.ActualThemeChanged += (_, _) => SendSceneConfig();
        root.Loaded += async (_, _) =>
        {
            await InitSceneAsync();
            if (smokeDirectory != null)
                await RunSmokeAsync(smokeDirectory);
        };
        AppWindow.Closing += async (_, e) =>
        {
            if (shutdownReady)
                return;
            e.Cancel = true;
            await vm.ShutdownAsync();
            shutdownReady = true;
            Close();
        };
        nav.SelectedItem = nav.MenuItems[0];
    }

    static string Glyph(string n) => n switch
    {
        "Overview" => "\uE80F",
        "CPU" => "\uE950",
        "GPU" => "\uE7F4",
        "Memory" => "\uE964",
        "Thermals" => "\uE9CA",
        "Gaming" => "\uE7FC",
        "Storage" => "\uEDA2",
        "Network" => "\uE774",
        "Processes" => "\uE9D9",
        "System Information" => "\uE946",
        "Performance History" => "\uE9D2",
        _ => "\uE713"
    };
    async Task InitSceneAsync()
    {
        try
        {
            var environment = await CoreWebView2Environment.CreateWithOptionsAsync(null, Path.Combine(AppPaths.DataDirectory, "WebView2"), null);
            await scene.EnsureCoreWebView2Async(environment);
            var core = scene.CoreWebView2;
            core.SetVirtualHostNameToFolderMapping("mysystem.local", Path.Combine(AppContext.BaseDirectory, "Scene"), CoreWebView2HostResourceAccessKind.DenyCors);
            core.Settings.AreDevToolsEnabled = false;
            core.Settings.AreDefaultContextMenusEnabled = false;
            core.Settings.IsStatusBarEnabled = false;
            core.Settings.IsPasswordAutosaveEnabled = false;
            core.Settings.IsGeneralAutofillEnabled = false;
            core.NavigationStarting += (_, e) =>
            {
                if (!IsTrustedScene(e.Uri))
                    e.Cancel = true;
            };
            core.NewWindowRequested += (_, e) => e.Handled = true;
            core.PermissionRequested += (_, e) => e.State = CoreWebView2PermissionState.Deny;
            core.DownloadStarting += (_, e) => e.Cancel = true;
            core.AddWebResourceRequestedFilter("*", CoreWebView2WebResourceContext.All);
            core.WebResourceRequested += (_, e) =>
            {
                if (!IsTrustedScene(e.Request.Uri))
                    e.Response = core.Environment.CreateWebResourceResponse(null, 403, "Blocked", "");
            };
            core.WebMessageReceived += (_, e) =>
            {
                if (!IsTrustedScene(e.Source))
                    return;
                try
                {
                    using var d = JsonDocument.Parse(e.WebMessageAsJson);
                    var r = d.RootElement;
                    var type = r.GetProperty("type").GetString();
                    if (type == "ready")
                    {
                        rendererReady = true;
                        return;
                    }

                    if (type == "rendererError")
                    {
                        rendererFailure = r.TryGetProperty("reason", out var reason) ? reason.GetString() : "Unknown renderer error";
                        return;
                    }

                    if (type != "select")
                        return;
                    string? target = r.GetProperty("component").GetString() switch
                    {
                        "cpu" => "CPU",
                        "gpu" => "GPU",
                        "memory" => "Memory",
                        "storage" => "Storage",
                        "cooling" => "Thermals",
                        "motherboard" => "System Information",
                        _ => null
                    };
                    if (target != null)
                        nav.SelectedItem = nav.MenuItems.OfType<NavigationViewItem>().First(n => (string)n.Tag == target);
                }
                catch (JsonException)
                {
                }
                catch (KeyNotFoundException)
                {
                }
            };
            core.NavigationCompleted += (_, e) =>
            {
                sceneReady = e.IsSuccess;
                if (sceneReady)
                {
                    SendSceneConfig();
                    SendSceneSelection();
                    if (vm.Current != null)
                        SendTelemetry(vm.Current);
                }
            };
            scene.Source = new Uri("https://mysystem.local/index.html");
        }
        catch (Exception ex)
        {
            sceneBox.Child = new TextBlock
            {
                Text = $"3D unavailable: {ex.Message}\nInstall Microsoft Edge WebView2 Runtime, then restart. Monitoring remains active.",
                TextWrapping = TextWrapping.Wrap,
                Margin = new(20)
            };
        }
    }

    static bool IsTrustedScene(string uri) => Uri.TryCreate(uri, UriKind.Absolute, out var u) && u.Scheme == "https" && u.Host == "mysystem.local" && u.Port == 443;
    void Send(object message)
    {
        if (sceneReady)
            scene.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(message));
    }

    void SendSceneConfig() => Send(new { type = "config", quality = vm.Settings.RenderQuality, fpsCap = vm.Settings.RenderFps, reducedMotion = vm.Settings.ReducedMotion, paused = minimized || section is not ("Overview" or "CPU" or "GPU" or "Memory" or "Storage" or "Thermals"), appearance = root.ActualTheme == ElementTheme.Light ? "light" : "dark", accent = vm.Settings.Accent });
    void SendSceneSelection() => Send(new { type = "select", component = section switch
    {
        "CPU" => "cpu",
        "GPU" => "gpu",
        "Memory" => "memory",
        "Storage" => "storage",
        "Thermals" => "cooling",
        _ => "overview"
    } });
    void SendTelemetry(TelemetrySnapshot s) => Send(new { type = "telemetry", metrics = s.Metrics.Select(m => new { id = m.Id, value = m.Value, unit = m.Unit, availability = m.Availability.ToString() }), thresholds = new { cpu = vm.Settings.CpuTemperatureThreshold, gpu = vm.Settings.GpuTemperatureThreshold } });
    void OnMinimized()
    {
        minimized = true;
        Send(new { type = "config", paused = true });
        if (vm.Settings.MinimizeToTray && tray?.Available == true)
            AppWindow.Hide();
    }

    void ShowWindow()
    {
        minimized = false;
        AppWindow.Show();
        if (AppWindow.Presenter is OverlappedPresenter presenter && presenter.State == OverlappedPresenterState.Minimized)
            presenter.Restore();
        Activate();
        SendSceneConfig();
    }

    void ToggleOverlay()
    {
        if (overlay is null)
        {
            overlay = new OverlayWindow(vm.Settings);
            overlay.Closed += (_, _) => overlay = null;
            overlay.Activate();
        }
        else
            overlay.Close();
    }

    void OnAlerts(IReadOnlyList<AlertEvent> alerts) => DispatcherQueue.TryEnqueue(() =>
    {
        foreach (var e in alerts.Where(a => !a.IsCleared && vm.Settings.Notifications))
            tray?.Notify(e.Name, $"{Format(e.Metric)}. Your configured threshold, not a hardware safety rating.");
    });
    void OnSnapshot(TelemetrySnapshot s) => DispatcherQueue.TryEnqueue(() =>
    {
        if (closed)
            return;
        status.Text = vm.Status;
        ObserveExtrema(s);
        if (!minimized)
        {
            UpdatePage(s);
            SendTelemetry(s);
        }

        overlay?.Update(s, vm.Gaming.Snapshot);
    });
    void ObserveExtrema(TelemetrySnapshot s)
    {
        foreach (var m in s.Metrics.Where(m => m.Value.HasValue))
        {
            var v = m.Value!.Value;
            extrema[m.Id] = extrema.TryGetValue(m.Id, out var old) ? (Math.Min(old.min, v), Math.Max(old.max, v)) : (v, v);
        }
    }

    void Navigate(string name)
    {
        section = name;
        page.Children.Clear();
        values.Clear();
        charts.Clear();
        rows = null;
        processSearch = null;
        processSort = null;
        gameSelect = null;
        gamingStatus = null;
        gamingValues = null;
        gamingChart = null;
        historyChart = null;
        historyMetric = null;
        historyRange = null;
        historySource = null;
        comparisonText = null;
        var heading = new StackPanel
        {
            Spacing = 6
        };
        heading.Children.Add(new TextBlock { Text = name == "Overview" ? "System command center" : name, FontSize = 30, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
        heading.Children.Add(new TextBlock { Text = Description(name), Opacity = .65, TextWrapping = TextWrapping.Wrap });
        page.Children.Add(heading);
        switch (name)
        {
            case "Overview":
                Metrics(("cpu.usage", "CPU utilization"), ("gpu.usage", "GPU utilization"), ("memory.usage", "Physical RAM"), ("cpu.temperature", "CPU temperature"), ("gpu.temperature", "GPU temperature"), ("disk.activity", "Disk activity"), ("network.download", "Download"), ("network.upload", "Upload"));
                page.Children.Add(sceneBox);
                Chart("cpu.usage", 1);
                break;
            case "CPU":
                Metrics(("cpu.usage", "Total CPU"), ("cpu.clock", "Clock"), ("cpu.temperature", "Package temperature"), ("cpu.power", "Package power"));
                page.Children.Add(sceneBox);
                Chart("cpu.usage", 5);
                AddRows();
                break;
            case "GPU":
                Metrics(("gpu.usage", "Busiest GPU engine"), ("gpu.temperature", "GPU temperature"), ("gpu.memory.used", "Dedicated memory used"), ("gpu.memory.total", "Dedicated memory capacity"));
                page.Children.Add(sceneBox);
                Chart("gpu.usage", 5);
                AddRows();
                break;
            case "Memory":
                Metrics(("memory.total", "Physical RAM capacity"), ("memory.used", "Physical RAM used"), ("memory.available", "Available physical RAM"), ("memory.usage", "Physical RAM utilization"));
                page.Children.Add(sceneBox);
                Chart("memory.usage", 5);
                AddRows();
                break;
            case "Thermals":
                Note("Enable hardware sensors in Settings. No driver is installed and no system elevation is requested. Threshold colors are configurable preferences.");
                page.Children.Add(sceneBox);
                AddRows();
                break;
            case "Storage":
                Metrics(("disk.read", "Read throughput"), ("disk.write", "Write throughput"), ("disk.activity", "Activity"), ("disk.free.percent", "Lowest free volume space"));
                page.Children.Add(sceneBox);
                Chart("disk.read", 5);
                AddRows();
                break;
            case "Network":
                Metrics(("network.download", "Download"), ("network.upload", "Upload"));
                Chart("network.download", 5);
                Chart("network.upload", 5);
                AddRows();
                break;
            case "Gaming":
                BuildGaming();
                break;
            case "Processes":
                BuildProcesses();
                break;
            case "System Information":
                var exports = Buttons();
                exports.Children.Add(Button("Export JSON", async () => await Export("MySystem-specifications", ".json", JsonSerializer.Serialize(vm.Current?.Inventory, new JsonSerializerOptions { WriteIndented = true }))));
                exports.Children.Add(Button("Export TXT", async () => await Export("MySystem-specifications", ".txt", string.Join(Environment.NewLine, vm.Current?.Inventory.Select(i => $"{i.Category} / {i.Name}: {i.Value}") ?? []))));
                AddRows();
                break;
            case "Performance History":
                BuildHistory();
                break;
            case "Settings":
                BuildSettings();
                break;
        }

        SendSceneConfig();
        SendSceneSelection();
        if (vm.Current != null)
            UpdatePage(vm.Current);
    }

    static string Description(string n) => n switch
    {
        "Overview" => "Advanced 3D Hardware Monitoring & System Control Center",
        "CPU" => "Measured processor load, frequency, and supported package sensors.",
        "GPU" => "Individual adapters and available vendor sensors. Missing measurements remain unavailable.",
        "Memory" => "Physical memory accounting: total minus available. Process working sets can share pages.",
        "Thermals" => "Temperature, fan speed, session extrema, and configured alert history.",
        "Gaming" => "Real ETW frame capture with PresentMon. No game injection.",
        "Storage" => "Volume capacity, physical drive inventory, and measured disk I/O.",
        "Network" => "Measured byte deltas from active network interfaces.",
        "Processes" => "CPU normalized to total logical capacity; memory is working set.",
        "System Information" => "Detected Windows and hardware inventory.",
        "Performance History" => "Bounded local session data. Long-term recording is opt-in.",
        _ => "Local preferences, sensor access, presentation, and privacy."
    };
    void Metrics(params (string id, string label)[] metrics)
    {
        var grid = new Grid
        {
            ColumnSpacing = 14,
            RowSpacing = 14
        };
        for (int i = 0; i < 4; i++)
            grid.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
        for (int i = 0; i < metrics.Length; i++)
        {
            if (i % 4 == 0)
                grid.RowDefinitions.Add(new() { Height = GridLength.Auto });
            var m = metrics[i];
            var stack = new StackPanel
            {
                Spacing = 10
            };
            stack.Children.Add(new TextBlock { Text = m.label.ToUpperInvariant(), FontSize = 10, CharacterSpacing = 90, Opacity = .65, TextWrapping = TextWrapping.Wrap });
            var value = new TextBlock
            {
                Text = "N/A",
                FontSize = 25,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                TextWrapping = TextWrapping.Wrap
            };
            values[m.id] = value;
            stack.Children.Add(value);
            var border = new Border
            {
                Child = stack,
                Padding = new(16),
                CornerRadius = new(8),
                BorderThickness = new(0, 2, 0, 0),
                BorderBrush = new SolidColorBrush(ColorFromHex(vm.Settings.Accent)),
                Background = new SolidColorBrush(Windows.UI.Color.FromArgb(15, 140, 160, 160))
            };
            Grid.SetColumn(border, i % 4);
            Grid.SetRow(border, i / 4);
            grid.Children.Add(border);
        }

        page.Children.Add(grid);
    }

    void Chart(string id, double minutes)
    {
        var c = new HistoryChart();
        charts.Add((c, id, minutes));
        page.Children.Add(c);
    }

    void AddRows()
    {
        rows = new ListView
        {
            SelectionMode = ListViewSelectionMode.Single,
            MaxHeight = 550,
            MinHeight = 100
        };
        page.Children.Add(rows);
    }

    void Note(string t) => page.Children.Add(new TextBlock { Text = t, TextWrapping = TextWrapping.Wrap, Opacity = .7, FontSize = 13 });
    StackPanel Buttons()
    {
        var p = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 10
        };
        page.Children.Add(p);
        return p;
    }

    static Button Button(string t, Action a)
    {
        var b = new Button
        {
            Content = t
        };
        b.Click += (_, _) => a();
        return b;
    }

    Button Button(string t, Func<Task> a)
    {
        var b = new Button
        {
            Content = t
        };
        b.Click += async (_, _) =>
        {
            b.IsEnabled = false;
            try
            {
                await a();
            }
            catch (Exception ex)
            {
                status.Text = ex.Message;
                ToolTipService.SetToolTip(b, ex.Message);
            }
            finally
            {
                b.IsEnabled = true;
            }
        };
        return b;
    }

    void BuildProcesses()
    {
        var controls = Buttons();
        processSearch = new TextBox
        {
            PlaceholderText = "Search name or PID",
            Width = 240
        };
        processSort = new ComboBox
        {
            ItemsSource = new[]
            {
                "CPU",
                "Memory",
                "Name"
            },
            SelectedIndex = 0,
            Width = 120
        };
        controls.Children.Add(processSearch);
        controls.Children.Add(processSort);
        processSearch.TextChanged += (_, _) =>
        {
            if (vm.Current != null)
                UpdatePage(vm.Current);
        };
        processSort.SelectionChanged += (_, _) =>
        {
            if (vm.Current != null)
                UpdatePage(vm.Current);
        };
        controls.Children.Add(Button("End selected process…", async () =>
        {
            if (rows?.SelectedItem is not ProcessRow row)
                return;
            using var selectedProcess = Process.GetProcessById(row.Sample.Id);
            var identity = selectedProcess.StartTime;
            var dialog = new ContentDialog
            {
                XamlRoot = root.XamlRoot,
                Title = $"End {row.Sample.Name} ({row.Sample.Id})?",
                Content = "Unsaved data in this process may be lost. Protected processes may reject this request.",
                PrimaryButtonText = "End process",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Close
            };
            if (await dialog.ShowAsync() != ContentDialogResult.Primary)
                return;
            try
            {
                using var process = Process.GetProcessById(row.Sample.Id);
                if (process.StartTime != identity)
                    throw new InvalidOperationException("The selected process exited and its PID was reused. Refresh the process list.");
                process.Kill();
            }
            catch (Exception ex)
            {
                await Message("Process could not be ended", ex.Message);
            }
        }));
        AddRows();
    }

    sealed record ProcessRow(ProcessSample Sample)
    {
        public override string ToString() => $"{Sample.Id, 6}   {Sample.Name, -28}   CPU {Sample.CpuPercent?.ToString("0.0") ?? "N/A", 6}%   RAM {Sample.WorkingSetBytes / 1048576d, 8:0.0} MiB   {Sample.Path ?? "Path unavailable"}";
    }

    void BuildGaming()
    {
        Note("Select a graphics process by name or PID. ETW permissions or protected apps may prevent capture. Exclusive fullscreen overlays are not supported. FPS and lows use a rolling 60-second frame window. Visualization frame rate is unrelated to game FPS.");
        var controls = Buttons();
        gameSelect = new ComboBox
        {
            Width = 320,
            PlaceholderText = "Select process",
            ItemsSource = vm.Current?.Processes.OrderBy(p => p.Name).Select(p => new ProcessRow(p)).ToArray()
        };
        controls.Children.Add(gameSelect);
        controls.Children.Add(Button("Refresh", () =>
        {
            gameSelect.ItemsSource = vm.Current?.Processes.OrderBy(p => p.Name).Select(p => new ProcessRow(p)).ToArray();
        }));
        controls.Children.Add(Button("Start capture", async () =>
        {
            if (gameSelect.SelectedItem is ProcessRow p)
                await vm.Gaming.StartAsync(p.Sample.Id, CancellationToken.None);
        }));
        controls.Children.Add(Button("Stop", () => vm.Gaming.Stop()));
        gamingStatus = new TextBlock
        {
            Text = "Capture not started",
            TextWrapping = TextWrapping.Wrap
        };
        gamingValues = new TextBlock
        {
            FontSize = 26
        };
        page.Children.Add(gamingStatus);
        page.Children.Add(gamingValues);
        gamingChart = new HistoryChart();
        page.Children.Add(gamingChart);
        Metrics(("cpu.usage", "CPU"), ("gpu.usage", "GPU"), ("memory.usage", "RAM"), ("gpu.temperature", "GPU temperature"));
        page.Children.Add(Button("Toggle desktop overlay • Ctrl+Alt+M", ToggleOverlay));
    }

    void UpdatePage(TelemetrySnapshot s)
    {
        foreach (var pair in values)
        {
            var m = s.Find(pair.Key);
            pair.Value.Text = Format(m);
            ToolTipService.SetToolTip(pair.Value, m is null ? "N/A — Sensor Not Available" : $"{m.Source}\n{m.Availability}: {m.Detail}\n{m.Timestamp:O}");
        }

        foreach (var(c, id, minutes)in charts)
            c.SetPoints(vm.History.Select(id, DateTimeOffset.UtcNow.AddMinutes(-minutes)));
        if (rows != null)
        {
            if (section == "Processes")
            {
                int? selected = (rows.SelectedItem as ProcessRow)?.Sample.Id;
                string q = processSearch?.Text ?? "";
                var processes = s.Processes.Where(p => p.Name.Contains(q, StringComparison.OrdinalIgnoreCase) || p.Id.ToString().Contains(q));
                processes = processSort?.SelectedIndex switch
                {
                    1 => processes.OrderByDescending(p => p.WorkingSetBytes),
                    2 => processes.OrderBy(p => p.Name),
                    _ => processes.OrderByDescending(p => p.CpuPercent)};
                var list = processes.Select(p => new ProcessRow(p)).ToArray();
                rows.ItemsSource = list;
                rows.SelectedItem = list.FirstOrDefault(p => p.Sample.Id == selected);
            }
            else if (section == "System Information")
                rows.ItemsSource = s.Inventory.Select(i => $"{i.Category} / {i.Name}\n{i.Value}").ToArray();
            else if (section == "Thermals")
                rows.ItemsSource = s.Metrics.Where(m => m.Unit is "°C" or "C" or "RPM" or "rpm" || m.Id.Contains("temperature")).Select(m => $"{m.Name}   {Format(m)}   {(extrema.TryGetValue(m.Id, out var v) ? $"min {v.min:0.0} / max {v.max:0.0} {m.Unit}" : "")}\n{m.Source} • {m.Availability} • {m.Detail}").Concat(vm.Alerts.History.Reverse().Select(a => $"{a.Timestamp.ToLocalTime():HH:mm:ss} • {a.Name} • {(a.IsCleared ? "Cleared" : "Triggered")} • {Format(a.Metric)}")).ToArray();
            else if (section == "Memory")
                rows.ItemsSource = s.Inventory.Where(i => i.Category.Contains("memory", StringComparison.OrdinalIgnoreCase) || i.Category.Contains("RAM", StringComparison.OrdinalIgnoreCase)).Select(i => $"{i.Name}: {i.Value}").Concat(s.Processes.OrderByDescending(p => p.WorkingSetBytes).Take(20).Select(p => $"{p.Name} ({p.Id}) {p.WorkingSetBytes / 1048576d:0} MiB working set")).ToArray();
            else
            {
                string prefix = section switch
                {
                    "CPU" => "cpu",
                    "GPU" => "gpu",
                    "Storage" => "disk",
                    "Network" => "network",
                    _ => ""
                };
                rows.ItemsSource = s.Inventory.Where(i => i.Category.Contains(section, StringComparison.OrdinalIgnoreCase) || section == "Storage" && i.Category.Contains("Disk", StringComparison.OrdinalIgnoreCase)).Select(i => $"{i.Category} / {i.Name}: {i.Value}").Concat(s.Metrics.Where(m => m.Id.StartsWith(prefix) || section == "GPU" && m.Id.Contains("gpu", StringComparison.OrdinalIgnoreCase)).Select(m => $"{m.Name} {Format(m)}\n{m.Source} • {m.Availability} • {m.Detail}")).ToArray();
            }
        }

        if (gamingStatus != null)
        {
            var g = vm.Gaming.Snapshot;
            gamingStatus.Text = g.Status;
            gamingValues!.Text = $"{g.Fps?.ToString("0.0") ?? "N/A"} FPS • 1% low {g.OnePercentLow?.ToString("0.0") ?? "N/A"} • {g.FrameTimeMs?.ToString("0.00") ?? "N/A"} ms";
            gamingChart!.SetPoints(g.FrameSamples.Select(f => new Metric("frame", "Frame time", f.FrameTimeMs, "ms", f.Timestamp, "PresentMon QPC", MetricAvailability.Available)).ToArray());
        }

        UpdateHistory();
    }

    public string Format(Metric? m)
    {
        if (m?.Value is not double v || m.Availability != MetricAvailability.Available)
            return "N/A";
        if (m.Unit is "bytes" or "B")
            return v >= 1073741824 ? $"{v / 1073741824d:0.0} GiB" : $"{v / 1048576d:0.0} MiB";
        if (m.Unit is "B/s" or "bytes/s")
            return v >= 1048576 ? $"{v / 1048576d:0.00} MiB/s" : $"{v / 1024d:0.0} KiB/s";
        if ((m.Unit is "°C" or "C") && vm.Settings.TemperatureUnit == "F")
            return $"{v * 1.8 + 32:0.0} °F";
        return $"{v:0.0} {m.Unit}";
    }

    static Windows.UI.Color ColorFromHex(string v)
    {
        try
        {
            return Windows.UI.Color.FromArgb(255, byte.Parse(v.Substring(1, 2), NumberStyles.HexNumber), byte.Parse(v.Substring(3, 2), NumberStyles.HexNumber), byte.Parse(v.Substring(5, 2), NumberStyles.HexNumber));
        }
        catch
        {
            return Windows.UI.Color.FromArgb(255, 65, 219, 193);
        }
    }

    async Task Export(string name, string ext, string content)
    {
        var picker = new FileSavePicker
        {
            SuggestedFileName = name
        };
        picker.FileTypeChoices.Add(ext.TrimStart('.').ToUpperInvariant(), new[] { ext });
        InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(this));
        var file = await picker.PickSaveFileAsync();
        if (file != null)
            await File.WriteAllTextAsync(file.Path, content);
    }

    async Task Message(string title, string content) => await new ContentDialog
    {
        XamlRoot = root.XamlRoot,
        Title = title,
        Content = content,
        CloseButtonText = "Close"
    }.ShowAsync();
    void BuildSettings()
    {
        Choice("Appearance", new[] { "dark", "light", "system" }, vm.Settings.Appearance, v =>
        {
            vm.Settings.Appearance = v;
            root.RequestedTheme = v switch
            {
                "light" => ElementTheme.Light,
                "system" => ElementTheme.Default,
                _ => ElementTheme.Dark
            };
        });
        Choice("Hardware accent", new[] { "#41dbc1", "#ffb454", "#79b8ff" }, vm.Settings.Accent, v => vm.Settings.Accent = v);
        Number("Telemetry interval (seconds)", vm.Settings.RefreshSeconds, .5, 10, v => vm.Settings.RefreshSeconds = v);
        Choice("3D quality", new[] { "low", "medium", "high" }, vm.Settings.RenderQuality, v => vm.Settings.RenderQuality = v);
        Number("3D frame cap", vm.Settings.RenderFps, 10, 60, v => vm.Settings.RenderFps = (int)v);
        Toggle("Reduced motion", vm.Settings.ReducedMotion, v => vm.Settings.ReducedMotion = v);
        var sensor = new ToggleSwitch
        {
            Header = "Read supported hardware sensors",
            IsOn = vm.Settings.EnableSensors
        };
        sensor.Toggled += async (_, _) =>
        {
            if (sensor.IsOn && !vm.Settings.EnableSensors)
            {
                var dialog = new ContentDialog
                {
                    XamlRoot = root.XamlRoot,
                    Title = "Enable local hardware sensor access?",
                    Content = "LibreHardwareMonitor will query supported devices. Some CPU sensors require an independently installed PawnIO driver and permissions. My System will not install a driver, elevate itself, change voltages, or control fans. Unsupported readings stay unavailable.",
                    PrimaryButtonText = "Enable sensors",
                    CloseButtonText = "Cancel",
                    DefaultButton = ContentDialogButton.Close
                };
                if (await dialog.ShowAsync() != ContentDialogResult.Primary)
                {
                    sensor.IsOn = false;
                    return;
                }
            }

            vm.Settings.EnableSensors = sensor.IsOn;
            SaveSettings();
        };
        page.Children.Add(sensor);
        Toggle("Minimize to system tray", vm.Settings.MinimizeToTray, v => vm.Settings.MinimizeToTray = v);
        Toggle("Start with Windows (this user)", vm.Settings.StartWithWindows, v =>
        {
            StartupRegistration.SetEnabled(v, Environment.ProcessPath!);
            vm.Settings.StartWithWindows = v;
        });
        Choice("Temperature display", new[] { "C", "F" }, vm.Settings.TemperatureUnit, v => vm.Settings.TemperatureUnit = v);
        Toggle("Enable threshold alerts", vm.Settings.AlertsEnabled, v => vm.Settings.AlertsEnabled = v);
        Toggle("Windows notification banners", vm.Settings.Notifications, v => vm.Settings.Notifications = v);
        Note("Thresholds are personal alert preferences, not universal safe temperatures. Alerts require sustained readings and clear before retriggering.");
        Toggle("CPU temperature alert", vm.Settings.CpuTemperatureAlert, v => vm.Settings.CpuTemperatureAlert = v);
        Number("CPU temperature threshold (°C)", vm.Settings.CpuTemperatureThreshold, 1, 150, v => vm.Settings.CpuTemperatureThreshold = v);
        Toggle("GPU temperature alert", vm.Settings.GpuTemperatureAlert, v => vm.Settings.GpuTemperatureAlert = v);
        Number("GPU temperature threshold (°C)", vm.Settings.GpuTemperatureThreshold, 1, 150, v => vm.Settings.GpuTemperatureThreshold = v);
        Toggle("Memory usage alert", vm.Settings.MemoryAlert, v => vm.Settings.MemoryAlert = v);
        Number("High RAM (%)", vm.Settings.RamThreshold, 1, 100, v => vm.Settings.RamThreshold = v);
        Toggle("CPU load alert", vm.Settings.CpuLoadAlert, v => vm.Settings.CpuLoadAlert = v);
        Number("Sustained CPU load (%)", vm.Settings.CpuLoadThreshold, 1, 100, v => vm.Settings.CpuLoadThreshold = v);
        Toggle("GPU load alert", vm.Settings.GpuLoadAlert, v => vm.Settings.GpuLoadAlert = v);
        Number("Sustained GPU load (%)", vm.Settings.GpuLoadThreshold, 1, 100, v => vm.Settings.GpuLoadThreshold = v);
        Toggle("Low storage alert", vm.Settings.StorageAlert, v => vm.Settings.StorageAlert = v);
        Number("Low free storage (%)", vm.Settings.StorageFreeThreshold, 0, 100, v => vm.Settings.StorageFreeThreshold = v);
        Number("Alert cooldown (seconds)", vm.Settings.AlertCooldownSeconds, 30, 3600, v => vm.Settings.AlertCooldownSeconds = (int)v);
        Toggle("Retain local history on disk", vm.Settings.RetainHistory, v => vm.Settings.RetainHistory = v);
        Number("History retention (days)", vm.Settings.RetentionDays, 1, 30, v => vm.Settings.RetentionDays = (int)v);
        Choice("Overlay preset", new[] { "Compact", "Expanded" }, vm.Settings.OverlayPreset, v => vm.Settings.OverlayPreset = v);
        Toggle("Overlay: game FPS", vm.Settings.OverlayShowFps, v => vm.Settings.OverlayShowFps = v);
        Toggle("Overlay: CPU", vm.Settings.OverlayShowCpu, v => vm.Settings.OverlayShowCpu = v);
        Toggle("Overlay: GPU", vm.Settings.OverlayShowGpu, v => vm.Settings.OverlayShowGpu = v);
        Toggle("Overlay: memory", vm.Settings.OverlayShowMemory, v => vm.Settings.OverlayShowMemory = v);
        Toggle("Overlay: temperatures", vm.Settings.OverlayShowTemperature, v => vm.Settings.OverlayShowTemperature = v);
        Number("Overlay opacity", vm.Settings.OverlayOpacity, .2, 1, v => vm.Settings.OverlayOpacity = v);
        Number("Overlay X (desktop pixels)", vm.Settings.OverlayX, -100000, 100000, v => vm.Settings.OverlayX = (int)v);
        Number("Overlay Y (desktop pixels)", vm.Settings.OverlayY, -100000, 100000, v => vm.Settings.OverlayY = (int)v);
        var actions = Buttons();
        actions.Children.Add(Button("Toggle overlay", ToggleOverlay));
        actions.Children.Add(Button("Clear session history", () => vm.History.Clear()));
        actions.Children.Add(Button("Open local data folder", () =>
        {
            Directory.CreateDirectory(AppPaths.DataDirectory);
            Process.Start(new ProcessStartInfo("explorer.exe", AppPaths.DataDirectory) { UseShellExecute = true });
        }));
        Note($"Privacy: all measurements and preferences remain on this PC. No remote analytics, account, or cloud sync. Diagnostics and optional JSONL history: {AppPaths.DataDirectory}. Retention is capped at 30 days and 100 MiB. My System 0.1.0 is unsigned. Review documented hardware and fullscreen limitations.");
        page.Children.Add(Button("Export diagnostics", async () => await Export("MySystem-diagnostics", ".json", JsonSerializer.Serialize(new { version = "0.1.0", os = Environment.OSVersion.ToString(), vm.Status, metrics = vm.Current?.Metrics.Select(m => new { m.Id, m.Source, m.Availability, m.Detail }), gaming = vm.Gaming.Snapshot.Status }, new JsonSerializerOptions { WriteIndented = true }))));
    }

    void Toggle(string label, bool current, Action<bool> set)
    {
        var c = new ToggleSwitch
        {
            Header = label,
            IsOn = current
        };
        c.Toggled += (_, _) =>
        {
            try
            {
                set(c.IsOn);
                SaveSettings();
            }
            catch (Exception e)
            {
                status.Text = $"Setting could not be saved: {e.Message}";
            }
        };
        page.Children.Add(c);
    }

    void Choice(string label, string[] options, string current, Action<string> set)
    {
        var c = new ComboBox
        {
            Header = label,
            ItemsSource = options,
            SelectedItem = current,
            MinWidth = 250
        };
        c.SelectionChanged += (_, _) =>
        {
            if (c.SelectedItem is string value)
            {
                set(value);
                SaveSettings();
            }
        };
        page.Children.Add(c);
    }

    void Number(string label, double current, double min, double max, Action<double> set)
    {
        var c = new NumberBox
        {
            Header = label,
            Value = current,
            Minimum = min,
            Maximum = max,
            Width = 280,
            HorizontalAlignment = HorizontalAlignment.Left,
            SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact
        };
        c.ValueChanged += (_, e) =>
        {
            if (double.IsFinite(e.NewValue))
            {
                set(e.NewValue);
                SaveSettings();
            }
        };
        page.Children.Add(c);
    }

    void SaveSettings()
    {
        try
        {
            vm.Save();
            SendSceneConfig();
            overlay?.ApplySettings();
        }
        catch (Exception ex)
        {
            status.Text = $"Settings could not be saved: {ex.Message}";
        }
    }
}
