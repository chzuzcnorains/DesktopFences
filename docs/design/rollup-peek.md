# Rollup 折叠 & Peek 预览

## 1. Rollup 折叠

### 状态机

```
Expanded ──(点击收起箭头 ▲)──→ RolledUp
RolledUp ──(鼠标悬停 hover_delay ms)──→ PeekExpanded (临时展开)
PeekExpanded ──(鼠标离开)──→ RolledUp
RolledUp ──(点击展开箭头 ▼)──→ Expanded
```

### UI 布局

```
标题栏右侧按钮区：[▲/▼ 收起/展开] [⋯ 菜单]
Tab 条右侧按钮区：[▲/▼ 收起/展开] [⋯ 菜单]
注：已移除双击标题栏触发折叠的行为，避免与快速 Tab 切换冲突
```

### 动画

- 折叠：Height 从当前值动画到 RolledUpHeight (32px)，EaseOut 200ms
- 展开：Height 从 RolledUpHeight 动画到保存的展开高度，EaseOut 200ms
- 临时展开：同展开动画，但鼠标离开后自动折回

### 数据属性

- `IsRolledUp` / `ExpandedHeight` 保存到持久化
- FenceHost 初始化时检查 `IsRolledUp`，已折叠的 Fence 直接以折叠态显示
- `RollupChanged` 事件通知 FenceHost 同步窗口高度动画

---

## 2. Peek 快速预览

### 触发

`Win + Space`（全局热键，通过 RegisterHotKey）

### 行为

```
1. 所有 FenceHost 窗口 → SetWindowPos(HWND_TOPMOST)
2. 背景模糊效果（可选，通过 DWM Acrylic/Mica）
3. Fence 窗口播放淡入动画
4. 用户可在 Peek 状态下拖放文件到当前活动窗口
5. 再次 Win+Space 或 Escape → 退出 Peek，恢复 z-order
```

### 与 Fences 6 对齐

- 也可通过任务栏托盘图标单击触发
- Peek 时支持拖放文件到其他应用程序（核心生产力功能）

### 实现

- `PeekManager.cs` — 使用 `RegisterHotKey(MOD_WIN | MOD_NOREPEAT, VK_SPACE)` 注册 Win+Space
- 创建隐藏 `HwndSource` 接收 `WM_HOTKEY` 消息
- Peek 激活 → `DesktopEmbedManager.EnterPeek()` → 所有窗口 `HWND_TOPMOST`
- Peek 期间 `OnForegroundChanged` 钩子不自动恢复 BOTTOM（`_isPeekActive` 保护）
- 再次 Win+Space → toggle 退出 Peek
- Escape 键 → `OnEscapePressed()` 退出 Peek（通过键盘钩子检测 `VK_ESCAPE`）

---

## 3. Quick Hide 快速隐藏

### 触发

双击桌面空白区域

### 行为

```
1. 检测双击位置不在任何 Fence 窗口内
2. 所有 Fence 窗口播放淡出动画 (Opacity 1→0, 200ms)
3. 动画完成后 Hide() 所有 FenceHost
4. 再次双击桌面空白区域 → 淡入动画恢复显示
5. 状态保存：可排除特定 Fence（如"固定"的 Fence 不隐藏）
```

### 实现难点

- 需要全局鼠标钩子 (WH_MOUSE_LL) 检测桌面双击
- 区分"桌面空白区域"和"Fence 窗口内"的点击
- 避免误触发（和正常双击打开文件冲突）

### 实现

- `QuickHideManager.cs` — 低级鼠标钩子 (`WH_MOUSE_LL`) 检测桌面双击
- 通过 `WindowFromPoint` + `GetClassName` 判断点击目标是否为桌面（Progman / WorkerW / SHELLDLL_DefView / SysListView32）
- 手动双击检测（500ms 阈值，4px 距离容差），避免依赖系统 `WM_LBUTTONDBLCLK`
- 双击 → 调用 `ToggleAllFences()` 隐藏/显示所有 Fence
- 系统托盘双击也触发 toggle
