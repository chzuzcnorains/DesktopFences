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
    /// True 仅当窗口属于真桌面层：顶层祖先 (GA_ROOT) 是壁纸宿主 Progman / WorkerW。
    /// 不能按命中窗口自身/单层父窗口类名判断——资源管理器 (CabinetWClass) 的文件视图
    /// 同样是 SHELLDLL_DefView / SysListView32，其内部还有 WorkerW 类子窗口；这些类名
    /// 不专属于桌面，只有"顶层是谁"才可靠（bug 45：Win+E 后 fence 被误 hoist 到
    /// 资源管理器之上）。幻灯片壁纸下桌面 DefView 挂在 WorkerW 下，故 Progman 和
    /// WorkerW 都认。调用方：sunk 自愈探测、quick-hide、page-switch、marquee（均接收
    /// WindowFromPoint 结果）及 OnForegroundChanged 桌面分支（前台顶层，语义等价）。
    /// </summary>
    public static bool IsDesktopWindow(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero) return false;

        var root = NativeMethods.GetAncestor(hwnd, NativeMethods.GA_ROOT);
        if (root == IntPtr.Zero) root = hwnd; // 防御：句柄失效时退回自身
        var rootName = GetClassName(root);
        return rootName is "Progman" or "WorkerW";
    }

    /// <summary>
    /// Like <see cref="IsDesktopWindow"/> but also treats the taskbar and taskbar popup menus
    /// as part of the desktop layer. Used by DesktopEmbedManager to avoid HWND_BOTTOM races
    /// while the taskbar / start menu has focus on Windows 11.
    /// ⚠️ 仅用于分类**前台顶层窗口**（GetForegroundWindow / EVENT_SYSTEM_FOREGROUND 的 hwnd）。
    /// 禁止喂入 WindowFromPoint 结果：本方法按祖先链宽匹配 SHELLDLL_DefView，会把资源管理器
    /// 的文件视图子窗口误判为桌面（point-hit 判定一律走 <see cref="IsDesktopWindow"/> /
    /// <see cref="IsDesktopAtPoint"/> 的 GA_ROOT 顶层判定）。#32768 菜单检测依赖 GetParent
    /// 的 owner 语义（bug 1），不要改成 GetAncestor。
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

    /// <summary>
    /// True 当窗口带 WS_EX_TOPMOST。用于识别"topmost 悬浮窗前台"（PowerToys 命令面板等
    /// 热键唤出的悬浮面板，WS_EX_TOPMOST|WS_EX_TOOLWINDOW）：它们抢到前台时屏幕可视顶层
    /// 往往仍是桌面（准桌面态），此时对 fence 下发 HWND_BOTTOM 会被 DWM 推到壁纸下（bug 46）；
    /// 且 topmost 前台永远在非 topmost fence 之上，降级本就不必要。
    /// </summary>
    public static bool HasTopmostStyle(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero) return false;
        var ex = (long)NativeMethods.GetWindowLongPtr(hwnd, NativeMethods.GWL_EXSTYLE);
        return (ex & NativeMethods.WS_EX_TOPMOST) != 0;
    }

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
