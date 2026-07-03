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
        => GetIcon(filePath, large ? LargeIconPixelSize : SmallIconPixelSize);

    /// <summary>
    /// Get the icon at a specific physical-pixel size (cached per size bucket).
    /// 调用方用 <see cref="QuantizeRequestSize"/> 把「显示 DIP × 实际 DPI」量化成
    /// 少数几个尺寸桶，避免为几乎相同的尺寸缓存多份位图。
    /// </summary>
    public ImageSource? GetIcon(string filePath, int pixelSize)
    {
        var key = $"{GetCacheKey(filePath)}@{pixelSize}";

        if (_cache.TryGetValue(key, out var cached))
        {
            TouchLru(key);
            return cached;
        }

        var icon = ExtractIcon(filePath, pixelSize);
        if (icon is null) return null;

        icon.Freeze();
        _cache[key] = icon;
        AddToLru(key);

        return icon;
    }

    /// <summary>
    /// Drop every cached size bucket for this file so the next GetIcon re-extracts.
    /// 桌面「刷新」时用：图标资源（如 .lnk 目标换图）可能已变。
    /// </summary>
    public void Invalidate(string filePath)
    {
        var baseKey = GetCacheKey(filePath);
        var prefix = baseKey + "@";
        foreach (var key in _cache.Keys)
        {
            if (!key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                continue;
            _cache.TryRemove(key, out _);
            lock (_lruLock)
            {
                _lruOrder.Remove(key);
            }
        }
    }

    /// <summary>
    /// Asynchronously extract icon (offloads SHGetFileInfo to thread pool).
    /// </summary>
    public Task<ImageSource?> GetIconAsync(string filePath, bool large = true)
    {
        var key = $"{GetCacheKey(filePath)}@{(large ? LargeIconPixelSize : SmallIconPixelSize)}";
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

    /// <summary>
    /// 显示 DIP × DPI 缩放 → 物理像素请求尺寸：向上取整后量化到 16 的倍数（钳 32-256）。
    /// 量化保证同一台机器上实际只出现 1-2 个尺寸桶（LRU key 含尺寸，桶多 = 同图多份）；
    /// 向上取整保证位图 ≥ 显示像素，WPF 只降采样不放大（放大才会糊）。
    /// 例：44 DIP@100%→48px；44@150%（需 66px）→80px；64@200%→128px。
    /// </summary>
    public static int QuantizeRequestSize(double displayDip, double dpiScale)
    {
        int needed = (int)Math.Ceiling(displayDip * Math.Max(1.0, dpiScale));
        return Math.Clamp((needed + 15) / 16 * 16, 32, 256);
    }

    private static ImageSource? ExtractIcon(string filePath, int pixelSize)
    {
        // Modern path: IShellItemImageFactory::GetImage — same code Explorer uses.
        // The shell decides which icon resource to pick and scales it for us, which
        // avoids the "padded jumbo" problem that plagues SHGetImageList(SHIL_JUMBO).
        var icon = ExtractViaShellItemImageFactory(filePath, pixelSize);
        if (icon is not null) return icon;

        // Fallback: legacy SHGetFileInfo + HICON (only two stock sizes available).
        return ExtractIconViaShGetFileInfo(filePath, large: pixelSize > SmallIconPixelSize);
    }

    private static ImageSource? ExtractViaShellItemImageFactory(string filePath, int pixelSize)
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

            var size = new NativeMethods.SIZE(pixelSize, pixelSize);
            // IconOnly: never substitute a thumbnail (we cache by extension, so
            // per-file thumbnails would all collide on one cache key anyway).
            // 不带 BiggerSizeOk：让 shell 用资源管理器同款算法降采样到精确请求尺寸。
            // 带上它 shell 会直接返回原生分辨率（常见 256×256 ≈ 256KB/张）整张进缓存，
            // 是内存大头；显示端反正 ≤ 请求尺寸，精确位图视觉不变。
            var flags = NativeMethods.SIIGBF.IconOnly;

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
            // 先 Remove 再 AddFirst（与 TouchLru 对齐）：GetIcon 的 check-then-act 非原子，
            // 两个线程在 miss 时可能为同一 key 各插一次，淘汰后再次加入也会重复 —— 否则
            // _lruOrder 出现同 key 多个节点，Count 虚高把仍在 _cache 中的活跃项误淘汰。
            _lruOrder.Remove(key);
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
