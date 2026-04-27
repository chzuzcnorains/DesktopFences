using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using DesktopFences.Core.Models;
using DesktopFences.UI.Controls.Settings;

namespace DesktopFences.UI.Controls;

public partial class SettingsWindow : Window
{
    private readonly AppSettings _settings;

    /// <summary>Fired when settings are saved. Provides the updated settings.</summary>
    public event Action<AppSettings>? SettingsSaved;

    /// <summary>Fired when rules are saved. Provides the updated rules list.</summary>
    public event Action<List<ClassificationRule>>? RulesSaved;

    /// <summary>Fence-management actions bubbled from FencesManageSettingsPane.</summary>
    public event Action? NewFenceRequested;
    public event Action? SaveSnapshotRequested;
    public event Action? ExportLayoutRequested;
    public event Action? ImportLayoutRequested;
    public event Action<Guid>? RestoreClosedFenceRequested;

    /// <summary>Danger-zone actions bubbled from AdvancedSettingsPane.</summary>
    public event Action? ResetLayoutRequested;
    public event Action? ClearRulesRequested;
    public event Action? RestoreDefaultsRequested;

    public SettingsWindow(AppSettings settings,
                          List<ClassificationRule> rules,
                          List<FenceDefinition> fences,
                          IEnumerable<FencesManageSettingsPane.ClosedFenceRecord>? closedFences = null,
                          int managedFileCount = 0,
                          TimeSpan? uptime = null)
    {
        InitializeComponent();
        _settings = settings;

        PaneGeneral.Load(_settings);
        PaneAppearance.Load(_settings);
        PaneAdvanced.Load(_settings);
        PaneRules.Initialize(rules, fences);
        PaneFences.Initialize(fences, closedFences ?? []);
        PaneAbout.Initialize(
            activeFenceCount: CountActiveGroups(fences),
            managedFileCount: managedFileCount,
            ruleCount: rules.Count,
            uptime: uptime ?? TimeSpan.Zero);

        WireSubpaneEvents();

        NavList.SelectedIndex = 0;
    }

    /// <summary>
    /// Counts distinct fence groups (a tab group counts as 1 even if it has multiple fences),
    /// matching what the FencesManage pane and the about-tab "活动 Fence" stat report.
    /// </summary>
    private static int CountActiveGroups(IEnumerable<FenceDefinition> fences) =>
        fences.Select(f => f.TabGroupId ?? f.Id).Distinct().Count();

    private void WireSubpaneEvents()
    {
        PaneFences.NewFenceRequested            += () => NewFenceRequested?.Invoke();
        PaneFences.SaveSnapshotRequested        += () => SaveSnapshotRequested?.Invoke();
        PaneFences.ExportLayoutRequested        += () => ExportLayoutRequested?.Invoke();
        PaneFences.ImportLayoutRequested        += () => ImportLayoutRequested?.Invoke();
        PaneFences.RestoreClosedFenceRequested  += id => RestoreClosedFenceRequested?.Invoke(id);

        PaneAdvanced.ResetLayoutRequested       += () => ResetLayoutRequested?.Invoke();
        PaneAdvanced.ClearRulesRequested        += () => ClearRulesRequested?.Invoke();
        PaneAdvanced.RestoreDefaultsRequested   += () => RestoreDefaultsRequested?.Invoke();
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
        => WindowState = WindowState.Minimized;

    private void BtnMaximize_Click(object sender, RoutedEventArgs e) => ToggleMaximize();

    private void ToggleMaximize()
    {
        WindowState = WindowState == WindowState.Maximized
            ? WindowState.Normal
            : WindowState.Maximized;
    }

    // ── Tab selection ─────────────────────────────────────────

    /// <summary>
    /// Backwards-compatible tab selector. Legacy callers used 0 = settings, 1 = rules.
    /// </summary>
    public void SelectTab(int legacyIndex)
    {
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
        PaneAppearance.Save(_settings);
        PaneAdvanced.Save(_settings);
        SettingsSaved?.Invoke(_settings);
        RulesSaved?.Invoke(PaneRules.GetRules());
        Close();
    }

    private void BtnCancel_Click(object sender, RoutedEventArgs e) => Close();
}
