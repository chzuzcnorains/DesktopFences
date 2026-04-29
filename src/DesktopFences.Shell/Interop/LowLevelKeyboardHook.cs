using System.Diagnostics;
using System.Runtime.InteropServices;

namespace DesktopFences.Shell.Interop;

/// <summary>
/// RAII wrapper around <c>SetWindowsHookEx(WH_KEYBOARD_LL, ...)</c>. Mirrors
/// <see cref="LowLevelMouseHook"/> for the keyboard side (used by DesktopEmbedManager
/// to detect Win+D and Escape).
/// </summary>
internal sealed class LowLevelKeyboardHook : IDisposable
{
    private IntPtr _hookId;
    private NativeMethods.LowLevelKeyboardProc? _proc;

    public void Install(NativeMethods.LowLevelKeyboardProc proc)
    {
        if (_hookId != IntPtr.Zero) return;
        _proc = proc;
        using var curProcess = Process.GetCurrentProcess();
        using var curModule = curProcess.MainModule!;
        _hookId = NativeMethods.SetWindowsHookEx(
            NativeMethods.WH_KEYBOARD_LL,
            _proc,
            NativeMethods.GetModuleHandle(curModule.ModuleName),
            0);

        if (_hookId == IntPtr.Zero)
            throw new InvalidOperationException(
                $"Failed to install keyboard hook. Error: {Marshal.GetLastWin32Error()}");
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
