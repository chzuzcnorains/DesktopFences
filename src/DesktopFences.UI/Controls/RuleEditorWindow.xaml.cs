using System.Windows;
using System.Windows.Controls;
using DesktopFences.Core.Models;

namespace DesktopFences.UI.Controls;

public partial class RuleEditorWindow : Window
{
    private readonly List<FenceDefinition> _fences;
    private readonly List<ClassificationRule> _rules;
    private int _selectedIndex = -1;
    private bool _suppressEvents;

    public event Action<List<ClassificationRule>>? RulesSaved;

    private record MatchTypeOption(string Display, string Hint, RuleMatchType Type);

    private static readonly MatchTypeOption[] MatchTypeOptions =
    [
        new("扩展名", "逗号分隔，如 .exe,.lnk,.url", RuleMatchType.Extension),
        new("文件名通配符", "支持 * 和 ?，如 report*", RuleMatchType.NameGlob),
        new("正则表达式", "如 ^\\d{4}.*\\.pdf$", RuleMatchType.Regex),
        new("是文件夹", "匹配所有文件夹（无需填写模式）", RuleMatchType.IsDirectory),
    ];

    public RuleEditorWindow(List<ClassificationRule> rules, List<FenceDefinition> fences)
    {
        InitializeComponent();
        _fences = fences;
        _rules = rules.Select(DeepCopy).ToList();

        foreach (var opt in MatchTypeOptions)
            CboMatchType.Items.Add(opt);
        CboMatchType.DisplayMemberPath = "Display";

        foreach (var fence in _fences)
            CboTargetFence.Items.Add(fence);
        CboTargetFence.DisplayMemberPath = "Title";

        RefreshList();
        EditPanel.Opacity = 0.4;
    }

    private static ClassificationRule DeepCopy(ClassificationRule r) => new()
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

    private void RefreshList()
    {
        _suppressEvents = true;
        RulesListBox.ItemsSource = null;
        RulesListBox.ItemsSource = _rules;
        if (_selectedIndex >= 0 && _selectedIndex < _rules.Count)
            RulesListBox.SelectedIndex = _selectedIndex;
        _suppressEvents = false;
    }

    private void PopulateForm(ClassificationRule rule)
    {
        _suppressEvents = true;

        TxtName.Text = rule.Name;
        ChkEnabled.IsChecked = rule.IsEnabled;
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
        if (_selectedIndex < 0 || _selectedIndex >= _rules.Count) return;
        var rule = _rules[_selectedIndex];

        rule.Name = TxtName.Text;
        rule.IsEnabled = ChkEnabled.IsChecked == true;
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

    // ── Event Handlers ────────────────────────────────────────

    private void RulesListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressEvents) return;
        _selectedIndex = RulesListBox.SelectedIndex;
        if (_selectedIndex >= 0 && _selectedIndex < _rules.Count)
        {
            PopulateForm(_rules[_selectedIndex]);
            EditPanel.IsEnabled = true;
            EditPanel.Opacity = 1.0;
        }
        else
        {
            EditPanel.IsEnabled = false;
            EditPanel.Opacity = 0.4;
        }
    }

    private void TxtName_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_suppressEvents) return;
        ApplyFormToRule();
        RefreshList();
    }

    private void FieldChanged(object sender, EventArgs e)
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

    private void BtnAdd_Click(object sender, RoutedEventArgs e)
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
        _selectedIndex = _rules.Count - 1;
        RefreshList();
        PopulateForm(rule);
        EditPanel.IsEnabled = true;
        EditPanel.Opacity = 1.0;
        TxtName.Focus();
        TxtName.SelectAll();
    }

    private void BtnDelete_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedIndex < 0 || _selectedIndex >= _rules.Count) return;
        _rules.RemoveAt(_selectedIndex);
        _selectedIndex = Math.Min(_selectedIndex, _rules.Count - 1);
        RefreshList();
        if (_selectedIndex >= 0)
            PopulateForm(_rules[_selectedIndex]);
        else
        {
            EditPanel.IsEnabled = false;
            EditPanel.Opacity = 0.4;
        }
    }

    private void BtnSave_Click(object sender, RoutedEventArgs e)
    {
        RulesSaved?.Invoke(_rules);
        Close();
    }

    private void BtnCancel_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
