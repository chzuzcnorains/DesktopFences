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

### 3.1 Windows 原生尺寸对照

| 项目 | 值 | 说明 |
|------|-----|------|
| 图标尺寸 | **48×48** | SHGFI_LARGEICON 标准大图标 |
| 网格单元 | **90×90** | 图标之间的间距与 Windows 原生一致 |
| 字体大小 | **12** | 图标下方文件名 |
| 边距 | **10** | 桌面边缘起始边距 |

### 3.2 覆盖层布局常量

```csharp
private const double GridCellWidth = 90;    // 图标网格宽度
private const double GridCellHeight = 90;   // 图标网格高度
private const double GridMarginLeft = 10;   // 左边距
private const double GridMarginTop = 10;    // 上边距
```

### 3.3 图标容器结构

```
Border (86×90, CornerRadius=4)
└── StackPanel (Margin=4)
    ├── Image (48×48)
    └── TextBlock (FontSize=12, MaxHeight=36, Wrap, DropShadow)
```

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

**触发条件**：拖拽中鼠标移动到覆盖层边缘 20px 范围内

**实现**：
```csharp
EndInternalMove(border, cancel: true);  // 取消内部移动，恢复原位
_isDragging = true;

var dataObject = new DataObject(DataFormats.FileDrop, new[] { filePath });
var result = DragDrop.DoDragDrop(border, dataObject, DragDropEffects.Copy | DragDropEffects.Move);

if (result == DragDropEffects.Move)
{
    RemoveIcon(filePath);
    FileDraggedToFence?.Invoke(filePath);
}
```

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
| 文件从 Fence 移出 | `AddIcon(filePath)` |
| 文件被删除 | `RemoveIcon(filePath)` |
| 文件重命名 | 更新显示名 |
| Fence 可见性切换 | 同步隐藏/显示 |

---

## 11. 历史调整记录

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
