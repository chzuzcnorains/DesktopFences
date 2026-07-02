using System.Runtime.InteropServices;
using System.Windows.Threading;
using DesktopFences.Shell.Interop;

namespace DesktopFences.Shell.Desktop;

/// <summary>
/// 桌面排序键（对应桌面右键「排序方式」子菜单的四项）。
/// </summary>
public enum DesktopSortKey
{
    Unknown,
    Name,          // 名称   System.ItemNameDisplay
    Size,          // 大小   System.Size
    ItemType,      // 项目类型 System.ItemTypeText
    DateModified,  // 修改日期 System.DateModified
}

/// <summary>
/// 监听「隐藏的原生桌面视图」的状态变化，把桌面右键菜单的
/// 「查看（大/中/小图标）/ 排序方式 / 刷新」联动到覆盖层。
///
/// 背景：覆盖层空白区 alpha=0 透传，桌面空白右键弹出的是原生 SHELLDLL_DefView
/// 菜单——用户点「查看→大图标」等命令实际已在被 ShowWindow(SW_HIDE) 隐藏的
/// SysListView32 上生效，只是不可见。本类读取该视图的真实状态并广播变化：
///
/// - 状态读取：Raymond Chen 官方路径 ShellWindows → FindWindowSW(CSIDL_DESKTOP,
///   SWC_DESKTOP) → IServiceProvider → IShellBrowser → QueryActiveShellView →
///   IFolderView2.GetViewModeAndIconSize / GetSortColumns。**COM 代理缓存复用**：
///   获取链路只在缓存缺失时走一遍，之后每次检查仅 2~3 个轻量跨进程调用；任一
///   调用失败即丢弃缓存、下周期重建（explorer 重启自动恢复）。
/// - 变化触发：**250ms 快速轮询是主通道**——隐藏的 SysListView32 实测大多不发
///   REORDER 无障碍事件（不可见时常跳过重排），钩子只能当加速器。轮询与上次
///   快照比对：尺寸变了发 IconSizeChanged、排序变了发 SortChanged。
///   SetWinEventHook(EVENT_OBJECT_REORDER)（200ms 去抖）触发的比对若无状态变化
///   则视为「刷新」发 DesktopRefreshed（轮询看不到 F5——刷新没有可轮询的状态）。
///
/// 线程模型：Start() 必须在 UI 线程调用（WinEvent OUTOFCONTEXT 回调经安装线程的
/// 消息循环派发，DispatcherTimer 亦然），因此所有事件都在 UI 线程触发。
/// </summary>
public sealed class DesktopViewMonitor : IDisposable
{
    private readonly IntPtr _listViewHwnd;
    private IntPtr _winEventHookId;
    private NativeMethods.WinEventDelegate? _winEventProc; // 防 GC 回收委托
    private DispatcherTimer? _debounceTimer;
    private DispatcherTimer? _pollTimer;
    private bool _disposed;

    /// <summary>缓存的桌面视图 COM 代理（仅 UI 线程使用）。缓存使 250ms 快速轮询
    /// 的每次检查只剩 2~3 个轻量跨进程调用；任一调用失败即丢弃，下周期重建。</summary>
    private IFolderView2? _cachedView;

    // 上次快照（用于变化比对）
    private int _lastIconSize = -1;
    private DesktopSortKey _lastSortKey = DesktopSortKey.Unknown;
    private bool _lastSortAscending = true;

    /// <summary>图标尺寸变化（96=大 / 48=中 / 32=小，shell 逻辑尺寸，可直接当 DIP 用）。</summary>
    public event Action<int>? IconSizeChanged;

    /// <summary>排序方式变化（键 + 是否递增）。</summary>
    public event Action<DesktopSortKey, bool>? SortChanged;

    /// <summary>桌面刷新（REORDER 触发但尺寸/排序均未变：F5、右键刷新、文件增删）。</summary>
    public event Action? DesktopRefreshed;

    public DesktopViewMonitor(IntPtr desktopListViewHwnd)
    {
        _listViewHwnd = desktopListViewHwnd;
    }

    /// <summary>读取当前桌面图标尺寸。失败（explorer 忙/重启中）返回 false。</summary>
    public bool TryGetIconSize(out int iconSize)
    {
        var state = TryReadViewState();
        iconSize = state?.IconSize ?? 0;
        return state is not null;
    }

    /// <summary>读取当前桌面排序方式。桌面从未设置过排序时 key 为 Unknown。</summary>
    public bool TryGetSort(out DesktopSortKey key, out bool ascending)
    {
        var state = TryReadViewState();
        key = state?.SortKey ?? DesktopSortKey.Unknown;
        ascending = state?.SortAscending ?? true;
        return state is not null;
    }

    /// <summary>开始监听。必须在 UI 线程调用。</summary>
    public void Start()
    {
        // 初始快照：之后只广播「变化」
        var state = TryReadViewState();
        if (state is not null)
        {
            _lastIconSize = state.IconSize;
            _lastSortKey = state.SortKey;
            _lastSortAscending = state.SortAscending;
        }

        // REORDER 事件去抖：一次刷新/排序会触发一串事件，200ms 静默后再比对
        _debounceTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
        _debounceTimer.Tick += (_, _) =>
        {
            _debounceTimer!.Stop();
            CheckForChanges(fromReorder: true);
        };

        if (_listViewHwnd != IntPtr.Zero)
        {
            uint tid = NativeMethods.GetWindowThreadProcessId(_listViewHwnd, out uint pid);
            _winEventProc = OnWinEvent;
            _winEventHookId = NativeMethods.SetWinEventHook(
                NativeMethods.EVENT_OBJECT_REORDER, NativeMethods.EVENT_OBJECT_REORDER,
                IntPtr.Zero, _winEventProc, pid, tid,
                NativeMethods.WINEVENT_OUTOFCONTEXT);
        }

        // 快速轮询主通道：隐藏 ListView 大多不发 REORDER（不可见时常跳过重排），
        // 尺寸/排序变化主要靠这里发现。COM 代理已缓存，每次检查仅 2~3 个轻量
        // 跨进程调用，250ms 保证亚半秒响应且开销可忽略。
        _pollTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        _pollTimer.Tick += (_, _) => CheckForChanges(fromReorder: false);
        _pollTimer.Start();
    }

    private void OnWinEvent(
        IntPtr hook, uint eventType, IntPtr hwnd,
        int idObject, int idChild, uint thread, uint time)
    {
        if (_disposed || hwnd != _listViewHwnd) return;
        // 重置去抖窗口
        _debounceTimer?.Stop();
        _debounceTimer?.Start();
    }

    private void CheckForChanges(bool fromReorder)
    {
        if (_disposed) return;

        var state = TryReadViewState();
        if (state is null) return;

        bool changed = false;

        if (state.IconSize > 0 && state.IconSize != _lastIconSize)
        {
            _lastIconSize = state.IconSize;
            changed = true;
            IconSizeChanged?.Invoke(state.IconSize);
        }

        if (state.SortKey != _lastSortKey || state.SortAscending != _lastSortAscending)
        {
            _lastSortKey = state.SortKey;
            _lastSortAscending = state.SortAscending;
            // Unknown（未识别的排序列）不广播——覆盖层无法复现该排序
            if (state.SortKey != DesktopSortKey.Unknown)
            {
                changed = true;
                SortChanged?.Invoke(state.SortKey, state.SortAscending);
            }
        }

        // REORDER 来了但尺寸/排序都没变 → 用户点了「刷新」（或桌面文件增删）
        if (fromReorder && !changed)
            DesktopRefreshed?.Invoke();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _debounceTimer?.Stop();
        _debounceTimer = null;
        _pollTimer?.Stop();
        _pollTimer = null;
        if (_winEventHookId != IntPtr.Zero)
        {
            NativeMethods.UnhookWinEvent(_winEventHookId);
            _winEventHookId = IntPtr.Zero;
        }
        _winEventProc = null;
        DropCachedView();
    }

    // ───────────────────────── IFolderView2 状态读取 ─────────────────────────

    private sealed record ViewState(int IconSize, DesktopSortKey SortKey, bool SortAscending);

    private const int CSIDL_DESKTOP = 0x0000;
    private const int SWC_DESKTOP = 0x08;
    private const int SWFO_NEEDDISPATCH = 0x01;

    // FMTID_Storage：桌面「排序方式」四项全部落在这一个 fmtid 下，仅 pid 不同
    private static readonly Guid FmtidStorage = new("B725F130-47EF-101A-A5F1-02608C9EEBAC");
    private const uint PidName = 10;         // System.ItemNameDisplay
    private const uint PidSize = 12;         // System.Size
    private const uint PidItemType = 4;      // System.ItemTypeText
    private const uint PidDateModified = 14; // System.DateModified

    /// <summary>
    /// 读取桌面视图状态（走缓存代理，缺失时重建）。任一环节失败返回 null 并丢弃
    /// 缓存（不抛异常——explorer 重启窗口期、RPC 断连等都是正常波动，下周期自愈）。
    /// </summary>
    private ViewState? TryReadViewState()
    {
        var view = _cachedView ??= AcquireDesktopFolderView();
        if (view is null) return null;

        try
        {
            if (view.GetViewModeAndIconSize(out _, out int iconSize) != 0)
            {
                DropCachedView();
                return null;
            }

            var sortKey = DesktopSortKey.Unknown;
            bool ascending = true;
            if (view.GetSortColumnCount(out int count) == 0 && count >= 1)
            {
                var columns = new SORTCOLUMN[count];
                if (view.GetSortColumns(columns, count) == 0)
                {
                    sortKey = MapSortKey(columns[0].PropKey);
                    ascending = columns[0].Direction >= 0; // SORT_ASCENDING=1 / SORT_DESCENDING=-1
                }
            }

            return new ViewState(iconSize, sortKey, ascending);
        }
        catch
        {
            DropCachedView(); // RPC 断连（explorer 重启）等 → 丢弃，下周期重建
            return null;
        }
    }

    private void DropCachedView()
    {
        if (_cachedView is null) return;
        try { Marshal.ReleaseComObject(_cachedView); } catch { }
        _cachedView = null;
    }

    /// <summary>走完整获取链路拿桌面视图代理（仅缓存缺失时调用），中间对象即取即放。</summary>
    private static IFolderView2? AcquireDesktopFolderView()
    {
        object? shellWindowsObj = null;
        object? dispatchObj = null;
        object? browserObj = null;
        object? viewObj = null;
        try
        {
            shellWindowsObj = new ShellWindowsClass();
            var shellWindows = (IShellWindows)shellWindowsObj;

            object loc = CSIDL_DESKTOP;
            object root = null!; // VT_EMPTY
            if (shellWindows.FindWindowSW(ref loc, ref root, SWC_DESKTOP, out _, SWFO_NEEDDISPATCH, out dispatchObj) != 0)
                return null;
            if (dispatchObj is not IShellServiceProvider sp) return null;

            Guid sidTopLevelBrowser = new("4C96BE40-915C-11CF-99D3-00AA004AE837");
            Guid iidShellBrowser = new("000214E2-0000-0000-C000-000000000046");
            if (sp.QueryService(ref sidTopLevelBrowser, ref iidShellBrowser, out browserObj) != 0 ||
                browserObj is not IShellBrowser browser)
                return null;

            if (browser.QueryActiveShellView(out viewObj) != 0 || viewObj is not IFolderView2 view)
                return null;

            viewObj = null; // 所有权移交给调用方缓存，finally 不释放
            return view;
        }
        catch
        {
            return null;
        }
        finally
        {
            ReleaseCom(viewObj);
            ReleaseCom(browserObj);
            ReleaseCom(dispatchObj);
            ReleaseCom(shellWindowsObj);
        }
    }

    private static DesktopSortKey MapSortKey(PROPERTYKEY key)
    {
        if (key.FmtId != FmtidStorage) return DesktopSortKey.Unknown;
        return key.Pid switch
        {
            PidName => DesktopSortKey.Name,
            PidSize => DesktopSortKey.Size,
            PidItemType => DesktopSortKey.ItemType,
            PidDateModified => DesktopSortKey.DateModified,
            _ => DesktopSortKey.Unknown,
        };
    }

    private static void ReleaseCom(object? o)
    {
        if (o is not null && Marshal.IsComObject(o))
            Marshal.ReleaseComObject(o);
    }

    // ───────────────────────── COM 声明（仅本类使用） ─────────────────────────

    [StructLayout(LayoutKind.Sequential)]
    private struct PROPERTYKEY
    {
        public Guid FmtId;
        public uint Pid;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SORTCOLUMN
    {
        public PROPERTYKEY PropKey;
        public int Direction; // SORT_ASCENDING=1, SORT_DESCENDING=-1
    }

    [ComImport, Guid("9BA05972-F6A8-11CF-A442-00A0C90A8F39")]
    private class ShellWindowsClass { }

    // 必须用 dual/vtable 早绑定：InterfaceIsIDispatch 会让 .NET RCW 走「按名字」的
    // GetIDsOfNames 晚绑定，ShellWindows 对 "FindWindowSW" 解析失败，抛
    // DISP_E_MEMBERNOTFOUND（0x80020003）。dual 声明下 CLR 自动跳过 IDispatch 的
    // 7 个槽位，之后按声明顺序对位 vtable —— FindWindowSW 之前的 8 个方法仅占位。
    [ComImport, Guid("85CB6900-4D95-11CF-960C-0080C7F4EE85")]
    [InterfaceType(ComInterfaceType.InterfaceIsDual)]
    private interface IShellWindows
    {
        [PreserveSig] int get_Count(out int count);
        [PreserveSig] int Item([In, MarshalAs(UnmanagedType.Struct)] object index, [MarshalAs(UnmanagedType.IDispatch)] out object? folder);
        [PreserveSig] int _NewEnum([MarshalAs(UnmanagedType.IUnknown)] out object? ppunk);
        [PreserveSig] int Register([MarshalAs(UnmanagedType.IDispatch)] object pid, int hwnd, int swClass, out int plCookie);
        [PreserveSig] int RegisterPending(int lThreadId, [In, MarshalAs(UnmanagedType.Struct)] ref object pvarLoc, [In, MarshalAs(UnmanagedType.Struct)] ref object pvarLocRoot, int swClass, out int plCookie);
        [PreserveSig] int Revoke(int lCookie);
        [PreserveSig] int OnNavigate(int lCookie, [In, MarshalAs(UnmanagedType.Struct)] ref object pvarLoc);
        [PreserveSig] int OnActivated(int lCookie, bool fActive);
        [PreserveSig] int FindWindowSW(
            [In, MarshalAs(UnmanagedType.Struct)] ref object pvarLoc,
            [In, MarshalAs(UnmanagedType.Struct)] ref object pvarLocRoot,
            int swClass, out int phwnd, int swfwOptions,
            [MarshalAs(UnmanagedType.IDispatch)] out object? ppdispOut);
    }

    // 命名为 IShellServiceProvider 以避开 System.IServiceProvider；GUID 才是身份
    [ComImport, Guid("6D5140C1-7436-11CE-8034-00AA006009FA")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellServiceProvider
    {
        [PreserveSig]
        int QueryService(ref Guid guidService, ref Guid riid,
            [MarshalAs(UnmanagedType.IUnknown)] out object? ppv);
    }

    // 只声明到 QueryActiveShellView；前面的槽位用占位符保持 vtable 顺序（绝不调用）
    [ComImport, Guid("000214E2-0000-0000-C000-000000000046")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellBrowser
    {
        // IOleWindow
        [PreserveSig] int GetWindow(out IntPtr phwnd);
        [PreserveSig] int ContextSensitiveHelp(int fEnterMode);
        // IShellBrowser
        [PreserveSig] int InsertMenusSB(IntPtr hmenuShared, IntPtr lpMenuWidths);
        [PreserveSig] int SetMenuSB(IntPtr hmenuShared, IntPtr holemenuRes, IntPtr hwndActiveObject);
        [PreserveSig] int RemoveMenusSB(IntPtr hmenuShared);
        [PreserveSig] int SetStatusTextSB(IntPtr pszStatusText);
        [PreserveSig] int EnableModelessSB(int fEnable);
        [PreserveSig] int TranslateAcceleratorSB(IntPtr pmsg, ushort wID);
        [PreserveSig] int BrowseObject(IntPtr pidl, uint wFlags);
        [PreserveSig] int GetViewStateStream(uint grfMode, out IntPtr ppStrm);
        [PreserveSig] int GetControlWindow(uint id, out IntPtr phwnd);
        [PreserveSig] int SendControlMsg(uint id, uint uMsg, IntPtr wParam, IntPtr lParam, out IntPtr pret);
        [PreserveSig] int QueryActiveShellView([MarshalAs(UnmanagedType.IUnknown)] out object? ppshv);
    }

    // IFolderView(14 槽) + IFolderView2 组合 vtable。占位方法仅撑槽位、绝不调用；
    // 只有 GetCurrentFolderFlags 之后的 GetSortColumnCount / GetSortColumns /
    // GetViewModeAndIconSize 是本类实际使用的入口。
    [ComImport, Guid("1AF3A467-214F-4298-908E-06B03E0B39F9")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IFolderView2
    {
        // ── IFolderView ──
        [PreserveSig] int GetCurrentViewMode(out uint pViewMode);
        [PreserveSig] int SetCurrentViewMode(uint viewMode);
        [PreserveSig] int GetFolder(ref Guid riid, out IntPtr ppv);
        [PreserveSig] int Item(int iItemIndex, out IntPtr ppidl);
        [PreserveSig] int ItemCount(uint uFlags, out int pcItems);
        [PreserveSig] int Items(uint uFlags, ref Guid riid, out IntPtr ppv);
        [PreserveSig] int GetSelectionMarkedItem(out int piItem);
        [PreserveSig] int GetFocusedItem(out int piItem);
        [PreserveSig] int GetItemPosition(IntPtr pidl, out NativeMethods.POINT ppt);
        [PreserveSig] int GetSpacing(IntPtr ppt);
        [PreserveSig] int GetDefaultSpacing(out NativeMethods.POINT ppt);
        [PreserveSig] int GetAutoArrange();
        [PreserveSig] int SelectItem(int iItem, uint dwFlags);
        [PreserveSig] int SelectAndPositionItems(uint cidl, IntPtr apidl, IntPtr apt, uint dwFlags);
        // ── IFolderView2 ──
        [PreserveSig] int SetGroupBy(IntPtr key, int fAscending);
        [PreserveSig] int GetGroupBy(IntPtr pkey, IntPtr pfAscending);
        [PreserveSig] int SetViewProperty(IntPtr pidl, IntPtr propkey, IntPtr propvar);
        [PreserveSig] int GetViewProperty(IntPtr pidl, IntPtr propkey, IntPtr ppropvar);
        [PreserveSig] int SetTileViewProperties(IntPtr pidl, [MarshalAs(UnmanagedType.LPWStr)] string pszPropList);
        [PreserveSig] int SetExtendedTileViewProperties(IntPtr pidl, [MarshalAs(UnmanagedType.LPWStr)] string pszPropList);
        [PreserveSig] int SetText(int iType, [MarshalAs(UnmanagedType.LPWStr)] string pwszText);
        [PreserveSig] int SetCurrentFolderFlags(uint dwMask, uint dwFlags);
        [PreserveSig] int GetCurrentFolderFlags(out uint pdwFlags);
        [PreserveSig] int GetSortColumnCount(out int pcColumns);
        [PreserveSig] int SetSortColumns([In, MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 1)] SORTCOLUMN[] rgSortColumns, int cColumns);
        [PreserveSig] int GetSortColumns([Out, MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 1)] SORTCOLUMN[] rgSortColumns, int cColumns);
        [PreserveSig] int GetItem(int iItem, ref Guid riid, out IntPtr ppv);
        [PreserveSig] int GetVisibleItem(int iStart, int fPrevious, out int piItem);
        [PreserveSig] int GetSelectedItem(int iStart, out int piItem);
        [PreserveSig] int GetSelection(int fNoneImpliesFolder, out IntPtr ppsia);
        [PreserveSig] int GetSelectionState(IntPtr pidl, out uint pdwFlags);
        [PreserveSig] int InvokeVerbOnSelection([MarshalAs(UnmanagedType.LPStr)] string pszVerb);
        [PreserveSig] int SetViewModeAndIconSize(int uViewMode, int iImageSize);
        [PreserveSig] int GetViewModeAndIconSize(out int puViewMode, out int piImageSize);
    }
}
