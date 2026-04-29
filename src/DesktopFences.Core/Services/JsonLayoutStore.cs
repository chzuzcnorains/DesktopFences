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
        => await JsonFileStore.ReadAsync<List<FenceDefinition>>(FencesPath, JsonOptions) ?? [];

    public Task SaveFencesAsync(IEnumerable<FenceDefinition> fences)
        => JsonFileStore.WriteAtomicAsync(FencesPath, fences.ToList(), JsonOptions);

    public async Task<List<ClassificationRule>> LoadRulesAsync()
        => await JsonFileStore.ReadAsync<List<ClassificationRule>>(RulesPath, JsonOptions) ?? [];

    public Task SaveRulesAsync(IEnumerable<ClassificationRule> rules)
        => JsonFileStore.WriteAtomicAsync(RulesPath, rules.ToList(), JsonOptions);

    public async Task<List<LayoutSnapshot>> LoadSnapshotsAsync()
    {
        if (!Directory.Exists(SnapshotsDir))
            return [];

        var snapshots = new List<LayoutSnapshot>();
        foreach (var file in Directory.GetFiles(SnapshotsDir, "*.json"))
        {
            var snapshot = await JsonFileStore.ReadAsync<LayoutSnapshot>(file, JsonOptions);
            if (snapshot is not null)
                snapshots.Add(snapshot);
        }
        return snapshots;
    }

    public Task SaveSnapshotAsync(LayoutSnapshot snapshot)
    {
        Directory.CreateDirectory(SnapshotsDir);
        var path = Path.Combine(SnapshotsDir, $"{snapshot.Id}.json");
        return JsonFileStore.WriteAtomicAsync(path, snapshot, JsonOptions);
    }

    public Task DeleteSnapshotAsync(Guid snapshotId)
    {
        var path = Path.Combine(SnapshotsDir, $"{snapshotId}.json");
        if (File.Exists(path))
            File.Delete(path);
        return Task.CompletedTask;
    }

    public Task SaveMonitorLayoutAsync(string configHash, IEnumerable<FenceDefinition> fences)
    {
        Directory.CreateDirectory(MonitorLayoutsDir);
        var path = Path.Combine(MonitorLayoutsDir, $"{configHash}.json");
        return JsonFileStore.WriteAtomicAsync(path, fences.ToList(), JsonOptions);
    }

    public Task<List<FenceDefinition>?> LoadMonitorLayoutAsync(string configHash)
    {
        var path = Path.Combine(MonitorLayoutsDir, $"{configHash}.json");
        return JsonFileStore.ReadAsync<List<FenceDefinition>>(path, JsonOptions);
    }

    public async Task<List<DesktopPage>> LoadPagesAsync()
        => await JsonFileStore.ReadAsync<List<DesktopPage>>(PagesPath, JsonOptions) ?? [];

    public Task SavePagesAsync(IEnumerable<DesktopPage> pages)
        => JsonFileStore.WriteAtomicAsync(PagesPath, pages.ToList(), JsonOptions);

    public async Task<AppSettings> LoadSettingsAsync()
        => await JsonFileStore.ReadAsync<AppSettings>(SettingsPath, JsonOptions) ?? new AppSettings();

    public Task SaveSettingsAsync(AppSettings settings)
        => JsonFileStore.WriteAtomicAsync(SettingsPath, settings, JsonOptions);

    public Task SaveHiddenFilesAsync(IEnumerable<string> paths)
        => JsonFileStore.WriteAtomicAsync(HiddenFilesPath, paths.ToList(), JsonOptions);

    public async Task<List<string>> LoadHiddenFilesAsync()
        => await JsonFileStore.ReadAsync<List<string>>(HiddenFilesPath, JsonOptions) ?? [];
}
