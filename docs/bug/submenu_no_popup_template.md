# 自定义 MenuItem 模板缺 Popup 导致子菜单无法展开（bug 32）

## 问题描述

fence 标题栏 ⋯ 菜单里的"图标风格""呈现方式""排序方式"三个子菜单，鼠标悬停/点击根项**没有任何反应**——二级菜单根本展不开，自然也无法选择里面的选项。一级菜单本身显示正常。

## 真因

`DarkTheme.xaml` 的 `DarkMenuItemStyle` 自定义了 `MenuItem` 的 `ControlTemplate`，但模板里**只有该项自身的 Icon/Header/Shortcut，没有 `Popup`、也没有 `ItemsPresenter`**。

WPF 的 `MenuItem` 内置有四套模板（按 `Role` 区分：`TopLevelHeader` / `TopLevelItem` / `SubmenuHeader` / `SubmenuItem`），其中 *Header 角色的模板带一个承载子项的 `Popup`+`ItemsPresenter`。一旦用单个自定义 `ControlTemplate` 覆盖 `Template`，它会替换**全部四种角色**，于是任何带子项的 MenuItem（`SubmenuHeader`）也没有 Popup 可用 → 子菜单无处渲染、无法展开。

一级菜单能正常显示，是因为它走的是 `ContextMenu` 自己的模板（`DarkContextMenuStyle` 里的 `IsItemsHost` StackPanel），与 MenuItem 模板无关；问题只在"带子项的 MenuItem"上暴露。该缺陷自 Phase 13 引入"图标风格"子菜单起就存在，只是当时没深入点开验证，直到 Phase 14 又加了两个子菜单才被发现。

## 修复

把 `DarkMenuItemStyle`（以及 `BasedOn` 它并 override 了 Template 的 `DarkDangerMenuItemStyle`）的模板根改为 `Grid`，在原可视 `Border` 之外补上：

1. 一个 `Popup x:Name="PART_Popup"`，`IsOpen="{TemplateBinding IsSubmenuOpen}"`，`Placement="Right"`，内部用与 `DarkContextMenuStyle` 同源风格的 `Border`（`#EB1C2030` / `#1AFFFFFF` / 圆角 8 / DropShadow）包 `ScrollViewer` + `ItemsPresenter`，容器设 `Grid.IsSharedSizeScope="True"` 让子项图标列对齐；
2. 一个子菜单箭头（`›`）指示符，`HasItems=True` 时才 Visible。

## 关键经验

**用单个自定义 `ControlTemplate` 覆盖 `MenuItem.Template` 时，必须自带 `Popup`+`ItemsPresenter`**，否则所有层级的子菜单都瘫痪。这类"一级正常、二级失效"的现象，第一嫌疑就是 MenuItem 模板漏了 submenu Popup，而不是 z-order（一级 Popup 能显示就证明 Popup 能浮在 bottom-most fence 之上）。

## 验证

独立 fence 标题栏 ⋯ → 悬停三个子菜单均能展开；选叶子项后视图/排序/图标风格立即变化；勾选状态、箭头、暗色样式一致。

| 项目 | 内容 |
|------|------|
| 修复日期 | 2026-06-15 |
| 涉及文件 | src/DesktopFences.UI/Themes/DarkTheme.xaml |
