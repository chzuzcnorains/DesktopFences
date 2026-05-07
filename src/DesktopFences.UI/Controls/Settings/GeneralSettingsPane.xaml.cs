using System.Windows.Controls;
using DesktopFences.Core.Models;

namespace DesktopFences.UI.Controls.Settings;

public partial class GeneralSettingsPane : UserControl
{
    public GeneralSettingsPane()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Populate UI controls from the supplied settings.
    /// </summary>
    public void Load(AppSettings settings)
    {
        SliderSnapThreshold.Value = settings.SnapThreshold;
        ChkQuickHide.IsChecked = settings.QuickHideEnabled;
        ChkStartWithWindows.IsChecked = settings.StartWithWindows;
        ChkStartMinimized.IsChecked = settings.StartMinimized;
    }

    /// <summary>
    /// Write UI control state back into the supplied settings instance.
    /// </summary>
    public void Save(AppSettings settings)
    {
        settings.SnapThreshold = (int)SliderSnapThreshold.Value;
        settings.QuickHideEnabled = ChkQuickHide.IsChecked == true;
        settings.StartWithWindows = ChkStartWithWindows.IsChecked == true;
        settings.StartMinimized = ChkStartMinimized.IsChecked == true;
    }
}
