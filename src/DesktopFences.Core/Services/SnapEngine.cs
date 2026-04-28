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

    public enum SnapEdge { Left, Right, Top, Bottom }

    public readonly record struct SnapLine(double Position, SnapEdge Edge, bool IsHorizontal);

    public readonly record struct SnapDetailResult(
        double X, double Y, double Width, double Height,
        IReadOnlyList<SnapLine> Lines);

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
        var detail = SnapWithDetail(moving, others, screenBounds, threshold);
        return new SnapResult(detail.X, detail.Y, detail.Width, detail.Height);
    }

    /// <summary>
    /// Snap a moving fence and return both the corrected position and the
    /// snap guide lines that should be displayed.
    /// </summary>
    public static SnapDetailResult SnapWithDetail(
        Rect moving,
        IReadOnlyList<Rect> others,
        Rect screenBounds,
        double threshold = DefaultThreshold)
    {
        var verticalEdges = new List<(double Position, bool IsScreen)> { (screenBounds.Left, true), (screenBounds.Right, true) };
        var horizontalEdges = new List<(double Position, bool IsScreen)> { (screenBounds.Top, true), (screenBounds.Bottom, true) };

        foreach (var other in others)
        {
            verticalEdges.Add((other.Left, false));
            verticalEdges.Add((other.Right, false));
            horizontalEdges.Add((other.Top, false));
            horizontalEdges.Add((other.Bottom, false));
        }

        var lines = new List<SnapLine>();

        // Snap left/right edges of the moving rect to vertical edges
        double bestDx = double.MaxValue;
        SnapEdge snapEdgeX = SnapEdge.Left;
        double snapLineX = 0;
        foreach (var (edge, _) in verticalEdges)
        {
            var d = edge - moving.Left;
            if (Math.Abs(d) < Math.Abs(bestDx) && Math.Abs(d) <= threshold)
            {
                bestDx = d;
                snapEdgeX = SnapEdge.Left;
                snapLineX = edge;
            }

            d = edge - moving.Right;
            if (Math.Abs(d) < Math.Abs(bestDx) && Math.Abs(d) <= threshold)
            {
                bestDx = d;
                snapEdgeX = SnapEdge.Right;
                snapLineX = edge;
            }
        }

        if (bestDx != double.MaxValue)
        {
            double x = moving.X + bestDx;
            // Vertical guide line spans the full height from moving rect top to the snap target
            double lineTop = Math.Min(moving.Top, snapLineX >= screenBounds.Top ? moving.Top : moving.Top);
            double lineBottom = moving.Bottom;
            lines.Add(new SnapLine(snapLineX, snapEdgeX, false));
        }

        // Snap top/bottom edges of the moving rect to horizontal edges
        double bestDy = double.MaxValue;
        SnapEdge snapEdgeY = SnapEdge.Top;
        double snapLineY = 0;
        foreach (var (edge, _) in horizontalEdges)
        {
            var d = edge - moving.Top;
            if (Math.Abs(d) < Math.Abs(bestDy) && Math.Abs(d) <= threshold)
            {
                bestDy = d;
                snapEdgeY = SnapEdge.Top;
                snapLineY = edge;
            }

            d = edge - moving.Bottom;
            if (Math.Abs(d) < Math.Abs(bestDy) && Math.Abs(d) <= threshold)
            {
                bestDy = d;
                snapEdgeY = SnapEdge.Bottom;
                snapLineY = edge;
            }
        }

        if (bestDy != double.MaxValue)
        {
            lines.Add(new SnapLine(snapLineY, snapEdgeY, true));
        }

        double dx = bestDx != double.MaxValue ? bestDx : 0;
        double dy = bestDy != double.MaxValue ? bestDy : 0;

        return new SnapDetailResult(
            moving.X + dx,
            moving.Y + dy,
            moving.Width,
            moving.Height,
            lines);
    }

    /// <summary>
    /// Snap a resizing fence against screen bounds and other fence rects.
    /// Returns the corrected position/size and snap guide lines.
    /// </summary>
    public static SnapDetailResult SnapResize(
        Rect moving,
        IReadOnlyList<Rect> others,
        Rect screenBounds,
        double threshold = DefaultThreshold)
    {
        // Resize snapping uses the same logic as move snapping —
        // all four edges can snap independently.
        return SnapWithDetail(moving, others, screenBounds, threshold);
    }
}
