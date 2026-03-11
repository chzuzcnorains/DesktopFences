using System.Collections.Concurrent;
using System.IO;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DesktopFences.Shell.Interop;

namespace DesktopFences.Shell.Desktop;

/// <summary>
/// Extracts file icons using SHGetFileInfo with extension-based LRU caching.
/// Thread-safe: extraction runs on a background thread, results cached for UI use.
/// </summary>
public sealed class ShellIconExtractor
{
    private readonly ConcurrentDictionary<string, ImageSource> _cache = new(StringComparer.OrdinalIgnoreCase);
    private readonly LinkedList<string> _lruOrder = new();
    private readonly object _lruLock = new();
    private readonly int _maxCacheSize;

    public ShellIconExtractor(int maxCacheSize = 500)
    {
        _maxCacheSize = maxCacheSize;
    }

    /// <summary>
    /// Get the icon for a file path. Returns cached icon if available.
    /// For non-image files, icons are cached by extension (e.g., ".txt" shares one icon).
    /// </summary>
    public ImageSource? GetIcon(string filePath, bool large = true)
    {
        var key = GetCacheKey(filePath);

        if (_cache.TryGetValue(key, out var cached))
        {
            TouchLru(key);
            return cached;
        }

        var icon = ExtractIcon(filePath, large);
        if (icon is null) return null;

        // Freeze for cross-thread usage
        icon.Freeze();
        _cache[key] = icon;
        AddToLru(key);

        return icon;
    }

    /// <summary>
    /// Asynchronously extract icon (offloads SHGetFileInfo to thread pool).
    /// </summary>
    public Task<ImageSource?> GetIconAsync(string filePath, bool large = true)
    {
        var key = GetCacheKey(filePath);
        if (_cache.TryGetValue(key, out var cached))
        {
            TouchLru(key);
            return Task.FromResult<ImageSource?>(cached);
        }

        return Task.Run(() => GetIcon(filePath, large));
    }

    private static string GetCacheKey(string filePath)
    {
        var ext = Path.GetExtension(filePath);
        // For executables and special files, cache by full path (they have unique icons)
        if (string.Equals(ext, ".exe", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(ext, ".lnk", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(ext, ".ico", StringComparison.OrdinalIgnoreCase))
        {
            return filePath;
        }
        // For everything else, cache by extension
        return string.IsNullOrEmpty(ext) ? "__no_ext__" : ext;
    }

    private static ImageSource? ExtractIcon(string filePath, bool large)
    {
        var flags = NativeMethods.SHGFI_ICON |
                    (large ? NativeMethods.SHGFI_LARGEICON : NativeMethods.SHGFI_SMALLICON);

        // If file doesn't exist, use USEFILEATTRIBUTES to get icon by extension
        if (!File.Exists(filePath) && !Directory.Exists(filePath))
            flags |= NativeMethods.SHGFI_USEFILEATTRIBUTES;

        var shfi = new NativeMethods.SHFILEINFO();
        var result = NativeMethods.SHGetFileInfo(
            filePath,
            NativeMethods.FILE_ATTRIBUTE_NORMAL,
            ref shfi,
            (uint)System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.SHFILEINFO>(),
            flags);

        if (result == IntPtr.Zero || shfi.hIcon == IntPtr.Zero)
            return null;

        try
        {
            var source = Imaging.CreateBitmapSourceFromHIcon(
                shfi.hIcon,
                Int32Rect.Empty,
                BitmapSizeOptions.FromEmptyOptions());
            return source;
        }
        finally
        {
            NativeMethods.DestroyIcon(shfi.hIcon);
        }
    }

    private void TouchLru(string key)
    {
        lock (_lruLock)
        {
            _lruOrder.Remove(key);
            _lruOrder.AddFirst(key);
        }
    }

    private void AddToLru(string key)
    {
        lock (_lruLock)
        {
            _lruOrder.AddFirst(key);
            while (_lruOrder.Count > _maxCacheSize)
            {
                var oldest = _lruOrder.Last!.Value;
                _lruOrder.RemoveLast();
                _cache.TryRemove(oldest, out _);
            }
        }
    }
}
