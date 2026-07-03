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
| 内存占用 | 图标缓存上限 500 个，LRU 淘汰；按显示尺寸×DPI 量化提取（见 §3）；空闲工作集裁剪 |

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

## 3. 内存占用优化（2026-07）

目标：降低任务管理器「内存」列（专用工作集）的稳态读数，不改变任何用户可见功能与视觉。
基线约 220MB，四项措施按收益排序：

### 3.1 图标提取尺寸 = 显示 DIP × 实际 DPI（主力削减）

- **量化公式**：`ShellIconExtractor.QuantizeRequestSize(displayDip, dpiScale)` —
  需求像素向上取整后量化到 16 的倍数（钳 32-256）。向上取整保证位图 ≥ 显示像素
  （WPF 只降采样不放大，不糊）；量化保证单机实际只出现 1-2 个 LRU 尺寸桶
  （缓存 key 含尺寸，桶多 = 同图标缓存多份）。
- **去掉 `SIIGBF.BiggerSizeOk`**（ShellIconExtractor）：带此 flag 时
  `IShellItemImageFactory::GetImage` 会直接返回图标原生分辨率（常见 256×256 BGRA
  ≈ 256KB/张）整张进缓存；去掉后 shell 用资源管理器同款算法降采样到精确请求尺寸。
- **桌面图标 overlay**（DesktopIconOverlay）：`IconPixelSize = Quantize(_iconSize, dpi)`，
  替代旧的固定 `_iconSize×2`（100% 缩放下 4 倍像素浪费）。DPI 在构造期用
  `VisualTreeHelper.GetDpi` 取（SetIcons 早于 Loaded），OnLoaded 用 CompositionTarget
  实测值校准，桶变了则对所有元素重取图标。
- **fence tile**（FencePanel）：按「IconSize 设置上限 64 DIP × DPI」量化请求，
  而非当前设置值——位图恒 ≥ 任何设置下的显示像素，IconSize 变更时无需重提取。
  计算结果回写 `UI.Services.IconMetrics.TileIconRequestPx`，App 动态添加文件
  （规则分类/portal 同步）复用同一尺寸桶。
- 效果量级：100 个桌面图标（中图标 48、100% DPI）从 96px→48px 位图 = 每张 36KB→9KB；
  最坏情况（BiggerSizeOk 返回 256px）每张 256KB→9KB。

### 3.2 空闲工作集裁剪（WorkingSetTrimmer，Shell 项目）

`WorkingSetTrimmer.Trim()` = LOH 压缩 + 两轮 full GC + `SetProcessWorkingSetSize(-1,-1)`
把不活跃页面移入备用列表（再访问只是软缺页，无磁盘 IO）。App 三个触发点，均有冷却：

| 触发点 | 时机 | 冷却 |
|--------|------|------|
| 启动稳定后 | App_OnStartup 完成 60s 后一次性（图标风暴/JIT/首帧已结束） | 无 |
| 全部隐藏 | ToggleAllFences 隐藏全部 2s 后复核仍全隐藏才裁 | 1 分钟 |
| 输入空闲 | 借用 10s 的 `_fileExistenceTimer`：`GetLastInputInfo` 空闲 ≥5 分钟 | 30 分钟 |

拖拽等交互路径绝不触发；`_isShuttingDown` 时跳过。P/Invoke
（GetCurrentProcess/SetProcessWorkingSetSize/GetLastInputInfo）在 NativeMethods。

### 3.3 GC 配置（runtimeconfig.template.json）

```json
{ "configProperties": { "System.GC.ConserveMemory": 5, "System.GC.Concurrent": false } }
```

- `ConserveMemory=5`（0-9）：更积极压缩/decommit，本应用分配率低（各扫描/保存有去抖），量测不满意可试 7。
- `Concurrent=false`：去掉后台 GC 线程及其保留内存；小堆桌面应用 full GC 停顿毫秒级不可感知。
- **否决 InvariantGlobalization**：DesktopIconOverlay.SortIcons 用 CurrentCulture 比较中文
  类型名（"应用程序"等），invariant 化会改变排序 = 功能变化；且 ICU 多为共享页收益小。

### 3.4 层叠窗口表面与阴影实例

- **SnapGuideOverlay 平时 1×1**：AllowsTransparency 窗口持有 宽×高×4 字节软件合成表面，
  全屏常驻在 2K 屏 ≈14MB。改为 ShowLines 显示虚线时才撑满虚拟屏（带跳过 guard，
  WM_MOVING 高频调用不反复布局；重读 SystemParameters 顺带修复拓扑变化后的陈旧边界），
  Hide 缩回 1×1 确定性释放。启动 Show()（建 HWND / 应用 WS_EX_TRANSPARENT）后立即 Hide。
- **DropShadowEffect 共享 + Freeze**：桌面 overlay 每图标标签、fence tile 每 badge 原来
  各 new 一个参数相同的 DropShadowEffect；改为共享冻结实例（overlay：静态 LabelShadow；
  tile：FileTile.xaml 的 TileBadgeShadow/SysBadgeShadow 资源，`po:Freeze="True"`）。

### 3.5 量测方法

```powershell
# 专用工作集 = 任务管理器「内存」列（中文系统计数器名已本地化）
(Get-Counter '\进程(DesktopFences.App)\工作集 - 专用').CounterSamples.CookedValue / 1MB
# 辅助：$p = Get-Process DesktopFences.App; $p.WorkingSet64/1MB; $p.PrivateMemorySize64/1MB
```

固定序列：冷启动 2 分钟记稳态 → 交互回合（显隐×2、拖 fence、搜索窗、桌面查看切换）记峰值
→ 静置 6 分钟（空闲裁剪）→ 再交互 1 分钟记回涨稳态。
