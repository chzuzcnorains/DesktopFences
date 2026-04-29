# 数据持久化

## 1. 存储格式

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

## 2. 保存策略

- **自动保存**：Fence 位置/大小变更后 debounce 2 秒自动保存
- **即时保存**：文件增删、规则变更即时保存
- **快照保存**：用户手动触发，完整序列化当前状态
- **原子写入**：写入临时文件 → rename 覆盖，防止写入中途崩溃导致数据丢失

`JsonLayoutStore` 的全部读写一律走 `JsonFileStore.ReadAsync<T>` / `JsonFileStore.WriteAtomicAsync<T>` 助手，
原子写入语义集中实现。新增任何 JSON 持久化文件请直接调用这两个助手，不要重复写
"`OpenRead/DeserializeAsync` 或 `Create/SerializeAsync + File.Move`" 模板。

## 3. 数据模型

### FenceDefinition

```csharp
public Guid Id { get; set; }
public string Title { get; set; }
public double X { get; set; }
public double Y { get; set; }
public double Width { get; set; }
public double Height { get; set; }
public double ExpandedHeight { get; set; }
public bool IsRolledUp { get; set; }
public Guid? TabGroupId { get; set; }   // 所属标签组 ID（null 表示独立窗口）
public int TabOrder { get; set; }        // 在标签组内的顺序
public string PortalPath { get; set; }  // 文件夹映射路径
public string BackgroundColor { get; set; }
public string TitleBarColor { get; set; }
public string TextColor { get; set; }
public double Opacity { get; set; }
public List<string> FilePaths { get; set; }
```

### ClassificationRule

```csharp
public Guid Id { get; set; }
public string Name { get; set; }
public RuleMatchType MatchType { get; set; }  // Extension, NameGlob, DateRange, SizeRange, Regex, IsDirectory
public string Pattern { get; set; }
public Guid TargetFenceId { get; set; }
public int Priority { get; set; }
public bool IsEnabled { get; set; }
```

### AppSettings

```csharp
public string DefaultFenceColor { get; set; } = "#CC1E1E2E"
public string DefaultTitleBarColor { get; set; } = "#44FFFFFF"
public string DefaultTextColor { get; set; } = "#DDEEEEEE"
public double DefaultOpacity { get; set; } = 1.0
public int TitleBarFontSize { get; set; } = 13
public int SnapThreshold { get; set; } = 10
public bool QuickHideEnabled { get; set; } = true
public bool StartWithWindows { get; set; } = false
public bool StartMinimized { get; set; } = true
public bool CompatibilityMode { get; set; } = false
public bool DebugLogging { get; set; } = false
public string TabStyle { get; set; } = "Flat"
public bool UseCustomFileIcons { get; set; } = true
public int IconSize { get; set; } = 44
public string AccentColor { get; set; } = "#7AA7E6"
public int FenceBgHue { get; set; } = 220
public double FenceOpacity { get; set; } = 0.85
public int FenceBlurRadius { get; set; } = 26
public List<string> RecentClosedFences { get; set; } = new()
```

## 4. RecentClosedFences FIFO

**用途**：用户关闭 Fence 后保留可恢复入口，避免误关。

**结构**：`AppSettings.RecentClosedFences` 是 `List<string>`，每条为 `RecentClosedFenceEntry`（App 内部 wrapper）的 JSON 序列化串：

```csharp
class RecentClosedFenceEntry
{
    FenceDefinition Definition;     // 完整 fence 定义
    DateTimeOffset  ClosedAt;       // 真实关闭时间（用于"X 分钟前"显示）
}
```

旧格式（bare `FenceDefinition` JSON，无 wrapper）的设置文件仍可读取——`App.DeserializeRecentClosedEntry` 通过检测根级是否有 `"Definition"` 属性决定走新/旧分支，旧条目的 `ClosedAt` 兜底为读取时刻。

**写入规则**（`App.RecordRecentlyClosedFences`）：
- 触发条件：`host.Closed` 满足 `!IsMerging && !IsBeingReplaced && !_isShuttingDown`。
- Tab 组内每个 Tab 单独入栈（每条都带写入时刻 `ClosedAt`），前插（最新在 index 0），上限 20，超出从尾部丢弃。
- 写入后 `SaveSettingsAsync` 持久化，托盘 `ShowBalloonTip` 提示，重建托盘菜单。

**关闭语义区分**：
- `FenceHost.IsMerging` — 合并到其他 host，跳过 page/portal 清理与 FIFO 写入。
- `FenceHost.IsBeingReplaced` — 快照恢复 / 显示器重配 / 重置布局触发的批量关闭，正常清理但不入 FIFO。
- `App._isShuttingDown` — 退出菜单 / OnExit 全程进入此态，整波关闭都跳过 FIFO 写入。

**用户操作**：
- **恢复**（`App.RestoreClosedFenceById`）：弹出条目 → 清空 `TabGroupId/TabOrder`（原组已不存在）→ `SpawnFenceWindow(bringToFront: true)` 重建 → 保存 settings 并重建托盘菜单。
- **删除**（`App.DeleteClosedFenceById`）：仅从 FIFO 摘除条目并持久化 + 重建托盘菜单，不创建窗口。设置 → Fence 管理 → 最近关闭中每张卡片独立的"删除"按钮触发；设置面板通过 `SettingsWindow.NotifyClosedFenceRemoved(id)` 即时刷新计数和卡片网格，无需关闭重开。

## 5. 布局导入 / 导出

**Schema**（`App.LayoutExport`，Version=1）：

```csharp
{
    "Version": 1,
    "ExportedAt": "ISO-8601 时间戳",
    "Fences":   [ FenceDefinition, ... ],   // 含 TabGroupId / TabOrder，迁入侧按组复原
    "Rules":    [ ClassificationRule, ... ],
    "Settings": AppSettings                 // 完整设置含 RecentClosedFences（导入侧通常被新值覆盖）
}
```

**文件名**：`desktopfences-layout-{yyyyMMdd-HHmm}.dfences.json`（双扩展名让用户用 `.json` 或 `.dfences.json` 都能匹配）。

**导出**（`App.ExportLayout`）：
- `SaveFileDialog` 选择输出路径。
- 收集当前 `_fenceWindows` 所有 Tab 的 Model + `_rules` + `_appSettings` 写入缩进 JSON。

**导入**（`App.ImportLayout`）：
- `OpenFileDialog` 接受 `.dfences.json` / `.json` / 任意。
- 反序列化失败 / `null` 报错并返回。
- 二次确认（替换数量 + 导出时间）。
- `IsBeingReplaced=true` 关闭所有 host（不写 RecentClosedFences），替换 `_rules / _appSettings`，立即 `ApplyIconAppearance + ApplyFenceShadow`，再 `SpawnFencesWithGroups` 还原 fences，保存 settings/rules，重建托盘菜单。

