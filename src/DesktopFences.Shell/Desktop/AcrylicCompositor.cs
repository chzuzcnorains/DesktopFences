using System.Diagnostics;
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
    /// Enable DWM blur-behind on the given window. Returns <c>true</c> on success;
    /// <c>false</c> when the hwnd is invalid or the underlying private API rejects
    /// the call (unsupported Windows build, hwnd belongs to another process, etc.).
    /// On failure the caller should leave the WPF FenceBorder background opaque so
    /// the fence remains visible without DWM blur.
    /// <paramref name="gradientArgb"/> is forwarded to the AccentPolicy but is
    /// unused under BlurBehind — kept for API symmetry with older Acrylic call sites.
    /// </summary>
    public static bool Enable(IntPtr hwnd, uint gradientArgb = 0x00000000)
    {
        if (hwnd == IntPtr.Zero) return false;
        return ApplyAccent(hwnd, NativeMethods.AccentState.ACCENT_ENABLE_BLURBEHIND, gradientArgb);
    }

    /// <summary>
    /// Disable blur-behind. Restores normal compositing. Returns <c>true</c> on
    /// success; failure is non-fatal (the hwnd may already be in the disabled state).
    /// </summary>
    public static bool Disable(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero) return false;
        return ApplyAccent(hwnd, NativeMethods.AccentState.ACCENT_DISABLED, 0);
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

    private static bool ApplyAccent(IntPtr hwnd, NativeMethods.AccentState state, uint gradientArgb)
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
            // Private API; treat any non-zero return as success (Win32 convention).
            // Zero means the call was rejected — log so failures aren't silent.
            var rc = NativeMethods.SetWindowCompositionAttribute(hwnd, ref data);
            if (rc == 0)
            {
                Debug.WriteLine($"[AcrylicCompositor] SetWindowCompositionAttribute failed for state={state}, hwnd=0x{hwnd.ToInt64():X}");
                return false;
            }
            return true;
        }
        finally
        {
            Marshal.FreeHGlobal(ptr);
        }
    }
}
