using System.Text.Json;
using System.Text.Json.Serialization;

namespace MySystem.Infrastructure;

public static class AppPaths
{
    public static string DataDirectory => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MySystem");
}

public sealed class SettingsStore
{
    private readonly string _path;
    private readonly object _gate = new();
    public SettingsStore(string? directory = null) => _path = Path.Combine(directory ?? AppPaths.DataDirectory, "settings.json");
    public string FilePath => _path;
    public string? LastLoadError { get; private set; }

    public AppSettings Load()
    {
        lock (_gate)
        {
            LastLoadError = null;
            try
            {
                if (!File.Exists(_path)) return new();
                if (new FileInfo(_path).Length > 128 * 1024) throw new JsonException("Settings file exceeds the size limit.");
                var settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(_path), JsonOptions.Indented) ?? new();
                settings.Validate();
                return settings;
            }
            catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
            {
                LastLoadError = ex.Message;
                // Preserve the original for diagnosis. Invalid configuration never prevents startup.
                return new();
            }
        }
    }

    public void Save(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        lock (_gate)
        {
            settings.Validate();
            AtomicFile.Write(_path, JsonSerializer.Serialize(settings, JsonOptions.Indented));
        }
    }
}

internal static class JsonOptions
{
    internal static readonly JsonSerializerOptions Compact = Create(false);
    internal static readonly JsonSerializerOptions Indented = Create(true);
    private static JsonSerializerOptions Create(bool indent) => new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = indent,
        Converters = { new JsonStringEnumConverter() }
    };
}

internal static class AtomicFile
{
    internal static void Write(string path, string contents)
    {
        string full = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        string temporary = full + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                using (var writer = new StreamWriter(stream, new System.Text.UTF8Encoding(false), leaveOpen: true))
                { writer.Write(contents); writer.Flush(); }
                stream.Flush(flushToDisk: true);
            }
            File.Move(temporary, full, overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
