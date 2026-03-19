using System.Runtime.InteropServices;
using DesktopFences.Shell.Interop;

namespace DesktopFences.Shell.Desktop;

/// <summary>
/// Represents one desktop icon's display name and screen position
/// as read from the SysListView32 control.
/// </summary>
public record DesktopIconInfo(string DisplayName, int X, int Y);

/// <summary>
/// Reads icon positions from the desktop SysListView32 via cross-process
/// SendMessage. Must be called BEFORE hiding the ListView.
///
/// The desktop SysListView32 lives in explorer.exe, so we need to:
/// 1. Allocate memory in explorer's address space (VirtualAllocEx)
/// 2. Write LVITEM structs there (WriteProcessMemory)
/// 3. Send ListView messages (SendMessage)
/// 4. Read results back (ReadProcessMemory)
/// </summary>
public static class DesktopIconPositionReader
{
    private const int TextBufferSize = 520; // 260 wchar
    private static readonly int LvItemSize = Marshal.SizeOf<NativeMethods.LVITEMW>();
    private static readonly int PointSize = Marshal.SizeOf<NativeMethods.POINT>();

    /// <summary>
    /// Read all icon positions and display names from the desktop SysListView32.
    /// Returns empty list on failure (graceful degradation).
    /// </summary>
    public static List<DesktopIconInfo> ReadAllPositions(IntPtr listViewHwnd)
    {
        if (listViewHwnd == IntPtr.Zero)
            return [];

        var result = new List<DesktopIconInfo>();

        // Get item count
        int count = (int)NativeMethods.SendMessage(
            listViewHwnd, NativeMethods.LVM_GETITEMCOUNT, IntPtr.Zero, IntPtr.Zero);
        if (count <= 0)
            return [];

        // Get explorer.exe process
        NativeMethods.GetWindowThreadProcessId(listViewHwnd, out uint processId);
        if (processId == 0)
            return [];

        IntPtr hProcess = NativeMethods.OpenProcess(
            NativeMethods.PROCESS_VM_OPERATION | NativeMethods.PROCESS_VM_READ | NativeMethods.PROCESS_VM_WRITE,
            false, processId);
        if (hProcess == IntPtr.Zero)
            return [];

        // Allocate memory in explorer's process for LVITEM + text buffer + POINT
        uint totalSize = (uint)(LvItemSize + TextBufferSize + PointSize);
        IntPtr remoteMem = NativeMethods.VirtualAllocEx(
            hProcess, IntPtr.Zero, totalSize,
            NativeMethods.MEM_COMMIT, NativeMethods.PAGE_READWRITE);

        if (remoteMem == IntPtr.Zero)
        {
            NativeMethods.CloseHandle(hProcess);
            return [];
        }

        try
        {
            IntPtr remoteTextBuffer = remoteMem + LvItemSize;
            IntPtr remotePoint = remoteTextBuffer + TextBufferSize;

            for (int i = 0; i < count; i++)
            {
                string? displayName = ReadItemText(hProcess, listViewHwnd, i, remoteMem, remoteTextBuffer);
                if (string.IsNullOrEmpty(displayName))
                    continue;

                var position = ReadItemPosition(hProcess, listViewHwnd, i, remotePoint);
                if (position.HasValue)
                {
                    result.Add(new DesktopIconInfo(displayName, position.Value.X, position.Value.Y));
                }
            }
        }
        finally
        {
            NativeMethods.VirtualFreeEx(hProcess, remoteMem, 0, NativeMethods.MEM_RELEASE);
            NativeMethods.CloseHandle(hProcess);
        }

        return result;
    }

    private static string? ReadItemText(
        IntPtr hProcess, IntPtr listViewHwnd, int index,
        IntPtr remoteLvItem, IntPtr remoteTextBuffer)
    {
        // Prepare LVITEM struct locally
        var lvItem = new NativeMethods.LVITEMW
        {
            mask = NativeMethods.LVIF_TEXT,
            iItem = index,
            iSubItem = 0,
            pszText = remoteTextBuffer,
            cchTextMax = 260
        };

        // Marshal LVITEM to unmanaged memory, write to explorer's process
        IntPtr localLvItem = Marshal.AllocHGlobal(LvItemSize);
        try
        {
            Marshal.StructureToPtr(lvItem, localLvItem, false);
            if (!NativeMethods.WriteProcessMemory(hProcess, remoteLvItem, localLvItem, (uint)LvItemSize, out _))
                return null;
        }
        finally
        {
            Marshal.FreeHGlobal(localLvItem);
        }

        // Send LVM_GETITEMTEXTW
        NativeMethods.SendMessage(listViewHwnd, NativeMethods.LVM_GETITEMTEXTW, (IntPtr)index, remoteLvItem);

        // Read back text buffer
        IntPtr localTextBuffer = Marshal.AllocHGlobal(TextBufferSize);
        try
        {
            if (!NativeMethods.ReadProcessMemory(hProcess, remoteTextBuffer, localTextBuffer, (uint)TextBufferSize, out _))
                return null;

            return Marshal.PtrToStringUni(localTextBuffer);
        }
        finally
        {
            Marshal.FreeHGlobal(localTextBuffer);
        }
    }

    private static (int X, int Y)? ReadItemPosition(
        IntPtr hProcess, IntPtr listViewHwnd, int index, IntPtr remotePoint)
    {
        // LVM_GETITEMPOSITION: wParam = index, lParam = pointer to POINT in remote process
        NativeMethods.SendMessage(listViewHwnd, NativeMethods.LVM_GETITEMPOSITION, (IntPtr)index, remotePoint);

        // Read back POINT
        IntPtr localPoint = Marshal.AllocHGlobal(PointSize);
        try
        {
            if (!NativeMethods.ReadProcessMemory(hProcess, remotePoint, localPoint, (uint)PointSize, out _))
                return null;

            var pt = Marshal.PtrToStructure<NativeMethods.POINT>(localPoint);
            return (pt.X, pt.Y);
        }
        finally
        {
            Marshal.FreeHGlobal(localPoint);
        }
    }
}
