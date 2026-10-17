namespace MySystem.Infrastructure;

public sealed class AppSettings
{
    public double RefreshSeconds { get; set; } = 1;
    public bool EnableSensors { get; set; }
    public string Appearance { get; set; } = "dark";
    public string Accent { get; set; } = "#41dbc1";
    public string RenderQuality { get; set; } = "medium";
    public int RenderFps { get; set; } = 30;
    public bool ReducedMotion { get; set; }
    public bool StartWithWindows { get; set; }
    public bool MinimizeToTray { get; set; } = true;
    public string TemperatureUnit { get; set; } = "C";
    public bool Notifications { get; set; } = true;
    public bool RetainHistory { get; set; }
    public int RetentionDays { get; set; } = 7;
    public double OverlayOpacity { get; set; } = .85;
    public string OverlayPreset { get; set; } = "Compact";
    public int OverlayX { get; set; } = 30;
    public int OverlayY { get; set; } = 30;
    public double CpuTemperatureThreshold { get; set; } = 85;
    public double GpuTemperatureThreshold { get; set; } = 83;
    public double RamThreshold { get; set; } = 90;
    public double CpuLoadThreshold { get; set; } = 95;
    public double GpuLoadThreshold { get; set; } = 95;
    public double StorageFreeThreshold { get; set; } = 10;
    public bool OverlayShowFps { get; set; } = true;
    public bool OverlayShowCpu { get; set; } = true;
    public bool OverlayShowGpu { get; set; } = true;
    public bool OverlayShowMemory { get; set; } = true;
    public bool OverlayShowTemperature { get; set; } = true;
    public bool CpuTemperatureAlert { get; set; } = true;
    public bool GpuTemperatureAlert { get; set; } = true;
    public bool MemoryAlert { get; set; } = true;
    public bool CpuLoadAlert { get; set; } = true;
    public bool GpuLoadAlert { get; set; } = true;
    public bool StorageAlert { get; set; } = true;
    public bool AlertsEnabled { get; set; } = true;
    public int AlertCooldownSeconds { get; set; } = 120;

    public void Validate()
    {
        RefreshSeconds = FiniteClamp(RefreshSeconds, .5, 10, 1);
        Appearance = Choice(Appearance, "dark", "light", "system");
        Accent = Accent is not null && System.Text.RegularExpressions.Regex.IsMatch(Accent, "^#[0-9a-fA-F]{6}$") ? Accent : "#41dbc1";
        RenderQuality = Choice(RenderQuality, "medium", "low", "high");
        RenderFps = Math.Clamp(RenderFps, 10, 60);
        TemperatureUnit = string.Equals(TemperatureUnit, "F", StringComparison.OrdinalIgnoreCase) ? "F" : "C";
        RetentionDays = Math.Clamp(RetentionDays, 1, 30);
        OverlayOpacity = FiniteClamp(OverlayOpacity, .2, 1, .85);
        OverlayPreset = Choice(OverlayPreset, "Compact", "Expanded");
        OverlayX = Math.Clamp(OverlayX, -100_000, 100_000);
        OverlayY = Math.Clamp(OverlayY, -100_000, 100_000);
        CpuTemperatureThreshold = FiniteClamp(CpuTemperatureThreshold, 1, 150, 85);
        GpuTemperatureThreshold = FiniteClamp(GpuTemperatureThreshold, 1, 150, 83);
        RamThreshold = FiniteClamp(RamThreshold, 1, 100, 90);
        CpuLoadThreshold = FiniteClamp(CpuLoadThreshold, 1, 100, 95);
        GpuLoadThreshold = FiniteClamp(GpuLoadThreshold, 1, 100, 95);
        StorageFreeThreshold = FiniteClamp(StorageFreeThreshold, 0, 100, 10);
        AlertCooldownSeconds = Math.Clamp(AlertCooldownSeconds, 30, 3600);
    }

    private static string Choice(string? value, string fallback, params string[] choices)
        => new[] { fallback }.Concat(choices).FirstOrDefault(s => string.Equals(s, value, StringComparison.OrdinalIgnoreCase)) ?? fallback;
    private static double FiniteClamp(double value, double min, double max, double fallback)
        => double.IsFinite(value) ? Math.Clamp(value, min, max) : fallback;
}
