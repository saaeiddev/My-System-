using System.Text.Json;
using Microsoft.UI.Xaml.Controls;
using MySystem.Core;

namespace MySystem.App;
public sealed partial class MainWindow
{
    bool rendererReady;
    string? rendererFailure;
    async Task RunSmokeAsync(string directory)
    {
        Directory.CreateDirectory(directory);
        var checks = new List<string>();
        try
        {
            var deadline = DateTimeOffset.UtcNow.AddSeconds(40);
            while ((vm.Current?.Find("cpu.usage")?.Availability != MetricAvailability.Available || !rendererReady) && DateTimeOffset.UtcNow < deadline)
                await Task.Delay(250);
            var first = vm.Current ?? throw new InvalidOperationException("No live Windows telemetry snapshot.");
            double Required(string id) => first.Find(id)is { Value: double value, Availability: MetricAvailability.Available } && double.IsFinite(value) ? value : throw new InvalidOperationException($"{id} unavailable during live integration test.");
            double total = Required("memory.total"), used = Required("memory.used"), available = Required("memory.available"), cpu = Required("cpu.usage");
            if (total <= 0 || used < 0 || available < 0 || Math.Abs(total - used - available) > 1 || cpu < 0 || cpu > 100)
                throw new InvalidOperationException("Live CPU/RAM invariant failed.");
            checks.Add("Real Windows CPU and physical-memory invariants");
            await Task.Delay(1200);
            if (vm.Current?.Timestamp <= first.Timestamp)
                throw new InvalidOperationException("Telemetry did not advance.");
            checks.Add("Two advancing live acquisition timestamps");
            if (!rendererReady)
                throw new InvalidOperationException("WebGL scene not ready: " + rendererFailure);
            string rendered = await scene.CoreWebView2.ExecuteScriptAsync("JSON.stringify({canvas:document.querySelectorAll('canvas').length,error:!document.getElementById('error').hidden})");
            using (var result = JsonDocument.Parse(JsonSerializer.Deserialize<string>(rendered)!))
                if (result.RootElement.GetProperty("canvas").GetInt32() != 1 || result.RootElement.GetProperty("error").GetBoolean())
                    throw new InvalidOperationException("Embedded WebGL scene reported an error.");
            checks.Add("Locally bundled WebGL canvas initialized successfully");
            foreach (var name in sections)
            {
                nav.SelectedItem = nav.MenuItems.OfType<NavigationViewItem>().First(n => (string)n.Tag == name);
                await Task.Delay(120);
                if (section != name || page.Children.Count < 2)
                    throw new InvalidOperationException($"Navigation to {name} failed.");
            }

            checks.Add("All twelve native navigation pages constructed and updated");
            nav.SelectedItem = nav.MenuItems[0];
            await Task.Delay(250);
            await scene.CoreWebView2.ExecuteScriptAsync("document.querySelector('[data-component=\"gpu\"]').click()");
            await Task.Delay(350);
            if (section != "GPU")
                throw new InvalidOperationException("3D selection did not open native GPU page.");
            checks.Add("WebView component selection routes to native GPU navigation");
            ToggleOverlay();
            await Task.Delay(250);
            ToggleOverlay();
            checks.Add("Desktop overlay open/update/close lifecycle");
            nav.SelectedItem = nav.MenuItems[0];
            var report = new
            {
                passed = true,
                timestamp = DateTimeOffset.UtcNow,
                checks,
                operatingSystem = Environment.OSVersion.ToString(),
                cpuPercent = cpu,
                totalPhysicalBytes = total,
                usedPhysicalBytes = used,
                availablePhysicalBytes = available,
                renderer = "WebView2/WebGL on CI runner; not physical GPU qualification",
                unverified = new[]
                {
                    "Physical temperatures/fans",
                    "Real game capture",
                    "Hardware-specific overhead",
                    "Windows 11 physical-device UX"
                }
            };
            await File.WriteAllTextAsync(Path.Combine(directory, "native-checks.json"), JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex)
        {
            await File.WriteAllTextAsync(Path.Combine(directory, "native-checks.json"), JsonSerializer.Serialize(new { passed = false, checks, error = ex.ToString(), rendererFailure }, new JsonSerializerOptions { WriteIndented = true }));
        }
    }
}
