# 最近关闭列表删除按钮样式与现有 UI 不一致

## 问题描述

在 设置 → Fence 管理 → 最近关闭 标签页，每张已关闭 Fence 卡片右下角并排两个按钮：

- 「恢复」按钮：使用 `AccentButtonStyle`，蓝色填充、白字、6px 圆角，padding 12,6
- 「删除」按钮：**裸 Button 默认样式**（Windows 经典灰白方角按钮），padding 10,6

视觉上严重不协调：删除按钮与暗色主题脱节，且高度/形状与紧邻的恢复按钮不齐。

## 期望

删除按钮整体样式与恢复按钮一致（圆角、白字、相同 padding/高度），但**背景色为红色**，以表达「危险/不可恢复」语义。

## 真因

`FencesManageSettingsPane.xaml.cs::BuildClosedCard()` 在创建删除按钮时**忘了赋 `Style`**，于是回退到 WPF 默认 Button 模板：

```csharp
// 修复前
var deleteBtn = new Button
{
    Content = "删除",
    Padding = new Thickness(10, 6, 10, 6),  // 与恢复的 12,6 不一致
    Margin = new Thickness(6, 0, 0, 0),
    VerticalAlignment = VerticalAlignment.Center,
    ToolTip = "从最近关闭列表中永久移除",
};
```

而 `DarkTheme.xaml` 早已提供配套的 `DangerButtonStyle`（与 `AccentButtonStyle` 同模板、同圆角、同字色，仅 Background 换成 `#44AA4444` 红色调，hover/press 配套加深），只需挂上即可。

## 修复

[FencesManageSettingsPane.xaml.cs:300-309](src/DesktopFences.UI/Controls/Settings/FencesManageSettingsPane.xaml.cs#L300-L309) 给删除按钮补上 `Style` 与对齐恢复按钮的 padding：

```csharp
var deleteBtn = new Button
{
    Content = "删除",
    Style = (Style)FindResource("DangerButtonStyle"),
    Padding = new Thickness(12, 6, 12, 6),
    Margin = new Thickness(6, 0, 0, 0),
    VerticalAlignment = VerticalAlignment.Center,
    ToolTip = "从最近关闭列表中永久移除",
};
```

## 教训

- WPF 在代码后台 `new Button{}` 时**不会自动应用主题样式**，必须显式 `FindResource` 挂上，否则回退到 OS 默认。
- 项目已为「主操作 / 次操作 / 危险操作」三类语义提供 `AccentButtonStyle / DarkButtonStyle / DangerButtonStyle`，新增按钮时优先复用而不是新建样式。

## 修复验证

- 打开 设置 → Fence 管理 → 最近关闭，确认每张卡片的「删除」按钮：
  - 红色填充、白字、6px 圆角
  - 与「恢复」按钮高度/padding 一致并排无错位
  - 鼠标悬停加深、按下再加深
- 点击「删除」仍能正常从最近关闭列表移除该条记录。

## 修复日期

2026-05-09
