# CLAUDE.md

本文件为 Claude Code (claude.ai/code) 在本仓库中工作时提供指引。

## 项目概述

DesktopFences 是一款 Windows 桌面整理工具（对标 Stardock Fences），基于 C# / .NET 9 / WPF 构建。通过桌面分区容器（Fence）管理文件，支持 `Win+D` 后仍然可见。核心功能：拖放管理、自动分类规则、折叠（Rollup）、Peek 预览、Snap 吸附、Folder Portal、桌面分页、多显示器支持。

## 核心约束
**沟通用中文**
**如果不清楚请先提问补全信息**
**每次功能开发或技术方案调整后，必须将变更内容同步回写到 `docs/design` 目录中。** 设计文档是项目的单一事实来源（Single Source of Truth），所有已验证的技术决策、架构变更、功能规格修改都必须在设计文档中体现。
**详细 Phase 计划见 `docs/plan/` 目录，计划完成后记得同步回写相关内容[当前计划](/docs/plan/currentplan.md) [当前任务](/docs/plan/currenttasks.md) [已完成 Phase 列表](/docs/plan/complete.md)  [待完成功能列表](/docs/plan/todo.md)** 
**bug修复请读取`docs/bug/`目录中[问题总结](/docs/bug/README.md)来寻找相似问题，修复完bug后要生成对应的修复文档，更新[问题总结](/docs/bug/README.md)，便于以后同类问题排查**


## 构建与运行

```bash
# 构建整个解决方案
dotnet build DesktopFences.sln

# Release 构建
dotnet build DesktopFences.sln -c Release

# 运行应用
dotnet run --project src/DesktopFences.App

# 运行测试
dotnet test tests/DesktopFences.Core.Tests

# 运行单个测试
dotnet test tests/DesktopFences.Core.Tests --filter "FullyQualifiedName~TestMethodName"

# 发布自包含版本
dotnet publish src/DesktopFences.App -c Release -r win-x64 --self-contained
```

## 架构

```
DesktopFences.App    → WPF 应用入口、DI 容器、托盘图标、启动管理
    ↓ references
DesktopFences.UI     → WPF 控件（FencePanel, FenceHost）、ViewModel、MVVM
    ↓ references
DesktopFences.Shell  → Win32 P/Invoke：桌面嵌入、热键钩子、Shell 图标、文件监控、拖放 COM
    ↓ references
DesktopFences.Core   → 纯 C# 模型、规则引擎、布局持久化（无 UI/OS 依赖）
```

**桌面嵌入方案（已验证）**：Fence 窗口使用 `WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE`（隐藏于任务栏/Alt+Tab）作为浮动 WPF 窗口。正常状态下通过 `SetWindowPos(HWND_BOTTOM)` 保持在桌面之上、其他窗口之下。`Win+D` 时通过 `WH_KEYBOARD_LL` 低级键盘钩子检测，延迟 300ms 后 `SetWindowPos(HWND_TOPMOST)` 临时置顶。通过 `SetWinEventHook(EVENT_SYSTEM_FOREGROUND)` 监听前台窗口变化，用户切换到其他窗口时自动恢复 `HWND_BOTTOM`。

## 关键文件

- `docs/design/` — 功能设计文档（架构、规则引擎、数据持久化等）
- `docs/plan/` — Phase 开发计划
- `src/DesktopFences.Core/Models/` — FenceDefinition, LayoutSnapshot, ClassificationRule, AppSettings
- `src/DesktopFences.Core/Services/` — IRuleEngine, ILayoutStore, RuleEngine
- `src/DesktopFences.Shell/Interop/` — Win32 P/Invoke 声明（NativeMethods）
- `src/DesktopFences.Shell/Desktop/` — DesktopEmbedManager（桌面嵌入 + 键盘钩子）
- `src/DesktopFences.Shell/Icon/` — ShellIconExtractor（图标的 LRU 缓存提取）
- `src/DesktopFences.Shell/FileMonitor/` — DesktopFileMonitor（FSWatcher + 全量扫描）
- `src/DesktopFences.UI/Controls/` — FencePanel（UserControl）, FenceHost（Window）
- `src/DesktopFences.UI/Controls/Settings/` — SettingsWindow 各面板 UserControl
- `src/DesktopFences.UI/Themes/` — DarkTheme.xaml, TabStyles.xaml, Icons.xaml
- `src/DesktopFences.UI/ViewModels/` — MVVM ViewModel

## 关键 Win32 API

所有 P/Invoke 集中在 `DesktopFences.Shell` 项目：
- `SetWindowLongPtr(GWL_EXSTYLE)` — 应用 WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE
- `SetWindowPos(HWND_BOTTOM / HWND_TOPMOST)` — z-order 管理
- `SetWindowsHookEx(WH_KEYBOARD_LL)` — 检测 Win+D
- `SetWinEventHook(EVENT_SYSTEM_FOREGROUND)` — 前台窗口变化监听
- `RegisterHotKey` — Peek 热键（Win+Space）、搜索热键（Ctrl+\`）
- `SHGetFileInfo` — 文件图标提取
- `SHChangeNotifyRegister` — 桌面文件变更通知
- `IContextMenu` COM — Shell 右键菜单

## 开发规范

- UI 层使用 MVVM 模式；ViewModel 在 `ViewModels/`，View 在 `Controls/`
- Core 项目零平台依赖 — 所有 Win32 调用在 Shell 项目
- Shell 项目启用 `AllowUnsafeBlocks` 用于 P/Invoke
- 数据以 JSON 格式持久化到 `%APPDATA%\DesktopFences\`
- 原子写入（写临时文件 → rename）防止数据损坏
- 图标缓存按文件扩展名（非路径）缓存，LRU 上限 500
- FileSystemWatcher + 定时全量扫描（30 秒）保证可靠性

## 当前开发阶段

**Phase 0-10 全部完成（61 个单元测试通过）。** 

