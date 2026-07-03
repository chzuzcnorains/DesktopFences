using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using DesktopFences.Shell.Desktop;
using DesktopFences.Shell.Interop;

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
public partial class DesktopIconOverlay : Window, ISelectionContainer
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

    // Marquee / multi-selection state
    private readonly HashSet<string> _selectedPaths = new(StringComparer.OrdinalIgnoreCase);
    private System.Windows.Shapes.Rectangle? _marqueeRect; // the rubber-band visual (lazily created)
    private bool _marqueeActive;           // true between first MarqueeUpdated and MarqueeCompleted
    private HashSet<string> _marqueeBaseline = new(StringComparer.OrdinalIgnoreCase); // selection at drag start (for additive)

    /// <summary>True if any icon is currently selected (gates Delete/Escape in the marquee manager).</summary>
    public bool HasSelection => _selectedPaths.Count > 0;

    /// <summary>Enforces a single mutually-exclusive selection across all fences + this overlay.</summary>
    public DesktopSelectionCoordinator? SelectionCoordinator { get; set; }

    /// <summary>
    /// Predicate (injected by App) telling whether a path is a real desktop file. Used by the
    /// drop handler to ignore non-desktop sources (e.g. portal-fence files mapped from an
    /// external folder) — the overlay only ever displays unfenced *desktop* files.
    /// </summary>
    public Func<string, bool>? IsDesktopFile { get; set; }

    // 图标尺寸跟随桌面右键「查看」菜单（大 96 / 中 48 / 小 32，shell 逻辑尺寸即 DIP）。
    // 其余度量全部由 _iconSize 推导，公式在 48 时精确还原旧常量（90/86/54/36）。
    private double _iconSize = 48;

    // 网格槽位尺寸（图标间距，与 Windows 原生一致）
    private double GridCellWidth => CellWidth + 4;
    private double GridCellHeight => CellHeight;
    private const double GridMarginLeft = 10;
    private const double GridMarginTop = 10;

    // Cell 内部布局：icon 区（IconRowHeight）+ 文字区（TextRowHeight）两个固定槽位，
    // 让 icon / 文字各自占据固定槽位，保证不同 cell 的 icon、文字水平/垂直中心一致
    private double CellWidth => _iconSize + 38;
    private double CellHeight => IconRowHeight + TextRowHeight;
    private double IconRowHeight => _iconSize + 6;
    private const double TextRowHeight = 36;

    // 提取像素 = 显示 DIP × 实际 DPI（量化到 16 的倍数）。旧写法固定 2×DIP 在
    // 100% 缩放下是 4 倍像素浪费（48 DIP 提 96px 位图），桌面图标上百时是内存大头。
    // 位图 ≥ 显示像素（QuantizeRequestSize 向上取整），WPF 只降采样保证清晰。
    private int IconPixelSize => ShellIconExtractor.QuantizeRequestSize(_iconSize, _dpiScaleX);

    // AllowsTransparency=True 的层叠窗口下，Windows OS 用每像素 alpha 决定 click 走向：
    // alpha=0 的像素直接被判为 click-through，根本不会送到 WPF 的命中测试。
    // Brushes.Transparent 的 alpha 就是 0，所以"透明背景但可点"的 WPF 经典写法
    // 在 AllowsTransparency 窗口里失效。用 alpha=1 的画刷（视觉不可感知）做兜底。
    private static readonly SolidColorBrush ClickableTransparentBrush =
        new(Color.FromArgb(1, 0, 0, 0));

    // Highlight for a selected icon cell (same accent used by the old single-click feedback).
    private static readonly SolidColorBrush SelectedBrush =
        new(Color.FromArgb(0x44, 0x66, 0x88, 0xCC));

    // 所有图标标签共用一份冻结阴影：桌面图标上百时，每标签独立 new 一个
    // DropShadowEffect 实例是纯浪费（参数完全相同，冻结后可安全共享）。
    private static readonly DropShadowEffect LabelShadow = new()
    {
        BlurRadius = 3,
        ShadowDepth = 1,
        Opacity = 0.8,
        Color = Colors.Black
    };

    static DesktopIconOverlay()
    {
        ClickableTransparentBrush.Freeze();
        SelectedBrush.Freeze();
        LabelShadow.Freeze();
    }

    /// <summary>Fired when a file is dragged off the overlay into a fence.</summary>
    public event Action<string>? FileDraggedToFence;

    /// <summary>Fired when a file is deleted from the overlay.</summary>
    public event Action<string>? FileDeleted;

    public DesktopIconOverlay(DesktopEmbedManager embedManager, ShellIconExtractor iconExtractor)
    {
        InitializeComponent();
        _embedManager = embedManager;
        _iconExtractor = iconExtractor;

        // IconPixelSize 依赖 DPI，而 SetIcons 在 Show()/Loaded 之前就会被调用 →
        // 构造期先用 GetDpi 取值（HWND 未建时返回主屏 DPI，overlay 本就定位主屏工作区）。
        // OnLoaded 再用 CompositionTarget 实测值校准，不同则重取图标。
        var dpi = VisualTreeHelper.GetDpi(this);
        _dpiScaleX = dpi.DpiScaleX;
        _dpiScaleY = dpi.DpiScaleY;

        // Size to primary monitor work area
        var workArea = SystemParameters.WorkArea;
        Left = workArea.Left;
        Top = workArea.Top;
        Width = workArea.Width;
        Height = workArea.Height;

        Loaded += OnLoaded;
        Closed += OnClosed;

        // Drop target for in-app drags (a file dragged out of a fence onto the desktop).
        // Only fires while AllowDrop is true — toggled on per drag via Begin/EndDropTargetMode.
        DragOver += OnOverlayDragOver;
        Drop += OnOverlayDrop;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        // Get DPI scale for converting physical pixels to WPF DIPs
        var source = PresentationSource.FromVisual(this);
        if (source?.CompositionTarget != null)
        {
            int oldPixelSize = IconPixelSize;
            _dpiScaleX = source.CompositionTarget.TransformToDevice.M11;
            _dpiScaleY = source.CompositionTarget.TransformToDevice.M22;

            // 构造期 GetDpi 与实测不一致（罕见：加载中途 DPI 变化）→ 按新桶重取图标
            if (IconPixelSize != oldPixelSize)
                foreach (var (path, border) in _iconElements)
                    ApplyMetricsToElement(border, path);
        }

        // Register with embed manager for z-order (same as FenceHost).
        // isOverlay: true → excluded from the WindowFromPoint occlusion self-heal probe
        // (transparent/click-through center would falsely read as sunk — bug 19/35).
        var hwnd = new WindowInteropHelper(this).Handle;
        _embedManager.RegisterWindow(hwnd, isOverlay: true);

        SelectionCoordinator?.Register(this);

        // 启动 overlay 不显示自愈(双机制保险)：
        //   机制1 z-order：overlay 启动瞬间可能被 DWM 压到壁纸下，且因 alpha=0 透传被排除出
        //     WindowFromPoint 自愈(bug 19/35)，fence 正常时常规兜底永远捞不回它 → 主动用
        //     HWND_TOPMOST「借用 topmost」拉回(EnsureOverlayVisible，不改 _isTopmost)。
        //   机制2 渲染：AllowsTransparency + Background=Transparent(alpha=0) + WS_EX_NOACTIVATE
        //     的全屏层叠窗口，初次 Show() 的 per-pixel alpha 合成可能不绘制，直到一次前台变化触发
        //     DWM 重组 → InvalidateVisual + UpdateLayout 强制重组。
        // 两拍：100ms 覆盖常见，500ms 覆盖开机自启等启动繁忙/合成迟的场景。一次性，跑完即停。
        ScheduleOverlaySelfHeal(hwnd, 100);
        ScheduleOverlaySelfHeal(hwnd, 500);
    }

    /// <summary>
    /// 启动后延迟 <paramref name="delayMs"/> 毫秒做一次性 overlay 可见性自愈：
    /// 借用 topmost 把窗口拉到壁纸上方(治 z-order 下沉) + 强制重绘(治层叠透明初次合成不绘制)。
    /// </summary>
    private void ScheduleOverlaySelfHeal(IntPtr hwnd, int delayMs)
    {
        var timer = new System.Windows.Threading.DispatcherTimer(
            System.Windows.Threading.DispatcherPriority.Loaded, Dispatcher)
        {
            Interval = TimeSpan.FromMilliseconds(delayMs)
        };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            _embedManager.EnsureOverlayVisible(hwnd);  // 机制1：借用 topmost 拉回，不改 _isTopmost
            InvalidateVisual();                         // 机制2：强制 per-pixel alpha 重组
            IconCanvas.InvalidateVisual();
            UpdateLayout();
        };
        timer.Start();
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        SelectionCoordinator?.Unregister(this);
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
        _selectedPaths.Clear();
        _marqueeRect = null; // was a child of IconCanvas just cleared; recreate on next marquee

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
            _selectedPaths.Remove(filePath);
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

    // ──────────────── 原生「查看 / 排序方式 / 刷新」联动（DesktopViewMonitor 驱动） ────────────────

    /// <summary>
    /// 应用桌面「查看」菜单的图标尺寸（大 96 / 中 48 / 小 32）。
    /// 现有图标原地重建视觉（重取对应分辨率位图、改行高/cell 尺寸），
    /// 并按原网格槽位映射到新度量下（槽位冲突/越界时补位到下一空槽）。
    /// </summary>
    public void SetIconSize(double iconSize)
    {
        if (iconSize < 16 || Math.Abs(iconSize - _iconSize) < 0.5)
            return;

        // 换度量前先记住每个图标的旧槽位（列优先序，保持视觉顺序稳定）
        double oldCellW = GridCellWidth, oldCellH = GridCellHeight;
        var slots = _iconElements
            .Select(kvp => (
                Path: kvp.Key,
                Border: kvp.Value,
                Col: Math.Max(0, (int)Math.Round((Canvas.GetLeft(kvp.Value) - GridMarginLeft) / oldCellW)),
                Row: Math.Max(0, (int)Math.Round((Canvas.GetTop(kvp.Value) - GridMarginTop) / oldCellH))))
            .OrderBy(t => t.Col).ThenBy(t => t.Row)
            .ToList();

        _iconSize = iconSize;

        int maxCols = Math.Max(1, (int)((Width - GridMarginLeft) / GridCellWidth));
        int maxRows = Math.Max(1, (int)((Height - GridMarginTop) / GridCellHeight));
        var used = new HashSet<(int Col, int Row)>();

        foreach (var (path, border, col, row) in slots)
        {
            ApplyMetricsToElement(border, path);

            // 原槽位优先；越界钳制、冲突（放大后行列数变少）则补位到下一空槽
            var slot = (Col: Math.Min(col, maxCols - 1), Row: Math.Min(row, maxRows - 1));
            if (!used.Add(slot))
            {
                slot = FindNextFreeSlot(used, maxCols, maxRows);
                used.Add(slot);
            }
            Canvas.SetLeft(border, GridMarginLeft + slot.Col * GridCellWidth);
            Canvas.SetTop(border, GridMarginTop + slot.Row * GridCellHeight);
        }
    }

    /// <summary>
    /// 应用桌面「排序方式」菜单：按键值排序后从 (0,0) 起列优先紧凑重排。
    /// Explorer 语义：文件夹分组在前，名称用 StrCmpLogicalW 自然排序；
    /// 降序时整体取反（分组也翻转，与 Explorer 一致）。
    /// </summary>
    public void SortIcons(DesktopSortKey key, bool ascending)
    {
        if (key == DesktopSortKey.Unknown || _iconElements.Count == 0)
            return;

        // 预计算排序键，避免比较回调里反复走文件系统/Shell
        var entries = _iconElements.Keys
            .Select(path =>
            {
                bool isDir = Directory.Exists(path);
                return (
                    Path: path,
                    IsDir: isDir,
                    Display: GetDisplayName(path),
                    Size: key == DesktopSortKey.Size && !isDir ? GetFileSizeSafe(path) : 0L,
                    Date: key == DesktopSortKey.DateModified ? GetLastWriteSafe(path) : default,
                    TypeName: key == DesktopSortKey.ItemType
                        ? ShellFileOperations.GetFileTypeName(path)
                        : string.Empty);
            })
            .ToList();

        int sign = ascending ? 1 : -1;
        entries.Sort((a, b) =>
        {
            if (a.IsDir != b.IsDir)
                return sign * (a.IsDir ? -1 : 1);

            int c = key switch
            {
                DesktopSortKey.Size => a.Size.CompareTo(b.Size),
                DesktopSortKey.DateModified => a.Date.CompareTo(b.Date),
                DesktopSortKey.ItemType => string.Compare(a.TypeName, b.TypeName,
                    StringComparison.CurrentCultureIgnoreCase),
                _ => 0,
            };
            if (c == 0) // 名称主键 / 其余键并列决胜
                c = NativeMethods.StrCmpLogicalW(a.Display, b.Display);
            return sign * c;
        });

        ReflowSequential(entries.Select(e => e.Path));
    }

    /// <summary>
    /// 应用桌面「刷新」：清除已删文件、失效图标缓存并重取位图（图标资源可能已变）。
    /// 新增文件由 App 侧同步触发的 DesktopFileMonitor.RescanNow 补进来。
    /// </summary>
    public void RefreshIcons()
    {
        foreach (var (path, border) in _iconElements.ToList())
        {
            if (!File.Exists(path) && !Directory.Exists(path))
            {
                RemoveIcon(path);
                FileDeleted?.Invoke(path);
                continue;
            }
            _iconExtractor.Invalidate(path);
            ApplyMetricsToElement(border, path);
        }
    }

    /// <summary>按当前度量重建一个图标元素的视觉（cell/行高/位图尺寸与分辨率）。</summary>
    private void ApplyMetricsToElement(Border border, string filePath)
    {
        border.Width = CellWidth;
        border.Height = CellHeight;
        if (border.Child is not Grid grid || grid.RowDefinitions.Count < 2)
            return;

        grid.RowDefinitions[0].Height = new GridLength(IconRowHeight);
        grid.RowDefinitions[1].Height = new GridLength(TextRowHeight);
        foreach (var child in grid.Children)
        {
            if (child is Image image)
            {
                image.Width = _iconSize;
                image.Height = _iconSize;
                var icon = _iconExtractor.GetIcon(filePath, IconPixelSize);
                if (icon is not null)
                    image.Source = icon;
            }
        }
    }

    /// <summary>把给定顺序的图标从 (0,0) 起列优先（自上而下、自左而右）紧凑排入网格。</summary>
    private void ReflowSequential(IEnumerable<string> orderedPaths)
    {
        int maxRows = Math.Max(1, (int)((Height - GridMarginTop) / GridCellHeight));
        int col = 0, row = 0;
        foreach (var path in orderedPaths)
        {
            if (!_iconElements.TryGetValue(path, out var border))
                continue;
            Canvas.SetLeft(border, GridMarginLeft + col * GridCellWidth);
            Canvas.SetTop(border, GridMarginTop + row * GridCellHeight);
            row++;
            if (row >= maxRows)
            {
                row = 0;
                col++;
            }
        }
    }

    private static (int Col, int Row) FindNextFreeSlot(HashSet<(int Col, int Row)> used, int maxCols, int maxRows)
    {
        for (int col = 0; col < maxCols; col++)
            for (int row = 0; row < maxRows; row++)
                if (!used.Contains((col, row)))
                    return (col, row);
        return (0, 0); // 网格全满（不应发生）：退化为原点堆叠
    }

    private static long GetFileSizeSafe(string path)
    {
        try { return new FileInfo(path).Length; }
        catch { return 0L; }
    }

    private static DateTime GetLastWriteSafe(string path)
    {
        try { return File.GetLastWriteTime(path); } // 对目录同样有效
        catch { return DateTime.MinValue; }
    }

    // ───────────────────────── In-app drop target (fence → overlay) ─────────────────────────
    // The overlay is normally NOT an OLE drop target: its empty area is alpha=0 (click-through),
    // so a file dragged out of a fence would fall through to the real desktop (SysListView32),
    // which treats it as a same-folder move and raises "源文件名和目标文件名相同" (bug 39).
    // While an in-app drag is in flight we flip the whole window to alpha=1 + AllowDrop so the
    // overlay itself catches the drop and handles it logically (AddIcon, no physical move),
    // mirroring the fence-to-fence convention. Reverted as soon as the drag ends.

    /// <summary>Enter drop-target mode for the duration of an in-app drag: make the whole
    /// window hit-testable (alpha=1) and accept drops, so a fence→overlay drop lands here
    /// instead of passing through to the native desktop.</summary>
    public void BeginDropTargetMode()
    {
        AllowDrop = true;
        // alpha=1 (imperceptible) background → the layered window becomes fully hit-testable.
        // Set on the Window: the Canvas Background={x:Null} doesn't hit-test, so empty-area
        // hits fall to the window's own background.
        Background = ClickableTransparentBrush;
        // Nudge the layered window to recomposite with the new per-pixel alpha right away.
        InvalidateVisual();
        UpdateLayout();
    }

    /// <summary>Leave drop-target mode: restore the alpha=0 click-through background so the
    /// native desktop right-click menu / double-click quick-hide / marquee keep working.</summary>
    public void EndDropTargetMode()
    {
        AllowDrop = false;
        Background = System.Windows.Media.Brushes.Transparent; // alpha=0 → click-through
        InvalidateVisual();
    }

    private void OnOverlayDragOver(object sender, DragEventArgs e)
    {
        // Only in-app drags are accepted as a move; anything else (shouldn't reach here while
        // AllowDrop is only on during in-app drags) gets no effect.
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop)
                    && e.Data.GetDataPresent(InternalDragFormats.Marker)
            ? DragDropEffects.Move
            : DragDropEffects.None;
        e.Handled = true;
    }

    private void OnOverlayDrop(object sender, DragEventArgs e)
    {
        e.Handled = true; // consume even on no-op so it never falls through to the desktop

        if (!e.Data.GetDataPresent(InternalDragFormats.Marker)) { e.Effects = DragDropEffects.None; return; }
        if (e.Data.GetData(DataFormats.FileDrop) is not string[] files) { e.Effects = DragDropEffects.None; return; }

        bool anyAdded = false;
        foreach (var filePath in files)
        {
            if (ContainsIcon(filePath)) continue;            // overlay self-drop / duplicate
            if (IsDesktopFile is not null && !IsDesktopFile(filePath)) continue; // skip portal/external
            AddIcon(filePath);
            anyAdded = true;
        }

        // Report Move only when something was newly added → the source fence removes its entry.
        // No-op (all duplicates / non-desktop) → None so the source keeps its entry (mirrors
        // FencePanel.OnDrop's anyAdded guard, see docs/bug/internal_drag_duplicate_entries.md).
        e.Effects = anyAdded ? DragDropEffects.Move : DragDropEffects.None;
    }

    /// <summary>与 Explorer 一致的显示名：默认隐藏 .lnk 扩展名（排序与标签共用）。</summary>
    private static string GetDisplayName(string filePath)
    {
        var name = Path.GetFileName(filePath);
        if (name.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase))
            return name.Substring(0, name.Length - 4);
        return name;
    }

    private Border CreateIconElement(string filePath)
    {
        var icon = _iconExtractor.GetIcon(filePath, IconPixelSize);
        var displayName = GetDisplayName(filePath);

        // 把 cell 拆成 icon 区（IconRowHeight）+ 文字区（TextRowHeight）两个固定槽位，
        // 让 icon 与文字的位置完全不受彼此尺寸影响，保证同行/同列水平/垂直中心一致。
        var image = new Image
        {
            Source = icon,
            Width = _iconSize,
            Height = _iconSize,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Stretch = Stretch.Uniform,
            SnapsToDevicePixels = true,
            UseLayoutRounding = true
        };
        RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.HighQuality);
        RenderOptions.SetClearTypeHint(image, ClearTypeHint.Enabled);
        Grid.SetRow(image, 0);

        var text = new TextBlock
        {
            Text = displayName,
            FontSize = 12,
            Foreground = Brushes.White,
            TextAlignment = TextAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
            TextWrapping = TextWrapping.Wrap,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(2, 2, 2, 0),
            SnapsToDevicePixels = true,
            UseLayoutRounding = true,
            Effect = LabelShadow
        };
        Grid.SetRow(text, 1);

        var grid = new Grid
        {
            SnapsToDevicePixels = true,
            UseLayoutRounding = true
        };
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(IconRowHeight) });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(TextRowHeight) });
        grid.Children.Add(image);
        grid.Children.Add(text);

        var border = new Border
        {
            Width = CellWidth,
            Height = CellHeight,
            CornerRadius = new CornerRadius(4),
            // 关键：必须用 alpha>=1 的画刷，Brushes.Transparent (alpha=0) 在 AllowsTransparency
            // 窗口里会被 OS 判为 click-through —— click 不会进入 WPF，整个 cell 的空白区域选不中
            Background = ClickableTransparentBrush,
            Child = grid,
            Tag = filePath, // store file path for event handlers
            ToolTip = filePath,
            SnapsToDevicePixels = true,
            UseLayoutRounding = true
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

        SelectionCoordinator?.NotifyActivated(this); // global single selection: clear fences

        // Selection logic (modifier-aware):
        //  - Ctrl/Shift: toggle this icon, keep the rest (incremental selection).
        //  - Already part of a multi-selection: keep it, so a following drag moves the group.
        //  - Plain click on an unselected icon: clear others and select just this one.
        bool additive = (Keyboard.Modifiers & (ModifierKeys.Control | ModifierKeys.Shift)) != 0;
        if (additive)
            SetSelected(filePath, !_selectedPaths.Contains(filePath));
        else if (!_selectedPaths.Contains(filePath))
        {
            ClearSelection();
            SetSelected(filePath, true);
        }
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

        // Group drag: when the pressed icon belongs to a multi-selection, drag the whole
        // group straight into a fence (skip the within-overlay reposition that single icons use).
        if (!_isMoving && _selectedPaths.Count > 1 && _selectedPaths.Contains(filePath))
        {
            _isDragging = true;
            var paths = _selectedPaths.ToArray();
            var groupData = new DataObject(DataFormats.FileDrop, paths);
            groupData.SetData(InternalDragFormats.Marker, true);
            // 不要在 overlay 自己发起的拖拽里开启放置目标模式：那会让 overlay 变成一个全屏
            // 竞争放置目标，把本该落到 fence 的 drop 抢过来——而这些 icon 已在 overlay 上
            // （ContainsIcon=true）→ OnOverlayDrop 回报 None → fence 收不到、图标不动（bug 39 回归）。
            // overlay 只在「fence 拖向 overlay」时才需放置目标，那条路径由 FencePanel 事件驱动。
            // try/finally：DoDragDrop 抛异常也必须复位 _isDragging，否则后续拖拽被静默吞掉。
            try
            {
                var groupResult = DragDrop.DoDragDrop(border, groupData, DragDropEffects.Copy | DragDropEffects.Move);
                if (groupResult == DragDropEffects.Move)
                {
                    foreach (var p in paths)
                    {
                        RemoveIcon(p);
                        FileDraggedToFence?.Invoke(p);
                    }
                    _selectedPaths.Clear();
                }
            }
            finally
            {
                _isDragging = false;
            }
            return;
        }

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

        // Switch from internal reposition to a cross-window OLE drag when the cursor is over
        // a fence (so a single unfenced icon can be dropped straight into a fence) OR near the
        // screen edge (legacy trigger). bug 39: without the over-fence check, a single icon
        // dragged onto a mid-screen fence only repositioned on the desktop and never entered
        // the fence ("单文件 overlay→fence 没反应"). Multi-select already goes straight to OLE.
        const double edgeThreshold = 20;
        var screenPt = PointToScreen(currentPos);
        bool overFence = _embedManager.IsPointOverFence(
            new NativeMethods.POINT { X = (int)screenPt.X, Y = (int)screenPt.Y });
        if (overFence ||
            currentPos.X < edgeThreshold || currentPos.X > Width - edgeThreshold ||
            currentPos.Y < edgeThreshold || currentPos.Y > Height - edgeThreshold)
        {
            // End internal move, start OLE drag
            EndInternalMove(border, cancel: true);
            _isDragging = true;

            var dataObject = new DataObject(DataFormats.FileDrop, new[] { filePath });
            // 内部拖拽标记：目标 fence 据此回报 Move，本侧才会移除 overlay 图标，
            // 否则拖入 fence 后图标残留（fence 与 overlay 同时显示该文件）。
            dataObject.SetData(InternalDragFormats.Marker, true);
            // 同上：overlay 自己发起的拖拽不开启放置目标模式（否则抢走本该落到 fence 的 drop）。
            // try/finally：DoDragDrop 抛异常也必须复位 _isDragging。
            try
            {
                var result = DragDrop.DoDragDrop(border, dataObject, DragDropEffects.Copy | DragDropEffects.Move);
                if (result == DragDropEffects.Move)
                {
                    RemoveIcon(filePath);
                    FileDraggedToFence?.Invoke(filePath);
                }
            }
            finally
            {
                _isDragging = false;
            }
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

        // Keep an existing multi-selection if right-clicking one of its icons; otherwise
        // select just this icon. The shell menu is built from the whole selection, so
        // delete/open/send-to operate on all selected files (Explorer semantics).
        if (!_selectedPaths.Contains(filePath))
        {
            SelectionCoordinator?.NotifyActivated(this); // global single selection: clear fences
            ClearSelection();
            SetSelected(filePath, true);
        }

        // 弹菜单前取快照；被右键的文件放首位（COM 构建失败时降级为该单文件菜单）
        string[] paths = _selectedPaths.Count > 1
            ? [filePath, .. _selectedPaths.Where(p => !string.Equals(p, filePath, StringComparison.OrdinalIgnoreCase))]
            : [filePath];

        // Show Shell context menu (same as FencePanel)
        var screenPoint = border.PointToScreen(e.GetPosition(border));
        var hwnd = new WindowInteropHelper(this).Handle;
        ShellContextMenu.Show(hwnd, paths, (int)screenPoint.X, (int)screenPoint.Y);

        // Remove any file the menu deleted right away (RemoveIcon also drops it from
        // _selectedPaths); the desktop monitor is only the eventual-consistency backstop.
        foreach (var path in paths)
        {
            if (!File.Exists(path) && !Directory.Exists(path))
            {
                RemoveIcon(path);
                FileDeleted?.Invoke(path);
            }
        }

        e.Handled = true;
    }

    void ISelectionContainer.ClearSelection() => ClearSelection();

    private void ClearSelection()
    {
        _selectedPaths.Clear();
        foreach (var element in _iconElements.Values)
        {
            // 必须用 ClickableTransparentBrush (alpha=1) 而不是 Brushes.Transparent (alpha=0)，
            // 否则取消选中后 cell 又变回 OS 层 click-through，blank 区域无法再被选中
            element.Background = ClickableTransparentBrush;
        }
    }

    /// <summary>Add/remove a file in the selection set and update its cell highlight.</summary>
    private void SetSelected(string filePath, bool selected)
    {
        if (selected) _selectedPaths.Add(filePath);
        else _selectedPaths.Remove(filePath);

        if (_iconElements.TryGetValue(filePath, out var border))
            border.Background = selected ? SelectedBrush : ClickableTransparentBrush;
    }

    // ───────────────────────── Rubber-band (marquee) selection ─────────────────────────
    // Driven by DesktopMarqueeManager's low-level mouse hook (screen coords). The overlay's
    // empty areas stay click-through (alpha=0) so the native desktop right-click menu and
    // double-click quick-hide keep working; the hook only *observes* the drag, and here we
    // draw the rectangle + hit-test our own icons. See docs/design/desktop-icon-overlay.md §12.

    /// <summary>Wire the overlay to a marquee manager. Events arrive on the UI thread
    /// (the hook is installed on it); marshalled defensively for safety.</summary>
    public void AttachMarquee(DesktopMarqueeManager marquee)
    {
        marquee.MarqueeUpdated += (rect, additive) => Dispatcher.Invoke(() => UpdateMarquee(rect, additive));
        marquee.MarqueeCompleted += (rect, additive) => Dispatcher.Invoke(() => CompleteMarquee(rect, additive));
        marquee.EmptyClicked += () => Dispatcher.Invoke(ClearSelectionAndHideMarquee);
        marquee.Cancelled += () => Dispatcher.Invoke(ClearSelectionAndHideMarquee);
        // Delete may show a recycle-bin confirmation dialog → never block the keyboard hook.
        marquee.DeleteRequested += () => Dispatcher.InvokeAsync(DeleteSelected);
    }

    private void UpdateMarquee(ScreenRect screenRect, bool additive)
    {
        if (!IsVisible) return; // overlay hidden (quick-hide) → nothing to select/draw
        var canvasRect = ScreenRectToCanvas(screenRect);

        EnsureMarqueeRect();
        Canvas.SetLeft(_marqueeRect, canvasRect.Left);
        Canvas.SetTop(_marqueeRect, canvasRect.Top);
        _marqueeRect!.Width = canvasRect.Width;
        _marqueeRect.Height = canvasRect.Height;
        _marqueeRect.Visibility = Visibility.Visible;

        if (!_marqueeActive)
        {
            // First update of this drag — snapshot the pre-drag selection for additive mode.
            _marqueeActive = true;
            _marqueeBaseline = new HashSet<string>(_selectedPaths, StringComparer.OrdinalIgnoreCase);
            SelectionCoordinator?.NotifyActivated(this); // global single selection: clear fences
        }

        ApplyMarqueeSelection(canvasRect, additive);
    }

    private void CompleteMarquee(ScreenRect screenRect, bool additive)
    {
        if (IsVisible)
            ApplyMarqueeSelection(ScreenRectToCanvas(screenRect), additive);
        _marqueeActive = false;
        HideMarquee();
    }

    /// <summary>Select every icon intersecting the rectangle (canvas DIP space); in additive
    /// mode keep the pre-drag selection as a baseline union.</summary>
    private void ApplyMarqueeSelection(Rect canvasRect, bool additive)
    {
        foreach (var kvp in _iconElements)
        {
            double left = Canvas.GetLeft(kvp.Value);
            double top = Canvas.GetTop(kvp.Value);
            if (double.IsNaN(left) || double.IsNaN(top)) continue;

            var iconRect = new Rect(left, top, kvp.Value.Width, kvp.Value.Height);
            bool inside = canvasRect.IntersectsWith(iconRect);
            bool selected = inside || (additive && _marqueeBaseline.Contains(kvp.Key));
            SetSelected(kvp.Key, selected);
        }
    }

    /// <summary>Convert a screen-pixel rect (from the hook) to canvas DIP coordinates.
    /// PointFromScreen takes physical pixels and applies the DPI transform + window offset.</summary>
    private Rect ScreenRectToCanvas(ScreenRect r)
    {
        var p1 = IconCanvas.PointFromScreen(new Point(r.Left, r.Top));
        var p2 = IconCanvas.PointFromScreen(new Point(r.Right, r.Bottom));
        return new Rect(p1, p2);
    }

    private void EnsureMarqueeRect()
    {
        if (_marqueeRect != null) return;
        _marqueeRect = new System.Windows.Shapes.Rectangle
        {
            Fill = new SolidColorBrush(Color.FromArgb(0x33, 0x33, 0xAA, 0xFF)),
            Stroke = new SolidColorBrush(Color.FromArgb(0xCC, 0x33, 0xAA, 0xFF)),
            StrokeThickness = 1,
            IsHitTestVisible = false,
            Visibility = Visibility.Collapsed
        };
        Panel.SetZIndex(_marqueeRect, 10000); // above all icons
        IconCanvas.Children.Add(_marqueeRect);
    }

    private void HideMarquee()
    {
        if (_marqueeRect != null) _marqueeRect.Visibility = Visibility.Collapsed;
    }

    private void ClearSelectionAndHideMarquee()
    {
        _marqueeActive = false;
        ClearSelection();
        HideMarquee();
    }

    /// <summary>Delete every selected file to the Recycle Bin and drop it from the overlay.</summary>
    private void DeleteSelected()
    {
        if (_selectedPaths.Count == 0) return;

        foreach (var path in _selectedPaths.ToArray())
        {
            if (ShellFileOperations.DeleteToRecycleBin(path))
            {
                RemoveIcon(path);
                FileDeleted?.Invoke(path);
            }
        }

        _selectedPaths.Clear();
        HideMarquee();
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
