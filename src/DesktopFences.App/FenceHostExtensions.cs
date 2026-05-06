using DesktopFences.Core.Models;
using DesktopFences.UI.Controls;
using DesktopFences.UI.ViewModels;

namespace DesktopFences.App;

/// <summary>
/// Convenience projections over the live list of <see cref="FenceHost"/> windows.
/// Replaces ad-hoc <c>SelectMany</c> chains and nested foreach loops that used to
/// be repeated throughout <see cref="App"/>.
/// </summary>
internal static class FenceHostExtensions
{
    /// <summary>All tab ViewModels across every host window.</summary>
    public static IEnumerable<FencePanelViewModel> AllTabs(this IEnumerable<FenceHost> hosts)
        => hosts.SelectMany(h => h.Tabs);

    /// <summary>All tab models (FenceDefinition) across every host window.</summary>
    public static List<FenceDefinition> AllDefinitions(this IEnumerable<FenceHost> hosts)
        => hosts.SelectMany(h => h.Tabs.Select(t => t.Model)).ToList();

    /// <summary>Locate a tab by its fence ID, or null if not found.</summary>
    public static FencePanelViewModel? FindTabById(this IEnumerable<FenceHost> hosts, Guid id)
        => hosts.SelectMany(h => h.Tabs).FirstOrDefault(t => t.Id == id);

    /// <summary>The host window containing a tab with the given fence ID, or null.</summary>
    public static FenceHost? FindHostByTabId(this IEnumerable<FenceHost> hosts, Guid id)
        => hosts.FirstOrDefault(h => h.Tabs.Any(t => t.Id == id));

    /// <summary>True if any tab in any host already contains <paramref name="filePath"/>.</summary>
    public static bool ContainsFile(this IEnumerable<FenceHost> hosts, string filePath)
        => hosts.Any(h => h.Tabs.Any(t => t.Files.Any(
            f => string.Equals(f.FilePath, filePath, StringComparison.OrdinalIgnoreCase))));
}
