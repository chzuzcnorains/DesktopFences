# 启动时未归档图标不显示 / 截图后面板消失

## 症状

1. **启动时**：未归档图标（DesktopIconOverlay）有时不显示，必须切换一次前景窗口才出现。
2. **截图后**：偶尔截图工具关闭后，fence 面板与未归档 overlay 一起消失，同样需要切换前景窗口才能恢复。

## 根因

与 bug 6《新建 Fence 不立刻显示》同构，均为 Windows 11 z-order 特性导致：

> 当前台窗口是桌面（`Progman`/`WorkerW`）或任务栏（`Shell_TrayWnd`/`Shell_SecondaryTrayWnd`）时，对 `WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE` 窗口调用 `SetWindowPos(HWND_TOP)` 或 `HWND_BOTTOM` 都会被 DWM 推到桌面壁纸下方；**只有 `HWND_TOPMOST` 安全。**

### 入口 A（启动 overlay）

启动流程：`LoadFencesAsync` → `CreateDesktopOverlay` → `DesktopIconOverlay.Show()` → `OnLoaded` → `RegisterWindow(hwnd)`。

原 `RegisterWindow`（[DesktopEmbedManager.cs:91-111](../src/DesktopFences.Shell/Desktop/DesktopEmbedManager.cs#L91-L111)）直接调用 `SetWindowPos(HWND_TOP)`，未区分前台是否为桌面/任务栏。启动时前台几乎总是 `Progman` → overlay 被压到壁纸下。

启动加载的 fence 路径：`SpawnFencesWithGroups(bringToFront=false)` → `EnsureVisibleAboveDesktop`。原 `EnsureVisibleAboveDesktop` 桌面分支"保留 `HWND_TOP` 等定时器兜底"，同样不可靠。

### 入口 B（截图后自愈）

典型流程：
1. 用户触发截图（Win+Shift+S / 截图工具）。
2. 截图工具成为前台 → `OnForegroundChanged` 触发，但不是桌面，启动 200ms debounce。
3. 用户完成截图，截图工具消失。
4. **前台立刻回到 `Progman`**。
5. 原 `OnForegroundChanged` 行 275：`if (IsDesktopOrTaskbarWindow(hwnd)) return;` → 不做任何恢复。
6. `OnDebouncedForegroundRecovery` 行 312：同样桌面前台 return。
7. 5 秒定时器行 76：同样桌面前台 return。

这些"保护"本意是避免 `HWND_BOTTOM` 把窗口压下去，但副作用是：**一旦窗口已经被压下且用户停留在桌面，自愈机制完全失效。**

## 修复方案

### 核心原则（来自 bug 6 并扩展）

- `_isTopmost` 字段**仍然只由 Win+D / Peek 拥有**，不引入新字段。
- "借用 topmost"路径：`RegisterWindow` / `OnForegroundChanged` 桌面前台分支 / 5 秒定时器桌面前台分支 → 用 `HWND_TOPMOST` 拉回窗口，**不修改 `_isTopmost`**。
- 清除 topmost：用户切到任意普通窗口 → `OnDebouncedForegroundRecovery` → `SendToBottom(HWND_BOTTOM)`（`HWND_BOTTOM` 隐含降级 topmost，是 user32 隐式状态机）。

### 1. 修复 `RegisterWindow`

在 `_managedWindows.Add(hwnd)` 之后直接复用 `BringNewWindowToFront(hwnd)` 的分支策略：
- 前台是桌面/任务栏 → `SetWindowPos(HWND_TOPMOST, SWP_SHOWWINDOW)`
- 前台是普通窗口 → `SetWindowPos(HWND_BOTTOM, SWP_SHOWWINDOW)`

影响范围：`DesktopIconOverlay` 与所有 fence，都获得"启动即可见"保证。

### 2. 修复 `EnsureVisibleAboveDesktop`

桌面分支从"保留 `HWND_TOP` 等定时器"改为与 `BringNewWindowToFront` 一致的 `HWND_TOPMOST`，避免与 `RegisterWindow` 互相覆盖。

### 3. 修复 `OnForegroundChanged` 桌面前台分支

不再 `return`，改为：对所有 `_managedWindows` 调 `SetWindowPos(HWND_TOPMOST, SWP_SHOWWINDOW)`（不动 `_isTopmost`），再 `return`。

覆盖入口 B 的"截图工具关闭 → 前台立刻回到 Progman"的瞬间，无需等 5 秒。

### 4. 修复 5 秒 z-order 恢复定时器

桌面前台分支从"跳过"改为：对每个 hwnd 调 `SetWindowPos(HWND_TOPMOST, SWP_SHOWWINDOW)`（不动 `_isTopmost`）。

作为兜底机制，覆盖边界情况（例如截图工具激活时未触发 `EVENT_SYSTEM_FOREGROUND`）。

### 5. 保留不变的

- `SendToBottom` 桌面前台保护：保留，避免再次把可见窗口压下去。
- `OnDebouncedForegroundRecovery` 桌面前台 `return`：保留，被 200ms 防抖保护，桌面前台已由修复 3 即时处理。
- `_isTopmost` 状态机：不碰，Win+D / Peek 路径完全不受影响。
- `_isDragging` / `_isPeekActive` / `_pendingTopmost` 守护：全部保留，避免冲突。

## 改动文件

- `src/DesktopFences.Shell/Desktop/DesktopEmbedManager.cs`：四处修改：`RegisterWindow`、`EnsureVisibleAboveDesktop`、`OnForegroundChanged` 桌面前台分支、5 秒定时器。

## 与历史 bug 修复的关系

- **bug 2（截图后 Fence 面板不展示）**：原修复在桌面分支 `return`，本次反过来"主动用 HWND_TOPMOST 拉回"——但 `SendToBottom` 桌面前台保护**仍然保留**，原修复的边界逻辑不变。
- **bug 6（新建 Fence 不立刻显示）**：本次把 `BringNewWindowToFront` 策略扩展到 `RegisterWindow` 与自愈路径，是对 bug 6 的补全。

## 验证

见 [bug-bug-snappy-dragon.md](C:\Users\Norains\.claude\plans\bug-bug-snappy-dragon.md) 中的验证步骤（冷启动、截图场景、回归验证、性能验证）。
