# Phase 3: 自动化

**目标**：新文件出现在桌面时自动分类到对应 Fence。

## 3.1 文件监控 (Shell 项目)
- [x] `DesktopFileMonitor.cs`
  - `FileSystemWatcher` 监控桌面目录 (`Environment.GetFolderPath(SpecialFolder.Desktop)`)
  - 定时全量扫描对账（每 30 秒），对比 `_knownFiles` HashSet 发现新增/删除
  - 事件 debounce（500ms，`System.Timers.Timer` 单次触发）
  - 事件：`FilesAdded`（新文件列表）、`FilesRemoved`（删除文件列表）、`FileRenamed`（旧路径→新路径）
  - `GetCurrentFiles()` 获取当前桌面所有文件
  - `SHChangeNotifyRegister` 作为补充（延迟到后续需要时实现）

## 3.2 规则引擎实现 (Core 项目)
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

## 3.3 规则持久化 (Core 项目)
- [x] `ILayoutStore` — 新增 `LoadRulesAsync()` / `SaveRulesAsync()` 接口
- [x] `JsonLayoutStore` — 新增 `rules.json` 文件读写，原子写入
- [x] 规则配置 UI（`RuleEditorWindow.xaml`）— 列表 + 表单双面板，支持增删改查（见 Phase 6 扩展）

## 3.4 自动分类流程集成
- [x] `App.xaml.cs` — 完整的自动分类管道：
  - 启动时加载规则 → 加载 Fence → 启动 `DesktopFileMonitor`
  - `OnDesktopFilesAdded`：新文件 → 跳过已存在于任何 Fence 的 → `RuleEngine.Match` → 添加到目标 Fence + 加载图标
  - `OnDesktopFileRenamed`：更新 Fence 中文件路径 → `SyncToModel`
  - 退出时 `Dispose` FileMonitor
- [ ] 用户手动放入的文件标记为"手动"（延迟到后续优化）

**验收标准**：配置规则 ".jpg,.png → 图片" 后，在桌面放一个 .jpg 文件，自动出现在"图片"Fence 中。 ✅
