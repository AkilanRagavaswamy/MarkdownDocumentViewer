using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using MarkdownViewer.Core.Contracts;

namespace MarkdownViewer.App.Services;

/// <summary>
/// Persists <see cref="AppSettings"/> as JSON in LocalFolder\settings.json.
/// Updates are applied immediately, saved after a short debounce, and serialized under a lock.
/// </summary>
public sealed class SettingsService : ISettingsService, IDisposable
{
    private const int SaveDelayMs = 400;

    private static readonly JsonSerializerOptions s_json = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly object _gate = new();
    private readonly object _fileGate = new();
    private readonly string _path;
    private readonly Timer _saveTimer;
    private AppSettings _current;
    private bool _dirty;
    private bool _disposed;

    public SettingsService()
        : this(Path.Combine(AppPaths.LocalFolder, "settings.json"))
    {
    }

    internal SettingsService(string path)
    {
        _path = path;
        _current = Load(path);
        _saveTimer = new Timer(_ => Flush(), null, Timeout.Infinite, Timeout.Infinite);
    }

    public event EventHandler<AppSettings>? Changed;

    public AppSettings Current
    {
        get
        {
            lock (_gate)
            {
                return _current;
            }
        }
    }

    public void Update(Action<AppSettings> change)
    {
        ArgumentNullException.ThrowIfNull(change);

        AppSettings snapshot;
        lock (_gate)
        {
            change(_current);
            _dirty = true;
            snapshot = _current;
            if (!_disposed)
            {
                _saveTimer.Change(SaveDelayMs, Timeout.Infinite);
            }
        }

        Changed?.Invoke(this, snapshot);
    }

    /// <summary>Writes pending changes now (called on window close and by the debounce timer).</summary>
    public void Flush()
    {
        string json;
        lock (_gate)
        {
            if (!_dirty)
            {
                return;
            }

            json = JsonSerializer.Serialize(_current, s_json);
            _dirty = false;
        }

        lock (_fileGate)
        {
            try
            {
                var directory = Path.GetDirectoryName(_path);
                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                var temp = _path + ".tmp";
                File.WriteAllText(temp, json);
                File.Move(temp, _path, overwrite: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Debug.WriteLine($"Saving settings failed: {ex.Message}");
                lock (_gate)
                {
                    _dirty = true;
                    if (!_disposed)
                    {
                        _saveTimer.Change(SaveDelayMs * 5, Timeout.Infinite);
                    }
                }
            }
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _disposed = true;
        }

        _saveTimer.Dispose();
        Flush();
    }

    private static AppSettings Load(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                var loaded = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path), s_json);
                if (loaded is not null)
                {
                    return Normalize(loaded);
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or NotSupportedException)
        {
            Debug.WriteLine($"Settings file unreadable, using defaults: {ex.Message}");
        }

        return new AppSettings();
    }

    private static AppSettings Normalize(AppSettings settings)
    {
        settings.AccentName ??= AccentPalette.DefaultName;
        settings.RecentFiles ??= new List<string>();

        // System.Text.Json creates an ordinal dictionary; restore the case-insensitive comparer for paths.
        var zoom = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        if (settings.ZoomByFile is not null)
        {
            foreach (var (file, factor) in settings.ZoomByFile)
            {
                zoom[file] = factor;
            }
        }

        settings.ZoomByFile = zoom;
        return settings;
    }
}
