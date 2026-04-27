# Phase 5: 布局管理

**目标**：布局快照、桌面分页、多显示器智能布局。

## 5.1 布局快照
- [x] 保存当前所有 Fence 位置/大小/内容为快照
- [x] 快照列表管理（重命名、删除、恢复）
- [x] 托盘菜单快速切换快照

## 5.2 桌面分页
- [x] `DesktopPage` 模型 — 每页独立的 Fence 集合
- [x] 切换动画（Fence 集体滑入/滑出）
- [x] 鼠标滚轮在桌面切换分页（需过滤掉在 Fence 内的滚轮事件）
- [x] 快捷键 Ctrl+PageUp/PageDown

## 5.3 多显示器
- [x] 枚举显示器配置，生成配置 hash
- [x] 按配置 hash 独立保存/恢复布局
- [x] 显示器热插拔检测 → 自动切换布局
- [x] Fence 限制在所属显示器范围内

## 5.4 Folder Portal
- [x] FenceDefinition 扩展 PortalPath 属性
- [x] Portal 模式：FileSystemWatcher 绑定到 PortalPath
- [x] 面包屑导航（双击子文件夹 → 进入，标题栏显示路径）
- [x] 支持网络路径和云同步目录

**验收标准**：可以保存/恢复布局快照，切换桌面分页时有动画过渡，Folder Portal 实时同步文件夹内容。 ✅

---

## Phase 5 实现记录

### 布局快照管理

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

### 桌面分页

**状态：已禁用**（2026-03，bug 1423）

自定义分页功能已禁用，改由 Windows 虚拟桌面原生管理。`PageSwitchManager` 保留但不启动，`PageManager` 仅用于数据持久化兼容（所有 Fence 归到 Page 0）。分页切换动画、托盘分页菜单、鼠标滚轮/键盘热键切换均已移除。

### 多显示器支持

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

### Folder Portal

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
