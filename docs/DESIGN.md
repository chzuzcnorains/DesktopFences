# DesktopFences 设计文档

## 1. 项目概述

DesktopFences 是一款 Windows 桌面整理工具，对标 Stardock Fences 6，通过桌面分区容器（Fence）对桌面文件/快捷方式进行分组、自动分类和布局管理。

**核心约束**：Fence 窗口必须在 `Win+D`（显示桌面）后仍然可见，这是产品的基本要求。

**技术栈**：C# / .NET 9 / WPF + Win32 Interop

---

## 2. 桌面嵌入方案分析与选择

### 2.1 方案对比

桌面嵌入是本项目最关键的技术决策。经调研有以下方案：

| 方案 | 原理 | Win+D 表现 | 交互性 | 复杂度 | 代表项目 |
|------|------|-----------|--------|--------|---------|
| **A: WorkerW 子窗口** | `SendMessage(Progman, 0x052C)` 触发 Explorer 创建 WorkerW，`SetParent` 将窗口挂为子窗口 | 随桌面一起显示/隐藏，正确 | **无法接收鼠标/键盘输入**（致命缺陷） | 低 | Lively Wallpaper（壁纸场景够用）|
| **B: WS_EX_TOPMOST 浮窗** | 普通 WPF 窗口 + `WS_EX_TOOLWINDOW` + `WS_EX_TOPMOST` | 始终最前，Win+D 后可见 | **完全交互** | 低 | — |
| **C: Explorer Hook (DLL注入)** | `WH_GETMESSAGE` Hook Explorer 进程，拦截 `WM_USER+83`（ShowDesktop 消息），动态切换 Topmost | Win+D 时动态置顶 | 完全交互 | 高 | Stardock Fences（推测） |
| **D: 混合方案（推荐）** | 普通 WPF 浮窗 + `WS_EX_TOOLWINDOW`（隐藏任务栏/Alt+Tab）+ 低级键盘钩子检测 Win+D + 文件系统监控桌面状态变化 | Win+D 后自动恢复显示 | 完全交互 | 中 | NoFences, DesktopFences (开源) |

### 2.2 选定方案：D - 混合浮窗方案 ✅ 已验证

**理由**：
- 方案 A 无法交互，对 Fences 工具是致命缺陷（需要拖放、点击、右键菜单）
- 方案 B 始终在最前面会遮挡其他窗口（用户体验差）
- 方案 C 需要注入 Explorer 进程，不稳定且维护成本高
- **方案 D** 在开源项目（NoFences, limbo666/DesktopFences）中已验证可行

**验证状态**：Demo 已通过验证（2026-03-03），具体行为正确：
- 正常状态：Fence 窗口在桌面之上、其他应用窗口之下
- Win+D 后：Fence 窗口仍然可见
- 用户切换到其他窗口后：Fence 自动回到窗口下方

**实现细节**：

```
窗口层级设计（正常态）：
┌─────────────────────────────────┐
│  普通应用窗口（z-order 正常）      │
├─────────────────────────────────┤
│  Fence 窗口（HWND_BOTTOM）       │  ← 正常模式：在所有应用窗口最底层，但仍在桌面之上
├─────────────────────────────────┤
│  桌面图标层 (SysListView32)       │
├─────────────────────────────────┤
│  桌面壁纸 (WorkerW / Progman)    │
└─────────────────────────────────┘

Z-Order 状态机：
  BOTTOM ──(Win+D 检测)──→ 延迟 300ms ──→ TOPMOST
  TOPMOST ──(EVENT_SYSTEM_FOREGROUND: 用户激活其他窗口)──→ BOTTOM

窗口样式：
  WS_EX_TOOLWINDOW  — 从任务栏和 Alt+Tab 隐藏
  WS_EX_NOACTIVATE  — 点击窗口时不激活（不抢焦点）

Win+D 完整流程：
1. WH_KEYBOARD_LL 低级键盘钩子检测 Win+D 组合键
2. 延迟 300ms 等待 Explorer 完成 ShowDesktop 动画
3. SetWindowPos(HWND_TOPMOST) 将所有 Fence 窗口临时置顶
4. SetWinEventHook(EVENT_SYSTEM_FOREGROUND) 持续监听前台窗口变化
5. 用户激活任何非 Fence 窗口 → SetWindowPos(HWND_BOTTOM) 恢复

Peek 模式（Win+Space）：
1. 全局热键捕获（RegisterHotKey）
2. 所有 Fence 窗口设为 HWND_TOPMOST + 提高透明度/动画
3. 再次按下或 Escape 退出 Peek，恢复 HWND_BOTTOM
```

**关键 Win32 API**：
- `SetWindowLongPtr(GWL_EXSTYLE, WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE)` — 隐藏于任务栏/Alt+Tab 且不抢焦点
- `SetWindowPos(HWND_BOTTOM)` — 正常态：桌面之上、窗口之下
- `SetWindowPos(HWND_TOPMOST)` — Win+D 后临时置顶
- `SetWindowsHookEx(WH_KEYBOARD_LL)` — 全局键盘钩子检测 Win+D
- `SetWinEventHook(EVENT_SYSTEM_FOREGROUND)` — 监听前台窗口变化，自动恢复 HWND_BOTTOM
- `RegisterHotKey` — 注册 Peek 热键
- `SHGetFileInfo` / `IExtractIcon` — 提取文件图标
- `SHChangeNotifyRegister` — Shell 变更通知（桌面文件增删）

---

## 3. 系统架构

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
│  │ ─LayoutSnap  │ │  规则匹配)   │ │  布局快照)     │ │
│  │ ─ClassRule   │ │             │ │                │ │
│  │ ─DesktopPage │ └─────────────┘ └────────────────┘ │
│  └──────────────┘                                     │
└──────────────────────────────────────────────────────┘
```

### 3.1 层级职责

| 层 | 项目 | 职责 | 依赖 |
|----|------|------|------|
| **Core** | DesktopFences.Core | 数据模型、规则引擎、布局持久化。纯 C#，无平台依赖 | 无 |
| **Shell** | DesktopFences.Shell | Win32 P/Invoke 封装：桌面嵌入、热键钩子、Shell 图标提取、文件系统监控、拖放 COM | Core |
| **UI** | DesktopFences.UI | WPF 控件：FencePanel、FenceHost 窗口、设置界面。MVVM 模式 | Core, Shell |
| **App** | DesktopFences.App | 应用入口、DI 容器、托盘图标、启动管理 | Core, Shell, UI |
| **Tests** | DesktopFences.Core.Tests | Core 层单元测试（规则引擎、布局序列化） | Core |

### 3.2 关键数据流

```
桌面文件变更 → FileMonitor (Shell) → RuleEngine (Core) → FencePanel 更新 (UI)
                                            ↓
                                     LayoutStore (Core) → fences.json (磁盘)

用户拖放文件 → DragDropHelper (Shell) → FenceViewModel (UI) → LayoutStore (Core)

Win+D 按下 → HotkeyHook (Shell) → DesktopEmbedManager (Shell) → SetWindowPos (所有 FenceHost)

Peek 触发 → HotkeyHook (Shell) → FenceHost (UI) → HWND_TOPMOST + 动画
```

---

## 4. 功能模块详细设计

### 4.1 Fence 容器 (FencePanel)

**WPF 控件结构**：
```xml
<FencePanel>
  ├─ <TitleBar>              <!-- 标题栏：标题文字、折叠按钮、Tab 标签 -->
  │    ├─ <TextBlock />      <!-- Fence 名称 -->
  │    ├─ <TabStrip />       <!-- 多 Tab 合并时显示 -->
  │    └─ <RollupButton />   <!-- 折叠/展开 -->
  │
  ├─ <IconArea>              <!-- 文件图标区域 -->
  │    ├─ <VirtualizingWrapPanel />  <!-- 图标视图（默认） -->
  │    ├─ <VirtualizingStackPanel /> <!-- 列表/详情视图 -->
  │    └─ <ScrollViewer />
  │
  └─ <ResizeGrips>           <!-- 八向调整大小手柄 -->
       ├─ Top/Bottom/Left/Right
       └─ TopLeft/TopRight/BottomLeft/BottomRight
</FencePanel>
```

**交互行为**：
- **拖动标题栏**：移动 Fence 位置（带 Snap 吸附逻辑）
- **拖动边缘**：调整 Fence 大小
- **点击标题栏收起箭头（▲/▼）**：Rollup 折叠/展开（只显示标题栏，高度缩小到 ~32px）
- **鼠标悬停折叠态**：展开 Fence（可配置为 click-to-open）
- **右键标题栏**：Fence 设置菜单（重命名、颜色、删除、规则配置）
- **右键文件图标**：Shell 原生右键菜单（通过 IContextMenu COM 接口）
- **双击文件图标**：ShellExecute 打开文件
- **拖入文件**：从 Explorer / 桌面拖入文件到 Fence
- **拖出文件**：从 Fence 拖出文件到 Explorer / 桌面 / 其他 Fence

### 4.2 Snap 吸附系统

```
吸附目标：
  ├─ 屏幕边缘（上下左右）
  ├─ 其他 Fence 的边缘（上下左右对齐）
  └─ 网格线（可配置间距，如 8px 或 16px）

吸附算法：
  1. 拖动时计算当前 Fence 四条边的位置
  2. 遍历所有吸附目标，找到距离 < threshold (默认 10px) 的边
  3. 将位置修正到吸附点
  4. 同时吸附多条边（如左边吸附屏幕边缘 + 上边吸附另一个 Fence 底部）
  5. 按住 Alt 拖动时临时禁用吸附
```

### 4.3 自动分类规则引擎

```
规则优先级（数字越小越优先）：
  Priority 1: 用户手动放入的文件 → 不受规则影响
  Priority 10: 扩展名规则 → .exe/.lnk → "应用程序" Fence
  Priority 20: 名称 Glob 规则 → report*.docx → "报告" Fence
  Priority 30: 日期范围规则 → 本周创建 → "最近文件" Fence
  Priority 40: 大小范围规则 → >100MB → "大文件" Fence

规则评估流程：
  1. FileMonitor 检测到桌面新增文件
  2. 按 Priority 排序遍历所有 enabled 规则
  3. 第一个匹配的规则决定目标 Fence
  4. 如果无规则匹配 → 文件留在桌面（不移入任何 Fence）
  5. 规则冲突时：最高优先级（数字最小）胜出

条件匹配实现：
  - Extension: string.Split(',') + 逐个比较 Path.GetExtension()
  - NameGlob: 转换为 Regex（* → .*, ? → .）+ Regex.IsMatch()
  - DateRange: File.GetCreationTime() / GetLastWriteTime() 范围比较
  - SizeRange: FileInfo.Length 范围比较
  - Regex: 直接 Regex.IsMatch(fileName)
```

### 4.4 Rollup 折叠

```
状态机：
  Expanded ──(点击收起箭头 ▲)──→ RolledUp
  RolledUp ──(鼠标悬停 hover_delay ms)──→ PeekExpanded (临时展开)
  PeekExpanded ──(鼠标离开)──→ RolledUp
  RolledUp ──(点击展开箭头 ▼)──→ Expanded

UI 布局：
  标题栏右侧按钮区：[▲/▼ 收起/展开] [⋯ 菜单]
  Tab 条右侧按钮区：[▲/▼ 收起/展开] [⋯ 菜单]
  注：已移除双击标题栏触发折叠的行为，避免与快速 Tab 切换冲突

动画：
  折叠：Height 从当前值动画到 RolledUpHeight (32px)，EaseOut 200ms
  展开：Height 从 RolledUpHeight 动画到保存的展开高度，EaseOut 200ms
  临时展开：同展开动画，但鼠标离开后自动折回
```

### 4.5 Peek 快速预览

```
触发：Win + Space（全局热键，通过 RegisterHotKey）
行为：
  1. 所有 FenceHost 窗口 → SetWindowPos(HWND_TOPMOST)
  2. 背景模糊效果（可选，通过 DWM Acrylic/Mica）
  3. Fence 窗口播放淡入动画
  4. 用户可在 Peek 状态下拖放文件到当前活动窗口
  5. 再次 Win+Space 或 Escape → 退出 Peek，恢复 z-order

与 Fences 6 对齐：
  - 也可通过任务栏托盘图标单击触发
  - Peek 时支持拖放文件到其他应用程序（核心生产力功能）
```

### 4.6 Folder Portal

```
原理：
  Fence 可以绑定到一个文件夹路径，实时显示该文件夹内容。
  不移动/复制文件，只是"镜像"显示。

实现：
  1. FenceDefinition 新增 PortalPath 属性
  2. 当 PortalPath 非空时，Fence 进入 Portal 模式
  3. 使用 FileSystemWatcher 监控目标文件夹
  4. 文件增删改 → 自动刷新 Fence 内容
  5. 双击文件 → ShellExecute 打开
  6. 支持在 Portal 内进入子文件夹（面包屑导航）
  7. 支持云存储文件夹（OneDrive / Dropbox 等本地同步目录）
```

### 4.7 桌面分页 (Desktop Pages) — 已禁用

```
设计变更（2026-03）：
  自定义分页功能已禁用。分页交由 Windows 虚拟桌面原生管理。
  原因：自定义鼠标滚轮分页切换会导致面板被动画滑出屏幕消失（bug 1423）。

当前行为：
  - 所有 Fence 面板始终可见，归属于单一默认页面
  - 鼠标滚轮在面板上滚动 → 滚动面板内部文件列表内容
  - Windows 虚拟桌面切换 → Windows 原生管理 Fence 窗口的显示/隐藏
  - 托盘菜单中已移除"桌面分页"子菜单
  - PageSwitchManager 仍保留但不启动（不注册热键、不安装鼠标钩子）
  - PageManager 仅用于数据持久化兼容，所有 Fence 归到 Page 0
```

### 4.8 多显示器支持

```
策略：
  1. 启动时枚举所有显示器 (Screen.AllScreens)
  2. 每个 Fence 记录所属 MonitorIndex
  3. Fence 拖动时不能跨越屏幕边界（吸附到屏幕边缘）
  4. 显示器配置变化（插拔）→ 触发布局重新计算
     - Fences 5.5+ 的方案：按显示器配置保存独立布局
     - 配置 hash = Screen count + resolutions + DPI
     - 相同配置 → 恢复对应布局
     - 新配置 → 智能迁移（按比例缩放位置）

DPI 处理：
  - 每个 FenceHost 窗口感知所在显示器的 DPI
  - 使用 PerMonitorDpiAware 模式
  - 窗口跨 DPI 边界时触发 WM_DPICHANGED → 重新布局
```

### 4.9 Quick Hide 快速隐藏

```
触发：双击桌面空白区域
行为：
  1. 检测双击位置不在任何 Fence 窗口内
  2. 所有 Fence 窗口播放淡出动画 (Opacity 1→0, 200ms)
  3. 动画完成后 Hide() 所有 FenceHost
  4. 再次双击桌面空白区域 → 淡入动画恢复显示
  5. 状态保存：可排除特定 Fence（如"固定"的 Fence 不隐藏）

实现难点：
  - 需要全局鼠标钩子 (WH_MOUSE_LL) 检测桌面双击
  - 区分"桌面空白区域"和"Fence 窗口内"的点击
  - 避免误触发（和正常双击打开文件冲突）
```

### 4.10 Shell 右键菜单集成

```
目标：在文件/桌面右键菜单中添加 "Move to Fence..." 选项

实现方式：
  方案 1（推荐）：Windows 11 Sparse Package + COM Shell Extension
    - 注册 IExplorerCommand 实现
    - 通过 Sparse Package 获取 Shell Extension 权限
    - .NET 8+ 支持 COM Source Generator

  方案 2：经典 COM Shell Extension (C++ DLL)
    - 实现 IContextMenu + IShellExtInit
    - 需要单独的 C++ 项目 (FencesMenu64.dll)
    - 通过 Named Pipe / Memory-Mapped File 与主进程通信

  方案 3（MVP 阶段）：不做 Shell Extension
    - 仅支持从 Fence 内右键操作
    - 降低初始复杂度
```

### 4.11 系统托盘

```
功能菜单：
  ├─ 显示/隐藏所有 Fence
  ├─ Peek 桌面
  ├─ ───────────────
  ├─ 新建 Fence
  ├─ 布局快照
  │    ├─ 保存当前布局
  │    ├─ [快照1] [快照2] ...
  │    └─ 管理快照...
  ├─ ───────────────
  ├─ 设置
  └─ 退出
```

---

## 5. 数据持久化

### 5.1 存储格式

使用 JSON 文件存储，位于 `%APPDATA%\DesktopFences\`：

```
%APPDATA%\DesktopFences\
  ├─ fences.json           # 当前 Fence 定义和文件列表
  ├─ rules.json            # 自动分类规则
  ├─ settings.json         # 用户设置（热键、外观、行为）
  ├─ snapshots/
  │    ├─ {guid}.json      # 每个布局快照一个文件
  │    └─ ...
  └─ monitor-layouts/
       ├─ {config-hash}.json  # 按显示器配置保存的布局
       └─ ...
```

### 5.2 保存策略

- **自动保存**：Fence 位置/大小变更后 debounce 2 秒自动保存
- **即时保存**：文件增删、规则变更即时保存
- **快照保存**：用户手动触发，完整序列化当前状态
- **原子写入**：写入临时文件 → rename 覆盖，防止写入中途崩溃导致数据丢失

---

## 6. 性能考量

| 场景 | 优化策略 |
|------|---------|
| 大量文件图标渲染 | `VirtualizingWrapPanel`，只渲染可见区域 |
| 图标提取 | 异步 + LRU 缓存（按文件扩展名缓存，不按路径） |
| 文件系统监控 | `FileSystemWatcher` + `SHChangeNotifyRegister` 双重监控，debounce 合并事件 |
| 拖放大量文件 | 异步 IO，UI 线程不阻塞 |
| Peek 动画 | WPF 硬件加速动画（`CompositionTarget`） |
| 启动速度 | 延迟加载图标（先显示 Fence 框架，图标异步填充） |
| 内存占用 | 图标缓存上限 500 个，LRU 淘汰 |

---

## 7. 技术风险

| 风险 | 影响 | 缓解措施 |
|------|------|---------|
| Win+D 检测不可靠（键盘钩子可能被安全软件拦截） | Fence 在 ShowDesktop 后消失 | 多重检测：键盘钩子 + 定时器轮询前台窗口 + Shell 通知 |
| Explorer 重启导致桌面层级重建 | Fence 窗口丢失 z-order 定位 | 监听 Explorer 进程重启事件，自动重新初始化 |
| 高 DPI 混合显示器下 WPF 渲染模糊 | 文字/图标在非主显示器上模糊 | 启用 `PerMonitorDpiAware`，处理 `WM_DPICHANGED` |
| FileSystemWatcher 丢事件 | 桌面文件增删未被 Fence 捕获 | FSWatcher + 定时全量扫描对账 (每 30 秒) |
| 与其他桌面增强工具冲突 | 窗口层级混乱 | 提供"兼容模式"选项（不做 z-order 管理） |

---

## 8. Phase 5 实现记录

### 8.1 布局快照管理

**已实现组件**：
- `SnapshotManager`（Core 服务）— 快照创建/恢复/重命名/删除，JSON 序列化深拷贝
- 托盘菜单 `Layout Snapshots` 子菜单 — 保存当前布局、按快照恢复、删除快照
- 快照保存时记录 `ScreenConfiguration`（含 ConfigHash），恢复时深拷贝 FenceDefinition 列表

**数据流**：
```
用户触发 → SnapshotManager.CreateSnapshotAsync() → JsonLayoutStore.SaveSnapshotAsync()
                                                        ↓
                                                  snapshots/{guid}.json
恢复快照 → SnapshotManager.RestoreSnapshot() → 深拷贝 FenceDefinition → 关闭旧窗口 → 重建
```

### 8.2 桌面分页

**状态：已禁用**（2026-03，bug 1423）

自定义分页功能已禁用，改由 Windows 虚拟桌面原生管理。`PageSwitchManager` 保留但不启动，`PageManager` 仅用于数据持久化兼容（所有 Fence 归到 Page 0）。分页切换动画、托盘分页菜单、鼠标滚轮/键盘热键切换均已移除。

### 8.3 多显示器支持

**已实现组件**：
- `MonitorManager`（Shell）— 显示器枚举、SHA256 配置哈希（16 字符）、热插拔检测
- `SystemEvents.DisplaySettingsChanged` — 监听显示器配置变化
- 按 ConfigHash 独立保存/恢复布局（`monitor-layouts/{hash}.json`）
- `ClampToMonitor()` — Fence 范围限制到所属显示器工作区（加载时、显示器变化时均执行）
- `SpawnFenceWindow()` 启动时自动调用 `ClampToMonitor` 校正坐标，防止 Fence 窗口落在屏幕外不可见
- `MigrateLayout()` — 配置变化时按比例缩放迁移 Fence 位置

**配置哈希算法**：
```
输入: "{ScreenCount}|{Width}x{Height}@{X},{Y}:{P/S}|..."（按设备名排序）
输出: SHA256 前 16 字符（HEX）
```

**热插拔处理流程**：
```
DisplaySettingsChanged → 计算新 ConfigHash → 与旧值比较
  → 匹配已有布局: 恢复该布局
  → 新配置: ClampToMonitor 限制 Fence 到新屏幕范围
  → 保存旧配置布局以备后用
```

### 8.4 Folder Portal

**已实现组件**：
- `FenceDefinition.PortalPath`（Core 模型扩展）
- `FolderPortalWatcher`（Shell）— 文件夹监控、子文件夹导航、面包屑路径、内容变更事件
- `FencePanelViewModel.IsPortalMode` / `PortalDisplayPath` — Portal 模式标识与路径显示

**Portal 操作界面**：
- **创建入口**：系统托盘右键菜单 → "新建文件夹映射 Fence..." → `OpenFolderDialog` 选择文件夹
- **设置/更改映射**：Fence 标题栏右键菜单 → "设为文件夹映射..." / "更改映射文件夹 (路径)" → `OpenFolderDialog`
- **取消映射**：Fence 标题栏右键菜单 → "取消文件夹映射" → 清除 PortalPath、移除标题 📁 前缀
- **视觉指示**：Portal Fence 标题自动显示 `📁 文件夹名`，标题栏 Tooltip 显示完整路径
- **空状态提示**：Portal 模式下显示 "右键标题栏可更改映射文件夹"（而非 "拖放文件到此处"）
- **事件流**：`FencePanel.PortalModeChanged` → App.xaml.cs 启动/停止 `FolderPortalWatcher`

**Portal 模式行为**：
```
FolderPortalWatcher.Watch(portalPath)
  → FileSystemWatcher 监控创建/删除/重命名/修改
  → 300ms 去抖后触发 ContentsChanged 事件
  → App.SyncPortalContents(): 增量同步（新增文件加入 VM，移除已删除文件）
  → 图标自动加载
```

**面包屑导航**：
- `NavigateToSubfolder(name)` — 切换到子文件夹
- `NavigateUp()` — 返回父目录
- `GetBreadcrumbs()` — 获取从根到当前路径的完整路径段列表
- 支持网络路径（UNC 路径）和云同步目录（OneDrive、Dropbox 本地目录）

---

## 9. Phase 6 实现记录

### 9.1 动画系统

**淡入/淡出**：
- `FencePanel.AnimateFadeIn()` — `DoubleAnimation` Opacity 0→1，250ms `CubicEase(EaseOut)`，作用在 FenceHost 窗口上
- `FencePanel.AnimateFadeOut(Action? onComplete)` — Opacity 1→0，200ms，完成回调触发 `Close()`
- `FenceHost.AnimateClose()` — 防重入（`_isClosing` 标志），委托 `FenceContent.AnimateFadeOut`

**拖入脉冲动画**：
- `FencePanel.AnimateDropPulse()` — `ScaleTransform` 作用在 `FenceBorder` 上，Scale 1.0→1.02→1.0，150ms 两阶段动画
- `OnDragOver` 蓝色边框高亮，`OnDrop` 重置边框并触发脉冲

### 9.2 主题与外观

**每个 Fence 独立主题**：
- `FenceDefinition` 扩展字段：`BackgroundColor?`，`TitleBarColor?`，`TextColor?`，`Opacity`
- `FencePanelViewModel` 对应属性：`BackgroundColor`，`TitleBarColor`，`TextColor`，`FenceOpacity`，setter 双向同步 Model

**主题应用逻辑**（`FencePanel.xaml.cs`）：
```csharp
public void ApplyTheme(FencePanelViewModel vm)
{
    // BrushConverter.ConvertFromString() + try/catch 安全转换
    // 有值时覆盖 FenceBorder/TitleBarBorder/TitleText 的笔刷和透明度
}
public void ApplyDefaultTheme(string bgColor, string titleColor, string textColor, double fontSize)
{
    // 仅在 ViewModel 对应属性为 null 时使用全局默认值
}
```

**WPF 元素命名**（`FencePanel.xaml`）：
- `x:Name="FenceBorder"` — 外层 Border，Opacity 绑定 `FenceOpacity`
- `x:Name="TitleBarBorder"` — 标题栏 Border
- `x:Name="TitleText"` — 标题栏 TextBlock

### 9.3 全局设置（AppSettings）

**模型**（`DesktopFences.Core/Models/AppSettings.cs`）：
```
DefaultFenceColor      = "#CC1E1E2E"
DefaultTitleBarColor   = "#44FFFFFF"
DefaultTextColor       = "#DDEEEEEE"
DefaultOpacity         = 1.0
TitleBarFontSize       = 13
SnapThreshold          = 10
QuickHideEnabled       = true
StartWithWindows       = false
StartMinimized         = true
CompatibilityMode      = false
DebugLogging           = false
```

**持久化**：通过 `ILayoutStore.LoadSettingsAsync()` / `SaveSettingsAsync()` 读写 `%APPDATA%\DesktopFences\settings.json`，原子写入。

**设置窗口**（`SettingsWindow.xaml`，统一窗口，TabControl 切换）：
- **Tab 1 "常规设置"**：四分组 — 外观（颜色文本框 + 透明度/字体滑块）/ 行为（Snap 阈值滑块 + Quick Hide 复选框）/ 启动（开机自启 + 启动最小化）/ 高级（兼容模式 + 调试日志）
- **Tab 2 "分类规则"**：双面板 — 左侧规则列表 + 添加/删除按钮，右侧编辑表单（名称、启用、优先级、匹配方式、匹配模式、目标 Fence）
- `SettingsSaved` 事件 → `App.OnSettingsSaved()` → 即时应用主题 + 更新 StartupManager + 更新 QuickHideManager
- `RulesSaved` 事件 → `App._rules = newRules` → `SaveRulesAsync()` + `ReEvaluateClassifiedFiles()`
- 托盘菜单"分类规则..."直接打开设置窗口并定位到分类规则 Tab（`SelectTab(1)`）

### 9.4 快捷搜索

**热键注册**（`SearchHotkeyManager.cs`）：
- `HwndSource` 隐藏窗口接收 `WM_HOTKEY`
- `RegisterHotKey(MOD_CONTROL | MOD_NOREPEAT, VK_OEM_3 = 0xC0)` → Ctrl+\`
- `SearchRequested` 事件触发 `App.ShowSearchWindow()`

**搜索窗口**（`SearchWindow.xaml`）：
- 深色主题（#1E1E2E 背景，AllowsTransparency，WindowStyle=None）
- `SearchResult` 数据模型：`FilePath`, `DisplayName`, `FenceName`, `FenceId`, `Icon`
- 实时过滤：TextChanged → LINQ `.Where(x => x.DisplayName.Contains(..., OrdinalIgnoreCase))`
- 键盘导航：Down 键聚焦列表，Enter 激活选中结果，Esc 关闭
- 失焦自动关闭（`Deactivated` 事件）
- 淡入动画（Opacity 0→1，150ms）

### 9.5 性能优化实现

| 优化项 | 实现方式 |
|--------|---------|
| 图标异步加载 | `{Binding Icon, IsAsync=True}` — WPF 延迟绑定 |
| 列表虚拟化 | `ListBox` + `VirtualizingPanel.IsVirtualizing=True` + `VirtualizationMode=Recycling` |
| 图标缓存 | 扩展名级 LRU，上限 500，`ConcurrentDictionary` + `LinkedList` |
| 写入安全 | 临时文件原子写入 (`{filename}.tmp` → rename) |
| 事件去抖 | FSWatcher 500ms debounce，自动保存 2s debounce |

### 9.6 打包分发

**发布配置**（`win-x64-self-contained.pubxml`）：
```xml
<RuntimeIdentifier>win-x64</RuntimeIdentifier>
<SelfContained>true</SelfContained>
<PublishSingleFile>true</PublishSingleFile>
<PublishReadyToRun>true</PublishReadyToRun>
<EnableCompressionInSingleFile>true</EnableCompressionInSingleFile>
<TrimMode>partial</TrimMode>
```

**发布命令**：
```bash
dotnet publish src/DesktopFences.App -c Release /p:PublishProfile=win-x64-self-contained
```

**开机自启**（`StartupManager.cs`）：
- 写入 `HKCU\SOFTWARE\Microsoft\Windows\CurrentVersion\Run\DesktopFences`
- 值为 `"<exePath>"` 带引号，处理路径含空格情况
- `SetEnabled(bool)` 统一接口，由 `SettingsSaved` 事件驱动

---

## 10. 规则编辑器与默认分类配置

### 10.1 规则编辑器（已合并至 SettingsWindow）

规则编辑器已从独立的 `RuleEditorWindow` 合并到 `SettingsWindow` 的第二个 Tab 页。详见 9.3 节"Tab 2 分类规则"。

**事件流**：
```
RulesListBox.SelectionChanged → PopulateRuleForm(rule)
TxtRuleName.TextChanged → ApplyFormToRule() + RefreshRuleList()
其他字段变化 → ApplyFormToRule()
CboMatchType.SelectionChanged → ApplyFormToRule() + UpdatePatternHint()
  （IsDirectory 时隐藏 PatternRow，显示"匹配所有文件夹"提示）
BtnSave → 触发 SettingsSaved + RulesSaved → App 即时应用
```

**打开入口**：托盘菜单"分类规则..."→ `ShowSettings(1)` 直接跳转到规则 Tab

### 10.2 IsDirectory 规则类型

`RuleMatchType.IsDirectory`（新增第 6 种）— 通过 `Directory.Exists(filePath)` 判断路径是否为文件夹。编辑器中选此类型时自动隐藏 PatternRow（无需填写模式）。

### 10.3 默认分类配置（首次运行）

首次启动（`fences.json` 不存在）时，`CreateDefaultConfiguration()` 自动创建 6 个 Fence + 6 条规则并持久化：

| Fence 名称 | 位置 | 规则类型 | 匹配内容 |
|-----------|------|---------|---------|
| 程序及快捷方式 | (20, 20) | Extension | .exe,.lnk,.url,.bat,.cmd,.ps1,.msi |
| 文件夹 | (340, 20) | IsDirectory | — |
| 文档 | (20, 240) | Extension | .doc,.docx,.pdf,.txt,.xls,.xlsx,.ppt,.pptx,.md,.rtf,.csv 等 |
| 视频 | (340, 240) | Extension | .mp4,.mkv,.avi,.mov,.wmv,.flv,.webm,.ts,.rmvb 等 |
| 音乐 | (20, 460) | Extension | .mp3,.wav,.flac,.aac,.ogg,.m4a,.wma,.opus,.ape 等 |
| 图片 | (340, 460) | Extension | .jpg,.jpeg,.png,.gif,.bmp,.webp,.svg,.ico,.tiff,.heic,.raw 等 |

规则 Priority 1-6，Fence 与规则通过 `Id`（Guid）关联，保存到 `rules.json`。

---

## 11. Tab 标签组（Phase 7）

### 11.1 功能概述

FenceHost 支持将多个 Fence 合并为标签组（Tab Group），通过 Tab 切换不同 Fence 内容，类似浏览器多标签页。

**核心特性**：
- 多个 Fence 可合并到同一窗口，通过 Tab 切换
- Tab 分组状态自动持久化（TabGroupId + TabOrder）
- 重启后恢复 Tab 分组状态
- Tab 条右侧 "⋯" 菜单按钮：重命名、分离、Portal 设置、关闭
- 标题栏右侧 "⋯" 菜单按钮：重命名、Portal 设置、关闭（替代原右键菜单）

### 11.2 数据模型

**FenceDefinition 扩展字段**：
```csharp
public Guid? TabGroupId { get; set; }   // 所属标签组 ID（null 表示独立窗口）
public int TabOrder { get; set; }        // 在标签组内的顺序
```

**分组逻辑**：
- 首次合并时生成新的 `TabGroupId`
- 同一 `TabGroupId` 的 Fence 在启动时恢复到同一窗口
- `TabOrder` 决定 Tab 显示顺序

### 11.3 UI 结构

**FenceHost 窗口结构**：
```xml
<Grid Margin="4">
    <Grid.RowDefinitions>
        <RowDefinition x:Name="TabRow" Height="0" />  <!-- 单 Tab 时隐藏 -->
        <RowDefinition Height="*" />
    </Grid.RowDefinitions>

    <!-- Tab 条（2+ Tab 时显示） -->
    <Border x:Name="TabStripBorder" Grid.Row="0"
            Background="#CC1E1E2E"
            CornerRadius="8,8,0,0"
            BorderBrush="#55888888"
            BorderThickness="1,1,1,0"
            Margin="0,0,0,-1">  <!-- -1 负边框消除与 Panel 的间隙 -->
        <ItemsControl x:Name="TabStrip">
            <ItemsControl.ItemsPanel>
                <ItemsPanelTemplate>
                    <StackPanel Orientation="Horizontal" />
                </ItemsPanelTemplate>
            </ItemsControl.ItemsPanel>
        </ItemsControl>
    </Border>

    <!-- 当前激活的 FencePanel -->
    <controls:FencePanel x:Name="FenceContent" Grid.Row="1" />
</Grid>
```

**Tab 按钮样式**：
- 激活 Tab：深蓝背景（`#99446699`），浅白文字（`#FFEEEEEE`）
- 非激活 Tab：灰色背景（`#33888888`），浅灰文字（`#AACCCCCC`）
- 字号 13px（与 Panel 标题一致）
- 高度 26px，圆角底部对齐

### 11.4 Tab 可见性与标题栏

**显示规则**：
- 单 Tab（`_tabs.Count == 1`）：TabRow 高度 = 0，`ShowTitleBar = true`
- 多 Tab（`_tabs.Count > 1`）：TabRow 高度 = 28，`ShowTitleBar = false`

**FencePanel.ShowTitleBar 依赖属性**：
```csharp
public static readonly DependencyProperty ShowTitleBarProperty =
    DependencyProperty.Register(nameof(ShowTitleBar), typeof(bool), typeof(FencePanel),
        new PropertyMetadata(true));
```

XAML 中通过 DataTrigger 控制 TitleBarBorder 可见性：
```xml
<Style x:Key="TitleBarVisibilityStyle" TargetType="{x:Type Border}">
    <Setter Property="Visibility" Value="Visible"/>
    <Style.Triggers>
        <DataTrigger Binding="{Binding ShowTitleBar, RelativeSource={RelativeSource AncestorType=UserControl}}"
                     Value="False">
            <Setter Property="Visibility" Value="Collapsed"/>
        </DataTrigger>
    </Style.Triggers>
</Style>
```

### 11.5 Tab 交互

**点击切换**：
```csharp
btn.Click += (_, _) =>
{
    _activeTabIndex = idx;
    ActivatePanelForTab(idx);  // 切换 DataContext
    RefreshTabStrip();         // 重绘 Tab 样式
};
```

**"⋯" 菜单按钮**（Tab 条右侧 + 标题栏右侧）：
- Tab 条 "⋯" 按钮（`TabMenuButton`）：重命名、分离为独立 Fence、Portal 设置、关闭
- 标题栏 "⋯" 按钮（`TitleMenuButton`）：重命名、Portal 设置、关闭
- 替代了原先的 Tab 右键菜单和标题栏右键菜单，避免双击/拖拽事件冲突
- 按钮样式：透明背景，hover 时 `#33FFFFFF`，圆角 4px

> **注意**：Tab 按钮上的拖拽检测已移除（原 `PreviewMouseLeftButtonDown` / `PreviewMouseMove` / `PreviewMouseLeftButtonUp`），因为双击与拖拽检测的微小鼠标移动冲突会触发 `TabDragDropped`，导致窗口意外消失。`TabDragDropped` 事件声明保留但不再触发。

### 11.6 合并与分离逻辑

**自动合并（位置重叠）**：
```csharp
private static bool FencesOverlapSignificantly(FenceHost a, FenceHost b)
{
    // 计算交集面积
    // 若交集 / 较小窗口面积 > 0.4，则触发合并
}
```

**手动合并（拖拽 Tab）**：
- 检测落点是否在目标窗口矩形内
- 更新 `TabGroupId` 和 `TabOrder`
- 调用 `sourceHost.RemoveTab()` + `targetHost.AddTab()`

**分离为独立窗口**：
```csharp
private void DetachTab(FenceHost host, FencePanelViewModel vm)
{
    host.RemoveTab(idx);
    vm.Model.TabGroupId = null;
    vm.Model.TabOrder = 0;

    // 偏移量 = vm.Width + 50，避免立即重新合并
    vm.X = host.Left + vm.Width + 50;
    vm.Y = host.Top + 50;

    var newHost = new FenceHost(_embedManager!, vm, _iconExtractor);
    // ... 设置事件，显示窗口
}
```

### 11.7 窗口尺寸同步

**Tab 条出现/消失时调整高度**：
```csharp
public void AddTab(FencePanelViewModel vm)
{
    bool wasTabbed = _tabs.Count > 1;
    _tabs.Add(vm);
    if (!wasTabbed)
        Height += 28;  // 首次出现 Tab 条
}

public FencePanelViewModel RemoveTab(int index)
{
    bool wasTabbed = _tabs.Count > 1;
    // ...
    if (wasTabbed && _tabs.Count == 1)
        Height -= 28;  // Tab 条消失
}
```

**布局保存**：
- `InteractionEnded` 事件触发时同步所有 Tab 的 X/Y/Width/Height
- `ResizeGrip.DragCompleted` 触发 `InteractionEnded`
- 自动保存 debounce 2 秒

### 11.8 启动恢复

**SpawnFencesWithGroups 逻辑**：
```csharp
// 按 TabGroupId 分组
var grouped = definitions
    .Where(d => d.TabGroupId.HasValue)
    .GroupBy(d => d.TabGroupId!.Value)
    .ToDictionary(g => g.Key, g => g.OrderBy(d => d.TabOrder).ToList());

// 无分组 Fence 正常启动
foreach (var def in standalone)
    SpawnFenceWindow(new FencePanelViewModel(def));

// 分组 Fence：首个作为主窗口，其余作为 Tab 添加
foreach (var group in grouped.Values)
{
    var primaryVm = new FencePanelViewModel(group[0]);
    SpawnFenceWindow(primaryVm);
    var host = _fenceWindows.Last();
    for (int i = 1; i < group.Count; i++)
        host.AddTab(new FencePanelViewModel(group[i]));
}
```

---

## 12. Phase 8 Bug 修复与功能增强

### 12.1 自动整理修复

**修复的 Bug**：
- `OrganizeDesktopOnceAsync` 过滤条件语法错误（`!f.StartsWith(...).StartsWith('.')`）→ 改为 `Path.GetFileName` 正确过滤
- `IsFileAlreadyInAnyFence` 缺括号导致编译错误 → 补全
- `HideFileOnDesktop` 隐藏判断取反（`== 0` 应为 `!= 0`）→ 修正
- `OrganizeDesktopOnceAsync` 在线程池线程修改 UI 集合 → 包裹 `Dispatcher.InvokeAsync`
- `StartAutoOrganizeTimer` 在 fences/settings 加载前调用 → 移至 `LoadFencesAsync` 末尾
- 启动后无初始扫描 → 添加 `await OrganizeDesktopOnceAsync()` 在 fences 创建后立即执行

**自动整理逻辑**：
```
启动流程: LoadFencesAsync → 创建 Fence → OrganizeDesktopOnceAsync (初始扫描) → StartAutoOrganizeTimer (2s 周期)
定时流程: Timer.Elapsed → OrganizeDesktopOnceAsync → Dispatcher.InvokeAsync(扫描+分类+隐藏)
```

**托盘菜单新增**：
- 「自动整理」勾选项 — 控制 `AppSettings.AutoOrganizeEnabled`，动态启停定时器
- 「立即整理桌面」— 手动触发一次 `OrganizeDesktopOnceAsync`

### 12.2 桌面文件隐藏与自渲染机制

**设计目标**：程序运行期间隐藏原生桌面图标层，已收纳文件由 Fence 展示，未收纳文件由覆盖窗口（DesktopIconOverlay）在原始位置自行渲染；退出时还原原生桌面图标。

#### 12.2.1 SysListView32 整层隐藏

曾使用 per-file `FileAttributes.Hidden` 方案，但该方案对部分文件类型无效（图片、文档等在资源管理器开启「显示隐藏文件」时仍以半透明形式显示），因此切换到 SysListView32 整层隐藏方案（类似小智桌面）。

**实现**（`DesktopIconManager` in `DesktopFences.Shell`）：
- `FindDesktopListView()` — 查找桌面图标 ListView 窗口，兼容 `Progman → SHELLDLL_DefView → SysListView32` 和 `WorkerW → SHELLDLL_DefView → SysListView32` 两种层级
- `GetListViewHandle()` — 获取缓存的或新查找的 SysListView32 句柄
- `HideIcons()` — `ShowWindow(SysListView32, SW_HIDE)` 隐藏整个图标层 + 写 flag 文件
- `ShowIcons()` — `ShowWindow(SysListView32, SW_SHOW)` 恢复图标层 + 删 flag 文件

#### 12.2.2 跨进程图标位置读取

**实现**（`DesktopIconPositionReader` in `DesktopFences.Shell`）：

SysListView32 在 explorer.exe 进程中，需要跨进程通信读取图标位置：
1. `SendMessage(LVM_GETITEMCOUNT)` 获取图标数量
2. `VirtualAllocEx` 在 explorer 进程分配共享内存
3. 对每个图标：`WriteProcessMemory` 写入 LVITEMW → `SendMessage(LVM_GETITEMTEXTW)` 读取显示名 → `SendMessage(LVM_GETITEMPOSITION)` 读取位置 → `ReadProcessMemory` 读回结果
4. 失败时返回空列表，覆盖窗口使用自动网格定位（优雅降级）

**名称匹配**：SysListView32 显示名可能与文件名不同（如 .lnk 文件不显示扩展名），匹配策略：精确匹配 → 无扩展名匹配 → 无匹配则自动定位。

#### 12.2.3 未收纳图标覆盖窗口（DesktopIconOverlay）

**实现**（`DesktopIconOverlay` in `DesktopFences.UI`）：

全屏透明 WPF 窗口，使用 Canvas 绝对定位渲染未收纳的桌面图标：
- `WindowStyle=None`, `AllowsTransparency=True`, `Background=Transparent`
- `Canvas Background="{x:Null}"` — 空白区域点击穿透，只有图标元素可点击
- 通过 `DesktopEmbedManager.RegisterWindow` 获得与 FenceHost 相同的 z-order 管理
- DPI 处理：SysListView32 坐标为物理像素，通过 `TransformToDevice` 转换为 WPF DIP

**交互**：
- 双击打开（`ShellFileOperations.OpenFile`）
- 右键 Shell 上下文菜单（`ShellContextMenu.Show`）
- 拖拽到 Fence（`DragDrop.DoDragDrop`，Move 效果）
- **图标自由移动**：鼠标拖拽在覆盖层内部时进入手动移动模式（CaptureMouse + Canvas 定位），松手后自动吸附最近网格格位（GridCellWidth=80, GridCellHeight=96）；若目标格位已有图标则交换位置。拖拽接近窗口边缘（20px 阈值）时自动切换为 OLE DragDrop 模式以支持拖入 Fence。

#### 12.2.4 生命周期与同步

**启动流程**（`LoadFencesAsync`）：
1. 崩溃恢复检查 → 加载 Fence → 自动分类
2. `DesktopIconPositionReader.ReadAllPositions()` 读取原始位置
3. `DesktopIconManager.HideIcons()` 隐藏原生图标层
4. `CreateDesktopOverlay()` 创建覆盖窗口，显示未收纳文件

**退出流程**（`OnExit`）：关闭覆盖窗口 → `ShowIcons()` 恢复原生图标

**实时同步**：
- 文件被自动分类到 Fence → `RemoveIcon` 从覆盖层移除
- 新桌面文件未匹配规则 → `AddIcon` 添加到覆盖层（自动网格定位）
- 桌面文件被删除 → 同时从 Fence 和覆盖层移除
- 文件从 Fence 移出但仍在桌面 → `AddIcon` 重新添加到覆盖层
- 文件重命名 → 更新覆盖层图标
- 切换 Fence 可见性 → 覆盖层同步隐藏/显示

**崩溃恢复**：flag 文件 `%APPDATA%\DesktopFences\.desktop_icons_hidden`，启动时检查 `NeedsCrashRecovery`

**规则变更时重新分类**（`ReEvaluateClassifiedFiles`）：
- 规则保存后触发，遍历所有 Fence 中的文件重新匹配
- 不匹配的文件从 Fence 移除，若仍在桌面则添加回覆盖层

### 12.3 Public Desktop 支持

Windows 桌面显示的是 `%USERPROFILE%\Desktop` + `C:\Users\Public\Desktop` 的合并内容。

**App.xaml.cs**：
- `_publicDesktopPath = Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory)`
- `GetAllDesktopEntries()` 合并扫描两个目录

**DesktopFileMonitor**：
- 新增 `_publicWatcher`（第二个 `FileSystemWatcher`）
- `ScanDesktop()` 合并两个目录的文件/目录到同一 `HashSet`

### 12.4 OnDesktopFilesAdded 多 Tab 修复

**原 Bug**：`f.ViewModel.Files` 只检查活动标签页 → 改为 `IsFileAlreadyInAnyFence` 遍历所有标签页；目标查找改为 `f.Tabs.Any(t => t.Id == ...)` + `targetTab.AddFile`。

### 12.5 Tab 交互修复

**Tab 切换无响应**：
- 根因：`PreviewMouseLeftButtonDown` 无条件 `Mouse.Capture(btn)` 破坏 Button 内部 Click 判定
- 修复：仅记录起始位置，`PreviewMouseMove` 超阈值后才 Capture

**多 Tab 时无法拖拽窗口**：
- 根因：多 Tab 时 `ShowTitleBar=false` 隐藏标题栏，TabStripBorder 无拖拽处理
- 修复：`TabStripBorder.MouseLeftButtonDown` 调用 `DragMove()`（Button 自身消费点击事件不冒泡）

### 12.6 Tab 右键菜单增强

**新增菜单项**：
- 重命名 — 切换到目标 Tab 后弹出 `RenameWindow` 对话框
- 关闭 Fence — 多 Tab 时 `RemoveTab`，单 Tab 时 `AnimateClose`
- 分离（仅多 Tab 时显示）

### 12.7 重命名对话框

**替代方案**：原内联 TextBox 编辑因 `WS_EX_NOACTIVATE` 无法获取焦点，改为独立 `RenameWindow` 弹窗。

**RenameWindow**：
- `Topmost=True`，`WindowStyle=None`，`AllowsTransparency=True`
- 显示原名称（只读）和新名称输入框
- 确认/取消按钮，Enter 确认，Escape 取消
- 打开时自动 Focus + SelectAll
- `ShowDialog()` 返回 `DialogResult`，确认时 `NewName` 属性含新标题

---

## 13. Phase 9a 实现记录

### 13.1 应用程序图标

**图标设计**：
- 概念：2×2 圆角矩形网格（桌面分区隐喻），蓝色渐变调（#4488CC 系列），深色背景 #1E1E26
- 多尺寸：16×16、32×32、48×48、256×256（PNG 内嵌 ICO 格式）
- 每个网格单元使用不同蓝色渐变，带微弱内发光高亮
- 外层圆角矩形背景带蓝色细边框

**文件与配置**：
- `src/DesktopFences.App/Assets/app.ico` — 多尺寸 ICO 文件
- `DesktopFences.App.csproj` — `<ApplicationIcon>Assets\app.ico</ApplicationIcon>` + `<Resource Include="Assets\app.ico" />`
- `App.xaml.cs SetupTrayIcon()` — 从嵌入资源加载：`Application.GetResourceStream(new Uri("pack://application:,,,/Assets/app.ico"))`
- `tools/IconGenerator/` — 图标生成工具（C# 控制台项目，使用 System.Drawing 程序化绘制）

### 13.2 DarkTheme 主题基础设施

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

**兼容性说明**：
- 样式以 `x:Key` 命名，不覆盖全局隐式样式，各窗口按需引用
- `ApplyTheme()` / `ApplyDefaultTheme()` 的 per-fence 颜色覆盖机制不受影响
- Phase 9b 已完成各窗口硬编码颜色替换为 `{DynamicResource XxxBrush}` 引用

---

## 14. Phase 9b 实现记录

### 14.1 UI 美化 — 各窗口 DynamicResource 替换

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

### 14.2 SettingsWindow 无边框改造

- `WindowStyle=None, AllowsTransparency=True, Background=Transparent`
- 外层 `Border`: `CornerRadius=10`, `DropShadowEffect(BlurRadius=12)`, 半透明暗色背景 `#EE1E1E2E`
- 自定义标题栏：40px 高度，`#22FFFFFF` 背景，圆角顶部，`MouseLeftButtonDown → DragMove()`
- 关闭按钮使用 `SubtleButtonStyle`
- 所有控件替换为 DarkTheme 样式：
  - TextBox → `DarkTextBoxStyle`
  - ComboBox → `DarkComboBoxStyle` + `DarkComboBoxItemStyle`
  - CheckBox → `DarkCheckBoxStyle`
  - Slider → `DarkSliderStyle`
  - ListBox → `DarkListBoxItemStyle`
  - 按钮 → `AccentButtonStyle` / `DangerButtonStyle` / `DarkButtonStyle`
- 文字标签统一使用 `{DynamicResource TextPrimaryBrush}` / `TextSecondaryBrush`

### 14.3 微动画增强

**Tab 切换 fade（150ms）**：
- `ActivatePanelForTab()` 先 75ms fade-out（QuadraticEase EaseIn），完成后切换 DataContext，再 75ms fade-in（QuadraticEase EaseOut）
- 首次加载（`IsLoaded=false`）时跳过动画直接赋值

**文件项弹入动画**：
- `LoadIconForLastFile()` 完成后调用 `AnimateNewFileItem()`
- 在 `DispatcherPriority.Loaded` 回调中对最后一个 ListBoxItem 容器执行：
  - `ScaleTransform` 0.8→1.0（200ms QuadraticEase EaseOut）
  - `Opacity` 0→1（200ms）

### 14.4 Tab 样式多样化

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
- 标题栏 "⋯" 菜单顶部显示 tab 列表（带勾选标记指示当前 tab）
- `TabMenuSwitchRequested` 事件通知 FenceHost 切换 tab

**FenceHost.SetTabStyle(TabStyle)**：
- 存储 `_tabStyle` 字段，调用 `RefreshTabStrip()` 重绘
- `RefreshTabStrip()` 根据 `_tabStyle` 从资源字典查找对应 Style 赋给 Button
- Segmented 模式最后一个 tab 移除右边框
- App 注册方式：`App.xaml` 中增加 `TabStyles.xaml` 资源字典合并

**设置界面**：
- SettingsWindow 外观分组新增 "标签页样式" ComboBox（4 个选项）
- `OnSettingsSaved()` 和 `SpawnFenceWindow()` 中调用 `host.SetTabStyle(settings.TabStyle)`

---

## 15. Phase 9c 实现记录 — 图标系统

**目标**：取代前期各处遗留的占位 emoji / ASCII 字符（▲ ⋯ ✕ 📁 等），为主窗口图标、托盘图标、标题栏按钮、右键菜单、搜索面板引入一套矢量图标系统。

### 15.1 资产与资源

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

### 15.2 图标资源 Key

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

### 15.3 按钮样式

- `DarkIconButtonStyle` — 26×22，圆角 4，悬停用 `HoverBrush`、按下用 `PressBrush`
- `CaptionButtonStyle` — 继承上者，46×40，用于自定义 caption
- `CaptionCloseButtonStyle` — 继承 CaptionButtonStyle，但**覆写了模板**：悬停 `#E0412B`、按下 `#B8331F`、前景白。原因：`DarkIconButtonStyle` 的 Template.Trigger 用 `TargetName="Bd"` 直接设置边框 `Background`，派生 Style.Trigger 无法盖过；重写模板后 `IsMouseOver` 才能真正把按钮染红

### 15.4 UI 层落地点

| 文件 | 变更 |
|---|---|
| `Controls/FencePanel.xaml` | `RollupToggleButton` / `TitleMenuButton` 删除内联 Button.Style，改用 `DarkIconButtonStyle` + `ContentControl`（`IconRollup` / `IconSettings`）。Rollup 图标带 `RotateTransform` 以便状态切换旋转 180° |
| `Controls/FencePanel.xaml.cs` | `UpdateRollupArrow()` 由设置 `Content="▲"/"▼"` 改为设置 `RollupIcon.RenderTransform` 的旋转角。新增 `BuildMenuIcon(string geometryKey)` 静态工厂，按 HANDOFF 要求使用 `ContentControl + IconTemplate` 而非裸 `<Path>` 以保留 Foreground 继承 |
| `Controls/FenceHost.xaml(.cs)` | Tab 条 `TabRollupToggleButton` / `TabMenuButton` 同步替换；新增 `UpdateTabRollupIcon(bool)` 处理旋转；合并/分离/关闭 Tab 右键菜单加挂 `IconSplit` / `IconPortal` / `IconTrash` |
| `Controls/SettingsWindow.xaml` | 自定义标题栏左上插入 16×16 `AppLogoImage`；`✕` 按钮替换为 `CaptionCloseButtonStyle` + `IconClose`；为了让红色悬停不溢出到外层圆角，标题栏 `Border` 加 `ClipToBounds="True"` |
| `Controls/SearchWindow.xaml(.cs)` | 搜索框加 `IconSearch` 前缀；结果区覆盖 64×64 `AppLogoImage` 空态层，`UpdateEmptyState()` 随结果数量切换可见性 |
| `Themes/DarkTheme.xaml` | `DarkMenuItemStyle` 模板中 Icon `ContentPresenter` 去掉硬编码 `Visibility="Collapsed"` 初值。原模板逻辑是"Icon 为空时折叠"，但默认即折叠、又缺少"非空时显示"的 trigger，导致 `MenuItem.Icon` 始终不渲染——这是 Phase 9c 启用前没人触发过的潜在 bug |
| `App.xaml.cs` | 无需改动：`NotifyIcon.Icon` 从 `pack://application:,,,/Assets/app.ico` 加载，随 ICO 重生成自动生效 |

### 15.5 ICO 生成方案

本机未装 ImageMagick / Inkscape。`build-ico.ps1` 用 WPF `XamlReader.Load` 载入 `AppLogo.xaml`，对 `AppLogoImage` 逐尺寸 `RenderTargetBitmap.Render` → `PngBitmapEncoder` 得到 PNG 帧，再手写 ICO `ICONDIR` + `ICONDIRENTRY` 头把 9 帧（16/20/24/32/40/48/64/128/256，BPP=32）拼接写入。Windows 全版本都支持内嵌 PNG 的 ICO，任务栏/Alt-Tab/资源管理器按需选帧。

### 15.6 约束遵守

- `DesktopFences.Core` / `DesktopFences.Shell` 零改动
- `DarkTheme.xaml` 既有 brush key 未变；仅修复 `DarkMenuItemStyle` 中的 Icon 可见性逻辑
- 图标系统新资源全部加在 `DesktopFences.UI/Themes/` 下，不覆盖既有 key
- 主题色扩展（`AppLogoTopColor` / `AppLogoBottomColor` 作为资源暴露）已保留，后续实现"自定义主题色"时无需改几何

### 15.7 验证

- `dotnet build DesktopFences.sln -c Release` 0 警告 0 错误
- `dotnet test tests/DesktopFences.Core.Tests` 通过 61/61
- 视觉检查待用户在真机确认：任务栏/标题栏 Logo、Fence 折叠按钮细线条、右键菜单前缀图标对齐、设置窗关闭按钮红色悬停

---

## 16. 参考资料

- [Stardock Fences 6 官方](https://www.stardock.com/products/fences/)
- [Fences 版本历史](https://www.stardock.com/products/fences/history)
- [Fences 5 评测 (XDA)](https://www.xda-developers.com/fences-5-review/)
- [NoFences 开源实现](https://github.com/Twometer/NoFences) — C# WinForms，浮窗方案
- [DesktopFences 开源实现](https://github.com/limbo666/DesktopFences) — C# WinForms，JSON 配置
- [Palisades 开源实现](https://github.com/Xstoudi/Palisades) — C# WPF + .NET 6
- [Universe (NoFences 增强)](https://github.com/Notbazz12/Universe) — Magic Fences + Undo/Redo
- [Win+D 窗口存活方案讨论](https://learn.microsoft.com/en-us/answers/questions/2127546/)
- [Draw Behind Desktop Icons (CodeProject)](https://www.codeproject.com/Articles/856020/Draw-Behind-Desktop-Icons-in-Windows-plus)
- [Lively Wallpaper (WorkerW 实现参考)](https://github.com/rocksdanister/lively)
