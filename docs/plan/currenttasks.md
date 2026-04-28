# 当前任务列表

## Phase 6 补全 — Snap 视觉反馈 + Inno Setup 安装包

- [x] Inno Setup 安装脚本 + 构建脚本（`tools/installer/`）
- [x] SnapEngine 扩展（`SnapWithDetail`, `SnapResize`, `SnapLine`）
- [x] NativeMethods 新增 Win32 常量（`WM_MOVING`, `WM_SIZING` 等）
- [x] SnapGuideOverlay 窗口（透明覆盖层，Canvas 绘制辅助线）
- [x] FenceHost WM_MOVING/WM_SIZING 拦截（HwndSourceHook）
- [x] 替换 DragMove() 为 WM_NCLBUTTONDOWN + HTCAPTION
- [x] Resize 吸附支持（Thumb DragDelta 中调用 SnapResize）
- [x] App.xaml.cs 集成 SnapGuideOverlay 生命周期管理
- [x] 文档更新（snap-engine.md, phase-6.md, todo.md）

---
*此文件由 CLAUDE.md 结构拆分时创建*
*当有新的任务时，在此记录任务名称、负责人和截止时间*
