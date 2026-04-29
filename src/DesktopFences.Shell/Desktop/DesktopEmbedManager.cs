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
    private readonly LowLevelKeyboardHook _keyboardHook = new();
    private IntPtr _winEventHookId;
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
        _keyboardHook.Install(KeyboardHookCallback);
        InstallForegroundHook();
        StartZOrderRecoveryTimer();
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
                    if (!WindowClassUtil.IsDesktopOrTaskbarWindow(NativeMethods.GetForegroundWindow()))
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
    /// Applies WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE and ensures it's visible above desktop.
    /// </summary>
    public void RegisterWindow(IntPtr hwnd)
    {
        var exStyle = NativeMethods.GetWindowLongPtr(hwnd, NativeMethods.GWL_EXSTYLE);
        var newStyle = (long)exStyle | NativeMethods.WS_EX_TOOLWINDOW | NativeMethods.WS_EX_NOACTIVATE;
        NativeMethods.SetWindowLongPtr(hwnd, NativeMethods.GWL_EXSTYLE, new IntPtr(newStyle));

        _managedWindows.Add(hwnd);

        // 新创建的窗口：直接放置在桌面上方，确保立即可见
        // 1. 先显示窗口
        NativeMethods.ShowWindow(hwnd, NativeMethods.SW_SHOW);

        // 2. 使用 HWND_TOP 确保在最上层（WS_EX_NOACTIVATE 保证不会干扰焦点）
        NativeMethods.SetWindowPos(
            hwnd, NativeMethods.HWND_TOP,
            0, 0, 0, 0,
            NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE);

        // 3. 不要立即设置回 HWND_BOTTOM，让用户先看到窗口
        // z-order 恢复定时器会在稍后处理
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
        if (WindowClassUtil.IsDesktopOrTaskbarWindow(NativeMethods.GetForegroundWindow()))
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

        return _keyboardHook.CallNext(nCode, wParam, lParam);
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

        // Ignore activation of our own managed windows
        if (_managedWindows.Contains(hwnd)) return;

        // Ignore activation of any window belonging to our own process (e.g. context menus, dialogs)
        // Prevents our own UI elements from dismissing Win+D topmost state
        uint foregroundProcessId;
        NativeMethods.GetWindowThreadProcessId(hwnd, out foregroundProcessId);
        var currentProcessId = (uint)System.Diagnostics.Process.GetCurrentProcess().Id;
        if (foregroundProcessId == currentProcessId)
            return;

        if (_isTopmost)
        {
            // If the new foreground is a desktop window (Progman/WorkerW), DON'T dismiss —
            // being visible on top of the desktop is the intended state after Win+D.
            if (WindowClassUtil.IsDesktopOrTaskbarWindow(hwnd))
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
            if (WindowClassUtil.IsDesktopOrTaskbarWindow(hwnd))
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
        if (WindowClassUtil.IsDesktopOrTaskbarWindow(NativeMethods.GetForegroundWindow()))
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

    /// <summary>
    /// Safely bring a window back and ensure it's visible above the desktop,
    /// even when the desktop is the foreground window.
    /// </summary>
    public void EnsureVisibleAboveDesktop(IntPtr hwnd)
    {
        // First, ensure the window is visible
        if (!NativeMethods.IsWindowVisible(hwnd))
        {
            NativeMethods.ShowWindow(hwnd, NativeMethods.SW_SHOW);
        }

        // Now, a safe two-step approach to get above the desktop without
        // getting stuck below it:
        // 1. Temporarily bring to top (but don't activate) so we're definitely visible
        // 2. Then carefully place below other windows but still above desktop

        // Step 1: Bring to top temporarily
        NativeMethods.SetWindowPos(
            hwnd, NativeMethods.HWND_TOP,
            0, 0, 0, 0,
            NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE);

        // Step 2: Ask the z-order recovery to fix it up when it's safe
        // We can also check if foreground is not desktop first, and if so, set bottom immediately
        var foreground = NativeMethods.GetForegroundWindow();
        if (!WindowClassUtil.IsDesktopOrTaskbarWindow(foreground))
        {
            // Safe to set bottom now
            NativeMethods.SetWindowPos(
                hwnd, NativeMethods.HWND_BOTTOM,
                0, 0, 0, 0,
                NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE |
                NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_SHOWWINDOW);
        }
        // If desktop IS foreground, we just leave it at HWND_TOP for now, and
        // the z-order recovery timer will fix it when the foreground changes.
        // HWND_TOP is okay - it's still below normal apps because of WS_EX_NOACTIVATE.
    }

    /// <summary>
    /// 让一个新创建的窗口立刻可见在桌面之上，专为"用户主动新建 Fence"路径使用
    /// （托盘菜单的"新建 Fence" / "新建文件夹映射 Fence..." / 规则触发创建）。
    ///
    /// Windows 11 上当桌面 (Progman/WorkerW) 或任务栏 (Shell_TrayWnd) 是前台时，
    /// 对 WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE 窗口调用 SetWindowPos(HWND_TOP) 仍可
    /// 能被 DWM 推到桌面壁纸层下方，导致窗口看不见。本方法用 HWND_TOPMOST 绕开壁纸层
    /// 压制确保新窗口立刻可见，并跟踪此 hwnd —— 当用户随后切到任意普通窗口时，
    /// OnForegroundChanged 会调用 SendToBottom，HWND_BOTTOM 会自动清除 topmost 状态。
    ///
    /// 启动加载、ToggleAllFences 等路径不应调用本方法，以免污染常规 z-order。
    /// </summary>
    public void BringNewWindowToFront(IntPtr hwnd)
    {
        if (!NativeMethods.IsWindowVisible(hwnd))
        {
            NativeMethods.ShowWindow(hwnd, NativeMethods.SW_SHOW);
        }

        var foreground = NativeMethods.GetForegroundWindow();
        if (WindowClassUtil.IsDesktopOrTaskbarWindow(foreground))
        {
            // 桌面/任务栏前台 —— HWND_TOP 不可靠，用 HWND_TOPMOST。
            // OnForegroundChanged → SendToBottom 会在用户切到普通窗口时通过
            // HWND_BOTTOM 自动清除 topmost。
            NativeMethods.SetWindowPos(
                hwnd, NativeMethods.HWND_TOPMOST,
                0, 0, 0, 0,
                NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE |
                NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_SHOWWINDOW);
        }
        else
        {
            // 前台是普通窗口，正常逻辑已经够用 —— 直接放回 z-order 底部
            NativeMethods.SetWindowPos(
                hwnd, NativeMethods.HWND_BOTTOM,
                0, 0, 0, 0,
                NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE |
                NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_SHOWWINDOW);
        }
    }

    private static void SendToBottom(IntPtr hwnd)
    {
        // Skip windows that are intentionally hidden (e.g. page switch, toggle).
        // Calling SetWindowPos on hidden windows can interfere with WPF visibility state.
        if (!NativeMethods.IsWindowVisible(hwnd)) return;

        // CRITICAL: Never call SetWindowPos(HWND_BOTTOM) when the foreground window
        // is the desktop! On Windows 11, this pushes WS_EX_TOOLWINDOW windows BELOW
        // the wallpaper layer, making them invisible until a non-desktop foreground
        // window triggers a z-order recovery.

        var foreground = NativeMethods.GetForegroundWindow();
        if (WindowClassUtil.IsDesktopOrTaskbarWindow(foreground))
        {
            // When desktop is foreground, don't change z-order at all!
            // Just ensure we're visible, but don't touch z-order.
            return;
        }

        // Normal path: foreground is not desktop, safe to use HWND_BOTTOM
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

        _keyboardHook.Dispose();

        if (_winEventHookId != IntPtr.Zero)
        {
            NativeMethods.UnhookWinEvent(_winEventHookId);
            _winEventHookId = IntPtr.Zero;
        }
    }
}
