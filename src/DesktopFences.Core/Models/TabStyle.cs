namespace DesktopFences.Core.Models;

/// <summary>
/// Tab strip visual style for multi-fence tab groups.
/// </summary>
public enum TabStyle
{
    /// <summary>Flat tabs with bottom indicator line.</summary>
    Flat,

    /// <summary>Segmented control with separator lines and outer rounded container.</summary>
    Segmented,

    /// <summary>Rounded capsule tabs.</summary>
    Rounded,

    /// <summary>No visible tab strip — tabs accessible via title bar dropdown menu.</summary>
    MenuOnly
}
