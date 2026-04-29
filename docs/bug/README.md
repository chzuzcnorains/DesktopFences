# DesktopFences 历史bug汇总

本文档汇总了项目开发过程中遇到的所有bug及其修复情况。

## bug列表

| 序号 | bug名称 | 问题描述 | 修复状态 | 修复版本 |
|------|---------|----------|----------|----------|
| 1 | [Win+D后右键自己的图标panel隐藏](win_d_right_click_panel_hide.md) | 按下Win+D显示桌面后，右键点击程序自己的图标，Fence面板会隐藏 | 已修复 | 2026-04-28 |
| 2 | [截图后Fence面板不展示](screenshot_after_panel_disappear.md) | 使用截图工具截图后，桌面上的Fence面板会消失，需要切换窗口才能重新显示 | 已修复 | 2026-04-28 |
| 3 | [虚伪边框的bug](fake_border_issue.md) | Fence面板周围有一个多余的透明边框，窗口实际大小比内容大 | 已修复 | 2026-04-28 |
| 4 | [吸附后面板消失的bug](snap_after_panel_disappear.md) | 拖动Fence窗口进行吸附对齐操作后，窗口会突然消失 | 已修复 | 2026-04-28 |
| 5 | [启用规则时自动创建缺失的Fence功能异常](rule_target_fence_not_found.md) | 规则被禁用后重新启用，或规则的目标Fence被删除后启用规则时，系统没有自动创建对应的Fence，反而错误绑定到其他Fence | 已修复 | 2026-04-28 |
| 6 | [新建Fence后不立刻显示](new_fence_not_visible_immediately.md) | 右键托盘新建Fence后窗口不可见，必须打开设置等普通窗口才会显现 | 已修复 | 2026-04-29 |
| 7 | [最近关闭列表无法删除且时间错误](closed_fences_no_delete_and_wrong_time.md) | Fence 管理 → 最近关闭只有恢复没有删除按钮，且关闭时间永远显示"刚刚" | 已修复 | 2026-04-29 |
| 8 | [滚动条样式与暗色设计不匹配](scrollbar_native_style.md) | 滚动条都是 Windows 原生灰白滚动条（带上下箭头按钮），与项目整体暗色 UI 风格严重不协调| 已修复 | 2026-04-29 |

## 常见问题说明

### Windows 11 z-order特性
多个bug都和Windows 11的z-order特性相关：当当前前台窗口是桌面（Progman/WorkerW）或任务栏（Shell_TrayWnd）时，对 `WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE` 窗口调用 `SetWindowPos(HWND_BOTTOM)` 或 `SetWindowPos(HWND_TOP)` 都可能被 DWM 推到桌面壁纸层下面，导致窗口不可见。**只有 `HWND_TOPMOST` 能稳妥地让窗口呈现在桌面壁纸之上。**

解决这个问题的通用原则是：
1. 永远不要在前台窗口是桌面时调用`SetWindowPos(HWND_BOTTOM)`，对 `HWND_TOP` 同样不可靠
2. 当桌面/任务栏是前台、又必须立即让窗口可见时，使用 `HWND_TOPMOST`；等前台变化时通过 `HWND_BOTTOM` 自动清除 topmost 状态（`HWND_BOTTOM` 隐含降级 topmost，无需单独 `HWND_NOTOPMOST`）
3. 使用z-order恢复定时器，定期检查窗口是否可见，必要时进行恢复
4. **`HWND_TOPMOST` 只用于"用户主动新建窗口"这类短暂场景**——如果在启动加载、`ToggleAllFences`、`DesktopIconOverlay` 等常规路径上也使用 topmost，会让 fence/overlay 一直浮动在普通应用之上，并连带破坏 Win+D 时桌面图标 overlay 的显示状态。修改全局 z-order 行为前先列出所有调用方

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
