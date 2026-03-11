namespace DesktopFences.Core.Models;

/// <summary>
/// Represents a fence (desktop partition container) and its configuration.
/// </summary>
public class FenceDefinition
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Title { get; set; } = "New Fence";
    public FenceRect Bounds { get; set; } = new();
    public bool IsRolledUp { get; set; }
    public bool IsVisible { get; set; } = true;
    public int PageIndex { get; set; }
    public int MonitorIndex { get; set; }
    public ViewMode ViewMode { get; set; } = ViewMode.Icon;
    public SortField SortBy { get; set; } = SortField.Name;
    public SortDirection SortDirection { get; set; } = SortDirection.Ascending;
    public double RolledUpHeight { get; set; } = 32;
    public string? BackgroundColor { get; set; }
    public string? TitleBarColor { get; set; }
    public string? TextColor { get; set; }
    public double Opacity { get; set; } = 1.0;
    public string? PortalPath { get; set; }
    public List<string> FilePaths { get; set; } = [];
    public List<Guid> RuleIds { get; set; } = [];

    /// <summary>
    /// When non-null, this fence belongs to a tab group. All fences with the same
    /// TabGroupId are displayed as tabs in the same FenceHost window.
    /// </summary>
    public Guid? TabGroupId { get; set; }

    /// <summary>Display order within the tab group (0 = leftmost tab).</summary>
    public int TabOrder { get; set; }
}

public class FenceRect
{
    public double X { get; set; }
    public double Y { get; set; }
    public double Width { get; set; } = 300;
    public double Height { get; set; } = 200;
}

public enum ViewMode
{
    Icon,
    List,
    Detail
}

public enum SortField
{
    Name,
    Extension,
    Size,
    DateModified,
    DateCreated
}

public enum SortDirection
{
    Ascending,
    Descending
}
