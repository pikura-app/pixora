using System.Text.Json;

namespace Pixora.Core.Settings;

/// <summary>
/// Thread-safe JSON-backed loader/saver for <see cref="AppSettings"/>.
/// Holds an in-memory current snapshot exposed via <see cref="Current"/>.
/// </summary>
public sealed class SettingsService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly string _path;
    private readonly Lock _gate = new();

    public AppSettings Current { get; private set; } = new();

    /// <summary>Raised on the calling thread after every <see cref="Update"/> call.</summary>
    public event EventHandler? Changed;

    public SettingsService(string? overridePath = null)
    {
        _path = overridePath ?? DefaultPath();
        Load();
    }

    public static string DefaultPath()
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Pixora");
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, "settings.json");
    }

    public void Load()
    {
        lock (_gate)
        {
            if (!File.Exists(_path))
            {
                Current = new AppSettings();
                return;
            }
            try
            {
                using var fs = File.OpenRead(_path);
                Current = JsonSerializer.Deserialize<AppSettings>(fs, JsonOptions) ?? new AppSettings();
            }
            catch
            {
                // Corrupt file; reset.
                Current = new AppSettings();
            }
        }
    }

    public void Save()
    {
        lock (_gate)
        {
            using var fs = File.Create(_path);
            JsonSerializer.Serialize(fs, Current, JsonOptions);
        }
    }

    /// <summary>Mutate and persist atomically.</summary>
    public void Update(Action<AppSettings> mutator)
    {
        lock (_gate)
        {
            mutator(Current);
            using var fs = File.Create(_path);
            JsonSerializer.Serialize(fs, Current, JsonOptions);
        }
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
