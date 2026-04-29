# 系统架构

## 1. 项目概述

DesktopFences 是一款 Windows 桌面整理工具，对标 Stardock Fences 6，通过桌面分区容器（Fence）对桌面文件/快捷方式进行分组、自动分类和布局管理。

**核心约束**：Fence 窗口必须在 `Win+D`（显示桌面）后仍然可见，这是产品的基本要求。

**技术栈**：C# / .NET 9 / WPF + Win32 Interop

---

## 2. 分层架构

```
┌──────────────────────────────────────────────────────┐
│                  DesktopFences.App                     │
│              (WPF Application 入口)                    │
│  ┌────────────┐  ┌───────────┐  ┌──────────────────┐ │
│  │ TrayIcon   │  │ HotkeyMgr │  │ StartupManager   │ │
│  └────────────┘  └───────────┘  └──────────────────┘ │
└──────────────────────┬───────────────────────────────┘
                       │
┌──────────────────────┼───────────────────────────────┐
│              DesktopFences.UI                          │
│            (WPF Controls & MVVM)                      │
│                      │                                │
│  ┌──────────────┐ ┌──┴──────────┐ ┌────────────────┐ │
│  │ FencePanel   │ │ FenceHost   │ │ SettingsWindow │ │
│  │ (UserControl)│ │ (Window)    │ │                │ │
│  │ ┌──────────┐│ │ ┌──────────┐│ └────────────────┘ │
│  │ │TitleBar  ││ │ │DesktopEmb││                     │
│  │ │IconGrid  ││ │ │edding    ││                     │
│  │ │TabBar    ││ │ │Layer     ││                     │
│  │ │Scrollbar ││ │ └──────────┘│                     │
│  │ └──────────┘│ └─────────────┘                     │
│  └──────────────┘                                     │
└──────────────────────┬───────────────────────────────┘
                       │
┌──────────────────────┼───────────────────────────────┐
│            DesktopFences.Shell                         │
│         (Win32 Interop & OS 集成)                      │
│                      │                                │
│  ┌──────────────┐ ┌──┴──────────┐ ┌────────────────┐ │
│  │DesktopEmbed  │ │ ShellIcon   │ │ FileMonitor    │ │
│  │  Manager     │ │ Extractor   │ │ (FSWatcher +   │ │
│  │(Win+D hook,  │ │(SHGetFile   │ │ SHChangeNotify)│ │
│  │ z-order mgmt)│ │ Info, thumb)│ │                │ │
│  ├──────────────┤ ├─────────────┤ ├────────────────┤ │
│  │ HotkeyHook   │ │ ShellMenu   │ │ DragDropHelper │ │
│  │(WH_KEYBOARD  │ │ Integration │ │(IDropTarget,   │ │
│  │ _LL, hotkeys)│ │(IContextMenu│ │ IDataObject)   │ │
│  │              │ │ shell ext)  │ │                │ │
│  └──────────────┘ └─────────────┘ └────────────────┘ │
└──────────────────────┬───────────────────────────────┘
                       │
┌──────────────────────┼───────────────────────────────┐
│            DesktopFences.Core                          │
│          (纯业务逻辑，无 UI/OS 依赖)                    │
│                      │                                │
│  ┌──────────────┐ ┌──┴──────────┐ ┌────────────────┐ │
│  │ Models       │ │ RuleEngine  │ │ LayoutStore    │ │
│  │ ─FenceDef    │ │ (自动分类    │ │ (JSON 持久化   │ │
│  │ ─LayoutSnap  │ │ 规则匹配)   │ │ 布局快照)     │ │
│  │ ─ClassRule   │ │             │ │                │ │
│  │ ─DesktopPage │ └─────────────┘ └────────────────┘ │
│  └──────────────┘                                     │
└──────────────────────────────────────────────────────┘
```

### 层级职责

| 层 | 项目 | 职责 | 依赖 |
|----|------|------|------|
| **Core** | DesktopFences.Core | 数据模型、规则引擎、布局持久化。纯 C#，无平台依赖 | 无 |
| **Shell** | DesktopFences.Shell | Win32 P/Invoke 封装：桌面嵌入、热键钩子、Shell 图标提取、文件系统监控、拖放 COM | Core |
| **UI** | DesktopFences.UI | WPF 控件：FencePanel、FenceHost 窗口、设置界面。MVVM 模式 | Core, Shell |
| **App** | DesktopFences.App | 应用入口、DI 容器、托盘图标、启动管理 | Core, Shell, UI |
| **Tests** | DesktopFences.Core.Tests | Core 层单元测试（规则引擎、布局序列化） | Core |

---

## 3. 关键数据流

```
桌面文件变更 → FileMonitor (Shell) → RuleEngine (Core) → FencePanel 更新 (UI)
                                            ↓
                                     LayoutStore (Core) → fences.json (磁盘)

用户拖放文件 → DragDropHelper (Shell) → FenceViewModel (UI) → LayoutStore (Core)

Win+D 按下 → HotkeyHook (Shell) → DesktopEmbedManager (Shell) → SetWindowPos (所有 FenceHost)

Peek 触发 → HotkeyHook (Shell) → FenceHost (UI) → HWND_TOPMOST + 动画
```

---

## 4. 项目结构

```
src/
├── DesktopFences.Core/
│   ├── Models/
│   │    ├── FenceDefinition.cs
│   │    ├── LayoutSnapshot.cs
│   │    ├── ClassificationRule.cs
│   │    ├── AppSettings.cs
│   │    └── TabStyle.cs
│   └── Services/
│        ├── IRuleEngine.cs / RuleEngine.cs
│        ├── ILayoutStore.cs / JsonLayoutStore.cs
│        ├── JsonFileStore.cs        # 原子 JSON 读/写助手
│        ├── PageManager.cs / SnapshotManager.cs
│        └── SnapEngine.cs
│
├── DesktopFences.Shell/
│   ├── Interop/
│   │    ├── NativeMethods.cs        # Win32 P/Invoke 声明
│   │    ├── HotkeyHost.cs           # 全局热键注册宿主
│   │    ├── LowLevelKeyboardHook.cs # WH_KEYBOARD_LL RAII 包装
│   │    ├── LowLevelMouseHook.cs    # WH_MOUSE_LL RAII 包装
│   │    └── WindowClassUtil.cs      # 桌面/任务栏类名识别
│   ├── Desktop/
│   │    ├── DesktopEmbedManager.cs
│   │    ├── PeekManager.cs / QuickHideManager.cs
│   │    ├── SearchHotkeyManager.cs / PageSwitchManager.cs
│   │    ├── ShellIconExtractor.cs
│   │    └── DesktopFileMonitor.cs / FolderPortalWatcher.cs
│
├── DesktopFences.UI/
│   ├── Controls/
│   │    ├── FencePanel.xaml(.cs)
│   │    ├── FenceHost.xaml(.cs)
│   │    └── Settings/
│   ├── ViewModels/
│   │    ├── ViewModelBase.cs
│   │    ├── FencePanelViewModel.cs
│   │    └── FileItemViewModel.cs
│   └── Themes/
│        ├── DarkTheme.xaml
│        ├── TabStyles.xaml
│        └── Icons.xaml
│
└── DesktopFences.App/
     ├── App.xaml(.cs)
     ├── FenceHostExtensions.cs    # FenceHost 集合查询扩展
     └── StartupManager.cs
```

---

## 5. 关键 Win32 API

所有 P/Invoke 集中在 `DesktopFences.Shell` 项目：

| API | 用途 |
|-----|------|
| `SetWindowLongPtr(GWL_EXSTYLE)` | 应用 WS_EX_TOOLWINDOW \| WS_EX_NOACTIVATE |
| `SetWindowPos(HWND_BOTTOM / HWND_TOPMOST)` | z-order 管理 |
| `SetWindowsHookEx(WH_KEYBOARD_LL)` | 检测 Win+D |
| `SetWinEventHook(EVENT_SYSTEM_FOREGROUND)` | 前台窗口变化监听 |
| `RegisterHotKey` | Peek 热键（Win+Space） |
| `SHGetFileInfo` | 文件图标提取 |
| `SHChangeNotifyRegister` | 桌面文件变更通知 |
| `IContextMenu` COM | Shell 右键菜单 |

---

## 6. 开发规范

- UI 层使用 MVVM 模式；ViewModel 在 `ViewModels/`，View 在 `Controls/`
- Core 项目零平台依赖 — 所有 Win32 调用在 Shell 项目
- Shell 项目启用 `AllowUnsafeBlocks` 用于 P/Invoke
- 数据以 JSON 格式持久化到 `%APPDATA%\DesktopFences\`
- 原子写入（写临时文件 → rename）防止数据损坏
- 图标缓存按文件扩展名（非路径）缓存，LRU 上限 500
- FileSystemWatcher + 定时全量扫描（30 秒）保证可靠性

---

## 7. 通用抽象工具

为消除 Manager 类、持久化层、集合查询的样板代码，引入了一组小而专注的工具，每个 Manager 只保留自己独有的业务逻辑：

### 7.1 Shell 层 Interop 工具（internal）

| 工具 | 职责 | 替代 |
|------|------|------|
| `HotkeyHost` | 持有隐藏 message-only 窗口，注册多个 `WM_HOTKEY` 并按 ID 分发回调；Dispose 自动反注册全部热键 | PeekManager / SearchHotkeyManager / PageSwitchManager 中重复的 `HwndSource + AddHook + RegisterHotKey + UnregisterHotKey` 模板 |
| `LowLevelKeyboardHook` | `SetWindowsHookEx(WH_KEYBOARD_LL)` RAII 包装，强引用 callback delegate 避免 GC | DesktopEmbedManager 内联钩子安装/卸载代码 |
| `LowLevelMouseHook` | `SetWindowsHookEx(WH_MOUSE_LL)` RAII 包装；提供 `CallNext` 转发 | QuickHideManager / PageSwitchManager 中重复的鼠标钩子样板 |
| `WindowClassUtil` | 集中桌面/任务栏类名常量；提供 `IsDesktopWindow / IsDesktopOrTaskbarWindow / IsDesktopAtPoint` | DesktopEmbedManager / QuickHideManager / PageSwitchManager 三处重复的 `Progman/WorkerW/...` 类名判断 |

### 7.2 Core 层持久化工具

| 工具 | 职责 | 替代 |
|------|------|------|
| `JsonFileStore` | 静态 `ReadAsync<T>(path, opts)` / `WriteAtomicAsync<T>(path, value, opts)` | `JsonLayoutStore` 中 14 处重复的 read-or-default 与 temp-file-then-rename 写入 |

### 7.3 App 层集合扩展

| 扩展方法 | 说明 |
|----------|------|
| `IEnumerable<FenceHost>.AllTabs()` | 展平所有 tab ViewModel |
| `IEnumerable<FenceHost>.AllDefinitions()` | 展平所有 tab 模型（`FenceDefinition` 列表） |
| `IEnumerable<FenceHost>.FindTabById(Guid)` | 按 ID 查 tab |
| `IEnumerable<FenceHost>.FindHostByTabId(Guid)` | 按 tab ID 查宿主窗口 |
| `IEnumerable<FenceHost>.ContainsFile(string)` | 任一 fence 是否已包含路径 |

替代 `App.xaml.cs` 中 10+ 处 `_fenceWindows.SelectMany(f => f.Tabs.Select(...))` 与三层嵌套 `foreach`。

### 7.4 设计原则

- 工具均为 internal — 不破坏 public API、不影响插件生态。
- Manager 类对外行为完全保持不变（事件签名、`Start/Stop/Dispose` 语义未变）；仅内部实现复用通用工具。
- Win32 类名常量只允许在 `WindowClassUtil` 中出现；新增桌面识别需求只改一个地方。
- JSON 持久化通过 `JsonFileStore` 路由 — 新增持久化文件无需复制 13 行 IO 模板。
