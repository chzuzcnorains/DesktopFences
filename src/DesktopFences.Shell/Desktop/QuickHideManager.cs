using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using DesktopFences.Shell.Interop;

namespace DesktopFences.Shell.Desktop;

/// <summary>
/// Detects double-click on the desktop background to toggle fence visibility.
/// Uses a low-level mouse hook to detect double-clicks, then checks if the
/// click target is the desktop (Progman or WorkerW class).
/// </summary>
public sealed class QuickHideManager : IDisposable
{
    private IntPtr _mouseHookId;
    private NativeMethods.LowLevelMouseProc? _mouseHookProc;
    private DateTime _lastClickTime = DateTime.MinValue;
    private NativeMethods.POINT _lastClickPoint;
    private bool _disposed;

    private const int DoubleClickThresholdMs = 500;
    private const int DoubleClickDistancePx = 4;

    /// <summary>
    /// Fired when a double-click on the desktop is detected.
    /// </summary>
    public event Action? DesktopDoubleClick;

    public void Start()
    {
        if (_mouseHookId != IntPtr.Zero) return; // already started
        _mouseHookProc = MouseHookCallback;
        using var curProcess = Process.GetCurrentProcess();
        using var curModule = curProcess.MainModule!;
        _mouseHookId = NativeMethods.SetWindowsHookEx(
            NativeMethods.WH_MOUSE_LL,
            _mouseHookProc,
            NativeMethods.GetModuleHandle(curModule.ModuleName),
            0);
    }

    public void Stop()
    {
        if (_mouseHookId == IntPtr.Zero) return;
        NativeMethods.UnhookWindowsHookEx(_mouseHookId);
        _mouseHookId = IntPtr.Zero;
    }

    private IntPtr MouseHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && (int)wParam == NativeMethods.WM_LBUTTONDOWN)
        {
            var ms = Marshal.PtrToStructure<NativeMethods.MSLLHOOKSTRUCT>(lParam);

            var now = DateTime.UtcNow;
            var elapsed = (now - _lastClickTime).TotalMilliseconds;

            if (elapsed < DoubleClickThresholdMs &&
                Math.Abs(ms.pt.X - _lastClickPoint.X) <= DoubleClickDistancePx &&
                Math.Abs(ms.pt.Y - _lastClickPoint.Y) <= DoubleClickDistancePx)
            {
                // Double-click detected — check if it's on the desktop
                if (IsDesktopWindow(ms.pt))
                {
                    DesktopDoubleClick?.Invoke();
                }
                _lastClickTime = DateTime.MinValue; // reset to avoid triple-click
            }
            else
            {
                _lastClickTime = now;
                _lastClickPoint = ms.pt;
            }
        }

        return NativeMethods.CallNextHookEx(_mouseHookId, nCode, wParam, lParam);
    }

    private static bool IsDesktopWindow(NativeMethods.POINT pt)
    {
        var hwnd = NativeMethods.WindowFromPoint(pt);
        if (hwnd == IntPtr.Zero) return false;

        var className = GetWindowClassName(hwnd);

        // Desktop window classes
        if (className is "Progman" or "WorkerW" or "SysListView32" or "SHELLDLL_DefView")
            return true;

        // Also check parent — SysListView32 is child of SHELLDLL_DefView which is child of WorkerW
        var parent = NativeMethods.GetParent(hwnd);
        if (parent != IntPtr.Zero)
        {
            var parentClass = GetWindowClassName(parent);
            if (parentClass is "SHELLDLL_DefView" or "Progman" or "WorkerW")
                return true;
        }

        return false;
    }

    private static string GetWindowClassName(IntPtr hwnd)
    {
        var sb = new StringBuilder(256);
        NativeMethods.GetClassName(hwnd, sb, sb.Capacity);
        return sb.ToString();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        if (_mouseHookId != IntPtr.Zero)
        {
            NativeMethods.UnhookWindowsHookEx(_mouseHookId);
            _mouseHookId = IntPtr.Zero;
        }
    }
}
