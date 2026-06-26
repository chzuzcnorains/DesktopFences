---
name: Win+D 恢复后 overlay+fence 卡顶层 / 启动时 overlay 不显示（bug 35 回归 + 补全）
description: bug 35 给 SendToBottom 加的 IsIconic 守卫被 Win+D 恢复这条「即时」路径误触发 → SetWindowPos(HWND_BOTTOM) 从未执行 → topmost 未清除；overlay 因被自愈探测排除，启动单独沉下时无人捞回
type: project
---

# Win+D 恢复后 overlay+fence 卡顶层 + 启动时 overlay 不显示（bug 36）

## 问题描述

1. **Win+D（现象 A）**：按两次 Win+D（显示桌面 → 恢复窗口）后，未归档图标层（`DesktopIconOverlay`）和所有 Fence 面板一起**浮在其他窗口最上层**，没有正常沉回桌面层。必须再切一次窗口（触发一次前台变化）才会沉底。
2. **启动（现象 B）**：冷启动时**只有** `DesktopIconOverlay`（未收纳图标）不显示，Fence 面板正常；必须切换一次前台窗口才出现。

两者都与 bug 35《鼠标移到任务栏闪烁图标时 Fence/未归档图标消失》的修复有关——是它引入的 `IsIconic` 守卫与「overlay 被自愈探测排除」两处设计的边界遗漏。

## 产生原因

### 现象 A：Win+D 恢复是「即时」路径，被 IsIconic 守卫拦下

`OnShowDesktopDetected` 的 `_isTopmost` 分支（第二次 Win+D = 恢复窗口）调用 `SetAllBottom()` → `foreach (_managedWindows) SendToBottom(hwnd)`（overlay 也在 `_managedWindows` 内，故 overlay 与 fence 同时受影响）。

bug 35 给 `SendToBottom` 加了守卫：

```csharp
if (WindowClassUtil.IsDesktopOrTaskbarWindow(foreground) || NativeMethods.IsIconic(foreground))
    return;  // 跳过 HWND_BOTTOM
```

Win+D 恢复**瞬间**，`GetForegroundWindow()` 往往仍是 `Progman`（桌面尚未切走），或正处于还原动画初期、仍 `IsIconic=true` 的那个被恢复窗口 → 守卫命中 → 对每个窗口的 `SendToBottom` 都**即时 no-op** → `SetWindowPos(HWND_BOTTOM)` 从未执行。

而 `SetAllBottom()` 首行已把 `_isTopmost = false`，于是状态机认为「已经在底部」，但 overlay 与 fence 物理上仍停在上次 `SetAllTopmost()` 留下的 `HWND_TOPMOST` 层 → **一起卡在最上层**。直到用户切到另一个普通窗口，`OnForegroundChanged`（此时 `_isTopmost` 已 false）→ 防抖 → `OnDebouncedForegroundRecovery` → `SendToBottom`（前台已是普通非 iconic 窗口）才把它们沉下去——这就是「兜底规则可以消除展示」但有明显卡顶层闪烁的来源。

> 这正是 bug 35 文档第 30 行警告过的回归模式：「`IsIconic` 判断必须延迟到 200ms 防抖之后，绝不能在即时路径执行」。但 bug 35 只把延迟判定用在 `OnDebouncedForegroundRecovery`，漏了 **Win+D 恢复 / ExitPeek 这两条会经过带 `IsIconic` 的 `SendToBottom` 的即时路径**。

### 现象 B：overlay 被自愈探测排除，单独沉下无人捞回

`DesktopIconOverlay` 注册时标 `isOverlay: true`，被 `IsAnyFenceSunkBehindDesktop()` 的 `WindowFromPoint` 自愈探测**排除**（它是 `AllowsTransparency` 层叠窗口，空白处 alpha=0 会透传误判为沉底，bug 19）。bug 35 文档第 39 行明确写道：「零 fence 仅 overlay 的极端场景由 (a) 的 `IsIconic` 预防覆盖」。

但实际还存在「fence 正常、唯独 overlay 单独被压到壁纸下」的情形：此时
- 自愈 `IsAnyFenceSunkBehindDesktop()` 只探测 fence、跳过 overlay → 探测不到 overlay 沉了；
- fence 没沉 → 不会触发整组 `HoistAllAboveDesktop` 把 overlay 一并带回；

→ overlay 永远捞不回，只有切到普通窗口的 `SendToBottom(overlay) → HWND_BOTTOM`（普通前台时安全，回到壁纸上方）才让它重现。叠加 overlay 是层叠透明窗口、初次 `Show()` 的 per-pixel alpha 合成可能不绘制（直到一次前台变化触发 DWM 重组），两种机制都会表现为「启动只有 overlay 不显示、切窗口才出现」，且无法在不实跑的情况下区分。

## 修复方案

### 现象 A：显式恢复路径补一次「防抖后延迟重沉」

不动 `SendToBottom` 的 `IsIconic` 守卫（bug 35 的 5 秒定时器 / 防抖路径仍依赖它），改为给 Win+D 恢复 / Peek 退出这类**显式用户意图**的恢复路径，在即时 `SetAllBottom()` 之外**复用已有的 200ms 防抖通道** `StartForegroundDebounce()`（→ `OnDebouncedForegroundRecovery`，其中已含正确的延迟 `IsIconic`/桌面/任务栏判定 + 末尾 `ScheduleSunkRecheck` 50ms 二次兜底）安排一次延迟重沉：等被还原窗口退出 iconic、真正盖住桌面后，`HWND_BOTTOM` 安全执行 → overlay + fence 一起正确沉底。

这就实现了用户要的「对**闪烁图标**判读与**正常态**判读做时间差异化」：同一个 `IsIconic` 信号，延迟 ~200ms 后再判——正在还原的窗口届时已退出 iconic（应沉），单纯闪烁、保持最小化的窗口仍 iconic（不沉）。

`DesktopEmbedManager.cs` 三处（均只「追加一次 `StartForegroundDebounce()`」，无新字段、无新 timer、不碰 `_isTopmost`、不改任何守卫）：
- `OnShowDesktopDetected` 的 `_isTopmost` 分支：`SetAllBottom();` 后 `StartForegroundDebounce();`
- `OnShowDesktopDetected` 的 `_pendingTopmost` 分支：同上
- `ExitPeek`：`SetAllBottom();` 后 `_dispatcher?.BeginInvoke(StartForegroundDebounce);`（public 方法，`BeginInvoke` 防御非 UI 线程调用）

### 现象 B：overlay 启动双机制自愈

- `DesktopEmbedManager.cs` 新增 `public void EnsureOverlayVisible(IntPtr hwnd)`：`hwnd==Zero` / 不可见保护后 `HoistSingleAboveDesktop(hwnd)`（「借用 topmost」拉到壁纸上方，**不改 `_isTopmost`**，后续切普通窗口由 `SendToBottom(HWND_BOTTOM)` 隐式降级，不会让 overlay 常驻浮在普通应用上方）。
- `DesktopIconOverlay.OnLoaded` 注册后，调 `ScheduleOverlaySelfHeal(hwnd, 100)` + `ScheduleOverlaySelfHeal(hwnd, 500)`（一次性 `DispatcherTimer`，两拍覆盖常见与启动繁忙场景）。每拍 Tick：
  - `_embedManager.EnsureOverlayVisible(hwnd)` —— 治机制1（z-order 下沉）
  - `InvalidateVisual() + IconCanvas.InvalidateVisual() + UpdateLayout()` —— 治机制2（层叠透明初次合成不绘制）

同时覆盖两种机制（无法在不实跑的情况下区分），且：不碰任何 `Background`/alpha 画刷（click-through、bug 19 保持）；完全不调 `WindowFromPoint`/`IsAnyFenceSunkBehindDesktop`（不引 bug 19）；纯一次性、最迟 500ms 跑完；timer 即使在 overlay 关闭后 fire 也安全。

## 核心代码修改
- `src/DesktopFences.Shell/Desktop/DesktopEmbedManager.cs`：`OnShowDesktopDetected` 两恢复分支 + `ExitPeek` 各加一次 `StartForegroundDebounce()`；新增 `public void EnsureOverlayVisible(IntPtr hwnd)`。
- `src/DesktopFences.UI/Controls/DesktopIconOverlay.xaml.cs`：`OnLoaded` 注册后加两拍 `ScheduleOverlaySelfHeal`；新增私有 `ScheduleOverlaySelfHeal(IntPtr, int)`。

## 影响范围与回归确认
- **bug 35**（任务栏闪烁图标）：`SendToBottom` 的 `IsIconic` 守卫与 `IsAnyFenceSunkBehindDesktop` 自愈均不变；延迟重沉的 `OnDebouncedForegroundRecovery` 在前台仍是 iconic 闪烁窗口时照样被 `IsIconic` 守卫拦下 → 不下沉。预防保持。
- **bug 14**（其他程序最大化右键托盘）：自愈只在 `WindowFromPoint` 命中桌面类时 hoist；overlay 两拍自愈最迟 500ms 且仅启动一次，之后随 `SendToBottom` 沉底。不回归。
- **bug 24**（托盘新建 Fence 立即可见）：`BringNewWindowToFront` 不变。
- **bug 19**（overlay click-through）：未碰 overlay 画刷/alpha，未对 overlay 做 `WindowFromPoint` 探测。
- **bug 10**（启动 overlay / 截图恢复）：原桌面前台分支整组 hoist 含 overlay 的逻辑不变，本次是对「fence 正常、overlay 单独沉」这一未覆盖情形的补全。
- **Win+D 第一次（显示桌面）**：落 `_showDesktopTimer` 300ms 分支，未触改动，行为不变。
- **拖拽**：延迟重沉的 Tick 带 `_isDragging` 守卫，不介入。
- **Peek**：进入 `SetAllTopmost` 不变；退出补延迟重沉，修偶发卡顶层。

## 经验总结
- bug 35 的延迟 `IsIconic` 判定只挂在了 `OnDebouncedForegroundRecovery`，但 `SendToBottom` 本身也带了 `IsIconic` 守卫——任何**绕过防抖、直接调 `SetAllBottom → SendToBottom` 的即时路径**（Win+D restore / ExitPeek）都会重新踩中「即时判 IsIconic」的坑。改 z-order 守卫时要把「所有会经过它的即时调用方」一起过一遍。
- 「借用 topmost」基元（`HoistSingleAboveDesktop`，不改 `_isTopmost`）是这类「让某个被压窗口立即可见」需求的统一答案；overlay 因被自愈探测排除，需要它自己独立的一次性自愈入口（`EnsureOverlayVisible`），不能只依赖「随 fence 一起被带回」。

---

## 补充（bug 37）：第 4 条 `SetAllBottom` 路径 + 把延迟重沉收敛进 `SetAllBottom`

### 现象
最大化窗口 A 按 Win+D（程序逻辑正常）→ 点击任务栏上的另一个窗口 B 还原 → overlay+fence 一起卡在最顶层，要等 5 秒兜底定时器才被沉下去隐藏。期望：窗口 B 直接在最顶层，不靠兜底补救。

### 根因
现象 A 的修复（上文）只在 **3 条** `SetAllBottom` 调用路径手工补了 `StartForegroundDebounce()`，但 `SetAllBottom` 在 `DesktopEmbedManager.cs` 共有 **4 条**路径，漏了第 4 条：
`OnForegroundChanged` 的 `_isTopmost` 分支——Win+D 置顶状态下用户激活一个真实应用窗口（如点击 B 还原）时调 `SetAllBottom()`，后面没有延迟重沉。B 还原动画初期仍 `IsIconic` → 即时 `SendToBottom` 被守卫拦下 no-op → overlay+fence 卡在 `HWND_TOPMOST`，只能等 5 秒 z-order 恢复定时器（前台已是非 iconic 的 B）才沉下去。

### 修复
不再「逐调用点手工补」，改为把延迟重沉**收敛进 `SetAllBottom()` 自身**：循环 `SendToBottom` 后统一 `_dispatcher?.BeginInvoke(StartForegroundDebounce);`，并删除 3 处冗余显式调用。这样全部 4 条路径（含 `OnForegroundChanged` 真实窗口激活）一次覆盖，第 4 条 bug 随之修复，未来也不会再漏。

- `SetAllBottom` 仅 4 处私有调用、全部是「离开 topmost、要回到底部」的转换，都需要延迟重沉兜底 → 收敛无副作用。
- 4 个调用点都在 UI 线程；`BeginInvoke` 额外防御并保证 `DispatcherTimer` 在 UI 线程启动。
- `EnterPeek → SetAllTopmost` 不受影响（不触 `SetAllBottom`）。

### 教训
**手工在每个调用点重复同一段防护，迟早漏一条**（这次正是漏 1/4 才有的回归报告）。当一段「后置兜底」逻辑对某方法的所有调用方都成立时，应收敛到该方法的公共出口，而非散落在各调用点。

