using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using DesktopFences.Core.Models;

namespace DesktopFences.UI.Controls.Settings;

/// <summary>
/// Mini-fence preview rendered inside the Appearance settings pane.
/// Apply(AppSettings) re-renders the strip + tiles so the user can see
/// AccentColor / FenceBgHue / FenceOpacity / FenceBlurRadius / IconSize / TabStyle
/// react in real time before clicking Save.
/// </summary>
public partial class FencePreviewControl : UserControl
{
    private record TileSample(string Label, string Glyph, Color Color);

    private static readonly TileSample[] Samples =
    [
        new("Notes",   "TXT", Color.FromRgb(0x7F, 0x8B, 0x9F)),
        new("Report",  "DOC", Color.FromRgb(0x4A, 0x85, 0xD8)),
        new("Photo",   "IMG", Color.FromRgb(0xC2, 0x4D, 0xB8)),
        new("Setup",   "EXE", Color.FromRgb(0x53, 0x77, 0xC7)),
        new("Stats",   "XLS", Color.FromRgb(0x3F, 0xB9, 0x78)),
        new("Backup",  "ZIP", Color.FromRgb(0x9B, 0xB5, 0x43)),
    ];

    private static readonly string[] TabLabels = ["程序", "文档", "图片"];

    public FencePreviewControl()
    {
        InitializeComponent();
    }

    public void Apply(AppSettings s)
    {
        var hue     = Clamp(s.FenceBgHue, 0, 360);
        var opacity = Clamp(s.FenceOpacity, 0.20, 0.90);
        var blur    = Clamp(s.FenceBlurRadius, 0, 60);
        var icon    = Clamp(s.IconSize, 28, 64);
        var accent  = TryParseColor(s.AccentColor, Color.FromRgb(0x7A, 0xA7, 0xE6));

        var alpha = (byte)Math.Round(opacity * 255);
        FenceBgBrush.Color    = HslToRgb(hue, 0.30, 0.18, alpha);
        FenceShadow.BlurRadius = blur;

        BuildTabStrip(s.TabStyle, accent);
        BuildTiles(icon);
    }

    private void BuildTabStrip(TabStyle style, Color accent)
    {
        TabStrip.Children.Clear();

        if (style == TabStyle.MenuOnly)
        {
            TabStripContainer.Padding = new Thickness(10, 6, 10, 6);
            var sp = new StackPanel { Orientation = Orientation.Horizontal };
            sp.Children.Add(new TextBlock
            {
                Text = TabLabels[0],
                Foreground = new SolidColorBrush(Color.FromArgb(0xFF, 0xE8, 0xEC, 0xF4)),
                FontSize = 11.5,
                FontWeight = FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center,
            });
            sp.Children.Add(new TextBlock
            {
                Text = "  ▾",
                Foreground = new SolidColorBrush(Color.FromArgb(0xAA, 0xAE, 0xB8, 0xCC)),
                FontSize = 11,
                VerticalAlignment = VerticalAlignment.Center,
            });
            TabStrip.Children.Add(sp);
            return;
        }

        TabStripContainer.Padding = new Thickness(8, 4, 8, 4);
        for (int i = 0; i < TabLabels.Length; i++)
            TabStrip.Children.Add(MakeTab(TabLabels[i], i == 0, style, accent));
    }

    private static UIElement MakeTab(string label, bool active, TabStyle style, Color accent)
    {
        var text = new TextBlock
        {
            Text = label,
            FontSize = 11.5,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
            Foreground = new SolidColorBrush(active
                ? Color.FromArgb(0xFF, 0xE8, 0xEC, 0xF4)
                : Color.FromArgb(0xCC, 0xAE, 0xB8, 0xCC)),
        };

        switch (style)
        {
            case TabStyle.Flat:
            {
                var border = new Border
                {
                    Padding = new Thickness(11, 4, 11, 4),
                    Margin = new Thickness(0, 0, 2, 0),
                    CornerRadius = new CornerRadius(6, 6, 0, 0),
                    Background = active
                        ? (Brush)new SolidColorBrush(Color.FromArgb(0x18, 0xFF, 0xFF, 0xFF))
                        : Brushes.Transparent,
                };
                if (active)
                {
                    var grid = new Grid();
                    grid.Children.Add(text);
                    grid.Children.Add(new Border
                    {
                        Height = 2,
                        VerticalAlignment = VerticalAlignment.Bottom,
                        Background = new SolidColorBrush(accent),
                        CornerRadius = new CornerRadius(1),
                        Margin = new Thickness(2, 0, 2, -1),
                    });
                    border.Child = grid;
                }
                else border.Child = text;
                return border;
            }

            case TabStyle.Segmented:
            {
                var border = new Border
                {
                    Padding = new Thickness(11, 3, 11, 3),
                    Margin = new Thickness(2, 1, 2, 1),
                    CornerRadius = new CornerRadius(12),
                };
                if (active)
                {
                    var darker = Color.FromArgb(255,
                        (byte)(accent.R * 0.78),
                        (byte)(accent.G * 0.78),
                        (byte)(accent.B * 0.78));
                    border.Background = new LinearGradientBrush(
                        accent, darker, new Point(0, 0), new Point(0, 1));
                    text.Foreground = Brushes.White;
                }
                else border.Background = Brushes.Transparent;
                border.Child = text;
                return border;
            }

            case TabStyle.Rounded:
            {
                var border = new Border
                {
                    Padding = new Thickness(11, 4, 11, 4),
                    Margin = new Thickness(0, 0, 2, 0),
                    CornerRadius = new CornerRadius(8, 8, 0, 0),
                    Background = active
                        ? (Brush)new SolidColorBrush(Color.FromArgb(0x1F, 0xFF, 0xFF, 0xFF))
                        : Brushes.Transparent,
                };
                if (active)
                {
                    var grid = new Grid();
                    grid.Children.Add(text);
                    grid.Children.Add(new Border
                    {
                        Height = 2,
                        VerticalAlignment = VerticalAlignment.Bottom,
                        Background = new SolidColorBrush(accent),
                        CornerRadius = new CornerRadius(1),
                        Margin = new Thickness(4, 0, 4, -1),
                    });
                    border.Child = grid;
                }
                else border.Child = text;
                return border;
            }

            default:
                return new Border { Padding = new Thickness(10, 4, 10, 4), Child = text };
        }
    }

    private void BuildTiles(int iconSize)
    {
        TileWrap.Children.Clear();

        double tileWidth  = iconSize + 28;
        double tileHeight = iconSize + 32;
        double glyphFont  = iconSize * 0.34;
        double cornerRadius = Math.Max(3, iconSize * 0.12);

        foreach (var sample in Samples)
        {
            var glyphBg = new Border
            {
                Width = iconSize,
                Height = iconSize,
                CornerRadius = new CornerRadius(cornerRadius),
                Background = new SolidColorBrush(sample.Color),
                Effect = new System.Windows.Media.Effects.DropShadowEffect
                {
                    BlurRadius = 6,
                    ShadowDepth = 2,
                    Opacity = 0.35,
                    Color = Colors.Black,
                },
                Child = new TextBlock
                {
                    Text = sample.Glyph,
                    Foreground = Brushes.White,
                    FontWeight = FontWeights.Bold,
                    FontSize = glyphFont,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                },
            };

            var label = new TextBlock
            {
                Text = sample.Label,
                FontSize = 10.5,
                Foreground = new SolidColorBrush(Color.FromArgb(0xEE, 0xE8, 0xEC, 0xF4)),
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 4, 0, 0),
                TextTrimming = TextTrimming.CharacterEllipsis,
                MaxWidth = tileWidth - 6,
            };

            var sp = new StackPanel
            {
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            };
            sp.Children.Add(glyphBg);
            sp.Children.Add(label);

            TileWrap.Children.Add(new Border
            {
                Width = tileWidth,
                Height = tileHeight,
                Margin = new Thickness(3),
                CornerRadius = new CornerRadius(4),
                Background = Brushes.Transparent,
                Child = sp,
            });
        }
    }

    // ── Helpers ──────────────────────────────────────────────

    private static int Clamp(int v, int lo, int hi) => Math.Max(lo, Math.Min(hi, v));
    private static double Clamp(double v, double lo, double hi) => Math.Max(lo, Math.Min(hi, v));

    private static Color HslToRgb(double h, double s, double l, byte alpha)
    {
        h = ((h % 360) + 360) % 360 / 360.0;
        double r, g, b;
        if (s == 0)
        {
            r = g = b = l;
        }
        else
        {
            double q = l < 0.5 ? l * (1 + s) : l + s - l * s;
            double p = 2 * l - q;
            r = HueToRgb(p, q, h + 1.0 / 3);
            g = HueToRgb(p, q, h);
            b = HueToRgb(p, q, h - 1.0 / 3);
        }
        return Color.FromArgb(alpha,
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

    private static Color TryParseColor(string hex, Color fallback)
    {
        if (string.IsNullOrWhiteSpace(hex)) return fallback;
        try
        {
            return (Color)ColorConverter.ConvertFromString(hex);
        }
        catch
        {
            return fallback;
        }
    }
}
