# bug #23：托盘图标右键菜单与暗色 UI 不一致

## 问题描述

任务栏右下角通知区 DesktopFences 托盘图标右键弹出的菜单（新建 Fence、显示/隐藏全部、布局快照…），仍然是 Windows 经典的灰白原生 `ContextMenuStrip`，与 Fence 面板、设置窗口、Fence 右键菜单的暗色玻璃风格（DarkTheme.xaml）严重违和。

## 复现步骤

1. 启动 DesktopFences。
2. 在任务栏通知区右键 DesktopFences 图标。
3. 弹出菜单为白底黑字、灰色边框、原生 Windows 控件外观。

## 真因分析

托盘菜单使用的是 `System.Windows.Forms.NotifyIcon` 与 `ContextMenuStrip`：

```csharp
// src/DesktopFences.App/App.xaml.cs:234
_trayIcon = new NotifyIcon
{
    ContextMenuStrip = BuildTrayMenu()
};
```

WinForms `ContextMenuStrip` 默认走 `ToolStripProfessionalRenderer`，渲染逻辑由 `ProfessionalColorTable` 控制，这是 Windows 经典蓝白配色。WPF 的 `DarkContextMenuStyle`/`DarkMenuItemStyle`（定义在 `src/DesktopFences.UI/Themes/DarkTheme.xaml`）只能作用于 WPF `ContextMenu`，对 WinForms 控件没有效果。

NotifyIcon 是 Win32 Shell 通知区图标，其右键弹出菜单只能是 HMENU 或 `ToolStripDropDown`，无法直接替换为 WPF `ContextMenu`（或者要付出额外的窗口管理 + 焦点处理代价），因此最稳妥的方案是给 WinForms 菜单提供一个暗色 Renderer。

## 修复方案

### 第一轮（颜色对齐）

新增 [DarkTrayMenuRenderer.cs](../../src/DesktopFences.App/DarkTrayMenuRenderer.cs)：

- `DarkColorTable`（继承 `ProfessionalColorTable`）覆盖以下颜色，统一使用 DarkTheme 同源色板：
  - 菜单背景 `Background = #1C2030`（与 `FenceBaseColor #1A2036` 同源）
  - hover 高亮 `HoverBackground = #3A4E6E`（DarkTheme 的 `#33FFFFFF` over base 的不透明等价值）
  - 边框 / 分割线 `#3A3F52`
  - 前景 / 禁用前景 `#E8ECF4` / `#6A7286`（对齐 `TextPrimaryBrush` / `TextFaintBrush`）
- `DarkRenderer`（继承 `ToolStripProfessionalRenderer`）重写：
  - `OnRenderItemText` / `OnRenderArrow` — 强制白色前景，禁用项灰色
  - `OnRenderSeparator` — 用 `#3A3F52` 1px 横线
  - `OnRenderToolStripBackground` / `OnRenderImageMargin` / `OnRenderToolStripBorder` — 全部铺暗色，避开默认渐变
  - `RoundedEdges = false` — 与 WPF 的 `CornerRadius=8` 不冲突；GDI+ 无 alpha，强行画圆角会出现锯齿
- `Apply(menu)` / `ApplyToItems(items)` — 一次性给整棵根菜单及其每一层子菜单刷 `BackColor`/`ForeColor`/`ShowImageMargin=false`。

调用点 [App.xaml.cs](../../src/DesktopFences.App/App.xaml.cs)：

1. `BuildTrayMenu()` 末尾 `DarkTrayMenuRenderer.Apply(menu);`
2. `RefreshRecentClosedMenu()` 末尾 `DarkTrayMenuRenderer.ApplyToItems(menu.DropDownItems);`
3. `RefreshSnapshotMenu()` 末尾 `DarkTrayMenuRenderer.ApplyToItems(menu.DropDownItems);`

### 第二轮（圆角 + 字号）

颜色对齐后，菜单仍是方角 + 偏小字号 (9pt)，与 fence panel 圆角 + WPF 菜单 12.5px 字号视觉脱节。补充：

- **圆角 8px**：与 [DarkContextMenuStyle](../../src/DesktopFences.UI/Themes/DarkTheme.xaml) 的 `CornerRadius="8"` 对齐。沿用 fence panel `AcrylicCompositor` 同方案 — 给 popup HWND 调用 `SetWindowRgn(CreateRoundRectRgn(...))` 做硬裁。
  - 新增 `AttachRoundCorner(ToolStripDropDown)` helper，在 `Opened` 事件里设 region。**不能用 `HandleCreated`**：handle 创建早于 layout，此时 `Width`/`Height` 仍为 0。
  - 用 `dropDown.Tag = true` 标志位防重，避免 reapply 时 attach 多份 handler。
  - 同时重写 `OnRenderToolStripBorder` 为空 — 否则 GDI+ 会在被裁掉的圆角区域外画方框 1px 边，四角会出现「裁掉一点的方边」。
- **字号 9pt → 10.5pt**：单独在 `Apply` / `ApplyToItems` 中给根菜单和每个 `mi.DropDown` 设 `Font = new Font("Microsoft YaHei UI", 10.5f)`。子菜单 `ToolStripDropDownMenu` 不会自动继承 owner 的 Font，必须显式设。

后两处必须再次 apply：`DropDownOpening` 时新 `new ToolStripMenuItem(...)` 出来的项默认前景是黑色 `SystemColors.ControlText`，不会从父菜单继承 `ForeColor`，光靠 Renderer 是无法在 hover 之外的常态背景上把字描白的——必须同时设置 `item.ForeColor = #E8ECF4`。

## 关键坑位

- **WinForms GDI+ 不支持 alpha**：DarkTheme.xaml 里大量 `#33FFFFFF` / `#EB1C2030` 这类含 alpha 通道的画刷在 WinForms 里直接传是被无视的（alpha 通道当作 Color.A 但 GDI+ 渲染目标是不透明的）。所有颜色必须先把 WPF 半透明色叠到底色上，折算为不透明等价值。
- **`ShowImageMargin = false`**：不关掉左边那条灰色 image margin gutter，整个菜单依然有"WinForms 味"。
- **子菜单要单独刷**：`ToolStripMenuItem.DropDown` 是另一个 `ToolStripDropDownMenu` 实例，它的 `ShowImageMargin` / `BackColor` / `Font` 不会自动从父菜单继承，必须递归 apply。
- **动态新加的菜单项**：`RefreshRecentClosedMenu` / `RefreshSnapshotMenu` 在 `DropDownOpening` 里 clear+rebuild，新的 `ToolStripMenuItem` 默认 `ForeColor = SystemColors.ControlText`（黑），常态下会被 Renderer 的 OnRenderItemText 兜住、但 hover 之外的子菜单悬停容器也会出问题——所以每次 rebuild 后都要 `ApplyToItems`。
- **圆角必须在 `Opened` 事件里设而非 `HandleCreated`**：HandleCreated 触发时 ToolStripDropDown 还未 layout，`Width`/`Height` 都是 0，`CreateRoundRectRgn` 出来是空区域，菜单整个不可见。Opened 触发时已确定尺寸。
- **圆角后必须吃掉默认 `OnRenderToolStripBorder`**：默认 1px 方边框落在 region 之外的角上会显方角缺口，把方法重写为空即可。
- **`SetWindowRgn` 接管 hRgn 句柄**：成功后不要 `DeleteObject(hRgn)`，那是 OS 的事 — 与 `AcrylicCompositor` 一致。

## 修复状态

已修复 — 2026-05-09。

## 验证

1. 右键托盘图标，弹出菜单是深色背景、白字、蓝灰色 hover 高亮。
2. 菜单四角是 8px 圆角（与 fence panel 风格一致），无方角缺口。
3. 中文字号比之前明显大一点（10.5pt MS YaHei UI），更接近 WPF 12.5px 视觉。
4. 鼠标移到「布局快照」「恢复最近关闭」上展开二级菜单，二级菜单也是深色 + 圆角 + 同字号。
5. 「自动整理」勾选项的勾号背景与暗色一致。
6. 禁用项（如「（无快照）」）显示为低对比度灰色而不是白底亮黑字。
7. Win+D / 截图 / Acrylic 模糊等之前修复的功能未受影响。
