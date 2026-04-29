using System.Runtime.InteropServices;
using DesktopFences.Shell.Interop;

namespace DesktopFences.Shell.Desktop;

/// <summary>
/// Handles page switching hotkeys (Ctrl+PageUp/PageDown) and desktop mouse wheel.
/// </summary>
public class PageSwitchManager : IDisposable
{
    private const int HOTKEY_PAGE_PREV = 9001;
    private const int HOTKEY_PAGE_NEXT = 9002;

    private HotkeyHost? _hotkey;
    private readonly LowLevelMouseHook _mouseHook = new();
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
        _hotkey = new HotkeyHost("PageSwitchHotkeyWindow");
        _hotkey.Register(HOTKEY_PAGE_PREV,
            NativeMethods.MOD_CONTROL | NativeMethods.MOD_NOREPEAT,
            (uint)NativeMethods.VK_PRIOR,
            () => PageSwitchRequested?.Invoke(-1));
        _hotkey.Register(HOTKEY_PAGE_NEXT,
            NativeMethods.MOD_CONTROL | NativeMethods.MOD_NOREPEAT,
            (uint)NativeMethods.VK_NEXT,
            () => PageSwitchRequested?.Invoke(1));

        _mouseHook.Install(MouseHookCallback);
    }

    private IntPtr MouseHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && wParam == NativeMethods.WM_MOUSEWHEEL)
        {
            var hookStruct = Marshal.PtrToStructure<NativeMethods.MSLLHOOKSTRUCT>(lParam);

            // Skip when over a visible fence — fences sit at HWND_BOTTOM so WindowFromPoint
            // returns the desktop behind them, but the wheel should drive the fence's ListBox.
            if (WindowClassUtil.IsDesktopAtPoint(hookStruct.pt) && !IsFenceWindowAtPoint(hookStruct.pt))
            {
                int delta = (short)(hookStruct.mouseData >> 16);
                PageSwitchRequested?.Invoke(delta > 0 ? -1 : 1);
            }
        }
        return _mouseHook.CallNext(nCode, wParam, lParam);
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

    public void Dispose()
    {
        _hotkey?.Dispose();
        _mouseHook.Dispose();
    }
}
