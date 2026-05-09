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
- **Tab 拖拽排序**：Tab 数 ≥ 2 时，按住 tab 按钮拖动超过 `SystemParameters.MinimumHorizontalDragDistance` 后激活拖拽，TabStrip 上 accent 色细竖线作为插入指示符跟随鼠标；释放时把 tab 移到目标缝隙，写入 `_tabs[i].Model.TabOrder = i` 并通过 `FenceContent.RaiseInteractionEnded()` 触发 `RequestAutoSave`。普通点击（位移未到阈值）走原 `Click` 切换 active tab，不被误判。
  - 实现位置：[FenceHost.xaml.cs](../../src/DesktopFences.UI/Controls/FenceHost.xaml.cs)（`OnTabStripPreviewMouseMove` / `OnTabStripPreviewMouseLeftButtonUp` / `ComputeTabDropIndex` / `PositionTabDropIndicator`）+ [FenceHost.xaml](../../src/DesktopFences.UI/Controls/FenceHost.xaml) 中的 `TabDropIndicator` Rectangle
  - 仅同 fence 内重排序；跨 fence 的 tab 移动由现有 fence-overlap 合并 / TabDetachRequested 路径承担
  - **监听挂在 FenceHost (Window) 级别**：`AddHandler(PreviewMouseMoveEvent / PreviewMouseLeftButtonUpEvent, ..., handledEventsToo: true)`。挂在 `TabStrip` 上的版本会因 Button 内部 mouse-capture + Handled 标记而错过 mouse up
  - **Capture target 为 Window，mode 为 `CaptureMode.SubTree`**：`Mouse.Capture(this, CaptureMode.SubTree)`——SubTree 模式保留子控件的事件路由（鼠标悬停子按钮仍生效），但保证 mouse up 一定路由到 Window 级 handler
  - **dropIndex 在虚拟序列上计算**（剔除被拖 tab 后的剩余序列），唯一 noop 条件是 `dropIndex == from`。这样拖到相邻位置也能生效，避免"看似拖了但没动"的体验

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
