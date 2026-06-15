# 应用内拖拽产生重复条目（fence 间拖拽不删源 / overlay 图标残留）

## 问题描述

1. 从 fence A 拖文件到 fence B：文件同时出现在两个 fence（A 中不消失）；
2. 从未归档 overlay 拖文件进 fence：overlay 图标可能残留，文件在 fence 和 overlay 同时显示。

## 真因

WPF 拖放协议中，`DoDragDrop` 的返回值由**目标**在 Drop 事件里设置的 `e.Effects` 决定。`FencePanel.OnDrop` 从未设置 `e.Effects`（默认值是 AllowedEffects = Copy|Move 的组合），而两个**源端**都用 `result == DragDropEffects.Move` 的相等判断决定是否删除自己的条目——永远不成立：

- `FencePanel.FileItem_MouseMove`：`result == Move` 才 `RemoveFile` → 源 fence 不删；
- `DesktopIconOverlay.OnIconMouseMove`：`result == Move` 才 `RemoveIcon` → overlay 图标残留。

## 修复（产品决策：应用内拖拽 = 移动语义）

1. 新增 `InternalDragFormats.Marker`（自定义 OLE 数据格式 `"DesktopFences.InternalDrag"`），两个源端创建 `DataObject` 时 `SetData(Marker, true)`；
2. `FencePanel.OnDrop` 显式回报 Effects：
   - **带标记（应用内）**：实际新增了条目 → `Move`（源端据此删除）；文件已在本 fence（含自拖自）→ `None`（防止源端把唯一条目删掉）；
   - **无标记（Explorer 来源）**：恒为 `Copy`——**绝不能对 Explorer 回报 Move**，同卷拖拽时 Explorer 会按"移动"语义删除磁盘源文件；
3. `OnDragOver` 同步区分，内部拖拽显示移动光标。

## 关键经验

- WPF 拖放中 **Drop 端不设置 `e.Effects` 时返回值是 AllowedEffects 原值**，源端用 `== Move` 判断会静默失效——跨窗口拖放协议的两端必须显式约定 Effects。
- 区分"应用内拖拽"与"外部拖拽"用自定义 DataFormat 标记最可靠；Explorer 会忽略未知格式，无副作用。
- 回报给 Explorer 的 Effect 有真实文件操作含义（Move = 删源文件），不能随意回报。

## 验证

fence A → fence B：A 中消失、B 中出现；fence 内自拖自：条目不丢；Explorer → fence：复制引用、磁盘文件不动；overlay → fence：overlay 图标消失。

| 项目 | 内容 |
|------|------|
| 修复日期 | 2026-06-12 |
| 涉及文件 | InternalDragFormats.cs (新增), FencePanel.xaml.cs, DesktopIconOverlay.xaml.cs |
