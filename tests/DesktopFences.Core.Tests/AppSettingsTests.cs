using DesktopFences.Core.Models;
using DesktopFences.Core.Services;

namespace DesktopFences.Core.Tests;

public class AppSettingsTests : IDisposable
{
    private readonly string _tempDir;
    private readonly JsonLayoutStore _store;

    public AppSettingsTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"DesktopFences_Test_{Guid.NewGuid():N}");
        _store = new JsonLayoutStore(_tempDir);
    }

    [Fact]
    public async Task LoadSettings_NoFile_ReturnsDefaults()
    {
        var settings = await _store.LoadSettingsAsync();

        Assert.Equal("#CC1E1E2E", settings.DefaultFenceColor);
        Assert.Equal(1.0, settings.DefaultOpacity);
        Assert.Equal(10, settings.SnapThreshold);
        Assert.True(settings.QuickHideEnabled);
        Assert.False(settings.StartWithWindows);
    }

    [Fact]
    public async Task SaveAndLoadSettings_RoundTrip()
    {
        var settings = new AppSettings
        {
            DefaultFenceColor = "#FF112233",
            DefaultOpacity = 0.8,
            SnapThreshold = 15,
            TitleBarFontSize = 14,
            StartWithWindows = true,
            QuickHideEnabled = false
        };

        await _store.SaveSettingsAsync(settings);
        var loaded = await _store.LoadSettingsAsync();

        Assert.Equal("#FF112233", loaded.DefaultFenceColor);
        Assert.Equal(0.8, loaded.DefaultOpacity);
        Assert.Equal(15, loaded.SnapThreshold);
        Assert.Equal(14, loaded.TitleBarFontSize);
        Assert.True(loaded.StartWithWindows);
        Assert.False(loaded.QuickHideEnabled);
    }

    [Fact]
    public async Task FenceDefinition_ThemeProperties_Persisted()
    {
        var fences = new List<FenceDefinition>
        {
            new()
            {
                Title = "Themed",
                BackgroundColor = "#CC223344",
                TitleBarColor = "#44AABBCC",
                TextColor = "#FFEEEEFF",
                Opacity = 0.9
            }
        };

        await _store.SaveFencesAsync(fences);
        var loaded = await _store.LoadFencesAsync();

        Assert.Equal("#CC223344", loaded[0].BackgroundColor);
        Assert.Equal("#44AABBCC", loaded[0].TitleBarColor);
        Assert.Equal("#FFEEEEFF", loaded[0].TextColor);
        Assert.Equal(0.9, loaded[0].Opacity);
    }

    [Fact]
    public void CloneForPersist_SnapshotsMutableCollection_IndependentOfOriginal()
    {
        var settings = new AppSettings { SnapThreshold = 42 };
        settings.RecentClosedFences.Add("a");
        settings.RecentClosedFences.Add("b");

        var snapshot = settings.CloneForPersist();

        // Mutating the original list after the snapshot must not affect the snapshot —
        // this is what removes the UI-thread-mutation vs. background-serialize race.
        settings.RecentClosedFences.Add("c");
        settings.RecentClosedFences.RemoveAt(0);

        Assert.NotSame(settings.RecentClosedFences, snapshot.RecentClosedFences);
        Assert.Equal(new[] { "a", "b" }, snapshot.RecentClosedFences);
        Assert.Equal(42, snapshot.SnapThreshold); // scalar fields copied by value
    }

    [Fact]
    public async Task SaveAndLoadSettings_PreservesRecentClosedFences()
    {
        var settings = new AppSettings();
        settings.RecentClosedFences.Add("{\"fake\":1}");
        settings.RecentClosedFences.Add("{\"fake\":2}");

        await _store.SaveSettingsAsync(settings);
        var loaded = await _store.LoadSettingsAsync();

        Assert.Equal(settings.RecentClosedFences, loaded.RecentClosedFences);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, true); }
        catch { }
    }
}
