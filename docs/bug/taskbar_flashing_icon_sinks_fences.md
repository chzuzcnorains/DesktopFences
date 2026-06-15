---
name: 鼠标移到任务栏闪烁图标时 Fence/未归档图标消失
description: 任务栏闪烁的图标多是最小化应用在请求关注；鼠标移过去时它抢到前台（仍最小化、不遮挡桌面），旧逻辑按"非桌面前台"把 fence SendToBottom(HWND_BOTTOM) → 被 Win11 DWM 压到壁纸下方且 5 秒定时器持续重沉永不恢复
type: project
---

# 鼠标移到任务栏闪烁图标时 Fence 面板和未归档图标消失（bug 35）

## 问题描述
当鼠标移动到任务栏上一个**闪烁（请求关注）的图标**时，所有 Fence 面板和未归档桌面图标层（`DesktopIconOverlay`）会一起消失（被压到壁纸下方），必须切换到其他普通窗口才能恢复。期望：保持正常显示。

## 产生原因
属于本项目反复出现的 Win11 z-order 同族 bug（历史 bug 1/2/4/6/10/14/24）。

1. Fence 与 overlay 都是 `WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE` 顶层窗口，稳态停在 `HWND_BOTTOM`。
2. 任务栏「闪烁的图标」几乎总是一个**最小化**的应用在请求关注（`FlashWindowEx`）。当鼠标移到任务栏上时，系统前台锁被释放，那个最小化窗口此前 pending 的 `SetForegroundWindow` 生效 → 它**成为前台窗口，但仍处于最小化状态、并不遮挡桌面**。
3. `OnForegroundChanged` / `OnDebouncedForegroundRecovery` / `SendToBottom` 只用 `GetForegroundWindow()` 的**窗口类名**（`WindowClassUtil.IsDesktopOrTaskbarWindow`）来近似判断"桌面是不是可见的顶层"。最小化窗口的类名既不是桌面类也不是任务栏类 → 守卫**漏判** → `SendToBottom(HWND_BOTTOM)` 照常执行。
4. 但此刻桌面才是实际可见顶层，Win11 DWM 把这两类 toolwindow 压到壁纸下方 → 消失。
5. 5 秒 z-order 恢复定时器的 `else` 分支继续 `SendToBottom`（前台仍是那个未识别的最小化窗口）→ **永远不恢复**。

根本教训：**`GetForegroundWindow()` 返回窗口的"类名"不是可靠信号**——最小化窗口、托盘菜单关闭过渡态、任务栏闪烁缩略图宿主等都会骗过类名判断。历次往 `IsDesktopOrTaskbarWindow` 里加类名只是在打补丁，跨 Win11 版本易碎。

## 修复方案
两条与"具体窗口类名/系统版本"无关的判据，预防 + 自愈兜底双保险：

### (a) 预防：最小化前台一律不下沉
最小化窗口不遮挡任何东西，把 fence 沉到它"之下"必然沉到壁纸下。在 `DesktopEmbedManager` 的**延迟判定点**加 `NativeMethods.IsIconic` 守卫，与现有桌面/任务栏守卫并列：
- `OnDebouncedForegroundRecovery` / `SendToBottom`：在现有 `IsDesktopOrTaskbarWindow(GetForegroundWindow())` 守卫旁追加 `|| IsIconic(foreground)` → 跳过 `HWND_BOTTOM`。

> ⚠️ **`IsIconic` 判断必须延迟到 200ms 防抖之后，绝不能在 `OnForegroundChanged` 即时执行。** 初版曾在 `OnForegroundChanged` 顶部加即时 `if (IsIconic(hwnd)) return;`，结果引入回归：按 Win+D 第二次恢复窗口、或点任务栏还原最小化窗口时，窗口在还原动画初期仍短暂 `IsIconic`，即时守卫直接 `return` → `SendToBottom` 永不执行 → fence/overlay 卡在恢复窗口**前面**。改到防抖后判定即可区分两者：正在还原的窗口届时已退出 iconic（应下沉），单纯闪烁、保持最小化的窗口仍 iconic（不下沉）。`OnForegroundChanged` 自身不做 iconic 判断。

### (b) 自愈兜底：基于 WindowFromPoint 实测遮挡（类名无关）
新增 `IsAnyFenceSunkBehindDesktop()`：对每个可见 fence 取矩形中心点（fence 内容边框中心不透明，可靠命中），`WindowFromPoint` 看那个位置实际是谁在画——若命中**桌面类窗口**（Progman/WorkerW/SHELLDLL_DefView/SysListView32），说明 fence 已沉到壁纸下 → 返回 true；命中其它普通 app 窗口（被合法遮挡）→ 不算沉（保护 bug 14）。

- 5 秒恢复定时器开头插入：`if (IsAnyFenceSunkBehindDesktop()) { HoistAllAboveDesktop(); return; }`，兜住所有"类名漏判"的过渡态。
- `OnDebouncedForegroundRecovery` 末尾追加一次性 50ms 快速复检（`ScheduleSunkRecheck`），把不可见窗口的存在时间从最长 5 秒缩短到几十毫秒。
- hoist 沿用既有「借用 topmost」模式：**不修改 `_isTopmost`**，切到普通窗口时由 `SendToBottom(HWND_BOTTOM)` 隐式降级。

**overlay 不参与探测**：`DesktopIconOverlay` 是 `AllowsTransparency` 层叠窗口，空白处 alpha=0 会被 OS 判为 click-through、`WindowFromPoint` 透传误返回桌面（bug 19）。注册时 `RegisterWindow(hwnd, isOverlay: true)` 标记，探测时跳过；它与 fence 一起下沉，靠 fence 触发整组 `HoistAllAboveDesktop` 一并带回。零 fence 仅 overlay 的极端场景由 (a) 的 `IsIconic` 预防覆盖。

## 核心代码修改
- `src/DesktopFences.Shell/Interop/NativeMethods.cs`：新增 `IsIconic` P/Invoke。
- `src/DesktopFences.Shell/Desktop/DesktopEmbedManager.cs`：
  - 三处 `IsIconic` 预防守卫；
  - `RegisterWindow(IntPtr, bool isOverlay)` 重载 + `_overlayWindow` 字段；
  - `IsAnyFenceSunkBehindDesktop()` 自愈检测；
  - 5 秒定时器开头 + `OnDebouncedForegroundRecovery` 末尾接入自愈；
  - `ScheduleSunkRecheck()` 一次性快速复检定时器（Dispose 中 Stop）。
- `src/DesktopFences.UI/Controls/DesktopIconOverlay.xaml.cs`：注册改为 `RegisterWindow(hwnd, isOverlay: true)`。

## 影响范围与回归确认
- **bug 14**（别的程序最大化时右键托盘小图标，fence 不该浮到最大化窗口之上）：自愈只在 `WindowFromPoint` 命中桌面类时 hoist；最大化 app 盖住 fence 时命中的是该 app → 不 hoist，比旧逻辑更安全。
- **bug 24**（托盘新建 Fence 立即可见）：`BringNewWindowToFront` 不变。
- **bug 1/2/10**（Win+D / 截图工具关闭恢复）：路径不变；`IsIconic` 仅在前台为最小化窗口时介入，与这些场景正交。
- **bug 19**（overlay 命中）：自愈不探测 overlay 中心，规避透传误判。
- **拖拽**（`_isDragging`）：自愈与快速复检都带 `_isDragging` 守卫，不介入。
- **多显示器**：`GetWindowRect` / `WindowFromPoint` 用虚拟屏坐标，副屏 fence 天然有效。

## 经验总结
- 修 z-order bug 时，**不要把"GetForegroundWindow() 此刻返回什么类的窗口"当作唯一分支依据**——最小化窗口、过渡态前台都会漏判。
- 最可靠的"是否被壁纸压住"信号是用 `WindowFromPoint` 在窗口自己的位置实测谁在画，与窗口类名/系统版本无关。
- 层叠透明窗口（overlay）的空白区 alpha=0 会被 `WindowFromPoint` 透传（bug 19），不能直接探测其中心；让它跟随可靠探测的 fence 一起被拉回。
