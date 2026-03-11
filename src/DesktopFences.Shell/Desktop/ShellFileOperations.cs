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
    /// </summary>
    public static void OpenFile(string filePath)
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = filePath,
            UseShellExecute = true
        });
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
