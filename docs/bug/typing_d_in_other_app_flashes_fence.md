# 在其他程序里打字时 fence 一闪而过

## 问题描述

用户在其他程序（聊天、编辑器、浏览器输入框等）正常打字时，DesktopFences 的 fence 面板会突然激活、浮到最前面，然后短暂"一闪而过"。期间用户**并没有按 Win+D 也没有按 Win+Space**。

## 复现条件

打字内容里出现字母 `D`（即 vkCode `VK_D = 0x44`），并且此前用户曾经按过任意 Win+X 系统组合键（Win+L、Win+E、Win+R、Win+Tab、Win+S 等）。

## 真因

`DesktopFences.Shell.Desktop.DesktopEmbedManager.KeyboardHookCallback` 通过累积变量 `_winKeyDown` 跟踪 Win 键状态，再配合 `VK_D` 的 KEYDOWN 触发 `OnShowDesktopDetected`：

```csharp
// 旧代码（有问题）
if (kb.vkCode == VK_LWIN || kb.vkCode == VK_RWIN) _winKeyDown = true;
if (kb.vkCode == VK_D && _winKeyDown) OnShowDesktopDetected();
// ... KEYUP 分支
if (kb.vkCode == VK_LWIN || kb.vkCode == VK_RWIN) _winKeyDown = false;
```

问题在于：当用户按 Win+L、Win+E、Win+R、Win+Tab 这类 **系统级组合键** 时，Windows 系统会优先消费整段输入序列，**Win 键的 KEYUP 事件不一定会被传递到低级钩子**（典型场景：Win+L 锁屏后焦点已切换、桌面切换流程中钩子被绕过等）。

结果 `_winKeyDown` 残留为 `true` 后再也清不掉。之后用户在普通程序里打字按到字母 D：

1. 第一个 D：被误判成 Win+D → `OnShowDesktopDetected` → 300ms 定时器到期 → `SetAllTopmost`，fence 突然浮到最前；
2. 紧接的另一个 D：`_isTopmost == true`，走 restore 分支 → `SetAllBottom`；

整个过程对用户呈现为 fence "一闪而过"。

## 修复方案

不再维护累积状态，改用 [`GetAsyncKeyState`](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-getasynckeystate) 在收到 D 键 KEYDOWN 时**实时**查询 `VK_LWIN` / `VK_RWIN` 的物理按键状态。`GetAsyncKeyState` 直接读硬件状态，不受 KEYUP 事件是否到达低级钩子影响。

修改位置：[src/DesktopFences.Shell/Desktop/DesktopEmbedManager.cs](../../src/DesktopFences.Shell/Desktop/DesktopEmbedManager.cs)

```csharp
private IntPtr KeyboardHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
{
    if (nCode >= 0)
    {
        var kb = Marshal.PtrToStructure<NativeMethods.KBDLLHOOKSTRUCT>(lParam);
        var msg = (int)wParam;

        if (msg == NativeMethods.WM_KEYDOWN || msg == NativeMethods.WM_SYSKEYDOWN)
        {
            if (kb.vkCode == NativeMethods.VK_D && IsWinKeyPhysicallyDown())
                OnShowDesktopDetected();

            if (kb.vkCode == NativeMethods.VK_ESCAPE)
                EscapePressed?.Invoke();
        }
    }
    return _keyboardHook.CallNext(nCode, wParam, lParam);
}

private static bool IsWinKeyPhysicallyDown()
{
    return (NativeMethods.GetAsyncKeyState(NativeMethods.VK_LWIN) & 0x8000) != 0
        || (NativeMethods.GetAsyncKeyState(NativeMethods.VK_RWIN) & 0x8000) != 0;
}
```

同时移除 `_winKeyDown` 字段以及 KEYUP 分支里维护它的代码。

## 验证

1. 真正的 Win+D：仍然按预期切换 fence 到 topmost / 恢复。
2. 在记事本、浏览器、聊天窗口等程序输入包含字母 D 的文本：fence 不会异常激活或闪烁。
3. 先按一次系统组合键（Win+E 打开资源管理器），关闭后回到任意程序打字含 D 的内容：fence 不会激活。

## 经验教训

**低级键盘钩子不要用累积布尔值维护"修饰键当前是否按下"。** Windows 在处理系统快捷键、UAC 弹窗、锁屏、Alt+Tab、桌面切换等场景时，会绕过/吞掉部分 KEYUP，累积状态注定会漂移。需要判断"按 X 时是否同时按住 Y"时，应在 X 的 KEYDOWN 回调里用 `GetAsyncKeyState(Y)` 实时查询。
