# 重命名对话框模态期间所有 fence/overlay 无法点击（bug 21 同类残留）

## 问题描述

右键 fence 标题栏/tab 选择"重命名"打开 `RenameWindow` 后，对话框开着期间，**所有** fence 窗口和未归档图标 overlay 都无法点击/拖拽，不只是被重命名的那个。

## 真因

与 bug 21（[设置窗口模态禁用 fences](settings_modal_disables_fences.md)）完全同根因：`FencePanel.BeginRename` 用 `ShowDialog()` 打开 `RenameWindow`，WPF 模态对话框会在 Win32 层对**同线程所有其他顶层窗口**调用 `EnableWindow(FALSE)`，整个桌面的 fence/overlay 在 OS 层被排除在输入路由之外。bug 21 修复时只改了 SettingsWindow，RenameWindow 漏掉了。

## 修复

与 SettingsWindow 同方案：

1. `RenameWindow` 移除 `DialogResult`（非模态窗口上设置 DialogResult 会抛 `InvalidOperationException`），新增 `RenameConfirmed` 事件，确认后 `Invoke + Close()`；
2. `FencePanel.BeginRename` 改 `Show()` 非模态 + 订阅 `RenameConfirmed` 回调；字段缓存当前实例防止重复打开（再次触发时 `Activate()` 置前复用）；
3. `Owner = Window.GetWindow(this)` 保持对话框浮在所属 fence 之上。

（`SaveFileDialog`/`OpenFileDialog`/`OpenFolderDialog` 等系统公共对话框保持模态——这是系统级预期行为，不受此问题影响。）

## 关键经验

bug 21 的规则需要全局执行：**本项目中任何自定义 WPF 窗口都禁止 `ShowDialog()`**，一律 `Show()` + 事件回调。模态只允许系统公共对话框。排查方法：`grep ShowDialog`，逐个确认是否系统对话框。

## 验证

打开重命名对话框时点击其他 fence 的文件 → 可正常选中/双击；重复触发重命名 → 复用同一窗口；确认/取消/Esc/Enter 行为不变。

| 项目 | 内容 |
|------|------|
| 修复日期 | 2026-06-12 |
| 涉及文件 | RenameWindow.xaml.cs, FencePanel.xaml.cs |
