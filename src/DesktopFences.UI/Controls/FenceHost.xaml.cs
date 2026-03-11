using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using DesktopFences.Shell.Desktop;
using DesktopFences.UI.ViewModels;

namespace DesktopFences.UI.Controls;

public partial class FenceHost : Window
{
    private readonly DesktopEmbedManager _embedManager;
    private readonly ShellIconExtractor? _iconExtractor;
    private readonly List<FencePanelViewModel> _tabs = [];
    private int _activeTabIndex;
    private bool _isClosing;


    /// <summary>
    /// Set to true before closing this host as part of a merge operation,
    /// so the Closed handler skips page/portal cleanup for tabs that moved elsewhere.
    /// </summary>
    public bool IsMerging { get; set; }

    /// <summary>
    /// Raised when the user right-clicks a tab and selects "Detach".
    /// The caller should remove the VM from this host and spawn a new window.
    /// </summary>
    public event Action<FencePanelViewModel>? TabDetachRequested;

public FenceHost(DesktopEmbedManager embedManager, FencePanelViewModel viewModel,
                     ShellIconExtractor? iconExtractor = null)
    {
        InitializeComponent();

        _embedManager = embedManager;
        _iconExtractor = iconExtractor;

        // Add first tab
        _tabs.Add(viewModel);
        _activeTabIndex = 0;
        ActivatePanelForTab(0);
        RefreshTabStrip();

        // Sync window geometry from ViewModel
        Left = viewModel.X;
        Top = viewModel.Y;
        Width = viewModel.Width + 8;   // +8 for Margin="4" on each side
        Height = viewModel.Height + 8;

        // If fence was saved in rolled-up state, apply it
        if (viewModel.IsRolledUp)
        {
            viewModel.ExpandedHeight = viewModel.Height;
            Height = 38 + 8; // RolledUpHeight + margin
        }

        FenceContent.InteractionEnded += OnInteractionEnded;
        FenceContent.RollupChanged += OnRollupChanged;
        FenceContent.CloseRequested += AnimateClose;
        TabStripBorder.MouseLeftButtonDown += TabStripBorder_MouseLeftButtonDown;
        MouseEnter += OnMouseEnter;
        MouseLeave += OnMouseLeave;
        Loaded += OnLoaded;
        Closed += OnClosed;
    }

    // ── Public API ──────────────────────────────────────────

    public FencePanelViewModel ViewModel => _tabs[_activeTabIndex];
    public IReadOnlyList<FencePanelViewModel> Tabs => _tabs;
    public FencePanel Panel => FenceContent;

    /// <summary>
    /// Add a tab from another fence being merged into this host.
    /// Grows the window by 28px when the tab strip first appears.
    /// </summary>
    public void AddTab(FencePanelViewModel vm)
    {
        bool wasTabbed = _tabs.Count > 1;
        _tabs.Add(vm);
        _activeTabIndex = _tabs.Count - 1;
        ActivatePanelForTab(_activeTabIndex);

        if (!wasTabbed)
            Height += 28; // first tab strip appearance

        RefreshTabStrip();
    }

    /// <summary>
    /// Switch to the tab whose ViewModel has the given fence ID.
    /// </summary>
    public void ActivateTab(Guid fenceId)
    {
        var idx = _tabs.FindIndex(t => t.Id == fenceId);
        if (idx >= 0 && idx != _activeTabIndex)
        {
            _activeTabIndex = idx;
            ActivatePanelForTab(idx);
            RefreshTabStrip();
        }
    }

    /// <summary>
    /// Remove the tab at <paramref name="index"/> and return its ViewModel.
    /// Shrinks the window by 28px when the tab strip disappears.
    /// </summary>
    public FencePanelViewModel RemoveTab(int index)
    {
        bool wasTabbed = _tabs.Count > 1;
        var vm = _tabs[index];
        _tabs.RemoveAt(index);

        if (_activeTabIndex >= _tabs.Count)
            _activeTabIndex = _tabs.Count - 1;

        if (wasTabbed && _tabs.Count == 1)
            Height -= 28; // tab strip hidden

        if (_tabs.Count > 0)
            ActivatePanelForTab(_activeTabIndex);

        RefreshTabStrip();
        return vm;
    }

    // ── Tab strip ────────────────────────────────────────────

    private void ActivatePanelForTab(int index)
    {
        var vm = _tabs[index];
        FenceContent.DataContext = vm;
        DataContext = vm;
        if (_iconExtractor is not null)
            FenceContent.IconExtractor = _iconExtractor;
        FenceContent.LoadAllIcons();
        SyncTabStripBackground();
    }

    /// <summary>
    /// Sync tab strip background with the panel's fence body background.
    /// </summary>
    public void SyncTabStripBackground()
    {
        var bg = FenceContent.FenceBackground;
        if (bg is not null)
            TabStripBorder.Background = bg;
    }

    private void RefreshTabStrip()
    {
        bool showTabs = _tabs.Count > 1;
        TabRow.Height = showTabs ? new GridLength(28) : new GridLength(0);
        FenceContent.ShowTitleBar = !showTabs;

        TabStrip.Items.Clear();
        if (!showTabs) return;

        for (int i = 0; i < _tabs.Count; i++)
        {
            var idx = i;
            bool isActive = i == _activeTabIndex;

            var btn = new Button
            {
                Content = _tabs[i].Title,
                Padding = new Thickness(10, 0, 10, 0),
                Height = 26,
                BorderThickness = new Thickness(0),
                Background = isActive
                    ? new SolidColorBrush(Color.FromArgb(0x99, 0x44, 0x66, 0x99))
                    : new SolidColorBrush(Color.FromArgb(0x33, 0x88, 0x88, 0x88)),
                Foreground = isActive
                    ? new SolidColorBrush(Color.FromArgb(0xFF, 0xEE, 0xEE, 0xEE))
                    : new SolidColorBrush(Color.FromArgb(0xAA, 0xCC, 0xCC, 0xCC)),
                FontSize = 13,
                Cursor = System.Windows.Input.Cursors.Hand,
                VerticalAlignment = VerticalAlignment.Bottom,
            };

            btn.Click += (_, _) =>
            {
                _activeTabIndex = idx;
                ActivatePanelForTab(idx);
                RefreshTabStrip();
            };

            TabStrip.Items.Add(btn);
        }
    }

    private void TabMenuButton_Click(object sender, RoutedEventArgs e)
    {
        var menu = new ContextMenu();

        var renameItem = new MenuItem { Header = "重命名" };
        renameItem.Click += (_, _) =>
        {
            FenceContent.BeginRename();
            RefreshTabStrip();
        };
        menu.Items.Add(renameItem);

        if (_tabs.Count > 1)
        {
            var detachItem = new MenuItem { Header = "分离为独立 Fence" };
            detachItem.Click += (_, _) => TabDetachRequested?.Invoke(_tabs[_activeTabIndex]);
            menu.Items.Add(detachItem);
        }

        menu.Items.Add(new Separator());

        // Folder Portal options (delegate to FencePanel)
        if (ViewModel.IsPortalMode)
        {
            var changePortalItem = new MenuItem { Header = $"更改映射文件夹 ({ViewModel.PortalPath})" };
            changePortalItem.Click += (_, _) => FenceContent.ShowTitleBarMenu(TabMenuButton);
            menu.Items.Add(changePortalItem);
        }
        else
        {
            var setPortalItem = new MenuItem { Header = "设为文件夹映射..." };
            setPortalItem.Click += (_, _) => FenceContent.ShowTitleBarMenu(TabMenuButton);
            menu.Items.Add(setPortalItem);
        }

        menu.Items.Add(new Separator());

        var closeItem = new MenuItem { Header = "关闭 Fence" };
        closeItem.Click += (_, _) =>
        {
            if (_tabs.Count > 1)
            {
                RemoveTab(_activeTabIndex);
                RefreshTabStrip();
            }
            else
            {
                AnimateClose();
            }
        };
        menu.Items.Add(closeItem);

        menu.PlacementTarget = sender as UIElement;
        menu.IsOpen = true;
    }

    // ── Tab strip drag to move window ──────────────────────

    private void TabStripBorder_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 1)
        {
            FenceContent.RaiseInteractionStarted();
            DragMove();
            OnInteractionEnded();
        }
    }

    // ── Window lifecycle ─────────────────────────────────────

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        var helper = new WindowInteropHelper(this);
        _embedManager.RegisterWindow(helper.Handle);

        // Fade-in animation on create
        FenceContent.AnimateFadeIn();
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        var helper = new WindowInteropHelper(this);
        _embedManager.UnregisterWindow(helper.Handle);
    }

    /// <summary>
    /// Animate closing: fade out then close.
    /// </summary>
    public void AnimateClose()
    {
        if (_isClosing) return;
        _isClosing = true;
        FenceContent.AnimateFadeOut(() => Close());
    }

    private void OnInteractionEnded()
    {
        var tabStripHeight = _tabs.Count > 1 ? 28.0 : 0.0;
        var w = Math.Max(Width - 8, FencePanelViewModel.MinWidth);
        var h = Math.Max(Height - 8 - tabStripHeight, FencePanelViewModel.MinHeight);

        foreach (var tab in _tabs)
        {
            tab.X = Left;
            tab.Y = Top;
            tab.Width = w;
            tab.Height = h;
        }
    }

    // ── Rollup ───────────────────────────────────────────────

    private void OnRollupChanged(bool isRolledUp, double targetHeight)
    {
        var animation = new DoubleAnimation
        {
            To = targetHeight,
            Duration = TimeSpan.FromMilliseconds(200),
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseInOut }
        };
        BeginAnimation(HeightProperty, animation);
    }

    private void OnMouseEnter(object sender, System.Windows.Input.MouseEventArgs e)
    {
        FenceContent.HoverExpand();
    }

    private void OnMouseLeave(object sender, System.Windows.Input.MouseEventArgs e)
    {
        FenceContent.HoverCollapse();
    }
}
