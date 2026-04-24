using System.Windows;
using System.Windows.Controls;

namespace DesktopFences.UI.Controls;

/// <summary>
/// Picks between the self-drawn file-type tile (CustomTemplate) and the Shell
/// icon template (ShellTemplate) for each file item, based on the global
/// Application resource key <c>UseCustomFileIcons</c> (bool).
///
/// The two templates are defined inside FencePanel.xaml's UserControl.Resources
/// (they need event handlers from the code-behind).
///
/// Switching modes at runtime: update
///   Application.Current.Resources["UseCustomFileIcons"] = &lt;bool&gt;
/// and call ListBox.Items.Refresh() so the selector re-runs.
/// </summary>
public sealed class FileIconTemplateSelector : DataTemplateSelector
{
    public DataTemplate? CustomTemplate { get; set; }
    public DataTemplate? ShellTemplate { get; set; }

    public override DataTemplate? SelectTemplate(object item, DependencyObject container)
    {
        var useCustom = Application.Current?.TryFindResource("UseCustomFileIcons") is bool b ? b : true;
        return useCustom ? CustomTemplate : ShellTemplate;
    }
}
