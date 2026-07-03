using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using DesktopFences.Shell.Interop;

namespace DesktopFences.Shell.Desktop;

/// <summary>
/// Provides shell file operations: open, delete-to-recycle-bin, rename.
/// </summary>
public static class ShellFileOperations
{
    /// <summary>
    /// Open a file using the default shell handler (ShellExecute).
    /// Returns false on failure (file deleted, no association, UAC cancelled…) —
    /// Process.Start throws Win32Exception in those cases and callers sit on the
    /// UI thread, so an unhandled throw would crash the whole app.
    /// </summary>
    public static bool OpenFile(string filePath)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = filePath,
                UseShellExecute = true
            });
            return true;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"OpenFile failed for '{filePath}': {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Delete a file to the Recycle Bin using SHFileOperation.
    /// </summary>
    public static bool DeleteToRecycleBin(string filePath)
    {
        var op = new NativeMethods.SHFILEOPSTRUCT
        {
            wFunc = NativeMethods.FO_DELETE,
            pFrom = filePath + '\0', // double-null terminated
            fFlags = NativeMethods.FOF_ALLOWUNDO | NativeMethods.FOF_NOCONFIRMATION | NativeMethods.FOF_SILENT
        };
        return NativeMethods.SHFileOperation(ref op) == 0;
    }

    /// <summary>
    /// 获取 shell 的本地化文件类型名（如「应用程序」「文本文档」），
    /// 与 Explorer「排序方式→项目类型」使用同一口径。失败返回空串。
    /// </summary>
    public static string GetFileTypeName(string filePath)
    {
        var flags = NativeMethods.SHGFI_TYPENAME;
        // 不存在的路径按扩展名推断（与图标提取的降级路径一致）
        if (!File.Exists(filePath) && !Directory.Exists(filePath))
            flags |= NativeMethods.SHGFI_USEFILEATTRIBUTES;

        var shfi = new NativeMethods.SHFILEINFO();
        var result = NativeMethods.SHGetFileInfo(
            filePath,
            NativeMethods.FILE_ATTRIBUTE_NORMAL,
            ref shfi,
            (uint)Marshal.SizeOf<NativeMethods.SHFILEINFO>(),
            flags);

        return result == IntPtr.Zero ? string.Empty : shfi.szTypeName ?? string.Empty;
    }

    /// <summary>
    /// Rename a file (same directory, new name).
    /// </summary>
    public static bool RenameFile(string filePath, string newName)
    {
        var dir = Path.GetDirectoryName(filePath);
        if (dir is null) return false;
        var newPath = Path.Combine(dir, newName);
        if (File.Exists(newPath)) return false;
        File.Move(filePath, newPath);
        return true;
    }

    /// <summary>
    /// Notify Explorer that a single file's attributes changed so the desktop view refreshes.
    /// </summary>
    public static void NotifyShellItemChanged(string filePath)
    {
        var ptr = Marshal.StringToCoTaskMemUni(filePath);
        try
        {
            NativeMethods.SHChangeNotify(
                NativeMethods.SHCNE_UPDATEITEM,
                NativeMethods.SHCNF_PATHW | NativeMethods.SHCNF_FLUSHNOWAIT,
                ptr, IntPtr.Zero);
        }
        finally { Marshal.FreeCoTaskMem(ptr); }
    }

    /// <summary>
    /// Notify Explorer to refresh a directory (e.g. after bulk hide/unhide).
    /// </summary>
    public static void NotifyShellDirectoryChanged(string directoryPath)
    {
        var ptr = Marshal.StringToCoTaskMemUni(directoryPath);
        try
        {
            NativeMethods.SHChangeNotify(
                NativeMethods.SHCNE_UPDATEDIR,
                NativeMethods.SHCNF_PATHW | NativeMethods.SHCNF_FLUSHNOWAIT,
                ptr, IntPtr.Zero);
        }
        finally { Marshal.FreeCoTaskMem(ptr); }
    }
}
