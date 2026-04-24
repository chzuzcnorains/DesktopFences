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
