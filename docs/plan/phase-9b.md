# Phase 9b: DarkTheme 深化与无边框改造

## 14.1 UI 美化 — 各窗口 DynamicResource 替换

**FencePanel.xaml**：
- `FenceBorder.Background` → `{DynamicResource FenceBackgroundBrush}`
- `FenceBorder.BorderBrush` → `{DynamicResource BorderBrush}`
- `TitleBarBorder.Background` → `{DynamicResource TitleBarBrush}`
- `TitleText.Foreground` → `{DynamicResource TextPrimaryBrush}`
- 文件项 `TextBlock.Foreground` → `{DynamicResource TextPrimaryBrush}`
- 文件项新增 `IsMouseOver` hover 触发器 → `{DynamicResource HoverBrush}`
- 文件项 `IsSelected` 触发器 → `{DynamicResource SelectedBrush}`
- 空状态文本 → `{DynamicResource SubtleBorderBrush}`
- 代码中拖拽边框颜色也改为 `FindResource()` 引用

## 14.2 SettingsWindow 无边框改造

- `WindowStyle=None, AllowsTransparency=True, Background=Transparent`
- 外层 `Border`: `CornerRadius=10`, `DropShadowEffect(BlurRadius=12)`, 半透明暗色背景 `#EE1E1E2E`
- 自定义标题栏：40px 高度，`#22FFFFFF` 背景，圆角顶部，`MouseLeftButtonDown → DragMove()`
- 关闭按钮使用 `SubtleButtonStyle`
- 所有控件替换为 DarkTheme 样式
- 文字标签统一使用 `{DynamicResource TextPrimaryBrush}` / `TextSecondaryBrush`

## 14.3 微动画增强

**Tab 切换 fade（150ms）**：
- `ActivatePanelForTab()` 先 75ms fade-out（QuadraticEase EaseIn），完成后切换 DataContext，再 75ms fade-in（QuadraticEase EaseOut）
- 首次加载（`IsLoaded=false`）时跳过动画直接赋值

**文件项弹入动画**：
- `LoadIconForLastFile()` 完成后调用 `AnimateNewFileItem()`
- 在 `DispatcherPriority.Loaded` 回调中对最后一个 ListBoxItem 容器执行：
  - `ScaleTransform` 0.8→1.0（200ms QuadraticEase EaseOut）
  - `Opacity` 0→1（200ms）

## 14.4 Tab 样式多样化

**TabStyle 枚举**（`src/DesktopFences.Core/Models/TabStyle.cs`）：
```csharp
public enum TabStyle { Flat, Segmented, Rounded, MenuOnly }
```

**AppSettings** 新增 `TabStyle TabStyle { get; set; } = TabStyle.Flat;`

**TabStyles.xaml**（`src/DesktopFences.UI/Themes/TabStyles.xaml`）— 3 种可见样式各有 active/inactive 两个 Style Key：

| 样式 | Active Key | Inactive Key | 特点 |
|------|-----------|-------------|------|
| Flat | `FlatTabButtonActiveStyle` | `FlatTabButtonStyle` | 2px 蓝色底部指示条 `#6688CC` |
| Segmented | `SegmentedTabButtonActiveStyle` | `SegmentedTabButtonStyle` | 右侧 1px 分隔线，active 蓝色填充 |
| Rounded | `RoundedTabButtonActiveStyle` | `RoundedTabButtonStyle` | `CornerRadius=12` 胶囊形，active 蓝色填充 |

**MenuOnly 模式**：
- Tab strip 高度设为 0，`ShowTitleBar=true`
- `FencePanel.MenuOnlyTabs` 属性提供 tab 列表
- 标题栏 "⋯" 菜单顶部显示 tab 列表
