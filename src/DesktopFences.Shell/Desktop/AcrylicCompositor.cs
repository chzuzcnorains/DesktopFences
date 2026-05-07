using System.Runtime.InteropServices;
using DesktopFences.Shell.Interop;

namespace DesktopFences.Shell.Desktop;

/// <summary>
/// Toggles Win10/11 DWM blur-behind on a window via the private
/// <c>SetWindowCompositionAttribute</c> API.
///
/// <para>
/// Uses <c>ACCENT_ENABLE_BLURBEHIND</c> instead of <c>ACCENT_ENABLE_ACRYLICBLURBEHIND</c>:
/// in Win11 22H2+ the Acrylic state composites a luminosity / tint layer on top of
/// the WPF surface and swallows the FenceBorder background — color/opacity sliders
/// stop being visible whenever blur is on. BlurBehind has no tint layer, so the
/// WPF <c>FenceBackgroundBrush</c> remains the source of truth for fence color.
/// See <c>docs/bug/acrylic_masks_color_opacity.md</c>.
/// </para>
/// </summary>
public static class AcrylicCompositor
{
    /// <summary>
    /// Enable DWM blur-behind on the given window. <paramref name="gradientArgb"/>
    /// is forwarded to the AccentPolicy but is unused under BlurBehind — kept for
    /// API symmetry with the older Acrylic call sites.
    /// </summary>
    public static void Enable(IntPtr hwnd, uint gradientArgb = 0x00000000)
    {
        if (hwnd == IntPtr.Zero) return;
        ApplyAccent(hwnd, NativeMethods.AccentState.ACCENT_ENABLE_BLURBEHIND, gradientArgb);
    }

    /// <summary>
    /// Disable blur-behind. Restores normal compositing.
    /// </summary>
    public static void Disable(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero) return;
        ApplyAccent(hwnd, NativeMethods.AccentState.ACCENT_DISABLED, 0);
    }

    /// <summary>
    /// Clip the window to a rounded rectangle so DWM blur (which fills the entire
    /// window rect) doesn't leak into the corners outside the WPF FenceBorder's
    /// CornerRadius. Re-call on every size change. <paramref name="cornerRadius"/>
    /// matches FenceBorder's CornerRadius (10 by default).
    /// </summary>
    public static void ApplyRoundedRegion(IntPtr hwnd, int width, int height, int cornerRadius)
    {
        if (hwnd == IntPtr.Zero || width <= 0 || height <= 0) return;
        // CreateRoundRectRgn ellipse params are full diameter, hence cornerRadius * 2.
        var hRgn = NativeMethods.CreateRoundRectRgn(0, 0, width + 1, height + 1,
            cornerRadius * 2, cornerRadius * 2);
        if (hRgn == IntPtr.Zero) return;
        // SetWindowRgn takes ownership of hRgn — do NOT DeleteObject after success.
        var rc = NativeMethods.SetWindowRgn(hwnd, hRgn, true);
        if (rc == 0) NativeMethods.DeleteObject(hRgn);
    }

    /// <summary>
    /// Clear the rounded clip region (return to default rectangular window).
    /// </summary>
    public static void ClearRegion(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero) return;
        NativeMethods.SetWindowRgn(hwnd, IntPtr.Zero, true);
    }

    private static void ApplyAccent(IntPtr hwnd, NativeMethods.AccentState state, uint gradientArgb)
    {
        var policy = new NativeMethods.AccentPolicy
        {
            AccentState = state,
            AccentFlags = 0, // BlurBehind ignores border-draw flags; keep clean to avoid tint side-effects
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
