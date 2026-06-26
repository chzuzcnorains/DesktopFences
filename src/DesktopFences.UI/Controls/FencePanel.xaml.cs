using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using DesktopFences.Core.Models;
using DesktopFences.Core.Services;
using DesktopFences.Shell.Desktop;
using DesktopFences.Shell.Interop;
using DesktopFences.UI.ViewModels;

namespace DesktopFences.UI.Controls;

public partial class FencePanel : UserControl, ISelectionContainer
{
    public static readonly DependencyProperty InnerContentProperty =
        DependencyProperty.Register(nameof(InnerContent), typeof(object), typeof(FencePanel));

    public object? InnerContent
    {
        get => GetValue(InnerContentProperty);
        set => SetValue(InnerContentProperty, value);
    }

    public static readonly DependencyProperty ShowTitleBarProperty =
        DependencyProperty.Register(nameof(ShowTitleBar), typeof(bool), typeof(FencePanel),
            new PropertyMetadata(true, OnShowTitleBarChanged));

    public bool ShowTitleBar
    {
        get => (bool)GetValue(ShowTitleBarProperty);
        set => SetValue(ShowTitleBarProperty, value);
    }

    // Collapse the title-bar row when in tab mode so the fence body sits
    // flush against the tab strip — otherwise the empty 30px row above
    // the file list renders a visible seam at the tab/body boundary.
    private static void OnShowTitleBarChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is FencePanel panel && panel.TitleBarRow is not null)
        {
            panel.TitleBarRow.Height = (bool)e.NewValue
                ? new GridLength(30)
                : new GridLength(0);
        }
    }

    public event Action? InteractionStarted;
    public event Action? InteractionEnded;
    public event Action? CloseRequested;

    /// <summary>Raised around an in-app file drag (DoDragDrop). App uses these to put the
    /// desktop overlay into drop-target mode so a file dragged out of a fence onto the
    /// desktop lands on the overlay instead of falling through to the native desktop
    /// (which would raise a same-folder "源文件名和目标文件名相同" error — bug 39).</summary>
    public event Action? InternalFileDragStarted;
    public event Action? InternalFileDragEnded;

    /// <summary>Allow FenceHost to raise InteractionStarted (e.g. tab strip drag).</summary>
    public void RaiseInteractionStarted() => InteractionStarted?.Invoke();
    /// <summary>Allow FenceHost to raise InteractionEnded (e.g. tab strip drag commit) so RequestAutoSave fires.</summary>
    public void RaiseInteractionEnded() => InteractionEnded?.Invoke();
    public ShellIconExtractor? IconExtractor { get; set; }

    // ── Snap support for resize ──────────────────────────────

    /// <summary>Injected delegate returning other fence rects for snap calculation.</summary>
    public Func<IReadOnlyList<SnapEngine.Rect>>? GetOtherFenceRects { get; set; }

    /// <summary>Snap threshold from AppSettings (0 = disabled).</summary>
    public double SnapThreshold { get; set; } = SnapEngine.DefaultThreshold;

    /// <summary>Shared snap guide overlay for displaying guide lines during resize.</summary>
    public SnapGuideOverlay? SnapOverlay { get; set; }

    /// <summary>
    /// Fired when rollup state changes. Host should sync window height.
    /// Arg: (isRolledUp, targetHeight)
    /// </summary>
    public event Action<bool, double>? RollupChanged;

    /// <summary>
    /// Fired when portal mode is set or cleared.
    /// Arg: (portalPath) — null means portal mode was cleared.
    /// </summary>
    public event Action<string?>? PortalModeChanged;

    /// <summary>
    /// Fired when user selects a tab from the title bar menu (Menu-only mode).
    /// Arg: tab index to activate.
    /// </summary>
    public event Action<int>? TabMenuSwitchRequested;

    private Point _dragStartPoint;
    private bool _isDraggingFile;
    private bool _isHoverExpanded;

    // Multi-selection + marquee state
    private bool _isMarqueeSelecting;
    private Point _marqueeOrigin;                       // in FileListBox coordinate space
    private List<FileItemViewModel> _marqueeBaseline = []; // selection snapshot for additive (Ctrl/Shift) marquee
    private FileItemViewModel? _selectionAnchor;        // anchor for Shift range selection

    /// <summary>Enforces a single mutually-exclusive selection across all fences + the desktop overlay.</summary>
    public DesktopSelectionCoordinator? SelectionCoordinator { get; set; }

    /// <summary>
    /// Set by FenceHost when in MenuOnly tab mode to provide tab titles.
    /// </summary>
    public IReadOnlyList<(string Title, int Index, bool IsActive)>? MenuOnlyTabs { get; set; }

    public FencePanel()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;

        // Marquee selection on empty list area (item Borders handle their own clicks).
        FileListBox.PreviewMouseLeftButtonDown += FileListBox_PreviewMouseLeftButtonDown;
        FileListBox.MouseMove += FileListBox_MouseMove;
        FileListBox.MouseLeftButtonUp += FileListBox_MouseLeftButtonUp;
    }

    private FencePanelViewModel? ViewModel => DataContext as FencePanelViewModel;

    private FencePanelViewModel? _subscribedVm;

    /// <summary>
    /// Returns the current fence body background brush (for tab strip color sync).
    /// </summary>
    public Brush? FenceBackground => FenceBorder.Background;

    // ── Theme ─────────────────────────────────────────────────

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (_subscribedVm is not null)
        {
            _subscribedVm.PropertyChanged -= OnViewModelPropertyChanged;
            _subscribedVm = null;
        }

        if (e.NewValue is FencePanelViewModel vm)
        {
            ApplyTheme(vm);
            vm.PropertyChanged += OnViewModelPropertyChanged;
            _subscribedVm = vm;
        }
    }

    /// <summary>
    /// Phase 13: when EffectiveIconStyle changes (per-fence override toggled,
    /// or any external code that flips IconStyleOverride), re-run the
    /// ItemTemplateSelector so each tile picks up the new template.
    /// </summary>
    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        // Icon style OR view mode change → re-run the template selector so each
        // tile picks up the right template (Recycling mode keeps stale ones).
        if (e.PropertyName is nameof(FencePanelViewModel.EffectiveIconStyle)
                           or nameof(FencePanelViewModel.ViewMode))
            RefreshFileTileTemplate();

        // Sort field / direction change → refresh the Detail header ▲/▼ glyphs.
        if (e.PropertyName is nameof(FencePanelViewModel.SortBy)
                           or nameof(FencePanelViewModel.SortDirection))
            UpdateSortGlyphs();
    }

    /// <summary>
    /// Apply per-fence color customization from ViewModel properties.
    /// </summary>
    public void ApplyTheme(FencePanelViewModel vm)
    {
        if (!string.IsNullOrEmpty(vm.BackgroundColor))
        {
            try { FenceBorder.Background = new BrushConverter().ConvertFromString(vm.BackgroundColor) as Brush; }
            catch { /* ignore invalid color */ }
        }

        if (!string.IsNullOrEmpty(vm.TitleBarColor))
        {
            try { TitleBarBorder.Background = new BrushConverter().ConvertFromString(vm.TitleBarColor) as Brush; }
            catch { /* ignore */ }
        }

        if (!string.IsNullOrEmpty(vm.TextColor))
        {
            try { TitleText.Foreground = new BrushConverter().ConvertFromString(vm.TextColor) as Brush; }
            catch { /* ignore */ }
        }
    }

    /// <summary>
    /// Force the file ListBox to re-run ItemTemplateSelector. Call after changing
    /// the global IconStyle Application resource so every file item switches
    /// templates in place. Items.Refresh() alone is insufficient — virtualized
    /// containers in Recycling mode keep their old ContentTemplate. Detaching
    /// and re-attaching the selector forces WPF to drop & rebuild item visuals.
    /// </summary>
    public void RefreshFileTileTemplate()
    {
        if (FileListBox is null) return;
        var selector = FileListBox.ItemTemplateSelector;
        FileListBox.ItemTemplateSelector = null;
        FileListBox.ItemTemplateSelector = selector;
    }

    /// <summary>
    /// Apply default colors from global settings (used when fence has no custom colors).
    /// </summary>
    public void ApplyDefaultTheme(string bgColor, string titleColor, string textColor, double fontSize)
    {
        if (ViewModel is not null)
        {
            // Only apply defaults if fence doesn't have custom colors
            if (string.IsNullOrEmpty(ViewModel.BackgroundColor))
            {
                try { FenceBorder.Background = new BrushConverter().ConvertFromString(bgColor) as Brush; }
                catch { }
            }
            if (string.IsNullOrEmpty(ViewModel.TitleBarColor))
            {
                try { TitleBarBorder.Background = new BrushConverter().ConvertFromString(titleColor) as Brush; }
                catch { }
            }
            if (string.IsNullOrEmpty(ViewModel.TextColor))
            {
                try { TitleText.Foreground = new BrushConverter().ConvertFromString(textColor) as Brush; }
                catch { }
            }
        }
        TitleText.FontSize = fontSize;
    }

    // ── Animations ────────────────────────────────────────────

    /// <summary>
    /// Play fade-in animation when fence is first shown.
    /// </summary>
    public void AnimateFadeIn()
    {
        var host = Window.GetWindow(this);
        if (host is null) return;

        host.Opacity = 0;
        var animation = new DoubleAnimation
        {
            From = 0,
            To = 1,
            Duration = TimeSpan.FromMilliseconds(250),
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
        };
        host.BeginAnimation(Window.OpacityProperty, animation);
    }

    /// <summary>
    /// Play fade-out animation, then invoke callback.
    /// </summary>
    public void AnimateFadeOut(Action? onComplete = null)
    {
        var host = Window.GetWindow(this);
        if (host is null)
        {
            onComplete?.Invoke();
            return;
        }

        var animation = new DoubleAnimation
        {
            To = 0,
            Duration = TimeSpan.FromMilliseconds(200),
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn }
        };
        animation.Completed += (_, _) => onComplete?.Invoke();
        host.BeginAnimation(Window.OpacityProperty, animation);
    }

    // ── Title bar drag + rename ──────────────────────────────

    private void TitleMenuButton_Click(object sender, RoutedEventArgs e)
    {
        ShowTitleBarMenu(sender as UIElement);
    }

    /// <summary>
    /// Show the title bar context menu (called from "..." button or externally from FenceHost tab menu).
    /// </summary>
    public void ShowTitleBarMenu(UIElement? placementTarget = null)
    {
        var menu = new ContextMenu();
        ApplyDarkContextMenuStyle(menu);

        // Menu-only tab mode: show tab list at top
        if (MenuOnlyTabs is { Count: > 1 })
        {
            foreach (var (title, tabIdx, isActive) in MenuOnlyTabs)
            {
                var tabItem = new MenuItem
                {
                    Header = title,
                    IsChecked = isActive,
                    IsCheckable = false
                };
                var capturedIdx = tabIdx;
                tabItem.Click += (_, _) => TabMenuSwitchRequested?.Invoke(capturedIdx);
                menu.Items.Add(tabItem);
            }
            menu.Items.Add(new Separator());
        }

        var renameItem = new MenuItem { Header = "重命名" };
        renameItem.Click += (_, _) => BeginRename();
        menu.Items.Add(renameItem);

        // Phase 13/14: per-fence icon style + view mode + sort submenus.
        // Shared with the tab-strip menu (FenceHost) so both surfaces match.
        AddViewSortMenuItems(menu.Items);

        menu.Items.Add(new Separator());

        // Folder Portal options
        if (ViewModel?.IsPortalMode == true)
        {
            var changePortalItem = new MenuItem
            {
                Header = $"更改映射文件夹 ({ViewModel.PortalPath})",
                Icon = BuildMenuIcon("IconPortal"),
            };
            changePortalItem.Click += (_, _) => BrowseAndSetPortalPath();
            menu.Items.Add(changePortalItem);

            var clearPortalItem = new MenuItem
            {
                Header = "取消文件夹映射",
                Icon = BuildMenuIcon("IconHide"),
            };
            clearPortalItem.Click += (_, _) => ClearPortalMode();
            menu.Items.Add(clearPortalItem);
        }
        else
        {
            var setPortalItem = new MenuItem
            {
                Header = "设为文件夹映射...",
                Icon = BuildMenuIcon("IconPortal"),
            };
            setPortalItem.Click += (_, _) => BrowseAndSetPortalPath();
            menu.Items.Add(setPortalItem);
        }

        menu.Items.Add(new Separator());

        var closeItem = new MenuItem
        {
            Header = "关闭 Fence",
            Icon = BuildMenuIcon("IconTrash"),
        };
        closeItem.Click += (_, _) => CloseRequested?.Invoke();
        menu.Items.Add(closeItem);

        menu.PlacementTarget = placementTarget ?? TitleMenuButton;
        menu.IsOpen = true;
    }

    private void BrowseAndSetPortalPath()
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog
        {
            Title = "选择要映射的文件夹"
        };

        if (!string.IsNullOrEmpty(ViewModel?.PortalPath))
            dialog.InitialDirectory = ViewModel.PortalPath;

        if (dialog.ShowDialog() == true)
        {
            if (ViewModel is not null)
            {
                ViewModel.PortalPath = dialog.FolderName;
                var folderName = System.IO.Path.GetFileName(dialog.FolderName);
                if (string.IsNullOrEmpty(folderName)) folderName = dialog.FolderName;
                ViewModel.Title = $"📁 {folderName}";
                PortalModeChanged?.Invoke(dialog.FolderName);
                InteractionEnded?.Invoke();
            }
        }
    }

    private void ClearPortalMode()
    {
        if (ViewModel is null) return;
        // Remove portal prefix from title if present
        if (ViewModel.Title.StartsWith("📁 "))
            ViewModel.Title = ViewModel.Title[3..];
        ViewModel.PortalPath = null;
        PortalModeChanged?.Invoke(null);
        InteractionEnded?.Invoke();
    }

    private RenameWindow? _renameWindow;

    /// <summary>
    /// Open rename dialog (called from title bar context menu or FenceHost tab context menu).
    /// 非模态：ShowDialog 会让 Win32 对同线程所有顶层窗口 EnableWindow(FALSE)，
    /// 重命名期间所有 fence 与桌面图标 overlay 都无法点击（bug 21 同根因，
    /// 参见 docs/bug/settings_modal_disables_fences.md）。
    /// </summary>
    public void BeginRename()
    {
        if (ViewModel is null) return;

        // 已打开则置前复用，避免重复窗口
        if (_renameWindow is not null)
        {
            _renameWindow.Activate();
            return;
        }

        var dialog = new RenameWindow(ViewModel.Title) { Owner = Window.GetWindow(this) };
        dialog.RenameConfirmed += newName =>
        {
            if (ViewModel is null) return;
            ViewModel.Title = newName;
            InteractionEnded?.Invoke();
        };
        dialog.Closed += (_, _) => _renameWindow = null;
        _renameWindow = dialog;
        dialog.Show();
        dialog.Activate();
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        // Ignore double-clicks — WM_NCLBUTTONDBLCLK is handled in FenceHost.WndProc.
        // Letting a double-click through would restart the drag loop unexpectedly.
        if (e.ClickCount != 1) return;

        if (e.LeftButton == MouseButtonState.Pressed)
        {
            InteractionStarted?.Invoke();
            var hostWindow = Window.GetWindow(this);
            if (hostWindow is not null)
            {
                // Use WM_NCLBUTTONDOWN + HTCAPTION instead of DragMove()
                // so that WM_MOVING messages are generated for real-time snap
                var helper = new WindowInteropHelper(hostWindow);
                NativeMethods.SendMessage(helper.Handle, NativeMethods.WM_NCLBUTTONDOWN,
                    (IntPtr)NativeMethods.HTCAPTION, IntPtr.Zero);
            }
            // Position sync is handled by WM_EXITSIZEMOVE → FenceHost.HandleExitSizeMove
        }
    }

    private void RollupToggleButton_Click(object sender, RoutedEventArgs e)
    {
        ToggleRollup();
    }

    private void SyncPositionFromWindow()
    {
        if (ViewModel is null) return;
        var hostWindow = Window.GetWindow(this);
        if (hostWindow is null) return;
        ViewModel.X = hostWindow.Left;
        ViewModel.Y = hostWindow.Top;
    }

    // ── Resize grips ────────────────────────────────────────

    private void GripTop_DragDelta(object sender, DragDeltaEventArgs e) => ResizeFromTop(e.VerticalChange);
    private void GripBottom_DragDelta(object sender, DragDeltaEventArgs e) => ResizeFromBottom(e.VerticalChange);
    private void GripLeft_DragDelta(object sender, DragDeltaEventArgs e) => ResizeFromLeft(e.HorizontalChange);
    private void GripRight_DragDelta(object sender, DragDeltaEventArgs e) => ResizeFromRight(e.HorizontalChange);

    private void Grip_DragCompleted(object sender, DragCompletedEventArgs e)
    {
        SnapOverlay?.Hide();
        InteractionStarted?.Invoke();
        InteractionEnded?.Invoke();
    }

    private void GripTopLeft_DragDelta(object sender, DragDeltaEventArgs e)
    {
        ResizeFromTop(e.VerticalChange);
        ResizeFromLeft(e.HorizontalChange);
    }

    private void GripTopRight_DragDelta(object sender, DragDeltaEventArgs e)
    {
        ResizeFromTop(e.VerticalChange);
        ResizeFromRight(e.HorizontalChange);
    }

    private void GripBottomLeft_DragDelta(object sender, DragDeltaEventArgs e)
    {
        ResizeFromBottom(e.VerticalChange);
        ResizeFromLeft(e.HorizontalChange);
    }

    private void GripBottomRight_DragDelta(object sender, DragDeltaEventArgs e)
    {
        ResizeFromBottom(e.VerticalChange);
        ResizeFromRight(e.HorizontalChange);
    }

    private void ResizeFromTop(double delta)
    {
        var win = Window.GetWindow(this);
        if (win is null || ViewModel is null) return;
        var newHeight = win.Height - delta;
        if (newHeight < FencePanelViewModel.MinHeight) return;
        win.Top += delta;
        win.Height = newHeight;
        ViewModel.Y = win.Top;
        ViewModel.Height = newHeight;
        ApplyResizeSnap(win);
    }

    private void ResizeFromBottom(double delta)
    {
        var win = Window.GetWindow(this);
        if (win is null || ViewModel is null) return;
        var newHeight = win.Height + delta;
        if (newHeight < FencePanelViewModel.MinHeight) return;
        win.Height = newHeight;
        ViewModel.Height = newHeight;
        ApplyResizeSnap(win);
    }

    private void ResizeFromLeft(double delta)
    {
        var win = Window.GetWindow(this);
        if (win is null || ViewModel is null) return;
        var newWidth = win.Width - delta;
        if (newWidth < FencePanelViewModel.MinWidth) return;
        win.Left += delta;
        win.Width = newWidth;
        ViewModel.X = win.Left;
        ViewModel.Width = newWidth;
        ApplyResizeSnap(win);
    }

    private void ResizeFromRight(double delta)
    {
        var win = Window.GetWindow(this);
        if (win is null || ViewModel is null) return;
        var newWidth = win.Width + delta;
        if (newWidth < FencePanelViewModel.MinWidth) return;
        win.Width = newWidth;
        ViewModel.Width = newWidth;
        ApplyResizeSnap(win);
    }

    /// <summary>
    /// After a resize delta is applied, check snap targets and correct
    /// the window position/size accordingly.
    /// </summary>
    private void ApplyResizeSnap(Window win)
    {
        if (SnapThreshold <= 0 || GetOtherFenceRects is null || ViewModel is null) return;

        var movingRect = new SnapEngine.Rect(win.Left, win.Top, win.Width, win.Height);
        var others = GetOtherFenceRects();

        var screen = System.Windows.Forms.Screen.FromPoint(
            new System.Drawing.Point((int)win.Left, (int)win.Top));
        var workArea = screen.WorkingArea;
        var screenRect = new SnapEngine.Rect(workArea.X, workArea.Y, workArea.Width, workArea.Height);

        var result = SnapEngine.SnapResize(movingRect, others, screenRect, SnapThreshold);

        if (result.X != win.Left || result.Y != win.Top ||
            result.Width != win.Width || result.Height != win.Height)
        {
            win.Left = result.X;
            win.Top = result.Y;
            win.Width = result.Width;
            win.Height = result.Height;
            ViewModel.X = result.X;
            ViewModel.Y = result.Y;
            ViewModel.Width = result.Width;
            ViewModel.Height = result.Height;
        }

        SnapOverlay?.ShowLines(result.Lines);
    }

    // ── Drag-drop from Explorer ──────────────────────────────

    private void OnDragOver(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            // 应用内拖拽（fence 间 / overlay→fence）按移动处理，源端在 DoDragDrop
            // 返回 Move 后删除自己的条目；Explorer 来源保持复制引用。
            e.Effects = e.Data.GetDataPresent(InternalDragFormats.Marker)
                ? DragDropEffects.Move
                : DragDropEffects.Copy;
            if (ViewModel is not null) ViewModel.IsDropHover = true;
        }
        else
        {
            e.Effects = DragDropEffects.None;
        }
        e.Handled = true;
    }

    private void OnDragLeave(object sender, DragEventArgs e)
    {
        if (ViewModel is not null) ViewModel.IsDropHover = false;
    }

    private void OnDrop(object sender, DragEventArgs e)
    {
        if (ViewModel is not null) ViewModel.IsDropHover = false;

        if (ViewModel is null) return;
        if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;
        // GetDataPresent 为 true 时 GetData 在延迟渲染失败等场景仍可能返回 null
        if (e.Data.GetData(DataFormats.FileDrop) is not string[] files) return;

        bool isInternal = e.Data.GetDataPresent(InternalDragFormats.Marker);

        // Phase 14c: self-drag within the same fence (non-portal) → reorder in
        // place instead of add/remove. Report None so the source side never
        // removes the (only) entry, and switch the fence to Manual order.
        if (isInternal && !ViewModel.IsPortalMode && files.Length == 1
            && e.Data.GetData(InternalDragFormats.SourceFenceId) is string sid
            && Guid.TryParse(sid, out var srcId) && srcId == ViewModel.Id)
        {
            int insertIndex = GetDropInsertIndex(e);
            if (ViewModel.ReorderFile(files[0], insertIndex))
            {
                AnimateDropPulse();
                InteractionEnded?.Invoke();
            }
            e.Effects = DragDropEffects.None;
            e.Handled = true;
            return;
        }

        bool anyAdded = false;

        foreach (var filePath in files)
        {
            if (ViewModel.ContainsFile(filePath)) continue;
            var added = ViewModel.AddFile(filePath);
            if (added is null) continue;
            LoadIconFor(added);
            anyAdded = true;
        }

        // Restore the active sort once after the batch (no-op in Manual mode).
        if (anyAdded) ViewModel.ResortAfterChange();

        // 内部拖拽 = 移动：回报 Move 让源端（另一个 fence / overlay）删除条目。
        // 文件已在本 fence（含自拖自）时回报 None，否则源端会把唯一条目删掉。
        // Explorer 来源永远回报 Copy——回报 Move 可能让 Explorer 删除磁盘源文件。
        e.Effects = isInternal
            ? (anyAdded ? DragDropEffects.Move : DragDropEffects.None)
            : DragDropEffects.Copy;

        // Scale-in animation for the border on drop
        if (anyAdded) AnimateDropPulse();
        e.Handled = true;
    }

    // ── Focus glow: subscribe to host window activation ─────────

    private Window? _hostWindow;

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        SelectionCoordinator?.Register(this);

        _hostWindow = Window.GetWindow(this);
        if (_hostWindow is null) return;
        _hostWindow.Activated += OnHostActivated;
        _hostWindow.Deactivated += OnHostDeactivated;
        if (ViewModel is not null) ViewModel.IsFocused = _hostWindow.IsActive;

        // Re-sync the title-bar row height in case ShowTitleBar was toggled
        // before the named row was reachable from the dependency-property callback.
        TitleBarRow.Height = ShowTitleBar ? new GridLength(30) : new GridLength(0);

        // Phase 14: seed the Detail header sort indicator for the initial state.
        UpdateSortGlyphs();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        SelectionCoordinator?.Unregister(this);

        if (_subscribedVm is not null)
        {
            _subscribedVm.PropertyChanged -= OnViewModelPropertyChanged;
            _subscribedVm = null;
        }

        if (_hostWindow is null) return;
        _hostWindow.Activated -= OnHostActivated;
        _hostWindow.Deactivated -= OnHostDeactivated;
        _hostWindow = null;
    }

    private void OnHostActivated(object? sender, EventArgs e)
    {
        if (ViewModel is not null) ViewModel.IsFocused = true;
    }

    private void OnHostDeactivated(object? sender, EventArgs e)
    {
        if (ViewModel is not null) ViewModel.IsFocused = false;
    }

    /// <summary>
    /// Phase 14c: compute the "insert before" index for a self-drag drop. Hit-tests
    /// the file under the cursor and decides before/after by the cursor's position
    /// within that tile — X midline for Icon view (horizontal flow), Y midline for
    /// List / Detail (vertical flow). A drop on empty space targets the end.
    /// </summary>
    private int GetDropInsertIndex(DragEventArgs e)
    {
        if (ViewModel is null) return 0;

        var pos = e.GetPosition(FileListBox);
        DependencyObject? d = FileListBox.InputHitTest(pos) as DependencyObject;
        while (d is not null and not ListBoxItem)
            d = VisualTreeHelper.GetParent(d) ?? LogicalTreeHelper.GetParent(d);

        if (d is not ListBoxItem { DataContext: FileItemViewModel hit })
            return ViewModel.Files.Count; // empty space → end

        int hitIndex = ViewModel.Files.IndexOf(hit);
        if (hitIndex < 0) return ViewModel.Files.Count;

        var container = (FrameworkElement)d;
        var posInItem = e.GetPosition(container);
        bool after = ViewModel.ViewMode == ViewMode.Icon
            ? posInItem.X > container.ActualWidth / 2
            : posInItem.Y > container.ActualHeight / 2;
        return after ? hitIndex + 1 : hitIndex;
    }

    private void AnimateDropPulse()
    {
        var scaleTransform = new ScaleTransform(1, 1);
        FenceBorder.RenderTransform = scaleTransform;
        FenceBorder.RenderTransformOrigin = new Point(0.5, 0.5);

        var scaleUp = new DoubleAnimation(1.0, 1.02, TimeSpan.FromMilliseconds(100));
        var scaleDown = new DoubleAnimation(1.02, 1.0, TimeSpan.FromMilliseconds(150));

        scaleDown.Completed += (_, _) =>
        {
            // Clear RenderTransform to avoid visual offset with DropShadowEffect
            FenceBorder.RenderTransform = Transform.Identity;
        };

        scaleUp.Completed += (_, _) =>
        {
            scaleTransform.BeginAnimation(ScaleTransform.ScaleXProperty, scaleDown);
            scaleTransform.BeginAnimation(ScaleTransform.ScaleYProperty, scaleDown);
        };

        scaleTransform.BeginAnimation(ScaleTransform.ScaleXProperty, scaleUp);
        scaleTransform.BeginAnimation(ScaleTransform.ScaleYProperty, scaleUp);
    }

    // ── File item interactions ────────────────────────────────

    private void FileItem_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement element || element.DataContext is not FileItemViewModel item)
            return;

        // Double-click → open file
        if (e.ClickCount == 2)
        {
            ShellFileOperations.OpenFile(item.FilePath);
            e.Handled = true;
            return;
        }

        // Single click → modifier-aware selection + prepare for drag.
        // NotifyActivated clears other fences / the desktop (global single selection).
        SelectionCoordinator?.NotifyActivated(this);

        bool ctrl = (Keyboard.Modifiers & ModifierKeys.Control) != 0;
        bool shift = (Keyboard.Modifiers & ModifierKeys.Shift) != 0;

        if (ctrl)
        {
            item.IsSelected = !item.IsSelected;   // toggle, keep the rest
            _selectionAnchor = item;
        }
        else if (shift && _selectionAnchor is not null)
        {
            SelectRange(_selectionAnchor, item);  // range from anchor
        }
        else if (item.IsSelected)
        {
            // Already part of a multi-selection → keep it so a drag moves the whole group.
            _selectionAnchor = item;
        }
        else
        {
            ClearSelection();
            item.IsSelected = true;
            _selectionAnchor = item;
        }

        _dragStartPoint = e.GetPosition(this);
        _isDraggingFile = false;
    }

    /// <summary>Shift-range select: select every item between the anchor and target (inclusive).</summary>
    private void SelectRange(FileItemViewModel anchor, FileItemViewModel target)
    {
        if (ViewModel is null) return;
        int a = ViewModel.Files.IndexOf(anchor);
        int b = ViewModel.Files.IndexOf(target);
        if (a < 0 || b < 0) return;
        if (a > b) (a, b) = (b, a);

        for (int i = 0; i < ViewModel.Files.Count; i++)
            ViewModel.Files[i].IsSelected = i >= a && i <= b;
    }

    private void FileItem_MouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed) return;
        // A tab drag in the host window captures the mouse (CaptureMode.SubTree),
        // which still routes MouseMove here. Don't start a file OLE-drag in that
        // case — it would hijack the tab tear-off and, dropped on the desktop,
        // raise a same-file shell error ("源文件名和目标文件名相同").
        if (_hostWindow is not null && ReferenceEquals(Mouse.Captured, _hostWindow)) return;
        if (sender is not FrameworkElement element || element.DataContext is not FileItemViewModel item)
            return;

        var currentPos = e.GetPosition(this);
        var diff = currentPos - _dragStartPoint;

        if (Math.Abs(diff.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(diff.Y) < SystemParameters.MinimumVerticalDragDistance)
            return;

        if (_isDraggingFile) return;
        _isDraggingFile = true;

        // Group drag: if the pressed item is part of a multi-selection, carry all selected paths.
        var selected = ViewModel?.Files.Where(f => f.IsSelected).Select(f => f.FilePath).ToArray();
        string[] paths = (item.IsSelected && selected is { Length: > 1 })
            ? selected
            : [item.FilePath];

        var dataObject = new DataObject(DataFormats.FileDrop, paths);
        // 内部拖拽标记：目标 fence 据此回报 Move（移动语义），Explorer 会忽略该格式。
        dataObject.SetData(InternalDragFormats.Marker, true);
        // 来源 fence id：drop 落到同一 fence 时识别为"自拖自"→ 原地重排（Phase 14c，仅单文件）。
        if (ViewModel is not null)
            dataObject.SetData(InternalDragFormats.SourceFenceId, ViewModel.Id.ToString());

        // 拖拽期间让桌面 overlay 进入放置目标模式，避免拖到桌面空白区穿透到真实桌面
        // 触发同名移动错误（bug 39）。整段包 try/finally：DoDragDrop / 事件链一旦抛异常，
        // 必须复位 _isDraggingFile，否则该 fence 后续所有拖拽被 `if (_isDraggingFile) return;`
        // 静默吞掉（表现为"多文件拖拽没有响应"——多选拖拽更易触发某个抛点）。
        try
        {
            InternalFileDragStarted?.Invoke();
            var result = DragDrop.DoDragDrop(element, dataObject, DragDropEffects.Copy | DragDropEffects.Move);

            if (result == DragDropEffects.Move && ViewModel is not null)
            {
                foreach (var p in paths)
                    ViewModel.RemoveFile(p);
                // 源 fence 丢失文件的状态需持久化，否则重启后文件又回到 fence。
                // （同时修复 fence→fence 移动不持久化的既有隐患。）
                InteractionEnded?.Invoke();
            }
        }
        finally
        {
            InternalFileDragEnded?.Invoke();
            _isDraggingFile = false;
        }
    }

    private void FileItem_MouseRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement element || element.DataContext is not FileItemViewModel item)
            return;

        // Keep an existing multi-selection if right-clicking one of its items; otherwise
        // select just this item. (Shell menu still operates on the single right-clicked file.)
        SelectionCoordinator?.NotifyActivated(this);
        if (!item.IsSelected)
        {
            ClearSelection();
            item.IsSelected = true;
        }

        var hostWindow = Window.GetWindow(this);
        if (hostWindow is null) return; // 控件已脱离视觉树（fence 正在关闭）

        var screenPoint = element.PointToScreen(e.GetPosition(element));
        var hwnd = new WindowInteropHelper(hostWindow).Handle;
        ShellContextMenu.Show(hwnd, item.FilePath, (int)screenPoint.X, (int)screenPoint.Y);

        e.Handled = true;
    }

    // ── Icon loading ─────────────────────────────────────────

    public void LoadAllIcons()
    {
        if (ViewModel is null || IconExtractor is null) return;
        foreach (var file in ViewModel.Files)
        {
            if (file.Icon is null)
                file.Icon = IconExtractor.GetIcon(file.FilePath);
        }
    }

    private void LoadIconFor(FileItemViewModel item)
    {
        if (IconExtractor is null) return;
        if (item.Icon is null)
            item.Icon = IconExtractor.GetIcon(item.FilePath);

        // Scale-in animation for newly added file item
        AnimateNewFileItem(item);
    }

    private void AnimateNewFileItem(FileItemViewModel item)
    {
        // Delay to let layout update, then animate the item's container.
        // Resolved via ContainerFromItem (not index) so the animation still
        // targets the right tile after a re-sort moves it off the end.
        Dispatcher.InvokeAsync(() =>
        {
            if (FileListBox.ItemContainerGenerator.ContainerFromItem(item) is not FrameworkElement container)
                return;

            var scale = new ScaleTransform(0.8, 0.8);
            container.RenderTransform = scale;
            container.RenderTransformOrigin = new Point(0.5, 0.5);
            container.Opacity = 0;

            var scaleAnim = new DoubleAnimation(0.8, 1.0, TimeSpan.FromMilliseconds(200))
            {
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
            };
            var opacityAnim = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(200));

            scale.BeginAnimation(ScaleTransform.ScaleXProperty, scaleAnim);
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, scaleAnim);
            container.BeginAnimation(OpacityProperty, opacityAnim);
        }, System.Windows.Threading.DispatcherPriority.Loaded);
    }

    public void ClearSelection()
    {
        if (ViewModel is null) return;
        foreach (var file in ViewModel.Files)
            file.IsSelected = false;
    }

    // ── Marquee (rubber-band) selection on empty list area ─────

    private void FileListBox_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (ViewModel is null) return;
        // Only empty area starts a marquee — presses on an item / scrollbar fall through
        // to the item Border handlers or the scrollbar.
        if (IsOverItemOrScrollBar(e.OriginalSource as DependencyObject)) return;

        SelectionCoordinator?.NotifyActivated(this); // global single selection

        bool additive = (Keyboard.Modifiers & (ModifierKeys.Control | ModifierKeys.Shift)) != 0;
        if (additive)
            _marqueeBaseline = ViewModel.Files.Where(f => f.IsSelected).ToList();
        else
        {
            ClearSelection();
            _marqueeBaseline = [];
        }
        _selectionAnchor = null;

        _marqueeOrigin = e.GetPosition(FileListBox);
        _isMarqueeSelecting = true;
        UpdateMarqueeRect(_marqueeOrigin, _marqueeOrigin);
        MarqueeRect.Visibility = Visibility.Visible;
        FileListBox.CaptureMouse();
        e.Handled = true;
    }

    private void FileListBox_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_isMarqueeSelecting) return;
        var current = e.GetPosition(FileListBox);
        UpdateMarqueeRect(_marqueeOrigin, current);

        bool additive = (Keyboard.Modifiers & (ModifierKeys.Control | ModifierKeys.Shift)) != 0;
        ApplyMarqueeSelection(new Rect(_marqueeOrigin, current), additive);
    }

    private void FileListBox_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_isMarqueeSelecting) return;
        _isMarqueeSelecting = false;
        FileListBox.ReleaseMouseCapture();
        MarqueeRect.Visibility = Visibility.Collapsed;
    }

    /// <summary>Select every realized item whose bounds intersect the marquee (FileListBox space);
    /// in additive mode keep the pre-drag baseline as a union. Virtualized (off-screen) items have
    /// no container and can't be under the rectangle anyway, so they're skipped.</summary>
    private void ApplyMarqueeSelection(Rect marquee, bool additive)
    {
        if (ViewModel is null) return;
        foreach (var item in ViewModel.Files)
        {
            bool inMarquee = false;
            if (FileListBox.ItemContainerGenerator.ContainerFromItem(item) is FrameworkElement container
                && container.IsVisible)
            {
                var topLeft = container.TransformToVisual(FileListBox).Transform(new Point(0, 0));
                var bounds = new Rect(topLeft, new Size(container.ActualWidth, container.ActualHeight));
                inMarquee = marquee.IntersectsWith(bounds);
            }
            item.IsSelected = inMarquee || (additive && _marqueeBaseline.Contains(item));
        }
    }

    private void UpdateMarqueeRect(Point a, Point b)
    {
        Canvas.SetLeft(MarqueeRect, Math.Min(a.X, b.X));
        Canvas.SetTop(MarqueeRect, Math.Min(a.Y, b.Y));
        MarqueeRect.Width = Math.Abs(a.X - b.X);
        MarqueeRect.Height = Math.Abs(a.Y - b.Y);
    }

    private static bool IsOverItemOrScrollBar(DependencyObject? d)
    {
        while (d is not null)
        {
            if (d is ListBoxItem or ScrollBar) return true;
            d = VisualTreeHelper.GetParent(d) ?? LogicalTreeHelper.GetParent(d);
        }
        return false;
    }

    // ── Rollup ───────────────────────────────────────────────

    private const double RolledUpHeight = 38;

    private void ToggleRollup()
    {
        if (ViewModel is null) return;

        if (ViewModel.IsRolledUp)
        {
            ViewModel.IsRolledUp = false;
            _isHoverExpanded = false;
            var targetHeight = ViewModel.ExpandedHeight > 0
                ? ViewModel.ExpandedHeight
                : FencePanelViewModel.MinHeight;
            RollupChanged?.Invoke(false, targetHeight);
        }
        else
        {
            var win = Window.GetWindow(this);
            if (win is not null)
                ViewModel.ExpandedHeight = win.Height;
            ViewModel.IsRolledUp = true;
            _isHoverExpanded = false;
            RollupChanged?.Invoke(true, RolledUpHeight);
        }
        UpdateRollupArrow();
        InteractionEnded?.Invoke();
    }

    /// <summary>
    /// Update the rollup toggle arrow direction based on current state.
    /// Icon is IconRollup; rotate 180° when rolled up.
    /// </summary>
    public void UpdateRollupArrow()
    {
        if (RollupIcon.RenderTransform is RotateTransform rt)
            rt.Angle = ViewModel?.IsRolledUp == true ? 180 : 0;
    }

    /// <summary>
    /// Public entry point for FenceHost to trigger rollup toggle (e.g. from tab strip arrow).
    /// </summary>
    public void ToggleRollupFromHost()
    {
        ToggleRollup();
    }

    public void HoverExpand()
    {
        if (ViewModel is null || !ViewModel.IsRolledUp || _isHoverExpanded) return;
        _isHoverExpanded = true;
        var targetHeight = ViewModel.ExpandedHeight > 0
            ? ViewModel.ExpandedHeight
            : FencePanelViewModel.MinHeight;
        RollupChanged?.Invoke(false, targetHeight);
    }

    public void HoverCollapse()
    {
        if (ViewModel is null || !ViewModel.IsRolledUp || !_isHoverExpanded) return;
        _isHoverExpanded = false;
        RollupChanged?.Invoke(true, RolledUpHeight);
    }

    /// <summary>
    /// Phase 13: build the "图标风格" submenu (跟随全局 / App 自绘 / System 经典 / Shell 真实).
    /// Reads the current per-fence override from <see cref="FencePanelViewModel.IconStyleOverride"/>
    /// for IsChecked state. Selecting an item updates the ViewModel, refreshes
    /// the file-tile templates, and fires <see cref="InteractionEnded"/> to
    /// trigger persistence via the host's RequestAutoSave path.
    /// </summary>
    private MenuItem BuildIconStyleSubmenu()
    {
        var root = new MenuItem
        {
            Header = "图标风格",
            Icon = BuildMenuIcon("IconTheme"),
        };

        var darkItemStyle = Application.Current?.TryFindResource("DarkMenuItemStyle") as Style;
        var current = ViewModel?.IconStyleOverride;

        AddChoice(root, darkItemStyle, "跟随全局",     null,                    current);
        AddChoice(root, darkItemStyle, "App 自绘",     FileIconStyle.App,       current);
        AddChoice(root, darkItemStyle, "System 经典",  FileIconStyle.System,    current);
        AddChoice(root, darkItemStyle, "Shell 真实",   FileIconStyle.Shell,     current);

        return root;
    }

    private void AddChoice(MenuItem parent, Style? itemStyle, string label,
                           FileIconStyle? value, FileIconStyle? current)
    {
        var item = new MenuItem
        {
            Header = label,
            IsCheckable = false,
            IsChecked = value == current,
        };
        if (itemStyle is not null) item.Style = itemStyle;
        item.Click += (_, _) =>
        {
            if (ViewModel is null || ViewModel.IconStyleOverride == value) return;
            ViewModel.IconStyleOverride = value;
            // RefreshFileTileTemplate is also invoked by the PropertyChanged
            // subscription, but call it here too so the update is synchronous
            // even if the binding hasn't propagated yet.
            RefreshFileTileTemplate();
            InteractionEnded?.Invoke();
        };
        parent.Items.Add(item);
    }

    /// <summary>
    /// Phase 14: append the per-fence 图标风格 / 呈现方式 / 排序方式 submenus to a
    /// menu's item collection. Shared by <see cref="ShowTitleBarMenu"/> (standalone /
    /// MenuOnly tab) and FenceHost's tab-strip menu so both surfaces stay in sync.
    /// The submenus bind to this panel's <c>ViewModel</c>; in tab mode FenceHost's
    /// active tab is this panel's DataContext, so they target the right fence.
    /// </summary>
    public void AddViewSortMenuItems(System.Windows.Controls.ItemCollection items)
    {
        items.Add(BuildIconStyleSubmenu());
        items.Add(BuildViewModeSubmenu());
        items.Add(BuildSortSubmenu());
    }

    /// <summary>
    /// Phase 14: build the "呈现方式" submenu — 图标 / 列表 / 详细信息. IsChecked
    /// reflects the current <see cref="FencePanelViewModel.ViewMode"/>. Selecting
    /// an item updates the ViewModel (which swaps ItemsPanel / templates via
    /// bindings) and fires <see cref="InteractionEnded"/> for persistence.
    /// </summary>
    private MenuItem BuildViewModeSubmenu()
    {
        var root = new MenuItem
        {
            Header = "呈现方式",
            Icon = BuildMenuIcon("IconGrid"),
        };

        var darkItemStyle = Application.Current?.TryFindResource("DarkMenuItemStyle") as Style;
        var current = ViewModel?.ViewMode ?? ViewMode.Icon;

        AddViewModeChoice(root, darkItemStyle, "图标", ViewMode.Icon, current);
        AddViewModeChoice(root, darkItemStyle, "列表", ViewMode.List, current);
        AddViewModeChoice(root, darkItemStyle, "详细信息", ViewMode.Detail, current);

        return root;
    }

    private void AddViewModeChoice(MenuItem parent, Style? itemStyle, string label,
                                   ViewMode value, ViewMode current)
    {
        var item = new MenuItem
        {
            Header = label,
            IsCheckable = false,
            IsChecked = value == current,
        };
        if (itemStyle is not null) item.Style = itemStyle;
        item.Click += (_, _) =>
        {
            if (ViewModel is null || ViewModel.ViewMode == value) return;
            ViewModel.ViewMode = value; // PropertyChanged → RefreshFileTileTemplate + Style triggers
            InteractionEnded?.Invoke();
        };
        parent.Items.Add(item);
    }

    /// <summary>
    /// Phase 14: build the "排序方式" submenu — sort field (名称 / 扩展名 / 大小 /
    /// 修改时间 / 创建时间 / 手动排列) plus a 升序 / 降序 group. Field / direction
    /// IsChecked reflects the current <see cref="FencePanelViewModel.SortBy"/> /
    /// <see cref="FencePanelViewModel.SortDirection"/>. Selecting an item drives
    /// the ViewModel (whose setter re-sorts) and fires <see cref="InteractionEnded"/>
    /// for persistence. 手动排列 is disabled for portal fences; the direction items
    /// are disabled while 手动排列 is active.
    /// </summary>
    private MenuItem BuildSortSubmenu()
    {
        var root = new MenuItem
        {
            Header = "排序方式",
            Icon = BuildMenuIcon("IconRule"),
        };

        var darkItemStyle = Application.Current?.TryFindResource("DarkMenuItemStyle") as Style;
        var currentField = ViewModel?.SortBy ?? SortField.Name;
        bool isPortal = ViewModel?.IsPortalMode == true;

        AddSortFieldChoice(root, darkItemStyle, "名称", SortField.Name, currentField);
        AddSortFieldChoice(root, darkItemStyle, "扩展名", SortField.Extension, currentField);
        AddSortFieldChoice(root, darkItemStyle, "大小", SortField.Size, currentField);
        AddSortFieldChoice(root, darkItemStyle, "修改时间", SortField.DateModified, currentField);
        AddSortFieldChoice(root, darkItemStyle, "创建时间", SortField.DateCreated, currentField);
        var manualItem = AddSortFieldChoice(root, darkItemStyle, "手动排列", SortField.Manual, currentField);
        manualItem.IsEnabled = !isPortal; // portals are folder-driven; manual order doesn't persist

        // The menu.Opened styler only reaches top-level items, so style this
        // nested separator explicitly to keep it dark.
        var sep = new Separator();
        if (Application.Current?.TryFindResource("DarkSeparatorStyle") is Style sepStyle)
            sep.Style = sepStyle;
        root.Items.Add(sep);

        var currentDir = ViewModel?.SortDirection ?? SortDirection.Ascending;
        bool dirEnabled = currentField != SortField.Manual;
        AddSortDirectionChoice(root, darkItemStyle, "升序", SortDirection.Ascending, currentDir, dirEnabled);
        AddSortDirectionChoice(root, darkItemStyle, "降序", SortDirection.Descending, currentDir, dirEnabled);

        return root;
    }

    private MenuItem AddSortFieldChoice(MenuItem parent, Style? itemStyle, string label,
                                        SortField value, SortField current)
    {
        var item = new MenuItem
        {
            Header = label,
            IsCheckable = false,
            IsChecked = value == current,
        };
        if (itemStyle is not null) item.Style = itemStyle;
        item.Click += (_, _) =>
        {
            if (ViewModel is null || ViewModel.SortBy == value) return;
            ViewModel.SortBy = value; // setter re-sorts (no-op for Manual)
            InteractionEnded?.Invoke();
        };
        parent.Items.Add(item);
        return item;
    }

    private void AddSortDirectionChoice(MenuItem parent, Style? itemStyle, string label,
                                        SortDirection value, SortDirection current, bool enabled)
    {
        var item = new MenuItem
        {
            Header = label,
            IsCheckable = false,
            IsChecked = enabled && value == current,
            IsEnabled = enabled,
        };
        if (itemStyle is not null) item.Style = itemStyle;
        item.Click += (_, _) =>
        {
            if (ViewModel is null || ViewModel.SortDirection == value) return;
            ViewModel.SortDirection = value; // setter re-sorts
            InteractionEnded?.Invoke();
        };
        parent.Items.Add(item);
    }

    /// <summary>
    /// Phase 14: Detail-view column header click. Clicking the active sort
    /// column flips the direction; clicking another column sorts by it (the
    /// ViewModel setter re-sorts). Fires <see cref="InteractionEnded"/> to persist.
    /// </summary>
    private void DetailHeader_Click(object sender, MouseButtonEventArgs e)
    {
        if (ViewModel is null
            || sender is not FrameworkElement fe
            || fe.Tag is not string tag
            || !Enum.TryParse<SortField>(tag, out var field))
            return;

        if (ViewModel.SortBy == field)
            ViewModel.SortDirection = ViewModel.SortDirection == SortDirection.Ascending
                ? SortDirection.Descending
                : SortDirection.Ascending;
        else
            ViewModel.SortBy = field;

        UpdateSortGlyphs(); // also fired via PropertyChanged, call here for immediacy
        InteractionEnded?.Invoke();
        e.Handled = true;
    }

    /// <summary>
    /// Phase 14: refresh the Detail header sort indicators (▲ asc / ▼ desc) so
    /// only the active column carries a glyph. No-op before the header is built.
    /// </summary>
    private void UpdateSortGlyphs()
    {
        if (ViewModel is null || HeaderName is null) return;
        string glyph = ViewModel.SortDirection == SortDirection.Ascending ? " ▲" : " ▼";
        HeaderName.Text = "名称" + (ViewModel.SortBy == SortField.Name ? glyph : "");
        HeaderSize.Text = "大小" + (ViewModel.SortBy == SortField.Size ? glyph : "");
        HeaderDate.Text = "修改时间" + (ViewModel.SortBy == SortField.DateModified ? glyph : "");
    }

    /// <summary>
    /// Build a ContentControl icon (from Icons.xaml resources) suitable for MenuItem.Icon.
    /// Using ContentControl + IconTemplate preserves the Foreground inheritance chain so
    /// icons pick up the correct theme brush; a bare Path would break that.
    /// </summary>
    internal static UIElement? BuildMenuIcon(string geometryKey)
    {
        var app = Application.Current;
        if (app is null) return null;
        if (app.TryFindResource("IconTemplate") is not ControlTemplate template) return null;
        if (app.TryFindResource(geometryKey) is not Geometry geometry) return null;
        var brush = app.TryFindResource("TextSecondaryBrush") as Brush;
        return new ContentControl
        {
            Template = template,
            Tag = geometry,
            Width = 14,
            Height = 14,
            Foreground = brush ?? Brushes.LightGray,
        };
    }

    /// <summary>
    /// Apply dark theme styles to a programmatically-created ContextMenu.
    /// </summary>
    internal static void ApplyDarkContextMenuStyle(ContextMenu menu)
    {
        if (Application.Current.TryFindResource("DarkContextMenuStyle") is Style contextMenuStyle)
            menu.Style = contextMenuStyle;

        void OnMenuOpened(object sender, RoutedEventArgs e)
        {
            var cm = (ContextMenu)sender;
            var menuItemStyle = Application.Current.TryFindResource("DarkMenuItemStyle") as Style;
            var separatorStyle = Application.Current.TryFindResource("DarkSeparatorStyle") as Style;
            foreach (var item in cm.Items)
            {
                if (item is MenuItem mi && menuItemStyle is not null)
                    mi.Style = menuItemStyle;
                else if (item is Separator sep && separatorStyle is not null)
                    sep.Style = separatorStyle;
            }
        }

        menu.Opened += OnMenuOpened;
    }
}
