# Phase 2: 文件管理

**目标**：Fence 内显示文件图标，支持双击打开、拖放、右键菜单。

## 2.1 图标提取 (Shell 项目)
- [x] `ShellIconExtractor.cs` — 合并了图标提取 + LRU 缓存
  - `SHGetFileInfo` 提取大图标 (32x32)
  - 扩展名级别 LRU 缓存（同扩展名共享图标，`.exe`/`.lnk`/`.ico` 按完整路径缓存）
  - `ConcurrentDictionary` + `LinkedList` 线程安全 LRU，上限 500
  - 同步 `GetIcon()` + 异步 `GetIconAsync()` 两种模式
  - Icon 提取后 `Freeze()` 确保跨线程安全
  - 文件不存在时使用 `SHGFI_USEFILEATTRIBUTES` 按扩展名获取图标

## 2.2 文件图标渲染 (UI 项目)
- [x] `FileItemViewModel.cs` — 文件项 ViewModel（FilePath, DisplayName, Icon, IsSelected, IsRenaming）
- [x] `FencePanelViewModel.cs` — 添加 `ObservableCollection<FileItemViewModel> Files`，`AddFile/RemoveFile/SyncToModel` 方法
- [x] FencePanel.xaml 使用 `ItemsControl` + `WrapPanel` + `DataTemplate` 渲染文件图标
  - 32x32 图标 + 文件名，72x80 单元格，选中态蓝色高亮
  - 空状态提示 "Drop files here"
  - F2 重命名（延迟到后续优化）

## 2.3 拖放 (UI 项目)
- [x] 从 Explorer 拖入文件到 Fence：`AllowDrop=True` + `OnDragOver/OnDrop` 事件处理
- [x] 从 Fence 拖出文件到 Explorer：`FileItem_MouseMove` → `DoDragDrop(FileDrop)`
- [x] Fence 之间拖放：Move 时从源 Fence 移除，Copy 时保留
- [ ] 拖放视觉反馈（延迟到 Phase 6 优化）

## 2.4 文件操作 (Shell 项目)
- [x] `ShellFileOperations.cs` — 静态工具类
  - 双击打开：`Process.Start(UseShellExecute=true)`
  - 删除到回收站：`SHFileOperation(FO_DELETE, FOF_ALLOWUNDO)`
  - 重命名：`File.Move`
- [ ] 快捷方式目标解析 `IShellLink`（延迟到后续需要时实现）

## 2.5 Shell 右键菜单 (Shell 项目)
- [x] `ShellContextMenu.cs` — 原生 Shell 上下文菜单
  - `SHParseDisplayName` → `SHBindToObject` → `IShellFolder.GetUIObjectOf` → `IContextMenu`
  - `QueryContextMenu` 填充菜单 → `TrackPopupMenuEx` 显示 → `InvokeCommand` 执行
  - 完整 COM 资源管理（`Marshal.FreeCoTaskMem`, `DestroyMenu`）

## 2.6 集成 (App + UI)
- [x] `App.xaml.cs` — 创建共享 `ShellIconExtractor` 实例，传递给所有 FenceHost
- [x] `FenceHost.xaml.cs` — 构造函数接收 `ShellIconExtractor`，传递给 `FencePanel.IconExtractor`
- [x] `FencePanel.xaml.cs` — `LoadAllIcons()` 加载已有文件图标，`LoadIconForLastFile()` 新增文件时即时加载

**验收标准**：Fence 内显示文件图标，可以从 Explorer 拖入文件，双击打开，右键出现系统上下文菜单。 ✅
