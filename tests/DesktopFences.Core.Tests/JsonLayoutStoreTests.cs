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

    [Fact]
    public async Task FenceDefinition_IconStyleOverride_DefaultsToNull()
    {
        var fences = new List<FenceDefinition>
        {
            new() { Title = "NoOverride" }
        };

        await _store.SaveFencesAsync(fences);
        var loaded = await _store.LoadFencesAsync();

        Assert.Null(loaded[0].IconStyleOverride);
    }

    [Fact]
    public async Task FenceDefinition_IconStyleOverride_PreservesNonNullValue()
    {
        var fences = new List<FenceDefinition>
        {
            new() { Title = "WithOverride", IconStyleOverride = FileIconStyle.System }
        };

        await _store.SaveFencesAsync(fences);
        var loaded = await _store.LoadFencesAsync();

        Assert.Equal(FileIconStyle.System, loaded[0].IconStyleOverride);
    }

    [Fact]
    public async Task FenceDefinition_ViewModeAndSortBy_RoundTrip()
    {
        // Phase 14: ViewMode / SortBy(含 Manual)/ SortDirection 必须往返保真。
        var fences = new List<FenceDefinition>
        {
            new()
            {
                Title = "Sorted",
                ViewMode = ViewMode.Detail,
                SortBy = SortField.Manual,
                SortDirection = SortDirection.Descending,
                FilePaths = [@"C:\a.txt", @"C:\b.txt"]
            }
        };

        await _store.SaveFencesAsync(fences);
        var loaded = await _store.LoadFencesAsync();

        Assert.Equal(ViewMode.Detail, loaded[0].ViewMode);
        Assert.Equal(SortField.Manual, loaded[0].SortBy);
        Assert.Equal(SortDirection.Descending, loaded[0].SortDirection);
        // 手动顺序的持久化载体是 FilePaths 顺序，必须原样保留
        Assert.Equal([@"C:\a.txt", @"C:\b.txt"], loaded[0].FilePaths);
    }

    [Fact]
    public async Task FenceDefinition_DefaultsViewModeIcon_SortByName()
    {
        // 旧 JSON 缺这些字段时走 C# 默认值，不应抛异常
        var fencesPath = Path.Combine(_tempDir, "fences.json");
        Directory.CreateDirectory(_tempDir);
        await File.WriteAllTextAsync(fencesPath, """[ { "Title": "Legacy" } ]""");

        var loaded = await _store.LoadFencesAsync();

        Assert.Single(loaded);
        Assert.Equal(ViewMode.Icon, loaded[0].ViewMode);
        Assert.Equal(SortField.Name, loaded[0].SortBy);
        Assert.Equal(SortDirection.Ascending, loaded[0].SortDirection);
    }

    [Fact]
    public async Task LoadFences_UnknownEnumValue_FallsBackToEmpty_AndBacksUp()
    {
        // 记录降级行为：旧版本读到含未知 SortBy(如本版的 "Manual")的新 JSON
        // 时,JsonStringEnumConverter 抛异常 → 整文件备份回退（不丢数据）。
        var fencesPath = Path.Combine(_tempDir, "fences.json");
        Directory.CreateDirectory(_tempDir);
        await File.WriteAllTextAsync(fencesPath,
            """[ { "Title": "Future", "SortBy": "Bogus" } ]""");

        var loaded = await _store.LoadFencesAsync();

        Assert.Empty(loaded);
        Assert.Single(_store.LoadFailures);
        Assert.NotNull(_store.LoadFailures[0].BackupPath);
        Assert.True(File.Exists(_store.LoadFailures[0].BackupPath));
    }

    [Fact]
    public async Task AppSettings_LegacyFenceBlurRadius_NonZero_MigratesToBlurEnabled()
    {
        // 模拟 Phase 11 之前的 settings.json：仅有 int FenceBlurRadius 字段
        var settingsPath = Path.Combine(_tempDir, "settings.json");
        Directory.CreateDirectory(_tempDir);
        await File.WriteAllTextAsync(settingsPath, """{ "FenceBlurRadius": 26 }""");

        var loaded = await _store.LoadSettingsAsync();

        Assert.True(loaded.FenceBlurEnabled);
    }

    [Fact]
    public async Task AppSettings_LegacyFenceBlurRadius_Zero_MigratesToBlurDisabled()
    {
        var settingsPath = Path.Combine(_tempDir, "settings.json");
        Directory.CreateDirectory(_tempDir);
        await File.WriteAllTextAsync(settingsPath, """{ "FenceBlurRadius": 0 }""");

        var loaded = await _store.LoadSettingsAsync();

        Assert.False(loaded.FenceBlurEnabled);
    }

    [Fact]
    public async Task AppSettings_RoundTrip_DoesNotEmitLegacyFenceBlurRadius()
    {
        // 旧 JSON 触发迁移 → Save 重写 → 文件中不应再出现 FenceBlurRadius 键
        var settingsPath = Path.Combine(_tempDir, "settings.json");
        Directory.CreateDirectory(_tempDir);
        await File.WriteAllTextAsync(settingsPath, """{ "FenceBlurRadius": 26 }""");

        var loaded = await _store.LoadSettingsAsync();
        await _store.SaveSettingsAsync(loaded);

        var rewritten = await File.ReadAllTextAsync(settingsPath);
        Assert.DoesNotContain("FenceBlurRadius", rewritten);
        Assert.Contains("FenceBlurEnabled", rewritten);
    }

    // ── 损坏数据回退（H1：损坏 JSON 不再炸掉启动链 / 不被自动保存覆盖丢数据） ──

    [Fact]
    public async Task LoadFences_CorruptJson_FallsBackToEmpty_AndBacksUpOriginal()
    {
        var fencesPath = Path.Combine(_tempDir, "fences.json");
        Directory.CreateDirectory(_tempDir);
        await File.WriteAllTextAsync(fencesPath, "{ this is not valid json !!!");

        var loaded = await _store.LoadFencesAsync();

        Assert.Empty(loaded);                       // 回退默认而不是抛异常
        Assert.Single(_store.LoadFailures);          // 失败被记录，App 据此提示用户
        var failure = _store.LoadFailures[0];
        Assert.Equal(fencesPath, failure.FilePath);
        Assert.NotNull(failure.BackupPath);
        Assert.True(File.Exists(failure.BackupPath)); // 原文件已备份，数据未丢
        Assert.Equal("{ this is not valid json !!!",
            await File.ReadAllTextAsync(failure.BackupPath!));
    }

    [Fact]
    public async Task LoadSettings_CorruptJson_FallsBackToDefaults()
    {
        var settingsPath = Path.Combine(_tempDir, "settings.json");
        Directory.CreateDirectory(_tempDir);
        await File.WriteAllTextAsync(settingsPath, """{ "FenceOpacity": "not-a-number" """);

        var loaded = await _store.LoadSettingsAsync();

        Assert.NotNull(loaded); // 默认 AppSettings，而不是异常
        Assert.Single(_store.LoadFailures);
    }

    [Fact]
    public async Task LoadFences_CorruptJson_ThenSave_DoesNotDestroyBackup()
    {
        var fencesPath = Path.Combine(_tempDir, "fences.json");
        Directory.CreateDirectory(_tempDir);
        await File.WriteAllTextAsync(fencesPath, "garbage");

        await _store.LoadFencesAsync();
        var backupPath = _store.LoadFailures[0].BackupPath!;

        // 后续正常保存会重写 fences.json，但备份必须保留
        await _store.SaveFencesAsync([new FenceDefinition { Title = "New" }]);

        Assert.True(File.Exists(backupPath));
        var reloaded = await _store.LoadFencesAsync();
        Assert.Single(reloaded);
        Assert.Equal("New", reloaded[0].Title);
    }

    [Fact]
    public async Task LoadFences_TransientIoLock_Propagates_AndDoesNotResetOrBackUp()
    {
        // 瞬时 IO 错误（文件被独占锁定，如杀软/备份工具）≠ 内容损坏：必须向上
        // 抛出，让 App 设置 _loadFailed 禁用保存，绝不回退默认值再被自动保存覆盖。
        var fencesPath = Path.Combine(_tempDir, "fences.json");
        Directory.CreateDirectory(_tempDir);
        var good = """[ { "Title": "Good" } ]""";
        await File.WriteAllTextAsync(fencesPath, good);

        // FileShare.None 独占锁定 → 内部 File.OpenRead 抛 IOException（共享冲突）
        using (var _ = new FileStream(fencesPath, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            await Assert.ThrowsAnyAsync<IOException>(() => _store.LoadFencesAsync());
        }

        Assert.Empty(_store.LoadFailures);                                   // 未当作"损坏"记录
        Assert.Empty(Directory.GetFiles(_tempDir, "fences.json.corrupt-*")); // 未备份/未污染目录
        Assert.Equal(good, await File.ReadAllTextAsync(fencesPath));         // 原文件原样保留
    }

    // ── 并发保存串行化（H4：并发写同一 .tmp 不再 IOException 丢保存） ──

    [Fact]
    public async Task ConcurrentSaveFences_DoNotThrow_AndResultIsValidJson()
    {
        var tasks = Enumerable.Range(0, 20).Select(i =>
            _store.SaveFencesAsync([new FenceDefinition { Title = $"Fence {i}" }]));

        await Task.WhenAll(tasks); // 修复前：并发 File.Create 同一 .tmp 会抛 IOException

        var loaded = await _store.LoadFencesAsync();
        Assert.Single(loaded);
        Assert.StartsWith("Fence ", loaded[0].Title);
        Assert.Empty(_store.LoadFailures); // 文件未被并发写坏
    }

    // ── 原子写失败清理（L1：序列化异常不再残留 .tmp） ──

    private sealed class ThrowsOnSerialize
    {
        public string Boom => throw new InvalidOperationException("boom");
    }

    [Fact]
    public async Task WriteAtomic_SerializationFails_CleansUpTempFile_AndKeepsOriginal()
    {
        Directory.CreateDirectory(_tempDir);
        var path = Path.Combine(_tempDir, "victim.json");
        await File.WriteAllTextAsync(path, """{"ok":true}""");

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            JsonFileStore.WriteAtomicAsync(path, new ThrowsOnSerialize(),
                new System.Text.Json.JsonSerializerOptions()));

        Assert.False(File.Exists(path + ".tmp"));                       // 无 .tmp 残留
        Assert.Equal("""{"ok":true}""", await File.ReadAllTextAsync(path)); // 原文件未被破坏
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, true); }
        catch { /* cleanup */ }
    }
}
