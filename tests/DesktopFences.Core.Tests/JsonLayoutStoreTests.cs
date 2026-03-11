using DesktopFences.Core.Models;
using DesktopFences.Core.Services;

namespace DesktopFences.Core.Tests;

public class JsonLayoutStoreTests : IDisposable
{
    private readonly string _tempDir;
    private readonly JsonLayoutStore _store;

    public JsonLayoutStoreTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"DesktopFences_Test_{Guid.NewGuid():N}");
        _store = new JsonLayoutStore(_tempDir);
    }

    [Fact]
    public async Task SaveAndLoadFences_RoundTrip()
    {
        var fences = new List<FenceDefinition>
        {
            new() { Title = "Test Fence", Bounds = new FenceRect { X = 100, Y = 200 } }
        };

        await _store.SaveFencesAsync(fences);
        var loaded = await _store.LoadFencesAsync();

        Assert.Single(loaded);
        Assert.Equal("Test Fence", loaded[0].Title);
        Assert.Equal(100, loaded[0].Bounds.X);
    }

    [Fact]
    public async Task SaveAndLoadMonitorLayout_RoundTrip()
    {
        var hash = "ABCD1234";
        var fences = new List<FenceDefinition>
        {
            new() { Title = "Monitor Layout", MonitorIndex = 1 }
        };

        await _store.SaveMonitorLayoutAsync(hash, fences);
        var loaded = await _store.LoadMonitorLayoutAsync(hash);

        Assert.NotNull(loaded);
        Assert.Single(loaded);
        Assert.Equal("Monitor Layout", loaded[0].Title);
        Assert.Equal(1, loaded[0].MonitorIndex);
    }

    [Fact]
    public async Task LoadMonitorLayout_NonExistent_ReturnsNull()
    {
        var result = await _store.LoadMonitorLayoutAsync("NONEXISTENT");
        Assert.Null(result);
    }

    [Fact]
    public async Task SaveAndLoadPages_RoundTrip()
    {
        var fenceId = Guid.NewGuid();
        var pages = new List<DesktopPage>
        {
            new() { PageIndex = 0, Name = "Page 1", FenceIds = [fenceId] },
            new() { PageIndex = 1, Name = "Page 2" }
        };

        await _store.SavePagesAsync(pages);
        var loaded = await _store.LoadPagesAsync();

        Assert.Equal(2, loaded.Count);
        Assert.Equal("Page 1", loaded[0].Name);
        Assert.Contains(fenceId, loaded[0].FenceIds);
    }

    [Fact]
    public async Task LoadPages_NoFile_ReturnsEmpty()
    {
        var loaded = await _store.LoadPagesAsync();
        Assert.Empty(loaded);
    }

    [Fact]
    public async Task SaveAndLoadSnapshot_RoundTrip()
    {
        var snapshot = new LayoutSnapshot
        {
            Name = "My Snapshot",
            Fences = [new FenceDefinition { Title = "F1" }],
            ScreenConfig = new ScreenConfiguration
            {
                ScreenCount = 2,
                ConfigHash = "HASH123"
            }
        };

        await _store.SaveSnapshotAsync(snapshot);
        var loaded = await _store.LoadSnapshotsAsync();

        Assert.Single(loaded);
        Assert.Equal("My Snapshot", loaded[0].Name);
        Assert.Equal("HASH123", loaded[0].ScreenConfig.ConfigHash);
    }

    [Fact]
    public async Task DeleteSnapshot_RemovesFile()
    {
        var snapshot = new LayoutSnapshot { Name = "ToDelete" };
        await _store.SaveSnapshotAsync(snapshot);
        await _store.DeleteSnapshotAsync(snapshot.Id);

        var loaded = await _store.LoadSnapshotsAsync();
        Assert.Empty(loaded);
    }

    [Fact]
    public async Task FenceDefinition_PortalPath_Persisted()
    {
        var fences = new List<FenceDefinition>
        {
            new() { Title = "Portal", PortalPath = @"C:\Users\Test\Documents" }
        };

        await _store.SaveFencesAsync(fences);
        var loaded = await _store.LoadFencesAsync();

        Assert.Equal(@"C:\Users\Test\Documents", loaded[0].PortalPath);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, true); }
        catch { /* cleanup */ }
    }
}
