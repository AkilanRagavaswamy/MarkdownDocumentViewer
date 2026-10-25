using MarkdownViewer.Core.Contracts;

namespace MarkdownViewer.Core.Services;

/// <summary>
/// Watches a single file for modification or replacement (including editors that save via a
/// temporary file followed by a rename) and raises a debounced callback.
/// </summary>
public sealed class FileWatcherService : IFileWatcherService
{
    /// <summary>Debounce interval applied to bursts of file system events.</summary>
    public static readonly TimeSpan Debounce = TimeSpan.FromMilliseconds(250);

    /// <inheritdoc />
    public IDisposable Watch(string filePath, Action onChanged)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        ArgumentNullException.ThrowIfNull(onChanged);

        var fullPath = Path.GetFullPath(filePath);
        var directory = Path.GetDirectoryName(fullPath);
        var fileName = Path.GetFileName(fullPath);
        if (string.IsNullOrEmpty(directory) || string.IsNullOrEmpty(fileName) || !Directory.Exists(directory))
        {
            return NullSubscription.Instance;
        }

        try
        {
            return new Subscription(directory, fileName, onChanged);
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException or PlatformNotSupportedException)
        {
            return NullSubscription.Instance;
        }
    }

    private sealed class NullSubscription : IDisposable
    {
        public static readonly NullSubscription Instance = new();

        public void Dispose()
        {
        }
    }

    private sealed class Subscription : IDisposable
    {
        private readonly object _gate = new();
        private readonly string _fileName;
        private readonly Action _onChanged;
        private readonly FileSystemWatcher _watcher;
        private readonly Timer _timer;
        private bool _disposed;

        public Subscription(string directory, string fileName, Action onChanged)
        {
            _fileName = fileName;
            _onChanged = onChanged;
            _timer = new Timer(OnTimer, null, Timeout.Infinite, Timeout.Infinite);
            _watcher = new FileSystemWatcher(directory, fileName)
            {
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size,
                IncludeSubdirectories = false,
            };

            _watcher.Changed += OnFileEvent;
            _watcher.Created += OnFileEvent;
            _watcher.Renamed += OnRenamed;
            _watcher.Error += OnError;
            _watcher.EnableRaisingEvents = true;
        }

        public void Dispose()
        {
            lock (_gate)
            {
                if (_disposed)
                {
                    return;
                }

                _disposed = true;
            }

            _watcher.EnableRaisingEvents = false;
            _watcher.Changed -= OnFileEvent;
            _watcher.Created -= OnFileEvent;
            _watcher.Renamed -= OnRenamed;
            _watcher.Error -= OnError;
            _watcher.Dispose();
            _timer.Dispose();
        }

        private void OnFileEvent(object sender, FileSystemEventArgs e)
        {
            if (IsTarget(e.FullPath))
            {
                Schedule();
            }
        }

        private void OnRenamed(object sender, RenamedEventArgs e)
        {
            // Temp file renamed onto the target ("safe save"), or target renamed back into place.
            if (IsTarget(e.FullPath))
            {
                Schedule();
            }
        }

        private void OnError(object sender, ErrorEventArgs e)
        {
            // Internal buffer overflow etc.: we may have missed an event, so reload to be safe.
            Schedule();
        }

        private bool IsTarget(string? path) =>
            path is not null && string.Equals(Path.GetFileName(path), _fileName, StringComparison.OrdinalIgnoreCase);

        private void Schedule()
        {
            lock (_gate)
            {
                if (_disposed)
                {
                    return;
                }

                _timer.Change(Debounce, Timeout.InfiniteTimeSpan);
            }
        }

        private void OnTimer(object? state)
        {
            lock (_gate)
            {
                if (_disposed)
                {
                    return;
                }
            }

            try
            {
                _onChanged();
            }
            catch
            {
                // A failing subscriber must not crash the thread-pool timer thread.
            }
        }
    }
}
