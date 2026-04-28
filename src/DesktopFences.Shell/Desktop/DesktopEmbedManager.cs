using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Threading;
using DesktopFences.Shell.Interop;

namespace DesktopFences.Shell.Desktop;

/// <summary>
/// Manages fence windows' z-order to keep them above desktop but below other windows.
///
/// Z-order strategy:
///   Normal:  HWND_BOTTOM — window sits at bottom of z-order, above desktop, below all apps
///   Win+D:   HWND_TOPMOST (temporary) — survives ShowDesktop
///   Drag:    HWND_TOP during drag — suppress foreground recovery — restore after drag ends
///   After:   When user activates a real (non-desktop) window → back to HWND_BOTTOM
///
/// Critical: HWND_BOTTOM can push windows behind the desktop when the desktop
/// (Progman/WorkerW) is the foreground window. All SendToBottom calls must be
/// guarded with a desktop-foreground check to avoid this.
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
    private bool _pendingTopmost;   // true while 300ms timer is in-flight
    private bool _winKeyDown;
    private bool _isDragging;       // true during drag — suppress foreground z-order recovery
    private System.Timers.Timer? _showDesktopTimer;
    private System.Timers.Timer? _zOrderRecoveryTimer;
    private DispatcherTimer? _foregroundDebounceTimer;
    private bool _foregroundDebounceHandlerAttached;
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
                // Only re-apply bottom z-order when not in topmost/peek/drag mode
                if (!_isTopmost && !_isPeekActive && !_isDragging && _managedWindows.Count > 0)
                {
                    // Skip recovery when desktop is foreground — calling HWND_BOTTOM
                    // in this state can push windows behind the desktop on Windows 11.
                    if (!IsDesktopWindow(NativeMethods.GetForegroundWindow()))
                    {
                        foreach (var hwnd in _managedWindows)
                            SendToBottom(hwnd);
                    }
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
    /// Temporarily bring a single window above other managed windows (for drag operations).
    /// Sets _isDragging to suppress foreground-change z-order recovery during drag.
    /// </summary>
    public void BringWindowAboveSiblings(IntPtr hwnd)
    {
        _isDragging = true;

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
    /// Clears _isDragging flag so foreground recovery can resume.
    /// When the desktop is foreground, defers SendToBottom to avoid pushing
    /// windows behind the desktop (Windows 11 z-order quirk).
    /// </summary>
    public void RestoreWindowToBottom(IntPtr hwnd)
    {
        _isDragging = false;

        // After drag, the desktop is likely foreground. Calling HWND_BOTTOM
        // in this state can push our window behind the desktop. Defer the
        // z-order correction to the recovery timer (fires in ≤5 seconds)
        // or the next foreground change to a non-desktop window.
        if (IsDesktopWindow(NativeMethods.GetForegroundWindow()))
            return;

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
        _showDesktopTimer = null;

        if (_isTopmost)
        {
            // Was topmost → go back to bottom
            _pendingTopmost = false;
            SetAllBottom();
            StatusChanged?.Invoke("BOTTOM (Win+D restore)");
            return;
        }

        if (_pendingTopmost)
        {
            // Timer was in-flight → user pressed Win+D again before it fired.
            // This means Explorer toggled back (restore windows), so cancel the topmost transition.
            _pendingTopmost = false;
            SetAllBottom();
            StatusChanged?.Invoke("BOTTOM (Win+D cancelled)");
            return;
        }

        // First Win+D press (show desktop) — delay to let Explorer finish, then bring to top
        _pendingTopmost = true;
        _showDesktopTimer = new System.Timers.Timer(300);
        _showDesktopTimer.AutoReset = false;
        _showDesktopTimer.Elapsed += (_, _) =>
        {
            // Dispatch to UI thread to avoid cross-thread races with _isTopmost and SetWindowPos
            _dispatcher?.BeginInvoke(() =>
            {
                if (!_pendingTopmost) return; // cancelled by a rapid second press
                _pendingTopmost = false;
                SetAllTopmost();
                StatusChanged?.Invoke("TOPMOST (Win+D survived)");
            });
        };
        _showDesktopTimer.Start();
    }

    /// <summary>
    /// Called when any window becomes the foreground window.
    /// - During drag: suppress all z-order changes to prevent panels disappearing.
    /// - During Win+D topmost: only dismiss when a real app activates (not desktop).
    /// - Normal mode: skip if desktop is foreground (no recovery needed; SendToBottom
    ///   when desktop is foreground can push windows behind the desktop).
    /// </summary>
    private void OnForegroundChanged(
        IntPtr hWinEventHook, uint eventType, IntPtr hwnd,
        int idObject, int idChild, uint dwEventThread, uint dwmsEventTime)
    {
        // Don't auto-dismiss during Peek mode — Peek is dismissed by hotkey/Escape
        if (_isPeekActive) return;

        // Don't interfere while Win+D topmost transition is pending
        if (_pendingTopmost) return;

        // Don't interfere during drag — prevents panels disappearing while snapping
        if (_isDragging) return;

        // If the activated window is one of our managed windows, ignore
        if (_managedWindows.Contains(hwnd)) return;

        if (_isTopmost)
        {
            // If the new foreground is a desktop window (Progman/WorkerW), DON'T dismiss —
            // being visible on top of the desktop is the intended state after Win+D.
            if (IsDesktopWindow(hwnd))
                return;

            // A real application window activated → fences go back to bottom
            SetAllBottom();
            StatusChanged?.Invoke("BOTTOM (behind other windows)");
        }
        else
        {
            // If the foreground is a desktop window, our windows at HWND_BOTTOM are already
            // correctly positioned above it — no recovery needed. Calling SendToBottom when
            // the desktop is foreground can push our windows behind the desktop on Windows 11.
            if (IsDesktopWindow(hwnd))
                return;

            // Debounce z-order recovery: wait 200ms to coalesce rapid foreground changes
            StartForegroundDebounce();
        }
    }

    private void StartForegroundDebounce()
    {
        if (_foregroundDebounceTimer is null)
        {
            _foregroundDebounceTimer = new DispatcherTimer(DispatcherPriority.Normal,
                _dispatcher ?? Dispatcher.CurrentDispatcher)
            {
                Interval = TimeSpan.FromMilliseconds(200)
            };
        }

        if (!_foregroundDebounceHandlerAttached)
        {
            _foregroundDebounceTimer.Tick += OnDebouncedForegroundRecovery;
            _foregroundDebounceHandlerAttached = true;
        }

        _foregroundDebounceTimer.Stop();
        _foregroundDebounceTimer.Start();
    }

    private void OnDebouncedForegroundRecovery(object? sender, EventArgs e)
    {
        _foregroundDebounceTimer?.Stop();

        if (_isTopmost || _isPeekActive || _pendingTopmost || _isDragging) return;

        // Skip recovery when desktop is foreground — calling HWND_BOTTOM
        // in this state can push windows behind the desktop on Windows 11.
        if (IsDesktopWindow(NativeMethods.GetForegroundWindow()))
            return;

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

    /// <summary>
    /// Check if a window handle belongs to the desktop (Progman or WorkerW).
    /// Used to prevent Win+D TOPMOST dismissal and HWND_BOTTOM calls when
    /// the desktop is foreground (which can push fence windows behind the desktop).
    /// </summary>
    private static bool IsDesktopWindow(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero) return false;

        var sb = new StringBuilder(256);
        NativeMethods.GetClassName(hwnd, sb, sb.Capacity);
        var className = sb.ToString();

        if (className is "Progman" or "WorkerW" or "SHELLDLL_DefView" or "SysListView32")
            return true;

        // Check parent chain
        var parent = NativeMethods.GetParent(hwnd);
        while (parent != IntPtr.Zero)
        {
            sb.Clear();
            NativeMethods.GetClassName(parent, sb, sb.Capacity);
            var parentClass = sb.ToString();
            if (parentClass is "Progman" or "WorkerW" or "SHELLDLL_DefView")
                return true;
            parent = NativeMethods.GetParent(parent);
        }

        return false;
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
