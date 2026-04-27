# 桌面嵌入方案分析与选择

## 1. 方案对比

桌面嵌入是本项目最关键的技术决策。经调研有以下方案：

| 方案 | 原理 | Win+D 表现 | 交互性 | 复杂度 | 代表项目 |
|------|------|-----------|--------|--------|---------|
| **A: WorkerW 子窗口** | `SendMessage(Progman, 0x052C)` 触发 Explorer 创建 WorkerW，`SetParent` 将窗口挂为子窗口 | 随桌面一起显示/隐藏，正确 | **无法接收鼠标/键盘输入**（致命缺陷） | 低 | Lively Wallpaper（壁纸场景够用）|
| **B: WS_EX_TOPMOST 浮窗** | 普通 WPF 窗口 + `WS_EX_TOOLWINDOW` + `WS_EX_TOPMOST` | 始终最前，Win+D 后可见 | **完全交互** | 低 | — |
| **C: Explorer Hook (DLL注入)** | `WH_GETMESSAGE` Hook Explorer 进程，拦截 `WM_USER+83`（ShowDesktop 消息），动态切换 Topmost | Win+D 时动态置顶 | 完全交互 | 高 | Stardock Fences（推测） |
| **D: 混合方案（推荐）** | 普通 WPF 浮窗 + `WS_EX_TOOLWINDOW`（隐藏任务栏/Alt+Tab）+ 低级键盘钩子检测 Win+D + 文件系统监控桌面状态变化 | Win+D 后自动恢复显示 | 完全交互 | 中 | NoFences, DesktopFences (开源) |

## 2. 选定方案：D - 混合浮窗方案

**理由**：
- 方案 A 无法交互，对 Fences 工具是致命缺陷（需要拖放、点击、右键菜单）
- 方案 B 始终在最前面会遮挡其他窗口（用户体验差）
- 方案 C 需要注入 Explorer 进程，不稳定且维护成本高
- **方案 D** 在开源项目（NoFences, limbo666/DesktopFences）中已验证可行

**验证状态**：Demo 已通过验证（2026-03-03），具体行为正确：
- 正常状态：Fence 窗口在桌面之上、其他应用窗口之下
- Win+D 后：Fence 窗口仍然可见
- 用户切换到其他窗口后：Fence 自动回到窗口下方

---

## 3. 实现细节

### 窗口层级设计（正常态）

```
┌─────────────────────────────────┐
│  普通应用窗口（z-order 正常）      │
├─────────────────────────────────┤
│  Fence 窗口（HWND_BOTTOM）       │  ← 正常模式：在所有应用窗口最底层，但仍在桌面之上
├─────────────────────────────────┤
│  桌面图标层 (SysListView32)       │
├─────────────────────────────────┤
│  桌面壁纸 (WorkerW / Progman)    │
└─────────────────────────────────┘
```

### Z-Order 状态机

```
BOTTOM ──(Win+D 检测)──→ 延迟 300ms ──→ TOPMOST
TOPMOST ──(EVENT_SYSTEM_FOREGROUND: 用户激活其他窗口)──→ BOTTOM
```

### 窗口样式

- `WS_EX_TOOLWINDOW` — 从任务栏和 Alt+Tab 隐藏
- `WS_EX_NOACTIVATE` — 点击窗口时不激活（不抢焦点）

### Win+D 完整流程

1. `WH_KEYBOARD_LL` 低级键盘钩子检测 Win+D 组合键
2. 延迟 300ms 等待 Explorer 完成 ShowDesktop 动画
3. `SetWindowPos(HWND_TOPMOST)` 将所有 Fence 窗口临时置顶
4. `SetWinEventHook(EVENT_SYSTEM_FOREGROUND)` 持续监听前台窗口变化
5. 用户激活任何非 Fence 窗口 → `SetWindowPos(HWND_BOTTOM)` 恢复

### Peek 模式（Win+Space）

1. 全局热键捕获（RegisterHotKey）
2. 所有 Fence 窗口设为 HWND_TOPMOST + 提高透明度/动画
3. 再次按下或 Escape 退出 Peek，恢复 HWND_BOTTOM

---

## 4. 关键 Win32 API

| API | 用途 |
|-----|------|
| `SetWindowLongPtr(GWL_EXSTYLE, WS_EX_TOOLWINDOW \| WS_EX_NOACTIVATE)` | 隐藏于任务栏/Alt+Tab 且不抢焦点 |
| `SetWindowPos(HWND_BOTTOM)` | 正常态：桌面之上、窗口之下 |
| `SetWindowPos(HWND_TOPMOST)` | Win+D 后临时置顶 |
| `SetWindowsHookEx(WH_KEYBOARD_LL)` | 全局键盘钩子检测 Win+D |
| `SetWinEventHook(EVENT_SYSTEM_FOREGROUND)` | 监听前台窗口变化，自动恢复 HWND_BOTTOM |
| `RegisterHotKey` | 注册 Peek 热键 |
| `SHGetFileInfo` / `IExtractIcon` | 提取文件图标 |
| `SHChangeNotifyRegister` | Shell 变更通知（桌面文件增删） |

---

## 5. 参考资料

- [Win+D 窗口存活方案讨论](https://learn.microsoft.com/en-us/answers/questions/2127546/)
- [Draw Behind Desktop Icons (CodeProject)](https://www.codeproject.com/Articles/856020/Draw-Behind-Desktop-Icons-in-Windows-plus)
- [Lively Wallpaper (WorkerW 实现参考)](https://github.com/rocksdanister/lively)
