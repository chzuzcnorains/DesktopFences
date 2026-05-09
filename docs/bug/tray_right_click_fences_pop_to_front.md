# 右键托盘小图标导致 fence 浮到最大化窗口之上的 bug

## 问题描述
当其他程序最大化处于前台时，用户右键单击系统托盘（任务栏通知区）中本程序的小图标，所有 fence 面板和未归档桌面图标 overlay 会被强行拉到 `HWND_TOPMOST`，浮在最大化窗口之上。期望行为是：右键托盘只弹出菜单本身，已有 fence 不应改变 z-order。

## 复现步骤
1. 启动 DesktopFences。
2. 打开任意一款全屏/最大化的应用（浏览器、IDE、视频播放器等）。
3. 右键点击任务栏通知区的 DesktopFences 托盘图标。
4. 现象：所有 fence + 未归档图标 overlay 立刻浮到最大化窗口之上。

## 产生原因
1. 托盘小图标位于任务栏通知区。Windows 在派发右键事件给 `NotifyIcon` 之前，会让 `Shell_TrayWnd`（任务栏窗口）短暂成为前台窗口，触发 `EVENT_SYSTEM_FOREGROUND` 钩子。
2. `DesktopEmbedManager.OnForegroundChanged` 在非 `_isTopmost` 分支判断前台是不是"桌面/任务栏"窗口，如果是就调用 `HoistAllAboveDesktop()`（设为 `HWND_TOPMOST`）。判断使用的是 `WindowClassUtil.IsDesktopOrTaskbarWindow`，该方法把 `Shell_TrayWnd` 归入"桌面相关"。
3. 同样的判断也存在于 5 秒 z-order 自愈定时器的 `Elapsed` 回调里。
4. 这一段 hoist 逻辑的初衷是处理"截图工具关闭后 foreground 立刻回到 `Progman`，DWM 把 fence 压到壁纸下"的场景——典型场景的前台是**真桌面 (`Progman` / `WorkerW`)**，并不是任务栏。但 `IsDesktopOrTaskbarWindow` 把任务栏一起算进来了，于是任何"前台短暂切到任务栏"的瞬间（包括右键托盘、点击任务栏空白处、点击开始按钮等）都会让 fence 抢到 topmost。
5. 之所以历史代码把任务栏并入"桌面相关"——参考 [bug #1](win_d_right_click_panel_hide.md)：当时是为了在 `_isTopmost`（Win+D 已生效）状态下避免任务栏交互撤销 topmost。而**非 topmost 分支**借用同一个方法时附带把"任务栏前台 → 主动 hoist"也变成默认行为，这是越权。

## 修复方案
把"非 topmost 时主动 hoist"的触发条件从 `IsDesktopOrTaskbarWindow` 收紧到 `IsDesktopWindow`（仅 `Progman` / `WorkerW` / `SHELLDLL_DefView` / `SysListView32`），任务栏前台时既不 hoist 也不 `SendToBottom`，让现有 fence 维持原 z-order。

`_isTopmost` 分支保持原样——Win+D 期间任务栏交互不应撤销 topmost，那个场景的 `IsDesktopOrTaskbarWindow` 是必要的。

## 核心代码修改
`src/DesktopFences.Shell/Desktop/DesktopEmbedManager.cs`

### `OnForegroundChanged` 非 topmost 分支
```csharp
else
{
    // 真桌面前台 (Progman/WorkerW/...)：截图工具/最大化窗口关闭后 foreground 回到 Progman，
    // 此时 fence 可能已被 DWM 压到壁纸下，主动用 HWND_TOPMOST 拉回。
    if (WindowClassUtil.IsDesktopWindow(hwnd))
    {
        HoistAllAboveDesktop();
        return;
    }

    // 任务栏 / 任务栏菜单前台（典型：右键托盘小图标时 foreground 短暂切到 Shell_TrayWnd）：
    // 不 hoist 也不 SendToBottom——其他程序最大化时若 hoist，fence 会抢到 topmost
    // 浮在最大化窗口之上。
    if (WindowClassUtil.IsDesktopOrTaskbarWindow(hwnd))
        return;

    StartForegroundDebounce();
}
```

### 5 秒 z-order 自愈定时器
```csharp
var foreground = NativeMethods.GetForegroundWindow();
if (WindowClassUtil.IsDesktopWindow(foreground))
{
    HoistAllAboveDesktop();
}
else if (WindowClassUtil.IsDesktopOrTaskbarWindow(foreground))
{
    // 任务栏前台：不 hoist。SendToBottom 在任务栏前台时本身就是 no-op，直接跳过。
}
else
{
    foreach (var hwnd in _managedWindows)
        SendToBottom(hwnd);
}
```

## 影响范围
- 修复：其他程序最大化时右键托盘小图标，fence/overlay 不再抢到前台。
- 不影响：
  - Win+D → 右键托盘 → fence 保持 topmost（走 `_isTopmost` 分支，逻辑未变）。
  - 截图工具关闭 → foreground 回到 `Progman` → fence 自动从壁纸下层拉回（仍由 `IsDesktopWindow` 分支处理）。
  - 用户主动新建 fence 时 `BringNewWindowToFront` 的 hoist 行为（独立路径，未修改）。
  - `OnDebouncedForegroundRecovery` 的桌面/任务栏跳过保护（保留 `IsDesktopOrTaskbarWindow`，因为 `SendToBottom` 在任一情况下都不安全）。

## 经验总结
- Windows 11 上"前台切到桌面"和"前台切到任务栏"是两类完全不同的事件：
  - 桌面前台 (`Progman` / `WorkerW`)：DWM 可能把 `WS_EX_TOOLWINDOW` 窗口压到壁纸下，需要主动用 `HWND_TOPMOST` 拉回。
  - 任务栏前台 (`Shell_TrayWnd`)：仅是用户与任务栏 UI 的临时交互，z-order 不应被打扰。
- `WindowClassUtil` 同时提供了 `IsDesktopWindow`（窄）和 `IsDesktopOrTaskbarWindow`（宽）两个方法，调用方必须按场景选择正确的粒度——用错就会引入 z-order 副作用。
- 类似的 z-order 修复历史上多次"复用相邻分支的判断函数"，但各分支语义并不完全等价；每次新增"桌面/任务栏前台 → 主动改 z-order"的代码前，都应当问清楚：任务栏前台对这条路径而言是否同样安全。
