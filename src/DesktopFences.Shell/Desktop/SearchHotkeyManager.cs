using System.Windows.Interop;
using DesktopFences.Shell.Interop;

namespace DesktopFences.Shell.Desktop;

/// <summary>
/// Registers a global hotkey (Ctrl+`) to trigger the search window.
/// </summary>
public class SearchHotkeyManager : IDisposable
{
    private const int HOTKEY_SEARCH = 9010;
    private const int VK_OEM_3 = 0xC0; // ` key

    private HwndSource? _hwndSource;

    /// <summary>
    /// Fired when the search hotkey is pressed.
    /// </summary>
    public event Action? SearchRequested;

    public void Start()
    {
        var parameters = new HwndSourceParameters("SearchHotkeyWindow")
        {
            Width = 0,
            Height = 0,
            WindowStyle = 0
        };
        _hwndSource = new HwndSource(parameters);
        _hwndSource.AddHook(WndProc);

        NativeMethods.RegisterHotKey(_hwndSource.Handle, HOTKEY_SEARCH,
            NativeMethods.MOD_CONTROL | NativeMethods.MOD_NOREPEAT, VK_OEM_3);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == NativeMethods.WM_HOTKEY && wParam.ToInt32() == HOTKEY_SEARCH)
        {
            SearchRequested?.Invoke();
            handled = true;
        }
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        if (_hwndSource is not null)
        {
            NativeMethods.UnregisterHotKey(_hwndSource.Handle, HOTKEY_SEARCH);
            _hwndSource.Dispose();
            _hwndSource = null;
        }
    }
}
