using DesktopFences.Shell.Interop;

namespace DesktopFences.Shell.Desktop;

/// <summary>
/// Manages the Peek feature: Win+Space brings all fences to TOPMOST,
/// second Win+Space or Escape returns them to normal z-order.
/// </summary>
public sealed class PeekManager : IDisposable
{
    private const int HOTKEY_PEEK = 0x0001;
    private HotkeyHost? _hotkey;
    private bool _isPeeking;

    /// <summary>Fired when Peek mode is toggled. Arg: isPeeking.</summary>
    public event Action<bool>? PeekToggled;

    public bool IsPeeking => _isPeeking;

    /// <summary>Register the Win+Space hotkey. Must be called from the UI thread.</summary>
    public void Start()
    {
        _hotkey = new HotkeyHost("DesktopFences_PeekHotkey");
        _hotkey.Register(HOTKEY_PEEK,
            NativeMethods.MOD_WIN | NativeMethods.MOD_NOREPEAT,
            (uint)NativeMethods.VK_SPACE,
            Toggle);
    }

    /// <summary>Call when Escape is pressed (detected by keyboard hook) to exit Peek.</summary>
    public void OnEscapePressed()
    {
        if (!_isPeeking) return;
        _isPeeking = false;
        PeekToggled?.Invoke(false);
    }

    private void Toggle()
    {
        _isPeeking = !_isPeeking;
        PeekToggled?.Invoke(_isPeeking);
    }

    public void Dispose() => _hotkey?.Dispose();
}
