# 性能优化

## 1. 优化策略

| 场景 | 优化策略 |
|------|---------|
| 大量文件图标渲染 | `VirtualizingWrapPanel`，只渲染可见区域 |
| 图标提取 | 异步 + LRU 缓存（按文件扩展名缓存，不按路径） |
| 文件系统监控 | `FileSystemWatcher` + `SHChangeNotifyRegister` 双重监控，debounce 合并事件 |
| 拖放大量文件 | 异步 IO，UI 线程不阻塞 |
| Peek 动画 | WPF 硬件加速动画（`CompositionTarget`） |
| 启动速度 | 延迟加载图标（先显示 Fence 框架，图标异步填充） |
| 内存占用 | 图标缓存上限 500 个，LRU 淘汰 |

## 2. 详细实现

### 图标异步加载

`{Binding Icon, IsAsync=True}` — WPF 延迟绑定，不阻塞 UI 线程

### 列表虚拟化

`ListBox` + `VirtualizingPanel.IsVirtualizing=True` + `VirtualizationMode=Recycling`

### 图标 LRU 缓存

- 扩展名级别 LRU（同扩展名共享图标，`.exe`/`.lnk`/`.ico` 按完整路径缓存）
- `ConcurrentDictionary` + `LinkedList` 线程安全 LRU，上限 500
- 同步 `GetIcon()` + 异步 `GetIconAsync()` 两种模式
- Icon 提取后 `Freeze()` 确保跨线程安全

### FSWatcher 事件去抖

- 500ms debounce 单次触发
- 定时全量扫描对账（每 30 秒）
- 事件：`FilesAdded`（新文件列表）、`FilesRemoved`（删除文件列表）、`FileRenamed`（旧路径→新路径）

### 自动保存去抖

- Fence 位置/大小变更后 debounce 2 秒自动保存
