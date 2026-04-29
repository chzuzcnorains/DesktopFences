using DesktopFences.Shell.Interop;

namespace DesktopFences.Shell.Desktop;

/// <summary>
/// Registers a global hotkey (Ctrl+`) to trigger the search window.
/// </summary>
public class SearchHotkeyManager : IDisposable
{
    private const int HOTKEY_SEARCH = 9010;
    private const int VK_OEM_3 = 0xC0; // ` key

    private HotkeyHost? _hotkey;

    /// <summary>Fired when the search hotkey is pressed.</summary>
    public event Action? SearchRequested;

    public void Start()
    {
        _hotkey = new HotkeyHost("SearchHotkeyWindow");
        _hotkey.Register(HOTKEY_SEARCH,
            NativeMethods.MOD_CONTROL | NativeMethods.MOD_NOREPEAT,
            VK_OEM_3,
            () => SearchRequested?.Invoke());
    }

    public void Dispose() => _hotkey?.Dispose();
}
