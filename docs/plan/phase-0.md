# Phase 0: 基础骨架

**目标**：一个无边框 WPF 窗口显示在桌面上，Win+D 后仍然可见。

## 0.1 项目结构搭建
- [x] 创建解决方案和项目（Core / Shell / UI / App / Tests）
- [x] 配置项目引用关系和 TargetFramework
- [x] 添加 `Directory.Build.props` 统一版本号和公共属性
- [x] 添加 `.editorconfig` 统一代码风格
- [x] 添加 `.gitignore` 并初始化 Git 仓库

## 0.2 Win32 Interop 基础层 (Shell 项目)
- [x] `NativeMethods.cs` — P/Invoke 声明集中管理
  - `SetWindowLongPtr`, `GetWindowLongPtr` (GWL_EXSTYLE)
  - `SetWindowPos` (HWND_BOTTOM / HWND_TOPMOST)
  - `SetWindowsHookEx`, `UnhookWindowsHookEx`, `CallNextHookEx` (WH_KEYBOARD_LL)
  - `SetWinEventHook`, `UnhookWinEvent` (EVENT_SYSTEM_FOREGROUND)
  - `GetForegroundWindow`, `GetAsyncKeyState`, `GetModuleHandle`
- [x] `DesktopEmbedManager.cs` — 桌面嵌入管理
  - 将 WPF 窗口配置为 `WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE`
  - 正常态 `HWND_BOTTOM`（桌面之上、其他窗口之下）
  - 低级键盘钩子检测 Win+D → 延迟 300ms → `HWND_TOPMOST`
  - `SetWinEventHook(EVENT_SYSTEM_FOREGROUND)` 监听前台窗口变化 → 自动恢复 `HWND_BOTTOM`

## 0.3 FenceHost 窗口 (UI 项目)
- [x] `FenceHost.xaml` — 无边框、透明背景的 WPF Window
  - `WindowStyle=None`, `AllowsTransparency=True`, `Background=Transparent`
  - `ShowInTaskbar=False`, `ResizeMode=NoResize`
- [x] 在 `Loaded` 事件中调用 `DesktopEmbedManager.RegisterWindow()` 配置窗口样式
- [x] 基础半透明背景渲染（深色半透明矩形 + 圆角 + 阴影）

## 0.4 应用入口 (App 项目)
- [x] 单实例检查 (`Mutex`)
- [x] 创建 FenceHost 窗口并显示
- [x] 验证 Win+D 后窗口仍然可见 ✅ 通过（2026-03-03）

**验收标准**：启动应用后，桌面上出现一个半透明矩形，按 Win+D 后矩形仍然显示。 ✅ 已通过
