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
| 14 | [右键托盘小图标导致 fence 浮到最大化窗口之上](tray_right_click_fences_pop_to_front.md) | 其他程序最大化时右键系统托盘小图标，所有 fence/overlay 被强行拉到 HWND_TOPMOST。原因：非 topmost 分支 hoist 触发条件包含了 Shell_TrayWnd；点托盘前 foreground 会短暂切到任务栏 | 已修复 | 2026-05-09 |
| 15 | [文件图标显示与系统关联不一致](icon_wrong_app_association.md) | `.docx` 显示红色 MS Word 图标而非已设默认的 WPS 蓝图标。两段式：①ShellIconExtractor 改用 IShellItemImageFactory 解决抽图模糊；② 把 Shell 风格暴露到外观设置 picker 与 fence 菜单 | 已修复 | 2026-05-09 |
| 16 | [保存设置后 Portal Fence 内容被清空](portal_files_wiped_after_save_settings.md) | Portal fence 在保存任意设置（IconStyle、Hue 等）后立刻变空。SettingsWindow 保存按钮无条件 fire RulesSaved → ReEvaluateClassifiedFiles 把"不被任何规则匹配"的文件全部 RemoveFile，portal 的外部文件夹文件首当其冲 | 已修复 | 2026-05-09 |
| 17 | [设置-分类规则下拉框选中后展示与下拉项不一致](rules_combobox_selectionbox_tostring.md) | 「匹配方式」「目标 Fence」下拉项正常显示中文，闭合后却显示对象 ToString（如 `MatchTypeOption { Display = ... }`）。自定义 ComboBox ControlTemplate 下 `DisplayMemberPath` 不会填充 `SelectionBoxItemTemplate`，需用显式 `ItemTemplate` | 已修复 | 2026-05-09 |
| 18 | [Cell 内 icon/文字水平垂直中心不一致（Overlay + FencePanel）](overlay_icon_text_misalignment.md) | DesktopIconOverlay 用 StackPanel、FencePanel 三个 file tile DataTemplate 用「外 Grid HorizontalAlignment=Center+VerticalAlignment=Center」包 icon+文字，中间容器尺寸都被内容反推，导致同行/同列 icon 中心错位、文字 wrap 行数变化时 icon 上下挪位。两处统一改为外容器撑满 Border + 两行固定槽位 + SnapsToDevicePixels/UseLayoutRounding | 已修复 | 2026-05-09 |
| 19 | [未归档 cell 空白区域单击无法选中](overlay_cell_blank_area_not_selectable.md) | 单击 cell 内 icon/文字之外的空白区域 cell 不被选中。**真因**：`AllowsTransparency=True` 是 layered window，OS 按每像素 alpha 决定 click 走向，alpha=0 直接 click-through，**不会进 WPF**。`Brushes.Transparent` (alpha=0) 把 cell 整片做成 OS 透传。修复：cell Border 改用 alpha=1 的 `ClickableTransparentBrush` (`Color.FromArgb(1,0,0,0)`)，视觉无差但 OS 视为可命中；`ClearSelection` 同步用同一画刷 | 已修复 | 2026-05-09 |
| 20 | [其他程序打字时 fence 一闪而过](typing_d_in_other_app_flashes_fence.md) | 其他程序里打字（输入含字母 D 的文本）时，fence 突然激活并一闪而过，未按 Win+D / Win+Space。**真因**：`DesktopEmbedManager` 用累积布尔值 `_winKeyDown` 跟踪 Win 键状态，但 Win+L/Win+E 等系统组合键的 KEYUP 经常不传到低级钩子，导致标志残留为 true，之后打到 D 键被误判为 Win+D。修复：移除累积状态，改用 `GetAsyncKeyState(VK_LWIN/VK_RWIN)` 在 D KEYDOWN 时实时查询物理按键状态 | 已修复 | 2026-05-09 |
| 21 | [设置窗口打开后未归档图标和 panel 图标无法选中](settings_modal_disables_fences.md) | 打开设置窗口后，fence panel 内文件图标和未归档图标层 (`DesktopIconOverlay`) 全部无法选中 / 双击 / 拖拽。**真因**：`SettingsWindow.ShowDialog()` 是 WPF 模态对话框，Win32 层会对同线程所有其他顶层窗口调用 `EnableWindow(FALSE)`，整个窗口在 OS 层就被排除在输入路由之外，与 bug 19 像素级 alpha=0 click-through 不同层。修复：改为 `Show()` 非模态，用字段缓存当前实例避免重复打开 | 已修复 | 2026-05-09 |
| 22 | [最近关闭删除按钮样式与现有 UI 不一致](closed_fence_delete_button_style.md) | Fence 管理 → 最近关闭卡片的「删除」按钮回退到 WPF 默认灰白方角样式，与并排的「恢复」按钮（AccentButtonStyle 蓝色圆角）严重不协调。**真因**：`BuildClosedCard()` 代码后台 `new Button{}` 时未挂 Style，WPF 不会自动应用主题。修复：挂上已有的 `DangerButtonStyle`（与 Accent 同模板，红色背景），padding 对齐恢复按钮 | 已修复 | 2026-05-09 |
| 23 | [托盘右键菜单样式与暗色 UI 不一致](tray_menu_dark_style.md) | 通知区右键菜单仍是 Windows 经典灰白原生外观。**真因**：托盘菜单是 WinForms `NotifyIcon.ContextMenuStrip`，WPF DarkTheme 样式对 WinForms 控件无效。修复：实现 `DarkTrayMenuRenderer`（自定义 `ProfessionalColorTable`+`ToolStripProfessionalRenderer`），色板与 DarkTheme 同源；动态刷新的子菜单（最近关闭、快照）每次 rebuild 后递归刷 `ForeColor`/`BackColor` | 已修复 | 2026-05-09 |
| 24 | [托盘新建 Fence 后窗口被推到壁纸下方（bug 6 补全）](new_fence_invisible_normal_window_foreground.md) | 托盘右键"新建 Fence"后 fence 完全看不见，必须"双击桌面隐藏 + 双击桌面展示"才能拉回。**真因**：bug 6 修复时假设"普通窗口前台 → HWND_BOTTOM 安全"，但托盘菜单刚关闭瞬间 foreground 处于过渡态，DWM 仍把 HWND_BOTTOM 推到壁纸下方。`GetForegroundWindow()` 返回的"普通窗口" 无法反映 DWM 内部的"准桌面态"判定。修复：`BringNewWindowToFront` 不再按 foreground 分支，统一用 `HWND_TOPMOST` 拉到壁纸上方，依赖既有 `OnDebouncedForegroundRecovery → HWND_BOTTOM` 隐式降级 | 已修复 | 2026-05-15 |

## 常见问题说明

### Windows 11 z-order特性
多个bug都和Windows 11的z-order特性相关：当当前前台窗口是桌面（Progman/WorkerW）或任务栏（Shell_TrayWnd）时，对 `WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE` 窗口调用 `SetWindowPos(HWND_BOTTOM)` 或 `SetWindowPos(HWND_TOP)` 都可能被 DWM 推到桌面壁纸层下面，导致窗口不可见。**只有 `HWND_TOPMOST` 能稳妥地让窗口呈现在桌面壁纸之上。**

解决这个问题的通用原则是：
1. 永远不要在前台窗口是桌面时调用`SetWindowPos(HWND_BOTTOM)`，对 `HWND_TOP` 同样不可靠
2. 当桌面/任务栏是前台、又必须立即让窗口可见时，使用 `HWND_TOPMOST`；等前台变化时通过 `HWND_BOTTOM` 自动清除 topmost 状态（`HWND_BOTTOM` 隐含降级 topmost，无需单独 `HWND_NOTOPMOST`）
3. 使用z-order恢复定时器，定期检查窗口是否可见，必要时进行恢复
4. **`HWND_TOPMOST` 只用于"用户主动新建窗口"这类短暂场景**——如果在启动加载、`ToggleAllFences`、`DesktopIconOverlay` 等常规路径上也使用 topmost，会让 fence/overlay 一直浮动在普通应用之上，并连带破坏 Win+D 时桌面图标 overlay 的显示状态。修改全局 z-order 行为前先列出所有调用方
5. **前台是桌面/任务栏时，z-order 自愈逻辑必须主动用 `HWND_TOPMOST` 拉回，而不是 return**——5 秒定时器与 `OnForegroundChanged` 桌面分支都应该这样做；topmost 由后续切到普通窗口时的 `HWND_BOTTOM` 自动降级清除
6. **新建窗口路径不要用 `GetForegroundWindow()` 做分支** —— 托盘菜单刚关闭等"前台过渡态"瞬间，即使返回值是普通窗口，DWM 仍可能把 `HWND_BOTTOM` 推到壁纸下（bug 24）。用户主动新建路径统一走 `HWND_TOPMOST`，依靠后续 `OnDebouncedForegroundRecovery → HWND_BOTTOM` 隐式降级

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
