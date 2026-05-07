using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace DesktopFences.Core.Models;

/// <summary>
/// File icon rendering style for tiles inside a fence.
/// Phase 12: dual-card picker exposes App vs System;Shell stays as a hidden
/// fallback (configurable only by editing settings.json).
/// </summary>
public enum FileIconStyle
{
    /// <summary>Self-drawn colored tile + letter overlay (default,Phase 10).</summary>
    App,
    /// <summary>Windows-classic page-with-fold + colored bottom badge (Phase 12).</summary>
    System,
    /// <summary>SHGetFileInfo system icon (legacy fallback).</summary>
    Shell,
}

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
    /// Legacy boolean retained for backward compatibility. Old settings.json
    /// files only carried this field;new logic prefers <see cref="IconStyle"/>.
    /// True = App or System style;false = Shell style.
    /// </summary>
    public bool UseCustomFileIcons { get; set; } = true;

    /// <summary>
    /// Persisted file icon style. Nullable so we can detect "field missing in
    /// old JSON" and migrate from <see cref="UseCustomFileIcons"/>.
    /// External code reads/writes the non-null <see cref="IconStyle"/> facade.
    /// </summary>
    [JsonPropertyName("IconStyle")]
    public FileIconStyle? IconStyleRaw { get; set; }

    /// <summary>
    /// File icon rendering style. Default <see cref="FileIconStyle.App"/>.
    /// Setter keeps <see cref="UseCustomFileIcons"/> in sync so any code still
    /// reading the old flag continues to work.
    /// </summary>
    [JsonIgnore]
    public FileIconStyle IconStyle
    {
        get => IconStyleRaw ?? (UseCustomFileIcons ? FileIconStyle.App : FileIconStyle.Shell);
        set
        {
            IconStyleRaw = value;
            UseCustomFileIcons = value != FileIconStyle.Shell;
        }
    }

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

    /// <summary>
    /// Delay (ms) between Win+D detection and z-order elevation. Default 300ms
    /// matches Explorer's ShowDesktop animation; 0 = instant. Requires restart.
    /// </summary>
    public int WinDDetectionDelayMs { get; set; } = 300;

    /// <summary>
    /// Severity floor for diagnostic logging. One of: Error, Warn, Info, Debug, Trace.
    /// Only consumed when DebugLogging is true.
    /// </summary>
    public string LogLevel { get; set; } = "Info";

    // Auto-organize
    /// <summary>
    /// Whether to automatically scan the desktop every 2 seconds and classify files.
    /// </summary>
    public bool AutoOrganizeEnabled { get; set; } = true;
}
