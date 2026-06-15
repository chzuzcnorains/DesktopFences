using System.Text.Json;
using System.Text.Json.Serialization;
using DesktopFences.Core.Models;

namespace DesktopFences.Core.Services;

/// <summary>
/// Details of a data file that failed to load (corrupt JSON / IO error).
/// The original file is preserved as <see cref="BackupPath"/> before the
/// store falls back to defaults, so user data is never silently destroyed.
/// </summary>
public sealed record LoadFailure(string FilePath, string? BackupPath, Exception Error);

public class JsonLayoutStore : ILayoutStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly string _dataDir;
    // Serializes all writes so concurrent saves (auto-save timer on a thread-pool
    // thread vs. direct calls on the UI thread) never race on the same .tmp file.
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private readonly List<LoadFailure> _loadFailures = [];

    private string FencesPath => Path.Combine(_dataDir, "fences.json");
    private string RulesPath => Path.Combine(_dataDir, "rules.json");
    private string PagesPath => Path.Combine(_dataDir, "pages.json");
    private string SettingsPath => Path.Combine(_dataDir, "settings.json");
    private string HiddenFilesPath => Path.Combine(_dataDir, "hidden_files.json");
    private string SnapshotsDir => Path.Combine(_dataDir, "snapshots");
    private string MonitorLayoutsDir => Path.Combine(_dataDir, "monitor-layouts");

    /// <summary>
    /// Load failures collected since startup. Non-empty means at least one data
    /// file was corrupt; callers should notify the user (the corrupt original is
    /// preserved at <see cref="LoadFailure.BackupPath"/>).
    /// </summary>
    public IReadOnlyList<LoadFailure> LoadFailures => _loadFailures;

    public JsonLayoutStore(string? dataDir = null)
    {
        _dataDir = dataDir ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "DesktopFences");
        Directory.CreateDirectory(_dataDir);
    }

    /// <summary>
    /// Read a JSON file with a deliberate split between two failure kinds:
    /// <list type="bullet">
    /// <item><b>Corrupt content</b> (<see cref="JsonException"/>): the file is
    /// definitively bad. Copy it to "{path}.corrupt-{timestamp}", record the
    /// failure in <see cref="LoadFailures"/>, and fall back to default — so a
    /// corrupt fences.json can't abort startup, and the original is preserved.</item>
    /// <item><b>Transient IO / permission errors</b> (<see cref="IOException"/>,
    /// <see cref="UnauthorizedAccessException"/>): the file may be perfectly fine
    /// but momentarily locked (antivirus, backup/sync tool). These are
    /// re-thrown rather than reset-to-default: the caller (App startup) sets its
    /// load-failed flag and disables saving for the session, so the still-good
    /// file on disk is never overwritten with an empty fallback.</item>
    /// </list>
    /// </summary>
    private async Task<T?> ReadResilientAsync<T>(string path)
    {
        try
        {
            return await JsonFileStore.ReadAsync<T>(path, JsonOptions);
        }
        catch (JsonException ex)
        {
            // Content is corrupt — back up the original and fall back to default.
            string? backupPath = null;
            try
            {
                backupPath = $"{path}.corrupt-{DateTime.Now:yyyyMMddHHmmss}";
                File.Copy(path, backupPath, overwrite: true);
            }
            catch
            {
                backupPath = null; // best effort — still record the failure
            }
            _loadFailures.Add(new LoadFailure(path, backupPath, ex));
            return default;
        }
        // IOException / UnauthorizedAccessException intentionally NOT caught:
        // a transient lock must not be mistaken for corruption and trigger a
        // default-and-overwrite. Let it propagate so saving is disabled instead.
    }

    private async Task WriteLockedAsync<T>(string path, T value)
    {
        await _writeLock.WaitAsync();
        try
        {
            await JsonFileStore.WriteAtomicAsync(path, value, JsonOptions);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    public async Task<List<FenceDefinition>> LoadFencesAsync()
        => await ReadResilientAsync<List<FenceDefinition>>(FencesPath) ?? [];

    public Task SaveFencesAsync(IEnumerable<FenceDefinition> fences)
        => WriteLockedAsync(FencesPath, fences.ToList());

    public async Task<List<ClassificationRule>> LoadRulesAsync()
        => await ReadResilientAsync<List<ClassificationRule>>(RulesPath) ?? [];

    public Task SaveRulesAsync(IEnumerable<ClassificationRule> rules)
        => WriteLockedAsync(RulesPath, rules.ToList());

    public async Task<List<LayoutSnapshot>> LoadSnapshotsAsync()
    {
        if (!Directory.Exists(SnapshotsDir))
            return [];

        var snapshots = new List<LayoutSnapshot>();
        foreach (var file in Directory.GetFiles(SnapshotsDir, "*.json"))
        {
            var snapshot = await ReadResilientAsync<LayoutSnapshot>(file);
            if (snapshot is not null)
                snapshots.Add(snapshot);
        }
        return snapshots;
    }

    public Task SaveSnapshotAsync(LayoutSnapshot snapshot)
    {
        Directory.CreateDirectory(SnapshotsDir);
        var path = Path.Combine(SnapshotsDir, $"{snapshot.Id}.json");
        return WriteLockedAsync(path, snapshot);
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
        return WriteLockedAsync(path, fences.ToList());
    }

    public Task<List<FenceDefinition>?> LoadMonitorLayoutAsync(string configHash)
    {
        var path = Path.Combine(MonitorLayoutsDir, $"{configHash}.json");
        return ReadResilientAsync<List<FenceDefinition>>(path);
    }

    public async Task<List<DesktopPage>> LoadPagesAsync()
        => await ReadResilientAsync<List<DesktopPage>>(PagesPath) ?? [];

    public Task SavePagesAsync(IEnumerable<DesktopPage> pages)
        => WriteLockedAsync(PagesPath, pages.ToList());

    public async Task<AppSettings> LoadSettingsAsync()
        => await ReadResilientAsync<AppSettings>(SettingsPath) ?? new AppSettings();

    public Task SaveSettingsAsync(AppSettings settings)
        => WriteLockedAsync(SettingsPath, settings);

    public Task SaveHiddenFilesAsync(IEnumerable<string> paths)
        => WriteLockedAsync(HiddenFilesPath, paths.ToList());

    public async Task<List<string>> LoadHiddenFilesAsync()
        => await ReadResilientAsync<List<string>>(HiddenFilesPath) ?? [];
}
