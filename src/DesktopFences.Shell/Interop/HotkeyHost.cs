using System.Windows.Interop;

namespace DesktopFences.Shell.Interop;

/// <summary>
/// Hidden message-only window that owns one or more global hotkey registrations
/// and dispatches WM_HOTKEY to per-id callbacks. Replaces the HwndSource +
/// RegisterHotKey + WndProc + UnregisterHotKey boilerplate that used to be
/// duplicated in every hotkey manager.
/// </summary>
internal sealed class HotkeyHost : IDisposable
{
    private readonly HwndSource _source;
    private readonly Dictionary<int, Action> _handlers = [];
    private bool _disposed;

    public HotkeyHost(string windowName)
    {
        var parameters = new HwndSourceParameters(windowName) { Width = 0, Height = 0, WindowStyle = 0 };
        _source = new HwndSource(parameters);
        _source.AddHook(WndProc);
    }

    /// <summary>
    /// Register a hotkey. <paramref name="id"/> must be unique within this host.
    /// </summary>
    public bool Register(int id, uint modifiers, uint vk, Action handler)
    {
        if (!NativeMethods.RegisterHotKey(_source.Handle, id, modifiers, vk))
            return false;
        _handlers[id] = handler;
        return true;
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == NativeMethods.WM_HOTKEY && _handlers.TryGetValue(wParam.ToInt32(), out var handler))
        {
            handler();
            handled = true;
        }
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        foreach (var id in _handlers.Keys)
            NativeMethods.UnregisterHotKey(_source.Handle, id);
        _handlers.Clear();

        _source.RemoveHook(WndProc);
        _source.Dispose();
    }
}
