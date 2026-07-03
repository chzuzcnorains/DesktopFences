# 桌面图标覆盖层（DesktopIconOverlay）

## 1. 概述

DesktopIconOverlay 是一个全屏透明 WPF 窗口，用于在隐藏原生桌面图标层（SysListView32）后，自行渲染未收纳到 Fence 中的桌面文件图标。

**核心功能**：
- 在原始位置显示未收纳的桌面图标
- 支持完整交互（双击打开、右键菜单、拖放至 Fence）
- 支持图标在覆盖层内自由移动
- z-order 管理与 FenceHost 一致（Win+D 后仍可见）

---

## 2. 窗口设计

### 2.1 窗口样式

```xml
WindowStyle=None
AllowsTransparency=True
Background=Transparent
ShowInTaskbar=False
Topmost=False  // z-order 由 DesktopEmbedManager 统一管理
```

### 2.2 Canvas 布局

使用 Canvas 绝对定位，`Background={x:Null}` 使空白区域点击穿透：

```
┌─────────────────────────────────────────────┐
│  Canvas (null 背景，点击穿透)               │
│                                             │
│  ┌────────┐     ┌────────┐                 │
│  │  Icon  │     │  Icon  │  ← 未收纳文件  │
│  │  Name  │     │  Name  │                 │
│  └────────┘     └────────┘                 │
│                                             │
└─────────────────────────────────────────────┘
```

---

## 3. 图标尺寸与布局

### 3.1 尺寸跟随桌面「查看」菜单（动态度量）

图标尺寸**不再是固定常量**，而是跟随桌面右键「查看」菜单（大图标 96 / 中等图标 48 / 小图标 32，
shell 逻辑尺寸可直接当 DIP 用），由 `DesktopViewMonitor` 读取/监听（见 §12）。
其余布局度量全部由 `_iconSize` 推导，公式在 48 时**精确还原**旧常量（90/86/54/36）：

```csharp
private double _iconSize = 48;                       // 查看菜单：96 / 48 / 32
private double IconRowHeight => _iconSize + 6;       // icon 槽位行高
private const double TextRowHeight = 36;             // 文字槽位行高（各尺寸下恒定）
private double CellHeight => IconRowHeight + TextRowHeight;
private double CellWidth  => _iconSize + 38;
private double GridCellWidth  => CellWidth + 4;      // 网格槽位（图标间距）
private double GridCellHeight => CellHeight;
private const double GridMarginLeft = 10;
private const double GridMarginTop = 10;
private int IconPixelSize => (int)Math.Round(_iconSize * 2); // 提取像素=2×DIP，预留 200% DPI
```

| _iconSize | 网格槽位 | Cell | icon 行高 | 提取像素 |
|-----------|---------|------|----------|----------|
| 32（小）  | 74×74   | 70×74 | 38 | 64 |
| 48（中）  | 90×90   | 86×90 | 54 | 96 |
| 96（大）  | 138×138 | 134×138 | 102 | 192 |

字体大小恒为 12（与 Windows 原生一致：换图标尺寸不改标签字号）。

### 3.2 图标容器结构

```
Border (CellWidth×CellHeight, CornerRadius=4)
└── Grid（两行固定槽位）
    ├── Row0 (IconRowHeight): Image (_iconSize×_iconSize)
    └── Row1 (TextRowHeight): TextBlock (FontSize=12, Wrap, DropShadow)
```

`SetIconSize()` 切换尺寸时对现有元素**原地重建视觉**（改 Border/RowDefinition/Image 尺寸 +
按 `IconPixelSize` 重取位图），并把每个图标的旧网格槽位 (col,row) 映射到新度量下
（越界钳制、冲突补位到下一空槽），保持视觉顺序稳定。

---

## 4. 文件名显示

### 4.1 .lnk 后缀隐藏策略

**默认行为**：不显示 `.lnk` 扩展名，与 Windows 原生桌面一致。

**实现位置**：
- `FileItemViewModel.cs` - Fence 内文件显示
- `DesktopIconOverlay.CreateIconElement()` - 未收纳文件显示

**代码逻辑**：
```csharp
private static string GetDisplayNameWithoutLnkExtension(string filePath)
{
    var fileName = Path.GetFileName(filePath);
    if (fileName.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase))
    {
        return fileName.Substring(0, fileName.Length - 4);
    }
    return fileName;
}
```

### 4.2 TextBlock 属性

| 属性 | 值 |
|------|-----|
| `Text` | 不含 .lnk 的文件名 |
| `FontSize` | 12 |
| `TextWrapping` | Wrap |
| `TextTrimming` | CharacterEllipsis |
| `MaxHeight` | 36 |
| `Effect` | DropShadow (BlurRadius=3, ShadowDepth=1, Opacity=0.8) |

---

## 5. 图标定位策略

### 5.1 原始位置读取

从 SysListView32 跨进程读取图标位置：
1. `DesktopIconPositionReader.ReadAllPositions()` 读取物理像素坐标
2. 转换为 WPF DIP（设备无关像素）：`x / _dpiScaleX`, `y / _dpiScaleY`
3. 在 Canvas 上使用 `Canvas.SetLeft/Top()` 定位

### 5.2 自动网格定位

读取失败时（权限问题、UAC 限制等），使用自动网格布局：

**算法**：
- 扫描列优先（从上到下，从左到右）
- 查找第一个未占用的网格槽 `(col, row)`
- 坐标：`(GridMarginLeft + col * GridCellWidth, GridMarginTop + row * GridCellHeight)`

**代码**：
```csharp
private Point FindNextGridPosition()
{
    for (int col = 0; col < maxCols; col++)
        for (int row = 0; row < maxRows; row++)
            if (!usedPositions.Contains((col, row)))
                return new Point(GridMarginLeft + col * GridCellWidth,
                                GridMarginTop + row * GridCellHeight);
}
```

### 5.3 拖拽后自动吸附

图标在覆盖层内拖拽释放后，自动吸附到最近的网格槽：

```csharp
int col = Math.Max(0, (int)Math.Round((rawX - GridMarginLeft) / GridCellWidth));
int row = Math.Max(0, (int)Math.Round((rawY - GridMarginTop) / GridCellHeight));
```

---

## 6. 交互设计

### 6.1 鼠标事件处理

| 事件 | 行为 |
|------|------|
| **左键双击** | `ShellFileOperations.OpenFile(filePath)` 打开文件 |
| **左键按下 + 移动** | 内部移动模式（覆盖层内拖拽图标） |
| **左键释放** | 结束移动，自动吸附到网格 |
| **右键点击** | 显示 Shell 上下文菜单 |
| **拖拽至边缘** | 切换为 OLE 拖放模式，可拖入 Fence |

### 6.2 内部移动模式

**进入条件**：鼠标左键按下后移动超过拖拽阈值 `SystemParameters.MinimumHorizontalDragDistance` / `MinimumVerticalDragDistance`

**状态**：
```csharp
_isMoving = true;
_movingIcon = border;
_moveOffset = (currentX - Canvas.GetLeft(border), currentY - Canvas.GetTop(border));
border.Opacity = 0.7;
Panel.SetZIndex(border, 999);
border.CaptureMouse();
```

**实时更新**：
```csharp
Canvas.SetLeft(border, currentPos.X - _moveOffset.X);
Canvas.SetTop(border, currentPos.Y - _moveOffset.Y);
```

### 6.3 OLE 拖放模式（跨窗口）

**触发条件**：拖拽中鼠标**落在某个 fence 之上**（`_embedManager.IsPointOverFence(PointToScreen(pos))`），
或移动到覆盖层边缘 20px 范围内。前者是 bug 39 补的：否则单个未归档图标拖到屏幕中间的 fence 上
只会在桌面内挪位置、永远进不了 fence（"单文件 overlay→fence 没反应"）。多选拖拽则直接走 OLE（§6.x 组拖）。

**实现**：
```csharp
EndInternalMove(border, cancel: true);  // 取消内部移动，恢复原位
_isDragging = true;

var dataObject = new DataObject(DataFormats.FileDrop, new[] { filePath });
// 内部拖拽标记（bug 27）：目标 fence 据此回报 Move，本侧才会移除 overlay 图标
dataObject.SetData(InternalDragFormats.Marker, true);
// try/finally 仅用于复位 _isDragging（异常也不能卡死后续拖拽）。
// 注意：overlay 自己发起的拖拽**绝不**调用 BeginDropTargetMode——那会让 overlay 变成
// 全屏竞争放置目标、抢走本该落到 fence 的 drop（这些 icon 已在 overlay 上 → OnOverlayDrop
// 回报 None → fence 收不到、图标不动，bug 39 回归）。放置目标模式只在「fence→overlay」时
// 由 FencePanel 事件驱动开启，见 §6.4。
try
{
    var result = DragDrop.DoDragDrop(border, dataObject, DragDropEffects.Copy | DragDropEffects.Move);
    if (result == DragDropEffects.Move)
    {
        RemoveIcon(filePath);
        FileDraggedToFence?.Invoke(filePath);
    }
}
finally { _isDragging = false; }
```

> 拖入 fence = **移动**语义：`FencePanel.OnDrop` 检测到 `InternalDragFormats.Marker` 且实际
> 新增条目时回报 `DragDropEffects.Move`，本侧据此移除 overlay 图标——修复了此前 OnDrop
> 不设置 Effects 导致 `result == Move` 永不成立、图标残留的问题（bug 27）。

### 6.4 overlay 作为放置目标（fence → overlay，bug 39）

overlay 平时**不是** OLE 放置目标：空白区 alpha=0 被 OS 判为 click-through，OLE 放置命中
（`WindowFromPoint`）会穿透到真实桌面。从 Fence 拖文件落到 overlay 空白区时，桌面把"已在
Desktop 文件夹的文件"当作同文件夹移动 → 弹"源文件名和目标文件名相同"，且 overlay 收不到
通知去 `AddIcon`，文件两处都不显示。

修复：让 overlay 仅在**应用内拖拽进行期间**临时成为合法放置目标。

```csharp
// 拖拽起：整窗切到 alpha=1（视觉不可感知）+ AllowDrop，令层叠窗口可命中
public void BeginDropTargetMode()
{
    AllowDrop = true;
    Background = ClickableTransparentBrush;          // Window 背景；Canvas 仍 {x:Null}
    InvalidateVisual(); UpdateLayout();              // 促使层叠窗口按新 alpha 重组
}
// 拖拽止：还原 alpha=0 click-through，恢复桌面右键菜单 / 双击隐藏 / 框选
public void EndDropTargetMode()
{
    AllowDrop = false;
    Background = Brushes.Transparent;
    InvalidateVisual();
}

private void OnOverlayDrop(object sender, DragEventArgs e)
{
    e.Handled = true;                                 // 即便 no-op 也消费，杜绝穿透到桌面
    if (!e.Data.GetDataPresent(InternalDragFormats.Marker)) { e.Effects = None; return; }
    if (e.Data.GetData(DataFormats.FileDrop) is not string[] files) { e.Effects = None; return; }

    bool anyAdded = false;
    foreach (var p in files)
    {
        if (ContainsIcon(p)) continue;                // overlay 自拖自 / 重复
        if (IsDesktopFile is not null && !IsDesktopFile(p)) continue; // 跳过 portal/外部文件
        AddIcon(p); anyAdded = true;
    }
    e.Effects = anyAdded ? DragDropEffects.Move : DragDropEffects.None; // Move → 源 fence 删条目
}
```

- 切换时机由 **FencePanel** 的 `InternalFileDragStarted` / `InternalFileDragEnded` 事件驱动
  （包住 `FileItem_MouseMove` 的 `DoDragDrop`），App 接到事件后调 overlay 的 `Begin/EndDropTargetMode`。
- `IsDesktopFile` 谓词由 App 注入（复用 `App.IsDesktopFile`），过滤掉 portal fence 的外部文件。
- 仅内部拖拽期间开启 → **Explorer→桌面 的复制不受影响**（那时 `AllowDrop` 仍为 false，放置穿透到桌面由系统复制）。
- 源 Fence 删条目后由 `FencePanel` 补发 `InteractionEnded` → `RequestAutoSave`，保证"未归档"状态持久化（重启不回退）。
- **绝不能长期 alpha=1**：那会破坏空白区 click-through（回归 bug 19 / 原生右键菜单 / 框选）。

---

## 7. DPI 适配

### 7.1 DPI 缩放获取

```csharp
var source = PresentationSource.FromVisual(this);
if (source?.CompositionTarget != null)
{
    _dpiScaleX = source.CompositionTarget.TransformToDevice.M11;
    _dpiScaleY = source.CompositionTarget.TransformToDevice.M22;
}
```

### 7.2 坐标转换

```
物理像素（从 SysListView32 读取） → 除以 DPI 缩放 → WPF DIP
```

---

## 8. Z-Order 管理

### 8.1 注册到 DesktopEmbedManager

```csharp
var hwnd = new WindowInteropHelper(this).Handle;
_embedManager.RegisterWindow(hwnd);
```

### 8.2 层级行为

与 FenceHost 窗口完全一致：
- **正常态**：`HWND_BOTTOM`（桌面之上，其他窗口之下）
- **Win+D 后**：`HWND_TOPMOST`（临时置顶）
- **用户切换窗口**：自动恢复 `HWND_BOTTOM`

---

## 9. 生命周期

### 9.1 启动流程

```
LoadFencesAsync
  ├─ 读取 Fence 布局
  ├─ DesktopIconPositionReader.ReadAllPositions()  ← 读取原生图标位置
  ├─ DesktopIconManager.HideIcons()                 ← 隐藏 SysListView32
  └─ CreateDesktopOverlay()                         ← 创建覆盖窗口
```

### 9.2 退出流程

```
OnExit
  ├─ Close DesktopIconOverlay
  └─ DesktopIconManager.ShowIcons()  ← 恢复原生图标层
```

### 9.3 崩溃恢复

启动时检查 flag 文件 `%APPDATA%\DesktopFences\.desktop_icons_hidden`：
- 存在且进程未运行 → 自动调用 `ShowIcons()` 恢复

---

## 10. 实时同步

### 10.1 与 Fence 的双向同步

| 事件 | 覆盖层操作 |
|------|-----------|
| 文件被自动分类到 Fence | `RemoveIcon(filePath)` |
| 新文件未匹配规则 | `AddIcon(filePath)` |
| 文件从 Fence 移出（程序事件） | `AddIcon(filePath)` |
| 文件**从 Fence 拖到 overlay** | overlay 自身 `OnOverlayDrop` → `AddIcon`（bug 39，见 §6.4） |
| 文件被删除 | `RemoveIcon(filePath)` |
| 文件重命名 | 更新显示名 |
| Fence 可见性切换 | 同步隐藏/显示 |

---

## 11. 框选多选（Rubber-band / Marquee）

### 11.1 为什么不能用 Windows 原生框选

原生桌面框选矩形由 `SysListView32`（桌面图标 ListView）绘制。本应用 `DesktopIconManager.HideIcons()` 把整个 `SysListView32` `ShowWindow(SW_HIDE)` 隐藏、改由覆盖层自绘图标，原生框选随之**永远不可能出现**。覆盖层必须**自行绘制并管理框选**。

### 11.2 承重墙约束：空白区 click-through 必须保留

覆盖层空白区是 `Canvas Background={x:Null}`（每像素 alpha=0），点击会透传到真实桌面。这层透传是承重墙——以下原生行为都依赖它：
- 桌面空白处**右键原生菜单**（新建 / 个性化 / 刷新）透传到 `Progman`/`WorkerW`；
- **双击快速隐藏 fence**（`QuickHideManager` 用 `WindowFromPoint` 判定命中真实桌面）。

因此**不能**把覆盖层整层改成可命中（alpha=1）来抓框选，否则上述行为会一起失效。

### 11.3 低侵入钩子方案

输入检测放在 Shell 层 `DesktopMarqueeManager`（`WH_MOUSE_LL` + `WH_KEYBOARD_LL`），几何/视觉/选中逻辑放在覆盖层：

- `WM_LBUTTONDOWN`：`WindowClassUtil.IsDesktopAtPoint(pt)` 为真且 `!DesktopEmbedManager.IsPointOverFence(pt)`（fence 坐 `HWND_BOTTOM`，`WindowFromPoint` 会误返回其后的桌面，故需矩形包含判断）→ 记录起点、置 `_armed`。按在覆盖层图标上（alpha=1）时 `IsDesktopAtPoint` 为假，自然让逐图标 WPF 事件接管，互不干扰。
- `WM_MOUSEMOVE`：位移超 4px → `_dragging`，以**屏幕像素**抛 `MarqueeUpdated(ScreenRect, additive)`。
- `WM_LBUTTONUP`：拖拽 → `MarqueeCompleted`；无位移单击 → `EmptyClicked`（清空选中）。
- 钩子**全程 `CallNext`，绝不吞事件**——桌面透传行为不变。与 `QuickHideManager` 正交：快速隐藏只在「无位移双击」触发，框选只在「拖拽」触发。
- `WH_KEYBOARD_LL`：覆盖层 `HasSelection==true` 时，Delete → `DeleteRequested`，Esc → `Cancelled`。

事件经 `Dispatcher.Invoke`（删除用 `InvokeAsync`，避免回收站确认框阻塞钩子）回到覆盖层。

### 11.4 覆盖层侧：选框、命中、选中模型

- **坐标转换**：`IconCanvas.PointFromScreen(物理像素)` → 画布 DIP，自动含 DPI 缩放与窗口偏移，无需手算 `_dpiScale`。
- **选框矩形**：`IconCanvas` 内一个高 ZIndex、`IsHitTestVisible=false` 的 `Rectangle`（半透明蓝填充 + 蓝边框），拖拽时显示、定稿后隐藏。
- **选中集合** `_selectedPaths`：`SetSelected(path,bool)` 同步集合与 cell 高亮（选中 `SelectedBrush` alpha=0x44，取消回 `ClickableTransparentBrush` alpha=1，**不可用 alpha=0** 否则空白区重新 click-through）。
- **命中**：每个图标 `Rect(Canvas.Left, Canvas.Top, CellWidth, CellHeight)` 与选框 `IntersectsWith`。additive（Ctrl/Shift 拖框）时与拖拽开始时的 `_marqueeBaseline` 求并集。

### 11.5 组操作

| 操作 | 行为 |
|------|------|
| **框选** | 拖拽出选框，框内图标实时高亮进入 `_selectedPaths` |
| **整组拖入 fence** | 按住选中图标之一拖拽→直接 OLE 拖放，`DataObject` 携带**全部**选中路径 + `InternalDragFormats.Marker`；fence `OnDrop` 已按 `string[]` 处理，无需改动；返回 `Move` 时逐个 `RemoveIcon` |
| **Delete 批量删除** | 逐个 `ShellFileOperations.DeleteToRecycleBin` → `RemoveIcon` |
| **右键选中图标之一** | 保留多选，Shell 菜单按**整个选区**构建（`ShellContextMenu.Show` 多文件重载，`IShellItemArray`，bug 43）——删除/打开/发送到作用于全部选中项；菜单返回后遍历选区做存在性检查，已删文件即时 `RemoveIcon` + `FileDeleted`（不等 FSW/30 秒扫描兜底） |
| **右键未选中图标** | 清空选区、单选该图标，弹单文件菜单（Explorer 语义） |
| **Ctrl/Shift 单击图标** | toggle 该图标，不清空其余 |
| **空白单击 / Esc** | 清空选中 |

### 11.6 已知限制

- 覆盖层仅覆盖**主屏工作区**；副屏空白处拖框不绘制/不选中（沿用覆盖层既有主屏限制，见 [multi-monitor.md](multi-monitor.md)）。
- Shift 范围选当前等价于「追加单个」，几何范围选为后续增强。
- 覆盖层被快速隐藏（`Hide()`）期间框选事件直接忽略（`IsVisible` 守卫）。

**涉及文件**：`DesktopMarqueeManager.cs`（新增）、`DesktopIconOverlay.xaml.cs`、`DesktopEmbedManager.IsPointOverFence`、`NativeMethods`（`WM_MOUSEMOVE`/`VK_DELETE`/`VK_CONTROL`/`VK_SHIFT`）、`App.xaml.cs`（`CreateDesktopOverlay` 装配 + `OnExit` 释放）。

---

## 12. 与原生桌面「查看 / 排序方式 / 刷新」联动（DesktopViewMonitor）

### 12.1 原理：隐藏的原生视图是状态源

覆盖层空白区右键弹出的是**原生 SHELLDLL_DefView 菜单**（§11.2 承重墙）。用户点
「查看→大图标」「排序方式→名称」「刷新」时，命令实际已在被 `ShowWindow(SW_HIDE)`
隐藏的 SysListView32 上生效——只是不可见。因此无需拦截菜单：**读取隐藏视图的真实
状态并镜像到覆盖层**即可，Shell 层新增 `DesktopViewMonitor` 负责此事。

### 12.2 状态读取：IFolderView2

Raymond Chen 官方路径。**COM 代理缓存复用**：获取链路只在缓存缺失时走一遍（中间对象
即取即放），之后每次检查仅 2~3 个轻量跨进程调用（实测约 0.2ms/次）；任一调用失败即
丢弃缓存、下周期重建——explorer 重启自动恢复：

```
ShellWindows (CLSID 9BA05972-…) → FindWindowSW(CSIDL_DESKTOP, SWC_DESKTOP)
  → IServiceProvider.QueryService(SID_STopLevelBrowser) → IShellBrowser
  → QueryActiveShellView → IFolderView2
      ├─ GetViewModeAndIconSize → 图标尺寸（96/48/32）
      └─ GetSortColumnCount / GetSortColumns → 主排序列 PROPERTYKEY + 方向
```

排序列映射（四项全部落在 FMTID_Storage `B725F130-47EF-101A-A5F1-02608C9EEBAC` 下）：

| 菜单项 | pid | DesktopSortKey |
|--------|-----|----------------|
| 名称 | 10 | Name |
| 大小 | 12 | Size |
| 项目类型 | 4 | ItemType |
| 修改日期 | 14 | DateModified |

COM 声明（IShellWindows/IShellBrowser/IFolderView2 组合 vtable + 占位槽）内嵌于
`DesktopViewMonitor` 私有区（沿用 ShellContextMenu 的就地声明模式）。

### 12.3 变化触发：250ms 快速轮询为主 + EVENT_OBJECT_REORDER 加速

- **250ms `DispatcherTimer` 快速轮询是主通道**——隐藏的 SysListView32 实测**大多不发**
  REORDER 无障碍事件（窗口不可见时常跳过重排），尺寸/排序变化主要靠轮询发现。
  COM 代理已缓存（§12.2，约 0.2ms/次），开销可忽略，保证亚半秒响应。
  轮询与上次快照比对：
  - 尺寸变了 → `IconSizeChanged(size)`；
  - 排序变了 → `SortChanged(key, ascending)`（Unknown 列不广播，覆盖层无法复现）。
- `SetWinEventHook(EVENT_OBJECT_REORDER)`（200ms 去抖）当加速器；且是「刷新」检测的
  **唯一来源**：REORDER 触发的比对若尺寸/排序均未变 → `DesktopRefreshed`
  （F5 / 右键刷新 / 桌面文件增删——刷新没有可轮询的状态，轮询看不到它）。
- 线程模型：`Start()` 必须在 UI 线程调用（OUTOFCONTEXT 回调经安装线程消息循环派发），
  所有事件天然落在 UI 线程；App 侧仍用 `Dispatcher.InvokeAsync` 防御性兜底。

### 12.4 覆盖层侧响应

| 事件 | 覆盖层行为 |
|------|-----------|
| `IconSizeChanged` | `SetIconSize()`：动态度量重算（§3.1）+ 原地重建视觉 + 旧槽位映射 |
| `SortChanged` | `SortIcons()`：按键排序后从 (0,0) 列优先**紧凑重排**（覆盖层本就自动网格，不镜像原生散布位置） |
| `DesktopRefreshed` | `RefreshIcons()`：清除已删文件、`ShellIconExtractor.Invalidate` 失效缓存并重取位图；App 同步调 `DesktopFileMonitor.RescanNow()` 立即补进新增文件（不等 30 秒兜底） |

排序实现 Explorer 语义：文件夹分组在前；名称用 `StrCmpLogicalW` 自然排序（"文件2"<"文件10"，
比较隐藏 .lnk 后的显示名）；「项目类型」用 `SHGetFileInfo(SHGFI_TYPENAME)` 本地化类型名
（与 Explorer 同口径）；其余键并列时以名称决胜；**降序整体取反**（文件夹分组随之翻转，与
Explorer 一致）。排序键在 sort 前一次性预计算，避免比较回调反复走文件系统/Shell。

启动时序（App.CreateDesktopOverlay）：先 `TryGetIconSize` 应用启动前用户已设置的尺寸 →
`SetIcons` 铺图标 → `TryGetSort` 按当前排序重排 → 订阅三事件 → `Start()`。

### 12.5 图标提取配合（ShellIconExtractor）

- 新增 `GetIcon(path, pixelSize)`：按物理像素尺寸提取，缓存 key 追加 `@px` 尺寸桶
  （同一文件的 32/48/96 位图各自缓存）；旧 `GetIcon(path, bool large)` 委托到 96/32。
- 新增 `Invalidate(path)`：清掉该文件全部尺寸桶，下次 GetIcon 重新提取（刷新联动用）。
- 覆盖层按 `iconSize*2` 请求（预留 200% DPI 余量，WPF 只降采样保证清晰）。

### 12.6 已知限制

- 「查看」子菜单的**自动排列图标 / 将图标与网格对齐 / 显示桌面图标**三个开关不联动
  （覆盖层本就恒定自动网格；「显示桌面图标」被本应用接管）。
- 覆盖层排序是**紧凑重排**，不含已收纳进 fence 的文件——与原生"全桌面排序后留洞"不同，
  这是覆盖层只显示未收纳文件的自然结果。
- 「刷新」检测是启发式（REORDER 且尺寸/排序均未变）：桌面文件增删也会触发一次
  `DesktopRefreshed`——副作用是良性的（重扫 + 图标缓存失效，等价于提前刷新）。

**涉及文件**：`DesktopViewMonitor.cs`（新增）、`DesktopIconOverlay.xaml.cs`
（SetIconSize/SortIcons/RefreshIcons + 动态度量）、`ShellIconExtractor.cs`（尺寸桶缓存 +
Invalidate）、`DesktopFileMonitor.RescanNow`、`ShellFileOperations.GetFileTypeName`、
`NativeMethods`（EVENT_OBJECT_REORDER/SHGFI_TYPENAME/StrCmpLogicalW）、`App.xaml.cs`（装配）。

---

## 13. 历史调整记录

### 2026-07-02: 右键「查看 / 排序方式 / 刷新」联动覆盖层

**需求**：overlay 图标大小固定 48；桌面右键「查看（大/中/小图标）」「排序方式」「刷新」
及其子菜单应对 overlay 生效（如选「大图标」则 overlay 图标同步变大）。

**实现**：Shell 层新增 `DesktopViewMonitor`（IFolderView2 读状态（COM 代理缓存）+
250ms 快速轮询主通道 + EVENT_OBJECT_REORDER 加速/刷新检测），覆盖层布局常量改为随
`_iconSize` 推导的动态度量，新增 `SetIconSize` / `SortIcons` / `RefreshIcons`
（详见 §3.1、§12）。同日修正 IShellWindows 晚绑定失效（bug 44）；后续实测隐藏
ListView 大多不发 REORDER，遂将轮询从 2s 兜底提为 250ms 主通道（代理缓存后
单次检查约 0.2ms）。

### 2026-06-25: 桌面框选（Rubber-band 多选）

**需求**：启动后桌面无法再用 Windows 原生拖拽框选一次选中多个图标（根因：`SysListView32` 已隐藏，原生框选矩形不可能出现）。

**实现**：低侵入 `WH_MOUSE_LL`/`WH_KEYBOARD_LL` 钩子（`DesktopMarqueeManager`）只观察不吞事件，覆盖层自绘选框 + 多选 + 组拖入 fence + Delete 批删 + Ctrl/Shift 增量。保留桌面空白右键原生菜单与双击快速隐藏（详见本文 §11）。

### 2026-04-28: 图标尺寸与 .lnk 后缀优化

**问题**：
- 未归纳图标尺寸偏小（32×32），与 Windows 原生不一致
- .lnk 后缀显示，不符合用户习惯

**调整**：
1. 图标尺寸：32×32 → 48×48（SHGFI_LARGEICON）
2. 网格单元：80×96 → 90×90
3. 图标容器：72×80 → 86×90
4. 字体大小：11 → 12
5. 默认隐藏 .lnk 扩展名（Fence 内和覆盖层一致）

**影响文件**：
- `src/DesktopFences.UI/Controls/DesktopIconOverlay.xaml.cs`
- `src/DesktopFences.UI/ViewModels/FileItemViewModel.cs`
