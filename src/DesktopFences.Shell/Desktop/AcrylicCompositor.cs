using System.Runtime.InteropServices;
using DesktopFences.Shell.Interop;

namespace DesktopFences.Shell.Desktop;

/// <summary>
/// Toggles Win10/11 DWM Acrylic blur-behind on a window via the private
/// <c>SetWindowCompositionAttribute</c> API. See <c>docs/design/acrylic-blur.md</c>
/// for the rationale on GradientColor=0x01000000 (let WPF own the tint).
/// </summary>
public static class AcrylicCompositor
{
    /// <summary>
    /// Enable Acrylic blur-behind on the given window. The visible tint is left
    /// to the WPF background brush — pass <paramref name="gradientArgb"/> as a
    /// near-transparent ABGR value (default 0x01000000) to avoid DWM tint pollution.
    /// </summary>
    public static void Enable(IntPtr hwnd, uint gradientArgb = 0x01000000)
    {
        if (hwnd == IntPtr.Zero) return;
        ApplyAccent(hwnd, NativeMethods.AccentState.ACCENT_ENABLE_ACRYLICBLURBEHIND, gradientArgb);
    }

    /// <summary>
    /// Disable Acrylic blur-behind. Restores normal compositing.
    /// </summary>
    public static void Disable(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero) return;
        ApplyAccent(hwnd, NativeMethods.AccentState.ACCENT_DISABLED, 0);
    }

    private static void ApplyAccent(IntPtr hwnd, NativeMethods.AccentState state, uint gradientArgb)
    {
        var policy = new NativeMethods.AccentPolicy
        {
            AccentState = state,
            AccentFlags = 0x02, // DrawAllBorders — visually neutral with our self-drawn border
            GradientColor = gradientArgb,
            AnimationId = 0,
        };

        var size = Marshal.SizeOf<NativeMethods.AccentPolicy>();
        var ptr = Marshal.AllocHGlobal(size);
        try
        {
            Marshal.StructureToPtr(policy, ptr, false);
            var data = new NativeMethods.WindowCompositionAttributeData
            {
                Attribute = NativeMethods.WindowCompositionAttribute.WCA_ACCENT_POLICY,
                SizeOfData = size,
                Data = ptr,
            };
            NativeMethods.SetWindowCompositionAttribute(hwnd, ref data);
        }
        finally
        {
            Marshal.FreeHGlobal(ptr);
        }
    }
}
