using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using DesktopFences.Core.Models;

namespace DesktopFences.UI.Controls;

public partial class SettingsWindow : Window
{
    private readonly AppSettings _settings;
    private readonly List<FenceDefinition> _fences;
    private readonly List<ClassificationRule> _rules;
    private int _selectedRuleIndex = -1;
    private bool _suppressEvents;

    /// <summary>
    /// Fired when settings are saved. Provides the updated settings.
    /// </summary>
    public event Action<AppSettings>? SettingsSaved;

    /// <summary>
    /// Fired when rules are saved. Provides the updated rules list.
    /// </summary>
    public event Action<List<ClassificationRule>>? RulesSaved;

    private record MatchTypeOption(string Display, string Hint, RuleMatchType Type);
    private record TabStyleOption(string Display, TabStyle Style);

    private static readonly MatchTypeOption[] MatchTypeOptions =
    [
        new("扩展名", "逗号分隔，如 .exe,.lnk,.url", RuleMatchType.Extension),
        new("文件名通配符", "支持 * 和 ?，如 report*", RuleMatchType.NameGlob),
        new("正则表达式", "如 ^\\d{4}.*\\.pdf$", RuleMatchType.Regex),
        new("是文件夹", "匹配所有文件夹（无需填写模式）", RuleMatchType.IsDirectory),
    ];

    private static readonly TabStyleOption[] TabStyleOptions =
    [
        new("扁平 (Flat)", TabStyle.Flat),
        new("分段 (Segmented)", TabStyle.Segmented),
        new("圆角 (Rounded)", TabStyle.Rounded),
        new("仅菜单 (Menu Only)", TabStyle.MenuOnly),
    ];

    public SettingsWindow(AppSettings settings, List<ClassificationRule> rules, List<FenceDefinition> fences)
    {
        InitializeComponent();
        _settings = settings;
        _fences = fences;
        _rules = rules.Select(DeepCopyRule).ToList();

        LoadSettingsToUI();
        InitRuleEditor();
    }

    // ── Title Bar ─────────────────────────────────────────────

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
            DragMove();
    }

    /// <summary>
    /// Switch to the specified tab by index (0 = settings, 1 = rules).
    /// </summary>
    public void SelectTab(int index)
    {
        MainTabControl.SelectedIndex = index;
    }

    // ── Settings ──────────────────────────────────────────────

    private void LoadSettingsToUI()
    {
        TxtFenceColor.Text = _settings.DefaultFenceColor;
        TxtTitleBarColor.Text = _settings.DefaultTitleBarColor;
        TxtTextColor.Text = _settings.DefaultTextColor;
        SliderOpacity.Value = _settings.DefaultOpacity;
        SliderFontSize.Value = _settings.TitleBarFontSize;
        SliderSnapThreshold.Value = _settings.SnapThreshold;
        ChkQuickHide.IsChecked = _settings.QuickHideEnabled;
        ChkStartWithWindows.IsChecked = _settings.StartWithWindows;
        ChkStartMinimized.IsChecked = _settings.StartMinimized;
        ChkCompatibilityMode.IsChecked = _settings.CompatibilityMode;
        ChkDebugLogging.IsChecked = _settings.DebugLogging;

        // Tab style
        CboTabStyle.Items.Clear();
        foreach (var opt in TabStyleOptions)
            CboTabStyle.Items.Add(opt);
        CboTabStyle.DisplayMemberPath = "Display";
        foreach (TabStyleOption opt in CboTabStyle.Items)
        {
            if (opt.Style == _settings.TabStyle)
            {
                CboTabStyle.SelectedItem = opt;
                break;
            }
        }
        if (CboTabStyle.SelectedIndex < 0 && CboTabStyle.Items.Count > 0)
            CboTabStyle.SelectedIndex = 0;
    }

    private void SaveSettingsFromUI()
    {
        _settings.DefaultFenceColor = TxtFenceColor.Text;
        _settings.DefaultTitleBarColor = TxtTitleBarColor.Text;
        _settings.DefaultTextColor = TxtTextColor.Text;
        _settings.DefaultOpacity = SliderOpacity.Value;
        _settings.TitleBarFontSize = SliderFontSize.Value;
        _settings.SnapThreshold = (int)SliderSnapThreshold.Value;
        _settings.QuickHideEnabled = ChkQuickHide.IsChecked == true;
        _settings.StartWithWindows = ChkStartWithWindows.IsChecked == true;
        _settings.StartMinimized = ChkStartMinimized.IsChecked == true;
        _settings.CompatibilityMode = ChkCompatibilityMode.IsChecked == true;
        _settings.DebugLogging = ChkDebugLogging.IsChecked == true;

        if (CboTabStyle.SelectedItem is TabStyleOption tabOpt)
            _settings.TabStyle = tabOpt.Style;
    }

    private void CboTabStyle_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // No-op during load; value saved on Save button click
    }

    // ── Rule Editor ───────────────────────────────────────────

    private void InitRuleEditor()
    {
        foreach (var opt in MatchTypeOptions)
            CboMatchType.Items.Add(opt);
        CboMatchType.DisplayMemberPath = "Display";

        foreach (var fence in _fences)
            CboTargetFence.Items.Add(fence);
        CboTargetFence.DisplayMemberPath = "Title";

        RefreshRuleList();
        EditPanel.Opacity = 0.4;
    }

    private static ClassificationRule DeepCopyRule(ClassificationRule r) => new()
    {
        Id = r.Id,
        Name = r.Name,
        Priority = r.Priority,
        IsEnabled = r.IsEnabled,
        TargetFenceId = r.TargetFenceId,
        Condition = new RuleCondition
        {
            MatchType = r.Condition.MatchType,
            Pattern = r.Condition.Pattern,
            DateFrom = r.Condition.DateFrom,
            DateTo = r.Condition.DateTo,
            MinSizeBytes = r.Condition.MinSizeBytes,
            MaxSizeBytes = r.Condition.MaxSizeBytes
        }
    };

    private void RefreshRuleList()
    {
        _suppressEvents = true;
        RulesListBox.ItemsSource = null;
        RulesListBox.ItemsSource = _rules;
        if (_selectedRuleIndex >= 0 && _selectedRuleIndex < _rules.Count)
            RulesListBox.SelectedIndex = _selectedRuleIndex;
        _suppressEvents = false;
    }

    private void PopulateRuleForm(ClassificationRule rule)
    {
        _suppressEvents = true;

        TxtRuleName.Text = rule.Name;
        ChkRuleEnabled.IsChecked = rule.IsEnabled;
        TxtPriority.Text = rule.Priority.ToString();
        TxtPattern.Text = rule.Condition.Pattern;

        foreach (MatchTypeOption opt in CboMatchType.Items)
        {
            if (opt.Type == rule.Condition.MatchType)
            {
                CboMatchType.SelectedItem = opt;
                break;
            }
        }

        if (CboMatchType.SelectedIndex < 0 && CboMatchType.Items.Count > 0)
            CboMatchType.SelectedIndex = 0;

        CboTargetFence.SelectedItem = null;
        foreach (FenceDefinition fence in CboTargetFence.Items)
        {
            if (fence.Id == rule.TargetFenceId)
            {
                CboTargetFence.SelectedItem = fence;
                break;
            }
        }
        if (CboTargetFence.SelectedIndex < 0 && CboTargetFence.Items.Count > 0)
            CboTargetFence.SelectedIndex = 0;

        UpdatePatternHint();
        _suppressEvents = false;
    }

    private void ApplyFormToRule()
    {
        if (_selectedRuleIndex < 0 || _selectedRuleIndex >= _rules.Count) return;
        var rule = _rules[_selectedRuleIndex];

        rule.Name = TxtRuleName.Text;
        rule.IsEnabled = ChkRuleEnabled.IsChecked == true;
        rule.Priority = int.TryParse(TxtPriority.Text, out int p) ? p : 0;
        rule.Condition.Pattern = TxtPattern.Text;

        if (CboMatchType.SelectedItem is MatchTypeOption matchOpt)
            rule.Condition.MatchType = matchOpt.Type;

        if (CboTargetFence.SelectedItem is FenceDefinition fence)
            rule.TargetFenceId = fence.Id;
    }

    private void UpdatePatternHint()
    {
        if (CboMatchType.SelectedItem is MatchTypeOption opt)
        {
            bool isDirectory = opt.Type == RuleMatchType.IsDirectory;
            PatternRow.Visibility = isDirectory ? Visibility.Collapsed : Visibility.Visible;
            TxtPatternHint.Text = opt.Hint;
        }
        else
        {
            PatternRow.Visibility = Visibility.Visible;
            TxtPatternHint.Text = string.Empty;
        }
    }

    // ── Rule Event Handlers ──────────────────────────────────

    private void RulesListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressEvents) return;
        _selectedRuleIndex = RulesListBox.SelectedIndex;
        if (_selectedRuleIndex >= 0 && _selectedRuleIndex < _rules.Count)
        {
            PopulateRuleForm(_rules[_selectedRuleIndex]);
            EditPanel.IsEnabled = true;
            EditPanel.Opacity = 1.0;
        }
        else
        {
            EditPanel.IsEnabled = false;
            EditPanel.Opacity = 0.4;
        }
    }

    private void TxtRuleName_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_suppressEvents) return;
        ApplyFormToRule();
        RefreshRuleList();
    }

    private void RuleFieldChanged(object sender, EventArgs e)
    {
        if (_suppressEvents) return;
        ApplyFormToRule();
    }

    private void CboMatchType_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressEvents) return;
        UpdatePatternHint();
        ApplyFormToRule();
    }

    private void BtnAddRule_Click(object sender, RoutedEventArgs e)
    {
        var targetId = _fences.Count > 0 ? _fences[0].Id : Guid.Empty;
        var rule = new ClassificationRule
        {
            Name = "新规则",
            Priority = _rules.Count > 0 ? _rules.Max(r => r.Priority) + 1 : 1,
            IsEnabled = true,
            TargetFenceId = targetId,
            Condition = new RuleCondition { MatchType = RuleMatchType.Extension, Pattern = "" }
        };
        _rules.Add(rule);
        _selectedRuleIndex = _rules.Count - 1;
        RefreshRuleList();
        PopulateRuleForm(rule);
        EditPanel.IsEnabled = true;
        EditPanel.Opacity = 1.0;
        TxtRuleName.Focus();
        TxtRuleName.SelectAll();
    }

    private void BtnDeleteRule_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedRuleIndex < 0 || _selectedRuleIndex >= _rules.Count) return;
        _rules.RemoveAt(_selectedRuleIndex);
        _selectedRuleIndex = Math.Min(_selectedRuleIndex, _rules.Count - 1);
        RefreshRuleList();
        if (_selectedRuleIndex >= 0)
            PopulateRuleForm(_rules[_selectedRuleIndex]);
        else
        {
            EditPanel.IsEnabled = false;
            EditPanel.Opacity = 0.4;
        }
    }

    // ── Save / Cancel ─────────────────────────────────────────

    private void BtnSave_Click(object sender, RoutedEventArgs e)
    {
        SaveSettingsFromUI();
        SettingsSaved?.Invoke(_settings);
        RulesSaved?.Invoke(_rules);
        Close();
    }

    private void BtnCancel_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
