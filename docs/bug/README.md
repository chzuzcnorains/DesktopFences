# DesktopFences 历史bug汇总

本文档汇总了项目开发过程中遇到的所有bug及其修复情况。

## bug列表

| 序号 | bug名称 | 问题描述 | 修复状态 | 修复版本 |
|------|---------|----------|----------|----------|
| 1 | [Win+D后右键自己的图标panel隐藏](win_d_right_click_panel_hide.md) | 按下Win+D显示桌面后，右键点击程序自己的图标，Fence面板会隐藏 | 已修复 | 2026-04-28 |
| 2 | [截图后Fence面板不展示](screenshot_after_panel_disappear.md) | 使用截图工具截图后，桌面上的Fence面板会消失，需要切换窗口才能重新显示 | 已修复 | 2026-04-28 |
| 3 | [虚伪边框的bug](fake_border_issue.md) | Fence面板周围有一个多余的透明边框，窗口实际大小比内容大 | 已修复 | 2026-04-28 |
| 4 | [吸附后面板消失的bug](snap_after_panel_disappear.md) | 拖动Fence窗口进行吸附对齐操作后，窗口会突然消失 | 已修复 | 2026-04-28 |

## 常见问题说明

### Windows 11 z-order特性
多个bug都和Windows 11的z-order特性相关：当当前前台窗口是桌面时，调用`SetWindowPos(HWND_BOTTOM)`会将`WS_EX_TOOLWINDOW`类型的窗口推到桌面壁纸层下面，导致窗口不可见。

解决这个问题的通用原则是：
1. 永远不要在前台窗口是桌面时调用`SetWindowPos(HWND_BOTTOM)`
2. 如果必须调整z-order，可以先临时设置为`HWND_TOP`，等待前台窗口变化后再恢复为`HWND_BOTTOM`
3. 使用z-order恢复定时器，定期检查窗口是否可见，必要时进行恢复

### 窗口样式限制
Fence窗口使用`WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE`样式，这种窗口有以下限制：
1. 不会显示在任务栏和Alt+Tab列表中
2. 最小化后无法通过常规方式找回
3. 在Windows 11中z-order行为特殊

因此在开发时需要注意：
1. 完全禁止窗口最小化
2. 谨慎处理z-order变化
3. 定期检查窗口可见性

## 修复验证标准
所有bug修复后需要通过以下验证：
1. 原问题场景不再复现
2. 相关功能正常工作，没有引入新的问题
3. 性能没有明显下降
4. 符合设计文档中的预期行为
