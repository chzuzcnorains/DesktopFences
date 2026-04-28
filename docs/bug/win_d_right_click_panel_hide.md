# Win+D后右键自己的图标panel隐藏的bug

## 问题描述
当用户按下Win+D显示桌面后，再右键点击程序自己的图标（托盘图标或Fence上的右键菜单），Fence面板会突然隐藏。

## 产生原因
1. Win+D显示桌面后，程序会将Fence窗口设置为HWND_TOPMOST，确保在桌面可见
2. 当用户右键点击程序自己的UI元素时，会触发前台窗口变化事件
3. 原来的逻辑认为这是切换到了其他窗口，因此将Fence窗口恢复为HWND_BOTTOM，导致其被其他窗口遮挡或隐藏
4. 另外，右键菜单如果是任务栏的上下文菜单，也会被误判为非桌面窗口，触发z-order恢复

## 修复方案
1. **本进程窗口判断**：在前台窗口变化处理逻辑中，增加对本进程窗口的判断。如果激活的窗口属于当前进程（包括上下文菜单、对话框等），则忽略该事件，不改变Fence的z-order状态。
2. **任务栏相关窗口判断**：在IsDesktopWindow方法中增加对任务栏窗口（Shell_TrayWnd、Shell_SecondaryTrayWnd）的判断，认为这些窗口属于桌面相关操作，不撤销Win+D的置顶状态。
3. **右键菜单父链检查**：对于Windows标准菜单类（#32768），检查其父窗口链，如果父窗口是任务栏，则认为是桌面相关操作，不触发z-order恢复。

## 核心代码修改
```csharp
// 忽略激活的本进程窗口
uint foregroundProcessId;
NativeMethods.GetWindowThreadProcessId(hwnd, out foregroundProcessId);
var currentProcessId = (uint)System.Diagnostics.Process.GetCurrentProcess().Id;
if (foregroundProcessId == currentProcessId)
    return;

// 任务栏窗口判断
if (className is "Shell_TrayWnd" or "Shell_SecondaryTrayWnd")
    return true;

// 右键菜单父链检查
if (className == "#32768")
{
    var menuParent = NativeMethods.GetParent(hwnd);
    while (menuParent != IntPtr.Zero)
    {
        sb.Clear();
        NativeMethods.GetClassName(menuParent, sb, sb.Capacity);
        var menuParentClass = sb.ToString();
        if (menuParentClass is "Shell_TrayWnd" or "Shell_SecondaryTrayWnd")
            return true;
        menuParent = NativeMethods.GetParent(menuParent);
    }
}
```

## 影响范围
- Win+D后的窗口置顶状态管理
- 右键菜单等弹出窗口的z-order处理
- 任务栏相关交互的行为
