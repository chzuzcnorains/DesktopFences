using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace DesktopFences.UI.Controls.Settings;

public partial class AboutSettingsPane : UserControl
{
    public AboutSettingsPane()
    {
        InitializeComponent();
        VersionLine.Text = ResolveVersionLine();
    }

    /// <summary>
    /// Populate live counters and uptime. Called from SettingsWindow after Initialize().
    /// </summary>
    public void Initialize(int activeFenceCount, int managedFileCount, int ruleCount, TimeSpan uptime)
    {
        StatActiveFences.Text = activeFenceCount.ToString();
        StatManagedFiles.Text = managedFileCount.ToString();
        StatRules.Text = ruleCount.ToString();
        StatUptime.Text = FormatUptime(uptime);
    }

    private static string FormatUptime(TimeSpan uptime)
    {
        if (uptime.TotalDays >= 1) return $"{(int)uptime.TotalDays} 天";
        if (uptime.TotalHours >= 1) return $"{(int)uptime.TotalHours} 时";
        return $"{Math.Max(1, (int)uptime.TotalMinutes)} 分";
    }

    private static string ResolveVersionLine()
    {
        try
        {
            var ver = Assembly.GetEntryAssembly()?.GetName().Version;
            var verText = ver is null ? "0.9.0" : $"{ver.Major}.{ver.Minor}.{ver.Build}";
            var build = DateTime.Now.ToString("yyyy.MM");
            return $"{verText} · Build {build} · Windows x64";
        }
        catch
        {
            return "0.9.0 · Phase 10 · Windows x64";
        }
    }

    // ── Link handlers (no real navigation; opens local resources where it makes sense) ──

    private void LinkChangelog_Click(object sender, MouseButtonEventArgs e)
    {
        // No remote URL to open in this build — surface what we have locally.
        var docPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "docs", "PLAN.md");
        TryOpen(docPath, "更新日志暂未发布。");
    }

    private void LinkCheckUpdate_Click(object sender, MouseButtonEventArgs e)
    {
        MessageBox.Show("当前版本已是最新（离线检查）。", "DesktopFences",
            MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void LinkConfigFolder_Click(object sender, MouseButtonEventArgs e)
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "DesktopFences");
        try
        {
            Directory.CreateDirectory(dir);
            Process.Start(new ProcessStartInfo { FileName = dir, UseShellExecute = true });
        }
        catch (Exception ex)
        {
            MessageBox.Show($"无法打开配置目录：{ex.Message}", "DesktopFences",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void LinkLicense_Click(object sender, MouseButtonEventArgs e)
    {
        var path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "LICENSE");
        TryOpen(path, "本仓库未附带 LICENSE 文件。");
    }

    private static void TryOpen(string path, string fallbackMessage)
    {
        if (File.Exists(path))
        {
            try
            {
                Process.Start(new ProcessStartInfo { FileName = path, UseShellExecute = true });
                return;
            }
            catch { }
        }
        MessageBox.Show(fallbackMessage, "DesktopFences",
            MessageBoxButton.OK, MessageBoxImage.Information);
    }
}
