using System.Runtime.InteropServices;
using DesktopFences.Shell.Interop;

namespace DesktopFences.Shell.Desktop;

/// <summary>
/// A selection rectangle in physical screen pixels. Public (unlike the internal
/// <c>NativeMethods.RECT</c>) so the UI-layer overlay can consume marquee events.
/// </summary>
public readonly record struct ScreenRect(int Left, int Top, int Right, int Bottom);

/// <summary>
/// Detects native-style rubber-band (marquee) drag-selection on the desktop background,
/// plus Delete / Escape while a selection is active, and raises events for the
/// <c>DesktopIconOverlay</c> to draw the selection rectangle and manage the selected set.
///
/// Why a low-level hook instead of WPF mouse events on the overlay:
/// the overlay's empty areas are intentionally click-through (alpha=0) so the *real*
/// desktop keeps its native right-click menu (New / Personalize / Refresh) and the
/// double-click quick-hide. A <c>WH_MOUSE_LL</c> hook lets us *observe* the empty-area
/// drag in screen coordinates without consuming it (always CallNext) — the overlay then
/// draws the rectangle and hit-tests its own icons. Mirrors
/// <see cref="QuickHideManager"/> / <see cref="PageSwitchManager"/>.
///
/// Orthogonal to QuickHideManager: quick-hide fires only on a no-move double-click,
/// the marquee fires only on a drag, so the two coexist on the desktop background.
/// </summary>
public sealed class DesktopMarqueeManager : IDisposable
{
    private readonly DesktopEmbedManager _embedManager;
    private readonly Func<bool> _hasSelection;
    private readonly LowLevelMouseHook _mouseHook = new();
    private readonly LowLevelKeyboardHook _keyboardHook = new();

    private NativeMethods.POINT _startPoint;
    private bool _armed;     // left button pressed on empty desktop, not yet a drag
    private bool _dragging;  // movement exceeded the drag threshold

    private const int DragThresholdPx = 4;

    /// <summary>Fired continuously while dragging. Args: selection rect (screen px), additive (Ctrl/Shift held).</summary>
    public event Action<ScreenRect, bool>? MarqueeUpdated;

    /// <summary>Fired on mouse-up that ended a drag. Args: final rect (screen px), additive.</summary>
    public event Action<ScreenRect, bool>? MarqueeCompleted;

    /// <summary>Fired on a press+release with no drag on empty desktop → clear selection.</summary>
    public event Action? EmptyClicked;

    /// <summary>Fired when Delete is pressed while a selection is active.</summary>
    public event Action? DeleteRequested;

    /// <summary>Fired when Escape is pressed while a selection is active.</summary>
    public event Action? Cancelled;

    public DesktopMarqueeManager(DesktopEmbedManager embedManager, Func<bool> hasSelection)
    {
        _embedManager = embedManager;
        _hasSelection = hasSelection;
    }

    public void Start()
    {
        _mouseHook.Install(MouseHookCallback);
        _keyboardHook.Install(KeyboardHookCallback);
    }

    public void Stop()
    {
        _mouseHook.Uninstall();
        _keyboardHook.Uninstall();
    }

    /// <summary>
    /// A marquee may start only on the real desktop background (so the press fell through
    /// the click-through overlay) and not over a fence (fences report the desktop behind
    /// them via WindowFromPoint, so a rect-containment check is required).
    /// </summary>
    private bool CanStartAt(NativeMethods.POINT pt)
        => WindowClassUtil.IsDesktopAtPoint(pt) && !_embedManager.IsPointOverFence(pt);

    private static bool IsAdditiveModifierDown()
        => (NativeMethods.GetAsyncKeyState(NativeMethods.VK_CONTROL) & 0x8000) != 0
        || (NativeMethods.GetAsyncKeyState(NativeMethods.VK_SHIFT) & 0x8000) != 0;

    private IntPtr MouseHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            switch ((int)wParam)
            {
                case NativeMethods.WM_LBUTTONDOWN:
                {
                    var ms = Marshal.PtrToStructure<NativeMethods.MSLLHOOKSTRUCT>(lParam);
                    _dragging = false;
                    _armed = CanStartAt(ms.pt);
                    if (_armed) _startPoint = ms.pt;
                    break;
                }
                case NativeMethods.WM_MOUSEMOVE:
                {
                    if (_armed)
                    {
                        var ms = Marshal.PtrToStructure<NativeMethods.MSLLHOOKSTRUCT>(lParam);
                        if (!_dragging &&
                            (Math.Abs(ms.pt.X - _startPoint.X) > DragThresholdPx ||
                             Math.Abs(ms.pt.Y - _startPoint.Y) > DragThresholdPx))
                        {
                            _dragging = true;
                        }
                        if (_dragging)
                            MarqueeUpdated?.Invoke(MakeRect(_startPoint, ms.pt), IsAdditiveModifierDown());
                    }
                    break;
                }
                case NativeMethods.WM_LBUTTONUP:
                {
                    if (_armed)
                    {
                        if (_dragging)
                        {
                            var ms = Marshal.PtrToStructure<NativeMethods.MSLLHOOKSTRUCT>(lParam);
                            MarqueeCompleted?.Invoke(MakeRect(_startPoint, ms.pt), IsAdditiveModifierDown());
                        }
                        else
                        {
                            EmptyClicked?.Invoke();
                        }
                    }
                    _armed = false;
                    _dragging = false;
                    break;
                }
            }
        }

        // Never consume: the real desktop must keep receiving these events so the native
        // right-click menu / double-click quick-hide still work.
        return _mouseHook.CallNext(nCode, wParam, lParam);
    }

    private IntPtr KeyboardHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && (int)wParam == NativeMethods.WM_KEYDOWN && _hasSelection())
        {
            var kb = Marshal.PtrToStructure<NativeMethods.KBDLLHOOKSTRUCT>(lParam);
            if (kb.vkCode == NativeMethods.VK_DELETE)
                DeleteRequested?.Invoke();
            else if (kb.vkCode == NativeMethods.VK_ESCAPE)
                Cancelled?.Invoke();
        }
        return _keyboardHook.CallNext(nCode, wParam, lParam);
    }

    private static ScreenRect MakeRect(NativeMethods.POINT a, NativeMethods.POINT b) => new(
        Math.Min(a.X, b.X),
        Math.Min(a.Y, b.Y),
        Math.Max(a.X, b.X),
        Math.Max(a.Y, b.Y));

    public void Dispose()
    {
        _mouseHook.Dispose();
        _keyboardHook.Dispose();
    }
}
