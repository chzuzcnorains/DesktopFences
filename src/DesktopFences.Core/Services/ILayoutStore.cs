using DesktopFences.Core.Models;

namespace DesktopFences.Core.Services;

/// <summary>
/// Persists and retrieves fence layouts and snapshots.
/// </summary>
public interface ILayoutStore
{
    Task<List<FenceDefinition>> LoadFencesAsync();
    Task SaveFencesAsync(IEnumerable<FenceDefinition> fences);
    Task<List<ClassificationRule>> LoadRulesAsync();
    Task SaveRulesAsync(IEnumerable<ClassificationRule> rules);
    Task<List<LayoutSnapshot>> LoadSnapshotsAsync();
    Task SaveSnapshotAsync(LayoutSnapshot snapshot);
    Task DeleteSnapshotAsync(Guid snapshotId);
    Task SaveMonitorLayoutAsync(string configHash, IEnumerable<FenceDefinition> fences);
    Task<List<FenceDefinition>?> LoadMonitorLayoutAsync(string configHash);
    Task<List<DesktopPage>> LoadPagesAsync();
    Task SavePagesAsync(IEnumerable<DesktopPage> pages);
    Task<AppSettings> LoadSettingsAsync();
    Task SaveSettingsAsync(AppSettings settings);

    /// <summary>
    /// Persists the list of file paths that were hidden by auto-organize.
    /// Used to restore files on app exit.
    /// </summary>
    Task SaveHiddenFilesAsync(IEnumerable<string> paths);

    /// <summary>
    /// Loads the previously saved list of hidden file paths.
    /// </summary>
    Task<List<string>> LoadHiddenFilesAsync();
}
