# Phase 9a: 应用程序图标

## 9.1 应用程序图标

**图标设计**：
- 概念：2×2 圆角矩形网格（桌面分区隐喻），蓝色渐变调（#4488CC 系列），深色背景 #1E1E26
- 多尺寸：16×16、32×32、48×48、256×256（PNG 内嵌 ICO 格式）
- 每个网格单元使用不同蓝色渐变，带微弱内发光高光
- 外层圆角矩形背景带蓝色细边框

**文件与配置**：
- `src/DesktopFences.App/Assets/app.ico` — 多尺寸 ICO 文件
- `DesktopFences.App.csproj` — `<ApplicationIcon>Assets\app.ico</ApplicationIcon>` + `<Resource Include="Assets\app.ico" />`
- `App.xaml.cs SetupTrayIcon()` — 从嵌入资源加载：`Application.GetResourceStream(new Uri("pack://application:,,,/Assets/app.ico"))`
- `tools/IconGenerator/` — 图标生成工具（C# 控制台项目，使用 System.Drawing 程序化绘制）

## 9.2 DarkTheme 主题基础设施

**资源字典**（`src/DesktopFences.UI/Themes/DarkTheme.xaml`）：

颜色资源（Color + SolidColorBrush 成对定义）：
```
FenceBaseColor    = #1E1E2E     AccentColor       = #6688CC
SurfaceColor      = #2A2A3E     TextPrimaryColor   = #EEEEEE
TextSecondaryColor = #AACCCCCC  BorderColor        = #55888888
HoverColor        = #22FFFFFF   PressColor         = #33FFFFFF
SelectedColor     = #446688CC   DangerColor        = #CC4444
```

半透明变体 Brush：
```
FenceBackgroundBrush = #CC1E1E2E   TitleBarBrush      = #44FFFFFF
InputBackgroundBrush = #22FFFFFF   SubtleBorderBrush  = #33FFFFFF
FocusBorderBrush     = #887799CC
```

控件样式（均含 hover/press 状态触发器）：
| 样式 Key | 目标控件 | 特点 |
|---------|---------|------|
| `DarkButtonStyle` | Button | 圆角 6px，无边框，hover #44FFFFFF，press #55FFFFFF |
| `AccentButtonStyle` | Button | 蓝色基调 #446688CC，hover/press 渐亮 |
| `DangerButtonStyle` | Button | 红色基调 #44AA4444 |
| `SubtleButtonStyle` | Button | 透明背景，圆角 4px，用于工具栏/菜单按钮 |
| `DarkTextBoxStyle` | TextBox | 暗背景 #22FFFFFF，圆角 4px，focus 蓝色边框 #887799CC |
| `DarkComboBoxStyle` | ComboBox | 自定义暗色下拉模板，下拉框 #EE2A2A3E 背景 |
| `DarkComboBoxItemStyle` | ComboBoxItem | hover #33FFFFFF，selected #446688CC |
| `DarkCheckBoxStyle` | CheckBox | 16×16 暗色方框，勾选蓝色 #6688CC + 蓝色边框 |
| `DarkSliderStyle` | Slider | 圆形蓝色 Thumb 16px，4px 暗色 Track |
| `DarkListBoxItemStyle` | ListBoxItem | hover #15FFFFFF，selected #30447799，圆角 4px |
| `DarkScrollBarStyle` | ScrollBar | 4px 薄滚动条，hover 扩展到 6px |

**注册方式**（`App.xaml`）：
```xml
<ResourceDictionary Source="pack://application:,,,/DesktopFences.UI;component/Themes/DarkTheme.xaml" />
```
