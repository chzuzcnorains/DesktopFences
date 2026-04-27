using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace DesktopFences.UI.Controls.Settings;

public partial class ShortcutsSettingsPane : UserControl
{
    private record ShortcutRow(string Label, string[] Keys);
    private record ShortcutGroup(string Title, ShortcutRow[] Rows);

    /// <summary>
    /// Static list of currently-registered shortcuts; mirrors what
    /// PeekManager / SearchHotkeyManager / QuickHideManager wire up at startup.
    /// </summary>
    private static readonly ShortcutGroup[] Groups =
    [
        new("窗口", [
            new("显示 / 隐藏所有 Fence", ["双击桌面"]),
            new("Peek · 所有 Fence 临时置顶", ["Win", "Space"]),
            new("显示桌面（系统）",       ["Win", "D"]),
        ]),
        new("导航", [
            new("快捷搜索",         ["Ctrl", "`"]),
            new("退出 Peek / 搜索", ["Esc"]),
        ]),
        new("Fence 内", [
            new("重命名 Fence",   ["F2"]),
            new("删除选中文件",   ["Del"]),
            new("打开选中文件",   ["Enter"]),
            new("全选",          ["Ctrl", "A"]),
        ]),
    ];

    public ShortcutsSettingsPane()
    {
        InitializeComponent();
        BuildGroups();
    }

    private void BuildGroups()
    {
        GroupsRoot.Children.Clear();
        foreach (var group in Groups)
            GroupsRoot.Children.Add(BuildGroupCard(group));
    }

    private UIElement BuildGroupCard(ShortcutGroup group)
    {
        var card = new Border
        {
            Style = (Style)FindResource("SwCardStyle"),
            Padding = new Thickness(0),
            Margin = new Thickness(0, 0, 0, 12),
        };

        var stack = new StackPanel();
        stack.Children.Add(new Border
        {
            Padding = new Thickness(16, 12, 16, 12),
            BorderBrush = (Brush)FindResource("SwRowDividerBrush"),
            BorderThickness = new Thickness(0, 0, 0, 1),
            Child = new TextBlock
            {
                Text = group.Title,
                Style = (Style)FindResource("SwSectionHeaderStyle"),
            },
        });

        for (int i = 0; i < group.Rows.Length; i++)
            stack.Children.Add(BuildRow(group.Rows[i], i == group.Rows.Length - 1));

        card.Child = stack;
        return card;
    }

    private UIElement BuildRow(ShortcutRow row, bool isLast)
    {
        var border = new Border
        {
            Padding = new Thickness(16, 10, 16, 10),
            BorderBrush = (Brush)FindResource("SwRowDividerBrush"),
            BorderThickness = new Thickness(0, 0, 0, isLast ? 0 : 1),
        };

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var label = new TextBlock
        {
            Text = row.Label,
            Style = (Style)FindResource("SwRowLabelStyle"),
            VerticalAlignment = VerticalAlignment.Center,
        };
        Grid.SetColumn(label, 0);
        grid.Children.Add(label);

        var kbdRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center,
        };
        for (int i = 0; i < row.Keys.Length; i++)
        {
            kbdRow.Children.Add(MakeBadge(row.Keys[i]));
            if (i < row.Keys.Length - 1)
            {
                kbdRow.Children.Add(new TextBlock
                {
                    Text = "+",
                    FontSize = 11,
                    Foreground = (Brush)FindResource("TextFaintBrush"),
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(2, 0, 6, 0),
                });
            }
        }
        Grid.SetColumn(kbdRow, 1);
        grid.Children.Add(kbdRow);

        border.Child = grid;
        return border;
    }

    private UIElement MakeBadge(string key) => new Border
    {
        Style = (Style)FindResource("SwKbdBadgeStyle"),
        Child = new TextBlock
        {
            Text = key,
            Style = (Style)FindResource("SwKbdTextStyle"),
        },
    };
}
