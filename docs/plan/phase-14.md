# Phase 14: Fence 呈现方式切换（图标/列表/详细）+ 排序

**目标**：为 Fence 面板添加类似 Windows Explorer 的呈现方式切换与排序能力，每个 fence 独立设置并持久化。竞品中 Stardock Fences 的标准 fence 不支持列表/详细视图（仅 Folder Portal 支持），本项目在标准 fence 中原生提供三种视图形成差异化。

分三个子阶段，每阶段独立构建、独立验收、独立提交。

## 背景与竞品

- **Stardock Fences**：标准 fence 仅图标视图；列表/详细仅 Folder Portal（借 Explorer 引擎），论坛长期有用户抱怨。排序支持名称/类型等字段。
- **Coodesker（酷呆桌面）**：视图菜单切换列表/图标，列表为纵向多列流式；支持时间等字段排序。

## 现状基础

`FenceDefinition` 早已有 `ViewMode`(Icon/List/Detail) / `SortField` / `SortDirection` 字段（仅 Icon + 字段排序部分实现）；`FencePanelViewModel` 有对应属性与 `ApplySort()`；视图层为单 `ListBox(FileListBox)` + 横向 WrapPanel + Recycling 虚拟化，`FileIconTemplateSelector` 按图标风格选模板，鼠标三事件挂在 DataTemplate 根 Border。

## 架构决策（三阶段共用）

- 单 ListBox + Style DataTrigger 切 `ItemsPanel` + 扩展 `FileIconTemplateSelector`（否决多 ListBox / ListView+GridView）
- 手动顺序的持久化载体是 `FilePaths` 顺序，不新增字段
- 图标风格（IconStyle）与视图模式（ViewMode）正交：List/Detail 小图标按 `EffectiveIconStyle` 切图源，不带字母叠加
- Portal fence 禁用手动排序（菜单灰显 + drop 分支拦截）

---

## Phase 14a：排序基础（✅ 已完成）

字段排序完整可用——菜单切换排序字段/方向，文件增删/重命名后自动维持排序，`Manual` 枚举就位（拖拽 UI 在 14c）。

| 步骤 | 文件 | 动作 |
|---|---|---|
| 1 | `Core/Models/FenceDefinition.cs` | `SortField` 加 `Manual` |
| 2 | `Core/Services/FileSorter.cs`（新增）| `FileSortKey` + `Sort<T>` + `AdjustMoveIndex`，Core 可单测 |
| 3 | `UI/ViewModels/FileItemViewModel.cs` | `SizeBytes`/`DateModified`/`DateCreated`/`SizeDisplay`/`DateModifiedDisplay`/`ToSortKey`/`RefreshMetadata`（lazy + try/catch） |
| 4 | `UI/ViewModels/FencePanelViewModel.cs` | `ApplySort` 接 FileSorter + Manual 早退；`ResortAfterChange`；`AddFile` 返回 `FileItemViewModel?` |
| 5 | `UI/Controls/FencePanel.xaml.cs` | `LoadIconForLastFile`→`LoadIconFor(item)`（`ContainerFromItem` 定位动画）；`OnDrop` 批量后 `ResortAfterChange` |
| 6 | `App/App.xaml.cs` | 4 处 AddFile 调用点用返回值 + 批量后 `ResortAfterChange`；portal 存量项 `RefreshMetadata` |
| 7 | `UI/Controls/FencePanel.xaml.cs::ShowTitleBarMenu` | `BuildSortSubmenu`（字段 + 升/降序，portal 手动灰显） |
| 8 | `tests/FileSorterTests.cs`（新增）+ `JsonLayoutStoreTests.cs` | 排序器/索引修正单测；ViewMode+SortBy 往返；未知枚举回退 |

**验收**：`dotnet test` 90 通过（原 71 + 19 新）；构建仅余既有无关警告。

## Phase 14b：视图模式（List/Detail 模板 + 面板切换 + 列头 + 呈现方式菜单）（✅ 已完成）

三视图可视化并可菜单切换，Detail 列头可点击排序。依赖 14a 元数据属性。构建通过 + 启动烟雾验证无 XAML 运行时异常。

- `Themes/FileTile.xaml`：`FileListItemWidth`(160)/`FileListIconSize`(18)/`DetailRowHeight`(24)
- `FencePanel.xaml`：三个 `ItemsPanelTemplate`（Icon 横向 WrapPanel / List 纵向 WrapPanel / Detail VirtualizingStackPanel）；ListBox Style DataTrigger 按 `ViewMode` 切面板与滚动方向（**把元素上的 `ScrollViewer.*` attribute 移进 Style**）；`ListFileTile`/`DetailFileTile` 模板（小图标按 `EffectiveIconStyle` 切，不带字母叠加；Detail 三列固定宽，**不用 SharedSizeGroup**）；Detail 列头行（身体 Grid 改 `30/Auto/*`）
- `FileIconTemplateSelector`：加 `ListTemplate`/`DetailTemplate`，先按 ViewMode 分流
- `FencePanel.xaml.cs`：`DetailHeader_Click`（同字段反转/换字段）；`UpdateSortGlyphs`（▲/▼）；`OnViewModelPropertyChanged` ViewMode→`RefreshFileTileTemplate`；`BuildViewModeSubmenu`

## Phase 14c：手动拖拽重排（✅ 已完成）

fence 内拖拽图标重排并持久化，重排后自动切 `Manual`。构建通过 + 90 测试回归。

- `InternalDragFormats.cs`：`SourceFenceId`
- `FileItem_MouseMove`：`SetData(SourceFenceId, Id)`
- `FencePanelViewModel.ReorderFile`：`AdjustMoveIndex` → `SortBy=Manual` → `Files.Move` → `SyncToModel`
- `OnDrop` 前置分支：内部 && 同 fence && 单文件 && 非 Portal → `GetDropInsertIndex` + `ReorderFile`，**固定 `e.Effects=None` 并 return**
- `GetDropInsertIndex`：InputHitTest 找 ListBoxItem，Icon 按 X 中线 / List·Detail 按 Y 中线，落空白=末尾

## 风险与规避

| 风险 | 规避 | 阶段 |
|---|---|---|
| AddFile 自动排序破坏"末尾"假设 | AddFile 不排序、返回新建项；动画用 `ContainerFromItem` | 14a |
| 元数据 IO 异常 | `EnsureMeta` 全 try/catch | 14a |
| 旧版本读新 JSON 的 Manual → 整文件回退 | 既有 `.corrupt-*` 备份兜底 | 14a |
| 局部 `ScrollViewer.*` 压死 Style 触发器 | attribute 移进 Style | 14b |
| Recycling 下切 ViewMode 模板不刷新 | 复用 `RefreshFileTileTemplate()` | 14b |
| 同 fence 重排误报 Move → 源端删条目 | 重排分支固定 `e.Effects=None` | 14c |
| Portal 手动序被刷新冲掉 | 菜单灰显 + drop 分支拦截 | 14c |
