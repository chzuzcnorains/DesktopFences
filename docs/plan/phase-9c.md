# Phase 9c: 图标系统

**目标**：取代前期各处遗留的占位 emoji / ASCII 字符（▲ ⋯ ✕ 📁 等），为主窗口图标、托盘图标、标题栏按钮、右键菜单、搜索面板引入一套矢量图标系统。

## 15.1 资产与资源

| 文件 | 说明 |
|---|---|
| `handoff/icons/app-logo.svg` | 48×48 viewBox 主 Logo 矢量源（蓝色渐变 + 四宫格 + 高光） |
| `handoff/icons/app-logo-mono.svg` | 托盘单色版本（`currentColor`） |
| `handoff/icons/actions.sprite.svg` | 20 个操作图标 sprite（24×24，`stroke-width=1.8`） |
| `handoff/icons/build-ico.ps1` | 由 AppLogo.xaml 渲染多尺寸 ICO 的 PowerShell 脚本 |
| `src/DesktopFences.UI/Themes/AppLogo.xaml` | Logo 的 `DrawingImage` 资源字典（`AppLogoImage`、`AppLogoTopColor`、`AppLogoBottomColor`） |
| `src/DesktopFences.UI/Themes/Icons.xaml` | 20 个操作图标 `Geometry` + `IconTemplate`（`ControlTemplate`）+ `DarkIconButtonStyle` + `CaptionButtonStyle` / `CaptionCloseButtonStyle` |
| `src/DesktopFences.App/Assets/app.ico` | 由 `build-ico.ps1` 生成的多尺寸 ICO（16/20/24/32/40/48/64/128/256，PNG 压缩帧） |

在 `App.xaml` 的 `MergedDictionaries` 中依序合并 `DarkTheme.xaml` → `TabStyles.xaml` → `AppLogo.xaml` → `Icons.xaml`。

## 15.2 图标资源 Key

| Key | 用途 |
|---|---|
| `IconSearch` | 搜索框前缀、托盘菜单、右键"搜索…" |
| `IconSettings` | Fence 标题栏菜单入口（"⋯"）、托盘"设置…" |
| `IconPin` / `IconLock` | 预留（置顶、锁定位置） |
| `IconHide` | "取消文件夹映射"、显隐相关 |
| `IconRollup` | Fence 折叠按钮（标题栏 + Tab 条），带 180° 旋转状态 |
| `IconPeek` | Peek 桌面（Win+Space） |
| `IconAdd` | 新建相关（托盘"新建 Fence"、Tab "+") |
| `IconMerge` / `IconSplit` | Tab 合并提示 / "分离为独立 Fence" |
| `IconTrash` | "关闭 Fence"、"删除" |
| `IconRule` | 分类规则 |
| `IconPortal` | Folder Portal（"设为文件夹映射…"、"更改映射文件夹") |
| `IconTheme` | 主题色（预留） |
| `IconClose` / `IconMin` / `IconMax` | 自定义窗口 caption 按钮 |
| `IconKeyboard` | 快捷键（预留） |
| `IconGrid` / `IconInfo` | Fence 管理 / 关于（预留） |

`IconTemplate` 是 `ContentControl` 模板：Tag 绑定 `Geometry`，`Stroke="{TemplateBinding Foreground}"`、`StrokeThickness=1.8`、圆角端点。所有图标自动跟随 `TextSecondaryBrush` / `TextPrimaryBrush` 的 `Foreground` 继承。

## 15.3 按钮样式

- `DarkIconButtonStyle` — 26×22，圆角 4，悬停用 `HoverBrush`、按下用 `PressBrush`
- `CaptionButtonStyle` — 继承上者，46×40，用于自定义 caption
- `CaptionCloseButtonStyle` — 继承 CaptionButtonStyle，但**覆写了模板**：悬停 `#E0412B`、按下 `#B8331F`、前景白

## 15.4 ICO 生成方案

`build-ico.ps1` 用 WPF `XamlReader.Load` 载入 `AppLogo.xaml`，对 `AppLogoImage` 逐尺寸 `RenderTargetBitmap.Render` → `PngBitmapEncoder` 得到 PNG 帧，再手写 ICO `ICONDIR` + `ICONDIRENTRY` 头把 9 帧（16/20/24/32/40/48/64/128/256，BPP=32）拼接写入。

## 15.5 验证

- `dotnet build DesktopFences.sln -c Release` 0 警告 0 错误
- `dotnet test tests/DesktopFences.Core.Tests` 通过 61/61
