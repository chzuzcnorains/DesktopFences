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
    private NativeMethods.POINT _pendingDownPoint;
    private bool _hasPendingDown;
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
        if (nCode >= 0)
        {
            var msg = (int)wParam;

            if (msg == NativeMethods.WM_LBUTTONDOWN)
            {
                var ms = Marshal.PtrToStructure<NativeMethods.MSLLHOOKSTRUCT>(lParam);

                // Only desktop presses can ever contribute to a quick-hide double-click.
                // A press on any non-desktop window (snipping tool overlay, fence, app)
                // must NOT update _lastClickTime — otherwise a screenshot drag-press
                // followed by a stray desktop click later would be mistaken for a
                // double-click and hide every fence.
                if (!IsDesktopWindow(ms.pt))
                {
                    _lastClickTime = DateTime.MinValue;
                    _hasPendingDown = false;
                    return NativeMethods.CallNextHookEx(_mouseHookId, nCode, wParam, lParam);
                }

                _pendingDownPoint = ms.pt;
                _hasPendingDown = true;
            }
            else if (msg == NativeMethods.WM_LBUTTONUP && _hasPendingDown)
            {
                var ms = Marshal.PtrToStructure<NativeMethods.MSLLHOOKSTRUCT>(lParam);
                _hasPendingDown = false;

                // Drag (down → moved → up) is not a click. Reject when the up
                // position drifted from the down position, so screenshot/marquee
                // drags that begin on the desktop don't seed a click timestamp.
                if (Math.Abs(ms.pt.X - _pendingDownPoint.X) > DoubleClickDistancePx ||
                    Math.Abs(ms.pt.Y - _pendingDownPoint.Y) > DoubleClickDistancePx ||
                    !IsDesktopWindow(ms.pt))
                {
                    _lastClickTime = DateTime.MinValue;
                    return NativeMethods.CallNextHookEx(_mouseHookId, nCode, wParam, lParam);
                }

                var now = DateTime.UtcNow;
                var elapsed = (now - _lastClickTime).TotalMilliseconds;

                if (elapsed < DoubleClickThresholdMs &&
                    Math.Abs(ms.pt.X - _lastClickPoint.X) <= DoubleClickDistancePx &&
                    Math.Abs(ms.pt.Y - _lastClickPoint.Y) <= DoubleClickDistancePx)
                {
                    DesktopDoubleClick?.Invoke();
                    _lastClickTime = DateTime.MinValue; // reset to avoid triple-click
                }
                else
                {
                    _lastClickTime = now;
                    _lastClickPoint = ms.pt;
                }
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
