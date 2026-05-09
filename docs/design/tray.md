# 系统托盘

## 1. 功能菜单

```
├─ 显示/隐藏所有 Fence
├─ Peek 桌面
├─ ───────────────
├─ 新建 Fence
├─ 新建文件夹映射 Fence...
├─ 布局快照
│    ├─ 保存当前布局
│    ├─ [快照1] [快照2] ...
│    └─ 管理快照...
├─ 自动整理
│    ├─ [✓] 自动整理（勾选状态）
│    └─ 立即整理桌面
├─ ───────────────
├─ 分类规则...
├─ 设置
└─ 退出
```

## 2. 托盘图标

- 从嵌入资源加载：`Application.GetResourceStream(new Uri("pack://application:,,,/Assets/app.ico"))`
- 多尺寸 ICO 文件（16/20/24/32/40/48/64/128/256，PNG 压缩帧）
- 任务栏/Alt-Tab/资源管理器按需选帧

## 3. 托盘交互

- 左键单击：显示/隐藏所有 Fence
- 右键单击：显示托盘菜单
- 双击：显示/隐藏所有 Fence（与 Quick Hide 独立）

## 4. 视觉风格

- 托盘菜单基于 WinForms `NotifyIcon.ContextMenuStrip`（Win32 Shell 通知区限制，无法直接换成 WPF `ContextMenu`）。
- 通过 `DarkTrayMenuRenderer`（位于 [`src/DesktopFences.App/DarkTrayMenuRenderer.cs`](../../src/DesktopFences.App/DarkTrayMenuRenderer.cs)）让 WinForms 菜单与 [`DarkTheme.xaml`](../../src/DesktopFences.UI/Themes/DarkTheme.xaml) 的 `DarkContextMenuStyle` 视觉一致：
  - 自定义 `ProfessionalColorTable` + `ToolStripProfessionalRenderer`，色板源自 DarkTheme（`FenceBaseColor #1A2036`、`AccentStrong`、`TextPrimaryBrush`）。
  - WinForms GDI+ 不支持 alpha，所有 WPF 半透明色必须先叠到底色折算为不透明等价值。
  - `ShowImageMargin = false` 关掉左侧 image gutter。
  - **8px 圆角**：与 `DarkContextMenuStyle CornerRadius="8"` 对齐。沿用 fence panel `AcrylicCompositor` 同方案 — `Opened` 事件里给 popup HWND 调 `SetWindowRgn(CreateRoundRectRgn(...))`（不能用 `HandleCreated`：handle 早于 layout，此时 `Width`/`Height` 仍为 0）。重写 `OnRenderToolStripBorder` 为空避免方边框跑出 region。
  - **字号 10.5pt（MS YaHei UI）**：放大原 9pt 默认字，向 WPF 菜单 `FontSize=12.5` 靠拢。子菜单 `Font` 不继承，需要单独设。
- 动态填充的子菜单（最近关闭、布局快照）在 `DropDownOpening` 重建后必须递归调用 `DarkTrayMenuRenderer.ApplyToItems`：新 `ToolStripMenuItem` 默认 `ForeColor = SystemColors.ControlText`（黑），不会从父菜单继承前景色。
