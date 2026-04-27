using System.Collections.Generic;

namespace DesktopFences.Core.Models;

/// <summary>
/// Global application settings, persisted to settings.json.
/// </summary>
public class AppSettings
{
    // Appearance
    public string DefaultFenceColor { get; set; } = "#CC1E1E2E";
    public string DefaultTitleBarColor { get; set; } = "#44FFFFFF";
    public string DefaultTextColor { get; set; } = "#DDEEEEEE";
    public double DefaultOpacity { get; set; } = 1.0;
    public double TitleBarFontSize { get; set; } = 13;
    public TabStyle TabStyle { get; set; } = TabStyle.Flat;

    /// <summary>
    /// When true (default), file items use the self-drawn colored file-type
    /// tiles (FileTypes.xaml + letter overlay). When false, falls back to
    /// the Shell-extracted Windows icon.
    /// </summary>
    public bool UseCustomFileIcons { get; set; } = true;

    /// <summary>
    /// Icon body size in pixels for file tiles inside a fence. The surrounding
    /// tile also scales: tile width = IconSize + 44, height = IconSize + 52.
    /// Valid range 28–64.
    /// </summary>
    public int IconSize { get; set; } = 44;

    /// <summary>
    /// Accent color (sRGB hex). Drives DynamicResource AccentColor — affects
    /// selected items, focus glow, primary buttons. v2 prototype uses
    /// 6 swatches: blue (#7AA7E6), indigo, teal, green, amber, coral.
    /// </summary>
    public string AccentColor { get; set; } = "#7AA7E6";

    /// <summary>
    /// Fence background hue 0-360. Combined with FenceOpacity it produces
    /// the per-fence translucent body color.
    /// </summary>
    public int FenceBgHue { get; set; } = 220;

    /// <summary>
    /// Fence body opacity (0.20 – 0.90). Default 0.85 means semi-transparent
    /// glass-like background.
    /// </summary>
    public double FenceOpacity { get; set; } = 0.85;

    /// <summary>
    /// Fence shadow blur radius (0 – 60 px). Drives the soft drop shadow under
    /// each fence panel; 0 disables it. WPF's DropShadowEffect approximates
    /// the CSS backdrop-filter blur from the v2 prototype.
    /// </summary>
    public int FenceBlurRadius { get; set; } = 26;

    /// <summary>
    /// FIFO of recently-closed fences (FenceDefinition serialized as JSON).
    /// Up to 20 entries. Drives the "Recently closed" panel + restore menu.
    /// </summary>
    public List<string> RecentClosedFences { get; set; } = new();

    // Behavior
    public int SnapThreshold { get; set; } = 10;
    public int RollupHoverDelay { get; set; } = 0;
    public bool QuickHideEnabled { get; set; } = true;

    // Startup
    public bool StartWithWindows { get; set; }
    public bool StartMinimized { get; set; } = true;

    // Advanced
    public bool CompatibilityMode { get; set; }
    public bool DebugLogging { get; set; }

    // Auto-organize
    /// <summary>
    /// Whether to automatically scan the desktop every 2 seconds and classify files.
    /// </summary>
    public bool AutoOrganizeEnabled { get; set; } = true;
}
