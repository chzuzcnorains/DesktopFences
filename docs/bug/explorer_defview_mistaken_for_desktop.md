---
name: Win+E 资源管理器被误判为桌面，fence/overlay 被误 hoist 到其上方
description: 资源管理器（CabinetWClass）的文件视图子窗口类名与真桌面相同（SHELLDLL_DefView/SysListView32），IsDesktopWindow 只看自身/单层父类名 → sunk 自愈探测把"被 Explorer 合法遮挡"误判为"沉到壁纸下"→ HoistAllAboveDesktop 把 fence/overlay 抬到 Explorer 之上；改为 GetAncestor(GA_ROOT) 顶层祖先 ∈ {Progman, WorkerW} 判定
type: project
---

# Win+E 资源管理器被误判为桌面，fence/overlay 被误 hoist 到其上方（bug 45）

## 问题描述
Win+E 打开文件资源管理器，当其窗口盖住某个 fence 的中心点时，fence + overlay 没有像面对其他应用窗口那样处于资源管理器之下，而是被抬到 `HWND_TOPMOST` 浮到资源管理器窗口**之上**，且每 5 秒复现（z-order 恢复定时器）。期望：fence/overlay 永远在其他应用（含资源管理器）的层级之下。

## 产生原因
排查排除项：
- **不是 Win+D 键盘钩子**：`KeyboardHookCallback` 精确匹配 `VK_D`（bug 20 已改为 `GetAsyncKeyState` 实时查询），Win+E 不触发"显示桌面"逻辑。
- **不是前台判定**：资源管理器（顶层类 `CabinetWClass`）成为前台后，`OnForegroundChanged` → 200ms 防抖 → `SendToBottom(HWND_BOTTOM)` 一度**正确**把 fence 沉到其之下。

真因在随后的**遮挡自愈探测**：`SendToBottom` 后 50ms 的 `ScheduleSunkRecheck` 和 5 秒恢复定时器都调用 `IsAnyFenceSunkBehindDesktop()`——对每个可见 fence 中心点 `WindowFromPoint`，命中窗口若 `WindowClassUtil.IsDesktopWindow()` 为真就判定"fence 已沉到壁纸下"→ `HoistAllAboveDesktop()` 全组 `HWND_TOPMOST`。

而**资源管理器的文件视图子窗口类名恰好也是 `SHELLDLL_DefView` / `SysListView32`**（层级 `CabinetWClass → … → SHELLDLL_DefView → DirectUIHWND/SysListView32`），与真桌面（`Progman/WorkerW → SHELLDLL_DefView → SysListView32`）完全相同。旧版 `IsDesktopWindow()` 只按**自身类名 + 单层父窗口类名**匹配，从不追溯顶层祖先，无法区分两者 → 假阳性 → "被 Explorer 合法遮挡"被误判为"真沉"→ 误 hoist。

这是 bug 35（引入 `WindowFromPoint` 实测判据）/ bug 38（实测门控）的盲区延伸：当年的假设是"命中桌面类 = 真沉、命中普通 app = 合法遮挡跳过"，没有意识到**桌面窗口类名不专属于桌面**——资源管理器共用同一组 shell 视图类，`CabinetWClass` 内部历史上还含 `WorkerW` 类子窗口。

### 同族潜在 bug（同一假阳性的另外三个受害调用方）
`IsDesktopAtPoint`（内部走 `IsDesktopWindow`）的三个调用方同样把资源管理器文件视图误判为桌面：
1. **QuickHideManager**：在 Explorer 文件列表空白区双击 = "双击桌面" → 误隐藏全部 fence；
2. **PageSwitchManager**：在 Explorer 文件列表上滚轮 = "桌面滚轮" → 误触发 fence 翻页（与 Explorer 自身滚动同时发生）；
3. **DesktopMarqueeManager.CanStartAt**：在 Explorer 内拖拽 → 误画 overlay 框选选择框。

## 修复方案
判定"真桌面"只认**顶层祖先类名**：`GetAncestor(GA_ROOT)` ∈ {`Progman`, `WorkerW`}。顶层是 `CabinetWClass`（或任何其他类）→ 普通窗口合法遮挡，不 hoist。

1. **`src/DesktopFences.Shell/Interop/NativeMethods.cs`**：新增 `GetAncestor` P/Invoke 与 `GA_ROOT = 2` 常量。注意**禁用 `GA_ROOTOWNER`**（追 owner 链会把任务栏弹出菜单归到 owner 顶层）。

2. **`src/DesktopFences.Shell/Interop/WindowClassUtil.cs`** 重写 `IsDesktopWindow`：
   ```csharp
   public static bool IsDesktopWindow(IntPtr hwnd)
   {
       if (hwnd == IntPtr.Zero) return false;
       var root = NativeMethods.GetAncestor(hwnd, NativeMethods.GA_ROOT);
       if (root == IntPtr.Zero) root = hwnd; // 防御：句柄失效时退回自身
       var rootName = GetClassName(root);
       return rootName is "Progman" or "WorkerW";
   }
   ```
   - **无条件取 root，不给命中窗口自身类名留快速通过**：顶层窗口的 `GA_ROOT` 是它自己，天然覆盖直接命中 `Progman`/`WorkerW` 的情况；若对自身类名快速通过，`CabinetWClass` 内部的 `WorkerW` 类子窗口会保留一个假阳性。
   - 幻灯片壁纸下桌面 `SHELLDLL_DefView` 挂在 `WorkerW` 下，故 `Progman` 和 `WorkerW` 都认。
   - `IsDesktopAtPoint` 不改，自动继承新语义 → 同族三个潜在 bug 一并修复。

3. **`IsDesktopOrTaskbarWindow` 不动**：其全部 7 个调用点都传入前台顶层窗口（`GetForegroundWindow` / `EVENT_SYSTEM_FOREGROUND` 的 hwnd），`CabinetWClass` 本来就返回 false，无 bug；其 `#32768` 菜单检测依赖 `GetParent` 的 owner 语义（bug 1 修复的一部分），不能改成 `GetAncestor`。补 doc 注释声明"仅限前台顶层窗口，禁止喂入 `WindowFromPoint` 结果"。

4. **`DesktopEmbedManager.cs`** 仅两处注释修正（`OnForegroundChanged` 桌面分支、`IsAnyFenceSunkBehindDesktop` doc），无行为改动；不碰 `_isTopmost`、防抖、定时器结构。

## 影响范围与回归确认
- **失败方向安全**：若异常 shell 环境导致假阴性，后果是"暂时不 hoist"（5 秒定时器持续复检兜底），不会出现"浮到应用之上"这种违反核心不变量的方向。
- **bug 2/10/35（真沉自愈）**：fence 真被压到壁纸下时，中心点命中桌面 `SysListView32`/`SHELLDLL_DefView`，其 `GA_ROOT` 是 `Progman`/`WorkerW` → 判定保持 true，自愈能力不变（普通/幻灯片壁纸均成立）。
- **bug 38（点桌面不误 hoist）**：`OnForegroundChanged` 桌面分支接收前台顶层窗口，`Progman`/`WorkerW`（或 root 归一后的 DefView）语义等价，门控逻辑不变。
- **bug 1/14（托盘/任务栏路径）**：走 `IsDesktopOrTaskbarWindow`，未修改，不受影响。
- **第三方壁纸引擎**（Wallpaper Engine/Lively 把播放器 `SetParent` 到 `WorkerW` 下）：命中壁纸播放器子窗口时 root=`WorkerW` → 仍判桌面，兼容性优于旧类名匹配。
- **QuickHide/PageSwitch/Marquee**：从"Explorer 内也误触发"收紧为"仅真桌面触发"；真桌面上的双击隐藏、滚轮翻页、框选路径语义不变（命中桌面图标列表任何子窗口 root 均为 `Progman`/`WorkerW`）。
- 自动化验证：`dotnet build` 0 错误；`dotnet test tests/DesktopFences.Core.Tests` 93/93 通过（Core 层与本改动正交）。

## 经验总结
- **shell 窗口类名不专属于桌面**：`SHELLDLL_DefView`/`SysListView32` 同时是资源管理器文件视图的类名，`WorkerW` 也可能出现在 `CabinetWClass` 内部。判定 `WindowFromPoint` 命中是否"真桌面"，唯一可靠判据是 **`GetAncestor(GA_ROOT)` 顶层祖先类名 ∈ {Progman, WorkerW}**，不能看命中窗口自身或单层父类名。
- 谓词按 hwnd 来源分两类，**用途不可混**：`IsDesktopWindow`/`IsDesktopAtPoint`（GA_ROOT 顶层判定）服务于 point-hit 场景；`IsDesktopOrTaskbarWindow`（含祖先链宽匹配 + owner 语义菜单检测）仅服务于前台顶层窗口分类，喂入 point-hit 结果会复现本 bug。
- 引入"实测判据"（bug 35 的 `WindowFromPoint`）时，判据自身的分类函数也要审视其输入域：当年只考虑了"桌面 vs 普通 app 顶层窗口"，没考虑"普通 app 的**子窗口**恰好与桌面同类名"。
