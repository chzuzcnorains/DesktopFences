using System.Text.Json;
using System.Text.Json.Serialization;
using DesktopFences.Core.Models;

namespace DesktopFences.Core.Services;

public class JsonLayoutStore : ILayoutStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly string _dataDir;
    private string FencesPath => Path.Combine(_dataDir, "fences.json");
    private string RulesPath => Path.Combine(_dataDir, "rules.json");
    private string PagesPath => Path.Combine(_dataDir, "pages.json");
    private string SettingsPath => Path.Combine(_dataDir, "settings.json");
    private string HiddenFilesPath => Path.Combine(_dataDir, "hidden_files.json");
    private string SnapshotsDir => Path.Combine(_dataDir, "snapshots");
    private string MonitorLayoutsDir => Path.Combine(_dataDir, "monitor-layouts");

    public JsonLayoutStore(string? dataDir = null)
    {
        _dataDir = dataDir ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "DesktopFences");
        Directory.CreateDirectory(_dataDir);
    }

    public async Task<List<FenceDefinition>> LoadFencesAsync()
    {
        if (!File.Exists(FencesPath))
            return [];

        await using var stream = File.OpenRead(FencesPath);
        return await JsonSerializer.DeserializeAsync<List<FenceDefinition>>(stream, JsonOptions)
               ?? [];
    }

    public async Task SaveFencesAsync(IEnumerable<FenceDefinition> fences)
    {
        var list = fences.ToList();
        // Atomic write: write to temp file, then rename
        var tempPath = FencesPath + ".tmp";
        await using (var stream = File.Create(tempPath))
        {
            await JsonSerializer.SerializeAsync(stream, list, JsonOptions);
        }
        File.Move(tempPath, FencesPath, overwrite: true);
    }

    public async Task<List<ClassificationRule>> LoadRulesAsync()
    {
        if (!File.Exists(RulesPath))
            return [];

        await using var stream = File.OpenRead(RulesPath);
        return await JsonSerializer.DeserializeAsync<List<ClassificationRule>>(stream, JsonOptions)
               ?? [];
    }

    public async Task SaveRulesAsync(IEnumerable<ClassificationRule> rules)
    {
        var list = rules.ToList();
        var tempPath = RulesPath + ".tmp";
        await using (var stream = File.Create(tempPath))
        {
            await JsonSerializer.SerializeAsync(stream, list, JsonOptions);
        }
        File.Move(tempPath, RulesPath, overwrite: true);
    }

    public async Task<List<LayoutSnapshot>> LoadSnapshotsAsync()
    {
        if (!Directory.Exists(SnapshotsDir))
            return [];

        var snapshots = new List<LayoutSnapshot>();
        foreach (var file in Directory.GetFiles(SnapshotsDir, "*.json"))
        {
            await using var stream = File.OpenRead(file);
            var snapshot = await JsonSerializer.DeserializeAsync<LayoutSnapshot>(stream, JsonOptions);
            if (snapshot is not null)
                snapshots.Add(snapshot);
        }
        return snapshots;
    }

    public async Task SaveSnapshotAsync(LayoutSnapshot snapshot)
    {
        Directory.CreateDirectory(SnapshotsDir);
        var path = Path.Combine(SnapshotsDir, $"{snapshot.Id}.json");
        var tempPath = path + ".tmp";
        await using (var stream = File.Create(tempPath))
        {
            await JsonSerializer.SerializeAsync(stream, snapshot, JsonOptions);
        }
        File.Move(tempPath, path, overwrite: true);
    }

    public Task DeleteSnapshotAsync(Guid snapshotId)
    {
        var path = Path.Combine(SnapshotsDir, $"{snapshotId}.json");
        if (File.Exists(path))
            File.Delete(path);
        return Task.CompletedTask;
    }

    public async Task SaveMonitorLayoutAsync(string configHash, IEnumerable<FenceDefinition> fences)
    {
        Directory.CreateDirectory(MonitorLayoutsDir);
        var list = fences.ToList();
        var path = Path.Combine(MonitorLayoutsDir, $"{configHash}.json");
        var tempPath = path + ".tmp";
        await using (var stream = File.Create(tempPath))
        {
            await JsonSerializer.SerializeAsync(stream, list, JsonOptions);
        }
        File.Move(tempPath, path, overwrite: true);
    }

    public async Task<List<FenceDefinition>?> LoadMonitorLayoutAsync(string configHash)
    {
        var path = Path.Combine(MonitorLayoutsDir, $"{configHash}.json");
        if (!File.Exists(path))
            return null;

        await using var stream = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<List<FenceDefinition>>(stream, JsonOptions);
    }

    public async Task<List<DesktopPage>> LoadPagesAsync()
    {
        if (!File.Exists(PagesPath))
            return [];

        await using var stream = File.OpenRead(PagesPath);
        return await JsonSerializer.DeserializeAsync<List<DesktopPage>>(stream, JsonOptions)
               ?? [];
    }

    public async Task SavePagesAsync(IEnumerable<DesktopPage> pages)
    {
        var list = pages.ToList();
        var tempPath = PagesPath + ".tmp";
        await using (var stream = File.Create(tempPath))
        {
            await JsonSerializer.SerializeAsync(stream, list, JsonOptions);
        }
        File.Move(tempPath, PagesPath, overwrite: true);
    }

    public async Task<AppSettings> LoadSettingsAsync()
    {
        if (!File.Exists(SettingsPath))
            return new AppSettings();

        await using var stream = File.OpenRead(SettingsPath);
        return await JsonSerializer.DeserializeAsync<AppSettings>(stream, JsonOptions)
               ?? new AppSettings();
    }

    public async Task SaveSettingsAsync(AppSettings settings)
    {
        var tempPath = SettingsPath + ".tmp";
        await using (var stream = File.Create(tempPath))
        {
            await JsonSerializer.SerializeAsync(stream, settings, JsonOptions);
        }
        File.Move(tempPath, SettingsPath, overwrite: true);
    }

    public async Task SaveHiddenFilesAsync(IEnumerable<string> paths)
    {
        var list = paths.ToList();
        var tempPath = HiddenFilesPath + ".tmp";
        await using (var stream = File.Create(tempPath))
        {
            await JsonSerializer.SerializeAsync(stream, list, JsonOptions);
        }
        File.Move(tempPath, HiddenFilesPath, overwrite: true);
    }

    public async Task<List<string>> LoadHiddenFilesAsync()
    {
        if (!File.Exists(HiddenFilesPath))
            return [];

        await using var stream = File.OpenRead(HiddenFilesPath);
        return await JsonSerializer.DeserializeAsync<List<string>>(stream, JsonOptions)
               ?? [];
    }
}
