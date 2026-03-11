namespace DesktopFences.Core.Models;

/// <summary>
/// A saved snapshot of all fences' positions, sizes, and contents.
/// Can be restored to return to a previous desktop layout.
/// </summary>
public class LayoutSnapshot
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public List<FenceDefinition> Fences { get; set; } = [];
    public ScreenConfiguration ScreenConfig { get; set; } = new();
}

public class ScreenConfiguration
{
    public int ScreenCount { get; set; } = 1;
    public string ConfigHash { get; set; } = string.Empty;
    public List<ScreenInfo> Screens { get; set; } = [];
}

public class ScreenInfo
{
    public int Index { get; set; }
    public double X { get; set; }
    public double Y { get; set; }
    public double Width { get; set; }
    public double Height { get; set; }
    public double DpiScale { get; set; } = 1.0;
    public bool IsPrimary { get; set; }
}

/// <summary>
/// Represents a virtual desktop page containing its own set of fences.
/// </summary>
public class DesktopPage
{
    public int PageIndex { get; set; }
    public string Name { get; set; } = string.Empty;
    public List<Guid> FenceIds { get; set; } = [];
}
