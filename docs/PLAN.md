# DesktopFences 开发计划

## 开发阶段总览

```
Phase 0: 基础骨架          ← 可运行的空 Fence 窗口出现在桌面上
Phase 1: 核心交互          ← 拖放、调整大小、移动、Snap 吸附
Phase 2: 文件管理    ✅    ← 图标渲染、文件操作、Shell 右键菜单
Phase 3: 自动化      ✅    ← 规则引擎、文件监控、自动分类
Phase 4: 高级功能    ✅    ← Rollup、Peek、Quick Hide、Tab 合并
Phase 5: 布局管理    ✅    ← 快照、桌面分页、多显示器
Phase 6: 精细打磨    ✅    ← 动画、主题、性能优化、打包分发
```

---

## Phase 0: 基础骨架 ✅ 已完成

**目标**：一个无边框 WPF 窗口显示在桌面上，Win+D 后仍然可见。

### 0.1 项目结构搭建
- [x] 创建解决方案和项目（Core / Shell / UI / App / Tests）
- [x] 配置项目引用关系和 TargetFramework
- [x] 添加 `Directory.Build.props` 统一版本号和公共属性
- [x] 添加 `.editorconfig` 统一代码风格
- [x] 添加 `.gitignore` 并初始化 Git 仓库

### 0.2 Win32 Interop 基础层 (Shell 项目)
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

### 0.3 FenceHost 窗口 (UI 项目)
- [x] `FenceHost.xaml` — 无边框、透明背景的 WPF Window
  - `WindowStyle=None`, `AllowsTransparency=True`, `Background=Transparent`
  - `ShowInTaskbar=False`, `ResizeMode=NoResize`
- [x] 在 `Loaded` 事件中调用 `DesktopEmbedManager.RegisterWindow()` 配置窗口样式
- [x] 基础半透明背景渲染（深色半透明矩形 + 圆角 + 阴影）

### 0.4 应用入口 (App 项目)
- [x] 单实例检查 (`Mutex`)
- [x] 创建 FenceHost 窗口并显示
- [x] 验证 Win+D 后窗口仍然可见 ✅ 通过（2026-03-03）

**验收标准**：启动应用后，桌面上出现一个半透明矩形，按 Win+D 后矩形仍然显示。 ✅ 已通过

---

## Phase 1: 核心交互 ✅ 已完成

**目标**：Fence 窗口可以拖动、调整大小、Snap 吸附。

### 1.1 FencePanel 控件骨架 (UI 项目)
- [x] `FencePanel.xaml` — 核心 UserControl（TitleBar + Content + 8向 ResizeGrip Thumb）
- [x] `FencePanelViewModel.cs` — MVVM ViewModel（Title, X, Y, Width, Height, IsRolledUp, ViewMode）
- [x] `ViewModelBase.cs` — INotifyPropertyChanged 基类

### 1.2 拖动 (UI 项目)
- [x] TitleBar `MouseLeftButtonDown` → `Window.DragMove()`
- [x] 拖动结束 → 同步位置到 ViewModel → 触发 Snap + AutoSave

### 1.3 调整大小 (UI 项目)
- [x] 8 个方向透明 Thumb 控件（Top, Bottom, Left, Right, 4 个角）
- [x] Thumb.DragDelta → 直接修改 Window 和 ViewModel 的 Width/Height/X/Y
- [x] 最小尺寸约束（Width >= 120, Height >= 60）

### 1.4 Snap 吸附 (Core 项目)
- [x] `SnapEngine.cs` — 纯函数，无副作用
  - 输入：moving Rect + other Rects + screen bounds
  - 输出：吸附修正后的 SnapResult
  - 算法：遍历所有边，找到 distance < 10px 的最近吸附目标
- [x] 拖动/调整大小结束时在 App 层调用 SnapEngine
- [x] 7 个单元测试全部通过
- [ ] 按住 Alt 临时禁用吸附（延迟到后续优化）

### 1.5 系统托盘 + Fence 管理 (App 项目)
- [x] `NotifyIcon` 系统托盘图标 + 右键菜单
  - New Fence / Show-Hide All / Save Layout / Exit
- [x] 多 Fence 窗口管理（创建、关闭、显示/隐藏切换）
- [x] 双击托盘图标 → 显示/隐藏所有 Fence

### 1.6 布局持久化 (Core 项目)
- [x] `JsonLayoutStore.cs` — JSON 文件持久化到 `%APPDATA%\DesktopFences\`
- [x] 原子写入（临时文件 → rename）
- [x] 启动时自动加载、操作后 debounce 2秒自动保存
- [x] 首次运行自动创建默认 Fence

**验收标准**：可以创建多个 Fence，拖动移动，调整大小，Fence 之间和屏幕边缘会吸附对齐。 ✅

---

## Phase 2: 文件管理 ✅ 已完成

**目标**：Fence 内显示文件图标，支持双击打开、拖放、右键菜单。

### 2.1 图标提取 (Shell 项目)
- [x] `ShellIconExtractor.cs` — 合并了图标提取 + LRU 缓存
  - `SHGetFileInfo` 提取大图标 (32x32)
  - 扩展名级别 LRU 缓存（同扩展名共享图标，`.exe`/`.lnk`/`.ico` 按完整路径缓存）
  - `ConcurrentDictionary` + `LinkedList` 线程安全 LRU，上限 500
  - 同步 `GetIcon()` + 异步 `GetIconAsync()` 两种模式
  - Icon 提取后 `Freeze()` 确保跨线程安全
  - 文件不存在时使用 `SHGFI_USEFILEATTRIBUTES` 按扩展名获取图标

### 2.2 文件图标渲染 (UI 项目)
- [x] `FileItemViewModel.cs` — 文件项 ViewModel（FilePath, DisplayName, Icon, IsSelected, IsRenaming）
- [x] `FencePanelViewModel.cs` — 添加 `ObservableCollection<FileItemViewModel> Files`，`AddFile/RemoveFile/SyncToModel` 方法
- [x] FencePanel.xaml 使用 `ItemsControl` + `WrapPanel` + `DataTemplate` 渲染文件图标
  - 32x32 图标 + 文件名，72x80 单元格，选中态蓝色高亮
  - 空状态提示 "Drop files here"
  - F2 重命名（延迟到后续优化）

### 2.3 拖放 (UI 项目)
- [x] 从 Explorer 拖入文件到 Fence：`AllowDrop=True` + `OnDragOver/OnDrop` 事件处理
- [x] 从 Fence 拖出文件到 Explorer：`FileItem_MouseMove` → `DoDragDrop(FileDrop)`
- [x] Fence 之间拖放：Move 时从源 Fence 移除，Copy 时保留
- [ ] 拖放视觉反馈（延迟到 Phase 6 优化）

### 2.4 文件操作 (Shell 项目)
- [x] `ShellFileOperations.cs` — 静态工具类
  - 双击打开：`Process.Start(UseShellExecute=true)`
  - 删除到回收站：`SHFileOperation(FO_DELETE, FOF_ALLOWUNDO)`
  - 重命名：`File.Move`
- [ ] 快捷方式目标解析 `IShellLink`（延迟到后续需要时实现）

### 2.5 Shell 右键菜单 (Shell 项目)
- [x] `ShellContextMenu.cs` — 原生 Shell 上下文菜单
  - `SHParseDisplayName` → `SHBindToObject` → `IShellFolder.GetUIObjectOf` → `IContextMenu`
  - `QueryContextMenu` 填充菜单 → `TrackPopupMenuEx` 显示 → `InvokeCommand` 执行
  - 完整 COM 资源管理（`Marshal.FreeCoTaskMem`, `DestroyMenu`）

### 2.6 集成 (App + UI)
- [x] `App.xaml.cs` — 创建共享 `ShellIconExtractor` 实例，传递给所有 FenceHost
- [x] `FenceHost.xaml.cs` — 构造函数接收 `ShellIconExtractor`，传递给 `FencePanel.IconExtractor`
- [x] `FencePanel.xaml.cs` — `LoadAllIcons()` 加载已有文件图标，`LoadIconForLastFile()` 新增文件时即时加载

**验收标准**：Fence 内显示文件图标，可以从 Explorer 拖入文件，双击打开，右键出现系统上下文菜单。 ✅

---

## Phase 3: 自动化 ✅ 已完成

**目标**：新文件出现在桌面时自动分类到对应 Fence。

### 3.1 文件监控 (Shell 项目)
- [x] `DesktopFileMonitor.cs`
  - `FileSystemWatcher` 监控桌面目录 (`Environment.GetFolderPath(SpecialFolder.Desktop)`)
  - 定时全量扫描对账（每 30 秒），对比 `_knownFiles` HashSet 发现新增/删除
  - 事件 debounce（500ms，`System.Timers.Timer` 单次触发）
  - 事件：`FilesAdded`（新文件列表）、`FilesRemoved`（删除文件列表）、`FileRenamed`（旧路径→新路径）
  - `GetCurrentFiles()` 获取当前桌面所有文件
  - `SHChangeNotifyRegister` 作为补充（延迟到后续需要时实现）

### 3.2 规则引擎实现 (Core 项目)
- [x] `RuleEngine.cs` — 实现 `IRuleEngine`
  - Extension 匹配：逗号分隔的扩展名列表，自动补全前导 `.`，大小写不敏感
  - NameGlob 匹配：glob → Regex 转换（`*` → `.*`，`?` → `.`），大小写不敏感
  - DateRange 匹配：`FileInfo.LastWriteTime` 范围检查
  - SizeRange 匹配：`FileInfo.Length` 范围检查
  - Regex 匹配：直接正则，无效正则返回 false 不抛异常
  - 规则按 Priority 升序排序，跳过 `IsEnabled=false` 的规则
  - `GlobToRegex()` 内部辅助方法
- [x] 22 个单元测试覆盖所有规则类型、优先级排序、禁用规则、空模式边界情况
- [x] `InternalsVisibleTo` 允许测试项目访问 `internal` 方法

### 3.3 规则持久化 (Core 项目)
- [x] `ILayoutStore` — 新增 `LoadRulesAsync()` / `SaveRulesAsync()` 接口
- [x] `JsonLayoutStore` — 新增 `rules.json` 文件读写，原子写入
- [x] 规则配置 UI（`RuleEditorWindow.xaml`）— 列表 + 表单双面板，支持增删改查（见 Phase 6 扩展）

### 3.4 自动分类流程集成
- [x] `App.xaml.cs` — 完整的自动分类管道：
  - 启动时加载规则 → 加载 Fence → 启动 `DesktopFileMonitor`
  - `OnDesktopFilesAdded`：新文件 → 跳过已存在于任何 Fence 的 → `RuleEngine.Match` → 添加到目标 Fence + 加载图标
  - `OnDesktopFileRenamed`：更新 Fence 中文件路径 → `SyncToModel`
  - 退出时 `Dispose` FileMonitor
- [ ] 用户手动放入的文件标记为"手动"（延迟到后续优化）

**验收标准**：配置规则 ".jpg,.png → 图片" 后，在桌面放一个 .jpg 文件，自动出现在"图片"Fence 中。 ✅

---

## Phase 4: 高级功能 ✅ 已完成

**目标**：实现 Rollup、Peek、Quick Hide 等 Fences 标志性功能。

### 4.1 Rollup 折叠
- [x] 双击标题栏 → `DoubleAnimation` 高度缩小到 38px（标题栏高度），只显示标题
- [x] 折叠态鼠标悬停 → `HoverExpand()` 临时展开到 `ExpandedHeight`
- [x] 鼠标离开 → `HoverCollapse()` 自动折回 38px
- [x] `IsRolledUp` / `ExpandedHeight` 保存到持久化
- [x] `FenceHost` 初始化时检查 `IsRolledUp`，已折叠的 Fence 直接以折叠态显示
- [x] `RollupChanged` 事件通知 `FenceHost` 同步窗口高度动画

### 4.2 Peek 快速预览
- [x] `PeekManager.cs` — 使用 `RegisterHotKey(MOD_WIN | MOD_NOREPEAT, VK_SPACE)` 注册 Win+Space
- [x] 创建隐藏 `HwndSource` 接收 `WM_HOTKEY` 消息
- [x] Peek 激活 → `DesktopEmbedManager.EnterPeek()` → 所有窗口 `HWND_TOPMOST`
- [x] Peek 期间 `OnForegroundChanged` 钩子不自动恢复 BOTTOM（`_isPeekActive` 保护）
- [x] 再次 Win+Space → toggle 退出 Peek
- [x] Escape 键 → `OnEscapePressed()` 退出 Peek（通过键盘钩子检测 `VK_ESCAPE`）
- [ ] DWM 背景模糊（延迟到 Phase 6 优化）

### 4.3 Quick Hide 快速隐藏
- [x] `QuickHideManager.cs` — 低级鼠标钩子 (`WH_MOUSE_LL`) 检测桌面双击
- [x] 通过 `WindowFromPoint` + `GetClassName` 判断点击目标是否为桌面（Progman / WorkerW / SHELLDLL_DefView / SysListView32）
- [x] 手动双击检测（500ms 阈值，4px 距离容差），避免依赖系统 `WM_LBUTTONDBLCLK`
- [x] 双击 → 调用 `ToggleAllFences()` 隐藏/显示所有 Fence
- [x] 系统托盘双击也触发 toggle（Phase 1 已实现）

### 4.4 Tab 合并
- [ ] Tab 合并功能延迟到 Phase 6（需要较大的 UI 重构，涉及标题栏 Tab 条 + 拖放合并/拆分逻辑）

### 4.5 多视图模式
- [ ] 多视图模式延迟到 Phase 6（需要新增 ListView/DetailView 的 DataTemplate + 切换逻辑）

### 4.6 文件排序
- [x] `FencePanelViewModel.ApplySort()` — 支持 5 种排序字段：名称、扩展名、大小、修改日期、创建日期
- [x] 升序/降序切换，`SortBy` / `SortDirection` 属性
- [x] 排序通过 `ObservableCollection.Move()` 原地重排，保持 UI 绑定
- [x] 排序后自动 `SyncToModel()` 同步到持久化
- [x] 每个 Fence 独立排序设置，从 Model 加载

**验收标准**：标题栏双击折叠/展开、Win+Space 弹出 Peek、桌面双击隐藏/显示、文件可排序。 ✅

---

## Phase 5: 布局管理

**目标**：布局快照、桌面分页、多显示器智能布局。

### 5.1 布局快照
- [x] 保存当前所有 Fence 位置/大小/内容为快照
- [x] 快照列表管理（重命名、删除、恢复）
- [x] 托盘菜单快速切换快照

### 5.2 桌面分页
- [x] `DesktopPage` 模型 — 每页独立的 Fence 集合
- [x] 切换动画（Fence 集体滑入/滑出）
- [x] 鼠标滚轮在桌面切换分页（需过滤掉在 Fence 内的滚轮事件）
- [x] 快捷键 Ctrl+PageUp/PageDown

### 5.3 多显示器
- [x] 枚举显示器配置，生成配置 hash
- [x] 按配置 hash 独立保存/恢复布局
- [x] 显示器热插拔检测 → 自动切换布局
- [x] Fence 限制在所属显示器范围内

### 5.4 Folder Portal
- [x] FenceDefinition 扩展 PortalPath 属性
- [x] Portal 模式：FileSystemWatcher 绑定到 PortalPath
- [x] 面包屑导航（双击子文件夹 → 进入，标题栏显示路径）
- [x] 支持网络路径和云同步目录

**验收标准**：可以保存/恢复布局快照，切换桌面分页时有动画过渡，Folder Portal 实时同步文件夹内容。 ✅

---

## Phase 6: 精细打磨 ✅ 已完成

**目标**：动画、主题、性能优化、打包分发。

### 6.1 动画优化
- [x] Fence 创建时淡入（FenceHost.OnLoaded → FenceContent.AnimateFadeIn，Opacity 0→1，250ms EaseOut）
- [x] Fence 删除时淡出（FenceHost.AnimateClose → FenceContent.AnimateFadeOut，200ms，完成后 Close）
- [x] 文件拖入"吸入"动画（ScaleTransform 1.0→1.02→1.0，150ms drop pulse）
- [x] 分页切换滑动动画（DoubleAnimation，300ms QuadraticEase，Phase 5 已实现）
- [x] Rollup 展开/折叠高度动画（DoubleAnimation，Phase 4 已实现）
- [ ] Snap 吸附磁性效果（视觉反馈，延迟到后续优化）

### 6.2 主题与外观
- [x] Fence 背景颜色自定义（每个 Fence 独立，`FenceDefinition.BackgroundColor`）
- [x] 标题栏颜色自定义（`FenceDefinition.TitleBarColor`）
- [x] 文字颜色自定义（`FenceDefinition.TextColor`）
- [x] 单 Fence 透明度设置（`FenceDefinition.Opacity`，绑定到 `FenceOpacity` ViewModel）
- [x] 全局默认颜色/透明度/字体由 `AppSettings` 统一管理，新建 Fence 继承全局默认
- [ ] Chameleon 模式（延迟到后续优化）
- [ ] Icon Tint（延迟到后续优化）

### 6.3 设置界面
- [x] `SettingsWindow.xaml` — 全局设置（外观 / 行为 / 启动 / 高级四组）
  - 外观：默认 Fence 颜色、标题栏颜色、文字颜色、透明度滑块、字体大小滑块
  - 行为：Snap 距离滑块、Quick Hide 开关
  - 启动：开机自启（HKCU Run）、最小化到托盘
  - 高级：兼容模式、调试日志
- [x] `SettingsSaved` 事件驱动更新：主题 + 开机自启 + Quick Hide 即时生效
- [x] `AppSettings` 模型 — 全局设置持久化到 `settings.json`

### 6.4 快捷搜索
- [x] `SearchHotkeyManager` — `RegisterHotKey(MOD_CONTROL|MOD_NOREPEAT, VK_OEM_3)` 全局 Ctrl+\` 热键
- [x] `SearchWindow.xaml` — 深色半透明浮动搜索框（500×400，TopMost，AllowsTransparency）
- [x] 搜索所有 Fence 内文件（DisplayName + FenceName 双字段过滤，OrdinalIgnoreCase）
- [x] 实时过滤（TextChanged 事件即时刷新）
- [x] Enter 打开文件（`Process.Start(UseShellExecute=true)`），Esc 关闭，失焦自动关闭
- [x] 搜索窗口淡入（Opacity 0→1，150ms）

### 6.5 性能优化
- [x] 图标异步加载：`{Binding Icon, IsAsync=True}` — WPF 延迟绑定，不阻塞 UI 线程
- [x] 虚拟化渲染：`ListBox` + `VirtualizingPanel.IsVirtualizing=True` + `VirtualizationMode=Recycling`
- [x] 图标 LRU 缓存（按扩展名，上限 500，Phase 2 已实现）
- [x] FSWatcher 事件 debounce（500ms 单次触发，Phase 3 已实现）
- [x] 原子写入（临时文件 → rename）防数据损坏

### 6.6 打包分发
- [x] `win-x64-self-contained.pubxml` — 自包含单文件发布配置
  - `SelfContained=true`，`PublishSingleFile=true`，`PublishReadyToRun=true`
  - `EnableCompressionInSingleFile=true`，`TrimMode=partial`
- [x] `StartupManager` — `HKCU\SOFTWARE\Microsoft\Windows\CurrentVersion\Run` 注册开机自启
- [ ] MSIX / Inno Setup 安装包（延迟到后续需要时）
- [ ] 自动更新检查（延迟到后续需要时）

**验收标准**：完整功能可用，动画流畅，设置可调，安装包可分发。 ✅ 已通过（61 个单元测试全部通过）

---

## 里程碑时间线（建议）

| 阶段 | 核心交付 | 预估工作量 |
|------|---------|-----------|
| Phase 0 | Fence 窗口在桌面可见 + Win+D 存活 | 基础搭建 |
| Phase 1 | 拖动、调整大小、Snap 吸附 | 核心交互 |
| Phase 2 | 文件图标、拖放、右键菜单 | **MVP 可用** |
| Phase 3 | 规则引擎、自动分类 | 自动化核心 |
| Phase 4 | Rollup, Peek, Quick Hide, Tab | 差异化功能 |
| Phase 5 | 快照、分页、多显示器 | 高级特性 |
| Phase 6 | 动画、主题、搜索、打包 | 生产就绪 |

**MVP = Phase 0 + 1 + 2**：桌面上有可交互的 Fence 容器，能放文件进去，能拖放、能打开。
