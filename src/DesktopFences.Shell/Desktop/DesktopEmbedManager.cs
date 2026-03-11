using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Threading;
using DesktopFences.Shell.Interop;

namespace DesktopFences.Shell.Desktop;

/// <summary>
/// Manages fence windows' z-order to keep them above desktop but below other windows.
///
/// Z-order strategy:
///   Normal:  HWND_BOTTOM — window sits at bottom of z-order, above desktop, below all apps
///   Win+D:   HWND_TOPMOST (temporary) — survives ShowDesktop
///   After:   When user activates any other window → back to HWND_BOTTOM
/// </summary>
public sealed class DesktopEmbedManager : IDisposable
{
    private readonly List<IntPtr> _managedWindows = [];
    private IntPtr _keyboardHookId;
    private IntPtr _winEventHookId;
    private NativeMethods.LowLevelKeyboardProc? _keyboardHookProc;
    private NativeMethods.WinEventDelegate? _winEventProc;
    private bool _isTopmost;
    private bool _isPeekActive;
    private bool _winKeyDown;
    private System.Timers.Timer? _showDesktopTimer;
    private System.Timers.Timer? _zOrderRecoveryTimer;
    private DispatcherTimer? _foregroundDebounceTimer;
    private Dispatcher? _dispatcher;
    private bool _disposed;

    public event Action? ShowDesktopDetected;
    public event Action? EscapePressed;
    public event Action<string>? StatusChanged;

    public void Start()
    {
        _dispatcher = Dispatcher.CurrentDispatcher;
        InstallKeyboardHook();
        InstallForegroundHook();
        StartZOrderRecoveryTimer();
    }

    private void InstallKeyboardHook()
    {
        _keyboardHookProc = KeyboardHookCallback;
        using var curProcess = Process.GetCurrentProcess();
        using var curModule = curProcess.MainModule!;
        _keyboardHookId = NativeMethods.SetWindowsHookEx(
            NativeMethods.WH_KEYBOARD_LL,
            _keyboardHookProc,
            NativeMethods.GetModuleHandle(curModule.ModuleName),
            0);

        if (_keyboardHookId == IntPtr.Zero)
            throw new InvalidOperationException(
                $"Failed to install keyboard hook. Error: {Marshal.GetLastWin32Error()}");
    }

    private void InstallForegroundHook()
    {
        _winEventProc = OnForegroundChanged;
        _winEventHookId = NativeMethods.SetWinEventHook(
            NativeMethods.EVENT_SYSTEM_FOREGROUND,
            NativeMethods.EVENT_SYSTEM_FOREGROUND,
            IntPtr.Zero,
            _winEventProc,
            0, 0,
            NativeMethods.WINEVENT_OUTOFCONTEXT);
    }

    private void StartZOrderRecoveryTimer()
    {
        _zOrderRecoveryTimer = new System.Timers.Timer(5000); // every 5 seconds
        _zOrderRecoveryTimer.AutoReset = true;
        _zOrderRecoveryTimer.Elapsed += (_, _) =>
        {
            // Dispatch to UI thread to avoid cross-thread SetWindowPos races
            _dispatcher?.BeginInvoke(() =>
            {
                // Only re-apply bottom z-order when not in topmost/peek mode
                if (!_isTopmost && !_isPeekActive && _managedWindows.Count > 0)
                {
                    foreach (var hwnd in _managedWindows)
                        SendToBottom(hwnd);
                }
            });
        };
        _zOrderRecoveryTimer.Start();
    }

    /// <summary>
    /// Register a window to be managed. Call after WPF window Loaded event.
    /// Applies WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE and sends to HWND_BOTTOM.
    /// </summary>
    public void RegisterWindow(IntPtr hwnd)
    {
        var exStyle = NativeMethods.GetWindowLongPtr(hwnd, NativeMethods.GWL_EXSTYLE);
        var newStyle = (long)exStyle | NativeMethods.WS_EX_TOOLWINDOW | NativeMethods.WS_EX_NOACTIVATE;
        NativeMethods.SetWindowLongPtr(hwnd, NativeMethods.GWL_EXSTYLE, new IntPtr(newStyle));

        _managedWindows.Add(hwnd);

        // Immediately send to bottom — above desktop, below everything else
        SendToBottom(hwnd);
    }

    public void UnregisterWindow(IntPtr hwnd)
    {
        _managedWindows.Remove(hwnd);
    }

    /// <summary>
    /// Temporarily bring a single window above all other managed windows (for drag operations).
    /// Places it just above other fence windows without making it truly topmost.
    /// </summary>
    public void BringWindowAboveSiblings(IntPtr hwnd)
    {
        // Place above other HWND_BOTTOM windows by using HWND_TOP briefly
        // — still below normal app windows since WS_EX_NOACTIVATE is set
        NativeMethods.SetWindowPos(
            hwnd, NativeMethods.HWND_TOP,
            0, 0, 0, 0,
            NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE |
            NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_NOSENDCHANGING);
    }

    /// <summary>
    /// Restore a window back to HWND_BOTTOM after a drag operation.
    /// </summary>
    public void RestoreWindowToBottom(IntPtr hwnd)
    {
        SendToBottom(hwnd);
    }

    private IntPtr KeyboardHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            var kb = Marshal.PtrToStructure<NativeMethods.KBDLLHOOKSTRUCT>(lParam);
            var msg = (int)wParam;

            if (msg == NativeMethods.WM_KEYDOWN || msg == NativeMethods.WM_SYSKEYDOWN)
            {
                if (kb.vkCode == NativeMethods.VK_LWIN || kb.vkCode == NativeMethods.VK_RWIN)
                    _winKeyDown = true;

                if (kb.vkCode == NativeMethods.VK_D && _winKeyDown)
                    OnShowDesktopDetected();

                if (kb.vkCode == NativeMethods.VK_ESCAPE)
                    EscapePressed?.Invoke();
            }
            else
            {
                if (kb.vkCode == NativeMethods.VK_LWIN || kb.vkCode == NativeMethods.VK_RWIN)
                    _winKeyDown = false;
            }
        }

        return NativeMethods.CallNextHookEx(_keyboardHookId, nCode, wParam, lParam);
    }

    private void OnShowDesktopDetected()
    {
        ShowDesktopDetected?.Invoke();

        _showDesktopTimer?.Stop();
        _showDesktopTimer?.Dispose();

        // Delay to let Explorer finish ShowDesktop, then bring to top
        _showDesktopTimer = new System.Timers.Timer(300);
        _showDesktopTimer.AutoReset = false;
        _showDesktopTimer.Elapsed += (_, _) =>
        {
            SetAllTopmost();
            StatusChanged?.Invoke("TOPMOST (Win+D survived)");
        };
        _showDesktopTimer.Start();
    }

    /// <summary>
    /// Called when any window becomes the foreground window.
    /// If we're in topmost mode and user activates a non-fence window, restore to bottom.
    /// When in normal mode, debounce z-order recovery to avoid rapid SendToBottom calls
    /// during transient system UI interactions (e.g. tray notification area popups).
    /// </summary>
    private void OnForegroundChanged(
        IntPtr hWinEventHook, uint eventType, IntPtr hwnd,
        int idObject, int idChild, uint dwEventThread, uint dwmsEventTime)
    {
        // Don't auto-dismiss during Peek mode — Peek is dismissed by hotkey/Escape
        if (_isPeekActive) return;

        // If the activated window is one of our managed windows, ignore
        if (_managedWindows.Contains(hwnd)) return;

        if (_isTopmost)
        {
            // User activated a real window → fences go back to bottom
            SetAllBottom();
            StatusChanged?.Invoke("BOTTOM (behind other windows)");
        }
        else
        {
            // Debounce z-order recovery: wait 200ms to coalesce rapid foreground changes
            // (e.g. clicking tray icon arrow triggers multiple foreground events).
            // This prevents aggressive HWND_BOTTOM calls that can send fences behind desktop.
            _foregroundDebounceTimer?.Stop();
            _foregroundDebounceTimer ??= new DispatcherTimer(DispatcherPriority.Normal, _dispatcher ?? Dispatcher.CurrentDispatcher)
            {
                Interval = TimeSpan.FromMilliseconds(200)
            };
            _foregroundDebounceTimer.Tick += OnDebouncedForegroundRecovery;
            _foregroundDebounceTimer.Start();
        }
    }

    private void OnDebouncedForegroundRecovery(object? sender, EventArgs e)
    {
        _foregroundDebounceTimer?.Stop();
        _foregroundDebounceTimer!.Tick -= OnDebouncedForegroundRecovery;

        if (_isTopmost || _isPeekActive) return;

        foreach (var w in _managedWindows)
            SendToBottom(w);
    }

    /// <summary>
    /// Enter Peek mode — fences go topmost and stay there until ExitPeek.
    /// </summary>
    public void EnterPeek()
    {
        _isPeekActive = true;
        SetAllTopmost();
    }

    /// <summary>
    /// Exit Peek mode — fences go back to bottom.
    /// </summary>
    public void ExitPeek()
    {
        _isPeekActive = false;
        SetAllBottom();
    }

    private void SetAllTopmost()
    {
        _isTopmost = true;
        foreach (var hwnd in _managedWindows)
        {
            NativeMethods.SetWindowPos(
                hwnd, NativeMethods.HWND_TOPMOST,
                0, 0, 0, 0,
                NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE |
                NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_SHOWWINDOW);
        }
    }

    private void SetAllBottom()
    {
        _isTopmost = false;
        foreach (var hwnd in _managedWindows)
        {
            SendToBottom(hwnd);
        }
    }

    private static void SendToBottom(IntPtr hwnd)
    {
        // Skip windows that are intentionally hidden (e.g. page switch, toggle).
        // Calling SetWindowPos on hidden windows can interfere with WPF visibility state.
        if (!NativeMethods.IsWindowVisible(hwnd)) return;

        NativeMethods.SetWindowPos(
            hwnd, NativeMethods.HWND_BOTTOM,
            0, 0, 0, 0,
            NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE |
            NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_SHOWWINDOW);
    }

    public bool IsTopmost => _isTopmost;

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _showDesktopTimer?.Stop();
        _showDesktopTimer?.Dispose();
        _zOrderRecoveryTimer?.Stop();
        _zOrderRecoveryTimer?.Dispose();
        _foregroundDebounceTimer?.Stop();

        if (_keyboardHookId != IntPtr.Zero)
        {
            NativeMethods.UnhookWindowsHookEx(_keyboardHookId);
            _keyboardHookId = IntPtr.Zero;
        }

        if (_winEventHookId != IntPtr.Zero)
        {
            NativeMethods.UnhookWinEvent(_winEventHookId);
            _winEventHookId = IntPtr.Zero;
        }
    }
}
