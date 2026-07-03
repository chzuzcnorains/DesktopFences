---
name: PowerToys 命令面板等 topmost 悬浮窗前台后 fence/overlay 被压到壁纸下消失
description: 热键唤出的 WS_EX_TOPMOST 悬浮面板抢到前台时，屏幕可视顶层仍是桌面（准桌面态），防抖恢复对本已停在 HWND_BOTTOM 的 fence 重复下发 HWND_BOTTOM 被 DWM 推到壁纸下 → 消失，5 秒定时器反复重沉与 sunk 自愈形成振荡；修复：SendToBottom / EnsureVisibleAboveDesktop 把"前台带 WS_EX_TOPMOST"视同桌面前台，不动 z-order
type: project
---

# PowerToys 命令面板等 topmost 悬浮窗前台后 fence/overlay 被压到壁纸下消失（bug 46）

## 问题描述
在桌面上按热键唤出 PowerToys 命令面板（Command Palette，`Microsoft.CmdPal.UI`），约两秒后所有 fence 和未归档 overlay 图标消失。期望：fence/overlay 正常展示（在命令面板之下、壁纸之上）。

## 产生原因
实测命令面板主窗口扩展样式为 `0x188` = **`WS_EX_TOPMOST | WS_EX_TOOLWINDOW`**——一个会抢前台（非 NOACTIVATE）的 topmost 悬浮工具窗。事件链：

1. 热键唤出面板 → `EVENT_SYSTEM_FOREGROUND`，前台切到面板窗口。
2. `OnForegroundChanged`：非桌面/任务栏 → 200ms 防抖 → `OnDebouncedForegroundRecovery` → 对全部 managed 窗口 `SendToBottom`。
3. `SendToBottom` 旧守卫只挡"桌面/任务栏前台"和"最小化前台"——面板两者都不是 → 对**本已停在 `HWND_BOTTOM`** 的 fence/overlay 重复下发 `HWND_BOTTOM`。
4. 此刻屏幕上除中央小面板外全是桌面（**准桌面态**，bug 24 同族：`GetForegroundWindow()` 返回"普通窗口"但 DWM 内部按准桌面处理）→ `HWND_BOTTOM` 被推到壁纸下 → 消失。
5. 50ms `ScheduleSunkRecheck` 实测真沉 → `HoistAllAboveDesktop` 拉回（借用 topmost）。但 5 秒定时器随后又在"面板前台（普通窗口）"分支重沉 → 再次沉底 → 下个 tick 又 hoist——**5 秒周期振荡**，用户看到的就是"约两秒后消失"（定时器相位随机，平均 ~2.5s）。

核心矛盾：对 topmost 悬浮窗前台执行降级**既危险又无意义**——topmost 前台永远在非 topmost fence 之上（不需要降级让路）；而重复下发 `HWND_BOTTOM` 是 fence 被压到壁纸下的唯一诱因。

## 修复方案
把"**前台窗口带 `WS_EX_TOPMOST`**"归入与"桌面/任务栏前台"同类的"不动 z-order"状态：

1. **`NativeMethods.cs`**：新增 `WS_EX_TOPMOST = 0x00000008` 常量。
2. **`WindowClassUtil.cs`**：新增 `HasTopmostStyle(hwnd)`（`GetWindowLongPtr(GWL_EXSTYLE)` 按位判断）。
3. **`DesktopEmbedManager.SendToBottom`**（全部降级路径的公共出口——防抖恢复、5 秒定时器、`SetAllBottom` 循环、拖拽结束 `RestoreWindowToBottom` 都经过这里，bug 37 收敛原则）：守卫新增 `HasTopmostStyle(foreground) && !HasTopmostStyle(hwnd)` → return——**仅跳过对普通带目标的冗余降级**。
4. **`EnsureVisibleAboveDesktop`**（启动加载 / `ToggleAllFences`）：分支条件同步扩展——topmost 悬浮窗前台走 `HWND_TOPMOST` 分支（保证"立即可见"，不走有壁纸压制风险的 `HWND_BOTTOM` 分支）。
5. **`OnForegroundChanged` 的 `_isTopmost` 分支**：topmost 悬浮窗抢前台不打断 Win+D / Peek 置顶（与桌面/任务栏同待遇，bug 1 同原则）——面板浮在被展示的桌面之上属于"桌面相关交互"，真正的普通窗口激活才退出置顶。
6. **5 秒定时器降级分支**：循环后补 `ScheduleSunkRecheck()`（与防抖恢复对齐）——对 topmost 带 fence 的降级若恰逢准桌面态被压沉，50ms 内自愈拉回，不用等下个 5 秒 tick。

修复后事件链：面板唤出 → 防抖恢复 → `SendToBottom` 守卫识别"topmost 前台 + 目标在普通带" → 不动 → fence/overlay 稳稳停在 `HWND_BOTTOM`、可见、位于面板之下。无沉底、无 hoist、无振荡。

### 守卫为什么必须是"双条件"（首版一刀切的回归教训）
- **首版守卫**只判 `HasTopmostStyle(foreground)` 一刀切跳过——实测回归：**面板开着时 Win+D 恢复应用，处于 topmost 带的 fence/overlay 永远等不到降级**（面板长期占住前台，"推迟到普通窗口前台"永远不来），卡在恢复的应用之上。fence 在 topmost 带时降级是必须的，且此时屏幕已有普通窗口遮挡桌面（非准桌面态），`HWND_BOTTOM` 稳定。
- **反向单条件**（只判目标 `!HasTopmostStyle(hwnd)` 跳过）也不可行：拖拽路径 `BringWindowAboveSiblings` 用 `HWND_TOP`（普通带顶部、无 topmost 样式），拖拽结束必须能降回底部（bug 4 家族回归）。
- 正确语义：**"这次降级是否冗余"= 前台 topmost（不需要让路）且目标已在普通带（无级可降）**，两个条件缺一不可。

## 影响范围与回归确认
- **面板开着时 Win+D → 再 Win+D 恢复应用**（首版回归场景）：键盘钩子 toggle → `SetAllBottom` → fence 带 topmost 样式 → 降级照常执行 → 沉到恢复的应用之下 ✓；若瞬时被压沉，防抖恢复与定时器的 `ScheduleSunkRecheck` 50ms 自愈。
- **Win+D 置顶状态下唤出面板**：`_isTopmost` 分支识别 topmost 悬浮窗 → 不打断置顶（与右键托盘菜单同待遇，bug 1 原则）；从面板启动应用（普通窗口激活）→ 正常退出置顶。
- **借用 topmost + 面板前台**（fence 恰处 hoist 状态时唤出面板）：fence 带 topmost 样式 → 降级照常执行；刚激活的面板本就在 topmost 带顶，视觉正确。
- **Win+D / Peek 退出**：`SetAllBottom → SendToBottom`，前台是用户点击的普通窗口（非 topmost）→ 照常降级 ✓。
- **拖拽结束**（`RestoreWindowToBottom`）：普通窗口前台照常降级；面板前台时推迟——与既有"桌面前台时推迟"语义一致（注释原文：defer to the recovery timer or the next foreground change）。
- **bug 35（最小化前台）/ bug 14（任务栏前台）**：`IsIconic` / `IsDesktopOrTaskbarWindow` 守卫在前，互不影响。`Shell_TrayWnd` 本身带 topmost，但已被 `IsDesktopOrTaskbarWindow` 先行拦截，行为不变。
- **全屏 topmost 应用（游戏/放映）前台**：跳过降级——fence 非 topmost 本就在其下；借用 topmost 的 fence 也在激活的全屏窗口之下。退出后正常降级。
- **sunk 自愈保持**：万一 fence 已被其他路径压到壁纸下，5 秒定时器开头与 `ScheduleSunkRecheck` 的 `IsAnyFenceSunkBehindDesktop` 探测不受本修改影响，照常拉回。
- 自动化验证：`dotnet build` 0 错误；`dotnet test tests/DesktopFences.Core.Tests` 93/93 通过。

## 经验总结
- **"前台是普通窗口"≠"HWND_BOTTOM 安全"**。安全的充分条件是"前台是一个真正遮挡桌面的普通带窗口"。已知的三个反例：最小化窗口抢前台（bug 35，`IsIconic` 判）、托盘菜单关闭过渡态（bug 24，路径级 topmost 兜底）、**topmost 悬浮面板前台（本 bug，`WS_EX_TOPMOST` 样式判）**——三者共性是桌面仍是可视顶层（准桌面态）。
- **跳过"无意义但有副作用"的操作时，"无意义"的判定必须完整**：首版只看前台（"topmost 前台不需要让路"）就跳过，漏了"目标在 topmost 带时降级另有目的（退出置顶）"——跳过条件宽一分，就把必要动作也吞掉了（本 bug 的回归）。跳过冗余操作的守卫要同时刻画"为什么不需要做"（前台 topmost）和"做了也没变化"（目标已在普通带）。
- 守卫加在 `SendToBottom` 公共出口（bug 37 收敛原则），防抖恢复 / 5 秒定时器 / `SetAllBottom` / 拖拽结束四条路径一次全覆盖。
- **验证 z-order 修改必须覆盖 Win+D / Peek 的完整进出循环**：z-order 状态机的每个新守卫都可能挡住"退出置顶"这条腿——本次回归正是手工回归清单里的 Win+D 场景暴露的。
- 排查这类 bug 时先用 `EnumWindows + GetWindowLongPtr(GWL_EXSTYLE)` 实测目标应用窗口的扩展样式（本次即由此确认命令面板是 `WS_EX_TOPMOST|WS_EX_TOOLWINDOW`），不要凭窗口外观猜测。
