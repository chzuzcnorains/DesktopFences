# Tab 模式菜单缺少视图/排序/图标风格子菜单（bug 33）

## 问题描述

把多个 fence 合并成 tab 组后，点 tab 条上的菜单按钮弹出的菜单里**没有**"图标风格""呈现方式""排序方式"，只有 重命名 / 分离为独立 Fence / 文件夹映射 / 关闭。独立（未合并）fence 的标题栏菜单则齐全。用户希望两者一致。

## 真因

可见 tab 条（Segmented / Underline 样式）的菜单由 `FenceHost.TabMenuButton_Click` **单独构建**，是一套与 `FencePanel.ShowTitleBarMenu` 平行、各自维护的菜单代码，没有包含 Phase 13/14 加在 `ShowTitleBarMenu` 里的三个子菜单。
（注：MenuOnly tab 样式走的是 `FencePanel.ShowTitleBarMenu`，本身就含这些子菜单，不受影响。）

## 修复

把三个子菜单的构建抽成 `FencePanel` 的公开方法复用，避免两套菜单再次漂移：

1. `FencePanel` 新增 `public void AddViewSortMenuItems(ItemCollection items)`，内部 `Add(BuildIconStyleSubmenu()) / Add(BuildViewModeSubmenu()) / Add(BuildSortSubmenu())`；
2. `FencePanel.ShowTitleBarMenu` 改为调用它（去重，行为不变）；
3. `FenceHost.TabMenuButton_Click` 在 重命名/分离 之后插入 `FenceContent.AddViewSortMenuItems(menu.Items)`。

`FenceContent` 是 tab 组当前活动 tab 对应的 `FencePanel`（`ActivatePanelForTab` 把 `FenceContent.DataContext` 设为活动 tab 的 VM），所以子菜单天然作用于活动 tab；`FenceContent.InteractionEnded` 也已被 FenceHost 订阅做自动保存，无需额外接线。

依赖 bug 32 的修复——子菜单要能展开，前提是 `DarkMenuItemStyle` 模板已带 Popup。

## 关键经验

同一功能存在两套并行的菜单构建路径（standalone 的 `ShowTitleBarMenu` 与 tab 的 `TabMenuButton_Click`）极易漂移。新增菜单项时应抽公共方法供两者复用，而不是各加一遍。

## 验证

把两个 fence 合并成 tab（Segmented/Underline 样式）→ 点 tab 菜单按钮 → 出现图标风格/呈现方式/排序方式且可用；切到另一个 tab 再开菜单 → 勾选状态与操作对应当前活动 tab。

| 项目 | 内容 |
|------|------|
| 修复日期 | 2026-06-15 |
| 涉及文件 | src/DesktopFences.UI/Controls/FencePanel.xaml.cs, src/DesktopFences.UI/Controls/FenceHost.xaml.cs |
