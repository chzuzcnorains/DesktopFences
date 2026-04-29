using System.Runtime.InteropServices;
using DesktopFences.Shell.Interop;

namespace DesktopFences.Shell.Desktop;

/// <summary>
/// Detects double-click on the desktop background to toggle fence visibility.
/// Uses a low-level mouse hook to detect double-clicks, then checks if the
/// click target is the desktop (Progman or WorkerW class).
/// </summary>
public sealed class QuickHideManager : IDisposable
{
    private readonly LowLevelMouseHook _hook = new();
    private DateTime _lastClickTime = DateTime.MinValue;
    private NativeMethods.POINT _lastClickPoint;
    private NativeMethods.POINT _pendingDownPoint;
    private bool _hasPendingDown;

    private const int DoubleClickThresholdMs = 500;
    private const int DoubleClickDistancePx = 4;

    /// <summary>Fired when a double-click on the desktop is detected.</summary>
    public event Action? DesktopDoubleClick;

    public void Start() => _hook.Install(MouseHookCallback);
    public void Stop() => _hook.Uninstall();

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
                if (!WindowClassUtil.IsDesktopAtPoint(ms.pt))
                {
                    _lastClickTime = DateTime.MinValue;
                    _hasPendingDown = false;
                    return _hook.CallNext(nCode, wParam, lParam);
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
                    !WindowClassUtil.IsDesktopAtPoint(ms.pt))
                {
                    _lastClickTime = DateTime.MinValue;
                    return _hook.CallNext(nCode, wParam, lParam);
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

        return _hook.CallNext(nCode, wParam, lParam);
    }

    public void Dispose() => _hook.Dispose();
}
