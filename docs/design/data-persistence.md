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
