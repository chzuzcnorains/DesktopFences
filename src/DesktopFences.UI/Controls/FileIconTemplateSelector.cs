using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using DesktopFences.Core.Models;
using DesktopFences.UI.ViewModels;

namespace DesktopFences.UI.Controls;

/// <summary>
/// Picks one of three file-tile templates per item. Phase 13 routes per-fence:
/// walks up the visual tree from the ListBoxItem container to find the owning
/// <see cref="FencePanel"/> and reads
/// <see cref="FencePanelViewModel.EffectiveIconStyle"/> (which honors the
/// per-fence <see cref="FencePanelViewModel.IconStyleOverride"/>, falling back
/// to the global setting). When the visual ancestor cannot be resolved (e.g.
/// detached preview), falls back to the global Application resource key
/// <c>IconStyle</c> ("App"/"System"/"Shell"), then to the legacy bool.
///
/// Switching modes at runtime: update the per-fence
/// <see cref="FencePanelViewModel.IconStyleOverride"/> or the global
///   Application.Current.Resources["IconStyle"] = "App" | "System" | "Shell"
/// then call <see cref="FencePanel.RefreshFileTileTemplate"/> so the selector
/// re-runs.
/// </summary>
public sealed class FileIconTemplateSelector : DataTemplateSelector
{
    public DataTemplate? CustomTemplate { get; set; }
    public DataTemplate? SystemTemplate { get; set; }
    public DataTemplate? ShellTemplate { get; set; }

    public override DataTemplate? SelectTemplate(object item, DependencyObject container)
    {
        var style = ResolveStyle(container);
        return style switch
        {
            FileIconStyle.System => SystemTemplate,
            FileIconStyle.Shell  => ShellTemplate,
            _                    => CustomTemplate,
        };
    }

    private static FileIconStyle ResolveStyle(DependencyObject container)
    {
        if (FindAncestor<FencePanel>(container)?.DataContext is FencePanelViewModel vm)
            return vm.EffectiveIconStyle;

        var app = Application.Current;
        if (app?.TryFindResource("IconStyle") is string s
            && Enum.TryParse<FileIconStyle>(s, out var parsed))
            return parsed;

        // Legacy bool fallback (settings.json from before Phase 12).
        var legacy = app?.TryFindResource("UseCustomFileIcons") is bool b ? b : true;
        return legacy ? FileIconStyle.App : FileIconStyle.Shell;
    }

    private static T? FindAncestor<T>(DependencyObject? d) where T : DependencyObject
    {
        while (d is not null)
        {
            if (d is T t) return t;
            d = VisualTreeHelper.GetParent(d) ?? LogicalTreeHelper.GetParent(d);
        }
        return null;
    }
}
