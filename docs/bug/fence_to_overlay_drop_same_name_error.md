# 从 Fence 拖文件到 overlay 报"源文件名和目标文件名相同"且文件消失

## 问题描述

从 Fence 中把文件拖到桌面 overlay（未归档图标层）的空白区域时：

1. 弹出 Windows shell 的"源文件名和目标文件名相同"对话框；
2. 点"跳过"后，文件在 overlay 和源 Fence 中**都不再显示**（磁盘上仍在 Desktop 文件夹）。

期望：文件以未归档图标出现在 overlay 上，并从源 Fence 消失，全程无 shell 提示。

## 真因

应用的数据模型：文件物理上**始终在 Desktop 文件夹**，Fence 与 overlay 都只是"视图"。
应用内拖拽采用**移动语义**（见 [internal_drag_duplicate_entries.md](internal_drag_duplicate_entries.md)）：
源端在 `DoDragDrop` 返回 `Move` 后删除自己的条目，目标端逻辑性地加上条目——**全程不做
物理文件操作**。Fence→Fence、overlay→Fence 都按此工作。

但 **overlay 从来不是一个 OLE 放置目标**：

- `DesktopIconOverlay.xaml` 既无 `AllowDrop`，也无 `Drop` 处理器；
- overlay 是 `AllowsTransparency` 层叠窗口，空白区 alpha=0 被 OS 判为 click-through
  （刻意设计，保证原生桌面右键菜单 / 双击快速隐藏 / marquee 框选可用，见 bug 19）。
  OLE 放置的命中测试（`WindowFromPoint`）同样会**穿透 alpha=0 区域**。

于是从 Fence 拖出的文件落到 overlay 空白区时，**穿透到真实桌面**（SysListView32/Progman）。
被拖文件本就在 Desktop 文件夹，桌面把它当作"同文件夹内移动"→ 弹"源文件名和目标文件名
相同"。桌面的 `IDropTarget` 回报 `Move`，源 Fence 的 `FencePanel.FileItem_MouseMove`
据此 `RemoveFile` 删除条目；而 overlay 从未收到通知去 `AddIcon` → 文件两处都不显示。

## 修复

让 overlay 在**应用内拖拽进行期间**临时成为合法的 WPF 放置目标，使放置落在 overlay 上
不再穿透到真实桌面；overlay 收到放置后逻辑性地 `AddIcon` 并回报 `Move`（复用既有的
"应用内拖拽=移动"约定）。仅在内部拖拽期间开启，Explorer→桌面 的复制不受影响。

1. **`DesktopIconOverlay`**：
   - 新增注入式 `Func<string,bool>? IsDesktopFile`，过滤掉 portal fence 的外部文件
     （overlay 只显示未归档的*桌面*文件）。
   - 构造函数订阅 `DragOver`/`Drop`。
   - `BeginDropTargetMode()`：`AllowDrop = true` + 把窗口背景从 alpha=0 的 `Transparent`
     改成 alpha=1 的 `ClickableTransparentBrush`（视觉不可感知），令整窗在拖拽期间可命中；
     `InvalidateVisual()/UpdateLayout()` 促使层叠窗口及时按 alpha=1 重组。背景设在 **Window**
     上即可——Canvas `Background={x:Null}` 不参与命中，空白处命中落到窗口自身背景。
   - `EndDropTargetMode()`：`AllowDrop = false` + 背景还原 `Brushes.Transparent`（恢复 click-through）。
   - `OnOverlayDrop`：仅处理带 `InternalDragFormats.Marker` 的拖拽；对每个路径，若
     `IsDesktopFile` 通过且 `!ContainsIcon` → `AddIcon` 并记 `anyAdded`；
     `e.Effects = anyAdded ? Move : None`（`None` 覆盖"overlay 自拖自/重复/非桌面"场景，
     防止源端误删，与 `FencePanel.OnDrop` 的 `anyAdded` 守卫一致）；`e.Handled = true`
     即便 `None` 也消费掉，杜绝穿透到桌面再弹同名框。
   - `OnIconMouseMove` 内 overlay 自己发起的两处 `DoDragDrop`（group / edge）**不**调用
     `BeginDropTargetMode`——只用 `try/finally` 复位 `_isDragging`。详见下方「踩坑」。

2. **`FencePanel`**：
   - 新增 `InternalFileDragStarted` / `InternalFileDragEnded` 事件，在 `FileItem_MouseMove`
     的 `DoDragDrop` 外层 `try/finally` 触发——App 据此切 overlay 放置目标模式。
   - `DoDragDrop` 整段包 `try/finally`，`finally` 复位 `_isDraggingFile`：否则一旦拖拽链抛
     异常，`if (_isDraggingFile) return;` 会**静默吞掉该 fence 后续所有拖拽**（表现为"拖拽
     没反应"）。overlay 两处 `_isDragging` 同理。
   - 源端 `result == Move` 删除条目后追加 `InteractionEnded?.Invoke()`：否则 Fence 丢失文件
     的状态不触发 `RequestAutoSave`，重启后文件又回到 Fence。**此改动同时修复 Fence→Fence
     移动不持久化的既有隐患**（同一段代码路径）。

3. **`App`**：
   - 每个 host 接线处新增
     `host.Panel.InternalFileDragStarted += () => _desktopOverlay?.BeginDropTargetMode();`
     与 `InternalFileDragEnded += () => _desktopOverlay?.EndDropTargetMode();`。
   - `CreateDesktopOverlay` 注入 `_desktopOverlay.IsDesktopFile = IsDesktopFile;`。

## 关键经验

- **`AllowsTransparency` 层叠窗口的 alpha=0 click-through 不仅吞鼠标点击，也吞 OLE 放置命中**
  （`WindowFromPoint` 穿透）。要让这种窗口在某段时间内成为放置目标，必须临时把背景切到
  alpha≥1 并开 `AllowDrop`，结束后还原——不能长期 alpha=1，否则破坏桌面 click-through
  （回归 bug 19 / marquee / 原生右键菜单）。
- **跨窗口"内部拖拽=移动"的两端都要显式约定 Effects**：新放置目标（overlay）必须回报
  `Move` 源端才删条目，且用 `anyAdded` 守卫避免重复/自拖自时误删（延续 bug 27 的结论）。
- **逻辑性移动（不动磁盘）的源端删除必须触发持久化**，否则重启回退。

## 踩坑：放置目标模式只能给「对侧来源」开，不能给「自己发起的拖拽」开（多文件回归）

初版为"对称收口"在 overlay 自己发起的 group / edge 拖拽外也套了 `BeginDropTargetMode`，
意图是 overlay 图标拖到边缘又落回 overlay 空白区时能被自己接住。结果引入回归：

- 现象：从 overlay 多选拖拽到 fence，光标正常、能松手，但**什么都没发生、图标留在 overlay**
  （单文件 overlay→fence 走 edge 块、多文件走 group 块，块不同，所以最初只表现为多文件挂掉）。
- 真因：`BeginDropTargetMode` 把 overlay 变成**全屏 alpha=1 放置目标**。overlay 自己拖拽时它
  仍是放置目标，会与目标 fence 竞争 drop；一旦 drop 命中 overlay（这些 icon 已在 overlay 上
  → `ContainsIcon=true` → `anyAdded=false`）→ 回报 `None` → 源端不 `RemoveIcon`、fence 也没拿到
  → 图标原地不动。

修复：**overlay 自己发起的拖拽绝不开启放置目标模式**。放置目标只在「fence→overlay」时由
`FencePanel` 的 `InternalFileDragStarted/Ended` 事件驱动开启。代价：overlay 边缘 OLE 拖拽又落回
overlay 空白区这一**罕见**路径仍会穿透到桌面（同名错误），可接受。

> 经验：临时放置目标模式的开关，必须**只在对侧窗口作为拖拽来源**时打开。给"自己也是来源"的
> 窗口开，等于制造一个会抢自己 drop 的竞争目标。

## 踩坑 2：单个未归档图标只在「屏幕边缘」才切 OLE → 拖到中间的 fence 没反应

排查"单文件 overlay→fence 没反应"时加临时日志（`%TEMP%\df_drag.log`）发现：单文件 fence→overlay
其实正常（`OnOverlayDrop anyAdded=True → Move`），但单文件 overlay→fence **整段没有 OLE 起始日志**。

真因：`OnIconMouseMove` 里单个 overlay 图标先进"桌面内挪动"（internal move）模式，**只有拖到
屏幕边缘 20px 内**才切成跨窗口 OLE 拖拽（多选则直接 OLE）。所以把单个图标拖到屏幕**中间**的
fence 上，它只是在桌面挪了位置、从不进 fence。

修复：OLE 切换条件加上"光标落在某个 fence 之上"——
`_embedManager.IsPointOverFence(PointToScreen(currentPos))`（`IsPointOverFence` 经 Shell 的
`InternalsVisibleTo(DesktopFences.UI)` 可见；`PointToScreen` 给物理像素，与 `GetWindowRect` 对齐）。
单图标拖到空白桌面仍是挪位置（保留桌面整理），拖到 fence 上则进 fence。

> 经验：**单文件与多文件走不同的拖拽起始分支**（单→internal move→边缘切 OLE；多→直接 OLE），
> 排查"单好多坏 / 多好单坏"先确认是不是两条分支之一。日志（拖拽起止、drop 落点、anyAdded）
> 是最快的定位手段，不要靠对称性猜。

## 验证

- 普通 Fence → overlay 空白区：无同名框，文件作为未归档图标出现在 overlay，并从源 Fence 消失；
  重启应用后仍在 overlay（未回到 Fence）。
- **多文件**：从 Fence 多选拖到 overlay → 全部出现在 overlay；从 overlay 多选拖到 Fence →
  全部进 Fence、overlay 不残留（验证不再被 overlay 自身放置目标抢 drop）。
- **单个未归档图标拖到屏幕中间的 fence 上** → 进入 fence、桌面消失（验证踩坑 2：不再要求拖到屏幕边缘）。
- 回归：Fence→Fence（源删目标增）、overlay→Fence 单/多文件（overlay 图标消失）、单图标拖到空白桌面
  仍是挪位置、Explorer→桌面空白区（仍复制到桌面、无异常）、非拖拽态桌面右键菜单 / 双击快速隐藏 / 框选 正常。
- portal Fence → overlay：`IsDesktopFile` 过滤后 no-op（消费、不动作、不弹框）；portal→桌面
  真实文件搬运是独立功能，不在本次范围。

| 项目 | 内容 |
|------|------|
| 修复日期 | 2026-06-25 |
| 涉及文件 | DesktopIconOverlay.xaml.cs, FencePanel.xaml.cs, App.xaml.cs |
