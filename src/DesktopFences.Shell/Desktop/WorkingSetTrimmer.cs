using System.Runtime;
using System.Runtime.InteropServices;
using DesktopFences.Shell.Interop;

namespace DesktopFences.Shell.Desktop;

/// <summary>
/// 空闲时主动压缩托管堆并把工作集页面换出到备用列表，降低任务管理器
/// 「内存」列（专用工作集）读数。换出的页面留在物理内存备用列表中，
/// 再次访问只是软缺页（无磁盘 IO），代价可忽略。
/// 调用时机由 App 控制（启动稳定后 / 全部隐藏 / 输入空闲），本类只做动作。
/// </summary>
public static class WorkingSetTrimmer
{
    /// <summary>
    /// 完整裁剪：LOH 压缩 + 两轮 full GC（第一轮后 finalizer 可能又释放非托管资源，
    /// 第二轮回收 finalizer 复活的对象）→ SetProcessWorkingSetSize(-1,-1) 换出。
    /// 阻塞调用线程数十毫秒量级，勿在拖拽等交互路径上调用。
    /// </summary>
    public static void Trim()
    {
        GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
        GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
        GC.WaitForPendingFinalizers();
        GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);

        // (-1,-1) = 尽可能清空工作集。GetCurrentProcess 返回伪句柄，无需关闭。
        NativeMethods.SetProcessWorkingSetSize(
            NativeMethods.GetCurrentProcess(), new IntPtr(-1), new IntPtr(-1));
    }

    /// <summary>距最后一次键盘/鼠标输入的时长（全系统而非本进程）。失败时返回 Zero（视为非空闲）。</summary>
    public static TimeSpan GetInputIdleTime()
    {
        var info = new NativeMethods.LASTINPUTINFO
        {
            cbSize = (uint)Marshal.SizeOf<NativeMethods.LASTINPUTINFO>()
        };
        if (!NativeMethods.GetLastInputInfo(ref info))
            return TimeSpan.Zero;

        // Environment.TickCount 与 dwTime 同源（GetTickCount），unchecked 差值对 49.7 天回绕安全
        return TimeSpan.FromMilliseconds(unchecked((uint)Environment.TickCount - info.dwTime));
    }
}
