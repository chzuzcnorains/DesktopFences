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
  - 重命名对话框为**非模态**（`Show()` + `RenameConfirmed` 事件回调，实例缓存防重复打开）——自定义 WPF 窗口禁止 `ShowDialog()`，模态会 EnableWindow(FALSE) 同线程所有 fence/overlay（bug 21/29）
- **右键文件图标**：Shell 原生右键菜单（通过 IContextMenu COM 接口，COM 对象 try/finally 确定性释放，bug 28）
- **双击文件图标**：ShellExecute 打开文件（`OpenFile` 返回 bool，文件已删/无关联不再抛异常崩溃，bug 26）
- **拖入文件**：从 Explorer / 桌面拖入文件到 Fence
- **拖出文件**：从 Fence 拖出文件到 Explorer / 桌面 / 其他 Fence
- **拖拽语义（bug 27，2026-06-12）**：应用内拖拽（fence↔fence、overlay→fence）= **移动**，Explorer 来源 = **复制引用**
  - 内部拖拽的 `DataObject` 带自定义格式标记 `InternalDragFormats.Marker`
  - `FencePanel.OnDrop` 显式回报 Effects：带标记且实际新增 → `Move`（源端删除条目）；文件已在本 fence（含自拖自）→ `None`（防止源端误删）；无标记（Explorer）→ 恒 `Copy`（回报 Move 会让 Explorer 删除磁盘源文件）
  - 同一文件不再同时存在于多个 fence；fence 内文件路径比较统一 `OrdinalIgnoreCase`（bug 31）
- **Tab 拖拽排序**：Tab 数 ≥ 2 时，按住 tab 按钮拖动超过 `SystemParameters.MinimumHorizontalDragDistance` 后激活拖拽，TabStrip 上 accent 色细竖线作为插入指示符跟随鼠标；释放时把 tab 移到目标缝隙，写入 `_tabs[i].Model.TabOrder = i` 并通过 `FenceContent.RaiseInteractionEnded()` 触发 `RequestAutoSave`。普通点击（位移未到阈值）走原 `Click` 切换 active tab，不被误判。
  - 实现位置：[FenceHost.xaml.cs](../../src/DesktopFences.UI/Controls/FenceHost.xaml.cs)（`OnTabStripPreviewMouseMove` / `OnTabStripPreviewMouseLeftButtonUp` / `ComputeTabDropIndex` / `PositionTabDropIndicator`）+ [FenceHost.xaml](../../src/DesktopFences.UI/Controls/FenceHost.xaml) 中的 `TabDropIndicator` Rectangle
  - 条内横向拖动 = 重排序；**垂直拖出 tab 条**（相对 `TabStripBorder` 越界 > `TabDetachThreshold`=24px）= 撕离，复用 `TabDetachRequested` → `App.DetachTab`，效果等同菜单"分离为独立 Fence"（bug 34）；跨 fence 的 tab 移动仍由 fence-overlap 合并承担
  - **SubTree 捕获副作用**：`Mouse.Capture(this, CaptureMode.SubTree)` 期间子控件仍收 mouse 事件，活动 tab 面板的文件 tile `FileItem_MouseMove` 会被误触发并发起 OLE 文件拖拽。`FencePanel.FileItem_MouseMove` 用 `Mouse.Captured == _hostWindow` 守卫拦截（bug 34）
  - **监听挂在 FenceHost (Window) 级别**：`AddHandler(PreviewMouseMoveEvent / PreviewMouseLeftButtonUpEvent, ..., handledEventsToo: true)`。挂在 `TabStrip` 上的版本会因 Button 内部 mouse-capture + Handled 标记而错过 mouse up
  - **Capture target 为 Window，mode 为 `CaptureMode.SubTree`**：`Mouse.Capture(this, CaptureMode.SubTree)`——SubTree 模式保留子控件的事件路由（鼠标悬停子按钮仍生效），但保证 mouse up 一定路由到 Window 级 handler
  - **dropIndex 在虚拟序列上计算**（剔除被拖 tab 后的剩余序列），唯一 noop 条件是 `dropIndex == from`。这样拖到相邻位置也能生效，避免"看似拖了但没动"的体验

## 2.5 框选与多选（2026-06-25）

Fence 文件列表支持框选（rubber-band）+ 多选 + 整组拖拽。与桌面覆盖层的框选（见 [desktop-icon-overlay.md §11](desktop-icon-overlay.md)）不同：**fence 是普通可命中 WPF 窗口，无需底层钩子**，直接用标准 WPF 鼠标捕获 + 一层覆盖 `Canvas/Rectangle`（`MarqueeLayer`/`MarqueeRect`，与 `FileListBox` 同 Grid 单元 + 同 Margin 以对齐坐标，`IsHitTestVisible=False`）。

- **选中模型**：复用既有 `FileItemViewModel.IsSelected`（DataTemplate 内 `DataTrigger IsSelected→SelectedBrush` 驱动高亮）。`FencePanel` 实现 `ISelectionContainer`。
- **框选**：`FileListBox.PreviewMouseLeftButtonDown` 命中点向上回溯——遇 `ListBoxItem`/`ScrollBar` 则放行（交给图标 Border 事件 / 滚动条），仅**空白区**起框选：捕获鼠标、画 `MarqueeRect`、`MouseMove` 时遍历 `Files`，对每个 `ItemContainerGenerator.ContainerFromItem(item)`（虚拟化下仅可见项有容器）用 `TransformToVisual(FileListBox)` 求 bounds 与选框 `IntersectsWith` → 设 `IsSelected`。
- **多选手势**（`FileItem_MouseLeftButtonDown`）：Ctrl 点击 toggle；Shift 点击按 `Files` 下标做范围选（锚点 `_selectionAnchor`）；点已选中项保留多选（供整组拖拽）；普通点击清空+单选。
- **整组拖拽**（`FileItem_MouseMove`）：被拖项已选中且选中数>1 → `DataObject` 携**全部**选中路径（沿用 `InternalDragFormats.Marker`/`SourceFenceId`），`result==Move` 时移除全部被拖路径。目标 fence `OnDrop` 已按 `string[]` 处理，无需改动。
- **全局互斥单一选区**：`DesktopSelectionCoordinator`（弱引用注册各 `FencePanel` + 桌面 overlay）。任一容器开始新选择前调 `NotifyActivated(self)`，协调器清空**其它**容器选中——同一时刻只有一个容器持有选区（类资源管理器）。App 持有单例，经 `FenceHost` 构造参数透传给 `FenceContent.SelectionCoordinator`，并赋给 overlay。

**已知限制**：暂无 Delete 键批量删除（fence 窗口 `WS_EX_NOACTIVATE` 拿不到键盘焦点，本期产品决策不做，删除仍走单文件原生右键）；右键多选仍弹单文件 Shell 菜单；多文件同 fence 自拖不重排（落到 `OnDrop` add/remove 空操作，安全无数据损失，单文件自拖重排见 §3.5 Phase 14c）；框选不自动滚动，仅命中可见项。

**涉及文件**：[DesktopSelectionCoordinator.cs](../../src/DesktopFences.UI/Controls/DesktopSelectionCoordinator.cs)（新增）、[FencePanel.xaml](../../src/DesktopFences.UI/Controls/FencePanel.xaml)/[.xaml.cs](../../src/DesktopFences.UI/Controls/FencePanel.xaml.cs)、[FenceHost.xaml.cs](../../src/DesktopFences.UI/Controls/FenceHost.xaml.cs)、[DesktopIconOverlay.xaml.cs](../../src/DesktopFences.UI/Controls/DesktopIconOverlay.xaml.cs)、[App.xaml.cs](../../src/DesktopFences.App/App.xaml.cs)。

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

## 3.5 视图模式与排序（Phase 14）

每个 fence 独立持有呈现方式与排序设置，持久化到 `fences.json`（`FenceDefinition.ViewMode` / `SortBy` / `SortDirection`）。入口在标题栏 ⋯ 菜单（`FencePanel.ShowTitleBarMenu`）。功能分三个子阶段交付，详见 [phase-14.md](../plan/phase-14.md)。

### 排序（Phase 14a — 已完成）

- **排序字段**（`SortField`）：`Name` / `Extension` / `Size` / `DateModified` / `DateCreated` / `Manual`
  - `Manual` 为 Phase 14 新增——用户手动拖拽顺序，`FilePaths` 顺序即持久化的单一事实来源；此模式下自动重排被跳过
- **方向**（`SortDirection`）：`Ascending` / `Descending`；`Manual` 模式下方向菜单项禁用
- **排序器**：[FileSorter.cs](../../src/DesktopFences.Core/Services/FileSorter.cs)（Core 层，纯函数、可单测）
  - `Sort<T>(items, field, direction, keySelector)` 接收预解析的 `FileSortKey`（Name/Extension/SizeBytes/DateModified/DateCreated），**不碰磁盘**；LINQ `OrderBy` 稳定，等值保持原序
  - `AdjustMoveIndex(oldIndex, insertIndex, count)` 为手动重排（14c）准备的 Move 语义索引修正纯函数
- **元数据**：[FileItemViewModel](../../src/DesktopFences.UI/ViewModels/FileItemViewModel.cs) 的 `SizeBytes` / `DateModified` / `DateCreated` 走 lazy 加载（首次访问读一次 `FileInfo`，全程 try/catch，目录/失败回退 `-1`/`default`），`RefreshMetadata()` 在重命名（`FilePath` setter）与 portal 刷新时失效缓存
- **重排时机**：`FencePanelViewModel.ResortAfterChange()` 在文件增删/重命名后调用（`Manual` 模式为 no-op）。调用点：`FencePanel.OnDrop`、`App.SyncPortalContents`、`App.OnDesktopFilesAdded`、规则分类、`App.OnDesktopFileRenamed`
  - `AddFile` 改为返回新建项且**不在内部排序**，保住调用方"新项在末尾"的假设（图标加载/落入动画用 `ContainerFromItem` 定位，重排后仍正确）
- **菜单**：`BuildSortSubmenu()`——字段 + 升/降序，`IsChecked` 实时读 ViewModel；portal fence 的"手动排列"灰显（folder-driven，手动序无法持久化）
- **持久化兼容**：`JsonStringEnumConverter` 读到未知枚举字符串会抛 `JsonException` → `JsonLayoutStore.ReadResilientAsync` 备份 `.corrupt-*` 并回退空（旧版本读到本版 `Manual` 会整文件降级，但有备份不丢数据，属既有机制）

### 视图模式（Phase 14b — 已完成）

`ViewMode`：`Icon`（现状）/ `List`（小图标+单行名，纵向多列流式）/ `Detail`（单列+"名称/大小/修改时间"列头，列头点击切换排序字段，同字段再点反转方向）。

- **单 ListBox + Style DataTrigger 切 `ItemsPanel`**（否决多 ListBox / ListView+GridView）：[FencePanel.xaml](../../src/DesktopFences.UI/Controls/FencePanel.xaml) 定义 `IconItemsPanel`（横向 WrapPanel）/ `ListItemsPanel`（纵向 WrapPanel，列满换列）/ `DetailItemsPanel`（VirtualizingStackPanel），`FileListBox` 的 Style 按 `ViewMode` 切面板与滚动方向（List 走水平滚动 + `CanContentScroll=False` 像素滚动；Detail 走垂直虚拟化滚动）。**`ScrollViewer.*` 必须写在 Style 里**——元素上的局部 attribute 会压死触发器
- **模板**：`ListFileTile` / `DetailFileTile`（同样把鼠标三事件挂在根 Border，零成本继承双击/拖拽/Shell 右键）。小图标按 fence 的 `EffectiveIconStyle` 用 DataTrigger 切三个 Image（App=KindToIcon / System=SysKindToIcon / Shell=Icon），**不带字母叠加**（16–20px 不可读）。Detail 三列固定宽（名称 `*` / 大小 80 / 时间 118），**不用 SharedSizeGroup**（会破坏虚拟化）；`ListBoxItem.HorizontalContentAlignment=Stretch` 让 Detail 行铺满列宽
- **TemplateSelector**：[FileIconTemplateSelector](../../src/DesktopFences.UI/Controls/FileIconTemplateSelector.cs) 先按 `ViewMode` 分流 List/Detail，否则回落图标风格三分支
- **Detail 列头**：身体 Grid 行改 `30/Auto/*`，列头 Border 仅 Detail 可见（DataTrigger），`DetailHeader_Click` 切字段/反转方向，`UpdateSortGlyphs()` 在活动列追加 ▲/▼
- **运行时刷新**：`OnViewModelPropertyChanged` 监听 `ViewMode` → 复用 `RefreshFileTileTemplate()`（Recycling 虚拟化下重选模板）；监听 `SortBy`/`SortDirection` → `UpdateSortGlyphs()`
- **尺寸资源**：[FileTile.xaml](../../src/DesktopFences.UI/Themes/FileTile.xaml) 的 `FileListItemWidth`(168) / `FileListIconSize`(18) / `DetailRowHeight`(24)
- **菜单**：`BuildViewModeSubmenu()`（图标/列表/详细信息，对勾）

### 手动拖拽重排（Phase 14c — 已完成）

fence 内拖拽图标重排并持久化，重排后自动切 `Manual`（Explorer 语义）。

- `InternalDragFormats.SourceFenceId`：file-tile 拖拽 DataObject 携带来源 fence `Id`；`FileItem_MouseMove` 写入
- `FencePanelViewModel.ReorderFile(filePath, insertIndex)`：`FileSorter.AdjustMoveIndex` 修正 Move 索引 → `SortBy=Manual` → `Files.Move` → `SyncToModel`
- `OnDrop` 前置分支：内部拖拽 && 来源==本 fence && 单文件 && 非 Portal → `GetDropInsertIndex(e)` + `ReorderFile`，**固定 `e.Effects=None` 并 return**（绝不回报 Move，否则源端 `RemoveFile` 删掉唯一条目）
- `GetDropInsertIndex`：InputHitTest 找 `ListBoxItem`，Icon 视图按 X 中线 / List·Detail 按 Y 中线判前后，落空白=末尾
- Portal fence 不参与重排（`IsPortalMode` 拦截 + 菜单"手动排列"灰显）

### 菜单架构（bug 32 / 33）

- 图标风格 / 呈现方式 / 排序方式三个子菜单由 `FencePanel.AddViewSortMenuItems(ItemCollection)` 统一构建，供 standalone 的 `ShowTitleBarMenu` 与 tab 条的 `FenceHost.TabMenuButton_Click` 共同复用（避免两套并行菜单漂移）。tab 模式下作用于活动 tab（`FenceContent.DataContext` = 活动 tab VM）。
- 暗色 `MenuItem` 模板（`DarkTheme.xaml` 的 `DarkMenuItemStyle` / `DarkDangerMenuItemStyle`）**必须自带 `PART_Popup` + `ItemsPresenter`**，否则带子项的 MenuItem 无法展开子菜单。

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
