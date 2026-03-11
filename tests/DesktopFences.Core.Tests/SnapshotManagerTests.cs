using DesktopFences.Core.Models;
using DesktopFences.Core.Services;

namespace DesktopFences.Core.Tests;

public class SnapshotManagerTests : IDisposable
{
    private readonly string _tempDir;
    private readonly JsonLayoutStore _store;
    private readonly SnapshotManager _manager;

    public SnapshotManagerTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"DesktopFences_Test_{Guid.NewGuid():N}");
        _store = new JsonLayoutStore(_tempDir);
        _manager = new SnapshotManager(_store);
    }

    [Fact]
    public async Task CreateSnapshot_SavesAndReturnsSnapshot()
    {
        var fences = new List<FenceDefinition>
        {
            new() { Title = "Test", Bounds = new FenceRect { X = 10, Y = 20, Width = 300, Height = 200 } }
        };
        var config = new ScreenConfiguration { ScreenCount = 1 };

        var snapshot = await _manager.CreateSnapshotAsync("My Snapshot", fences, config);

        Assert.Equal("My Snapshot", snapshot.Name);
        Assert.Single(snapshot.Fences);
        Assert.Single(_manager.Snapshots);
    }

    [Fact]
    public async Task RestoreSnapshot_ReturnsDeepClone()
    {
        var fences = new List<FenceDefinition>
        {
            new() { Title = "Original", Bounds = new FenceRect { X = 50, Y = 50 } }
        };

        var snapshot = await _manager.CreateSnapshotAsync("S1", fences, new ScreenConfiguration());
        var restored = _manager.RestoreSnapshot(snapshot.Id);

        Assert.NotNull(restored);
        Assert.Single(restored);
        Assert.Equal("Original", restored[0].Title);

        // Verify deep clone (different object)
        restored[0].Title = "Modified";
        Assert.Equal("Original", _manager.Snapshots[0].Fences[0].Title);
    }

    [Fact]
    public async Task DeleteSnapshot_RemovesFromList()
    {
        var snapshot = await _manager.CreateSnapshotAsync("ToDelete",
            [new FenceDefinition()], new ScreenConfiguration());

        await _manager.DeleteSnapshotAsync(snapshot.Id);

        Assert.Empty(_manager.Snapshots);
    }

    [Fact]
    public async Task RenameSnapshot_UpdatesName()
    {
        var snapshot = await _manager.CreateSnapshotAsync("Old Name",
            [new FenceDefinition()], new ScreenConfiguration());

        await _manager.RenameSnapshotAsync(snapshot.Id, "New Name");

        Assert.Equal("New Name", _manager.Snapshots[0].Name);
    }

    [Fact]
    public async Task LoadAsync_RestoresPersistedSnapshots()
    {
        // Create and save a snapshot
        await _manager.CreateSnapshotAsync("Persisted",
            [new FenceDefinition { Title = "F1" }], new ScreenConfiguration());

        // Create a new manager instance with same store
        var manager2 = new SnapshotManager(_store);
        await manager2.LoadAsync();

        Assert.Single(manager2.Snapshots);
        Assert.Equal("Persisted", manager2.Snapshots[0].Name);
    }

    [Fact]
    public void RestoreSnapshot_NonExistentId_ReturnsNull()
    {
        var result = _manager.RestoreSnapshot(Guid.NewGuid());
        Assert.Null(result);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, true); }
        catch { /* cleanup */ }
    }
}
