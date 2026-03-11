using DesktopFences.Core.Services;

namespace DesktopFences.Core.Tests;

public class SnapEngineTests
{
    private static readonly SnapEngine.Rect Screen = new(0, 0, 1920, 1080);

    [Fact]
    public void SnapToLeftScreenEdge()
    {
        var moving = new SnapEngine.Rect(5, 100, 300, 200);
        var result = SnapEngine.Snap(moving, [], Screen);
        Assert.Equal(0, result.X);
    }

    [Fact]
    public void SnapToTopScreenEdge()
    {
        var moving = new SnapEngine.Rect(100, 7, 300, 200);
        var result = SnapEngine.Snap(moving, [], Screen);
        Assert.Equal(0, result.Y);
    }

    [Fact]
    public void SnapRightEdgeToScreenRight()
    {
        // 300 width, placed at x=1625 → right edge at 1925, within 10px of 1920
        var moving = new SnapEngine.Rect(1625, 100, 300, 200);
        var result = SnapEngine.Snap(moving, [], Screen);
        Assert.Equal(1620, result.X); // 1920 - 300 = 1620
    }

    [Fact]
    public void SnapToOtherFenceEdge()
    {
        var other = new SnapEngine.Rect(400, 100, 300, 200);
        // Place moving fence so its left edge is near the other's right edge (700)
        var moving = new SnapEngine.Rect(705, 100, 300, 200);
        var result = SnapEngine.Snap(moving, [other], Screen);
        Assert.Equal(700, result.X); // Snaps left edge to other's right edge
    }

    [Fact]
    public void NoSnapWhenFarAway()
    {
        var moving = new SnapEngine.Rect(500, 500, 300, 200);
        var result = SnapEngine.Snap(moving, [], Screen);
        Assert.Equal(500, result.X);
        Assert.Equal(500, result.Y);
    }

    [Fact]
    public void SnapBothAxes()
    {
        // Near top-left corner
        var moving = new SnapEngine.Rect(3, 8, 300, 200);
        var result = SnapEngine.Snap(moving, [], Screen);
        Assert.Equal(0, result.X);
        Assert.Equal(0, result.Y);
    }

    [Fact]
    public void SizePreserved()
    {
        var moving = new SnapEngine.Rect(5, 5, 300, 200);
        var result = SnapEngine.Snap(moving, [], Screen);
        Assert.Equal(300, result.Width);
        Assert.Equal(200, result.Height);
    }
}
