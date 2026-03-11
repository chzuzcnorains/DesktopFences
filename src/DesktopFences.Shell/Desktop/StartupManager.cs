using Microsoft.Win32;

namespace DesktopFences.Shell.Desktop;

/// <summary>
/// Manages auto-start with Windows via HKCU\Run registry key.
/// </summary>
public static class StartupManager
{
    private const string AppName = "DesktopFences";
    private const string RunKeyPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";

    public static bool IsRegistered()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, false);
        return key?.GetValue(AppName) is not null;
    }

    public static void Register()
    {
        var exePath = Environment.ProcessPath;
        if (string.IsNullOrEmpty(exePath)) return;

        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, true);
        key?.SetValue(AppName, $"\"{exePath}\"");
    }

    public static void Unregister()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, true);
        key?.DeleteValue(AppName, throwOnMissingValue: false);
    }

    public static void SetEnabled(bool enabled)
    {
        if (enabled) Register();
        else Unregister();
    }
}
