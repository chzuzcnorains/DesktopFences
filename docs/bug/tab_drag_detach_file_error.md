# 从 tab 拖拽分离 fence 报"源文件名和目标文件名相同"（bug 34）

## 问题描述

把多个 fence 合并成 tab 组后，想用鼠标把某个 tab 拖出来分离成独立 fence（期望与菜单"分离为独立 Fence"一样），结果弹出 Windows 错误对话框"源文件名和目标文件名相同"，分离失败。

## 真因（两段）

1. **根本没有 tab 拖拽分离功能**：tab 拖拽（`FenceHost.OnTabStripPreviewMouseMove` / `OnTabStripPreviewMouseLeftButtonUp`）只做**条内重排序**，用 `Mouse.Capture(this, CaptureMode.SubTree)` 实现，拖出 tab 条只会被 `ComputeTabDropIndex` 夹回条内 → noop。

2. **文件拖拽被误触发（错误对话框的真正来源）**：`CaptureMode.SubTree` 捕获仍会把 `MouseMove` 路由给活动 tab 面板里的文件 tile。用户把 tab 往下拖到内容区时，文件 tile 的 `FencePanel.FileItem_MouseMove` 触发——它只判 `LeftButton==Pressed` 和（陈旧的）`_dragStartPoint`，于是对光标下的桌面文件发起 OLE 文件拖拽（`DoDragDrop` + `FileDrop`）。拖到桌面释放 → Windows shell 尝试把桌面文件移动到桌面 → 报"源文件名和目标文件名相同"（该对话框由 Windows 弹出，不是本应用）。

## 修复

1. **实现 tab 拖拽撕离**：`OnTabStripPreviewMouseLeftButtonUp` 释放捕获后，按释放点相对 `TabStripBorder` 的垂直越界量 `vOut`，若 `vOut > TabDetachThreshold`(24px) 且 tab 数 > 1，判定为撕离 → `TabDetachRequested?.Invoke(_tabs[from])`，复用既有 `App.DetachTab`，效果与菜单"分离为独立 Fence"完全一致；否则走原重排序逻辑。

2. **堵住文件拖拽误触发**：`FencePanel.FileItem_MouseMove` 开头加守卫 `if (_hostWindow is not null && ReferenceEquals(Mouse.Captured, _hostWindow)) return;`。正常文件拖拽前 FencePanel 不捕获鼠标（`Mouse.Captured==null`，不受影响）；tab 拖拽期间 `Mouse.Captured==FenceHost 窗口==_hostWindow`，守卫精确拦住误触发。这也是让撕离可靠工作的必要前提——否则 `DoDragDrop` 的模态循环会劫持手势，tab 的 up 处理器都收不到。

## 关键经验

`Mouse.Capture(window, CaptureMode.SubTree)` 期间，子控件仍会收到 mouse 事件。任何"按住即可发起 `DoDragDrop`"的子控件处理器（只看 `LeftButton==Pressed`）都可能在父级捕获的拖拽手势里被误触发。用 `Mouse.Captured == 宿主窗口` 作守卫可干净区分"父级正在做捕获式拖拽"与"普通子控件交互"。

## 验证

合并两个 fence 成 tab → 按住一个 tab 往下拖出 tab 条释放 → 分离成独立 fence（位置/主题同菜单分离），不再弹错误；条内横向拖动仍正常重排；菜单分离不变；从 tab 化 fence 拖文件到另一 fence 仍是移动、拖到 Explorer 仍复制引用。

| 项目 | 内容 |
|------|------|
| 修复日期 | 2026-06-15 |
| 涉及文件 | src/DesktopFences.UI/Controls/FenceHost.xaml.cs, src/DesktopFences.UI/Controls/FencePanel.xaml.cs |
