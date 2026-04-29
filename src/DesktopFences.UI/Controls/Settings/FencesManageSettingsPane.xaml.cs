using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using DesktopFences.Core.Models;

namespace DesktopFences.UI.Controls.Settings;

public partial class FencesManageSettingsPane : UserControl
{
    /// <summary>
    /// One closed-fence record (rendered in the 最近关闭 grid).
    /// </summary>
    public record ClosedFenceRecord(
        Guid Id,
        string Title,
        IReadOnlyList<string> TabTitles,
        int FileCount,
        DateTimeOffset ClosedAt);

    public event Action? NewFenceRequested;
    public event Action? SaveSnapshotRequested;
    public event Action? ExportLayoutRequested;
    public event Action? ImportLayoutRequested;
    public event Action<Guid>? RestoreClosedFenceRequested;
    public event Action<Guid>? DeleteClosedFenceRequested;

    private List<FenceDefinition> _activeFences = [];
    private List<ClosedFenceRecord> _closedFences = [];

    public FencesManageSettingsPane()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Populate both segments. Called once when SettingsWindow opens.
    /// </summary>
    public void Initialize(IEnumerable<FenceDefinition> activeFences,
                           IEnumerable<ClosedFenceRecord> closedFences)
    {
        _activeFences = activeFences.ToList();
        _closedFences = closedFences.ToList();

        SegActiveCount.Text = _activeFences.Count.ToString();
        SegClosedCount.Text = _closedFences.Count.ToString();

        BuildActiveRows();
        BuildClosedGrid();
    }

    private void BuildActiveRows()
    {
        var rows = new List<UIElement>();

        // Group by TabGroupId so a tab group renders as one row (matches FenceHost reality).
        var groups = _activeFences
            .GroupBy(f => f.TabGroupId ?? f.Id)
            .ToList();

        foreach (var group in groups)
        {
            var tabs = group.OrderBy(g => g.TabOrder).ToList();
            var primary = tabs[0];
            var totalFiles = tabs.Sum(t => t.FilePaths.Count);

            var row = new Border
            {
                Padding = new Thickness(14, 10, 14, 10),
                BorderBrush = new SolidColorBrush(Color.FromArgb(0x08, 0xFF, 0xFF, 0xFF)),
                BorderThickness = new Thickness(0, 0, 0, 1),
                Background = Brushes.Transparent,
            };

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(60) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(100) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(100) });

            var titleText = new TextBlock
            {
                Text = primary.Title,
                FontSize = 13,
                FontWeight = FontWeights.Medium,
                Foreground = (Brush)FindResource("TextPrimaryBrush"),
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
            };
            if (primary.IsRolledUp)
            {
                titleText.Inlines.Add(new System.Windows.Documents.Run("  (已折叠)")
                {
                    FontSize = 11,
                    Foreground = (Brush)FindResource("TextFaintBrush"),
                });
            }
            Grid.SetColumn(titleText, 0);
            grid.Children.Add(titleText);

            grid.Children.Add(MakeCell(tabs.Count.ToString(), 1));
            grid.Children.Add(MakeCell(totalFiles.ToString(), 2));
            grid.Children.Add(MakeMonoCell($"{(int)primary.Bounds.X},{(int)primary.Bounds.Y}", 3));
            grid.Children.Add(MakeMonoCell($"{(int)primary.Bounds.Width}×{(int)primary.Bounds.Height}", 4));

            row.Child = grid;
            rows.Add(row);
        }

        ActiveRows.Items.Clear();
        foreach (var r in rows) ActiveRows.Items.Add(r);
        ActiveEmpty.Visibility = rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private TextBlock MakeCell(string text, int column)
    {
        var tb = new TextBlock
        {
            Text = text,
            FontSize = 12.5,
            Foreground = (Brush)FindResource("TextSecondaryBrush"),
            VerticalAlignment = VerticalAlignment.Center,
        };
        Grid.SetColumn(tb, column);
        return tb;
    }

    private TextBlock MakeMonoCell(string text, int column)
    {
        var tb = new TextBlock
        {
            Text = text,
            Style = (Style)FindResource("SwMonoStyle"),
            VerticalAlignment = VerticalAlignment.Center,
        };
        Grid.SetColumn(tb, column);
        return tb;
    }

    private void BuildClosedGrid()
    {
        ClosedGrid.Items.Clear();

        if (_closedFences.Count == 0)
        {
            ClosedEmpty.Visibility = Visibility.Visible;
            ClosedGrid.Visibility = Visibility.Collapsed;
            return;
        }

        ClosedEmpty.Visibility = Visibility.Collapsed;
        ClosedGrid.Visibility = Visibility.Visible;

        foreach (var rec in _closedFences)
            ClosedGrid.Items.Add(BuildClosedCard(rec));
    }

    /// <summary>
    /// Drop a closed-fence record by id and rebuild the grid + count badge in place,
    /// so deleting / restoring an entry refreshes immediately without reopening Settings.
    /// </summary>
    public void RemoveClosedFenceRecord(Guid id)
    {
        var idx = _closedFences.FindIndex(r => r.Id == id);
        if (idx < 0) return;
        _closedFences.RemoveAt(idx);
        SegClosedCount.Text = _closedFences.Count.ToString();
        BuildClosedGrid();
    }

    private UIElement BuildClosedCard(ClosedFenceRecord rec)
    {
        var card = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(0x06, 0xFF, 0xFF, 0xFF)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x10, 0xFF, 0xFF, 0xFF)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Margin = new Thickness(6),
        };

        var stack = new StackPanel();

        // Mini preview area
        var preview = new Border
        {
            Padding = new Thickness(10),
            Height = 88,
        };
        preview.Background = new LinearGradientBrush(
            Color.FromArgb(0x66, 0x32, 0x46, 0x70),
            Color.FromArgb(0x66, 0x29, 0x36, 0x55),
            new Point(0, 0), new Point(1, 1));

        var previewStack = new StackPanel();
        var tabStrip = new StackPanel { Orientation = Orientation.Horizontal };
        var visibleTabs = rec.TabTitles.Take(4).ToList();
        for (int i = 0; i < visibleTabs.Count; i++)
        {
            var isActive = i == 0;
            var tab = new Border
            {
                Background = new SolidColorBrush(isActive
                    ? Color.FromArgb(0x24, 0xFF, 0xFF, 0xFF)
                    : Color.FromArgb(0x10, 0xFF, 0xFF, 0xFF)),
                CornerRadius = new CornerRadius(3),
                Padding = new Thickness(8, 3, 8, 3),
                Margin = new Thickness(0, 0, 3, 0),
                MaxWidth = 90,
                BorderThickness = new Thickness(0, 0, 0, isActive ? 1.5 : 0),
                BorderBrush = (Brush)FindResource("AccentBrush"),
                Child = new TextBlock
                {
                    Text = visibleTabs[i],
                    FontSize = 10,
                    Foreground = (Brush)FindResource(isActive ? "TextPrimaryBrush" : "TextSecondaryBrush"),
                    TextTrimming = TextTrimming.CharacterEllipsis,
                },
            };
            tabStrip.Children.Add(tab);
        }
        if (rec.TabTitles.Count > 4)
        {
            tabStrip.Children.Add(new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(0x10, 0xFF, 0xFF, 0xFF)),
                CornerRadius = new CornerRadius(3),
                Padding = new Thickness(6, 3, 6, 3),
                Child = new TextBlock
                {
                    Text = $"+{rec.TabTitles.Count - 4}",
                    FontSize = 10,
                    Foreground = (Brush)FindResource("TextFaintBrush"),
                },
            });
        }
        previewStack.Children.Add(tabStrip);

        // Mini icon row
        var iconRow = new WrapPanel { Margin = new Thickness(0, 6, 0, 0) };
        int iconCount = Math.Min(8, Math.Max(rec.FileCount, 3));
        for (int i = 0; i < iconCount; i++)
        {
            var hue = 200 + (i * 22) % 160;
            iconRow.Children.Add(new Border
            {
                Width = 16,
                Height = 16,
                CornerRadius = new CornerRadius(2),
                Margin = new Thickness(0, 0, 5, 0),
                Background = MakeIconGradient(hue),
                Opacity = 0.7,
            });
        }
        previewStack.Children.Add(iconRow);
        preview.Child = previewStack;
        stack.Children.Add(preview);

        // Meta row
        var meta = new Border { Padding = new Thickness(14, 12, 14, 12) };
        var metaGrid = new Grid();
        metaGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        metaGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var metaText = new StackPanel();
        metaText.Children.Add(new TextBlock
        {
            Text = rec.Title,
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            Foreground = (Brush)FindResource("TextPrimaryBrush"),
            TextTrimming = TextTrimming.CharacterEllipsis,
        });
        metaText.Children.Add(new TextBlock
        {
            Text = $"{rec.TabTitles.Count} Tab · {rec.FileCount} 文件 · {FormatRelative(rec.ClosedAt)}",
            FontSize = 11,
            Foreground = (Brush)FindResource("TextFaintBrush"),
            Margin = new Thickness(0, 3, 0, 0),
        });
        Grid.SetColumn(metaText, 0);
        metaGrid.Children.Add(metaText);

        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center,
        };

        var restoreBtn = new Button
        {
            Content = "恢复",
            Style = (Style)FindResource("AccentButtonStyle"),
            Padding = new Thickness(12, 6, 12, 6),
            VerticalAlignment = VerticalAlignment.Center,
        };
        restoreBtn.Click += (_, _) => RestoreClosedFenceRequested?.Invoke(rec.Id);
        actions.Children.Add(restoreBtn);

        var deleteBtn = new Button
        {
            Content = "删除",
            Padding = new Thickness(10, 6, 10, 6),
            Margin = new Thickness(6, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
            ToolTip = "从最近关闭列表中永久移除",
        };
        deleteBtn.Click += (_, _) => DeleteClosedFenceRequested?.Invoke(rec.Id);
        actions.Children.Add(deleteBtn);

        Grid.SetColumn(actions, 1);
        metaGrid.Children.Add(actions);

        meta.Child = metaGrid;
        stack.Children.Add(meta);

        card.Child = stack;
        return card;
    }

    private static Brush MakeIconGradient(int hue)
    {
        var c1 = HslToRgb(hue, 0.5, 0.55);
        var c2 = HslToRgb(hue, 0.6, 0.40);
        return new LinearGradientBrush(c1, c2, new Point(0, 0), new Point(1, 1));
    }

    private static Color HslToRgb(double h, double s, double l)
    {
        h = ((h % 360) + 360) % 360 / 360.0;
        double r, g, b;
        if (s == 0) r = g = b = l;
        else
        {
            double q = l < 0.5 ? l * (1 + s) : l + s - l * s;
            double p = 2 * l - q;
            r = HueToRgb(p, q, h + 1.0 / 3);
            g = HueToRgb(p, q, h);
            b = HueToRgb(p, q, h - 1.0 / 3);
        }
        return Color.FromRgb(
            (byte)Math.Round(r * 255),
            (byte)Math.Round(g * 255),
            (byte)Math.Round(b * 255));
    }

    private static double HueToRgb(double p, double q, double t)
    {
        if (t < 0) t += 1;
        if (t > 1) t -= 1;
        if (t < 1.0 / 6) return p + (q - p) * 6 * t;
        if (t < 0.5) return q;
        if (t < 2.0 / 3) return p + (q - p) * (2.0 / 3 - t) * 6;
        return p;
    }

    private static string FormatRelative(DateTimeOffset when)
    {
        var span = DateTimeOffset.Now - when;
        if (span.TotalSeconds < 60) return $"{(int)span.TotalSeconds} 秒前";
        if (span.TotalMinutes < 60) return $"{(int)span.TotalMinutes} 分钟前";
        if (span.TotalHours < 24) return $"{(int)span.TotalHours} 小时前";
        return $"{(int)span.TotalDays} 天前";
    }

    // ── Segmented behavior (mutex toggle) ─────────────────────

    private void SegActive_Checked(object sender, RoutedEventArgs e)
    {
        // BAML 加载阶段 IsChecked="True" 会触发 Checked，但后续 x:Name 字段尚未注入；
        // XAML 默认就保持 ActiveSection 可见 / ClosedSection 折叠，跳过即可。
        if (!IsInitialized) return;

        SegClosed.IsChecked = false;
        ActiveSection.Visibility = Visibility.Visible;
        ClosedSection.Visibility = Visibility.Collapsed;
    }

    private void SegClosed_Checked(object sender, RoutedEventArgs e)
    {
        if (!IsInitialized) return;

        SegActive.IsChecked = false;
        ActiveSection.Visibility = Visibility.Collapsed;
        ClosedSection.Visibility = Visibility.Visible;
    }

    // ── Buttons ──────────────────────────────────────────────

    private void BtnNewFence_Click(object sender, RoutedEventArgs e) => NewFenceRequested?.Invoke();
    private void BtnSaveSnapshot_Click(object sender, RoutedEventArgs e) => SaveSnapshotRequested?.Invoke();
    private void BtnExportLayout_Click(object sender, RoutedEventArgs e) => ExportLayoutRequested?.Invoke();
    private void BtnImportLayout_Click(object sender, RoutedEventArgs e) => ImportLayoutRequested?.Invoke();
}
