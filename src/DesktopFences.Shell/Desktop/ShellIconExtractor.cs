using System.Collections.Concurrent;
using System.IO;
using System.Runtime.InteropServices;
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

    // Target physical-pixel size for the large variant. Render target is 48 DIP
    // (= 48px @ 100% DPI, 60px @ 125%, 72px @ 150%, 96px @ 200%). Asking for 96 means
    // WPF only ever downscales, never upscales — and downscaling is what produces crisp
    // results regardless of display DPI.
    private const int LargeIconPixelSize = 96;
    private const int SmallIconPixelSize = 32;

    private static ImageSource? ExtractIcon(string filePath, bool large)
    {
        // Modern path: IShellItemImageFactory::GetImage — same code Explorer uses.
        // The shell decides which icon resource to pick and scales it for us, which
        // avoids the "padded jumbo" problem that plagues SHGetImageList(SHIL_JUMBO).
        var icon = ExtractViaShellItemImageFactory(filePath, large);
        if (icon is not null) return icon;

        // Fallback: legacy SHGetFileInfo + HICON.
        return ExtractIconViaShGetFileInfo(filePath, large);
    }

    private static ImageSource? ExtractViaShellItemImageFactory(string filePath, bool large)
    {
        // SHCreateItemFromParsingName needs a real path. For non-existent paths the
        // legacy fallback (SHGetFileInfo + SHGFI_USEFILEATTRIBUTES) handles by-extension
        // lookup correctly; this modern API doesn't.
        if (!File.Exists(filePath) && !Directory.Exists(filePath))
            return null;

        var iid = NativeMethods.IID_IShellItemImageFactory;
        int hr = NativeMethods.SHCreateItemFromParsingName(filePath, IntPtr.Zero, ref iid, out object? itemObj);
        if (hr != 0 || itemObj is null)
            return null;

        try
        {
            if (itemObj is not NativeMethods.IShellItemImageFactory factory)
                return null;

            int px = large ? LargeIconPixelSize : SmallIconPixelSize;
            var size = new NativeMethods.SIZE(px, px);
            // IconOnly: never substitute a thumbnail (we cache by extension, so
            // per-file thumbnails would all collide on one cache key anyway).
            // BiggerSizeOk: let shell return its native resolution if larger; we'll
            // still downscale to the render target.
            var flags = NativeMethods.SIIGBF.IconOnly | NativeMethods.SIIGBF.BiggerSizeOk;

            hr = factory.GetImage(size, flags, out IntPtr hbitmap);
            if (hr != 0 || hbitmap == IntPtr.Zero)
                return null;

            try
            {
                var source = Imaging.CreateBitmapSourceFromHBitmap(
                    hbitmap, IntPtr.Zero, Int32Rect.Empty,
                    BitmapSizeOptions.FromEmptyOptions());
                source.Freeze();
                return source;
            }
            finally
            {
                NativeMethods.DeleteObject(hbitmap);
            }
        }
        finally
        {
            Marshal.ReleaseComObject(itemObj);
        }
    }

    private static ImageSource? ExtractIconViaShGetFileInfo(string filePath, bool large)
    {
        var flags = NativeMethods.SHGFI_ICON |
                    NativeMethods.SHGFI_ADDOVERLAYS |
                    (large ? NativeMethods.SHGFI_LARGEICON : NativeMethods.SHGFI_SMALLICON);

        if (!File.Exists(filePath) && !Directory.Exists(filePath))
            flags |= NativeMethods.SHGFI_USEFILEATTRIBUTES;

        var shfi = new NativeMethods.SHFILEINFO();
        var result = NativeMethods.SHGetFileInfo(
            filePath,
            NativeMethods.FILE_ATTRIBUTE_NORMAL,
            ref shfi,
            (uint)Marshal.SizeOf<NativeMethods.SHFILEINFO>(),
            flags);

        if (result == IntPtr.Zero || shfi.hIcon == IntPtr.Zero)
            return null;

        try
        {
            var source = Imaging.CreateBitmapSourceFromHIcon(
                shfi.hIcon,
                Int32Rect.Empty,
                BitmapSizeOptions.FromEmptyOptions());
            source.Freeze();
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
