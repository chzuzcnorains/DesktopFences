# Fence 容器设计

## 1. WPF 控件结构

```xml
<FencePanel>
  ├─ <TitleBar>              <!-- 标题栏：标题文字、折叠按钮、Tab 标签 -->
  │    ├─ <TextBlock />      <!-- Fence 名称 -->
  │    ├─ <TabStrip />       <!-- 多 Tab 合并时显示 -->
  │    └─ <RollupButton />   <!-- 折叠/展开 -->
  │
  ├─ <IconArea>              <!-- 文件图标区域 -->
  │    ├─ <VirtualizingWrapPanel />  <!-- 图标视图（默认） -->
  │    ├─ <VirtualizingStackPanel /> <!-- 列表/详情视图 -->
  │    └─ <ScrollViewer />
  │
  └─ <ResizeGrips>           <!-- 八向调整大小手柄 -->
       ├─ Top/Bottom/Left/Right
       └─ TopLeft/TopRight/BottomLeft/BottomRight
</FencePanel>
```

## 2. 交互行为

- **拖动标题栏**：移动 Fence 位置（带 Snap 吸附逻辑）
- **拖动边缘**：调整 Fence 大小
- **点击标题栏收起箭头（▲/▼）**：Rollup 折叠/展开（只显示标题栏，高度缩小到 ~32px）
- **鼠标悬停折叠态**：展开 Fence（可配置为 click-to-open）
- **右键标题栏**：Fence 设置菜单（重命名、颜色、删除、规则配置）
- **右键文件图标**：Shell 原生右键菜单（通过 IContextMenu COM 接口）
- **双击文件图标**：ShellExecute 打开文件
- **拖入文件**：从 Explorer / 桌面拖入文件到 Fence
- **拖出文件**：从 Fence 拖出文件到 Explorer / 桌面 / 其他 Fence

## 3. 文件图标渲染

### 双模式切换

**AppSettings 配置**：
- `bool UseCustomFileIcons { get; set; } = true` — 自绘/Shell 切换开关
- `int IconSize { get; set; } = 44` — 图标大小 28-64

**文件类型图标**（14 套自绘彩色文档图标 + 字母叠加）：
| 类型 | 扩展名 | 标签 |
|------|--------|------|
| Folder | — | "" |
| Doc | .doc, .docx | W |
| Xls | .xls, .xlsx | X |
| Ppt | .ppt, .pptx | P |
| Pdf | .pdf | PDF |
| Img | .jpg, .png, .gif... | IMG |
| Video | .mp4, .mkv, .avi... | MP4 |
| Music | .mp3, .wav, .flac... | ♪ |
| Code | .cs, .js, .py... | <> |
| Zip | .zip, .rar, .7z | ZIP |
| Exe | .exe, .msi | EXE |
| Txt | .txt, .md, .rtf | TXT |
| Link | .lnk, .url | ↗ |
| Ttf | .ttf, .otf | Aa |

**FencePanel.xaml 内嵌 DataTemplate**：
- `CustomFileTile` — 使用 FileTypes DrawingImage + KindLabel 字母叠加
- `ShellFileTile` — 使用 `{Binding Icon}` 走 ShellIconExtractor
- `FileIconSelector`（`DataTemplateSelector`）— 根据 `UseCustomFileIcons` 选择模板

## 4. 外观与三态 Glow 反馈

**FencePanelViewModel 新增属性**：
- `bool IsFocused` — 窗口激活状态
- `bool IsDropHover` — 文件拖入悬停
- `bool IsMergeTarget` — 合并拖拽目标

**FencePanel.xaml 变更**：
- `CornerRadius` 8 → 10（含 showTabs 模式 `0,0,10,10`）
- `FenceBorder.Effect` 改引用 `FenceShadowEffect`，`BorderBrush` 换 `FenceBorderBrush`
- `IsFocused=True` 时 `BorderBrush` 切到 `FenceBorderStrongBrush`
- 新增 `GlowBorder` 层，Style Triggers 按优先级 IsMergeTarget（teal glow）> IsDropHover（accent 蓝色 glow）> IsFocused（白色 glow）切换 `DropShadowEffect`

**交互实现**：
- `OnDragOver` → `IsDropHover=true`；`OnDragLeave`/`OnDrop` → 清零
- `OnLoaded`/`OnUnloaded` 订阅 host Window `Activated`/`Deactivated` 同步 `IsFocused`
