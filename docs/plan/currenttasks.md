# 当前任务列表

> Phase 14 代码实现全部完成（90 测试通过），等待用户手动验收。

## Phase 14: Fence 呈现方式切换 + 排序

### 14a 排序基础 — ✅ 已完成
- [x] `SortField` 加 `Manual`；新增 `Core/Services/FileSorter.cs`
- [x] `FileItemViewModel` 元数据（Size/DateModified/DateCreated + Display + RefreshMetadata）
- [x] `FencePanelViewModel`（ApplySort Manual 早退 / ResortAfterChange / AddFile 返回值）
- [x] `FencePanel.xaml.cs` LoadIconFor 重构 + OnDrop ResortAfterChange
- [x] `App.xaml.cs` 4 处 AddFile 调用点
- [x] 排序方式子菜单 + FileSorterTests / JsonLayoutStoreTests（90 通过）

### 14b 视图模式 — ✅ 已完成
- [x] FileTile.xaml 尺寸资源
- [x] 三个 ItemsPanelTemplate + ListBox Style DataTrigger
- [x] ListFileTile / DetailFileTile 模板 + Detail 列头
- [x] FileIconTemplateSelector 扩展 List/Detail
- [x] DetailHeader_Click / UpdateSortGlyphs / 呈现方式菜单 / PropertyChanged

### 14c 手动拖拽重排 — ✅ 已完成
- [x] InternalDragFormats.SourceFenceId + FileItem_MouseMove 写入
- [x] FencePanelViewModel.ReorderFile（AdjustMoveIndex）
- [x] OnDrop 重排分支 + GetDropInsertIndex

### 待用户手动验收
- [ ] 三视图互切（Recycling 无残留模板）× 三图标风格走查
- [ ] Detail 列头点击换字段/反转/▲▼ 指示
- [ ] 拖拽：同 fence 重排（三视图）自动切手动；跨 fence 仍 Move；Explorer 拖入仍 Copy；重启顺序保持
- [ ] Portal：手动排列灰显；Detail 大小/时间外部修改后刷新

---
*Phase 13 及更早已完成，详见 [complete.md](complete.md)。*
