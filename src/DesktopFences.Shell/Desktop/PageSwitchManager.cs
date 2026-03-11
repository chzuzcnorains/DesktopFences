using System.Runtime.InteropServices;
using System.Windows.Interop;
using DesktopFences.Shell.Interop;

namespace DesktopFences.Shell.Desktop;

/// <summary>
/// Handles page switching hotkeys (Ctrl+PageUp/PageDown) and desktop mouse wheel.
/// </summary>
public class PageSwitchManager : IDisposable
{
    private const int HOTKEY_PAGE_PREV = 9001;
    private const int HOTKEY_PAGE_NEXT = 9002;

    private HwndSource? _hwndSource;
    private IntPtr _mouseHook;
    private NativeMethods.LowLevelMouseProc? _mouseProc;
    private readonly List<IntPtr> _fenceWindows = [];

    /// <summary>
    /// Fired when user requests next page. Parameter: +1 for next, -1 for previous.
    /// </summary>
    public event Action<int>? PageSwitchRequested;

    /// <summary>
    /// Register a fence window so that mouse wheel over it does NOT trigger page switching.
    /// </summary>
    public void RegisterFenceWindow(IntPtr hwnd) => _fenceWindows.Add(hwnd);
    public void UnregisterFenceWindow(IntPtr hwnd) => _fenceWindows.Remove(hwnd);

    public void Start()
    {
        // Create hidden window for hotkey messages
        var parameters = new HwndSourceParameters("PageSwitchHotkeyWindow")
        {
            Width = 0,
            Height = 0,
            WindowStyle = 0
        };
        _hwndSource = new HwndSource(parameters);
        _hwndSource.AddHook(WndProc);

        var hwnd = _hwndSource.Handle;
        NativeMethods.RegisterHotKey(hwnd, HOTKEY_PAGE_PREV,
            NativeMethods.MOD_CONTROL | NativeMethods.MOD_NOREPEAT, (uint)NativeMethods.VK_PRIOR);
        NativeMethods.RegisterHotKey(hwnd, HOTKEY_PAGE_NEXT,
            NativeMethods.MOD_CONTROL | NativeMethods.MOD_NOREPEAT, (uint)NativeMethods.VK_NEXT);

        // Mouse hook for wheel on desktop
        _mouseProc = MouseHookCallback;
        _mouseHook = NativeMethods.SetWindowsHookEx(
            NativeMethods.WH_MOUSE_LL, _mouseProc,
            NativeMethods.GetModuleHandle(null), 0);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == NativeMethods.WM_HOTKEY)
        {
            int id = wParam.ToInt32();
            if (id == HOTKEY_PAGE_PREV)
            {
                PageSwitchRequested?.Invoke(-1);
                handled = true;
            }
            else if (id == HOTKEY_PAGE_NEXT)
            {
                PageSwitchRequested?.Invoke(1);
                handled = true;
            }
        }
        return IntPtr.Zero;
    }

    private IntPtr MouseHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && wParam == NativeMethods.WM_MOUSEWHEEL)
        {
            var hookStruct = Marshal.PtrToStructure<NativeMethods.MSLLHOOKSTRUCT>(lParam);

            // Check if mouse is over desktop (same logic as QuickHideManager).
            // Also skip if mouse is over a fence window — fences sit at HWND_BOTTOM
            // so WindowFromPoint returns the desktop behind them, but the scroll
            // should go to the fence's ListBox content, not trigger page switching.
            if (IsDesktopWindow(hookStruct.pt) && !IsFenceWindowAtPoint(hookStruct.pt))
            {
                int delta = (short)(hookStruct.mouseData >> 16);
                // delta > 0 = scroll up = previous page, delta < 0 = scroll down = next page
                PageSwitchRequested?.Invoke(delta > 0 ? -1 : 1);
            }
        }
        return NativeMethods.CallNextHookEx(_mouseHook, nCode, wParam, lParam);
    }

    private bool IsFenceWindowAtPoint(NativeMethods.POINT pt)
    {
        foreach (var hwnd in _fenceWindows)
        {
            if (!NativeMethods.IsWindowVisible(hwnd)) continue;
            if (NativeMethods.GetWindowRect(hwnd, out var rect)
                && pt.X >= rect.Left && pt.X <= rect.Right
                && pt.Y >= rect.Top && pt.Y <= rect.Bottom)
                return true;
        }
        return false;
    }

    private static bool IsDesktopWindow(NativeMethods.POINT pt)
    {
        var hwnd = NativeMethods.WindowFromPoint(pt);
        if (hwnd == IntPtr.Zero) return false;

        var className = new System.Text.StringBuilder(256);
        NativeMethods.GetClassName(hwnd, className, 256);
        var name = className.ToString();

        // Desktop window class names
        if (name is "Progman" or "WorkerW" or "SHELLDLL_DefView" or "SysListView32")
            return true;

        // Check parent chain
        var parent = NativeMethods.GetParent(hwnd);
        if (parent != IntPtr.Zero)
        {
            NativeMethods.GetClassName(parent, className, 256);
            var parentName = className.ToString();
            if (parentName is "Progman" or "WorkerW" or "SHELLDLL_DefView")
                return true;
        }

        return false;
    }

    public void Dispose()
    {
        if (_hwndSource is not null)
        {
            var hwnd = _hwndSource.Handle;
            NativeMethods.UnregisterHotKey(hwnd, HOTKEY_PAGE_PREV);
            NativeMethods.UnregisterHotKey(hwnd, HOTKEY_PAGE_NEXT);
            _hwndSource.Dispose();
            _hwndSource = null;
        }

        if (_mouseHook != IntPtr.Zero)
        {
            NativeMethods.UnhookWindowsHookEx(_mouseHook);
            _mouseHook = IntPtr.Zero;
        }
    }
}
