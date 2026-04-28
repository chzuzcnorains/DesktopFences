using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using DesktopFences.Core.Services;
using DesktopFences.Shell.Desktop;
using DesktopFences.Shell.Interop;
using DesktopFences.UI.ViewModels;

namespace DesktopFences.UI.Controls;

public partial class FencePanel : UserControl
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

    /// <summary>Allow FenceHost to raise InteractionStarted (e.g. tab strip drag).</summary>
    public void RaiseInteractionStarted() => InteractionStarted?.Invoke();
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

    /// <summary>
    /// Set by FenceHost when in MenuOnly tab mode to provide tab titles.
    /// </summary>
    public IReadOnlyList<(string Title, int Index, bool IsActive)>? MenuOnlyTabs { get; set; }

    public FencePanel()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private FencePanelViewModel? ViewModel => DataContext as FencePanelViewModel;

    /// <summary>
    /// Returns the current fence body background brush (for tab strip color sync).
    /// </summary>
    public Brush? FenceBackground => FenceBorder.Background;

    // ── Theme ─────────────────────────────────────────────────

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.NewValue is FencePanelViewModel vm)
            ApplyTheme(vm);
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
    /// the global UseCustomFileIcons Application resource so every file item
    /// switches between custom tiles and Shell icons in place.
    /// </summary>
    public void RefreshFileTileTemplate()
    {
        FileListBox?.Items.Refresh();
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

    /// <summary>
    /// Open rename dialog (called from title bar context menu or FenceHost tab context menu).
    /// </summary>
    public void BeginRename()
    {
        if (ViewModel is null) return;
        var dialog = new RenameWindow(ViewModel.Title);
        if (dialog.ShowDialog() == true && dialog.NewName is not null)
        {
            ViewModel.Title = dialog.NewName;
            InteractionEnded?.Invoke();
        }
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
            e.Effects = DragDropEffects.Copy;
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

        var files = (string[])e.Data.GetData(DataFormats.FileDrop)!;
        foreach (var filePath in files)
        {
            ViewModel.AddFile(filePath);
            LoadIconForLastFile();
        }

        // Scale-in animation for the border on drop
        AnimateDropPulse();
        e.Handled = true;
    }

    // ── Focus glow: subscribe to host window activation ─────────

    private Window? _hostWindow;

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _hostWindow = Window.GetWindow(this);
        if (_hostWindow is null) return;
        _hostWindow.Activated += OnHostActivated;
        _hostWindow.Deactivated += OnHostDeactivated;
        if (ViewModel is not null) ViewModel.IsFocused = _hostWindow.IsActive;

        // Re-sync the title-bar row height in case ShowTitleBar was toggled
        // before the named row was reachable from the dependency-property callback.
        TitleBarRow.Height = ShowTitleBar ? new GridLength(30) : new GridLength(0);
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
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

        // Single click → select + prepare for drag
        ClearSelection();
        item.IsSelected = true;
        _dragStartPoint = e.GetPosition(this);
        _isDraggingFile = false;
    }

    private void FileItem_MouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed) return;
        if (sender is not FrameworkElement element || element.DataContext is not FileItemViewModel item)
            return;

        var currentPos = e.GetPosition(this);
        var diff = currentPos - _dragStartPoint;

        if (Math.Abs(diff.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(diff.Y) < SystemParameters.MinimumVerticalDragDistance)
            return;

        if (_isDraggingFile) return;
        _isDraggingFile = true;

        var dataObject = new DataObject(DataFormats.FileDrop, new[] { item.FilePath });
        var result = DragDrop.DoDragDrop(element, dataObject, DragDropEffects.Copy | DragDropEffects.Move);

        if (result == DragDropEffects.Move && ViewModel is not null)
        {
            ViewModel.RemoveFile(item.FilePath);
        }

        _isDraggingFile = false;
    }

    private void FileItem_MouseRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement element || element.DataContext is not FileItemViewModel item)
            return;

        ClearSelection();
        item.IsSelected = true;

        var screenPoint = element.PointToScreen(e.GetPosition(element));
        var hwnd = new WindowInteropHelper(Window.GetWindow(this)!).Handle;
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

    private void LoadIconForLastFile()
    {
        if (ViewModel is null || IconExtractor is null) return;
        var last = ViewModel.Files.LastOrDefault();
        if (last is not null && last.Icon is null)
            last.Icon = IconExtractor.GetIcon(last.FilePath);

        // Scale-in animation for newly added file item
        AnimateNewFileItem();
    }

    private void AnimateNewFileItem()
    {
        // Delay to let layout update, then animate the last item container
        Dispatcher.InvokeAsync(() =>
        {
            if (ViewModel is null || ViewModel.Files.Count == 0) return;
            var lastIndex = ViewModel.Files.Count - 1;
            var container = FileListBox.ItemContainerGenerator.ContainerFromIndex(lastIndex) as FrameworkElement;
            if (container is null) return;

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

    private void ClearSelection()
    {
        if (ViewModel is null) return;
        foreach (var file in ViewModel.Files)
            file.IsSelected = false;
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
            RollupChanged?.Invoke(false, targetHeight + 8);
        }
        else
        {
            var win = Window.GetWindow(this);
            if (win is not null)
                ViewModel.ExpandedHeight = win.Height - 8;
            ViewModel.IsRolledUp = true;
            _isHoverExpanded = false;
            RollupChanged?.Invoke(true, RolledUpHeight + 8);
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
        RollupChanged?.Invoke(false, targetHeight + 8);
    }

    public void HoverCollapse()
    {
        if (ViewModel is null || !ViewModel.IsRolledUp || !_isHoverExpanded) return;
        _isHoverExpanded = false;
        RollupChanged?.Invoke(true, RolledUpHeight + 8);
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
