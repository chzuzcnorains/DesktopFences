using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using DesktopFences.Core.Models;

namespace DesktopFences.UI.Controls;

public partial class SettingsWindow : Window
{
    private readonly AppSettings _settings;

    /// <summary>
    /// Fired when settings are saved. Provides the updated settings.
    /// </summary>
    public event Action<AppSettings>? SettingsSaved;

    /// <summary>
    /// Fired when rules are saved. Provides the updated rules list.
    /// </summary>
    public event Action<List<ClassificationRule>>? RulesSaved;

    public SettingsWindow(AppSettings settings, List<ClassificationRule> rules, List<FenceDefinition> fences)
    {
        InitializeComponent();
        _settings = settings;

        PaneGeneral.Load(_settings);
        PaneRules.Initialize(rules, fences);

        // Default selection: 常规
        NavList.SelectedIndex = 0;
    }

    // ── Title Bar ─────────────────────────────────────────────

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            ToggleMaximize();
            return;
        }
        if (e.LeftButton == MouseButtonState.Pressed)
            DragMove();
    }

    private void BtnMinimize_Click(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;
    }

    private void BtnMaximize_Click(object sender, RoutedEventArgs e)
    {
        ToggleMaximize();
    }

    private void ToggleMaximize()
    {
        WindowState = WindowState == WindowState.Maximized
            ? WindowState.Normal
            : WindowState.Maximized;
    }

    // ── Tab selection ─────────────────────────────────────────

    /// <summary>
    /// Backwards-compatible tab selector. Legacy callers used 0 = settings, 1 = rules.
    /// New layout maps these to the corresponding sidebar items.
    /// </summary>
    public void SelectTab(int legacyIndex)
    {
        // 0 → general, 1 → rules; everything else falls back to general.
        var navIndex = legacyIndex switch
        {
            0 => 0, // general
            1 => 2, // rules
            _ => 0,
        };
        if (navIndex >= 0 && navIndex < NavList.Items.Count)
            NavList.SelectedIndex = navIndex;
    }

    private void NavList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (NavList.SelectedItem is not ListBoxItem item) return;
        var key = item.Tag as string;

        PaneGeneral.Visibility = Visibility.Collapsed;
        PaneAppearance.Visibility = Visibility.Collapsed;
        PaneRules.Visibility = Visibility.Collapsed;
        PaneFences.Visibility = Visibility.Collapsed;
        PaneShortcuts.Visibility = Visibility.Collapsed;
        PaneAdvanced.Visibility = Visibility.Collapsed;
        PaneAbout.Visibility = Visibility.Collapsed;

        switch (key)
        {
            case "general": PaneGeneral.Visibility = Visibility.Visible; break;
            case "appearance": PaneAppearance.Visibility = Visibility.Visible; break;
            case "rules": PaneRules.Visibility = Visibility.Visible; break;
            case "fences": PaneFences.Visibility = Visibility.Visible; break;
            case "shortcuts": PaneShortcuts.Visibility = Visibility.Visible; break;
            case "advanced": PaneAdvanced.Visibility = Visibility.Visible; break;
            case "about": PaneAbout.Visibility = Visibility.Visible; break;
            default: PaneGeneral.Visibility = Visibility.Visible; break;
        }
    }

    // ── Save / Cancel ─────────────────────────────────────────

    private void BtnSave_Click(object sender, RoutedEventArgs e)
    {
        PaneGeneral.Save(_settings);
        SettingsSaved?.Invoke(_settings);
        RulesSaved?.Invoke(PaneRules.GetRules());
        Close();
    }

    private void BtnCancel_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
