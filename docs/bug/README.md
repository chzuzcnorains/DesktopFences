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
| 9 | [未归档icon显示模糊](icon_blurry.md) | 未归档的桌面图标显示不够锐利，比Windows 11原生桌面图标模糊 | 已修复 | 2026-04-29 |
| 10 | [启动时未归档图标不显示 / 截图后面板消失](startup_overlay_invisible_and_screenshot_recovery.md) | 启动时未归档图标有时不显示，偶尔截图后面板与未归档图标一起消失，必须切换前景窗口才出现 | 已修复 | 2026-04-29 |
| 11 | [切换图标风格后已显示的 tile 不刷新](icon_style_switch_no_refresh.md) | 外观设置切 App ↔ System 风格保存后，已渲染的文件 tile 不切模板，必须重启或刷新数据才生效 | 已修复 | 2026-05-07 |
| 12 | [模糊强度 > 0 时颜色/透明度调整失效](acrylic_masks_color_opacity.md) | 设置模糊强度后，背景色调和透明度滑块完全不生效，fence 始终是灰白磨砂玻璃；Acrylic 在 Win11 22H2+ 加了 luminosity tint 层覆盖 WPF 背景 | 已修复 | 2026-05-07 |
| 13 | [设置模糊强度后 panel 圆角丢失](blur_corners_squared.md) | 启用 BlurBehind 后 fence 四个圆角变方，因为 DWM blur 早于 WPF 渲染，WPF 的 CornerRadius 截断不了；用 SetWindowRgn 给窗口本身设圆角剪裁区域解决 | 已修复 | 2026-05-07 |

## 常见问题说明

### Windows 11 z-order特性
多个bug都和Windows 11的z-order特性相关：当当前前台窗口是桌面（Progman/WorkerW）或任务栏（Shell_TrayWnd）时，对 `WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE` 窗口调用 `SetWindowPos(HWND_BOTTOM)` 或 `SetWindowPos(HWND_TOP)` 都可能被 DWM 推到桌面壁纸层下面，导致窗口不可见。**只有 `HWND_TOPMOST` 能稳妥地让窗口呈现在桌面壁纸之上。**

解决这个问题的通用原则是：
1. 永远不要在前台窗口是桌面时调用`SetWindowPos(HWND_BOTTOM)`，对 `HWND_TOP` 同样不可靠
2. 当桌面/任务栏是前台、又必须立即让窗口可见时，使用 `HWND_TOPMOST`；等前台变化时通过 `HWND_BOTTOM` 自动清除 topmost 状态（`HWND_BOTTOM` 隐含降级 topmost，无需单独 `HWND_NOTOPMOST`）
3. 使用z-order恢复定时器，定期检查窗口是否可见，必要时进行恢复
4. **`HWND_TOPMOST` 只用于"用户主动新建窗口"这类短暂场景**——如果在启动加载、`ToggleAllFences`、`DesktopIconOverlay` 等常规路径上也使用 topmost，会让 fence/overlay 一直浮动在普通应用之上，并连带破坏 Win+D 时桌面图标 overlay 的显示状态。修改全局 z-order 行为前先列出所有调用方
5. **前台是桌面/任务栏时，z-order 自愈逻辑必须主动用 `HWND_TOPMOST` 拉回，而不是 return**——5 秒定时器与 `OnForegroundChanged` 桌面分支都应该这样做；topmost 由后续切到普通窗口时的 `HWND_BOTTOM` 自动降级清除

### 窗口样式限制
Fence窗口使用`WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE`样式，这种窗口有以下限制：
1. 不会显示在任务栏和Alt+Tab列表中
2. 最小化后无法通过常规方式找回
3. 在Windows 11中z-order行为特殊

因此在开发时需要注意：
1. 完全禁止窗口最小化
2. 谨慎处理z-order变化
3. 定期检查窗口可见性

### DPI 缩放处理
Windows 11 支持多种 DPI 缩放级别（100%、125%、150%、175%、200%等），WPF 使用设备无关像素 (DIP) 作为单位：

关键要点：
1. **1 DIP = 物理像素 × (96 / 当前 DPI)**
   - 100% DPI 下：1 DIP = 1 物理像素
   - 150% DPI 下：1 DIP = 1.5 物理像素
   - 200% DPI 下：1 DIP = 2 物理像素

2. **实际测量验证**
   - 不要只依赖公式计算，要实际截图测量 UI 显示效果
   - Windows 11 原生桌面图标在 150% DPI 下显示为 72 物理像素（72 / 1.5 = 48 DIP）

3. **图标渲染最佳实践**
   - 使用 `Stretch.Uniform` 配合固定尺寸，让 WPF 进行高质量缩放
   - `BitmapScalingMode.HighQuality` 对于图标缩放效果通常最好
   - 启用 `SnapsToDevicePixels` 和 `UseLayoutRounding` 确保像素对齐，避免模糊
   - 不要过度复杂化，简单的 `SHGetFileInfo` 配合正确的显示设置通常最稳定

## 修复验证标准
所有bug修复后需要通过以下验证：
1. 原问题场景不再复现
2. 相关功能正常工作，没有引入新的问题
3. 性能没有明显下降
4. 符合设计文档中的预期行为
