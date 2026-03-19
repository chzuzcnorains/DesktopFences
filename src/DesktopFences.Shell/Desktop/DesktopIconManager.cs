using System.IO;
using DesktopFences.Shell.Interop;

namespace DesktopFences.Shell.Desktop;

/// <summary>
/// Hides/shows the desktop icon layer (SysListView32) via ShowWindow.
/// No file attributes are modified — works for all file types including
/// public desktop shortcuts that require admin privileges to modify.
///
/// Desktop window hierarchy:
///   Progman → SHELLDLL_DefView → SysListView32
///   or (after wallpaper changes):
///   WorkerW → SHELLDLL_DefView → SysListView32
///
/// A flag file is used for crash recovery: if the app exits without
/// restoring icons, the next launch will detect the flag and restore them.
/// </summary>
public sealed class DesktopIconManager
{
    private readonly string _flagFilePath;
    private IntPtr _listViewHwnd;

    public DesktopIconManager()
    {
        var dataDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "DesktopFences");
        Directory.CreateDirectory(dataDir);
        _flagFilePath = Path.Combine(dataDir, ".desktop_icons_hidden");
    }

    /// <summary>
    /// True if a previous session hid icons but didn't restore them (crash).
    /// </summary>
    public bool NeedsCrashRecovery => File.Exists(_flagFilePath);

    /// <summary>
    /// Hide all desktop icons by hiding the SysListView32 window.
    /// </summary>
    public bool HideIcons()
    {
        var hwnd = FindDesktopListView();
        if (hwnd == IntPtr.Zero) return false;

        _listViewHwnd = hwnd;
        NativeMethods.ShowWindow(hwnd, NativeMethods.SW_HIDE);
        WriteFlagFile();
        return true;
    }

    /// <summary>
    /// Show all desktop icons by showing the SysListView32 window.
    /// </summary>
    public bool ShowIcons()
    {
        // Try cached handle first, fall back to re-discovery
        var hwnd = _listViewHwnd != IntPtr.Zero ? _listViewHwnd : FindDesktopListView();
        if (hwnd == IntPtr.Zero) return false;

        _listViewHwnd = hwnd;
        NativeMethods.ShowWindow(hwnd, NativeMethods.SW_SHOW);
        DeleteFlagFile();
        return true;
    }

    /// <summary>
    /// Get the cached or freshly-found SysListView32 handle.
    /// </summary>
    public IntPtr GetListViewHandle()
    {
        if (_listViewHwnd == IntPtr.Zero)
            _listViewHwnd = FindDesktopListView();
        return _listViewHwnd;
    }

    /// <summary>
    /// Check if desktop icons are currently visible.
    /// </summary>
    public bool AreIconsVisible()
    {
        var hwnd = _listViewHwnd != IntPtr.Zero ? _listViewHwnd : FindDesktopListView();
        if (hwnd == IntPtr.Zero) return true; // assume visible if we can't find
        return NativeMethods.IsWindowVisible(hwnd);
    }

    /// <summary>
    /// Locates the desktop SysListView32.
    /// Tries Progman first, then enumerates WorkerW windows.
    /// </summary>
    private static IntPtr FindDesktopListView()
    {
        // Try: Progman → SHELLDLL_DefView → SysListView32
        var progman = NativeMethods.FindWindow("Progman", null);
        if (progman != IntPtr.Zero)
        {
            var defView = NativeMethods.FindWindowEx(progman, IntPtr.Zero, "SHELLDLL_DefView", null);
            if (defView != IntPtr.Zero)
            {
                var listView = NativeMethods.FindWindowEx(defView, IntPtr.Zero, "SysListView32", null);
                if (listView != IntPtr.Zero)
                    return listView;
            }
        }

        // Fallback: WorkerW → SHELLDLL_DefView → SysListView32
        // (Windows may move SHELLDLL_DefView to a WorkerW after wallpaper changes)
        var workerW = IntPtr.Zero;
        while (true)
        {
            workerW = NativeMethods.FindWindowEx(IntPtr.Zero, workerW, "WorkerW", null);
            if (workerW == IntPtr.Zero) break;

            var defView = NativeMethods.FindWindowEx(workerW, IntPtr.Zero, "SHELLDLL_DefView", null);
            if (defView != IntPtr.Zero)
            {
                var listView = NativeMethods.FindWindowEx(defView, IntPtr.Zero, "SysListView32", null);
                if (listView != IntPtr.Zero)
                    return listView;
            }
        }

        return IntPtr.Zero;
    }

    private void WriteFlagFile()
    {
        try { File.WriteAllText(_flagFilePath, "hidden"); }
        catch { }
    }

    private void DeleteFlagFile()
    {
        try { if (File.Exists(_flagFilePath)) File.Delete(_flagFilePath); }
        catch { }
    }
}
