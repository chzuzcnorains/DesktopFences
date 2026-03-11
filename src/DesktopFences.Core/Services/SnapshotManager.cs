using System.Text.Json;
using DesktopFences.Core.Models;

namespace DesktopFences.Core.Services;

/// <summary>
/// Manages layout snapshots: create, restore, rename, delete.
/// </summary>
public class SnapshotManager
{
    private readonly ILayoutStore _store;
    private List<LayoutSnapshot> _snapshots = [];

    public IReadOnlyList<LayoutSnapshot> Snapshots => _snapshots;

    public SnapshotManager(ILayoutStore store)
    {
        _store = store;
    }

    public async Task LoadAsync()
    {
        _snapshots = await _store.LoadSnapshotsAsync();
        _snapshots.Sort((a, b) => a.CreatedAt.CompareTo(b.CreatedAt));
    }

    /// <summary>
    /// Create a snapshot from the current fence definitions.
    /// Deep-clones fences so the snapshot is independent.
    /// </summary>
    public async Task<LayoutSnapshot> CreateSnapshotAsync(
        string name, IEnumerable<FenceDefinition> currentFences, ScreenConfiguration screenConfig)
    {
        var snapshot = new LayoutSnapshot
        {
            Name = name,
            CreatedAt = DateTime.UtcNow,
            Fences = DeepCloneFences(currentFences),
            ScreenConfig = screenConfig
        };

        await _store.SaveSnapshotAsync(snapshot);
        _snapshots.Add(snapshot);
        return snapshot;
    }

    /// <summary>
    /// Restore a snapshot, returning its fence definitions (deep-cloned).
    /// </summary>
    public List<FenceDefinition>? RestoreSnapshot(Guid snapshotId)
    {
        var snapshot = _snapshots.FirstOrDefault(s => s.Id == snapshotId);
        if (snapshot is null) return null;
        return DeepCloneFences(snapshot.Fences);
    }

    public async Task RenameSnapshotAsync(Guid snapshotId, string newName)
    {
        var snapshot = _snapshots.FirstOrDefault(s => s.Id == snapshotId);
        if (snapshot is null) return;
        snapshot.Name = newName;
        await _store.SaveSnapshotAsync(snapshot);
    }

    public async Task DeleteSnapshotAsync(Guid snapshotId)
    {
        await _store.DeleteSnapshotAsync(snapshotId);
        _snapshots.RemoveAll(s => s.Id == snapshotId);
    }

    private static List<FenceDefinition> DeepCloneFences(IEnumerable<FenceDefinition> fences)
    {
        // Use JSON round-trip for deep clone
        var json = JsonSerializer.Serialize(fences.ToList());
        return JsonSerializer.Deserialize<List<FenceDefinition>>(json) ?? [];
    }
}
