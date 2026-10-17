using Microsoft.Win32;

namespace MySystem.Infrastructure;

public static class StartupRegistration
{
    private const string Key = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string Name = "MySystem";

    public static bool IsEnabled
    {
        get
        {
            if (!OperatingSystem.IsWindows()) return false;
            using var key = Registry.CurrentUser.OpenSubKey(Key, writable: false);
            return key?.GetValue(Name) is string { Length: > 0 };
        }
    }

    /// <summary>Call only in response to the user's startup toggle; applies to the current Windows user.</summary>
    public static void SetEnabled(bool enabled, string executablePath)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Windows startup settings require Windows.");
        using var key = Registry.CurrentUser.CreateSubKey(Key, writable: true);
        if (enabled)
        {
            string full = Path.GetFullPath(executablePath);
            if (full.Contains('"') || !File.Exists(full)) throw new ArgumentException("Select an existing executable.", nameof(executablePath));
            key.SetValue(Name, $"\"{full}\"", RegistryValueKind.String);
        }
        else key.DeleteValue(Name, throwOnMissingValue: false);
    }
}
