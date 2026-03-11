using System.IO;
using System.Runtime.InteropServices;
using DesktopFences.Shell.Interop;

namespace DesktopFences.Shell.Desktop;

/// <summary>
/// Shows the native Windows shell context menu for a file.
/// Uses IContextMenu COM interface via IShellFolder.
/// </summary>
public static class ShellContextMenu
{
    // COM interface GUIDs
    private static readonly Guid IID_IShellFolder = new("000214E6-0000-0000-C000-000000000046");
    private static readonly Guid IID_IContextMenu = new("000214E4-0000-0000-C000-000000000046");

    /// <summary>
    /// Show the shell context menu at the given screen coordinates for the specified file.
    /// </summary>
    public static void Show(IntPtr hwndOwner, string filePath, int screenX, int screenY)
    {
        // Get IShellFolder for the parent directory and the child PIDL
        var parentDir = Path.GetDirectoryName(filePath);
        if (parentDir is null) return;

        int hr = SHParseDisplayName(parentDir, IntPtr.Zero, out var pidlFolder, 0, out _);
        if (hr != 0) return;

        try
        {
            var iidFolder = IID_IShellFolder;
            hr = SHBindToObject(IntPtr.Zero, pidlFolder, IntPtr.Zero, ref iidFolder, out var folderObj);
            if (hr != 0) return;

            var folder = (IShellFolder)folderObj;
            var fileName = Path.GetFileName(filePath);

            int attrs = 0;
            hr = folder.ParseDisplayName(IntPtr.Zero, IntPtr.Zero, fileName, out _, out var pidlChild, ref attrs);
            if (hr != 0) return;

            try
            {
                var pidls = new[] { pidlChild };
                var iidCtxMenu = IID_IContextMenu;
                hr = folder.GetUIObjectOf(hwndOwner, 1, pidls, ref iidCtxMenu, IntPtr.Zero, out var ctxObj);
                if (hr != 0) return;

                var contextMenu = (IContextMenu)ctxObj;
                var hMenu = NativeMethods.CreatePopupMenu();

                try
                {
                    contextMenu.QueryContextMenu(hMenu, 0, 1, 0x7FFF, 0); // CMF_NORMAL

                    int cmd = NativeMethods.TrackPopupMenuEx(
                        hMenu,
                        NativeMethods.TPM_RETURNCMD | NativeMethods.TPM_LEFTALIGN,
                        screenX, screenY, hwndOwner, IntPtr.Zero);

                    if (cmd > 0)
                    {
                        var ci = new CMINVOKECOMMANDINFO
                        {
                            cbSize = Marshal.SizeOf<CMINVOKECOMMANDINFO>(),
                            lpVerb = (IntPtr)(cmd - 1),
                            nShow = 1 // SW_SHOWNORMAL
                        };
                        contextMenu.InvokeCommand(ref ci);
                    }
                }
                finally
                {
                    NativeMethods.DestroyMenu(hMenu);
                }
            }
            finally
            {
                Marshal.FreeCoTaskMem(pidlChild);
            }
        }
        finally
        {
            Marshal.FreeCoTaskMem(pidlFolder);
        }
    }

    // ── COM imports ─────────────────────────────────────────

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHParseDisplayName(
        string pszName, IntPtr pbc, out IntPtr ppidl, uint sfgaoIn, out uint psfgaoOut);

    [DllImport("shell32.dll")]
    private static extern int SHBindToObject(
        IntPtr psf, IntPtr pidl, IntPtr pbc, ref Guid riid,
        [MarshalAs(UnmanagedType.Interface)] out object ppv);

    [ComImport, Guid("000214E6-0000-0000-C000-000000000046")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellFolder
    {
        int ParseDisplayName(IntPtr hwnd, IntPtr pbc,
            [MarshalAs(UnmanagedType.LPWStr)] string pszDisplayName,
            out uint pchEaten, out IntPtr ppidl, ref int pdwAttributes);

        void EnumObjects(IntPtr hwnd, uint grfFlags, out IntPtr ppenumIDList);

        void BindToObject(IntPtr pidl, IntPtr pbc, ref Guid riid,
            [MarshalAs(UnmanagedType.Interface)] out object ppv);

        void BindToStorage(IntPtr pidl, IntPtr pbc, ref Guid riid, out IntPtr ppv);

        void CompareIDs(IntPtr lParam, IntPtr pidl1, IntPtr pidl2);

        void CreateViewObject(IntPtr hwndOwner, ref Guid riid, out IntPtr ppv);

        void GetAttributesOf(uint cidl, [MarshalAs(UnmanagedType.LPArray)] IntPtr[] apidl,
            ref uint rgfInOut);

        int GetUIObjectOf(IntPtr hwndOwner, uint cidl,
            [MarshalAs(UnmanagedType.LPArray)] IntPtr[] apidl,
            ref Guid riid, IntPtr rgfReserved,
            [MarshalAs(UnmanagedType.Interface)] out object ppv);

        void GetDisplayNameOf(IntPtr pidl, uint uFlags, IntPtr pName);

        void SetNameOf(IntPtr hwnd, IntPtr pidl,
            [MarshalAs(UnmanagedType.LPWStr)] string pszName, uint uFlags, out IntPtr ppidlOut);
    }

    [ComImport, Guid("000214E4-0000-0000-C000-000000000046")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IContextMenu
    {
        [PreserveSig]
        int QueryContextMenu(IntPtr hMenu, uint indexMenu, uint idCmdFirst,
            uint idCmdLast, uint uFlags);

        void InvokeCommand(ref CMINVOKECOMMANDINFO pici);

        void GetCommandString(UIntPtr idCmd, uint uType, IntPtr pReserved,
            IntPtr pszName, uint cchMax);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct CMINVOKECOMMANDINFO
    {
        public int cbSize;
        public int fMask;
        public IntPtr hwnd;
        public IntPtr lpVerb;
        [MarshalAs(UnmanagedType.LPStr)]
        public string? lpParameters;
        [MarshalAs(UnmanagedType.LPStr)]
        public string? lpDirectory;
        public int nShow;
        public int dwHotKey;
        public IntPtr hIcon;
    }
}
