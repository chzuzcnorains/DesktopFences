# 吸附后面板消失的bug

## 问题描述
拖动Fence窗口进行吸附对齐操作后，窗口会突然消失，需要切换窗口才能重新显示。

## 产生原因
1. **拖拽状态下的z-order变化**：在拖拽Fence窗口时，会调用`BringWindowAboveSiblings`将窗口临时放到最上层，方便拖拽操作。
2. **吸附结束后的错误恢复**：拖拽完成后，会调用`RestoreWindowToBottom`将窗口放回最底层。但在Windows 11中，如果拖拽结束时当前前台窗口是桌面，调用`SetWindowPos(HWND_BOTTOM)`会将`WS_EX_TOOLWINDOW`窗口推到桌面壁纸层下面，导致窗口不可见。
3. **前台变化事件干扰**：在拖拽过程中如果有前台窗口变化事件，会触发z-order恢复逻辑，同样可能导致窗口被推到壁纸层下面。
4. **拖拽实现缺陷**：原来使用WPF的`DragMove()`方法实现拖拽，无法接收到拖拽过程中的`WM_MOVING`和拖拽结束的`WM_EXITSIZEMOVE`消息，无法精确控制拖拽状态。

## 修复方案
### 方案1：拖拽状态隔离
- 新增`_isDragging`标志位，在拖拽过程中禁止所有z-order恢复操作，避免拖拽过程中窗口被意外改变z-order。
- 在`OnForegroundChanged`、`OnDebouncedForegroundRecovery`等z-order恢复逻辑中，都增加`_isDragging`判断，如果正在拖拽则跳过恢复。

### 方案2：安全的恢复时机
- 在`RestoreWindowToBottom`方法中增加判断：如果拖拽结束时当前前台窗口是桌面，则不立即调用`SendToBottom`，而是等待下一次前台窗口变化或者z-order恢复定时器处理。
- 所有调用`SendToBottom`的地方都增加判断：只有当前前台窗口不是桌面时才调用，避免窗口被推到壁纸层下面。

### 方案3：拖拽实现重构
- 从WPF的`DragMove()`改为直接发送`WM_NCLBUTTONDOWN`消息实现拖拽，这样可以接收到`WM_MOVING`、`WM_SIZING`和`WM_EXITSIZEMOVE`消息。
- 在`WndProc`中处理这些消息，实现更精确的拖拽状态控制和实时吸附功能。

### 方案4：实时吸附优化
- 在`WM_MOVING`和`WM_SIZING`消息处理中计算吸附位置，实时调整窗口大小和位置。
- 显示吸附引导线，提升用户体验。
- 支持Alt键临时禁用吸附功能。

## 核心代码修改
```csharp
// 拖拽开始时设置标志位
public void BringWindowAboveSiblings(IntPtr hwnd)
{
    _isDragging = true;
    NativeMethods.SetWindowPos(
        hwnd, NativeMethods.HWND_TOP,
        0, 0, 0, 0,
        NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE);
}

// 拖拽结束时安全恢复
public void RestoreWindowToBottom(IntPtr hwnd)
{
    _isDragging = false;
    // 如果当前前台是桌面，不立即恢复，等待下次前台变化
    if (IsDesktopWindow(NativeMethods.GetForegroundWindow()))
        return;
    SendToBottom(hwnd);
}

// 前台变化处理中增加拖拽判断
if (_isDragging) return;

// 拖拽实现改为发送WM_NCLBUTTONDOWN消息
var helper = new WindowInteropHelper(this);
NativeMethods.SendMessage(helper.Handle, NativeMethods.WM_NCLBUTTONDOWN,
    (IntPtr)NativeMethods.HTCAPTION, IntPtr.Zero);
```

## 影响范围
- Fence窗口的拖拽和吸附功能
- 窗口z-order管理逻辑
- 拖拽过程中的前台窗口变化处理
- 实时吸附引导线的显示
