using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Forms;
using System.Windows.Media.Animation;
using DesktopFences.Core.Models;
using DesktopFences.Core.Services;
using DesktopFences.Shell.Desktop;
using DesktopFences.UI.Controls;
using DesktopFences.UI.ViewModels;
using Application = System.Windows.Application;
using MessageBox = System.Windows.MessageBox;

namespace DesktopFences.App;

public partial class App : Application
{
    private Mutex? _singleInstanceMutex;
    private DesktopEmbedManager? _embedManager;
    private JsonLayoutStore? _layoutStore;
    private ShellIconExtractor? _iconExtractor;
    private RuleEngine? _ruleEngine;
    private DesktopFileMonitor? _fileMonitor;
    private PeekManager? _peekManager;
    private QuickHideManager? _quickHideManager;
    private SnapshotManager? _snapshotManager;
    private PageManager? _pageManager;
    private MonitorManager? _monitorManager;
    private PageSwitchManager? _pageSwitchManager;
    private SearchHotkeyManager? _searchHotkeyManager;
    private AppSettings _appSettings = new();
    private readonly Dictionary<Guid, FolderPortalWatcher> _portalWatchers = [];
    private List<ClassificationRule> _rules = [];
    private NotifyIcon? _trayIcon;
    private readonly List<FenceHost> _fenceWindows = [];
    private readonly DateTime _appStartTime = DateTime.Now;
    private System.Timers.Timer? _autoSaveTimer;
    private System.Timers.Timer? _autoOrganizeTimer;
    private System.Timers.Timer? _fileExistenceTimer;
    private bool _isShuttingDown;
    private UI.Controls.SnapGuideOverlay? _snapGuideOverlay;
    private DesktopIconManager? _desktopIconManager;
    private DesktopIconOverlay? _desktopOverlay;
    private readonly string _desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
    private readonly string _publicDesktopPath = Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory);

    private void App_OnStartup(object sender, StartupEventArgs e)
    {
        _singleInstanceMutex = new Mutex(true, "DesktopFences_SingleInstance", out bool isNew);
        if (!isNew)
        {
            MessageBox.Show("DesktopFences 已在运行。", "DesktopFences",
                MessageBoxButton.OK, MessageBoxImage.Information);
            Shutdown();
            return;
        }

        _layoutStore = new JsonLayoutStore();
        _iconExtractor = new ShellIconExtractor();
        _ruleEngine = new RuleEngine();
        _embedManager = new DesktopEmbedManager();
        _embedManager.Start();

        // Snapshot manager
        _snapshotManager = new SnapshotManager(_layoutStore);

        // Page manager
        _pageManager = new PageManager();

        // Monitor manager
        _monitorManager = new MonitorManager();
        _monitorManager.DisplayConfigChanged += OnDisplayConfigChanged;

        // Peek: Win+Space
        _peekManager = new PeekManager();
        _peekManager.PeekToggled += OnPeekToggled;
        _peekManager.Start();

        // Escape exits Peek
        _embedManager.EscapePressed += () =>
            Dispatcher.InvokeAsync(() => _peekManager.OnEscapePressed());

        // Quick Hide: double-click desktop
        _quickHideManager = new QuickHideManager();
        _quickHideManager.DesktopDoubleClick += () =>
            Dispatcher.InvokeAsync(ToggleAllFences);
        _quickHideManager.Start();

        // Page switching disabled — fences stay on whichever Windows virtual desktop
        // they belong to. Windows manages virtual desktop visibility natively.
        _pageSwitchManager = new PageSwitchManager();

        // Search: Ctrl+`
        _searchHotkeyManager = new SearchHotkeyManager();
        _searchHotkeyManager.SearchRequested += () =>
            Dispatcher.InvokeAsync(ShowSearchWindow);
        _searchHotkeyManager.Start();

        SetupTrayIcon();

        // Create the shared snap guide overlay (transparent, click-through window)
        _snapGuideOverlay = new UI.Controls.SnapGuideOverlay();
        _snapGuideOverlay.Show();

        _ = LoadFencesAsync();
        StartAutoSave();
    }

    // ── Auto-Organize Timer ────────────────────────────────────

    private void StartAutoOrganizeTimer()
    {
        if (!_appSettings.AutoOrganizeEnabled) return;

        _autoOrganizeTimer = new System.Timers.Timer(2000) { AutoReset = true };
        _autoOrganizeTimer.Elapsed += async (_, _) =>
        {
            await OrganizeDesktopOnceAsync();
        };
        _autoOrganizeTimer.Start();
    }

    private async Task OrganizeDesktopOnceAsync()
    {
        try
        {
            var allFiles = GetAllDesktopEntries();

            await Dispatcher.InvokeAsync(() =>
            {
                foreach (var filePath in allFiles)
                {
                    if (IsFileAlreadyInAnyFence(filePath))
                        continue;

                    var matchedRule = _ruleEngine?.Match(filePath, _rules);
                    if (matchedRule == null || !matchedRule.IsEnabled)
                        continue;

                    // 查找目标 Fence
                    FencePanelViewModel? targetTab = FindFenceById(matchedRule.TargetFenceId);

                    // 如果没找到，创建新的
                    if (targetTab == null)
                    {
                        targetTab = CreateFenceForRule(matchedRule);
                    }

                    // 添加文件
                    targetTab.AddFile(filePath);
                    _desktopOverlay?.RemoveIcon(filePath);

                    var lastFile = targetTab.Files.LastOrDefault();
                    if (lastFile != null && _iconExtractor != null)
                        lastFile.Icon = _iconExtractor.GetIcon(filePath);
                }
            });
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"OrganizeDesktopOnceAsync Error: {ex}");
        }
    }

    private List<string> GetAllDesktopEntries()
    {
        var entries = new List<string>();
        foreach (var dir in new[] { _desktopPath, _publicDesktopPath })
        {
            try
            {
                entries.AddRange(
                    Directory.GetFiles(dir)
                        .Concat(Directory.GetDirectories(dir)));
            }
            catch { }
        }
        return entries.Where(f =>
        {
            var name = Path.GetFileName(f);
            return !name.StartsWith('.') &&
                   !name.Equals("System Volume Information", StringComparison.OrdinalIgnoreCase);
        }).ToList();
    }

    private bool IsFileAlreadyInAnyFence(string filePath)
        => _fenceWindows.ContainsFile(filePath);


    /// <summary>
    /// When rules change, remove files from Fences that no longer match any enabled rule.
    /// </summary>
    private void ReEvaluateClassifiedFiles()
    {
        bool changed = false;
        foreach (var tab in _fenceWindows.AllTabs())
        {
            // Portal fence 内容来自被映射的外部文件夹,不参与规则分类。
            // 规则引擎不会把外部文件路径匹配到 portal fence,跳过避免被清空。
            if (tab.IsPortalMode) continue;

            var filesToRemove = tab.Files
                .Where(f =>
                {
                    var matched = _ruleEngine?.Match(f.FilePath, _rules);
                    return matched is null || matched.TargetFenceId != tab.Id;
                })
                .Select(f => f.FilePath)
                .ToList();

            foreach (var path in filesToRemove)
            {
                tab.RemoveFile(path);
                changed = true;
                if (IsDesktopFile(path) && (File.Exists(path) || Directory.Exists(path)))
                    _desktopOverlay?.AddIcon(path);
            }
        }
        if (changed)
            RequestAutoSave();
    }

    private void RebuildTrayMenu()
    {
        if (_trayIcon is null) return;
        _trayIcon.ContextMenuStrip = BuildTrayMenu();
    }

    // ── Tray Icon ───────────────────────────────────────────

    private void SetupTrayIcon()
    {
        _trayIcon = new NotifyIcon
        {
            Text = "DesktopFences",
            Icon = new System.Drawing.Icon(
                Application.GetResourceStream(new Uri("pack://application:,,,/Assets/app.ico")).Stream),
            Visible = true,
            ContextMenuStrip = BuildTrayMenu()
        };
        _trayIcon.DoubleClick += (_, _) => Dispatcher.Invoke(ToggleAllFences);
    }

    private ContextMenuStrip BuildTrayMenu()
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add("新建 Fence", null, (_, _) => Dispatcher.Invoke(() => CreateNewFence()));
        menu.Items.Add("新建文件夹映射 Fence...", null, (_, _) => Dispatcher.Invoke(() => CreatePortalFence()));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("显示/隐藏全部", null, (_, _) => Dispatcher.Invoke(ToggleAllFences));
        menu.Items.Add("搜索 (Ctrl+`)", null, (_, _) => Dispatcher.Invoke(ShowSearchWindow));
        menu.Items.Add(new ToolStripSeparator());

        // Snapshots submenu
        var snapshotMenu = new ToolStripMenuItem("布局快照");
        snapshotMenu.DropDownItems.Add("保存当前布局...", null, async (_, _) =>
            await Dispatcher.InvokeAsync(async () => await SaveSnapshotAsync()));
        snapshotMenu.DropDownItems.Add(new ToolStripSeparator());
        snapshotMenu.DropDownOpening += (_, _) => RefreshSnapshotMenu(snapshotMenu);
        menu.Items.Add(snapshotMenu);

        // Recently-closed submenu — populated lazily on open from RecentClosedFences FIFO.
        var recentClosedMenu = new ToolStripMenuItem("恢复最近关闭");
        recentClosedMenu.DropDownOpening += (_, _) => RefreshRecentClosedMenu(recentClosedMenu);
        RefreshRecentClosedMenu(recentClosedMenu); // initial state (count + enabled)
        menu.Items.Add(recentClosedMenu);

        menu.Items.Add(new ToolStripSeparator());
        var autoOrganizeItem = new ToolStripMenuItem("自动整理")
        {
            Checked = _appSettings.AutoOrganizeEnabled,
            CheckOnClick = true
        };
        autoOrganizeItem.CheckedChanged += (_, _) =>
        {
            _appSettings.AutoOrganizeEnabled = autoOrganizeItem.Checked;
            if (autoOrganizeItem.Checked)
                StartAutoOrganizeTimer();
            else
            {
                _autoOrganizeTimer?.Stop();
                _autoOrganizeTimer?.Dispose();
                _autoOrganizeTimer = null;
            }
            _ = _layoutStore!.SaveSettingsAsync(_appSettings);
        };
        menu.Items.Add(autoOrganizeItem);
        menu.Items.Add("立即整理桌面", null, async (_, _) => await OrganizeDesktopOnceAsync());
        menu.Items.Add("分类规则...", null, (_, _) => Dispatcher.Invoke(() => ShowSettings(1)));
        menu.Items.Add("设置...", null, (_, _) => Dispatcher.Invoke(() => ShowSettings(0)));
        menu.Items.Add("保存布局", null, async (_, _) => await SaveFencesAsync());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("退出", null, (_, _) => Dispatcher.Invoke(() =>
        {
            _isShuttingDown = true; // skip RecentClosed recording during the shutdown wave
            _ = SaveFencesAsync();
            Shutdown();
        }));
        return menu;
    }

    private void RefreshRecentClosedMenu(ToolStripMenuItem menu)
    {
        menu.DropDownItems.Clear();

        if (_appSettings.RecentClosedFences.Count == 0)
        {
            menu.DropDownItems.Add(new ToolStripMenuItem("（无最近关闭的 Fence）") { Enabled = false });
            return;
        }

        foreach (var json in _appSettings.RecentClosedFences)
        {
            var (def, _) = DeserializeRecentClosedEntry(json);
            if (def is null) continue;

            var label = $"{def.Title} · {def.FilePaths.Count} 文件";
            var capturedId = def.Id;
            menu.DropDownItems.Add(label, null, (_, _) =>
                Dispatcher.Invoke(() => RestoreClosedFenceById(capturedId)));
        }

        if (menu.DropDownItems.Count > 0)
        {
            menu.DropDownItems.Add(new ToolStripSeparator());
            menu.DropDownItems.Add("清空列表", null, (_, _) => Dispatcher.Invoke(ClearRecentClosedFences));
        }
    }

    /// <summary>
    /// JSON envelope for entries in <see cref="AppSettings.RecentClosedFences"/>: stores
    /// the FenceDefinition together with the close time so the manage-Fences pane can
    /// show "X 分钟前" honestly instead of always rendering "now".
    /// </summary>
    private sealed class RecentClosedFenceEntry
    {
        public FenceDefinition Definition { get; set; } = new();
        public DateTimeOffset ClosedAt { get; set; } = DateTimeOffset.Now;
    }

    /// <summary>
    /// Parse one entry from RecentClosedFences with backward-compatibility for the
    /// pre-envelope format (a bare FenceDefinition JSON without ClosedAt). Legacy entries
    /// fall back to DateTimeOffset.Now so first-upgrade users see "刚刚" — subsequent
    /// closes record the real timestamp.
    /// </summary>
    private static (FenceDefinition? def, DateTimeOffset closedAt) DeserializeRecentClosedEntry(string json)
    {
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("Definition", out _))
            {
                var entry = System.Text.Json.JsonSerializer.Deserialize<RecentClosedFenceEntry>(json);
                if (entry?.Definition is { } d)
                    return (d, entry.ClosedAt);
                return (null, DateTimeOffset.Now);
            }
            // Legacy: bare FenceDefinition JSON.
            var legacy = System.Text.Json.JsonSerializer.Deserialize<FenceDefinition>(json);
            return (legacy, DateTimeOffset.Now);
        }
        catch
        {
            return (null, DateTimeOffset.Now);
        }
    }

    private void ClearRecentClosedFences()
    {
        if (_appSettings.RecentClosedFences.Count == 0) return;
        _appSettings.RecentClosedFences.Clear();
        _ = _layoutStore!.SaveSettingsAsync(_appSettings);
        RebuildTrayMenu();
    }

    private void RefreshSnapshotMenu(ToolStripMenuItem menu)
    {
        while (menu.DropDownItems.Count > 2)
            menu.DropDownItems.RemoveAt(2);

        if (_snapshotManager is null) return;

        foreach (var snapshot in _snapshotManager.Snapshots)
        {
            var item = new ToolStripMenuItem(
                $"{snapshot.Name} ({snapshot.CreatedAt.ToLocalTime():g})");

            var snapshotId = snapshot.Id;
            item.DropDownItems.Add("恢复", null, (_, _) =>
                Dispatcher.Invoke(() => RestoreSnapshot(snapshotId)));
            item.DropDownItems.Add("删除", null, async (_, _) =>
            {
                await _snapshotManager.DeleteSnapshotAsync(snapshotId);
                RefreshSnapshotMenu(menu);
            });

            menu.DropDownItems.Add(item);
        }

        if (_snapshotManager.Snapshots.Count == 0)
        {
            var empty = new ToolStripMenuItem("（无快照）") { Enabled = false };
            menu.DropDownItems.Add(empty);
        }
    }

    // Page menu removed — Windows virtual desktops handle page/desktop management.

    // ── Settings (unified window) ──────────────────────────────

    private void ShowSettings(int tabIndex = 0)
    {
        var fences = _fenceWindows.AllDefinitions();
        var managedFiles = fences.Sum(f => f.FilePaths.Count);
        var closedFences = ParseRecentClosedFences(_appSettings.RecentClosedFences);
        var uptime = DateTime.Now - _appStartTime;

        var settingsWindow = new SettingsWindow(
            _appSettings, _rules, fences,
            closedFences: closedFences,
            managedFileCount: managedFiles,
            uptime: uptime);
        settingsWindow.SelectTab(tabIndex);

        settingsWindow.SettingsSaved += OnSettingsSaved;
        settingsWindow.RulesSaved += newRules =>
        {
            _rules = newRules;
            _ = SaveRulesAsync();

            // 为启用的规则创建缺失的 Fence
            foreach (var rule in _rules.Where(r => r.IsEnabled))
            {
                var existingFence = FindFenceById(rule.TargetFenceId);
                if (existingFence == null)
                {
                    CreateFenceForRule(rule);
                }
            }

            ReEvaluateClassifiedFiles();
        };

        settingsWindow.NewFenceRequested           += () => CreateNewFence();
        settingsWindow.SaveSnapshotRequested       += () => _ = SaveSnapshotAsync();
        settingsWindow.ExportLayoutRequested       += () => ExportLayout();
        settingsWindow.ImportLayoutRequested       += () => ImportLayout();
        settingsWindow.RestoreClosedFenceRequested += id =>
        {
            RestoreClosedFenceById(id);
            settingsWindow.NotifyClosedFenceRemoved(id);
        };
        settingsWindow.DeleteClosedFenceRequested += id =>
        {
            if (DeleteClosedFenceById(id))
                settingsWindow.NotifyClosedFenceRemoved(id);
        };

        settingsWindow.ResetLayoutRequested        += () => { ResetAllFences(); settingsWindow.Close(); };
        settingsWindow.ClearRulesRequested         += () => { ClearAllRules(); settingsWindow.Close(); };
        settingsWindow.RestoreDefaultsRequested    += () => { RestoreDefaultSettings(); settingsWindow.Close(); };

        settingsWindow.ShowDialog();
    }

    /// <summary>
    /// Decode the FIFO list of serialized FenceDefinition entries into preview records.
    /// Bad entries are skipped silently — the list is best-effort.
    /// </summary>
    private static List<DesktopFences.UI.Controls.Settings.FencesManageSettingsPane.ClosedFenceRecord>
        ParseRecentClosedFences(List<string> raw)
    {
        var result = new List<DesktopFences.UI.Controls.Settings.FencesManageSettingsPane.ClosedFenceRecord>();
        foreach (var json in raw)
        {
            var (def, closedAt) = DeserializeRecentClosedEntry(json);
            if (def is null) continue;
            result.Add(new DesktopFences.UI.Controls.Settings.FencesManageSettingsPane.ClosedFenceRecord(
                Id: def.Id,
                Title: def.Title,
                TabTitles: [def.Title],
                FileCount: def.FilePaths.Count,
                ClosedAt: closedAt));
        }
        return result;
    }

    /// <summary>
    /// Self-contained on-disk envelope for layout export/import. Version=1 contains the
    /// current fences (with tab grouping), rules, and full AppSettings — enough to clone
    /// a setup onto another machine.
    /// </summary>
    private sealed class LayoutExport
    {
        public int Version { get; set; } = 1;
        public DateTimeOffset ExportedAt { get; set; }
        public List<FenceDefinition> Fences { get; set; } = [];
        public List<ClassificationRule> Rules { get; set; } = [];
        public AppSettings Settings { get; set; } = new();
    }

    private void ExportLayout()
    {
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = "导出 DesktopFences 布局",
            Filter = "DesktopFences 布局 (*.dfences.json)|*.dfences.json|JSON (*.json)|*.json",
            FileName = $"desktopfences-layout-{DateTime.Now:yyyyMMdd-HHmm}.dfences.json",
            DefaultExt = ".dfences.json",
        };
        if (dialog.ShowDialog() != true) return;

        try
        {
            var payload = new LayoutExport
            {
                Version = 1,
                ExportedAt = DateTimeOffset.Now,
                Fences = _fenceWindows.AllDefinitions(),
                Rules = _rules,
                Settings = _appSettings,
            };
            var options = new System.Text.Json.JsonSerializerOptions { WriteIndented = true };
            var json = System.Text.Json.JsonSerializer.Serialize(payload, options);
            File.WriteAllText(dialog.FileName, json);

            ShowToast($"已导出 {payload.Fences.Count} 个 Fence、{payload.Rules.Count} 条规则到\n{dialog.FileName}");
        }
        catch (Exception ex)
        {
            MessageBox.Show($"导出失败：{ex.Message}", "DesktopFences",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ImportLayout()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "导入 DesktopFences 布局",
            Filter = "DesktopFences 布局 (*.dfences.json;*.json)|*.dfences.json;*.json|所有文件 (*.*)|*.*",
        };
        if (dialog.ShowDialog() != true) return;

        LayoutExport? payload;
        try
        {
            var json = File.ReadAllText(dialog.FileName);
            payload = System.Text.Json.JsonSerializer.Deserialize<LayoutExport>(json);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"无法解析文件：{ex.Message}", "DesktopFences",
                MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }
        if (payload is null)
        {
            MessageBox.Show("文件不包含有效的布局数据。", "DesktopFences",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var confirm = MessageBox.Show(
            $"将替换当前 {_fenceWindows.Count} 个 Fence、{_rules.Count} 条规则与全部应用设置，导入：\n" +
            $"  Fence: {payload.Fences.Count}\n  规则: {payload.Rules.Count}\n  导出时间: {payload.ExportedAt:g}\n\n" +
            $"是否继续？",
            "DesktopFences · 导入布局",
            MessageBoxButton.OKCancel,
            MessageBoxImage.Question);
        if (confirm != MessageBoxResult.OK) return;

        try
        {
            // Tear down existing fences without recording them as recently-closed.
            foreach (var host in _fenceWindows.ToList())
            {
                host.IsBeingReplaced = true;
                host.Close();
            }
            _fenceWindows.Clear();
            _pageManager?.Initialize([]);

            _rules = payload.Rules;
            _appSettings = payload.Settings ?? new AppSettings();

            ApplyIconAppearance(_appSettings);
            ApplyFenceShadow(_appSettings);

            SpawnFencesWithGroups(payload.Fences, bringToFront: true);
            foreach (var tab in _fenceWindows.AllTabs())
                _pageManager?.AssignFenceToCurrentPage(tab.Id);

            _ = _layoutStore!.SaveSettingsAsync(_appSettings);
            _ = SaveRulesAsync();
            RequestAutoSave();
            RebuildTrayMenu();
            ShowToast($"已导入 {payload.Fences.Count} 个 Fence、{payload.Rules.Count} 条规则。");
        }
        catch (Exception ex)
        {
            MessageBox.Show($"导入失败：{ex.Message}", "DesktopFences",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void RestoreClosedFenceById(Guid id)
    {
        // Pop matching entry from the FIFO + spawn a fresh fence with that definition.
        for (int i = 0; i < _appSettings.RecentClosedFences.Count; i++)
        {
            var (def, _) = DeserializeRecentClosedEntry(_appSettings.RecentClosedFences[i]);
            if (def is null || def.Id != id) continue;

            _appSettings.RecentClosedFences.RemoveAt(i);

            // The original tab group no longer exists — restore as a standalone fence.
            def.TabGroupId = null;
            def.TabOrder = 0;

            var vm = new FencePanelViewModel(def);
            SpawnFenceWindow(vm, bringToFront: true);
            _pageManager?.AssignFenceToCurrentPage(def.Id);
            _ = _layoutStore!.SaveSettingsAsync(_appSettings);
            RebuildTrayMenu();
            RequestAutoSave();
            return;
        }
    }

    /// <summary>
    /// Permanently drop one entry from RecentClosedFences (Settings → 最近关闭 → 删除).
    /// Returns true if an entry was actually removed.
    /// </summary>
    private bool DeleteClosedFenceById(Guid id)
    {
        for (int i = 0; i < _appSettings.RecentClosedFences.Count; i++)
        {
            var (def, _) = DeserializeRecentClosedEntry(_appSettings.RecentClosedFences[i]);
            if (def is null || def.Id != id) continue;

            _appSettings.RecentClosedFences.RemoveAt(i);
            _ = _layoutStore!.SaveSettingsAsync(_appSettings);
            RebuildTrayMenu();
            return true;
        }
        return false;
    }

    /// <summary>
    /// Push the given fence definitions onto the RecentClosedFences FIFO (≤20),
    /// persist settings, raise a tray balloon, and rebuild the tray menu so the
    /// "Recently closed" submenu reflects the new entries.
    /// </summary>
    private void RecordRecentlyClosedFences(List<FenceDefinition> defs)
    {
        var now = DateTimeOffset.Now;
        // Insert in reverse order so the first def in `defs` ends up on top of the FIFO.
        foreach (var def in defs.AsEnumerable().Reverse())
        {
            try
            {
                var entry = new RecentClosedFenceEntry { Definition = def, ClosedAt = now };
                var json = System.Text.Json.JsonSerializer.Serialize(entry);
                _appSettings.RecentClosedFences.Insert(0, json);
            }
            catch { /* skip un-serializable */ }
        }
        const int Limit = 20;
        while (_appSettings.RecentClosedFences.Count > Limit)
            _appSettings.RecentClosedFences.RemoveAt(_appSettings.RecentClosedFences.Count - 1);

        _ = _layoutStore!.SaveSettingsAsync(_appSettings);

        var msg = defs.Count == 1
            ? $"已关闭 \"{defs[0].Title}\"，可在托盘菜单 → 恢复最近关闭 中找回。"
            : $"已关闭 {defs.Count} 个 Fence，可在托盘菜单 → 恢复最近关闭 中找回。";
        _trayIcon?.ShowBalloonTip(2000, "DesktopFences", msg, ToolTipIcon.Info);

        RebuildTrayMenu();
    }

    private void ResetAllFences()
    {
        foreach (var host in _fenceWindows.ToList())
        {
            // Page/portal cleanup still runs; only suppress "recently closed" recording —
            // the user invoked a reset, they don't want the wiped fences to reappear in
            // the restore submenu.
            host.IsBeingReplaced = true;
            host.Close();
        }
        _fenceWindows.Clear();
        _pageManager?.Initialize([]);

        var (defaultFences, _) = CreateDefaultConfiguration();
        foreach (var def in defaultFences)
        {
            var vm = new FencePanelViewModel(def);
            SpawnFenceWindow(vm, bringToFront: true);
            _pageManager?.AssignFenceToCurrentPage(def.Id);
        }
        RequestAutoSave();
    }

    private void ClearAllRules()
    {
        _rules = [];
        _ = SaveRulesAsync();
        ReEvaluateClassifiedFiles();
    }

    private void RestoreDefaultSettings()
    {
        // Preserve persisted-only state (recent closed list) so users don't lose recoverable fences.
        var preservedClosed = _appSettings.RecentClosedFences;
        _appSettings = new AppSettings { RecentClosedFences = preservedClosed };
        OnSettingsSaved(_appSettings);
    }

    private static void ShowToast(string message)
    {
        MessageBox.Show(message, "DesktopFences",
            MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void OnSettingsSaved(AppSettings settings)
    {
        _appSettings = settings;

        StartupManager.SetEnabled(settings.StartWithWindows);

        if (!settings.QuickHideEnabled)
            _quickHideManager?.Stop();
        else if (_quickHideManager is not null)
            _quickHideManager.Start();

        ApplyIconAppearance(settings);
        ApplyFenceShadow(settings);

        var fenceBg = ComputeFenceBgColor(settings.FenceBgHue, settings.FenceOpacity);

        foreach (var host in _fenceWindows)
        {
            host.Panel.ApplyDefaultTheme(
                fenceBg,
                settings.DefaultTitleBarColor,
                settings.DefaultTextColor,
                settings.TitleBarFontSize);
            ApplyHostStyle(host, settings);
            host.Panel.RefreshFileTileTemplate();

            // Update snap threshold on existing hosts
            host.SnapThreshold = settings.SnapThreshold;
            host.Panel.SnapThreshold = settings.SnapThreshold;
        }

        _ = _layoutStore!.SaveSettingsAsync(settings);
    }

    /// <summary>
    /// Push IconStyle / UseCustomFileIcons / IconSize / AccentColor from AppSettings to
    /// Application.Resources so DynamicResource consumers (FileIconTemplateSelector,
    /// AccentBrush, tile-size keys) pick them up live. Safe to call multiple times.
    /// </summary>
    private void ApplyIconAppearance(AppSettings settings)
    {
        var clamped = Math.Max(28, Math.Min(64, settings.IconSize));
        Resources["IconStyle"] = settings.IconStyle.ToString();
        Resources["UseCustomFileIcons"] = settings.UseCustomFileIcons;
        Resources["FileTileIconSize"] = (double)clamped;
        Resources["FileTileWidth"] = (double)(clamped + 44);
        Resources["FileTileHeight"] = (double)(clamped + 52);

        // Push AccentColor → DarkTheme.xaml AccentBrush rebinds via DynamicResource.
        // AccentStrongColor is derived (~85% lightness) so the segmented-tab gradient stays cohesive.
        if (TryParseColor(settings.AccentColor, out var accent))
        {
            Resources["AccentColor"] = accent;
            Resources["AccentStrongColor"] = System.Windows.Media.Color.FromRgb(
                (byte)(accent.R * 0.78), (byte)(accent.G * 0.78), (byte)(accent.B * 0.78));
        }
    }

    /// <summary>
    /// Apply the edge DropShadow that complements DWM blur. FencePanel.xaml references
    /// this via DynamicResource so existing fences pick up the new shadow live.
    /// FenceBlurEnabled=false disables the shadow entirely; when enabled, uses the
    /// legacy 26-px radius (the prior default of FenceBlurRadius before it became binary).
    /// </summary>
    private void ApplyFenceShadow(AppSettings settings)
    {
        var blur = settings.FenceBlurEnabled ? 26 : 0;
        Resources["FenceShadowEffect"] = new System.Windows.Media.Effects.DropShadowEffect
        {
            BlurRadius = blur,
            ShadowDepth = blur > 0 ? Math.Min(12, blur * 0.4) : 0,
            Direction = 270,
            Opacity = blur > 0 ? 0.45 : 0,
            Color = System.Windows.Media.Colors.Black,
        };
    }

    /// <summary>
    /// Compute the fence body background ARGB hex from FenceBgHue + FenceOpacity, using the
    /// same HSL formula (S=0.30, L=0.18) as the Appearance pane preview. Returns "#AARRGGBB".
    /// </summary>
    private static string ComputeFenceBgColor(int hue, double opacity)
    {
        var alpha = (byte)Math.Round(Math.Max(0.20, Math.Min(0.90, opacity)) * 255);
        var rgb = HslToRgb(hue, 0.30, 0.18);
        return $"#{alpha:X2}{rgb.R:X2}{rgb.G:X2}{rgb.B:X2}";
    }

    private static System.Windows.Media.Color HslToRgb(double h, double s, double l)
    {
        h = ((h % 360) + 360) % 360 / 360.0;
        double r, g, b;
        if (s == 0)
        {
            r = g = b = l;
        }
        else
        {
            double q = l < 0.5 ? l * (1 + s) : l + s - l * s;
            double p = 2 * l - q;
            r = HueToRgb(p, q, h + 1.0 / 3);
            g = HueToRgb(p, q, h);
            b = HueToRgb(p, q, h - 1.0 / 3);
        }
        return System.Windows.Media.Color.FromRgb(
            (byte)Math.Round(r * 255),
            (byte)Math.Round(g * 255),
            (byte)Math.Round(b * 255));
    }

    private static double HueToRgb(double p, double q, double t)
    {
        if (t < 0) t += 1;
        if (t > 1) t -= 1;
        if (t < 1.0 / 6) return p + (q - p) * 6 * t;
        if (t < 0.5) return q;
        if (t < 2.0 / 3) return p + (q - p) * (2.0 / 3 - t) * 6;
        return p;
    }

    private static bool TryParseColor(string? hex, out System.Windows.Media.Color color)
    {
        color = default;
        if (string.IsNullOrWhiteSpace(hex)) return false;
        try
        {
            color = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(hex);
            return true;
        }
        catch
        {
            return false;
        }
    }

    // ── Search ────────────────────────────────────────────────

    private void ShowSearchWindow()
    {
        var results = _fenceWindows.AllTabs()
            .SelectMany(tab => tab.Files.Select(file => new SearchWindow.SearchResult
            {
                FilePath = file.FilePath,
                DisplayName = file.DisplayName,
                FenceName = tab.Title,
                FenceId = tab.Id,
                Icon = file.Icon
            }))
            .ToList();

        var searchWindow = new SearchWindow();
        searchWindow.SetItems(results);
        searchWindow.ResultSelected += OnSearchResultSelected;
        searchWindow.Show();
    }

    private void OnSearchResultSelected(string filePath, Guid fenceId)
    {
        var targetHost = _fenceWindows.FindHostByTabId(fenceId);
        if (targetHost is null) return;

        // Switch to the correct tab if this is a grouped window
        targetHost.ActivateTab(fenceId);

        if (!targetHost.IsVisible)
            targetHost.Show();

        targetHost.Activate();
        ShellFileOperations.OpenFile(filePath);
    }

    // ── Snapshots ─────────────────────────────────────────────

    private async Task SaveSnapshotAsync()
    {
        if (_snapshotManager is null || _monitorManager is null) return;

        var name = $"快照 {_snapshotManager.Snapshots.Count + 1}";
        var definitions = _fenceWindows.AllDefinitions();
        var screenConfig = _monitorManager.GetScreenConfiguration();
        await _snapshotManager.CreateSnapshotAsync(name, definitions, screenConfig);
    }

    private void RestoreSnapshot(Guid snapshotId)
    {
        if (_snapshotManager is null) return;

        var fences = _snapshotManager.RestoreSnapshot(snapshotId);
        if (fences is null) return;

        foreach (var host in _fenceWindows.ToList())
        {
            host.IsBeingReplaced = true;
            host.Close();
        }
        _fenceWindows.Clear();

        SpawnFencesWithGroups(fences, bringToFront: true);
        RequestAutoSave();
    }

    // ── Desktop Pages ─────────────────────────────────────────

    // Page switching is disabled — Windows virtual desktops handle visibility natively.
    // All fences are assigned to page 0 for data persistence purposes only.

    // ── Multi-Monitor ─────────────────────────────────────────

    private void OnDisplayConfigChanged(string newConfigHash)
    {
        Dispatcher.InvokeAsync(async () =>
        {
            if (_monitorManager is null || _layoutStore is null) return;

            var oldHash = _monitorManager.CurrentConfigHash;
            var currentDefs = _fenceWindows.Select(f => f.ViewModel.Model).ToList();
            await _layoutStore.SaveMonitorLayoutAsync(oldHash, currentDefs);

            var savedLayout = await _layoutStore.LoadMonitorLayoutAsync(newConfigHash);
            if (savedLayout is not null)
            {
                foreach (var host in _fenceWindows.ToList())
                {
                    host.IsBeingReplaced = true;
                    host.Close();
                }
                _fenceWindows.Clear();

                SpawnFencesWithGroups(savedLayout);
            }
            else
            {
                foreach (var host in _fenceWindows)
                {
                    var vm = host.ViewModel;
                    var clamped = MonitorManager.ClampToMonitor(vm.Model.Bounds, vm.MonitorIndex);
                    vm.X = clamped.X;
                    vm.Y = clamped.Y;
                    vm.Width = clamped.Width;
                    vm.Height = clamped.Height;
                    host.Left = clamped.X;
                    host.Top = clamped.Y;
                    host.Width = clamped.Width + 8;
                    host.Height = clamped.Height + 8;
                }
            }

            RequestAutoSave();
        });
    }

    // ── Fence Management ────────────────────────────────────

    private FencePanelViewModel? FindFenceById(Guid fenceId)
        => fenceId == Guid.Empty ? null : _fenceWindows.FindTabById(fenceId);

    private FencePanelViewModel CreateFenceForRule(ClassificationRule rule)
    {
        // 寻找一个合适的位置 - 在现有 Fence 附近或在 200, 200
        double x = 200, y = 200;
        if (_fenceWindows.Count > 0)
        {
            var lastFence = _fenceWindows.Last();
            x = lastFence.Left + 50;
            y = lastFence.Top + 50;
        }

        // 创建新 Fence
        var definition = new FenceDefinition
        {
            Title = rule.Name, // 使用规则名称作为 Fence 名称
            Bounds = new FenceRect { X = x, Y = y, Width = 300, Height = 200 },
            MonitorIndex = MonitorManager.GetMonitorIndexForPoint(x, y),
            PageIndex = _pageManager?.CurrentPageIndex ?? 0,
            RuleIds = new List<Guid> { rule.Id }
        };

        // 更新规则的 TargetFenceId 指向新创建的 Fence
        rule.TargetFenceId = definition.Id;
        _ = SaveRulesAsync();

        var vm = new FencePanelViewModel(definition);
        SpawnFenceWindow(vm, bringToFront: true);
        _pageManager?.AssignFenceToCurrentPage(definition.Id);
        RequestAutoSave();

        return vm;
    }

    private void CreateNewFence(double x = 200, double y = 200)
    {
        var definition = new FenceDefinition
        {
            Title = $"Fence {_fenceWindows.Count + 1}",
            Bounds = new FenceRect { X = x, Y = y, Width = 300, Height = 200 },
            MonitorIndex = MonitorManager.GetMonitorIndexForPoint(x, y),
            PageIndex = _pageManager?.CurrentPageIndex ?? 0
        };
        var vm = new FencePanelViewModel(definition);
        SpawnFenceWindow(vm, bringToFront: true);

        _pageManager?.AssignFenceToCurrentPage(definition.Id);

        RequestAutoSave();
    }

    private void CreatePortalFence()
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog
        {
            Title = "选择要映射的文件夹"
        };

        if (dialog.ShowDialog() != true) return;

        var folderName = Path.GetFileName(dialog.FolderName);
        if (string.IsNullOrEmpty(folderName))
            folderName = dialog.FolderName;

        var definition = new FenceDefinition
        {
            Title = $"📁 {folderName}",
            Bounds = new FenceRect { X = 200, Y = 200, Width = 300, Height = 250 },
            MonitorIndex = MonitorManager.GetMonitorIndexForPoint(200, 200),
            PageIndex = _pageManager?.CurrentPageIndex ?? 0,
            PortalPath = dialog.FolderName
        };
        var vm = new FencePanelViewModel(definition);
        SpawnFenceWindow(vm, bringToFront: true);

        _pageManager?.AssignFenceToCurrentPage(definition.Id);
        RequestAutoSave();
    }

    /// <summary>
    /// 把 AppSettings 的 host 级别外观（tab strip 背景同步、tab style、DWM 模糊）
    /// 推送到一个 FenceHost 实例。新建 host (SpawnFenceWindow / DetachTab) 与
    /// 设置变更 (OnSettingsSaved) 三条路径共用此 helper，避免漏改某一处。
    /// </summary>
    private static void ApplyHostStyle(FenceHost host, AppSettings settings)
    {
        host.SyncTabStripBackground();
        host.SetTabStyle(settings.TabStyle);
        host.SetAcrylicBlur(settings.FenceBlurEnabled);
    }

    private void SpawnFenceWindow(FencePanelViewModel vm, bool bringToFront = false)
    {
        // Clamp fence position to visible screen area to prevent off-screen windows
        var clamped = MonitorManager.ClampToMonitor(
            new FenceRect { X = vm.X, Y = vm.Y, Width = vm.Width, Height = vm.Height },
            vm.MonitorIndex);
        vm.X = clamped.X;
        vm.Y = clamped.Y;
        vm.Width = clamped.Width;
        vm.Height = clamped.Height;

        var host = new FenceHost(_embedManager!, vm, _iconExtractor);
        SetupFenceHostEvents(host, vm);
        _fenceWindows.Add(host);

        // 确保窗口在 Loaded 后可见
        host.Loaded += (_, _) =>
        {
            // 获取窗口句柄并确保它显示在桌面上方
            var hwnd = new System.Windows.Interop.WindowInteropHelper(host).Handle;
            if (hwnd == IntPtr.Zero) return;

            if (bringToFront)
            {
                // 用户主动新建路径（托盘菜单 / 规则触发 / 恢复关闭 / 导入布局）：
                // 桌面/任务栏前台时 HWND_TOP 不可靠，用 HWND_TOPMOST 确保立刻可见，
                // 后续切到普通窗口时会自动通过 HWND_BOTTOM 清除 topmost。
                _embedManager!.BringNewWindowToFront(hwnd);
            }
            else
            {
                _embedManager!.EnsureVisibleAboveDesktop(hwnd);
            }
        };

        host.Show();

        // Apply default theme from settings (Fence body color derives from Hue + Opacity).
        host.Panel.ApplyDefaultTheme(
            ComputeFenceBgColor(_appSettings.FenceBgHue, _appSettings.FenceOpacity),
            _appSettings.DefaultTitleBarColor,
            _appSettings.DefaultTextColor,
            _appSettings.TitleBarFontSize);
        ApplyHostStyle(host, _appSettings);

        host.Panel.LoadAllIcons();

        if (vm.IsPortalMode)
            StartPortalWatcher(vm);
    }

    private void SetupFenceHostEvents(FenceHost host, FencePanelViewModel primaryVm)
    {
        // Inject snap support into FenceHost
        host.GetOtherFenceRects = () => _fenceWindows
            .Select(f => f.ViewModel)
            .Where(v => v.Id != host.ViewModel.Id)
            .Select(v => new DesktopFences.Core.Services.SnapEngine.Rect(v.X, v.Y, v.Width, v.Height))
            .ToList();
        host.SnapThreshold = _appSettings.SnapThreshold;
        if (_snapGuideOverlay is not null)
            host.SetSnapGuideOverlay(_snapGuideOverlay);

        // Inject snap support into FencePanel (for resize snap)
        host.Panel.GetOtherFenceRects = host.GetOtherFenceRects;
        host.Panel.SnapThreshold = _appSettings.SnapThreshold;
        host.Panel.SnapOverlay = _snapGuideOverlay;

        // Temporarily elevate z-order during drag so the panel appears above sibling fences
        host.Panel.InteractionStarted += () =>
        {
            var hwnd = new System.Windows.Interop.WindowInteropHelper(host).Handle;
            if (hwnd != IntPtr.Zero)
                _embedManager!.BringWindowAboveSiblings(hwnd);
        };
        host.Panel.InteractionEnded += () =>
        {
            var hwnd = new System.Windows.Interop.WindowInteropHelper(host).Handle;
            if (hwnd != IntPtr.Zero)
                _embedManager!.RestoreWindowToBottom(hwnd);
            foreach (var tab in host.Tabs)
                tab.MonitorIndex = MonitorManager.GetMonitorIndexForPoint(tab.X, tab.Y);
            // Snap is already applied in real time during drag (WM_MOVING) and resize
            // (ApplyResizeSnap). Re-snapping here using the vm rect (which is host - 8px
            // for the shadow margin) would offset the fence by 8px every time the right
            // or bottom edge is snapped to the screen.
            TryMergeFences(host);
            RequestAutoSave();
        };

        // Handle WM_EXITSIZEMOVE (drag/resize via Win32 messages)
        host.InteractionEndedFromWndProc += () =>
        {
            var hwnd = new System.Windows.Interop.WindowInteropHelper(host).Handle;
            if (hwnd != IntPtr.Zero)
                _embedManager!.RestoreWindowToBottom(hwnd);
            foreach (var tab in host.Tabs)
                tab.MonitorIndex = MonitorManager.GetMonitorIndexForPoint(tab.X, tab.Y);
            TryMergeFences(host);
            RequestAutoSave();
        };
        host.Closed += (_, _) =>
        {
            _fenceWindows.Remove(host);
            if (!host.IsMerging)
            {
                // Snapshot definitions BEFORE cleanup — once portal watchers stop the
                // tab list is still intact (FenceHost doesn't clear it), but capture early
                // for clarity.
                var closedDefs = host.Tabs.Select(t => t.Model).ToList();

                foreach (var tab in host.Tabs)
                {
                    _pageManager?.RemoveFence(tab.Id);
                    StopPortalWatcher(tab.Id);
                }

                if (!_isShuttingDown && !host.IsBeingReplaced && closedDefs.Count > 0)
                    RecordRecentlyClosedFences(closedDefs);
            }
            RequestAutoSave();
        };
        host.Panel.PortalModeChanged += portalPath => Dispatcher.Invoke(() =>
        {
            var activeVm = host.ViewModel;

            if (portalPath is not null)
            {
                // Start or restart portal watcher
                StopPortalWatcher(activeVm.Id);
                StartPortalWatcher(activeVm);
            }
            else
            {
                // Clear portal mode — stop watcher and keep existing files
                StopPortalWatcher(activeVm.Id);
            }
            RequestAutoSave();
        });
        // BeginInvoke (async) to avoid modifying the visual tree while the tab button's
        // event handler is still on the call stack — synchronous removal causes WPF issues.
        host.TabDetachRequested += vm => Dispatcher.BeginInvoke(() => DetachTab(host, vm));
    }

    // ── Tab Merging / Detach ──────────────────────────────────

    private static bool FencesOverlapSignificantly(FenceHost a, FenceHost b)
    {
        double ix1 = Math.Max(a.Left, b.Left);
        double iy1 = Math.Max(a.Top, b.Top);
        double ix2 = Math.Min(a.Left + a.Width, b.Left + b.Width);
        double iy2 = Math.Min(a.Top + a.Height, b.Top + b.Height);

        if (ix2 <= ix1 || iy2 <= iy1) return false;

        double intersect = (ix2 - ix1) * (iy2 - iy1);
        double smallerArea = Math.Min(a.Width * a.Height, b.Width * b.Height);
        return smallerArea > 0 && intersect / smallerArea > 0.4;
    }

    private void TryMergeFences(FenceHost movedHost)
    {
        // ToList() snapshot: MergeFences → Close → Closed handler modifies _fenceWindows
        foreach (var other in _fenceWindows.ToList())
        {
            if (other == movedHost) continue;
            if (!FencesOverlapSignificantly(movedHost, other)) continue;
            MergeFences(movedHost, other);
            return;
        }
    }

    private void MergeFences(FenceHost source, FenceHost target)
    {
        // Assign a shared TabGroupId to all tabs
        var groupId = target.Tabs.First().Model.TabGroupId ?? Guid.NewGuid();
        foreach (var t in target.Tabs)
            t.Model.TabGroupId = groupId;

        int nextOrder = target.Tabs.Count;
        var sourceTabs = source.Tabs.ToList();
        foreach (var vm in sourceTabs)
        {
            vm.Model.TabGroupId = groupId;
            vm.Model.TabOrder = nextOrder++;
            target.AddTab(vm);
        }

        source.IsMerging = true;
        source.Close();
    }

    private void DetachTab(FenceHost host, FencePanelViewModel vm)
    {
        int idx = host.Tabs.ToList().IndexOf(vm);
        if (idx < 0) return;

        host.RemoveTab(idx);
        vm.Model.TabGroupId = null;
        vm.Model.TabOrder = 0;

        // If host has only one tab left, clear its TabGroupId too
        if (host.Tabs.Count == 1)
            host.Tabs[0].Model.TabGroupId = null;

        // If host has no tabs left (shouldn't happen but be safe), close it
        if (host.Tabs.Count == 0)
        {
            host.IsMerging = true; // prevent Closed handler from doing cleanup twice
            host.Close();
        }

        // Spawn detached fence — clamp position to screen bounds
        var screen = System.Windows.Forms.Screen.FromPoint(
            new System.Drawing.Point((int)host.Left, (int)host.Top));
        var workArea = screen.WorkingArea;

        var newX = host.Left + vm.Width + 50;
        var newY = host.Top + 50;

        // Clamp to ensure the new window is within screen bounds
        if (newX + vm.Width > workArea.Right)
            newX = Math.Max(workArea.Left, host.Left - vm.Width - 50);
        if (newY + vm.Height > workArea.Bottom)
            newY = Math.Max(workArea.Top, workArea.Bottom - vm.Height);

        vm.X = newX;
        vm.Y = newY;
        var newHost = new FenceHost(_embedManager!, vm, _iconExtractor);
        SetupFenceHostEvents(newHost, vm);
        _fenceWindows.Add(newHost);
        _pageManager?.AssignFenceToCurrentPage(vm.Id);
        newHost.Show();

        newHost.Panel.ApplyDefaultTheme(
            ComputeFenceBgColor(_appSettings.FenceBgHue, _appSettings.FenceOpacity),
            _appSettings.DefaultTitleBarColor,
            _appSettings.DefaultTextColor,
            _appSettings.TitleBarFontSize);
        ApplyHostStyle(newHost, _appSettings);

        newHost.Panel.LoadAllIcons();
        RequestAutoSave();
    }

    private void SpawnFencesWithGroups(List<FenceDefinition> definitions, bool bringToFront = false)
    {
        // Separate grouped fences from standalone fences
        var grouped = definitions
            .Where(d => d.TabGroupId.HasValue)
            .GroupBy(d => d.TabGroupId!.Value)
            .ToDictionary(g => g.Key,
                g => g.OrderBy(d => d.TabOrder).ToList());

        var standalone = definitions.Where(d => !d.TabGroupId.HasValue).ToList();

        // Spawn standalone fences normally
        foreach (var def in standalone)
        {
            var vm = new FencePanelViewModel(def);
            SpawnFenceWindow(vm, bringToFront);
            if (_pageManager?.Pages.All(p => !p.FenceIds.Contains(def.Id)) == true)
                _pageManager.AssignFenceToCurrentPage(def.Id);
        }

        // Spawn grouped fences: first tab spawns a window, rest are added as tabs
        foreach (var group in grouped.Values)
        {
            if (group.Count == 0) continue;

            // First fence spawns the host window
            var primaryVm = new FencePanelViewModel(group[0]);
            SpawnFenceWindow(primaryVm, bringToFront);
            if (_pageManager?.Pages.All(p => !p.FenceIds.Contains(group[0].Id)) == true)
                _pageManager.AssignFenceToCurrentPage(group[0].Id);

            var host = _fenceWindows.Last();

            // Remaining fences are added as tabs
            for (int i = 1; i < group.Count; i++)
            {
                var tabVm = new FencePanelViewModel(group[i]);
                if (_pageManager?.Pages.All(p => !p.FenceIds.Contains(group[i].Id)) == true)
                    _pageManager.AssignFenceToCurrentPage(group[i].Id);
                host.AddTab(tabVm, activate: false);
                if (tabVm.IsPortalMode)
                    StartPortalWatcher(tabVm);
            }
        }
    }

    private void ToggleAllFences()
    {
        if (_fenceWindows.Count == 0) return;
        bool anyVisible = _fenceWindows.Any(f => f.IsVisible);
        foreach (var fence in _fenceWindows)
        {
            if (anyVisible)
                fence.Hide();
            else
            {
                fence.Show();
                // Ensure window stays above desktop and doesn't get pushed behind
                var hwnd = new System.Windows.Interop.WindowInteropHelper(fence).Handle;
                if (hwnd != IntPtr.Zero && _embedManager != null)
                    _embedManager.EnsureVisibleAboveDesktop(hwnd);
            }
        }
        // Sync overlay visibility
        if (anyVisible) _desktopOverlay?.Hide();
        else _desktopOverlay?.Show();
    }

    // ── Folder Portal ─────────────────────────────────────────

    private void StartPortalWatcher(FencePanelViewModel vm)
    {
        if (string.IsNullOrEmpty(vm.PortalPath)) return;

        var watcher = new FolderPortalWatcher();
        watcher.ContentsChanged += contents =>
            Dispatcher.InvokeAsync(() => SyncPortalContents(vm, contents));
        watcher.Watch(vm.PortalPath);
        _portalWatchers[vm.Id] = watcher;
    }

    private void StopPortalWatcher(Guid fenceId)
    {
        if (_portalWatchers.Remove(fenceId, out var watcher))
            watcher.Dispose();
    }

    private void SyncPortalContents(FencePanelViewModel vm, IReadOnlyList<string> contents)
    {
        var toRemove = vm.Files
            .Where(f => !contents.Contains(f.FilePath))
            .Select(f => f.FilePath)
            .ToList();
        foreach (var path in toRemove)
            vm.RemoveFile(path);

        foreach (var path in contents)
        {
            if (!vm.Files.Any(f =>
                string.Equals(f.FilePath, path, StringComparison.OrdinalIgnoreCase)))
            {
                vm.AddFile(path);
                var lastFile = vm.Files.LastOrDefault();
                if (lastFile is not null && lastFile.Icon is null && _iconExtractor is not null)
                    lastFile.Icon = _iconExtractor.GetIcon(path);
            }
        }
    }

    // ── Peek ─────────────────────────────────────────────────

    private void OnPeekToggled(bool isPeeking)
    {
        Dispatcher.InvokeAsync(() =>
        {
            if (isPeeking)
            {
                foreach (var fence in _fenceWindows)
                    if (!fence.IsVisible) fence.Show();
                _embedManager!.EnterPeek();
            }
            else
            {
                _embedManager!.ExitPeek();
            }
        });
    }

    // ── Auto-Classification ──────────────────────────────────

    private void StartFileMonitor()
    {
        _fileMonitor = new DesktopFileMonitor();
        _fileMonitor.FilesAdded += OnDesktopFilesAdded;
        _fileMonitor.FilesRemoved += OnDesktopFilesRemoved;
        _fileMonitor.FileRenamed += OnDesktopFileRenamed;
        _fileMonitor.Start();

        // Periodic check: remove fence items whose files no longer exist on disk
        _fileExistenceTimer = new System.Timers.Timer(10000) { AutoReset = true };
        _fileExistenceTimer.Elapsed += (_, _) => Dispatcher.InvokeAsync(RemoveDeletedFilesFromFences);
        _fileExistenceTimer.Start();
    }

    private void OnDesktopFilesAdded(IReadOnlyList<string> newFiles)
    {
        Dispatcher.InvokeAsync(() =>
        {
            foreach (var filePath in newFiles)
            {
                if (IsFileAlreadyInAnyFence(filePath))
                    continue;

                var matchedRule = _ruleEngine?.Match(filePath, _rules);
                if (matchedRule == null || !matchedRule.IsEnabled)
                {
                    _desktopOverlay?.AddIcon(filePath);
                    continue;
                }

                // 查找目标 Fence
                FencePanelViewModel? targetTab = FindFenceById(matchedRule.TargetFenceId);

                // 如果没找到，创建新的
                if (targetTab == null)
                {
                    targetTab = CreateFenceForRule(matchedRule);
                }

                // 添加文件
                targetTab.AddFile(filePath);
                _desktopOverlay?.RemoveIcon(filePath);

                if (_iconExtractor != null)
                {
                    var lastFile = targetTab.Files.LastOrDefault();
                    if (lastFile != null && lastFile.Icon == null)
                        lastFile.Icon = _iconExtractor.GetIcon(filePath);
                }

                RequestAutoSave();
            }
        });
    }

    private void OnDesktopFilesRemoved(IReadOnlyList<string> removedFiles)
    {
        Dispatcher.InvokeAsync(() =>
        {
            foreach (var filePath in removedFiles)
            {
                RemoveFileFromAllFences(filePath);
                _desktopOverlay?.RemoveIcon(filePath);
            }
        });
    }

    private void OnDesktopFileRenamed(string oldPath, string newPath)
    {
        Dispatcher.InvokeAsync(() =>
        {
            foreach (var tab in _fenceWindows.AllTabs())
            {
                var fileItem = tab.Files.FirstOrDefault(
                    f => string.Equals(f.FilePath, oldPath, StringComparison.OrdinalIgnoreCase));
                if (fileItem is null) continue;

                fileItem.FilePath = newPath;
                tab.SyncToModel();
                RequestAutoSave();
                break;
            }

            if (_desktopOverlay is not null && _desktopOverlay.ContainsIcon(oldPath))
            {
                _desktopOverlay.RemoveIcon(oldPath);
                _desktopOverlay.AddIcon(newPath);
            }
        });
    }

    /// <summary>
    /// Periodically checks all files in all fences and removes entries
    /// whose underlying files no longer exist on disk.
    /// </summary>
    private void RemoveDeletedFilesFromFences()
    {
        bool changed = false;
        foreach (var tab in _fenceWindows.AllTabs())
        {
            var deadFiles = tab.Files
                .Where(f => !File.Exists(f.FilePath) && !Directory.Exists(f.FilePath))
                .Select(f => f.FilePath)
                .ToList();

            foreach (var path in deadFiles)
            {
                tab.RemoveFile(path);
                changed = true;
            }
        }
        if (changed)
            RequestAutoSave();
    }

    private void RemoveFileFromAllFences(string filePath)
    {
        bool changed = false;
        foreach (var tab in _fenceWindows.AllTabs())
        {
            var fileItem = tab.Files.FirstOrDefault(
                f => string.Equals(f.FilePath, filePath, StringComparison.OrdinalIgnoreCase));
            if (fileItem is not null)
            {
                tab.RemoveFile(fileItem.FilePath);
                changed = true;
            }
        }
        if (changed)
        {
            if (IsDesktopFile(filePath) && (File.Exists(filePath) || Directory.Exists(filePath)))
                _desktopOverlay?.AddIcon(filePath);
            RequestAutoSave();
        }
    }

    private bool IsDesktopFile(string filePath)
    {
        var dir = Path.GetDirectoryName(filePath);
        return string.Equals(dir, _desktopPath, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(dir, _publicDesktopPath, StringComparison.OrdinalIgnoreCase);
    }

    // ── Persistence ─────────────────────────────────────────

    private async Task LoadFencesAsync()
    {
        // Crash recovery: restore desktop icons if a previous session crashed
        _desktopIconManager = new DesktopIconManager();
        if (_desktopIconManager.NeedsCrashRecovery)
            _desktopIconManager.ShowIcons();

        var definitions = await _layoutStore!.LoadFencesAsync();
        _rules = await _layoutStore.LoadRulesAsync();
        _appSettings = await _layoutStore.LoadSettingsAsync();
        ApplyIconAppearance(_appSettings);
        ApplyFenceShadow(_appSettings);

        if (_appSettings.StartWithWindows && !StartupManager.IsRegistered())
            StartupManager.Register();

        if (!_appSettings.QuickHideEnabled)
            _quickHideManager?.Stop();

        await _snapshotManager!.LoadAsync();

        // Consolidate all pages into a single page — custom page switching is disabled,
        // Windows virtual desktops manage desktop visibility natively.
        _pageManager!.Initialize([]);  // single default page

        if (_monitorManager is not null)
        {
            var configHash = _monitorManager.CurrentConfigHash;
            var monitorLayout = await _layoutStore.LoadMonitorLayoutAsync(configHash);
            if (monitorLayout is not null)
                definitions = monitorLayout;
        }

        if (definitions.Count == 0)
        {
            var (defaultFences, defaultRules) = CreateDefaultConfiguration();
            _rules = defaultRules;
            await _layoutStore.SaveRulesAsync(_rules);

            foreach (var def in defaultFences)
            {
                var vm = new FencePanelViewModel(def);
                SpawnFenceWindow(vm);
                _pageManager?.AssignFenceToCurrentPage(def.Id);
            }
        }
        else
        {
            SpawnFencesWithGroups(definitions);

            // Assign all fences to the single page
            foreach (var tab in _fenceWindows.AllTabs())
                _pageManager?.AssignFenceToCurrentPage(tab.Id);
        }

        StartFileMonitor();

        // Initial organize + start periodic timer (after fences & settings are loaded)
        await OrganizeDesktopOnceAsync();
        StartAutoOrganizeTimer();

        // Read icon positions from SysListView32 BEFORE hiding
        var overlayItems = ReadAndMatchDesktopIcons();

        // Hide the entire desktop icon layer (SysListView32)
        _desktopIconManager?.HideIcons();

        // Create overlay to render unfenced desktop icons at their native positions
        CreateDesktopOverlay(overlayItems);
    }

    // ── Desktop Icon Overlay ──────────────────────────────────

    private List<OverlayIconItem> ReadAndMatchDesktopIcons()
    {
        var listViewHwnd = _desktopIconManager?.GetListViewHandle() ?? IntPtr.Zero;
        var allPositions = DesktopIconPositionReader.ReadAllPositions(listViewHwnd);
        var allDesktopFiles = GetAllDesktopEntries();
        var result = new List<OverlayIconItem>();

        foreach (var desktopFile in allDesktopFiles)
        {
            if (IsFileAlreadyInAnyFence(desktopFile))
                continue;

            // Always auto-arrange into grid (ignore original scattered positions)
            result.Add(new OverlayIconItem(desktopFile, -1, -1));
        }

        return result;
    }

    private static (int X, int Y)? MatchFileToPosition(string filePath, List<DesktopIconInfo> positions)
    {
        if (positions.Count == 0) return null;

        var fileName = Path.GetFileName(filePath);
        var nameNoExt = Path.GetFileNameWithoutExtension(filePath);

        // Try exact match (full filename)
        var match = positions.FirstOrDefault(p =>
            string.Equals(p.DisplayName, fileName, StringComparison.OrdinalIgnoreCase));
        if (match is not null) return (match.X, match.Y);

        // Try without extension (for .lnk, .url that hide extensions)
        match = positions.FirstOrDefault(p =>
            string.Equals(p.DisplayName, nameNoExt, StringComparison.OrdinalIgnoreCase));
        if (match is not null) return (match.X, match.Y);

        return null;
    }

    private void CreateDesktopOverlay(List<OverlayIconItem> items)
    {
        if (_embedManager is null || _iconExtractor is null) return;

        _desktopOverlay = new DesktopIconOverlay(_embedManager, _iconExtractor);
        _desktopOverlay.SetIcons(items);
        _desktopOverlay.Show();
    }

    private async Task SaveFencesAsync()
    {
        try
        {
            var definitions = Dispatcher.CheckAccess()
                ? _fenceWindows.AllDefinitions()
                : await Dispatcher.InvokeAsync(() => _fenceWindows.AllDefinitions());

            await _layoutStore!.SaveFencesAsync(definitions);

            if (_monitorManager is not null)
                await _layoutStore.SaveMonitorLayoutAsync(_monitorManager.CurrentConfigHash, definitions);
        }
        catch { }
    }

    private async Task SavePagesAsync()
    {
        try
        {
            if (_pageManager is not null)
                await _layoutStore!.SavePagesAsync(_pageManager.Pages);
        }
        catch { }
    }

    private async Task SaveRulesAsync()
    {
        try { await _layoutStore!.SaveRulesAsync(_rules); }
        catch { }
    }

    private void StartAutoSave()
    {
        _autoSaveTimer = new System.Timers.Timer(2000) { AutoReset = false };
        _autoSaveTimer.Elapsed += async (_, _) => await SaveFencesAsync();
    }

    private void RequestAutoSave()
    {
        _autoSaveTimer?.Stop();
        _autoSaveTimer?.Start();
    }

    // ── Default Configuration ────────────────────────────────

    private static (List<FenceDefinition>, List<ClassificationRule>) CreateDefaultConfiguration()
    {
        var programs = MakeFence("程序及快捷方式", 20, 20);
        var folders  = MakeFence("文件夹",        340, 20);
        var docs     = MakeFence("文档",          20, 240);
        var videos   = MakeFence("视频",          340, 240);
        var music    = MakeFence("音乐",          20, 460);
        var pictures = MakeFence("图片",          340, 460);

        var fences = new List<FenceDefinition> { programs, folders, docs, videos, music, pictures };
        var rules = new List<ClassificationRule>
        {
            MakeRule("程序及快捷方式", ".exe,.lnk,.url,.bat,.cmd,.ps1,.msi", programs.Id, 1),
            new() {
                Name = "文件夹", Priority = 2, IsEnabled = true,
                TargetFenceId = folders.Id,
                Condition = new RuleCondition { MatchType = RuleMatchType.IsDirectory }
            },
            MakeRule("文档", ".doc,.docx,.pdf,.txt,.xls,.xlsx,.ppt,.pptx,.odt,.ods,.odp,.md,.rtf,.csv", docs.Id, 3),
            MakeRule("视频", ".mp4,.mkv,.avi,.mov,.wmv,.flv,.webm,.m4v,.mpg,.mpeg,.ts,.rmvb", videos.Id, 4),
            MakeRule("音乐", ".mp3,.wav,.flac,.aac,.ogg,.m4a,.wma,.opus,.ape,.mid", music.Id, 5),
            MakeRule("图片", ".jpg,.jpeg,.png,.gif,.bmp,.webp,.svg,.ico,.tiff,.tif,.heic,.raw,.psd", pictures.Id, 6),
        };

        return (fences, rules);
    }

    private static FenceDefinition MakeFence(string title, double x, double y) => new()
    {
        Title = title,
        Bounds = new FenceRect { X = x, Y = y, Width = 300, Height = 200 }
    };

    private static ClassificationRule MakeRule(string name, string pattern, Guid targetId, int priority) => new()
    {
        Name = name,
        Priority = priority,
        IsEnabled = true,
        TargetFenceId = targetId,
        Condition = new RuleCondition { MatchType = RuleMatchType.Extension, Pattern = pattern }
    };

    // ── Shutdown ─────────────────────────────────────────────

    protected override void OnExit(ExitEventArgs e)
    {
        _isShuttingDown = true;
        _snapGuideOverlay?.Close();
        _snapGuideOverlay = null;
        _desktopOverlay?.Close();
        _desktopOverlay = null;
        _desktopIconManager?.ShowIcons();

        foreach (var watcher in _portalWatchers.Values)
            watcher.Dispose();
        _portalWatchers.Clear();

        _searchHotkeyManager?.Dispose();
        _pageSwitchManager?.Dispose();
        _monitorManager?.Dispose();
        _fileMonitor?.Dispose();
        _peekManager?.Dispose();
        _quickHideManager?.Dispose();
        _autoSaveTimer?.Stop();
        _autoSaveTimer?.Dispose();
        _fileExistenceTimer?.Stop();
        _fileExistenceTimer?.Dispose();
        _trayIcon?.Dispose();
        _embedManager?.Dispose();
        _singleInstanceMutex?.Dispose();
        base.OnExit(e);
    }
}
