# 桌面嵌入方案分析与选择

## 1. 方案对比

桌面嵌入是本项目最关键的技术决策。经调研有以下方案：

| 方案 | 原理 | Win+D 表现 | 交互性 | 复杂度 | 代表项目 |
|------|------|-----------|--------|--------|---------|
| **A: WorkerW 子窗口** | `SendMessage(Progman, 0x052C)` 触发 Explorer 创建 WorkerW，`SetParent` 将窗口挂为子窗口 | 随桌面一起显示/隐藏，正确 | **无法接收鼠标/键盘输入**（致命缺陷） | 低 | Lively Wallpaper（壁纸场景够用）|
| **B: WS_EX_TOPMOST 浮窗** | 普通 WPF 窗口 + `WS_EX_TOOLWINDOW` + `WS_EX_TOPMOST` | 始终最前，Win+D 后可见 | **完全交互** | 低 | — |
| **C: Explorer Hook (DLL注入)** | `WH_GETMESSAGE` Hook Explorer 进程，拦截 `WM_USER+83`（ShowDesktop 消息），动态切换 Topmost | Win+D 时动态置顶 | 完全交互 | 高 | Stardock Fences（推测） |
| **D: 混合方案（推荐）** | 普通 WPF 浮窗 + `WS_EX_TOOLWINDOW`（隐藏任务栏/Alt+Tab）+ 低级键盘钩子检测 Win+D + 文件系统监控桌面状态变化 | Win+D 后自动恢复显示 | 完全交互 | 中 | NoFences, DesktopFences (开源) |

## 2. 选定方案：D - 混合浮窗方案

**理由**：
- 方案 A 无法交互，对 Fences 工具是致命缺陷（需要拖放、点击、右键菜单）
- 方案 B 始终在最前面会遮挡其他窗口（用户体验差）
- 方案 C 需要注入 Explorer 进程，不稳定且维护成本高
- **方案 D** 在开源项目（NoFences, limbo666/DesktopFences）中已验证可行

**验证状态**：Demo 已通过验证（2026-03-03），具体行为正确：
- 正常状态：Fence 窗口在桌面之上、其他应用窗口之下
- Win+D 后：Fence 窗口仍然可见
- 用户切换到其他窗口后：Fence 自动回到窗口下方

---

## 3. 实现细节

### 窗口层级设计（正常态）

```
┌─────────────────────────────────┐
│  普通应用窗口（z-order 正常）      │
├─────────────────────────────────┤
│  Fence 窗口（HWND_BOTTOM）       │  ← 正常模式：在所有应用窗口最底层，但仍在桌面之上
├─────────────────────────────────┤
│  桌面图标层 (SysListView32)       │
├─────────────────────────────────┤
│  桌面壁纸 (WorkerW / Progman)    │
└─────────────────────────────────┘
```

### Z-Order 状态机

```
BOTTOM ──(Win+D 检测)──→ 延迟 300ms ──→ TOPMOST
TOPMOST ──(EVENT_SYSTEM_FOREGROUND: 用户激活其他窗口)──→ BOTTOM
TOPMOST ──(任意 SetAllBottom：Win+D restore / ExitPeek / 前台切到真实窗口，即时 SendToBottom 可能因桌面/iconic 前台 no-op)──→ 延迟 200ms 防抖重沉 ──→ BOTTOM ──→ 50ms 快速复检（IsAnyFenceSunkBehindDesktop 实测遮挡，沉了则 hoist 自愈；桌面前台且未沉时也走同一复检兜底）
（用户主动新建 Fence 且当前桌面/任务栏前台）──→ TOPMOST ──(前台切到普通窗口)──→ BOTTOM
（RegisterWindow / EnsureVisibleAboveDesktop 且当前桌面/任务栏前台）──→ TOPMOST ──(前台切到普通窗口)──→ BOTTOM
（前台变成桌面 / 5 秒定时器，且 IsAnyFenceSunkBehindDesktop 实测 fence 真被压到壁纸下）──→ TOPMOST ──(前台切到普通窗口)──→ BOTTOM
  注：未沉时即使桌面前台也**不**hoist（bug 38：否则点桌面会把 fence 抬到普通窗口之上）
```

### "让窗口可见"的统一策略

Windows 11 上当桌面（Progman/WorkerW）或任务栏（Shell_TrayWnd）是前台时，对 `WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE` 窗口调用 `SetWindowPos(HWND_TOP)` 或 `HWND_BOTTOM` 都会被 DWM 推到桌面壁纸下方。策略按**调用路径**分而非按当前 foreground 分：

- **用户主动新建路径**（`BringNewWindowToFront`）→ 统一 `SetWindowPos(HWND_TOPMOST, SWP_SHOWWINDOW)`：托盘菜单刚关闭时 foreground 处于过渡态，即便 `GetForegroundWindow()` 返回普通窗口，`HWND_BOTTOM` 仍可能被 DWM 推到壁纸下（bug 24）。这条路径无法依赖 foreground 判定，必须 topmost 兜底
- **常规可见路径**（`EnsureVisibleAboveDesktop` / `OnForegroundChanged` / 5 秒定时器）→ 按 foreground 分支：桌面/任务栏前台**或 topmost 悬浮窗前台（`HasTopmostStyle`，bug 46）** → `HWND_TOPMOST`（绕开壁纸层压制）；普通窗口前台 → `HWND_BOTTOM`（放回正常 z-order 底部）。这条路径 foreground 已稳定，分支判定可靠
- **清除 topmost**：切到任意普通窗口 → `OnDebouncedForegroundRecovery → SendToBottom(HWND_BOTTOM)`（`HWND_BOTTOM` 隐含降级 topmost，不需单独 `HWND_NOTOPMOST`）。**前台带 `WS_EX_TOPMOST`（PowerToys 命令面板等热键悬浮面板）且目标 fence 在普通带时跳过**（bug 46 双条件守卫）：这次降级冗余（topmost 前台不需要让路 + 目标无级可降），重复下发 `HWND_BOTTOM` 只剩准桌面态壁纸压制副作用；**topmost 带的 fence 照常降级**——面板可能长期占前台，一刀切跳过会让 Win+D 恢复应用后 fence 卡在应用之上

### 应用此策略的路径

1. **RegisterWindow**：所有新窗口（包括 `DesktopIconOverlay` 与 fence）启动时注册，确保立即可见
2. **BringNewWindowToFront**（新建路径，统一 topmost）：用户主动新建 Fence（托盘菜单"新建 Fence" / 规则触发创建 / 恢复最近关闭 / 重置布局 / 导入布局 / 恢复快照）
3. **EnsureVisibleAboveDesktop**（常规路径，按 foreground 分支）：启动加载的 fence、`ToggleAllFences` 等
4. **OnForegroundChanged（前台变成桌面分支）**：截图工具关闭后前台立刻回到 Progman 这类场景，**仅当 `IsAnyFenceSunkBehindDesktop()` 实测真沉时才即时拉回**，否则不动 + 50ms `ScheduleSunkRecheck` 复检（bug 38：避免点击桌面把没沉的 fence 抬到普通窗口之上）
5. **5 秒 z-order 恢复定时器**：兜底机制——开头 `IsAnyFenceSunkBehindDesktop()` 实测真沉则整组 hoist；否则桌面/任务栏前台一律不动，普通前台才 `SendToBottom`（bug 38：删除了原桌面分支的无条件 hoist）；降级循环后补 `ScheduleSunkRecheck()` 50ms 复检（bug 46：与防抖恢复对齐，压沉快速自愈）
6. **EnsureOverlayVisible**（overlay 启动专属，统一 topmost）：`DesktopIconOverlay.OnLoaded` 两拍（100/500ms）自愈，解决"fence 正常、overlay 单独被压/未绘制"——overlay 被 `WindowFromPoint` 自愈排除（bug 19），不能只依赖随 fence 带回（bug 36）

### _isTopmost 状态不变量

上述路径的 "借用 topmost" **不修改 `_isTopmost` 字段**——`_isTopmost` 仍然只由 Win+D / Peek 拥有，避免与现有 Win+D 状态机、Peek 模式、拖拽模式冲突。

### 保留不变的边界保护

- `SendToBottom` 仍然在桌面前台时 `return`：避免再次把可见窗口压下去；topmost 悬浮窗前台同样 `return`（bug 46，准桌面态）
- `OnDebouncedForegroundRecovery` 仍然在桌面前台时 `return`：被 200ms 防抖保护，桌面前台已由 `OnForegroundChanged` 即时处理
- 所有守护（`_isTopmost` / `_isPeekActive` / `_isDragging` / `_pendingTopmost`）全部保留：避免冲突

### 类名无关的判据（bug 35/46）

`GetForegroundWindow()` 返回窗口的**类名不是可靠信号**——最小化窗口、托盘菜单关闭过渡态、任务栏闪烁缩略图宿主等都会骗过 `IsDesktopOrTaskbarWindow`。继续往类名表里加窗口类只是打补丁、跨 Win11 版本易碎。改用与类名/系统版本无关的物理判据：

- **topmost 悬浮窗前台跳过冗余降级（bug 46，双条件）**：`SendToBottom` 在 `HasTopmostStyle(foreground) && !HasTopmostStyle(目标)` 时 return。热键唤出的悬浮面板（PowerToys 命令面板 = `WS_EX_TOPMOST|WS_EX_TOOLWINDOW`）抢前台时屏幕可视顶层仍是桌面（准桌面态），对本已在 `HWND_BOTTOM` 的 fence 重复下发 `HWND_BOTTOM` 会被 DWM 推到壁纸下。**两个条件缺一不可**：只判前台一刀切 → 面板长期占前台时 topmost 带 fence 永远等不到降级，Win+D 恢复应用后卡在应用之上（首版回归）；只判目标 → 拖拽 `HWND_TOP`（普通带顶、无 topmost 样式）降不回底部（bug 4 家族）。配套：`_isTopmost` 分支把面板前台视同桌面（不打断 Win+D/Peek 置顶，bug 1 原则）；5 秒定时器降级循环后补 `ScheduleSunkRecheck()`（topmost 带降级恰逢准桌面态被压沉时 50ms 自愈）。

- **最小化前台不下沉（预防）**：`OnDebouncedForegroundRecovery` / `SendToBottom` 在判定前先查 `NativeMethods.IsIconic(foreground)`，为 true 则跳过 `HWND_BOTTOM`。最小化窗口不遮挡任何东西，把 fence 沉到它"之下"必然沉到壁纸下。典型场景：任务栏闪烁图标多是最小化应用在请求关注，鼠标移过去释放前台锁后它抢到前台但仍最小化。
  - ⚠️ **`IsIconic` 判断必须延迟到 200ms 防抖之后，不能在 `OnForegroundChanged` 即时执行**：窗口在还原动画初期（Win+D 第二次恢复 / 点任务栏还原最小化窗口）仍短暂 `IsIconic`，即时 `return` 会让 fence 永不下沉、卡在恢复窗口前面（bug 35 回归）。延迟到防抖后，正在还原的窗口已退出 iconic（应下沉），单纯闪烁、保持最小化的窗口仍 iconic（不下沉），两者自然区分。
  - ⚠️ **同理，绕过防抖、直接 `SetAllBottom → SendToBottom` 的即时恢复路径也会踩同一个坑**（bug 36/37）：恢复瞬间前台仍是桌面 / 还原中的 iconic 窗口，`SendToBottom` 的 `IsDesktopOrTaskbarWindow || IsIconic` 守卫即时拦下、`HWND_BOTTOM` 从未执行，`_isTopmost` 已清但 overlay+fence 物理仍停 `HWND_TOPMOST` → 一起卡顶层。`SetAllBottom` 有 **4 条**这样的调用路径（Win+D restore 的 `_isTopmost`/`_pendingTopmost`、`ExitPeek`、`OnForegroundChanged` 真实窗口激活分支——典型：最大化窗口按 Win+D 后点任务栏另一窗口还原）。**修复：把延迟重沉收敛进 `SetAllBottom()` 自身**（循环 `SendToBottom` 后统一 `_dispatcher?.BeginInvoke(StartForegroundDebounce)`），复用含延迟 `IsIconic` 判定的 200ms 防抖通道，一次覆盖全部 4 条路径。即时尝试 + 延迟兜底，把卡顶层/不可见的修复从最长 5 秒（z-order 恢复定时器）缩短到 ~200ms。bug 37 教训：别在各调用点手工重复同一段后置防护（漏 1/4 即回归），对所有调用方都成立的兜底应收敛到方法公共出口。
- **WindowFromPoint 实测遮挡（自愈兜底）**：`IsAnyFenceSunkBehindDesktop()` 对每个可见 fence 取矩形中心点，`WindowFromPoint` 命中**顶层祖先为 Progman/WorkerW 的窗口（真桌面，`GetAncestor(GA_ROOT)` 判定）** 即判定该 fence 已被压到壁纸下，整组 `HoistAllAboveDesktop` 拉回。命中普通 app 窗口（被合法遮挡）则不动（保护 bug 14）。接入点：5 秒恢复定时器开头 + `OnDebouncedForegroundRecovery` 末尾的 50ms 一次性快速复检（`ScheduleSunkRecheck`）。
  - ⚠️ **"命中桌面"不能按命中窗口自身/单层父类名判**（bug 45）：资源管理器（CabinetWClass）的文件视图子窗口类名同为 `SHELLDLL_DefView`/`SysListView32`，按类名判会把"被 Explorer 合法遮挡"误判为"真沉"→ 误 hoist 到 Explorer 之上。只有 `GA_ROOT` 顶层祖先 ∈ {Progman, WorkerW} 可靠。
  - **overlay 不参与探测**：`DesktopIconOverlay` 是 `AllowsTransparency` 层叠窗口、空白处 alpha=0 会被 OS click-through 透传、`WindowFromPoint` 误返回桌面（bug 19）。`RegisterWindow(hwnd, isOverlay: true)` 标记后探测时跳过；它与 fence 一起下沉，靠 fence 触发整组 hoist 一并带回。
  - **overlay 启动专属自愈（bug 36）**：上一条"靠 fence 带回"的前提是 fence 也沉了。但存在"fence 正常、overlay 启动时单独被压"的情形——自愈探测跳过 overlay、fence 又没沉触发不了整组 hoist → overlay 永远捞不回（叠加层叠透明窗口初次 `Show()` 的 per-pixel alpha 合成可能不绘制）。因此 `DesktopIconOverlay.OnLoaded` 注册后安排两拍（100ms / 500ms）一次性自愈：`EnsureOverlayVisible`（「借用 topmost」拉到壁纸上方，不改 `_isTopmost`）+ `InvalidateVisual/UpdateLayout`（强制 per-pixel alpha 重组）。不调 `WindowFromPoint`、不碰 overlay 画刷/alpha，不引 bug 19。

### 窗口样式

- `WS_EX_TOOLWINDOW` — 从任务栏和 Alt+Tab 隐藏
- `WS_EX_NOACTIVATE` — 点击窗口时不激活（不抢焦点）

### Win+D 完整流程

1. `WH_KEYBOARD_LL` 低级键盘钩子检测 Win+D 组合键
2. 延迟 300ms 等待 Explorer 完成 ShowDesktop 动画
3. `SetWindowPos(HWND_TOPMOST)` 将所有 Fence 窗口临时置顶
4. `SetWinEventHook(EVENT_SYSTEM_FOREGROUND)` 持续监听前台窗口变化
5. 用户激活任何非 Fence 窗口 → `SetWindowPos(HWND_BOTTOM)` 恢复

### Peek 模式（Win+Space）

1. 全局热键捕获（RegisterHotKey）
2. 所有 Fence 窗口设为 HWND_TOPMOST + 提高透明度/动画
3. 再次按下或 Escape 退出 Peek，恢复 HWND_BOTTOM

---

## 4. 关键 Win32 API

| API | 用途 |
|-----|------|
| `SetWindowLongPtr(GWL_EXSTYLE, WS_EX_TOOLWINDOW \| WS_EX_NOACTIVATE)` | 隐藏于任务栏/Alt+Tab 且不抢焦点 |
| `SetWindowPos(HWND_BOTTOM)` | 正常态：桌面之上、窗口之下 |
| `SetWindowPos(HWND_TOPMOST)` | Win+D 后临时置顶 |
| `SetWindowsHookEx(WH_KEYBOARD_LL)` | 全局键盘钩子检测 Win+D |
| `SetWinEventHook(EVENT_SYSTEM_FOREGROUND)` | 监听前台窗口变化，自动恢复 HWND_BOTTOM |
| `GetAncestor(GA_ROOT)` | 真桌面判定：`WindowFromPoint` 命中窗口的顶层祖先 ∈ {Progman, WorkerW} 才算桌面（bug 45，禁用 GA_ROOTOWNER） |
| `RegisterHotKey` | 注册 Peek 热键 |
| `SHGetFileInfo` / `IExtractIcon` | 提取文件图标 |
| `SHChangeNotifyRegister` | Shell 变更通知（桌面文件增删） |
| `SetWindowCompositionAttribute(WCA_ACCENT_POLICY)` | DWM Acrylic 背景模糊（Phase 11，详见 [acrylic-blur.md](acrylic-blur.md)） |

---

## 5. 实现复用（重构后）

钩子/热键/桌面识别的样板代码集中到 `DesktopFences.Shell/Interop/`：

| 工具 | 作用 |
|------|------|
| `LowLevelKeyboardHook` | DesktopEmbedManager 通过它安装 `WH_KEYBOARD_LL`，检测 Win+D / Escape |
| `LowLevelMouseHook` | QuickHideManager（双击桌面）、PageSwitchManager（滚轮切页）共享 `WH_MOUSE_LL` 包装 |
| `HotkeyHost` | PeekManager / SearchHotkeyManager / PageSwitchManager 共享一个隐藏窗口 + `WM_HOTKEY` 分发器 |
| `WindowClassUtil` | 集中桌面/任务栏类名与"是否桌面"判定，两个谓词输入域不可混用（bug 45）：`IsDesktopWindow / IsDesktopAtPoint` = `GetAncestor(GA_ROOT)` 顶层祖先 ∈ {Progman, WorkerW}，服务于 point-hit 场景（sunk 自愈探测、QuickHide、PageSwitch、Marquee）；`IsDesktopOrTaskbarWindow` = 类名 + 祖先链宽匹配 + `#32768` owner 语义菜单检测，**仅限前台顶层窗口分类**（GetForegroundWindow / EVENT_SYSTEM_FOREGROUND），禁止喂入 `WindowFromPoint` 结果；`HasTopmostStyle` = `GWL_EXSTYLE` 含 `WS_EX_TOPMOST`，识别热键悬浮面板前台（bug 46） |

新增类似行为（例如另一个全局热键、新的桌面消息钩子）应直接复用上述工具，避免再次粘贴 30 行 `SetWindowsHookEx + UnhookWindowsHookEx` / `HwndSource + RegisterHotKey + UnregisterHotKey` 的样板。

---

## 6. 参考资料

- [Win+D 窗口存活方案讨论](https://learn.microsoft.com/en-us/answers/questions/2127546/)
- [Draw Behind Desktop Icons (CodeProject)](https://www.codeproject.com/Articles/856020/Draw-Behind-Desktop-Icons-in-Windows-plus)
- [Lively Wallpaper (WorkerW 实现参考)](https://github.com/rocksdanister/lively)
