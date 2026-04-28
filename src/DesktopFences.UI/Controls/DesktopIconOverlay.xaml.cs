using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Effects;
using DesktopFences.Shell.Desktop;

namespace DesktopFences.UI.Controls;

/// <summary>
/// Data for one icon to display on the overlay.
/// </summary>
public record OverlayIconItem(string FilePath, double X, double Y);

/// <summary>
/// Full-screen transparent overlay that renders unfenced desktop icons
/// at their native positions (read from SysListView32 before hiding).
///
/// - Canvas Background="{x:Null}" makes empty areas click-through
/// - Each icon element is hit-testable (double-click open, right-click menu, drag to fence)
/// - Registered with DesktopEmbedManager for z-order management
/// </summary>
public partial class DesktopIconOverlay : Window
{
    private readonly DesktopEmbedManager _embedManager;
    private readonly ShellIconExtractor _iconExtractor;
    private readonly Dictionary<string, Border> _iconElements = new(StringComparer.OrdinalIgnoreCase);

    // DPI scale factor (physical pixels → WPF DIPs)
    private double _dpiScaleX = 1.0;
    private double _dpiScaleY = 1.0;

    // Drag state
    private Point _dragStartPoint;
    private bool _isDragging;
    private bool _isMoving;       // internal move mode (within overlay)
    private Border? _movingIcon;  // the icon being moved
    private Point _moveOffset;    // offset from icon top-left to mouse

    // Auto-position grid settings (match Windows native desktop icon size/spacing)
    private const double GridCellWidth = 90;
    private const double GridCellHeight = 90;
    private const double GridMarginLeft = 10;
    private const double GridMarginTop = 10;

    /// <summary>Fired when a file is dragged off the overlay into a fence.</summary>
    public event Action<string>? FileDraggedToFence;

    /// <summary>Fired when a file is deleted from the overlay.</summary>
    public event Action<string>? FileDeleted;

    public DesktopIconOverlay(DesktopEmbedManager embedManager, ShellIconExtractor iconExtractor)
    {
        InitializeComponent();
        _embedManager = embedManager;
        _iconExtractor = iconExtractor;

        // Size to primary monitor work area
        var workArea = SystemParameters.WorkArea;
        Left = workArea.Left;
        Top = workArea.Top;
        Width = workArea.Width;
        Height = workArea.Height;

        Loaded += OnLoaded;
        Closed += OnClosed;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        // Get DPI scale for converting physical pixels to WPF DIPs
        var source = PresentationSource.FromVisual(this);
        if (source?.CompositionTarget != null)
        {
            _dpiScaleX = source.CompositionTarget.TransformToDevice.M11;
            _dpiScaleY = source.CompositionTarget.TransformToDevice.M22;
        }

        // Register with embed manager for z-order (same as FenceHost)
        var hwnd = new WindowInteropHelper(this).Handle;
        _embedManager.RegisterWindow(hwnd);
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        _embedManager.UnregisterWindow(hwnd);
    }

    /// <summary>
    /// Set the icons to display. Coordinates are in physical pixels
    /// (from SysListView32) and will be converted to WPF DIPs.
    /// </summary>
    public void SetIcons(IReadOnlyList<OverlayIconItem> items)
    {
        IconCanvas.Children.Clear();
        _iconElements.Clear();

        foreach (var item in items)
        {
            var element = CreateIconElement(item.FilePath);

            double x = item.X, y = item.Y;
            if (x < 0 || y < 0)
            {
                // Auto-position: find next available grid slot
                var pos = FindNextGridPosition();
                x = pos.X;
                y = pos.Y;
            }
            else
            {
                // Convert physical pixels to WPF DIPs
                x /= _dpiScaleX;
                y /= _dpiScaleY;
            }

            Canvas.SetLeft(element, x);
            Canvas.SetTop(element, y);
            IconCanvas.Children.Add(element);
            _iconElements[item.FilePath] = element;
        }
    }

    /// <summary>
    /// Remove a specific file from the overlay.
    /// </summary>
    public void RemoveIcon(string filePath)
    {
        if (_iconElements.TryGetValue(filePath, out var element))
        {
            IconCanvas.Children.Remove(element);
            _iconElements.Remove(filePath);
        }
    }

    /// <summary>
    /// Add a new file to the overlay at an auto-calculated position.
    /// </summary>
    public void AddIcon(string filePath)
    {
        if (_iconElements.ContainsKey(filePath))
            return;

        var element = CreateIconElement(filePath);
        var pos = FindNextGridPosition();
        Canvas.SetLeft(element, pos.X);
        Canvas.SetTop(element, pos.Y);
        IconCanvas.Children.Add(element);
        _iconElements[filePath] = element;
    }

    /// <summary>
    /// Check if a file is currently displayed on the overlay.
    /// </summary>
    public bool ContainsIcon(string filePath)
    {
        return _iconElements.ContainsKey(filePath);
    }

    private Border CreateIconElement(string filePath)
    {
        var icon = _iconExtractor.GetIcon(filePath);
        var displayName = Path.GetFileName(filePath);

        // Hide .lnk extension by default
        if (displayName.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase))
        {
            displayName = displayName.Substring(0, displayName.Length - 4);
        }

        var image = new Image
        {
            Source = icon,
            Width = 48,
            Height = 48,
            HorizontalAlignment = HorizontalAlignment.Center
        };
        RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.HighQuality);

        var text = new TextBlock
        {
            Text = displayName,
            FontSize = 12,
            Foreground = Brushes.White,
            TextAlignment = TextAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxHeight = 36,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 4, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Center,
            Effect = new DropShadowEffect
            {
                BlurRadius = 3,
                ShadowDepth = 1,
                Opacity = 0.8,
                Color = Colors.Black
            }
        };

        var stack = new StackPanel
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(4)
        };
        stack.Children.Add(image);
        stack.Children.Add(text);

        var border = new Border
        {
            Width = 86,
            Height = 90,
            CornerRadius = new CornerRadius(4),
            Background = Brushes.Transparent,
            Child = stack,
            Tag = filePath, // store file path for event handlers
            ToolTip = filePath
        };

        border.MouseLeftButtonDown += OnIconMouseDown;
        border.MouseMove += OnIconMouseMove;
        border.MouseLeftButtonUp += OnIconMouseUp;
        border.MouseRightButtonUp += OnIconRightClick;

        return border;
    }

    private void OnIconMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not Border border || border.Tag is not string filePath)
            return;

        // Double-click → open file
        if (e.ClickCount == 2)
        {
            ShellFileOperations.OpenFile(filePath);
            e.Handled = true;
            return;
        }

        // Single click → record start point for drag
        _dragStartPoint = e.GetPosition(this);
        _isDragging = false;
        _isMoving = false;

        // Visual selection feedback
        ClearSelection();
        border.Background = new SolidColorBrush(Color.FromArgb(0x44, 0x66, 0x88, 0xCC));
    }

    private void OnIconMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed)
            return;
        if (sender is not Border border || border.Tag is not string filePath)
            return;

        var currentPos = e.GetPosition(this);
        var diff = currentPos - _dragStartPoint;

        if (!_isMoving && !_isDragging &&
            Math.Abs(diff.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(diff.Y) < SystemParameters.MinimumVerticalDragDistance)
            return;

        // Already in OLE drag mode — skip
        if (_isDragging) return;

        // Enter internal move mode
        if (!_isMoving)
        {
            _isMoving = true;
            _movingIcon = border;
            // Offset from icon top-left corner to mouse position
            _moveOffset = new Point(
                currentPos.X - Canvas.GetLeft(border),
                currentPos.Y - Canvas.GetTop(border));
            border.Opacity = 0.7;
            Panel.SetZIndex(border, 999);
            border.CaptureMouse();
            return;
        }

        // If cursor is near the overlay edge, switch to OLE drag for fence drop
        const double edgeThreshold = 20;
        if (currentPos.X < edgeThreshold || currentPos.X > Width - edgeThreshold ||
            currentPos.Y < edgeThreshold || currentPos.Y > Height - edgeThreshold)
        {
            // End internal move, start OLE drag
            EndInternalMove(border, cancel: true);
            _isDragging = true;

            var dataObject = new DataObject(DataFormats.FileDrop, new[] { filePath });
            var result = DragDrop.DoDragDrop(border, dataObject, DragDropEffects.Copy | DragDropEffects.Move);

            if (result == DragDropEffects.Move)
            {
                RemoveIcon(filePath);
                FileDraggedToFence?.Invoke(filePath);
            }

            _isDragging = false;
            return;
        }

        // Move the icon with cursor
        Canvas.SetLeft(border, currentPos.X - _moveOffset.X);
        Canvas.SetTop(border, currentPos.Y - _moveOffset.Y);
    }

    private void OnIconMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is not Border border)
            return;

        if (_isMoving && _movingIcon == border)
        {
            EndInternalMove(border, cancel: false);
        }
    }

    /// <summary>
    /// End internal icon move. If cancel=false, snap to nearest grid slot.
    /// If cancel=true, restore original position (before OLE drag takes over).
    /// </summary>
    private void EndInternalMove(Border border, bool cancel)
    {
        if (!_isMoving) return;
        _isMoving = false;
        _movingIcon = null;
        border.ReleaseMouseCapture();
        border.Opacity = 1.0;
        Panel.SetZIndex(border, 0);

        if (cancel)
            return;

        // Snap to nearest grid cell
        double rawX = Canvas.GetLeft(border);
        double rawY = Canvas.GetTop(border);

        int col = Math.Max(0, (int)Math.Round((rawX - GridMarginLeft) / GridCellWidth));
        int row = Math.Max(0, (int)Math.Round((rawY - GridMarginTop) / GridCellHeight));

        int maxCols = Math.Max(1, (int)((Width - GridMarginLeft) / GridCellWidth));
        int maxRows = Math.Max(1, (int)((Height - GridMarginTop) / GridCellHeight));
        col = Math.Min(col, maxCols - 1);
        row = Math.Min(row, maxRows - 1);

        double snapX = GridMarginLeft + col * GridCellWidth;
        double snapY = GridMarginTop + row * GridCellHeight;

        // If another icon is already at this grid slot, swap positions
        string? filePath = border.Tag as string;
        foreach (var kvp in _iconElements)
        {
            if (kvp.Value == border) continue;
            double otherX = Canvas.GetLeft(kvp.Value);
            double otherY = Canvas.GetTop(kvp.Value);
            int otherCol = (int)Math.Round((otherX - GridMarginLeft) / GridCellWidth);
            int otherRow = (int)Math.Round((otherY - GridMarginTop) / GridCellHeight);
            if (otherCol == col && otherRow == row)
            {
                // Swap: move the other icon to the drag source's original grid slot
                int srcCol = (int)Math.Round((_dragStartPoint.X - _moveOffset.X - GridMarginLeft) / GridCellWidth);
                int srcRow = (int)Math.Round((_dragStartPoint.Y - _moveOffset.Y - GridMarginTop) / GridCellHeight);
                Canvas.SetLeft(kvp.Value, GridMarginLeft + srcCol * GridCellWidth);
                Canvas.SetTop(kvp.Value, GridMarginTop + srcRow * GridCellHeight);
                break;
            }
        }

        Canvas.SetLeft(border, snapX);
        Canvas.SetTop(border, snapY);
    }

    private void OnIconRightClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is not Border border || border.Tag is not string filePath)
            return;

        // Visual selection
        ClearSelection();
        border.Background = new SolidColorBrush(Color.FromArgb(0x44, 0x66, 0x88, 0xCC));

        // Show Shell context menu (same as FencePanel)
        var screenPoint = border.PointToScreen(e.GetPosition(border));
        var hwnd = new WindowInteropHelper(this).Handle;
        ShellContextMenu.Show(hwnd, filePath, (int)screenPoint.X, (int)screenPoint.Y);

        // Check if file was deleted via context menu
        if (!File.Exists(filePath) && !Directory.Exists(filePath))
        {
            RemoveIcon(filePath);
            FileDeleted?.Invoke(filePath);
        }

        e.Handled = true;
    }

    private void ClearSelection()
    {
        foreach (var element in _iconElements.Values)
        {
            element.Background = Brushes.Transparent;
        }
    }

    /// <summary>
    /// Find the next available grid slot (column-major, top to bottom, left to right).
    /// </summary>
    private Point FindNextGridPosition()
    {
        var usedPositions = new HashSet<(int col, int row)>();

        foreach (var element in _iconElements.Values)
        {
            double left = Canvas.GetLeft(element);
            double top = Canvas.GetTop(element);
            if (double.IsNaN(left) || double.IsNaN(top)) continue;

            int col = (int)((left - GridMarginLeft) / GridCellWidth);
            int row = (int)((top - GridMarginTop) / GridCellHeight);
            usedPositions.Add((col, row));
        }

        int maxRows = Math.Max(1, (int)((Height - GridMarginTop) / GridCellHeight));
        int maxCols = Math.Max(1, (int)((Width - GridMarginLeft) / GridCellWidth));

        // Scan column by column, top to bottom (Windows default icon arrangement)
        for (int col = 0; col < maxCols; col++)
        {
            for (int row = 0; row < maxRows; row++)
            {
                if (!usedPositions.Contains((col, row)))
                {
                    return new Point(
                        GridMarginLeft + col * GridCellWidth,
                        GridMarginTop + row * GridCellHeight);
                }
            }
        }

        // Fallback: place at origin
        return new Point(GridMarginLeft, GridMarginTop);
    }
}
