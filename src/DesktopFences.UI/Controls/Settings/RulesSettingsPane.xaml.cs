using System.Windows;
using System.Windows.Controls;
using DesktopFences.Core.Models;

namespace DesktopFences.UI.Controls.Settings;

public partial class RulesSettingsPane : UserControl
{
    private List<ClassificationRule> _rules = new();
    private List<FenceDefinition> _fences = new();
    private int _selectedRuleIndex = -1;
    private bool _suppressEvents;

    private record MatchTypeOption(string Display, string Hint, RuleMatchType Type);

    private static readonly MatchTypeOption[] MatchTypeOptions =
    [
        new("扩展名", "逗号分隔，如 .exe,.lnk,.url", RuleMatchType.Extension),
        new("文件名通配符", "支持 * 和 ?，如 report*", RuleMatchType.NameGlob),
        new("正则表达式", "如 ^\\d{4}.*\\.pdf$", RuleMatchType.Regex),
        new("是文件夹", "匹配所有文件夹（无需填写模式）", RuleMatchType.IsDirectory),
    ];

    public RulesSettingsPane()
    {
        InitializeComponent();

        foreach (var opt in MatchTypeOptions)
            CboMatchType.Items.Add(opt);
        CboMatchType.DisplayMemberPath = "Display";
    }

    /// <summary>
    /// Bind the editor to a (deep-copied) rules list and a fence reference list.
    /// Pane keeps its own working copy and exposes <see cref="GetRules"/> on save.
    /// </summary>
    public void Initialize(List<ClassificationRule> rules, List<FenceDefinition> fences)
    {
        _fences = fences;
        _rules = rules.Select(DeepCopyRule).ToList();

        CboTargetFence.Items.Clear();
        foreach (var fence in _fences)
            CboTargetFence.Items.Add(fence);
        CboTargetFence.DisplayMemberPath = "Title";

        RefreshRuleList();
        EditPanel.Opacity = 0.4;
    }

    /// <summary>
    /// Returns the working rules list to be persisted by the host window.
    /// </summary>
    public List<ClassificationRule> GetRules() => _rules;

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
        // 找不到目标 Fence 时，不要自动选择第一个，保持 TargetFenceId 不变
        // 这样保存时会自动创建同名的 Fence

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

    // ── Event Handlers ──────────────────────────────────────────

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
}
