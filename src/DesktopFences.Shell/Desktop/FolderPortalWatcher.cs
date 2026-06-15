using System.IO;

namespace DesktopFences.Shell.Desktop;

/// <summary>
/// Watches a folder path and reports file changes for Folder Portal mode.
/// </summary>
public class FolderPortalWatcher : IDisposable
{
    private FileSystemWatcher? _watcher;
    private readonly System.Timers.Timer _debounceTimer;
    private readonly HashSet<string> _pendingChanges = [];
    private readonly object _lock = new();
    private string _currentPath = string.Empty;
    private bool _disposed;

    /// <summary>
    /// Fired when the contents of the watched folder change.
    /// Provides the full list of file paths in the folder.
    /// </summary>
    public event Action<IReadOnlyList<string>>? ContentsChanged;

    /// <summary>
    /// The currently watched folder path.
    /// </summary>
    public string CurrentPath => _currentPath;

    public FolderPortalWatcher()
    {
        _debounceTimer = new System.Timers.Timer(300) { AutoReset = false };
        _debounceTimer.Elapsed += (_, _) => RaiseContentsChanged();
    }

    /// <summary>
    /// Start watching a folder. Call again with a different path to navigate.
    /// </summary>
    public void Watch(string folderPath)
    {
        if (!Directory.Exists(folderPath)) return;

        Stop();
        _currentPath = folderPath;

        _watcher = new FileSystemWatcher(folderPath)
        {
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName
                           | NotifyFilters.LastWrite | NotifyFilters.Size,
            IncludeSubdirectories = false,
            EnableRaisingEvents = true
        };

        _watcher.Created += OnChanged;
        _watcher.Deleted += OnChanged;
        _watcher.Renamed += OnChanged;
        _watcher.Changed += OnChanged;
        _watcher.Error += OnError;

        // Raise initial contents
        RaiseContentsChanged();
    }

    /// <summary>
    /// Navigate to a subfolder within the current path.
    /// </summary>
    public bool NavigateToSubfolder(string subfolderName)
    {
        if (string.IsNullOrEmpty(_currentPath)) return false;
        var newPath = Path.Combine(_currentPath, subfolderName);
        if (!Directory.Exists(newPath)) return false;
        Watch(newPath);
        return true;
    }

    /// <summary>
    /// Navigate to the parent folder.
    /// </summary>
    public bool NavigateUp()
    {
        if (string.IsNullOrEmpty(_currentPath)) return false;
        var parent = Directory.GetParent(_currentPath);
        if (parent is null) return false;
        Watch(parent.FullName);
        return true;
    }

    /// <summary>
    /// Get the breadcrumb path segments from root to current folder.
    /// </summary>
    public List<string> GetBreadcrumbs()
    {
        if (string.IsNullOrEmpty(_currentPath)) return [];
        var parts = new List<string>();
        var path = _currentPath;
        while (!string.IsNullOrEmpty(path))
        {
            parts.Insert(0, path);
            var parent = Directory.GetParent(path);
            path = parent?.FullName;
            if (path == parts[0]) break; // Root reached
        }
        return parts;
    }

    /// <summary>
    /// Get current folder contents.
    /// </summary>
    public IReadOnlyList<string> GetContents()
    {
        if (string.IsNullOrEmpty(_currentPath) || !Directory.Exists(_currentPath))
            return [];

        try
        {
            return Directory.GetFileSystemEntries(_currentPath)
                .OrderBy(p => !Directory.Exists(p)) // Directories first
                .ThenBy(p => Path.GetFileName(p), StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        catch
        {
            return [];
        }
    }

    public void Stop()
    {
        _watcher?.Dispose();
        _watcher = null;
    }

    private void OnChanged(object sender, FileSystemEventArgs e)
    {
        lock (_lock)
        {
            if (_disposed) return;
            _pendingChanges.Add(e.FullPath);

            // timer 操作必须在锁内：Dispose 可能正在并发销毁它，
            // ObjectDisposedException 在 FSW 线程池线程上未处理 = 进程崩溃
            _debounceTimer.Stop();
            _debounceTimer.Start();
        }
    }

    private void OnError(object sender, ErrorEventArgs e)
    {
        // FSW 内部缓冲区溢出后会静默丢事件，立即全量刷新自愈
        RaiseContentsChanged();
    }

    private void RaiseContentsChanged()
    {
        lock (_lock)
        {
            if (_disposed) return;
            _pendingChanges.Clear();
        }
        ContentsChanged?.Invoke(GetContents());
    }

    public void Dispose()
    {
        lock (_lock)
        {
            if (_disposed) return;
            _disposed = true;
            _debounceTimer.Stop();
            _debounceTimer.Dispose();
            _watcher?.Dispose();
        }
    }
}
