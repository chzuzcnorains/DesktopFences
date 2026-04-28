# 截图后Fence面板不展示的bug

## 问题描述
使用截图工具（如Snip & Sketch、微信截图等）进行截图后，桌面上的Fence面板会消失，需要切换窗口才能重新显示。

## 产生原因
1. **Windows 11 z-order特性**：在Windows 11中，当当前前台窗口是桌面时，调用`SetWindowPos(HWND_BOTTOM)`会将`WS_EX_TOOLWINDOW`类型的窗口推到桌面壁纸层的下面，导致窗口不可见。
2. **截图工具的影响**：截图工具通常会让桌面成为前台窗口，此时如果程序调用`SendToBottom`方法，就会将Fence窗口推到壁纸层下面。
3. **双击误判**：截图时的拖拽操作会被鼠标钩子误判为双击桌面，触发快速隐藏功能，导致所有Fence被隐藏。
4. **最小化问题**：双击标题栏会触发窗口最小化，而Fence窗口是`WS_EX_TOOLWINDOW`且`ShowInTaskbar=False`，最小化后就无法在任务栏找到，相当于消失了。

## 修复方案
### 方案1：z-order管理优化
1. **新增EnsureVisibleAboveDesktop方法**：采用两步法确保窗口可见：
   - 先临时将窗口设置为HWND_TOP（但不激活），确保它肯定可见
   - 如果当前前台窗口不是桌面，再将其设置为HWND_BOTTOM；如果是桌面，则暂时保持在HWND_TOP，等前台窗口变化后由z-order恢复定时器处理
2. **SendToBottom方法保护**：在SendToBottom方法中增加判断，当前台窗口是桌面时，不改变z-order，避免窗口被推到壁纸层下面

### 方案2：鼠标钩子逻辑重构
1. **点击有效性判断**：只有在桌面的完整点击（鼠标按下和释放都在桌面，且没有拖拽）才会被记录为有效点击
2. **拖拽排除**：如果鼠标按下和释放的位置超过双击距离阈值，则认为是拖拽操作，不记录为有效点击
3. **非桌面点击重置**：如果点击发生在非桌面窗口，重置最后点击时间，避免后续的桌面点击被误判为双击

### 方案3：禁止窗口最小化
1. **拦截WM_NCLBUTTONDBLCLK消息**：禁止双击标题栏的默认行为，避免触发最小化
2. **拦截SC_MINIMIZE命令**：在WndProc中拦截WM_SYSCOMMAND消息中的SC_MINIMIZE命令，完全禁止窗口最小化

## 核心代码修改
```csharp
// EnsureVisibleAboveDesktop实现
public void EnsureVisibleAboveDesktop(IntPtr hwnd)
{
    if (!NativeMethods.IsWindowVisible(hwnd))
    {
        NativeMethods.ShowWindow(hwnd, NativeMethods.SW_SHOW);
    }

    // Step 1: Bring to top temporarily
    NativeMethods.SetWindowPos(
        hwnd, NativeMethods.HWND_TOP,
        0, 0, 0, 0,
        NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE);

    // Step 2: If foreground is not desktop, set to bottom immediately
    var foreground = NativeMethods.GetForegroundWindow();
    if (!IsDesktopWindow(foreground))
    {
        NativeMethods.SetWindowPos(
            hwnd, NativeMethods.HWND_BOTTOM,
            0, 0, 0, 0,
            NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE |
            NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_SHOWWINDOW);
    }
}

// 禁止最小化的WndProc处理
case NativeMethods.WM_NCLBUTTONDBLCLK:
    if (wParam.ToInt32() == NativeMethods.HTCAPTION)
    {
        handled = true;
        return IntPtr.Zero;
    }
    break;

case NativeMethods.WM_SYSCOMMAND:
    if ((wParam.ToInt32() & 0xFFF0) == NativeMethods.SC_MINIMIZE)
    {
        handled = true;
        return IntPtr.Zero;
    }
    break;
```

## 影响范围
- 窗口z-order管理逻辑
- 快速隐藏（双击桌面隐藏Fence）功能
- 窗口最小化行为
- 截图等操作后的窗口可见性
