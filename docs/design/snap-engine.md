# Snap 吸附系统

## 1. 吸附目标

```
吸附目标：
  ├─ 屏幕边缘（上下左右）
  ├─ 其他 Fence 的边缘（上下左右对齐）
  └─ 网格线（可配置间距，如 8px 或 16px）
```

## 2. 吸附算法

```
SnapEngine.cs — 纯函数，无副作用
  输入：moving Rect + other Rects + screen bounds
  输出：吸附修正后的 SnapResult / SnapDetailResult

算法步骤：
  1. 拖动时计算当前 Fence 四条边的位置
  2. 遍历所有吸附目标，找到距离 < threshold (默认 10px) 的边
  3. 将位置修正到吸附点
  4. 同时吸附多条边（如左边吸附屏幕边缘 + 上边吸附另一个 Fence 底部）
  5. 按住 Alt 拖动时临时禁用吸附
```

### 2.1 实时吸附与视觉反馈（WM_MOVING 方案）

WPF 的 `DragMove()` 是阻塞调用，拖拽期间无法获取窗口实时位置。改用 Win32 消息拦截实现实时吸附：

```
拖拽流程（Phase 6 补全）：
  1. TitleBar_MouseLeftButtonDown → 发送 WM_NCLBUTTONDOWN + HTCAPTION
     （替代 DragMove()，触发系统原生拖拽）
  2. 系统发送 WM_MOVING 消息 → FenceHost.HwndSourceHook 拦截
  3. 从 lParam 解析 RECT → 调用 SnapEngine.SnapWithDetail()
  4. 将修正后的 RECT 写回 lParam → 窗口"磁吸"到吸附点
  5. SnapGuideOverlay 显示吸附辅助线（AccentColor 虚线）
  6. WM_EXITSIZEMOVE → 隐藏辅助线 → 触发 InteractionEndedFromWndProc
```

**关键改动**：
- `FencePanel.TitleBar_MouseLeftButtonDown` — 替换 `DragMove()` 为 `NativeMethods.SendMessage(WM_NCLBUTTONDOWN, HTCAPTION)`
- `FenceHost.TabStripBorder_MouseLeftButtonDown` — 同上
- `FenceHost.WndProc` — 新增 HwndSourceHook，处理 WM_MOVING / WM_SIZING / WM_EXITSIZEMOVE

**注意：拖动/调整结束时不再二次 Snap**。WM_MOVING 已经在拖动期间用 host rect（含 4px 阴影 margin）做了实时吸附，调整大小走 ApplyResizeSnap 同样实时吸附；InteractionEnded / InteractionEndedFromWndProc 上触发 `RestoreWindowToBottom + TryMergeFences + RequestAutoSave`。早期版本在事件回调里又调用一次基于 vm rect（= host - 8px）的 `ApplySnap`，会把已经贴住屏幕右/下边的 fence 再向外推 8px，多次拖动累积后导致 fence 漂出工作区。

**Z-order 恢复**：拖拽开始时 `InteractionStarted` 调用 `BringWindowAboveSiblings(hwnd)` 将窗口提升到 `HWND_TOP`（保持在其他 Fence 之上、应用窗口之下），同时设置 `_isDragging` 标志抑制前台窗口变化引起的 z-order 恢复。拖拽结束后在 `InteractionEnded` 和 `InteractionEndedFromWndProc` 中调用 `RestoreWindowToBottom(hwnd)` 恢复到桌面图标层之上，并清除 `_isDragging` 标志。

**Z-order 策略（DesktopEmbedManager 改进）**：
- 核心发现：`SetWindowPos(HWND_BOTTOM)` 在桌面窗口（Progman/WorkerW）是前台窗口时，可能将 Fence 窗口推到桌面后面。这是 Win11 的 z-order 行为差异
- `RestoreWindowToBottom` 在桌面是前台时跳过 `SendToBottom`，延迟到恢复定时器或下次非桌面前台切换处理
- 拖拽期间（`_isDragging`）完全抑制 `OnForegroundChanged` 的 z-order 操作
- Win+D TOPMOST 状态下，前台切换到桌面窗口不撤销 TOPMOST
- 正常模式下，前台切换到桌面窗口时跳过 z-order 恢复
- debounce timer 和 5s 恢复定时器中都检查前台窗口，桌面时跳过
- 修复 debounce timer handler 泄漏：只创建一次 DispatcherTimer

### 2.2 SnapWithDetail 和 SnapResize

```
SnapEngine.SnapWithDetail() — 移动时实时吸附
  输入/输出同 Snap()，额外返回 SnapLine 列表

SnapEngine.SnapResize() — 调整大小时吸附
  当前逻辑与 SnapWithDetail 相同（四边独立吸附）
  Thumb DragDelta 中逐帧调用

SnapLine — 吸附辅助线信息
  Position: 线的位置（X 或 Y 坐标）
  Edge: 吸附的是移动窗口的哪条边（Left/Right/Top/Bottom）
  IsHorizontal: true = 水平线, false = 垂直线
```

### 2.3 SnapGuideOverlay

透明无边框窗口，覆盖整个虚拟屏幕：
- `WS_EX_TRANSPARENT | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE` — 不拦截鼠标
- Canvas 动态绘制 Line 元素（AccentColor #7AA7E6，1px 虚线）
- 全局共享一个实例，由 App.xaml.cs 创建和管理

### 2.4 Alt 键禁用吸附

WM_MOVING / WM_SIZING 处理中检测 Alt 键：
```csharp
if ((NativeMethods.GetAsyncKeyState(NativeMethods.VK_MENU) & 0x8000) != 0)
{
    _snapGuideOverlay?.Hide();
    return; // 跳过吸附
}
```

## 3. 配置

- `SnapThreshold` — 吸附距离阈值，默认 10px，范围 0-30（0 = 禁用吸附）
- `CompatibilityMode` — 禁用 z-order 管理（兼容其他桌面增强工具）

## 4. Resize 吸附

FencePanel 的 8 个 Thumb resize 手柄在 DragDelta 中调用 `SnapEngine.SnapResize()`：
- 每次尺寸变更后，计算当前窗口的 SnapEngine.Rect
- 调用 SnapResize() 获取修正后的位置/尺寸和辅助线
- 将修正值写回窗口和 ViewModel
- 显示辅助线，DragCompleted 时隐藏
