using System.Runtime.InteropServices;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using MySystem.Core;
using MySystem.Gaming;
using MySystem.Infrastructure;

namespace MySystem.App.Views;
public sealed class OverlayWindow : Window
{
    readonly AppSettings settings;
    readonly TextBlock text = new()
    {
        FontFamily = new FontFamily("Consolas"),
        FontSize = 15,
        Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 220, 245, 240)),
        Margin = new(16)
    };
    public OverlayWindow(AppSettings s)
    {
        settings = s;
        Title = "My System Overlay";
        Content = new Border
        {
            Background = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 17, 23, 25)),
            Child = text
        };
        var p = (OverlappedPresenter)AppWindow.Presenter;
        p.IsAlwaysOnTop = true;
        p.IsResizable = false;
        p.IsMaximizable = false;
        p.IsMinimizable = false;
        p.SetBorderAndTitleBar(false, false);
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        SetWindowLongPtr(hwnd, -20, GetWindowLongPtr(hwnd, -20) | (nint)0x08000080);
        ApplySettings();
    }

    public void ApplySettings()
    {
        ((FrameworkElement)Content).Opacity = settings.OverlayOpacity;
        AppWindow.MoveAndResize(new Windows.Graphics.RectInt32(settings.OverlayX, settings.OverlayY, 700, settings.OverlayPreset == "Expanded" ? 132 : 66));
    }

    public void Update(TelemetrySnapshot s, GamingSnapshot g)
    {
        string M(string id) => s.Find(id)is { Value: double v, Availability: MetricAvailability.Available } ? v.ToString("0") : "N/A";
        string T(string id) => s.Find(id)is { Value: double v, Availability: MetricAvailability.Available } ? (settings.TemperatureUnit == "F" ? $"{v * 1.8 + 32:0} °F" : $"{v:0} °C") : "N/A";
        var metrics = new List<string>();
        if (settings.OverlayShowFps)
            metrics.Add($"FPS {g.Fps?.ToString("0") ?? "N/A"}");
        if (settings.OverlayShowCpu)
            metrics.Add($"CPU {M("cpu.usage")}%");
        if (settings.OverlayShowGpu)
            metrics.Add($"GPU {M("gpu.usage")}%");
        if (settings.OverlayShowMemory)
            metrics.Add($"RAM {M("memory.usage")}%");
        if (settings.OverlayShowTemperature)
            metrics.Add($"GPU {T("gpu.temperature")}");
        text.Text = metrics.Count > 0 ? string.Join(" | ", metrics) : "My System • no overlay metrics selected";
        if (settings.OverlayPreset == "Expanded")
            text.Text += $"\nCPU temperature {T("cpu.temperature")}\n1% low {g.OnePercentLow?.ToString("0") ?? "N/A"} | {g.FrameTimeMs?.ToString("0.0") ?? "N/A"} ms\nCtrl+Alt+M to hide";
    }

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    static extern nint GetWindowLongPtr(nint hwnd, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    static extern nint SetWindowLongPtr(nint hwnd, int index, nint value);
}
