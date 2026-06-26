---
name: 点击桌面导致 fences/overlay 浮到普通窗口之上
description: 桌面成为前台时无条件 HoistAllAboveDesktop（bug 10 旧逻辑）把停在 HWND_BOTTOM 的正常 fence 抬到 HWND_TOPMOST，浮到屏幕上的普通窗口之上；改为仅当 IsAnyFenceSunkBehindDesktop 实测真沉时才 hoist
type: project
---

# 点击桌面导致 fences/overlay 浮到普通窗口之上（bug 38）

## 问题描述
非最大化窗口 A 在最顶层（前台），点击一下桌面空白处 → 所有 fence（及未归档 overlay）跳到窗口 A **之上**。期望：fence 和 overlay **永远在其他应用的层级之下**（稳态 `HWND_BOTTOM`：桌面之上、所有应用之下）。

## 产生原因
点击桌面让 `Progman`/`WorkerW`/`SHELLDLL_DefView`/`SysListView32` 成为前台 → `OnForegroundChanged` 非 topmost 分支 → `IsDesktopWindow(hwnd)` 为真 → **无条件** `HoistAllAboveDesktop()`（借用 `HWND_TOPMOST`）→ fence/overlay 从 `HWND_BOTTOM` 被抬到 `HWND_TOPMOST`，浮到窗口 A 之上。

这个「桌面前台就无条件 hoist」是 **bug 10/2（截图工具关闭后 fence 被压到壁纸下、需切前台才恢复）** 修复时加的：当时桌面成为前台后，fence 可能已被 DWM 压到壁纸下，于是主动用 `HWND_TOPMOST` 拉回。但那时（2026-04-29）**还没有可靠的「fence 是否真的被压到壁纸下」判据**，只能用「前台类名是不是桌面」来近似——于是把「真沉下去了需要拉回」和「只是用户点了下桌面、fence 其实好好停在 `HWND_BOTTOM`」混为一谈，后者被错误地 hoist 到普通窗口之上。

`bug 35` 引入了类名无关的实测判据 `IsAnyFenceSunkBehindDesktop()`（对每个可见 fence 取中心点 `WindowFromPoint` 看是否被桌面类窗口遮挡），此后就能精确区分两者了。

同样的无条件 hoist 还存在于 **5 秒 z-order 恢复定时器**的「桌面前台分支」——它在开头已先用 `IsAnyFenceSunkBehindDesktop()` 判过、不沉就往下走，结果下面的桌面分支又无条件 hoist，等于「桌面前台时每 5 秒把没沉的 fence 抬一次」。

## 修复方案
用 `IsAnyFenceSunkBehindDesktop()` **门控**桌面前台的 hoist：只有实测确实被压到壁纸下才借用 topmost 拉回，否则不动。`src/DesktopFences.Shell/Desktop/DesktopEmbedManager.cs` 两处：

1. **`OnForegroundChanged` 桌面前台分支**：
   ```csharp
   if (WindowClassUtil.IsDesktopWindow(hwnd))
   {
       if (IsAnyFenceSunkBehindDesktop())
           HoistAllAboveDesktop();   // 真沉 → 借用 topmost 拉回（bug 10/2）
       else
           ScheduleSunkRecheck();    // 未沉 → 不动；50ms 复检兜住"下沉略晚于前台事件"的时序
       return;
   }
   ```
   `ScheduleSunkRecheck()` 内部同样只在 `IsAnyFenceSunkBehindDesktop()` 为真时才 hoist，所以「只是点桌面、没沉」的常见情形不会有任何 hoist，fence 稳稳停在 `HWND_BOTTOM`、在 A 之下。

2. **5 秒 z-order 恢复定时器**：删除桌面前台分支的无条件 `HoistAllAboveDesktop()`，把「桌面前台」与「任务栏前台」合并为「不动」（真沉的情况已由定时器开头的 `IsAnyFenceSunkBehindDesktop()` 自愈处理）。

不新增字段、不改 `_isTopmost` 状态机、不改任何 z-order 守卫、不动 `IsAnyFenceSunkBehindDesktop`/`SendToBottom`/`HoistAllAboveDesktop` 本身。

## 影响范围与回归确认
- **bug 10/2（截图工具关闭后 fence/overlay 被压到壁纸下）**：截图工具关闭、foreground 回到 Progman 的瞬间 fence 已被压到壁纸下 → `IsAnyFenceSunkBehindDesktop()` 实测为真 → 照常 `HoistAllAboveDesktop()` 拉回；若下沉略晚于前台事件，50ms `ScheduleSunkRecheck` 兜底，5 秒定时器开头的 sunk 自愈再兜一层。恢复能力保持。
- **bug 14（其他程序最大化时右键托盘小图标，fence 不该浮到其上）**：进一步加固——桌面/任务栏前台且未沉时一律不 hoist。
- **bug 35（任务栏闪烁图标 / WindowFromPoint 自愈）**：`IsAnyFenceSunkBehindDesktop()` / `SendToBottom` 的 `IsIconic` 守卫均不变；自愈接入点不变。
- **bug 36/37（SetAllBottom 延迟重沉）**：未触及。
- **overlay**：`HoistAllAboveDesktop` 仍整组（含 overlay）拉回；overlay 因被 sunk 探测排除（bug 19），其单独可见性由启动两拍自愈（bug 36）保证，与本次正交。

## 经验总结
- 「前台类名是不是桌面」**不能**等价于「fence 需要被拉回」。fence 该不该 hoist 的唯一可靠判据是 `IsAnyFenceSunkBehindDesktop()`（实测是否被壁纸压住），而不是前台窗口类名。
- bug 10 时代用「桌面前台 → 无条件 hoist」是受限于当时没有实测判据的权宜；bug 35 引入实测判据后，所有「桌面前台就拉回」的旧分支都应回头改成门控，否则就会在「fence 没沉、屏幕上有普通窗口」时把 fence 抬到应用之上。
