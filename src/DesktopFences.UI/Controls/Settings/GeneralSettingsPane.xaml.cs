using System.Windows.Controls;
using DesktopFences.Core.Models;

namespace DesktopFences.UI.Controls.Settings;

public partial class GeneralSettingsPane : UserControl
{
    private record TabStyleOption(string Display, TabStyle Style);

    private static readonly TabStyleOption[] TabStyleOptions =
    [
        new("扁平 (Flat)", TabStyle.Flat),
        new("分段 (Segmented)", TabStyle.Segmented),
        new("圆角 (Rounded)", TabStyle.Rounded),
        new("仅菜单 (Menu Only)", TabStyle.MenuOnly),
    ];

    public GeneralSettingsPane()
    {
        InitializeComponent();

        CboTabStyle.Items.Clear();
        foreach (var opt in TabStyleOptions)
            CboTabStyle.Items.Add(opt);
        CboTabStyle.DisplayMemberPath = "Display";
    }

    /// <summary>
    /// Populate UI controls from the supplied settings.
    /// </summary>
    public void Load(AppSettings settings)
    {
        TxtFenceColor.Text = settings.DefaultFenceColor;
        TxtTitleBarColor.Text = settings.DefaultTitleBarColor;
        TxtTextColor.Text = settings.DefaultTextColor;
        SliderOpacity.Value = settings.DefaultOpacity;
        SliderFontSize.Value = settings.TitleBarFontSize;
        SliderSnapThreshold.Value = settings.SnapThreshold;
        ChkQuickHide.IsChecked = settings.QuickHideEnabled;
        ChkStartWithWindows.IsChecked = settings.StartWithWindows;
        ChkStartMinimized.IsChecked = settings.StartMinimized;
        ChkCompatibilityMode.IsChecked = settings.CompatibilityMode;
        ChkDebugLogging.IsChecked = settings.DebugLogging;

        CboTabStyle.SelectedItem = null;
        foreach (TabStyleOption opt in CboTabStyle.Items)
        {
            if (opt.Style == settings.TabStyle)
            {
                CboTabStyle.SelectedItem = opt;
                break;
            }
        }
        if (CboTabStyle.SelectedIndex < 0 && CboTabStyle.Items.Count > 0)
            CboTabStyle.SelectedIndex = 0;
    }

    /// <summary>
    /// Write UI control state back into the supplied settings instance.
    /// </summary>
    public void Save(AppSettings settings)
    {
        settings.DefaultFenceColor = TxtFenceColor.Text;
        settings.DefaultTitleBarColor = TxtTitleBarColor.Text;
        settings.DefaultTextColor = TxtTextColor.Text;
        settings.DefaultOpacity = SliderOpacity.Value;
        settings.TitleBarFontSize = SliderFontSize.Value;
        settings.SnapThreshold = (int)SliderSnapThreshold.Value;
        settings.QuickHideEnabled = ChkQuickHide.IsChecked == true;
        settings.StartWithWindows = ChkStartWithWindows.IsChecked == true;
        settings.StartMinimized = ChkStartMinimized.IsChecked == true;
        settings.CompatibilityMode = ChkCompatibilityMode.IsChecked == true;
        settings.DebugLogging = ChkDebugLogging.IsChecked == true;

        if (CboTabStyle.SelectedItem is TabStyleOption tabOpt)
            settings.TabStyle = tabOpt.Style;
    }
}
