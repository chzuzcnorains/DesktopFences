# Phase 10: 视觉系统升级与原型落地

**目标**：将 WPF 工程视觉与交互对齐 `desktop-v2.html` v2 原型。涵盖颜色系统、文件图标、Fence 外观反馈、设置窗口侧栏导航重构。

**关键决策**：
- 主题色从 OKLCH 近似为 sRGB（WPF 不支持 OKLCH），新增语义 Brush 而不破坏已有 key
- 文件图标采用 14 套自绘彩色文档图标 + 字母叠加方案，保留 Shell 图标作为设置可切换项
- Acrylic 玻璃质感用半透明 Background + DropShadowEffect 近似（WPF 不支持原生 `backdrop-filter`）
- SettingsWindow 从顶部 TabControl 改为 220px 侧栏导航 + 7 面板

## 16.1 文件类型图标资源（批次 1）

**新增文件**：

| 文件 | 说明 |
|---|---|
| `Themes/FileTypes.xaml` | 14 个 `DrawingImage`：FileIconFolder/Doc/Xls/Ppt/Pdf/Img/Video/Music/Code/Zip/Exe/Txt/Link/Ttf |
| `Converters/FileKindToIconConverter.cs` | 扩展名 → DrawingImage 资源 Key 查表 |
| `Converters/LabelLenToFontSizeConverter.cs` | `length==0→0`（文件夹隐藏字母）、`≤2→11`、`>2→8.5` |

**FileItemViewModel 扩展**：
- `string KindLabel` — 基于扩展名查表返回 `W`/`X`/`P`/`PDF`/`IMG`/`MP4`/`♪`/`<>`/`ZIP`/`EXE`/`TXT`/`↗`/`Aa`，文件夹返回 `""`

## 16.2 文件图标双模式切换（批次 2）

**AppSettings 新增**：
- `bool UseCustomFileIcons { get; set; } = true` — 自绘/Shell 切换开关
- `int IconSize { get; set; } = 44` — 图标大小 28-64

**FencePanel.xaml 内嵌 DataTemplate**：
- `CustomFileTile` — 使用 FileTypes DrawingImage + KindLabel 字母叠加
- `ShellFileTile` — 使用 `{Binding Icon}` 走 ShellIconExtractor
- `FileIconSelector`（`DataTemplateSelector`）— 根据 `UseCustomFileIcons` 选择模板

**刷新机制**：`FencePanel.RefreshFileTileTemplate()` 在启动加载和 `OnSettingsSaved` 时调用。

## 16.3 DarkTheme 色板升级（批次 3）

**OKLCH → sRGB 近似色板**：

| Key | 旧值 | 新值 | 用途 |
|---|---|---|---|
| `AccentColor` | `#6688CC` | `#7AA7E6` | 主强调色 |
| `DangerColor` | `#CC4444` | `#E0412B` | 危险动作 |
| `TextPrimaryColor` | `#EEEEEE` | `#E8ECF4` | 正文 |
| `TextSecondaryColor` | `#AACCCCCC` | `#AEB8CC` | 次要文字 |
| `FenceBaseColor` | `#1E1E2E` | `#1A2036` | Fence 背景基 |
| `FenceBackgroundBrush` | `#CC1E1E2E` | `#CC1A2036` | Fence 半透明背景 |

**新增语义 Brush**：

| Key | 值 | 用途 |
|---|---|---|
| `AccentStrongBrush` | `#5A82DC` | 选中/glow |
| `MergeTargetBrush` | `#6BD49A` | 合并高亮（teal） |
| `TitleBarActiveBrush` | `#594E78B8` | 激活 Tab 背景 |
| `FenceBorderBrush` | `#17FFFFFF` | Fence 常态边框 |
| `FenceBorderStrongBrush` | `#2EFFFFFF` | Fence 聚焦边框 |
| `TextFaintBrush` | `#7B8296` | 淡化文字 |
| `FocusBorderBrush` | `#887AA7E6` | 输入框焦点边框 |

## 16.4 FencePanel 外观与三态 glow 反馈（批次 4）

**FencePanelViewModel 新增属性**：
- `bool IsFocused` — 窗口激活状态
- `bool IsDropHover` — 文件拖入悬停
- `bool IsMergeTarget` — 合并拖拽目标

**FencePanel.xaml 变更**：
- `CornerRadius` 8 → 10
- `FenceBorder.Effect` 改引用 `FenceShadowEffect`
- `IsFocused=True` 时 `BorderBrush` 切到 `FenceBorderStrongBrush`
- 新增 `GlowBorder` 层，Style Triggers 按优先级 IsMergeTarget > IsDropHover > IsFocused

## 16.5 ContextMenu 与 SearchWindow 视觉对齐（批次 5）

**DarkTheme.xaml 调整**：
- `DarkContextMenuStyle`：背景 `#EB1C2030`、DropShadow BlurRadius=40/Opacity=0.5、圆角 8、Padding 6,4
- `DarkMenuItemStyle`：Padding 12,6、字号 12.5、Foreground 改 `DynamicResource`
- 新增 `DarkDangerMenuItemStyle`

**SearchWindow.xaml 对齐**：
- 宽 520 / 高 420，背景 `#EB161A2A`
- 输入框底部 1px 分隔线
- IconSearch 18px，结果项 3 栏布局

## 16.6 AppSettings 扩展与 TabStyles 精修（批次 6）

**AppSettings 新增字段**：
```csharp
public string AccentColor { get; set; } = "#7AA7E6";
public int FenceBgHue { get; set; } = 220;
public double FenceOpacity { get; set; } = 0.85;
public int FenceBlurRadius { get; set; } = 26;
public List<string> RecentClosedFences { get; set; } = new();
```

## 16.7 SettingsWindow 侧栏导航重构（批次 7）

**架构变更**：从顶部 TabControl（2 Tab）改为 220px 侧栏 + 7 面板内容区。

**7 个面板 UserControl**（`Controls/Settings/` 目录）：
- `GeneralSettingsPane` — 常规设置
- `RulesSettingsPane` — 分类规则
- `AppearanceSettingsPane` — 外观设置
- `FencesManageSettingsPane` — Fence 管理
- `ShortcutsSettingsPane` — 快捷键
- `AdvancedSettingsPane` — 高级设置
- `AboutSettingsPane` — 关于

## 16.8 AppearanceSettingsPane + FencePreviewControl（批次 8）

**FencePreviewControl** — 实时预览 mini fence：
- 外层 Border 模拟桌面壁纸
- 内嵌 mini fence Border（MinW 280 / MaxW 380）
- Tab strip 容器 + 3 假 Tab
- WrapPanel 6 个示例 tile

## 16.9 FencesManage / Shortcuts / Advanced / About 四面板（批次 9）

**FencesManageSettingsPane**：
- 顶部 `+ 新建 Fence` 按钮
- Segmented 切换（活动 / 最近关闭）
- 活动段 5 列表格，按 TabGroup 分组
- 最近关闭段 2 列卡片网格

**ShortcutsSettingsPane**：
- 3 个分组卡片展示快捷键

**AdvancedSettingsPane**：
- 桌面嵌入 / 诊断 / 危险操作三卡片

**AboutSettingsPane**：
- 72×72 logo + 版本号 + 4 列 stats + 链接

## 16.10 Hue / Opacity / Blur 接入 + 最近关闭 FIFO（批次 10）

**Hue / Opacity / Blur 实时生效**：
- `App.ComputeFenceBgColor(hue, opacity)` 用与预览一致的 HSL（S=0.30, L=0.18）+ alpha 通道公式生成 `#AARRGGBB` 字符串。
- `OnSettingsSaved` / `SpawnFenceWindow` / `DetachTab` 改为传入派生色，替换原 `DefaultFenceColor`（字段保留以兼容旧布局）。
- `App.ApplyFenceShadow(settings)` 替换 `Application.Resources["FenceShadowEffect"]` 为新 `DropShadowEffect`；`FencePanel.xaml` 的 Effect 改 `DynamicResource` 让现有 Fence 即时刷新。
- 启动 `LoadFencesAsync` 读取设置后立即应用。

**RecentClosedFences FIFO（≤20）**：
- `FenceHost` 新增 `IsBeingReplaced` 标志，区分"用户关闭"与"快照恢复 / 显示器切换 / 重置"等被动关闭。
- `host.Closed` 满足 `!IsMerging && !IsBeingReplaced && !_isShuttingDown` 时调用 `RecordRecentlyClosedFences`：序列化每个 Tab 的 `FenceDefinition`，前插到 `_appSettings.RecentClosedFences`，超 20 截尾。
- 持久化到 `settings.json`，托盘 `ShowBalloonTip` 提示 2s。
- "退出"菜单项预先置位 `_isShuttingDown=true` 以跳过整波关闭事件的 FIFO 写入。

**托盘菜单"恢复最近关闭"子菜单**：
- 与"布局快照"并排，`DropDownOpening` 时读取 FIFO 渲染 `{Title} · {FileCount} 文件` 列表。
- 点击调用 `RestoreClosedFenceById`：弹出条目、清掉 `TabGroupId/TabOrder`（原组已不存在）、`SpawnFenceWindow` 重建。
- 提供"清空列表"项。空状态显示禁用项"（无最近关闭的 Fence）"。

## 16.11 导入 / 导出布局 + 文档收尾（批次 11）

**导出**（`App.ExportLayout`）：
- `SaveFileDialog` 默认文件名 `desktopfences-layout-{yyyyMMdd-HHmm}.dfences.json`。
- 写入 `LayoutExport { Version=1, ExportedAt, Fences[], Rules[], Settings }`，缩进 JSON。
- 失败弹错误对话框；成功 `ShowToast` 报告条数与路径。

**导入**（`App.ImportLayout`）：
- `OpenFileDialog` 接受 `.dfences.json` / `.json`。
- 读取并反序列化 `LayoutExport`；解析失败 / 内容为 null 时弹警告。
- 二次确认对话框（替换数量 + 导出时间）。
- 关闭所有现 host（`IsBeingReplaced=true`，避免污染 RecentClosedFences），替换 `_rules / _appSettings`，立即 `ApplyIconAppearance + ApplyFenceShadow`，再 `SpawnFencesWithGroups` 还原 fences，保存 settings/rules，重建托盘菜单。

**文档收尾**：
- `docs/plan/phase-10.md` 补全批次 10 / 11 章节并删除"待完成"列表。
- `docs/plan/complete.md` 加入 Phase 10 ✅。
- `docs/plan/todo.md` 移除 Phase 10 整段。
- `README.md` 改写为简明的功能介绍 + 构建运行指引。
