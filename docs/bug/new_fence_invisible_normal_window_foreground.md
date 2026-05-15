---
name: 托盘新建 Fence 后窗口被推到壁纸下方（bug 6 补全）
description: 当托盘菜单关闭瞬间 foreground 切回普通窗口时，BringNewWindowToFront 走 HWND_BOTTOM 分支仍可能让 fence 被 DWM 推到壁纸下，需要双击桌面 Hide+Show 才能拉回
type: project
---

# 托盘"新建 Fence"后窗口被推到壁纸下方（bug 6 补全）

## 问题描述
托盘右键菜单"新建 Fence"后，桌面上完全看不到新 fence。必须**双击桌面隐藏全部 fence、再双击桌面展示**才能让新 fence 出现。

bug 6《新建 Fence 后不立刻显示》之前修复过类似症状，但只覆盖了"foreground 是桌面/任务栏"的情况；本次回归发生于 foreground 已切回**普通窗口**（如浏览器、编辑器）时。

## 产生原因
1. 用户右键托盘时 foreground 短暂是 `Shell_TrayWnd` 体系；点击"新建 Fence"那一刻菜单关闭，foreground 开始切换。
2. `Dispatcher.Invoke(() => CreateNewFence())` 同步执行 → `SpawnFenceWindow(vm, bringToFront: true)` → `host.Show()`。WPF 的 `Loaded` 事件由 dispatcher 排到后续 cycle 才触发。
3. `host.Loaded` 触发时 foreground 已经切回 menu 的原 owner 应用（如浏览器最大化窗口），即 GetForegroundWindow() 返回的是**普通窗口**。
4. bug 6 修复后的 `BringNewWindowToFront` 在普通窗口前台分支调用 `SetWindowPos(HWND_BOTTOM)` —— 当时假设普通前台时 HWND_BOTTOM 安全。
5. 实测：Windows 11 上对 `WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE` 窗口在**任意** SetWindowPos(HWND_BOTTOM) 调用都可能被 DWM 推到桌面壁纸层下方 —— 不仅限于"前台是桌面/任务栏"的场景。托盘菜单刚关闭时 foreground 处于过渡态，HWND_BOTTOM 此刻被 DWM 当作"准桌面前台"处理，从而压到壁纸下。
6. 之后用户双击桌面：
   - 第一次双击：`ToggleAllFences` 检测到 `IsVisible=true`（WPF 层认为可见）→ 全部 `Hide()`
   - 第二次双击：`fence.Show()` + `EnsureVisibleAboveDesktop` → 桌面前台 → `HoistSingleAboveDesktop(HWND_TOPMOST)` → fence 从壁纸下被拉回 → 可见

## 修复方案
对"用户主动新建 Fence"路径**统一**使用 `HWND_TOPMOST`，不再按 foreground 分支：
- 桌面/任务栏前台 → topmost 是早已验证唯一可靠的方案
- 普通窗口前台 → 同样 topmost；用户感知差别极小（fence 短暂浮在普通窗口之上），随后由既有自愈机制降级

topmost 状态由既有机制自动清除：
- 用户切到任意普通窗口（含当前的 owner 应用本身的下一次 foreground 事件）→ `OnForegroundChanged` → `OnDebouncedForegroundRecovery` → `SendToBottom(HWND_BOTTOM)` 隐式降级 topmost
- 5 秒 z-order 恢复定时器在普通窗口前台时兜底执行同样动作

## 核心代码修改
`src/DesktopFences.Shell/Desktop/DesktopEmbedManager.cs` 的 `BringNewWindowToFront`：

```csharp
public void BringNewWindowToFront(IntPtr hwnd)
{
    if (!NativeMethods.IsWindowVisible(hwnd))
        NativeMethods.ShowWindow(hwnd, NativeMethods.SW_SHOW);

    // 不再按 foreground 分支：托盘菜单关闭瞬间 foreground 处于过渡态，HWND_BOTTOM
    // 即便在 "GetForegroundWindow() 返回普通窗口" 的情况下也可能被 DWM 推到壁纸下。
    // 统一用 HWND_TOPMOST 把新窗口拉到壁纸上方；topmost 由后续 OnDebouncedForegroundRecovery
    // / z-order 恢复定时器在用户切到普通窗口时通过 HWND_BOTTOM 自动降级。
    HoistSingleAboveDesktop(hwnd);
}
```

## 影响范围
- 托盘菜单"新建 Fence" / "新建文件夹映射 Fence..."
- 设置窗口"+ 新建 Fence"按钮（同样走 `CreateNewFence` → `SpawnFenceWindow(bringToFront: true)`）
- 规则触发（`CreateFenceForRule`）创建 Fence
- "恢复最近关闭" / "重置布局" / 导入布局 / 恢复快照

不影响启动加载、监视器配置变化、`ToggleAllFences` 等路径 —— 它们走 `EnsureVisibleAboveDesktop`，其桌面分支已用 topmost、普通分支用 HWND_BOTTOM（这条路径下 foreground 是稳定的非过渡态，HWND_BOTTOM 不会被 DWM 误判）。

## 经验总结
- bug 6 修复时假设"前台是普通窗口 → HWND_BOTTOM 安全"，这个假设在**前台正在过渡** 的瞬间不成立。`GetForegroundWindow()` 返回值无法反映 DWM 内部的"准桌面状态"判定。
- "借用 topmost" 模式（hoist 到 topmost、不修改 `_isTopmost` 标志、依赖后续 `SendToBottom(HWND_BOTTOM)` 隐式降级）是 Win11 上**唯一可靠** 的"立即可见"方案。同样模式已在 `EnsureVisibleAboveDesktop`、`HoistAllAboveDesktop`（timer 自愈 / OnForegroundChanged 桌面分支）中验证。
- 修复 z-order 类 bug 时，不要把"GetForegroundWindow() 此刻返回什么类的窗口"作为分支依据 —— 短暂过渡态下这个判断不可靠。要么所有分支都用 topmost（让 OS 在用户实际交互时降级），要么显式延迟（不可取，闪烁）。
