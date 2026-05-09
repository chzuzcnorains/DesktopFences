# Folder Portal

## 1. 原理

```
Fence 可以绑定到一个文件夹路径，实时显示该文件夹内容。
不移动/复制文件，只是"镜像"显示。
```

## 2. 实现

```
1. FenceDefinition 新增 PortalPath 属性
2. 当 PortalPath 非空时，Fence 进入 Portal 模式
3. 使用 FileSystemWatcher 监控目标文件夹
4. 文件增删改 → 自动刷新 Fence 内容
5. 双击文件 → ShellExecute 打开
6. 支持在 Portal 内进入子文件夹（面包屑导航）
7. 支持云存储文件夹（OneDrive / Dropbox 本地同步目录）
```

## 3. Portal 操作界面

- **创建入口**：系统托盘右键菜单 → "新建文件夹映射 Fence..." → `OpenFolderDialog` 选择文件夹
- **设置/更改映射**：Fence 标题栏右键菜单 → "设为文件夹映射..." / "更改映射文件夹 (路径)" → `OpenFolderDialog`
- **取消映射**：Fence 标题栏右键菜单 → "取消文件夹映射" → 清除 PortalPath、移除标题 📁 前缀
- **视觉指示**：Portal Fence 标题自动显示 `📁 文件夹名`，标题栏 Tooltip 显示完整路径
- **空状态提示**：Portal 模式下显示 "右键标题栏可更改映射文件夹"（而非 "拖放文件到此处"）

## 4. 事件流

```
FencePanel.PortalModeChanged → App.xaml.cs 启动/停止 FolderPortalWatcher
FolderPortalWatcher.Watch(portalPath)
  → FileSystemWatcher 监控创建/删除/重命名/修改
  → 300ms 去抖后触发 ContentsChanged 事件
  → App.SyncPortalContents(): 增量同步（新增文件加入 VM，移除已删除文件）
  → 图标自动加载
```

## 5. 面包屑导航

- `NavigateToSubfolder(name)` — 切换到子文件夹
- `NavigateUp()` — 返回父目录
- `GetBreadcrumbs()` — 获取从根到当前路径的完整路径段列表
- 支持网络路径（UNC 路径）和云同步目录（OneDrive、Dropbox 本地目录）

## 6. 与规则分类的隔离契约

Portal Fence 的 `Files` 集合**只受 FolderPortalWatcher 管理**，规则引擎与 `ReEvaluateClassifiedFiles` 都不应触及：

- **规则分类不应清理 portal**：`ReEvaluateClassifiedFiles` 在每次规则保存（含 SettingsWindow 一键保存的全事件触发）时跑一遍，按"规则匹配是否指向本 fence"过滤所有 tab 的 Files。Portal 内的外部文件夹文件没有任何规则会把它们指向当前 portal fence，因此**该函数必须在循环开头 `if (tab.IsPortalMode) continue;`**——否则 portal fence 会被瞬间清空（[bug 16](../bug/portal_files_wiped_after_save_settings.md)）。
- **Watcher 是增量同步,不是完整重灌**：`FolderPortalWatcher.ContentsChanged` 只在文件系统真发生变化时增删，不会因为 `Files` 被外部代码清空而主动重灌。所以任何"清空 portal Files"的代码路径都是潜在 bug——要在更早处阻止清空，而不是寄希望于 watcher 重建。
- **App.xaml.cs 中所有 `tab.RemoveFile` / `Files.Clear` 类的全局操作**都需要先判断 `IsPortalMode` 决定是否跳过。
