# 新建 Fence 后不立刻显示的 bug

## 问题描述
右键托盘图标点击"新建 Fence"或"新建文件夹映射 Fence..."后，Fence 已被正确创建并写入布局，但桌面上看不到。
必须再次右键托盘点击"设置..."（或任何弹出本进程的普通窗口）后，新建的 Fence 才会出现。

## 产生原因
1. 托盘菜单（WinForms `NotifyIcon.ContextMenuStrip`）关闭后回调跑在 Dispatcher 上，等到 `CreateNewFence` 真正执行时，前台窗口往往是任务栏（`Shell_TrayWnd`）或桌面（`Progman` / `WorkerW`）。
2. `host.Loaded` 中调用的 `DesktopEmbedManager.EnsureVisibleAboveDesktop` 采用"两步法"：
   - 先 `SetWindowPos(HWND_TOP)` 让窗口"先可见"
   - 若前台不是桌面/任务栏，再 `SetWindowPos(HWND_BOTTOM, SWP_SHOWWINDOW)`；若前台是桌面/任务栏，则保留在 `HWND_TOP`，等 z-order 恢复定时器兜底
3. Windows 11 上对 `WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE` 窗口在桌面前台时调用 `SetWindowPos(HWND_TOP)` **同样会被 DWM 推到桌面壁纸层下方**（早期 README 总结的"只有 `HWND_BOTTOM` 危险"是不完整的认知），结果新窗口完全不可见。
4. 用户随后打开"设置..."时，`SettingsWindow` 成为前台（非桌面/任务栏），触发 `OnForegroundChanged` → `OnDebouncedForegroundRecovery` → 对所有 `_managedWindows` 调用 `SendToBottom`，其中 `SetWindowPos(HWND_BOTTOM, SWP_SHOWWINDOW)` 把已被 DWM 压到壁纸下的新 Fence 重新拉回到 z-order 底部并显示——这就是"打开设置后才看见"的现象。

## 修复方案
关键观察：启动加载、监视器配置变化等"非用户主动"路径在原"两步法"下并没有可见性问题（实际场景下前台往往不是桌面），盲目地把所有路径都改为 `HWND_TOPMOST` 反而会让 fence / overlay 在普通应用之上一直浮动，并连带破坏 Win+D 时桌面图标 overlay 的显示状态。

修复方式是**只在用户主动新建 Fence 的路径**中走"绕开壁纸层压制"的策略：

1. 在 `DesktopEmbedManager` 中新增 `BringNewWindowToFront(IntPtr hwnd)`：
   - 桌面/任务栏前台 → `SetWindowPos(HWND_TOPMOST, SWP_SHOWWINDOW)`，进入 topmost 类绕开壁纸层
   - 普通窗口前台 → `SetWindowPos(HWND_BOTTOM, SWP_SHOWWINDOW)`，直接放回正常 z-order 底部
2. `SpawnFenceWindow` / `SpawnFencesWithGroups` 增加 `bringToFront` 参数，仅在用户主动路径中传 `true`：
   - `CreateNewFence`、`CreatePortalFence`、`CreateFenceForRule`
   - `RestoreClosedFenceById`、`ResetAllFences`
   - 导入布局、恢复快照
3. 启动加载（`LoadFencesAsync`）、监视器配置变化（`OnDisplayConfigChanged`）保持默认 `false`，继续走原 `EnsureVisibleAboveDesktop` 的两步法。
4. topmost 状态由既有机制自动清除：用户切到任意普通窗口 → `OnForegroundChanged` → `OnDebouncedForegroundRecovery` → `SendToBottom(HWND_BOTTOM)`（`HWND_BOTTOM` 隐含降级 topmost）；5 秒 z-order 恢复定时器在前台是非桌面时也会兜底执行同样动作。

## 核心代码修改
`src/DesktopFences.Shell/Desktop/DesktopEmbedManager.cs`

```csharp
public void BringNewWindowToFront(IntPtr hwnd)
{
    if (!NativeMethods.IsWindowVisible(hwnd))
        NativeMethods.ShowWindow(hwnd, NativeMethods.SW_SHOW);

    var foreground = NativeMethods.GetForegroundWindow();
    if (WindowClassUtil.IsDesktopOrTaskbarWindow(foreground))
    {
        // 桌面/任务栏前台 —— HWND_TOP 不可靠，用 HWND_TOPMOST 绕开壁纸层。
        // OnForegroundChanged → SendToBottom(HWND_BOTTOM) 会在用户切到普通窗口时自动清除 topmost。
        NativeMethods.SetWindowPos(
            hwnd, NativeMethods.HWND_TOPMOST,
            0, 0, 0, 0,
            NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE |
            NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_SHOWWINDOW);
    }
    else
    {
        NativeMethods.SetWindowPos(
            hwnd, NativeMethods.HWND_BOTTOM,
            0, 0, 0, 0,
            NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE |
            NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_SHOWWINDOW);
    }
}
```

`src/DesktopFences.App/App.xaml.cs`

```csharp
private void SpawnFenceWindow(FencePanelViewModel vm, bool bringToFront = false)
{
    // ...
    host.Loaded += (_, _) =>
    {
        var hwnd = new WindowInteropHelper(host).Handle;
        if (hwnd == IntPtr.Zero) return;
        if (bringToFront)
            _embedManager!.BringNewWindowToFront(hwnd);
        else
            _embedManager!.EnsureVisibleAboveDesktop(hwnd);
    };
    host.Show();
    // ...
}
```

## 影响范围
- 托盘菜单"新建 Fence" / "新建文件夹映射 Fence..."的可见性
- 规则触发（`CreateFenceForRule`）创建 Fence 时的可见性
- "恢复最近关闭"、"重置布局" 时新 fence 的可见性
- 导入布局 / 恢复快照后的可见性
- 不影响启动加载、监视器配置变化、`ToggleAllFences` 等路径（继续走 `EnsureVisibleAboveDesktop` 的两步法）

## 经验总结
- Windows 11 上桌面/任务栏前台 + `WS_EX_TOOLWINDOW` 组合，对 `HWND_TOP` 与 `HWND_BOTTOM` 都不安全；只有 `HWND_TOPMOST` 能稳妥地让窗口呈现在桌面壁纸之上。
- 但 `HWND_TOPMOST` 不应被用于已存在 fence 的常规可见性维护——否则 fence / overlay 会一直浮动在普通应用之上。topmost 必须只用于"用户主动新建"这种短暂状态，让既有的 `OnDebouncedForegroundRecovery → HWND_BOTTOM` 在用户切走时清除它。
- 修改全局 z-order 行为前，先列出所有调用方（启动加载、`ToggleAllFences`、`DesktopIconOverlay` 注册、规则创建、恢复关闭等），评估每个路径是否可以承受新行为——否则会引入"未归档图标 overlay 不显示"、"窗口一直浮动"这类连锁副作用。
