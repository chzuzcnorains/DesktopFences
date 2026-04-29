using System.Text;

namespace DesktopFences.Shell.Interop;

/// <summary>
/// Window-class helpers for detecting the desktop / taskbar shell windows.
/// Centralizes the class-name constants previously duplicated across
/// QuickHideManager, PageSwitchManager, and DesktopEmbedManager.
/// </summary>
internal static class WindowClassUtil
{
    /// <summary>The set of class names that identify the desktop itself.</summary>
    public static readonly string[] DesktopClasses =
        ["Progman", "WorkerW", "SHELLDLL_DefView", "SysListView32"];

    /// <summary>Taskbar class names — only relevant for DesktopEmbedManager z-order checks.</summary>
    public static readonly string[] TaskbarClasses =
        ["Shell_TrayWnd", "Shell_SecondaryTrayWnd"];

    /// <summary>Read a window's class name. Returns "" if the handle is invalid.</summary>
    public static string GetClassName(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero) return string.Empty;
        var sb = new StringBuilder(256);
        NativeMethods.GetClassName(hwnd, sb, sb.Capacity);
        return sb.ToString();
    }

    /// <summary>
    /// True if the window is part of the desktop layer (Progman / WorkerW / DefView / SysListView32),
    /// either directly or one ancestor up. Used by quick-hide and page-switch hooks.
    /// </summary>
    public static bool IsDesktopWindow(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero) return false;

        var name = GetClassName(hwnd);
        if (Array.IndexOf(DesktopClasses, name) >= 0) return true;

        // Single-level parent check covers SysListView32 → SHELLDLL_DefView → WorkerW chain.
        var parent = NativeMethods.GetParent(hwnd);
        if (parent != IntPtr.Zero)
        {
            var parentName = GetClassName(parent);
            if (parentName is "Progman" or "WorkerW" or "SHELLDLL_DefView") return true;
        }

        return false;
    }

    /// <summary>
    /// Like <see cref="IsDesktopWindow"/> but also treats the taskbar and taskbar popup menus
    /// as part of the desktop layer. Used by DesktopEmbedManager to avoid HWND_BOTTOM races
    /// while the taskbar / start menu has focus on Windows 11.
    /// </summary>
    public static bool IsDesktopOrTaskbarWindow(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero) return false;

        var name = GetClassName(hwnd);
        if (Array.IndexOf(DesktopClasses, name) >= 0) return true;
        if (Array.IndexOf(TaskbarClasses, name) >= 0) return true;

        // Standard menu class — qualifies only when an ancestor is the taskbar.
        if (name == "#32768" && AnyAncestorIs(hwnd, TaskbarClasses)) return true;

        // Walk the full parent chain for the desktop / taskbar shell.
        return AnyAncestorIs(hwnd,
            ["Progman", "WorkerW", "SHELLDLL_DefView", "Shell_TrayWnd", "Shell_SecondaryTrayWnd"]);
    }

    /// <summary>Resolve the window under a screen point and check if it's the desktop.</summary>
    public static bool IsDesktopAtPoint(NativeMethods.POINT pt)
        => IsDesktopWindow(NativeMethods.WindowFromPoint(pt));

    private static bool AnyAncestorIs(IntPtr hwnd, string[] classes)
    {
        var parent = NativeMethods.GetParent(hwnd);
        while (parent != IntPtr.Zero)
        {
            if (Array.IndexOf(classes, GetClassName(parent)) >= 0) return true;
            parent = NativeMethods.GetParent(parent);
        }
        return false;
    }
}
