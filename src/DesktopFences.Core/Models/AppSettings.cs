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
