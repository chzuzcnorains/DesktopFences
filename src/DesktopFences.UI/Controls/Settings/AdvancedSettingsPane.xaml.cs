using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using DesktopFences.Core.Models;

namespace DesktopFences.UI.Controls.Settings;

public partial class AdvancedSettingsPane : UserControl
{
    private static readonly string[] LogLevels =
        ["Error", "Warn", "Info", "Debug", "Trace"];

    public event Action? ResetLayoutRequested;
    public event Action? ClearRulesRequested;
    public event Action? RestoreDefaultsRequested;

    public AdvancedSettingsPane()
    {
        InitializeComponent();
        foreach (var lvl in LogLevels) CboLogLevel.Items.Add(lvl);
    }

    public void Load(AppSettings s)
    {
        ChkCompatibilityMode.IsChecked = s.CompatibilityMode;
        ChkDebugLogging.IsChecked = s.DebugLogging;
        SliderWinDDelay.Value = Math.Max(0, Math.Min(800, s.WinDDetectionDelayMs));

        var lvl = string.IsNullOrWhiteSpace(s.LogLevel) ? "Info" : s.LogLevel;
        CboLogLevel.SelectedItem = LogLevels.Contains(lvl) ? lvl : "Info";

        UpdateWinDDelayLabel();
    }

    public void Save(AppSettings s)
    {
        s.CompatibilityMode = ChkCompatibilityMode.IsChecked == true;
        s.DebugLogging = ChkDebugLogging.IsChecked == true;
        s.WinDDetectionDelayMs = (int)Math.Round(SliderWinDDelay.Value);
        s.LogLevel = CboLogLevel.SelectedItem as string ?? "Info";
    }

    // ── Slider ────────────────────────────────────────────

    private void SliderWinDDelay_ValueChanged(object sender,
        RoutedPropertyChangedEventArgs<double> e) => UpdateWinDDelayLabel();

    private void UpdateWinDDelayLabel()
    {
        if (WinDDelayValue is null) return;
        WinDDelayValue.Text = $"{(int)Math.Round(SliderWinDDelay.Value)} ms";
    }

    // ── Buttons ───────────────────────────────────────────

    private void BtnOpenLogFolder_Click(object sender, RoutedEventArgs e)
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "DesktopFences", "log");
        try
        {
            Directory.CreateDirectory(dir);
            Process.Start(new ProcessStartInfo { FileName = dir, UseShellExecute = true });
        }
        catch (Exception ex)
        {
            MessageBox.Show($"无法打开日志文件夹：{ex.Message}", "DesktopFences",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void BtnResetLayout_Click(object sender, RoutedEventArgs e)
    {
        if (Confirm("确认重置所有 Fence 布局？", "当前所有 Fence 将被关闭，下次启动按默认 6 分类重建。"))
            ResetLayoutRequested?.Invoke();
    }

    private void BtnClearRules_Click(object sender, RoutedEventArgs e)
    {
        if (Confirm("确认清空分类规则？", "rules.json 将被清空。Fence 内已分类的文件保留，但不再自动归档新文件。"))
            ClearRulesRequested?.Invoke();
    }

    private void BtnRestoreDefaults_Click(object sender, RoutedEventArgs e)
    {
        if (Confirm("确认恢复所有默认设置？", "settings.json 还原为默认值；Fence 布局、规则、快照不受影响。"))
            RestoreDefaultsRequested?.Invoke();
    }

    private static bool Confirm(string title, string body)
    {
        return MessageBox.Show(body, title,
            MessageBoxButton.OKCancel, MessageBoxImage.Warning) == MessageBoxResult.OK;
    }
}
