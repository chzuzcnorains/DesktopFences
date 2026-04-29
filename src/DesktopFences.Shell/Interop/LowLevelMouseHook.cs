using System.Diagnostics;

namespace DesktopFences.Shell.Interop;

/// <summary>
/// RAII wrapper around <c>SetWindowsHookEx(WH_MOUSE_LL, ...)</c>. Holds a strong
/// reference to the delegate so the hook callback is never collected while
/// installed. Replaces the duplicated install/uninstall plumbing in
/// QuickHideManager and PageSwitchManager.
/// </summary>
internal sealed class LowLevelMouseHook : IDisposable
{
    private IntPtr _hookId;
    private NativeMethods.LowLevelMouseProc? _proc;

    public bool IsInstalled => _hookId != IntPtr.Zero;

    public void Install(NativeMethods.LowLevelMouseProc proc)
    {
        if (_hookId != IntPtr.Zero) return;
        _proc = proc;
        using var curProcess = Process.GetCurrentProcess();
        using var curModule = curProcess.MainModule!;
        _hookId = NativeMethods.SetWindowsHookEx(
            NativeMethods.WH_MOUSE_LL,
            _proc,
            NativeMethods.GetModuleHandle(curModule.ModuleName),
            0);
    }

    public void Uninstall()
    {
        if (_hookId == IntPtr.Zero) return;
        NativeMethods.UnhookWindowsHookEx(_hookId);
        _hookId = IntPtr.Zero;
        _proc = null;
    }

    public IntPtr CallNext(int nCode, IntPtr wParam, IntPtr lParam)
        => NativeMethods.CallNextHookEx(_hookId, nCode, wParam, lParam);

    public void Dispose() => Uninstall();
}
