using System.Security.Cryptography;
using System.Text;
using System.Windows.Forms;
using DesktopFences.Core.Models;
using Microsoft.Win32;

namespace DesktopFences.Shell.Desktop;

/// <summary>
/// Manages multi-monitor enumeration, config hashing, and hot-plug detection.
/// </summary>
public class MonitorManager : IDisposable
{
    private string _currentConfigHash = string.Empty;

    /// <summary>
    /// Fired when the display configuration changes (monitor added/removed/resolution changed).
    /// Provides the new config hash.
    /// </summary>
    public event Action<string>? DisplayConfigChanged;

    public MonitorManager()
    {
        _currentConfigHash = ComputeConfigHash();
        SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
    }

    public string CurrentConfigHash => _currentConfigHash;

    /// <summary>
    /// Get the current screen configuration.
    /// </summary>
    public ScreenConfiguration GetScreenConfiguration()
    {
        var screens = Screen.AllScreens;
        var config = new ScreenConfiguration
        {
            ScreenCount = screens.Length,
            ConfigHash = _currentConfigHash,
            Screens = screens.Select((s, i) => new ScreenInfo
            {
                Index = i,
                X = s.Bounds.X,
                Y = s.Bounds.Y,
                Width = s.Bounds.Width,
                Height = s.Bounds.Height,
                IsPrimary = s.Primary
            }).ToList()
        };
        return config;
    }

    /// <summary>
    /// Clamp a fence's bounds to stay within its assigned monitor's work area.
    /// </summary>
    public static FenceRect ClampToMonitor(FenceRect bounds, int monitorIndex)
    {
        var screens = Screen.AllScreens;
        if (monitorIndex < 0 || monitorIndex >= screens.Length)
            monitorIndex = 0;

        var workArea = screens[monitorIndex].WorkingArea;
        var result = new FenceRect
        {
            Width = Math.Min(bounds.Width, workArea.Width),
            Height = Math.Min(bounds.Height, workArea.Height)
        };

        result.X = Math.Max(workArea.X, Math.Min(bounds.X, workArea.X + workArea.Width - result.Width));
        result.Y = Math.Max(workArea.Y, Math.Min(bounds.Y, workArea.Y + workArea.Height - result.Height));

        return result;
    }

    /// <summary>
    /// Determine which monitor index a point belongs to.
    /// </summary>
    public static int GetMonitorIndexForPoint(double x, double y)
    {
        var screen = Screen.FromPoint(new System.Drawing.Point((int)x, (int)y));
        var screens = Screen.AllScreens;
        for (int i = 0; i < screens.Length; i++)
        {
            if (screens[i].DeviceName == screen.DeviceName)
                return i;
        }
        return 0;
    }

    /// <summary>
    /// Migrate fence layouts when monitor config changes (proportional scaling).
    /// </summary>
    public static List<FenceDefinition> MigrateLayout(
        List<FenceDefinition> fences, ScreenConfiguration oldConfig, ScreenConfiguration newConfig)
    {
        foreach (var fence in fences)
        {
            var oldScreen = oldConfig.Screens.FirstOrDefault(s => s.Index == fence.MonitorIndex);
            var newScreen = newConfig.Screens.FirstOrDefault(s => s.Index == fence.MonitorIndex);

            if (oldScreen is null || newScreen is null)
            {
                // Monitor was removed, move to primary
                fence.MonitorIndex = 0;
                newScreen = newConfig.Screens.FirstOrDefault(s => s.IsPrimary)
                            ?? newConfig.Screens.FirstOrDefault();
                oldScreen ??= newScreen;
                if (newScreen is null || oldScreen is null) continue;
            }

            // Proportional scaling
            double xRatio = newScreen.Width / Math.Max(oldScreen.Width, 1);
            double yRatio = newScreen.Height / Math.Max(oldScreen.Height, 1);

            fence.Bounds.X = newScreen.X + (fence.Bounds.X - oldScreen.X) * xRatio;
            fence.Bounds.Y = newScreen.Y + (fence.Bounds.Y - oldScreen.Y) * yRatio;
            fence.Bounds.Width *= xRatio;
            fence.Bounds.Height *= yRatio;

            // Clamp to new monitor bounds
            var clamped = ClampToMonitor(fence.Bounds, fence.MonitorIndex);
            fence.Bounds = clamped;
        }
        return fences;
    }

    private void OnDisplaySettingsChanged(object? sender, EventArgs e)
    {
        var newHash = ComputeConfigHash();
        if (newHash != _currentConfigHash)
        {
            _currentConfigHash = newHash;
            DisplayConfigChanged?.Invoke(newHash);
        }
    }

    private static string ComputeConfigHash()
    {
        var sb = new StringBuilder();
        var screens = Screen.AllScreens;
        sb.Append(screens.Length);
        foreach (var screen in screens.OrderBy(s => s.DeviceName))
        {
            sb.Append($"|{screen.Bounds.Width}x{screen.Bounds.Height}@{screen.Bounds.X},{screen.Bounds.Y}");
            sb.Append(screen.Primary ? ":P" : ":S");
        }

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString()));
        return Convert.ToHexString(hash)[..16]; // 16-char hex hash
    }

    public void Dispose()
    {
        SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
    }
}
