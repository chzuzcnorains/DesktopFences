using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shapes;
using DesktopFences.Core.Services;
using DesktopFences.Shell.Interop;

namespace DesktopFences.UI.Controls;

/// <summary>
/// A transparent, click-through overlay that displays snap guide lines
/// while a fence is being dragged or resized.
/// </summary>
public partial class SnapGuideOverlay : Window
{
    private static readonly Color GuideColor = Color.FromRgb(0x7A, 0xA7, 0xE6); // AccentColor
    private const double GuideLineThickness = 1.0;

    public SnapGuideOverlay()
    {
        InitializeComponent();

        // Span the entire virtual screen
        Left = SystemParameters.VirtualScreenLeft;
        Top = SystemParameters.VirtualScreenTop;
        Width = SystemParameters.VirtualScreenWidth;
        Height = SystemParameters.VirtualScreenHeight;
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        var helper = new WindowInteropHelper(this);
        var hwnd = helper.Handle;

        // Make the window click-through: WS_EX_TRANSPARENT passes mouse events through
        var exStyle = NativeMethods.GetWindowLongPtr(hwnd, NativeMethods.GWL_EXSTYLE);
        exStyle = new IntPtr(exStyle.ToInt64()
            | NativeMethods.WS_EX_TOOLWINDOW
            | NativeMethods.WS_EX_NOACTIVATE
            | NativeMethods.WS_EX_TRANSPARENT);
        NativeMethods.SetWindowLongPtr(hwnd, NativeMethods.GWL_EXSTYLE, exStyle);
    }

    /// <summary>
    /// Display snap guide lines at the specified positions.
    /// Lines are drawn relative to the virtual screen origin.
    /// </summary>
    public void ShowLines(IReadOnlyList<SnapEngine.SnapLine> lines)
    {
        GuideCanvas.Children.Clear();

        if (lines.Count == 0)
        {
            Visibility = Visibility.Hidden;
            return;
        }

        var brush = new SolidColorBrush(GuideColor);
        var dashStyle = new DashStyle(new double[] { 4, 3 }, 0);

        foreach (var line in lines)
        {
            var guideLine = new Line
            {
                Stroke = brush,
                StrokeThickness = GuideLineThickness,
                StrokeDashArray = dashStyle.Dashes,
                StrokeStartLineCap = PenLineCap.Flat,
                StrokeEndLineCap = PenLineCap.Flat,
            };

            // SnapLine.Position is in absolute screen coordinates, but the Canvas
            // sits inside a window positioned at (VirtualScreenLeft, VirtualScreenTop),
            // so subtract the offset to get window-local coordinates.
            if (line.IsHorizontal)
            {
                guideLine.X1 = 0;
                guideLine.X2 = SystemParameters.VirtualScreenWidth;
                guideLine.Y1 = line.Position - SystemParameters.VirtualScreenTop;
                guideLine.Y2 = line.Position - SystemParameters.VirtualScreenTop;
            }
            else
            {
                guideLine.X1 = line.Position - SystemParameters.VirtualScreenLeft;
                guideLine.X2 = line.Position - SystemParameters.VirtualScreenLeft;
                guideLine.Y1 = 0;
                guideLine.Y2 = SystemParameters.VirtualScreenHeight;
            }

            GuideCanvas.Children.Add(guideLine);
        }

        Visibility = Visibility.Visible;
    }

    /// <summary>
    /// Hide all snap guide lines.
    /// </summary>
    public new void Hide()
    {
        GuideCanvas.Children.Clear();
        Visibility = Visibility.Hidden;
    }
}
