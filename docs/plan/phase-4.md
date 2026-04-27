# Phase 4: 高级功能

**目标**：实现 Rollup、Peek、Quick Hide 等 Fences 标志性功能。

## 4.1 Rollup 折叠
- [x] 双击标题栏 → `DoubleAnimation` 高度缩小到 38px（标题栏高度），只显示标题
- [x] 折叠态鼠标悬停 → `HoverExpand()` 临时展开到 `ExpandedHeight`
- [x] 鼠标离开 → `HoverCollapse()` 自动折回 38px
- [x] `IsRolledUp` / `ExpandedHeight` 保存到持久化
- [x] `FenceHost` 初始化时检查 `IsRolledUp`，已折叠的 Fence 直接以折叠态显示
- [x] `RollupChanged` 事件通知 `FenceHost` 同步窗口高度动画

## 4.2 Peek 快速预览
- [x] `PeekManager.cs` — 使用 `RegisterHotKey(MOD_WIN | MOD_NOREPEAT, VK_SPACE)` 注册 Win+Space
- [x] 创建隐藏 `HwndSource` 接收 `WM_HOTKEY` 消息
- [x] Peek 激活 → `DesktopEmbedManager.EnterPeek()` → 所有窗口 `HWND_TOPMOST`
- [x] Peek 期间 `OnForegroundChanged` 钩子不自动恢复 BOTTOM（`_isPeekActive` 保护）
- [x] 再次 Win+Space → toggle 退出 Peek
- [x] Escape 键 → `OnEscapePressed()` 退出 Peek（通过键盘钩子检测 `VK_ESCAPE`）
- [ ] DWM 背景模糊（延迟到 Phase 6 优化）

## 4.3 Quick Hide 快速隐藏
- [x] `QuickHideManager.cs` — 低级鼠标钩子 (`WH_MOUSE_LL`) 检测桌面双击
- [x] 通过 `WindowFromPoint` + `GetClassName` 判断点击目标是否为桌面（Progman / WorkerW / SHELLDLL_DefView / SysListView32）
- [x] 手动双击检测（500ms 阈值，4px 距离容差），避免依赖系统 `WM_LBUTTONDBLCLK`
- [x] 双击 → 调用 `ToggleAllFences()` 隐藏/显示所有 Fence
- [x] 系统托盘双击也触发 toggle（Phase 1 已实现）

## 4.4 Tab 合并
- [ ] Tab 合并功能延迟到 Phase 6（需要较大的 UI 重构，涉及标题栏 Tab 条 + 拖放合并/拆分逻辑）

## 4.5 多视图模式
- [ ] 多视图模式延迟到 Phase 6（需要新增 ListView/DetailView 的 DataTemplate + 切换逻辑）

## 4.6 文件排序
- [x] `FencePanelViewModel.ApplySort()` — 支持 5 种排序字段：名称、扩展名、大小、修改日期、创建日期
- [x] 升序/降序切换，`SortBy` / `SortDirection` 属性
- [x] 排序通过 `ObservableCollection.Move()` 原地重排，保持 UI 绑定
- [x] 排序后自动 `SyncToModel()` 同步到持久化
- [x] 每个 Fence 独立排序设置，从 Model 加载

**验收标准**：标题栏双击折叠/展开、Win+Space 弹出 Peek、桌面双击隐藏/显示、文件可排序。 ✅
