using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using DesktopFences.Core.Models;
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
    private TabStyle _tabStyle = TabStyle.Flat;


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
            FenceContent.UpdateRollupArrow();
            UpdateTabRollupIcon(true);
        }

        FenceContent.InteractionEnded += OnInteractionEnded;
        FenceContent.RollupChanged += OnRollupChanged;
        FenceContent.CloseRequested += AnimateClose;
        FenceContent.TabMenuSwitchRequested += OnTabMenuSwitch;
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
    public void AddTab(FencePanelViewModel vm, bool activate = true)
    {
        bool wasTabbed = _tabs.Count > 1;
        _tabs.Add(vm);

        if (activate)
        {
            _activeTabIndex = _tabs.Count - 1;
            ActivatePanelForTab(_activeTabIndex);
        }

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

        // Tab switch fade animation (150ms)
        if (FenceContent.IsLoaded && FenceContent.Opacity > 0)
        {
            var fadeOut = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(75))
            {
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn }
            };
            fadeOut.Completed += (_, _) =>
            {
                FenceContent.DataContext = vm;
                DataContext = vm;
                if (_iconExtractor is not null)
                    FenceContent.IconExtractor = _iconExtractor;
                FenceContent.LoadAllIcons();
                SyncTabStripBackground();

                var fadeIn = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(75))
                {
                    EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
                };
                FenceContent.BeginAnimation(OpacityProperty, fadeIn);
            };
            FenceContent.BeginAnimation(OpacityProperty, fadeOut);
        }
        else
        {
            FenceContent.DataContext = vm;
            DataContext = vm;
            if (_iconExtractor is not null)
                FenceContent.IconExtractor = _iconExtractor;
            FenceContent.LoadAllIcons();
            SyncTabStripBackground();
        }
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

    /// <summary>
    /// Set the tab style and refresh the strip.
    /// </summary>
    public void SetTabStyle(TabStyle style)
    {
        _tabStyle = style;
        RefreshTabStrip();
    }

    private void RefreshTabStrip()
    {
        bool showTabs = _tabs.Count > 1;

        // MenuOnly: hide tab strip, show title bar with dropdown arrow
        if (_tabStyle == TabStyle.MenuOnly && showTabs)
        {
            TabRow.Height = new GridLength(0);
            FenceContent.ShowTitleBar = true;
            TabStrip.Items.Clear();
            // Populate tab info for the title bar menu
            FenceContent.MenuOnlyTabs = _tabs.Select((t, i) =>
                (t.Title, i, i == _activeTabIndex)).ToList();
            return;
        }

        // Clear MenuOnly data when not in MenuOnly mode
        FenceContent.MenuOnlyTabs = null;

        TabRow.Height = showTabs ? new GridLength(28) : new GridLength(0);
        FenceContent.ShowTitleBar = !showTabs;

        TabStrip.Items.Clear();
        if (!showTabs) return;

        for (int i = 0; i < _tabs.Count; i++)
        {
            var idx = i;
            bool isActive = i == _activeTabIndex;
            bool isLast = i == _tabs.Count - 1;

            var btn = new Button
            {
                Content = _tabs[i].Title,
                Foreground = isActive
                    ? new SolidColorBrush(Color.FromArgb(0xFF, 0xEE, 0xEE, 0xEE))
                    : new SolidColorBrush(Color.FromArgb(0xAA, 0xCC, 0xCC, 0xCC)),
                Cursor = Cursors.Hand,
            };

            // Apply style based on current tab style
            var (activeKey, inactiveKey) = _tabStyle switch
            {
                TabStyle.Segmented => ("SegmentedTabButtonActiveStyle", "SegmentedTabButtonStyle"),
                TabStyle.Rounded => ("RoundedTabButtonActiveStyle", "RoundedTabButtonStyle"),
                _ => ("FlatTabButtonActiveStyle", "FlatTabButtonStyle") // Flat default
            };

            var styleKey = isActive ? activeKey : inactiveKey;
            if (TryFindResource(styleKey) is Style tabBtnStyle)
            {
                btn.Style = tabBtnStyle;
            }
            else
            {
                // Fallback inline styling
                btn.Padding = new Thickness(10, 0, 10, 0);
                btn.Height = 26;
                btn.BorderThickness = new Thickness(0);
                btn.FontSize = 13;
                btn.VerticalAlignment = VerticalAlignment.Bottom;
            }

            btn.Background = isActive
                ? new SolidColorBrush(Color.FromArgb(0x33, 0x88, 0x88, 0x88))
                : Brushes.Transparent;

            // For segmented style: remove right border on last item
            if (_tabStyle == TabStyle.Segmented && isLast)
                btn.BorderThickness = new Thickness(0);

            btn.Click += (_, _) =>
            {
                _activeTabIndex = idx;
                ActivatePanelForTab(idx);
                RefreshTabStrip();
            };

            TabStrip.Items.Add(btn);
        }

        // Apply segmented container rounding
        if (_tabStyle == TabStyle.Segmented)
        {
            TabStripBorder.CornerRadius = new CornerRadius(6, 6, 0, 0);
        }
        else
        {
            TabStripBorder.CornerRadius = new CornerRadius(8, 8, 0, 0);
        }
    }

    private void TabMenuButton_Click(object sender, RoutedEventArgs e)
    {
        var menu = new ContextMenu();
        FencePanel.ApplyDarkContextMenuStyle(menu);

        var renameItem = new MenuItem { Header = "重命名" };
        renameItem.Click += (_, _) =>
        {
            FenceContent.BeginRename();
            RefreshTabStrip();
        };
        menu.Items.Add(renameItem);

        if (_tabs.Count > 1)
        {
            var detachItem = new MenuItem
            {
                Header = "分离为独立 Fence",
                Icon = FencePanel.BuildMenuIcon("IconSplit"),
            };
            detachItem.Click += (_, _) => TabDetachRequested?.Invoke(_tabs[_activeTabIndex]);
            menu.Items.Add(detachItem);
        }

        menu.Items.Add(new Separator());

        // Folder Portal options (delegate to FencePanel)
        if (ViewModel.IsPortalMode)
        {
            var changePortalItem = new MenuItem
            {
                Header = $"更改映射文件夹 ({ViewModel.PortalPath})",
                Icon = FencePanel.BuildMenuIcon("IconPortal"),
            };
            changePortalItem.Click += (_, _) => FenceContent.ShowTitleBarMenu(TabMenuButton);
            menu.Items.Add(changePortalItem);
        }
        else
        {
            var setPortalItem = new MenuItem
            {
                Header = "设为文件夹映射...",
                Icon = FencePanel.BuildMenuIcon("IconPortal"),
            };
            setPortalItem.Click += (_, _) => FenceContent.ShowTitleBarMenu(TabMenuButton);
            menu.Items.Add(setPortalItem);
        }

        menu.Items.Add(new Separator());

        var closeItem = new MenuItem
        {
            Header = "关闭 Fence",
            Icon = FencePanel.BuildMenuIcon("IconTrash"),
        };
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

    private void OnTabMenuSwitch(int tabIndex)
    {
        if (tabIndex >= 0 && tabIndex < _tabs.Count && tabIndex != _activeTabIndex)
        {
            _activeTabIndex = tabIndex;
            ActivatePanelForTab(tabIndex);
            RefreshTabStrip();
        }
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

    private void TabRollupToggleButton_Click(object sender, RoutedEventArgs e)
    {
        // Delegate to FencePanel's ToggleRollup via the public toggle method
        FenceContent.ToggleRollupFromHost();
    }

    private void OnRollupChanged(bool isRolledUp, double targetHeight)
    {
        var animation = new DoubleAnimation
        {
            To = targetHeight,
            Duration = TimeSpan.FromMilliseconds(200),
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseInOut }
        };
        BeginAnimation(HeightProperty, animation);

        // Sync arrow state on both title bar and tab strip
        FenceContent.UpdateRollupArrow();
        UpdateTabRollupIcon(isRolledUp);
    }

    private void UpdateTabRollupIcon(bool isRolledUp)
    {
        if (TabRollupIcon.RenderTransform is RotateTransform rt)
            rt.Angle = isRolledUp ? 180 : 0;
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
