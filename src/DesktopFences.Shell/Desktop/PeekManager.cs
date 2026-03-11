using System.Windows.Interop;
using DesktopFences.Shell.Interop;

namespace DesktopFences.Shell.Desktop;

/// <summary>
/// Manages the Peek feature: Win+Space brings all fences to TOPMOST,
/// second Win+Space or Escape returns them to normal z-order.
/// Uses RegisterHotKey for Win+Space and the keyboard hook for Escape.
/// </summary>
public sealed class PeekManager : IDisposable
{
    private const int HOTKEY_PEEK = 0x0001;
    private HwndSource? _hwndSource;
    private bool _isPeeking;
    private bool _disposed;

    /// <summary>
    /// Fired when Peek mode is toggled. Arg: isPeeking.
    /// </summary>
    public event Action<bool>? PeekToggled;

    public bool IsPeeking => _isPeeking;

    /// <summary>
    /// Register the Win+Space hotkey. Must be called from the UI thread.
    /// </summary>
    public void Start()
    {
        // Create a hidden window to receive WM_HOTKEY messages
        var parameters = new HwndSourceParameters("DesktopFences_PeekHotkey")
        {
            Width = 0,
            Height = 0,
            WindowStyle = 0x00000000 // WS_OVERLAPPED but invisible
        };
        _hwndSource = new HwndSource(parameters);
        _hwndSource.AddHook(WndProc);

        NativeMethods.RegisterHotKey(
            _hwndSource.Handle,
            HOTKEY_PEEK,
            NativeMethods.MOD_WIN | NativeMethods.MOD_NOREPEAT,
            (uint)NativeMethods.VK_SPACE);
    }

    /// <summary>
    /// Call when Escape is pressed (detected by keyboard hook) to exit Peek.
    /// </summary>
    public void OnEscapePressed()
    {
        if (_isPeeking)
        {
            _isPeeking = false;
            PeekToggled?.Invoke(false);
        }
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == NativeMethods.WM_HOTKEY && wParam.ToInt32() == HOTKEY_PEEK)
        {
            _isPeeking = !_isPeeking;
            PeekToggled?.Invoke(_isPeeking);
            handled = true;
        }
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        if (_hwndSource is not null)
        {
            NativeMethods.UnregisterHotKey(_hwndSource.Handle, HOTKEY_PEEK);
            _hwndSource.RemoveHook(WndProc);
            _hwndSource.Dispose();
            _hwndSource = null;
        }
    }
}
