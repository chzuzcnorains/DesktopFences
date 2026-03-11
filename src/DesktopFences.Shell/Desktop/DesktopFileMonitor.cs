using System.IO;
using System.Timers;

namespace DesktopFences.Shell.Desktop;

/// <summary>
/// Monitors the desktop directory for file changes using FileSystemWatcher
/// with a periodic full scan fallback (FSWatcher can miss events).
/// Events are debounced to avoid rapid-fire notifications.
/// </summary>
public sealed class DesktopFileMonitor : IDisposable
{
    private readonly string _desktopPath;
    private readonly string _publicDesktopPath;
    private FileSystemWatcher? _watcher;
    private FileSystemWatcher? _publicWatcher;
    private System.Timers.Timer? _scanTimer;
    private System.Timers.Timer? _debounceTimer;
    private HashSet<string> _knownFiles = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _lock = new();
    private readonly HashSet<string> _pendingChanges = new(StringComparer.OrdinalIgnoreCase);
    private bool _disposed;

    /// <summary>
    /// Fired when new files appear on the desktop (debounced).
    /// Provides a list of new file paths.
    /// </summary>
    public event Action<IReadOnlyList<string>>? FilesAdded;

    /// <summary>
    /// Fired when files are removed from the desktop (debounced).
    /// Provides a list of removed file paths.
    /// </summary>
    public event Action<IReadOnlyList<string>>? FilesRemoved;

    /// <summary>
    /// Fired when files are renamed on the desktop.
    /// Provides (oldPath, newPath).
    /// </summary>
    public event Action<string, string>? FileRenamed;

    public DesktopFileMonitor(string? desktopPath = null)
    {
        _desktopPath = desktopPath ??
            Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
        _publicDesktopPath = Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory);
    }

    /// <summary>
    /// Start monitoring the desktop directory.
    /// </summary>
    public void Start()
    {
        // Take initial snapshot
        _knownFiles = ScanDesktop();

        // FileSystemWatcher for real-time events
        _watcher = new FileSystemWatcher(_desktopPath)
        {
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName |
                           NotifyFilters.LastWrite,
            IncludeSubdirectories = false,
            EnableRaisingEvents = true
        };

        _watcher.Created += (_, e) => OnFileEvent(e.FullPath, FileChangeType.Created);
        _watcher.Deleted += (_, e) => OnFileEvent(e.FullPath, FileChangeType.Deleted);
        _watcher.Renamed += OnRenamed;

        // Also watch the Public Desktop directory
        if (!string.IsNullOrEmpty(_publicDesktopPath) &&
            !string.Equals(_desktopPath, _publicDesktopPath, StringComparison.OrdinalIgnoreCase) &&
            Directory.Exists(_publicDesktopPath))
        {
            _publicWatcher = new FileSystemWatcher(_publicDesktopPath)
            {
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName |
                               NotifyFilters.LastWrite,
                IncludeSubdirectories = false,
                EnableRaisingEvents = true
            };
            _publicWatcher.Created += (_, e) => OnFileEvent(e.FullPath, FileChangeType.Created);
            _publicWatcher.Deleted += (_, e) => OnFileEvent(e.FullPath, FileChangeType.Deleted);
            _publicWatcher.Renamed += OnRenamed;
        }

        // Debounce timer — fires 500ms after last change
        _debounceTimer = new System.Timers.Timer(500) { AutoReset = false };
        _debounceTimer.Elapsed += OnDebounceElapsed;

        // Periodic full scan (every 30 seconds) as fallback
        _scanTimer = new System.Timers.Timer(30000) { AutoReset = true };
        _scanTimer.Elapsed += (_, _) => PerformFullScan();
        _scanTimer.Start();
    }

    /// <summary>
    /// Stop monitoring.
    /// </summary>
    public void Stop()
    {
        _watcher?.Dispose();
        _watcher = null;
        _publicWatcher?.Dispose();
        _publicWatcher = null;
        _scanTimer?.Stop();
        _scanTimer?.Dispose();
        _scanTimer = null;
        _debounceTimer?.Stop();
        _debounceTimer?.Dispose();
        _debounceTimer = null;
    }

    /// <summary>
    /// Get all current files on the desktop.
    /// </summary>
    public IReadOnlyList<string> GetCurrentFiles()
    {
        lock (_lock)
        {
            return _knownFiles.ToList();
        }
    }

    private void OnRenamed(object sender, RenamedEventArgs e)
    {
        lock (_lock)
        {
            _knownFiles.Remove(e.OldFullPath);
            _knownFiles.Add(e.FullPath);
        }
        FileRenamed?.Invoke(e.OldFullPath, e.FullPath);
    }

    private void OnFileEvent(string fullPath, FileChangeType changeType)
    {
        lock (_lock)
        {
            _pendingChanges.Add(fullPath);
        }

        // Reset debounce timer
        _debounceTimer?.Stop();
        _debounceTimer?.Start();
    }

    private void OnDebounceElapsed(object? sender, ElapsedEventArgs e)
    {
        PerformFullScan();
    }

    private void PerformFullScan()
    {
        var currentFiles = ScanDesktop();

        List<string> added;
        List<string> removed;

        lock (_lock)
        {
            added = currentFiles.Except(_knownFiles, StringComparer.OrdinalIgnoreCase).ToList();
            removed = _knownFiles.Except(currentFiles, StringComparer.OrdinalIgnoreCase).ToList();
            _knownFiles = currentFiles;
            _pendingChanges.Clear();
        }

        if (added.Count > 0)
            FilesAdded?.Invoke(added);

        if (removed.Count > 0)
            FilesRemoved?.Invoke(removed);
    }

    private HashSet<string> ScanDesktop()
    {
        var files = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var dir in new[] { _desktopPath, _publicDesktopPath })
        {
            try
            {
                if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir))
                {
                    foreach (var f in Directory.GetFiles(dir).Concat(Directory.GetDirectories(dir)))
                        files.Add(f);
                }
            }
            catch { }
        }
        return files;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Stop();
    }

    private enum FileChangeType
    {
        Created,
        Deleted
    }
}
