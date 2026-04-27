# Phase 8: Bug 修复与功能增强

## 8.1 自动整理修复

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

## 8.2 桌面文件隐藏与自渲染机制

**设计目标**：程序运行期间隐藏原生桌面图标层，已收纳文件由 Fence 展示，未收纳文件由覆盖窗口（DesktopIconOverlay）在原始位置自行渲染；退出时还原原生桌面图标。

### 8.2.1 SysListView32 整层隐藏

曾使用 per-file `FileAttributes.Hidden` 方案，但该方案对部分文件类型无效（图片、文档等在资源管理器开启「显示隐藏文件」时仍以半透明形式显示），因此切换到 SysListView32 整层隐藏方案。

**实现**（`DesktopIconManager` in `DesktopFences.Shell`）：
- `FindDesktopListView()` — 查找桌面图标 ListView 窗口
- `GetListViewHandle()` — 获取缓存的或新查找的 SysListView32 句柄
- `HideIcons()` — `ShowWindow(SysListView32, SW_HIDE)` 隐藏整个图标层 + 写 flag 文件
- `ShowIcons()` — `ShowWindow(SysListView32, SW_SHOW)` 恢复图标层 + 删 flag 文件

### 8.2.2 跨进程图标位置读取

**实现**（`DesktopIconPositionReader` in `DesktopFences.Shell`）：

SysListView32 在 explorer.exe 进程中，需要跨进程通信读取图标位置：
1. `SendMessage(LVM_GETITEMCOUNT)` 获取图标数量
2. `VirtualAllocEx` 在 explorer 进程分配共享内存
3. 对每个图标：`WriteProcessMemory` 写入 LVITEMW → `SendMessage(LVM_GETITEMTEXTW)` 读取显示名 → `SendMessage(LVM_GETITEMPOSITION)` 读取位置 → `ReadProcessMemory` 读回结果
4. 失败时返回空列表，覆盖窗口使用自动网格定位（优雅降级）

### 8.2.3 未收纳图标覆盖窗口（DesktopIconOverlay）

**实现**（`DesktopIconOverlay` in `DesktopFences.UI`）：

全屏透明 WPF 窗口，使用 Canvas 绝对定位渲染未收纳的桌面图标：
- `WindowStyle=None`, `AllowsTransparency=True`, `Background=Transparent`
- `Canvas Background="{x:Null}"` — 空白区域点击穿透
- 通过 `DesktopEmbedManager.RegisterWindow` 获得与 FenceHost 相同的 z-order 管理

**交互**：
- 双击打开（`ShellFileOperations.OpenFile`）
- 右键 Shell 上下文菜单（`ShellContextMenu.Show`）
- 拖拽到 Fence（`DragDrop.DoDragDrop`，Move 效果）
- **图标自由移动**：鼠标拖拽在覆盖层内部时进入手动移动模式

### 8.2.4 生命周期与同步

**启动流程**（`LoadFencesAsync`）：
1. 崩溃恢复检查 → 加载 Fence → 自动分类
2. `DesktopIconPositionReader.ReadAllPositions()` 读取原始位置
3. `DesktopIconManager.HideIcons()` 隐藏原生图标层
4. `CreateDesktopOverlay()` 创建覆盖窗口，显示未收纳文件

**退出流程**（`OnExit`）：关闭覆盖窗口 → `ShowIcons()` 恢复原生图标

**实时同步**：
- 文件被自动分类到 Fence → `RemoveIcon` 从覆盖层移除
- 新桌面文件未匹配规则 → `AddIcon` 添加到覆盖层
- 桌面文件被删除 → 同时从 Fence 和覆盖层移除
- 文件从 Fence 移出但仍在桌面 → `AddIcon` 重新添加到覆盖层
- 文件重命名 → 更新覆盖层图标
- 切换 Fence 可见性 → 覆盖层同步隐藏/显示

**崩溃恢复**：flag 文件 `%APPDATA%\DesktopFences\.desktop_icons_hidden`，启动时检查 `NeedsCrashRecovery`

## 8.3 Public Desktop 支持

Windows 桌面显示的是 `%USERPROFILE%\Desktop` + `C:\Users\Public\Desktop` 的合并内容。

**App.xaml.cs**：
- `_publicDesktopPath = Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory)`
- `GetAllDesktopEntries()` 合并扫描两个目录

**DesktopFileMonitor**：
- 新增 `_publicWatcher`（第二个 `FileSystemWatcher`）
- `ScanDesktop()` 合并两个目录的文件/目录到同一 `HashSet`

## 8.4 Tab 交互修复

**Tab 切换无响应**：
- 根因：`PreviewMouseLeftButtonDown` 无条件 `Mouse.Capture(btn)` 破坏 Button 内部 Click 判定
- 修复：仅记录起始位置，`PreviewMouseMove` 超阈值后才 Capture

**多 Tab 时无法拖拽窗口**：
- 根因：多 Tab 时 `ShowTitleBar=false` 隐藏标题栏，TabStripBorder 无拖拽处理
- 修复：`TabStripBorder.MouseLeftButtonDown` 调用 `DragMove()`

**Tab 右键菜单增强**：
- 新增菜单项：重命名、关闭 Fence、分离（仅多 Tab 时显示）

## 8.5 重命名对话框

**替代方案**：原内联 TextBox 编辑因 `WS_EX_NOACTIVATE` 无法获取焦点，改为独立 `RenameWindow` 弹窗。

**RenameWindow**：
- `Topmost=True`，`WindowStyle=None`，`AllowsTransparency=True`
- 显示原名称（只读）和新名称输入框
- 确认/取消按钮，Enter 确认，Escape 取消
- 打开时自动 Focus + SelectAll
- `ShowDialog()` 返回 `DialogResult`，确认时 `NewName` 属性含新标题
