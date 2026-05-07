using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using DesktopFences.Core.Models;

namespace DesktopFences.UI.Controls.Settings;

public partial class AppearanceSettingsPane : UserControl
{
    private record Swatch(string Hex, string Name, string Letter);

    /// <summary>
    /// 6 v2-prototype accent swatches (sRGB approximations of the OKLCH 0.65 / 0.15 / N° series).
    /// Order matches the desktop-v2.html prototype.
    /// </summary>
    private static readonly Swatch[] Swatches =
    [
        new("#7AA7E6", "Blue",   "B"),
        new("#9388E0", "Indigo", "I"),
        new("#4FC3D6", "Teal",   "T"),
        new("#5DC58A", "Green",  "G"),
        new("#E8B85B", "Amber",  "A"),
        new("#E58A6B", "Coral",  "C"),
    ];

    private record TabStyleEntry(TabStyle Style, string Label, string Description);

    private static readonly TabStyleEntry[] TabStyleEntries =
    [
        new(TabStyle.Flat,      "Flat",      "极简底线指示"),
        new(TabStyle.Segmented, "Segmented", "胶囊填色"),
        new(TabStyle.Rounded,   "Rounded",   "顶部圆角 + 底线"),
        new(TabStyle.MenuOnly,  "MenuOnly",  "隐藏标签条"),
    ];

    private record IconStyleEntry(FileIconStyle Style, string Label, string Description);

    private static readonly IconStyleEntry[] IconStyleEntries =
    [
        new(FileIconStyle.App,    "App",    "彩色圆角 tile + 字母叠加"),
        new(FileIconStyle.System, "System", "Windows 经典 page-with-fold + 角标"),
    ];

    private string _accentColor = Swatches[0].Hex;
    private TabStyle _tabStyle = TabStyle.Flat;
    private FileIconStyle _iconStyle = FileIconStyle.App;
    private bool _suppressEvents = true;

    public AppearanceSettingsPane()
    {
        InitializeComponent();
        BuildSwatches();
        BuildTabStyleGrid();
        BuildIconStyleGrid();
    }

    public void Load(AppSettings s)
    {
        _suppressEvents = true;

        _accentColor = string.IsNullOrWhiteSpace(s.AccentColor) ? Swatches[0].Hex : s.AccentColor;
        _tabStyle = s.TabStyle;
        // System 与 App 双卡可见;Shell 是隐藏 fallback,picker 仅在两者间切。
        _iconStyle = s.IconStyle == FileIconStyle.Shell ? FileIconStyle.App : s.IconStyle;

        HueSlider.Value      = Math.Max(0, Math.Min(360, s.FenceBgHue));
        OpacitySlider.Value  = Math.Max(0.20, Math.Min(0.90, s.FenceOpacity));
        BlurSlider.Value     = Math.Max(0, Math.Min(60, s.FenceBlurRadius));
        IconSizeSlider.Value = Math.Max(28, Math.Min(64, s.IconSize));

        RefreshSwatchSelection();
        RefreshTabStyleSelection();
        RefreshIconStyleSelection();
        RefreshValueLabels();

        _suppressEvents = false;
        Preview.Apply(BuildSnapshot());
    }

    public void Save(AppSettings s)
    {
        s.AccentColor     = _accentColor;
        s.TabStyle        = _tabStyle;
        s.IconStyle       = _iconStyle;
        s.FenceBgHue      = (int)Math.Round(HueSlider.Value);
        s.FenceOpacity    = Math.Round(OpacitySlider.Value, 2);
        s.FenceBlurRadius = (int)Math.Round(BlurSlider.Value);
        s.IconSize        = (int)Math.Round(IconSizeSlider.Value);
    }

    // ── Swatches ─────────────────────────────────────────────

    private void BuildSwatches()
    {
        SwatchStrip.Children.Clear();
        foreach (var swatch in Swatches)
        {
            var color = (Color)ColorConverter.ConvertFromString(swatch.Hex);
            var border = new Border
            {
                Style = (Style)Resources["AccentSwatchStyle"],
                Background = new SolidColorBrush(color),
                ToolTip = $"{swatch.Name}  {swatch.Hex}",
            };
            // Tag carries the hex so the click handler doesn't need closures
            border.MouseLeftButtonDown += (_, _) => SelectSwatch(swatch.Hex);
            // Letter overlay — visual cue from the v2 prototype's circle interior
            border.Child = new TextBlock
            {
                Text = swatch.Letter,
                Foreground = Brushes.White,
                FontWeight = FontWeights.Bold,
                FontSize = 14,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Effect = new System.Windows.Media.Effects.DropShadowEffect
                {
                    BlurRadius = 2, ShadowDepth = 1, Opacity = 0.45, Color = Colors.Black,
                },
            };
            SwatchStrip.Children.Add(border);
        }
    }

    private void SelectSwatch(string hex)
    {
        _accentColor = hex;
        RefreshSwatchSelection();
        if (!_suppressEvents) Preview.Apply(BuildSnapshot());
    }

    private void RefreshSwatchSelection()
    {
        for (int i = 0; i < SwatchStrip.Children.Count; i++)
        {
            if (SwatchStrip.Children[i] is Border b)
                b.Tag = string.Equals(Swatches[i].Hex, _accentColor, StringComparison.OrdinalIgnoreCase)
                    ? "active" : null;
        }
    }

    // ── Tab style chooser ────────────────────────────────────

    private void BuildTabStyleGrid()
    {
        TabStyleGrid.Children.Clear();
        foreach (var entry in TabStyleEntries)
        {
            var border = new Border
            {
                Style = (Style)Resources["TabStyleTileStyle"],
                Margin = new Thickness(4),
            };
            border.MouseLeftButtonDown += (_, _) => SelectTabStyle(entry.Style);

            var stack = new StackPanel();
            stack.Children.Add(new TextBlock
            {
                Text = entry.Label,
                FontSize = 13,
                FontWeight = FontWeights.SemiBold,
                Foreground = (Brush)FindResource("TextPrimaryBrush"),
            });
            stack.Children.Add(new TextBlock
            {
                Text = entry.Description,
                FontSize = 10.5,
                Foreground = (Brush)FindResource("TextFaintBrush"),
                Margin = new Thickness(0, 2, 0, 0),
            });
            border.Child = stack;
            TabStyleGrid.Children.Add(border);
        }
    }

    private void SelectTabStyle(TabStyle style)
    {
        _tabStyle = style;
        RefreshTabStyleSelection();
        if (!_suppressEvents) Preview.Apply(BuildSnapshot());
    }

    private void RefreshTabStyleSelection()
    {
        for (int i = 0; i < TabStyleGrid.Children.Count; i++)
        {
            if (TabStyleGrid.Children[i] is Border b)
                b.Tag = TabStyleEntries[i].Style == _tabStyle ? "active" : null;
        }
    }

    // ── Icon style chooser (Phase 12) ────────────────────────

    private void BuildIconStyleGrid()
    {
        IconStyleGrid.Children.Clear();
        foreach (var entry in IconStyleEntries)
        {
            var border = new Border
            {
                Style = (Style)Resources["TabStyleTileStyle"],
                Margin = new Thickness(4),
            };
            border.MouseLeftButtonDown += (_, _) => SelectIconStyle(entry.Style);

            var stack = new StackPanel();
            stack.Children.Add(new TextBlock
            {
                Text = entry.Label,
                FontSize = 13,
                FontWeight = FontWeights.SemiBold,
                Foreground = (Brush)FindResource("TextPrimaryBrush"),
            });
            stack.Children.Add(new TextBlock
            {
                Text = entry.Description,
                FontSize = 10.5,
                Foreground = (Brush)FindResource("TextFaintBrush"),
                Margin = new Thickness(0, 2, 0, 0),
                TextWrapping = TextWrapping.Wrap,
            });
            border.Child = stack;
            IconStyleGrid.Children.Add(border);
        }
    }

    private void SelectIconStyle(FileIconStyle style)
    {
        _iconStyle = style;
        RefreshIconStyleSelection();
        if (!_suppressEvents) Preview.Apply(BuildSnapshot());
    }

    private void RefreshIconStyleSelection()
    {
        for (int i = 0; i < IconStyleGrid.Children.Count; i++)
        {
            if (IconStyleGrid.Children[i] is Border b)
                b.Tag = IconStyleEntries[i].Style == _iconStyle ? "active" : null;
        }
    }

    // ── Slider events ────────────────────────────────────────

    private void OnControlChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        // XAML 解析阶段，Slider 的 Min/Max 设置会触发 Value 协调并触发 ValueChanged，
        // 但此时后续 x:Name 字段（如 BlurValueLabel）尚未注入。等到 EndInit 才能安全访问。
        if (!IsInitialized) return;

        RefreshValueLabels();
        if (_suppressEvents) return;
        Preview.Apply(BuildSnapshot());
    }

    private void RefreshValueLabels()
    {
        HueValueLabel.Text      = $"h={(int)Math.Round(HueSlider.Value)}°";
        OpacityValueLabel.Text  = $"{(int)Math.Round(OpacitySlider.Value * 100)}%";
        BlurValueLabel.Text     = $"{(int)Math.Round(BlurSlider.Value)} px";
        IconSizeValueLabel.Text = $"{(int)Math.Round(IconSizeSlider.Value)} px";
    }

    private AppSettings BuildSnapshot() => new()
    {
        AccentColor     = _accentColor,
        TabStyle        = _tabStyle,
        IconStyle       = _iconStyle,
        FenceBgHue      = (int)Math.Round(HueSlider.Value),
        FenceOpacity    = OpacitySlider.Value,
        FenceBlurRadius = (int)Math.Round(BlurSlider.Value),
        IconSize        = (int)Math.Round(IconSizeSlider.Value),
    };
}
