# 切换图标风格后已显示的 tile 不刷新

## 问题描述

外观设置 → 图标风格 在 `App`(自绘彩色 tile + 字母叠加) 与 `System`(Windows 经典 page-with-fold + 角标) 之间切换并保存后，已显示的 fence 内文件 tile **不会立即重新走 ItemTemplateSelector**，仍维持旧的视觉模板。
重启应用或新增/删除文件触发 ListBox 重建容器后才生效。

## 复现步骤

1. 打开任意一个有文件的 fence
2. 托盘 → 设置 → 外观 → 图标风格切到另一个值（App ↔ System）
3. 保存
4. 观察现有 tile：仍是切换前的样子

## 根因

`FencePanel.RefreshFileTileTemplate()` 原本调用 `FileListBox.Items.Refresh()`：

```csharp
public void RefreshFileTileTemplate()
{
    FileListBox?.Items.Refresh();
}
```

`Items.Refresh()` 只是刷新视图（重新跑 filter/sort/group），**不会让 ItemTemplateSelector 在已存在的 ListBoxItem 上重新跑一次 `SelectTemplate`**。`FileListBox` 启用了 `VirtualizingPanel.IsVirtualizing="True"` + `VirtualizationMode="Recycling"`，回收的容器保留旧的 ContentTemplate，所以已渲染的 tile 不会切换到新模板。

## 修复

`FencePanel.RefreshFileTileTemplate()` 改为 detach 再 re-attach `ItemTemplateSelector`，强制 WPF 丢弃所有 ItemContainer 并按新选择器重建：

```csharp
public void RefreshFileTileTemplate()
{
    if (FileListBox is null) return;
    var selector = FileListBox.ItemTemplateSelector;
    FileListBox.ItemTemplateSelector = null;
    FileListBox.ItemTemplateSelector = selector;
}
```

WPF 检测到 `ItemTemplateSelector` 从非空变为非空后会触发完整的 ItemContainer 重建。

## 验证

修复版本下重复复现步骤 1-3，所有 tile 立即按新风格渲染。

## 修复日期

2026-05-07

## 相关文件

- `src/DesktopFences.UI/Controls/FencePanel.xaml.cs` — `RefreshFileTileTemplate()`
- `src/DesktopFences.UI/Controls/FencePanel.xaml` — `FileListBox` 启用虚拟化的位置

## 经验教训

WPF ListBox 在 `VirtualizationMode="Recycling"` 下，**任何只刷新数据视图的方法（`Items.Refresh()` / `CollectionView.Refresh()`）都不会让 `ItemTemplateSelector` 重新对已存在的容器跑选择**。如果切换的是模板维度（不是数据维度），必须强制容器重建：detach/re-attach selector，或者临时 reset `ItemsSource`。
