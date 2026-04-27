# Phase 1: 核心交互

**目标**：Fence 窗口可以拖动、调整大小、Snap 吸附。

## 1.1 FencePanel 控件骨架 (UI 项目)
- [x] `FencePanel.xaml` — 核心 UserControl（TitleBar + Content + 8向 ResizeGrip Thumb）
- [x] `FencePanelViewModel.cs` — MVVM ViewModel（Title, X, Y, Width, Height, IsRolledUp, ViewMode）
- [x] `ViewModelBase.cs` — INotifyPropertyChanged 基类

## 1.2 拖动 (UI 项目)
- [x] TitleBar `MouseLeftButtonDown` → `Window.DragMove()`
- [x] 拖动结束 → 同步位置到 ViewModel → 触发 Snap + AutoSave

## 1.3 调整大小 (UI 项目)
- [x] 8 个方向透明 Thumb 控件（Top, Bottom, Left, Right, 4 个角）
- [x] Thumb.DragDelta → 直接修改 Window 和 ViewModel 的 Width/Height/X/Y
- [x] 最小尺寸约束（Width >= 120, Height >= 60）

## 1.4 Snap 吸附 (Core 项目)
- [x] `SnapEngine.cs` — 纯函数，无副作用
  - 输入：moving Rect + other Rects + screen bounds
  - 输出：吸附修正后的 SnapResult
  - 算法：遍历所有边，找到 distance < 10px 的最近吸附目标
- [x] 拖动/调整大小结束时在 App 层调用 SnapEngine
- [x] 7 个单元测试全部通过
- [ ] 按住 Alt 临时禁用吸附（延迟到后续优化）

## 1.5 系统托盘 + Fence 管理 (App 项目)
- [x] `NotifyIcon` 系统托盘图标 + 右键菜单
  - New Fence / Show-Hide All / Save Layout / Exit
- [x] 多 Fence 窗口管理（创建、关闭、显示/隐藏切换）
- [x] 双击托盘图标 → 显示/隐藏所有 Fence

## 1.6 布局持久化 (Core 项目)
- [x] `JsonLayoutStore.cs` — JSON 文件持久化到 `%APPDATA%\DesktopFences\`
- [x] 原子写入（临时文件 → rename）
- [x] 启动时自动加载、操作后 debounce 2秒自动保存
- [x] 首次运行自动创建默认 Fence

**验收标准**：可以创建多个 Fence，拖动移动，调整大小，Fence 之间和屏幕边缘会吸附对齐。 ✅
