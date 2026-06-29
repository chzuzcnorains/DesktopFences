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
| 25 | [持久化数据丢失链](persistence_data_loss_chain.md) | 系统排查发现的四连环：①损坏 JSON 抛异常被 fire-and-forget 吞掉 → 应用空状态运行 → 自动保存用空列表覆盖 fences.json；②托盘退出 `_ = SaveFencesAsync(); Shutdown()` 竞态丢最后一次保存；③auto-save 定时器与直接调用并发写同一 .tmp → IOException 被吞；④序列化异常残留 .tmp。修复：损坏文件备份 .corrupt-* 后回退默认 + 弹窗提示、`_loadFailed` 禁止写盘、退出 await 保存 + SessionEnding 兜底、store 内 SemaphoreSlim 串行化写入。**补强（06-15）**：`ReadResilientAsync` 只对 `JsonException`（内容坏）回退默认，`IOException/UnauthorizedAccessException`（文件被临时锁定等瞬时态）改为上抛 → `_loadFailed` 禁写，避免误把好文件覆盖为空 | 已修复 | 2026-06-12（补强 06-15） |
| 26 | [双击打开文件可崩溃整个应用](openfile_crash_no_global_handler.md) | 双击已删除/无关联程序的文件，`Process.Start` 抛 Win32Exception，三个调用点无 try-catch 且项目无任何全局异常处理 → 进程崩溃、桌面图标层无法恢复。修复：OpenFile 返回 bool 内部捕获 + 调用点 Toast + 注册 `DispatcherUnhandledException` 全局兜底 | 已修复 | 2026-06-12 |
| 27 | [应用内拖拽产生重复条目](internal_drag_duplicate_entries.md) | fence→fence 拖拽文件同时存在于两个 fence；overlay→fence 后 overlay 图标残留。**真因**：`OnDrop` 不设置 `e.Effects`，源端 `result == Move` 判断永远不成立。修复：自定义 DataFormat 标记区分应用内拖拽，内部回报 Move（移动语义）、Explorer 来源恒回报 Copy（回报 Move 会让 Explorer 删源文件） | 已修复 | 2026-06-12 |
| 28 | [ShellContextMenu COM 对象泄漏](shell_context_menu_com_leak.md) | 每次右键文件弹 Shell 菜单泄漏 IShellFolder/IContextMenu 两个 COM 引用，失败提前 return 路径同样泄漏。修复：try/finally `Marshal.ReleaseComObject` 覆盖所有路径 | 已修复 | 2026-06-12 |
| 29 | [重命名对话框模态禁用所有 fence（bug 21 残留）](rename_modal_disables_fences.md) | `RenameWindow.ShowDialog()` 期间所有 fence/overlay 无法点击，bug 21 同根因（模态 EnableWindow(FALSE) 同线程所有窗口）。修复：改 Show() 非模态 + `RenameConfirmed` 事件回调 + 实例缓存防重复打开。**规则：自定义 WPF 窗口一律禁止 ShowDialog** | 已修复 | 2026-06-12 |
| 30 | [FSW 关闭竞态崩溃 + Error 未处理](fsw_dispose_race_and_error_event.md) | 退出时在途文件事件操作已 Dispose 的防抖 timer → ObjectDisposedException 在线程池线程 = 进程崩溃（UI 层全局兜底救不了）；FSW 缓冲区溢出后 Error 未订阅 → 监控静默失效最长 30 秒。修复：timer 操作入锁 + disposed 检查；订阅 Error 立即全量扫描自愈 | 已修复 | 2026-06-12 |
| 31 | [路径大小写敏感比较产生重复条目](path_case_duplicate_entries.md) | `FencePanelViewModel.AddFile/RemoveFile` 用大小写敏感比较，与项目统一的 OrdinalIgnoreCase 约定不一致，同一文件不同大小写路径绕过去重。修复：统一 OrdinalIgnoreCase；顺带修复右键菜单 `Window.GetWindow(this)!` 潜在 NRE | 已修复 | 2026-06-12 |
| 32 | [自定义 MenuItem 模板缺 Popup 致子菜单无法展开](submenu_no_popup_template.md) | 图标风格/呈现方式/排序方式子菜单点了没反应。`DarkMenuItemStyle` 自定义 MenuItem 模板缺 `Popup`+`ItemsPresenter`，单模板替换全部角色（含 SubmenuHeader）→ 带子项的 MenuItem 无法展开。修复：模板补 `PART_Popup`+`ItemsPresenter`+箭头指示 | 已修复 | 2026-06-15 |
| 33 | [Tab 菜单缺少视图/排序/图标风格子菜单](tab_menu_missing_view_sort.md) | tab 条菜单由 `FenceHost.TabMenuButton_Click` 单独构建，未含 standalone 菜单的三个子菜单。修复：抽 `FencePanel.AddViewSortMenuItems` 供两套菜单复用，tab 菜单作用于活动 tab | 已修复 | 2026-06-15 |
| 34 | [从 tab 拖拽分离 fence 报"源文件名和目标文件名相同"](tab_drag_detach_file_error.md) | tab 拖拽只重排序无撕离；`CaptureMode.SubTree` 捕获把 MouseMove 漏给文件 tile，`FileItem_MouseMove` 误发 OLE 文件拖拽，拖到桌面致 shell 同名文件报错。修复：实现拖拽撕离（垂直越界→`TabDetachRequested`/`DetachTab`）+ `FileItem_MouseMove` 加 `Mouse.Captured==宿主窗口` 守卫 | 已修复 | 2026-06-15 |
| 35 | [鼠标移到任务栏闪烁图标时 Fence/未归档图标消失](taskbar_flashing_icon_sinks_fences.md) | 任务栏闪烁图标多是最小化应用在请求关注；鼠标移过去时它抢到前台（仍最小化、不遮挡桌面），旧逻辑按"非桌面前台"把 fence `SendToBottom(HWND_BOTTOM)` → 被 Win11 DWM 压到壁纸下方，5 秒定时器持续重沉永不恢复。修复：①`IsIconic` 前台一律不下沉（预防）；②`WindowFromPoint` 实测窗口被桌面遮挡则整组 hoist（类名无关自愈兜底，overlay 因 alpha=0 透传不探测、随 fence 一并拉回） | 已修复 | 2026-06-15 |
| 36 | [Win+D 恢复后 overlay+fence 卡顶层 / 启动时 overlay 不显示（bug 35 回归+补全）](windd_restore_fence_stuck_and_startup_overlay.md) | ①Win+D 第二次恢复瞬间前台仍是桌面/还原中的 iconic 窗口，bug 35 给 `SendToBottom` 加的 `IsIconic` 守卫即时拦下 → `HWND_BOTTOM` 从未执行、`_isTopmost` 已清但物理停 `HWND_TOPMOST` → overlay+fence 一起卡顶层。②overlay 被 `IsAnyFenceSunkBehindDesktop` 自愈排除，fence 正常时单独沉下无人捞回（叠加层叠透明初次合成可能不绘制）。修复：①Win+D 两恢复分支 + `ExitPeek` 即时 `SetAllBottom` 后补一次 `StartForegroundDebounce` 延迟重沉（复用含延迟 `IsIconic` 判定的防抖通道）；②overlay `OnLoaded` 两拍（100/500ms）`EnsureOverlayVisible`（借用 topmost）+ `InvalidateVisual` 强制重绘 | 已修复 | 2026-06-25 |
| 37 | [Win+D 后点击窗口 B 还原 overlay+fence 卡顶层（bug 36 第 4 条路径）](windd_restore_fence_stuck_and_startup_overlay.md) | 最大化窗口 A 按 Win+D 后点击任务栏窗口 B 还原，overlay+fence 卡顶层、要等 5 秒兜底才隐藏。根因：bug 36 只在 3 条 `SetAllBottom` 路径手工补延迟重沉，漏了第 4 条——`OnForegroundChanged` 的 `_isTopmost` 分支（真实窗口激活），B 还原初期仍 iconic、即时 `SendToBottom` no-op。修复：把延迟重沉**收敛进 `SetAllBottom()` 自身**（循环后统一 `BeginInvoke(StartForegroundDebounce)`），删 3 处冗余显式调用，一次覆盖全部 4 条路径 | 已修复 | 2026-06-25 |
| 38 | [点击桌面导致 fences/overlay 浮到普通窗口之上](click_desktop_hoists_fences_above_apps.md) | 非最大化窗口 A 在前台，点击桌面 → fence/overlay 跳到 A 之上。根因：桌面成为前台时 `OnForegroundChanged` / 5 秒定时器**无条件** `HoistAllAboveDesktop()`（bug 10 旧逻辑，那时还没有可靠的"是否真沉"判据），把停在 `HWND_BOTTOM` 的正常 fence 抬到 `HWND_TOPMOST`。修复：用 bug 35 引入的 `IsAnyFenceSunkBehindDesktop()` **门控**——仅当实测真被压到壁纸下才 hoist，否则不动（`OnForegroundChanged` 加 50ms `ScheduleSunkRecheck` 兜截图恢复时序；5 秒定时器删桌面分支无条件 hoist） | 已修复 | 2026-06-25 |
| 39 | [从 Fence 拖文件到 overlay 报"源文件名和目标文件名相同"且文件消失](fence_to_overlay_drop_same_name_error.md) | 从 Fence 拖文件到未归档 overlay 空白区，弹 shell"源文件名和目标文件名相同"，跳过后文件在 overlay 和 Fence 都不显示。**真因**：overlay 是 `AllowsTransparency` 层叠窗口、空白区 alpha=0 被 OS 判为 click-through，OLE 放置命中（`WindowFromPoint`）穿透到真实桌面，桌面把"已在 Desktop 的文件"当同文件夹移动报错并回报 Move，源 Fence 据此删条目而 overlay 没被通知 AddIcon。修复：内部拖拽期间临时把 overlay 切成放置目标（alpha=1 + AllowDrop，结束还原），`OnDrop` 逻辑性 `AddIcon` 并回报 Move（`anyAdded`/`IsDesktopFile` 守卫）；FencePanel 拖拽起止事件驱动 + 源端删条目后补 `InteractionEnded` 持久化 | 已修复 | 2026-06-25 |
| 40 | [保存设置时 settings 被 UI 线程并发修改致序列化竞态](settings_serialize_uithread_mutation_race.md) | （评审发现 F2）多处 `_ = SaveSettingsAsync(_appSettings)` 把活的 `_appSettings` 交给后台序列化；`SerializeAsync` 在线程池枚举 `RecentClosedFences` 时，UI 线程可能并发 `Insert/RemoveAt` 同一 list（连续关闭多个 fence）→ `List<T>` 边读边写抛 `InvalidOperationException`，被 fire-and-forget 吞掉致该次保存静默失败。`store` 的 `_writeLock` 只串行化写、挡不住 UI 线程改 list。修复：`AppSettings.CloneForPersist()` 在调用方线程同步快照可变集合，`SaveSettingsAsync` 改用快照 | 已修复 | 2026-06-29 |
| 41 | [图标 LRU 重复键累积致活跃缓存被提前淘汰](icon_lru_duplicate_key_eviction.md) | （评审发现 F4）`ShellIconExtractor.AddToLru` 无条件 `AddFirst`，不像 `TouchLru` 先 `Remove`；`GetIcon` 的 check-then-act 非原子，多线程 miss 同 key 或淘汰后再加入会让 `_lruOrder` 出现同 key 多节点 → `Count` 虚高 → 仍在 `_cache` 的活跃项被提前淘汰（图标无谓重抽）。修复：`AddToLru` 先 `Remove(key)` 再 `AddFirst`，保证 key 唯一 | 已修复 | 2026-06-29 |

## 常见问题说明

### Windows 11 z-order特性
多个bug都和Windows 11的z-order特性相关：当当前前台窗口是桌面（Progman/WorkerW）或任务栏（Shell_TrayWnd）时，对 `WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE` 窗口调用 `SetWindowPos(HWND_BOTTOM)` 或 `SetWindowPos(HWND_TOP)` 都可能被 DWM 推到桌面壁纸层下面，导致窗口不可见。**只有 `HWND_TOPMOST` 能稳妥地让窗口呈现在桌面壁纸之上。**

解决这个问题的通用原则是：
1. 永远不要在前台窗口是桌面时调用`SetWindowPos(HWND_BOTTOM)`，对 `HWND_TOP` 同样不可靠
2. 当桌面/任务栏是前台、又必须立即让窗口可见时，使用 `HWND_TOPMOST`；等前台变化时通过 `HWND_BOTTOM` 自动清除 topmost 状态（`HWND_BOTTOM` 隐含降级 topmost，无需单独 `HWND_NOTOPMOST`）
3. 使用z-order恢复定时器，定期检查窗口是否可见，必要时进行恢复
4. **`HWND_TOPMOST` 只用于"用户主动新建窗口"这类短暂场景**——如果在启动加载、`ToggleAllFences`、`DesktopIconOverlay` 等常规路径上也使用 topmost，会让 fence/overlay 一直浮动在普通应用之上，并连带破坏 Win+D 时桌面图标 overlay 的显示状态。修改全局 z-order 行为前先列出所有调用方
5. **桌面前台时是否要 hoist，必须用 `IsAnyFenceSunkBehindDesktop()` 实测判据门控，不能只看「前台类名是桌面」就无条件拉回**（bug 38 更正了早期写法）——「前台是桌面」≠「fence 需要被拉回」。只有实测 fence 确实被压到壁纸下（`WindowFromPoint` 命中桌面类）才借用 `HWND_TOPMOST` 拉回；若 fence 其实好好停在 `HWND_BOTTOM`（用户只是点了下桌面），无条件 hoist 会把 fence 抬到屏幕上的普通窗口之上。`OnForegroundChanged` 桌面分支 / 5 秒定时器都按此门控；topmost 由后续切到普通窗口时的 `HWND_BOTTOM` 自动降级清除。（实测判据 `IsAnyFenceSunkBehindDesktop()` 由 bug 35 引入；在此之前 bug 10 只能用类名近似，故有无条件 hoist 的旧逻辑。）
6. **新建窗口路径不要用 `GetForegroundWindow()` 做分支** —— 托盘菜单刚关闭等"前台过渡态"瞬间，即使返回值是普通窗口，DWM 仍可能把 `HWND_BOTTOM` 推到壁纸下（bug 24）。用户主动新建路径统一走 `HWND_TOPMOST`，依靠后续 `OnDebouncedForegroundRecovery → HWND_BOTTOM` 隐式降级
7. **`SetAllBottom`（离开 topmost 回到底部）的即时 `SendToBottom` 可能因桌面/iconic 前台 no-op，必须额外安排一次"防抖后延迟重沉"**（bug 36/37）—— `SendToBottom` 带的 `IsDesktopOrTaskbarWindow || IsIconic` 守卫是为 bug 35 服务的，但 `SetAllBottom → SendToBottom` 这条即时路径在「恢复瞬间前台仍是桌面 / 还原中的 iconic 窗口」时会被它即时拦下、`_isTopmost` 已清但窗口物理仍停 `HWND_TOPMOST` → 卡顶层。**该延迟重沉已收敛进 `SetAllBottom()` 自身**（循环 `SendToBottom` 后统一 `BeginInvoke(StartForegroundDebounce)`），一次覆盖全部 4 条调用路径（Win+D restore 的 `_isTopmost`/`_pendingTopmost`、`ExitPeek`、`OnForegroundChanged` 真实窗口激活）。bug 37 的教训：**别在每个调用点手工重复同一段后置防护（迟早漏一条），对所有调用方都成立的兜底应收敛到方法公共出口**。延迟判定让 `IsIconic` 区分"正在还原（应沉）"vs"单纯闪烁（不沉）"
8. **被 `IsAnyFenceSunkBehindDesktop` 排除探测的 overlay，需有独立的一次性自愈入口**（bug 36）—— overlay 是 `AllowsTransparency` 层叠窗口（alpha=0 透传会误判，bug 19），不参与 `WindowFromPoint` 自愈；它"随 fence 一起被带回"的前提是 fence 也沉了。"fence 正常、overlay 单独沉"时无人捞回，需 `OnLoaded` 用 `EnsureOverlayVisible`（借用 topmost）+ `InvalidateVisual`（治层叠透明初次合成不绘制）两拍自愈

### 窗口样式限制
Fence窗口使用`WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE`样式，这种窗口有以下限制：
1. 不会显示在任务栏和Alt+Tab列表中
2. 最小化后无法通过常规方式找回
3. 在Windows 11中z-order行为特殊

因此在开发时需要注意：
1. 完全禁止窗口最小化
2. 谨慎处理z-order变化
3. 定期检查窗口可见性

### 模态对话框限制（bug 21 / 29）
本项目所有 fence/overlay 与对话框同属一个 UI 线程，WPF `ShowDialog()` 会在 Win32 层对同线程所有其他顶层窗口 `EnableWindow(FALSE)`：
1. **自定义 WPF 窗口一律 `Show()` 非模态 + 事件回调**，并用字段缓存实例防止重复打开
2. 非模态窗口上设置 `DialogResult` 会抛 `InvalidOperationException`，回调模式要彻底移除 DialogResult
3. 系统公共对话框（SaveFileDialog/OpenFolderDialog 等）保持模态是预期行为
4. 排查方法：`grep ShowDialog`，逐个确认是否系统对话框

### 数据持久化原则（bug 25）
1. **加载路径禁止 fire-and-forget**（`_ = LoadAsync()`）：必须 await + 显式失败分支
2. **损坏文件先备份（.corrupt-时间戳）再回退默认**，并弹窗告知——数据永远不被静默销毁
3. **"读失败"必须分两类**：`JsonException`（内容确实损坏）才备份+回退默认；`IOException/UnauthorizedAccessException`（文件被杀软/备份/同步工具临时锁定等瞬时态，文件可能完好）**一律上抛 → 禁止写盘**，绝不回退默认——否则锁释放后会用空状态覆盖好数据。判据：能确定"内容坏"才重置，"读不到"一律保守不写
4. **加载失败 → 禁止一切写盘**（`_loadFailed` 标志）：内存状态不完整时保存 = 用坏数据覆盖好数据
5. 最终保存必须发生在窗口销毁前（托盘退出 await / SessionEnding）；**OnExit 阶段 `_fenceWindows` 已被 Closed 清空，在那里"兜底保存"会写出空列表**
6. 同一文件的并发写必须串行化（store 内 SemaphoreSlim）

### 后台线程回调安全（bug 30）
1. FSW / `System.Timers.Timer` 回调在线程池线程，**未处理异常直接终止进程**，`DispatcherUnhandledException` 救不了
2. 回调中访问可被 Dispose 的对象（timer 等）必须 lock + `_disposed` 标志，`?.` 防不了 ObjectDisposedException
3. FileSystemWatcher 必须订阅 `Error` 事件（缓冲区溢出唯一通知），用全量扫描自愈

### 路径比较约定（bug 31）
Windows 路径比较必须统一 `OrdinalIgnoreCase`（集合用 `StringComparer.OrdinalIgnoreCase`），新增含路径集合的类时对照检查。

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
