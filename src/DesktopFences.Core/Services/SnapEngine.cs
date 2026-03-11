namespace DesktopFences.Core.Services;

/// <summary>
/// Calculates snap corrections when moving or resizing fences.
/// Snaps to screen edges and other fences' edges within a threshold.
/// </summary>
public static class SnapEngine
{
    public const double DefaultThreshold = 10.0;

    public readonly record struct Rect(double X, double Y, double Width, double Height)
    {
        public double Left => X;
        public double Top => Y;
        public double Right => X + Width;
        public double Bottom => Y + Height;
    }

    public readonly record struct SnapResult(double X, double Y, double Width, double Height);

    /// <summary>
    /// Snap a fence rect against screen bounds and other fence rects.
    /// Returns the corrected position/size.
    /// </summary>
    public static SnapResult Snap(
        Rect moving,
        IReadOnlyList<Rect> others,
        Rect screenBounds,
        double threshold = DefaultThreshold)
    {
        double dx = 0, dy = 0;

        // Collect all snap edges: screen edges + other fences' edges
        var verticalEdges = new List<double> { screenBounds.Left, screenBounds.Right };
        var horizontalEdges = new List<double> { screenBounds.Top, screenBounds.Bottom };

        foreach (var other in others)
        {
            verticalEdges.Add(other.Left);
            verticalEdges.Add(other.Right);
            horizontalEdges.Add(other.Top);
            horizontalEdges.Add(other.Bottom);
        }

        // Snap left/right edges of the moving rect to vertical edges
        double bestDx = double.MaxValue;
        foreach (var edge in verticalEdges)
        {
            // moving.Left → edge
            var d = edge - moving.Left;
            if (Math.Abs(d) < Math.Abs(bestDx) && Math.Abs(d) <= threshold)
                bestDx = d;

            // moving.Right → edge
            d = edge - moving.Right;
            if (Math.Abs(d) < Math.Abs(bestDx) && Math.Abs(d) <= threshold)
                bestDx = d;
        }
        if (bestDx != double.MaxValue)
            dx = bestDx;

        // Snap top/bottom edges of the moving rect to horizontal edges
        double bestDy = double.MaxValue;
        foreach (var edge in horizontalEdges)
        {
            // moving.Top → edge
            var d = edge - moving.Top;
            if (Math.Abs(d) < Math.Abs(bestDy) && Math.Abs(d) <= threshold)
                bestDy = d;

            // moving.Bottom → edge
            d = edge - moving.Bottom;
            if (Math.Abs(d) < Math.Abs(bestDy) && Math.Abs(d) <= threshold)
                bestDy = d;
        }
        if (bestDy != double.MaxValue)
            dy = bestDy;

        return new SnapResult(
            moving.X + dx,
            moving.Y + dy,
            moving.Width,
            moving.Height);
    }
}
