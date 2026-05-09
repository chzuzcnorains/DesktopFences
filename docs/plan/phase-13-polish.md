# Phase 13 Polish:Tab 拖拽排序 + Portal 规则隔离修复

**目标**:Phase 7 引入 tab grouping 时只做了"合并入位"和"右键分离",没有重排序入口;同时一个潜伏的 portal/规则交叉 bug 在用户保存任意设置时把 portal fence 内容清空。两件事一并清理。触发于 2026-05-09 用户反馈。

## 13.P.1 任务 A — Tab 拖拽排序

### 现状

- Tab 渲染:`FenceHost.xaml.cs::RefreshTabStrip()` 用 `ItemsControl + StackPanel` 动态构建 Button
- 持久化:`FenceDefinition.TabOrder` 字段(Phase 7 已有),加载时 `g.OrderBy(d => d.TabOrder)`,合并时 `nextOrder++`
- 无重排序入口:点击只能切换 active tab,右键菜单只有重命名 / 分离 / 关闭

### 设计

仅同 fence 内重排序(跨 fence 移动 tab 已由 fence-overlap merge / TabDetachRequested 覆盖)。**实时跟随 + 插入指示线**交互:

1. Tab 按钮注册 `PreviewMouseLeftButtonDown` 设 armed 状态(记录 `_tabDragFromIndex` 与 `_tabDragStart`)
2. **FenceHost (Window) 级别**注册 `PreviewMouseMove` 与 `PreviewMouseLeftButtonUp`,用 `AddHandler(..., handledEventsToo: true)` 确保即便 Button 内部把事件标 `Handled` 也能收到。等待位移超过 `SystemParameters.MinimumHorizontalDragDistance` 后激活:
   - `Mouse.Capture(this, CaptureMode.SubTree)` 在 Window 上捕获,SubTree 模式保留子树事件正常派发(button 仍能收 mouse,但 mouse up 一定路由到 Window 级 handler)
   - 显示 `TabDropIndicator`(2 px accent 竖线)
   - 实时计算 `dropIndex`(在**虚拟序列**上算,即剔除当前被拖 tab 的剩余序列)
   - 把指示线 `Margin.Left` 移到目标缝隙(同样按虚拟序列定位)
3. Window 级 `PreviewMouseLeftButtonUp` 提交:
   - `noop` 判定:`dropIndex == from`(虚拟序列中"放回原位置"等价于不动,只有这一种情形)
   - 否则 `_tabs.RemoveAt(from); _tabs.Insert(dropIndex, vm);` —— dropIndex 已是虚拟空间索引,无需偏移校正
   - 写回 `_tabs[i].Model.TabOrder = i`
   - `RefreshTabStrip()` 重建 + `FenceContent.RaiseInteractionEnded()` 触发 RequestAutoSave

### 实现要点

- **监听必须挂在 Window 级别**:之前挂在 `TabStrip` 上的版本无法 commit reorder,根因是 Button 内部捕获鼠标 + 标 `Handled` 的副作用让 `PreviewMouseLeftButtonUp` 不到 TabStrip;改 Window + `handledEventsToo: true` 后稳定
- **虚拟序列 dropIndex**:把"剔除被拖 tab 后剩余 tab"作为目标空间计算位置。这样唯一 noop 条件是 `dropIndex == from`,其他位置一律生效。早先版本用包含 from 自己的位置序列 + `dropIndex == from || dropIndex == from + 1` 双条件 noop,导致拖到任意相邻位置都被吞,看起来"拖了但没动"
- **普通 click 不被吞**:Click handler 内 `if (_tabDragActive) { e.Handled = true; return; }`,由位移阈值天然区分点击与拖拽
- **ItemsControl 容器查询双重 fallback**:`ContainerFromIndex(i) ?? Items[i] as FrameworkElement`(Button 是 UIElement 时可能直接放进去,不被 ContentPresenter 包裹)
- **持久化路径零修改**:沿用现有 `host.Panel.InteractionEnded → RequestAutoSave` 链路,`TabOrder` 加载顺序逻辑无变更

### 关键文件

- [src/DesktopFences.UI/Controls/FenceHost.xaml.cs](../../src/DesktopFences.UI/Controls/FenceHost.xaml.cs):`RefreshTabStrip` 中按钮注册 PreviewMouseLeftButtonDown;新增 `OnTabStripPreviewMouseMove` / `OnTabStripPreviewMouseLeftButtonUp` / `ComputeTabDropIndex` / `PositionTabDropIndicator` / `ResetTabDragState` / `GetTabContainer`
- [src/DesktopFences.UI/Controls/FenceHost.xaml](../../src/DesktopFences.UI/Controls/FenceHost.xaml):TabStripBorder 内 Grid 加 `TabDropIndicator` Rectangle(2×22, AccentBrush, IsHitTestVisible=False)
- [src/DesktopFences.UI/Controls/FencePanel.xaml.cs:56](../../src/DesktopFences.UI/Controls/FencePanel.xaml.cs#L56):新增 `RaiseInteractionEnded()` 与已有 `RaiseInteractionStarted()` 对称

## 13.P.2 任务 B — Portal Fence 在保存设置后变空

### 根因

`SettingsWindow.BtnSave_Click` 无条件触发 `SettingsSaved` + `RulesSaved` 双事件;后者绑到 `App.xaml.cs::ReEvaluateClassifiedFiles`,该方法对**所有** tab(包括 portal)按规则筛文件;portal fence 的外部文件夹文件不被任何规则匹配到本 fence,一律被 `RemoveFile`。

### 修复

[src/DesktopFences.App/App.xaml.cs:193-217](../../src/DesktopFences.App/App.xaml.cs#L193-L217) 加一行:

```csharp
foreach (var tab in _fenceWindows.AllTabs())
{
    if (tab.IsPortalMode) continue;   // ← 新增
    // ...原逻辑不变
}
```

详细分析见 [docs/bug/portal_files_wiped_after_save_settings.md](../bug/portal_files_wiped_after_save_settings.md)。

### 设计契约同步

[docs/design/folder-portal.md](../design/folder-portal.md) 第 6 节新增「与规则分类的隔离契约」,把 "portal Files 只由 watcher 管理 / 规则分类不应触及 portal / watcher 是增量同步不是重灌" 三条写成显式契约,防止未来类似 bug。

## 13.P.3 验证

- `dotnet build DesktopFences.sln -c Debug`:0 错 0 警
- `dotnet test tests/DesktopFences.Core.Tests`:66 通过
- 手测:
  1. 创建多 tab fence(合并 3 个 fence),拖第 1 个 tab 到第 3 个右侧 → 顺序变化,指示线跟随,Click 不被误判
  2. 重启应用,顺序保持(`TabOrder` 持久化)
  3. 创建 portal fence(映射 D:\Documents),设置 → 外观 → 切 IconStyle 保存 → 内容保留
  4. 同时存在 portal 与规则分类 fence:规则分类 fence 仍按规则重评估;portal 不动

## 13.P.4 经验沉淀

1. **Tab 拖拽不需要 `DragDrop.DoDragDrop`**:轻量 `Mouse.Capture` + Preview 路由就够,自定义可控,无需考虑外部 DnD source/target 协议
2. **WPF Button 的内部 mouse-capture 会拦截 mouse up**:看似挂在 ItemsControl 上的 `PreviewMouseLeftButtonUp` 也可能收不到,因为 Button 在 mouse down 时 `CaptureMouse()` 给自己,mouse up 时 e.Handled 在内部上抬。修复套路三件套:**Window 级别 `AddHandler` + `handledEventsToo: true` + `Mouse.Capture(this, CaptureMode.SubTree)`**。先在 button 上挂的版本看起来 work(mouse down/move 都触发,指示线也显示),但 mouse up 只有挂到 Window 才稳——这种"看似工作的部分实现"在调试时极有迷惑性
3. **拖拽位置算法用"虚拟序列"避免相邻位置 noop**:dropIndex 在剔除被拖 tab 的剩余序列上算,这样唯一 noop 条件是"放回原位"。包含 from 自己的索引方案会让相邻位置(`from+1`)永远是 noop,UX 表现成"看似拖了但没动",非常难定位
4. **持久化字段先于 UI 入口**:Phase 7 的 `TabOrder` 字段已经备好,本次 polish 只改 UI 层;如果当时没预留这个字段,本次会顺带做迁移,改动面会大很多——值得继续保持
5. **"all-fire 保存按钮 + 一处 cleanup"是隐性 bug 工厂**:SettingsWindow 一键全保存语义自然,但 `RulesSaved` 即使规则没变也触发,加上 cleanup 不做 source 区分,相加产生破坏性副作用。设计 cleanup 函数时要显式列出"哪些 source 的文件不在我清理范围内"

## 13.P.5 关联文档

- 修复文档:[docs/bug/portal_files_wiped_after_save_settings.md](../bug/portal_files_wiped_after_save_settings.md)
- 设计文档:[docs/design/folder-portal.md](../design/folder-portal.md)、[docs/design/fence-container.md](../design/fence-container.md)
- 上游 Phase:[docs/plan/phase-7.md](phase-7.md)(tab grouping)
