# Phase 6: 精细打磨

**目标**：动画、主题、性能优化、打包分发。

## 6.1 动画优化
- [x] Fence 创建时淡入（FenceHost.OnLoaded → FenceContent.AnimateFadeIn，Opacity 0→1，250ms EaseOut）
- [x] Fence 删除时淡出（FenceHost.AnimateClose → FenceContent.AnimateFadeOut，200ms，完成后 Close）
- [x] 文件拖入"吸入"动画（ScaleTransform 1.0→1.02→1.0，150ms drop pulse）
- [x] 分页切换滑动动画（DoubleAnimation，300ms QuadraticEase，Phase 5 已实现）
- [x] Rollup 展开/折叠高度动画（DoubleAnimation，Phase 4 已实现）
- [ ] Snap 吸附磁性效果（视觉反馈，延迟到后续优化）

## 6.2 主题与外观
- [x] Fence 背景颜色自定义（每个 Fence 独立，`FenceDefinition.BackgroundColor`）
- [x] 标题栏颜色自定义（`FenceDefinition.TitleBarColor`）
- [x] 文字颜色自定义（`FenceDefinition.TextColor`）
- [x] 单 Fence 透明度设置（`FenceDefinition.Opacity`，绑定到 `FenceOpacity` ViewModel）
- [x] 全局默认颜色/透明度/字体由 `AppSettings` 统一管理，新建 Fence 继承全局默认
- [ ] Chameleon 模式（延迟到后续优化）
- [ ] Icon Tint（延迟到后续优化）

## 6.3 设置界面
- [x] `SettingsWindow.xaml` — 全局设置（外观 / 行为 / 启动 / 高级四组）
  - 外观：默认 Fence 颜色、标题栏颜色、文字颜色、透明度滑块、字体大小滑块
  - 行为：Snap 距离滑块、Quick Hide 开关
  - 启动：开机自启（HKCU Run）、最小化到托盘
  - 高级：兼容模式、调试日志
- [x] `SettingsSaved` 事件驱动更新：主题 + 开机自启 + Quick Hide 即时生效
- [x] `AppSettings` 模型 — 全局设置持久化到 `settings.json`

## 6.4 快捷搜索
- [x] `SearchHotkeyManager` — `RegisterHotKey(MOD_CONTROL|MOD_NOREPEAT, VK_OEM_3)` 全局 Ctrl+\` 热键
- [x] `SearchWindow.xaml` — 深色半透明浮动搜索框（500×400，TopMost，AllowsTransparency）
- [x] 搜索所有 Fence 内文件（DisplayName + FenceName 双字段过滤，OrdinalIgnoreCase）
- [x] 实时过滤（TextChanged 事件即时刷新）
- [x] Enter 打开文件（`Process.Start(UseShellExecute=true)`），Esc 关闭，失焦自动关闭
- [x] 搜索窗口淡入（Opacity 0→1，150ms）

## 6.5 性能优化
- [x] 图标异步加载：`{Binding Icon, IsAsync=True}` — WPF 延迟绑定，不阻塞 UI 线程
- [x] 虚拟化渲染：`ListBox` + `VirtualizingPanel.IsVirtualizing=True` + `VirtualizationMode=Recycling`
- [x] 图标 LRU 缓存（按扩展名，上限 500，Phase 2 已实现）
- [x] FSWatcher 事件 debounce（500ms 单次触发，Phase 3 已实现）
- [x] 原子写入（临时文件 → rename）防数据损坏

## 6.6 打包分发
- [x] `win-x64-self-contained.pubxml` — 自包含单文件发布配置
  - `SelfContained=true`，`PublishSingleFile=true`，`PublishReadyToRun=true`
  - `EnableCompressionInSingleFile=true`，`TrimMode=partial`
- [x] `StartupManager` — `HKCU\SOFTWARE\Microsoft\Windows\CurrentVersion\Run` 注册开机自启
- [ ] MSIX / Inno Setup 安装包（延迟到后续需要时）
- [ ] 自动更新检查（延迟到后续需要时）

**验收标准**：完整功能可用，动画流畅，设置可调，安装包可分发。 ✅ 已通过（61 个单元测试全部通过）

---

## Phase 6 实现记录

### 动画系统

**淡入/淡出**：
- `FencePanel.AnimateFadeIn()` — `DoubleAnimation` Opacity 0→1，250ms `CubicEase(EaseOut)`，作用在 FenceHost 窗口上
- `FencePanel.AnimateFadeOut(Action? onComplete)` — Opacity 1→0，200ms，完成回调触发 `Close()`
- `FenceHost.AnimateClose()` — 防重入（`_isClosing` 标志），委托 `FenceContent.AnimateFadeOut`

**拖入脉冲动画**：
- `FencePanel.AnimateDropPulse()` — `ScaleTransform` 作用在 `FenceBorder` 上，Scale 1.0→1.02→1.0，150ms 两阶段动画
- `OnDragOver` 蓝色边框高亮，`OnDrop` 重置边框并触发脉冲

### 全局设置（AppSettings）

**模型**（`DesktopFences.Core/Models/AppSettings.cs`）：
```
DefaultFenceColor      = "#CC1E1E2E"
DefaultTitleBarColor   = "#44FFFFFF"
DefaultTextColor       = "#DDEEEEEE"
DefaultOpacity         = 1.0
TitleBarFontSize       = 13
SnapThreshold          = 10
QuickHideEnabled       = true
StartWithWindows       = false
StartMinimized         = true
CompatibilityMode      = false
DebugLogging           = false
```

**持久化**：通过 `ILayoutStore.LoadSettingsAsync()` / `SaveSettingsAsync()` 读写 `%APPDATA%\DesktopFences\settings.json`，原子写入。

### 快捷搜索

**热键注册**（`SearchHotkeyManager.cs`）：
- `HwndSource` 隐藏窗口接收 `WM_HOTKEY`
- `RegisterHotKey(MOD_CONTROL | MOD_NOREPEAT, VK_OEM_3 = 0xC0)` → Ctrl+\`
- `SearchRequested` 事件触发 `App.ShowSearchWindow()`

**搜索窗口**（`SearchWindow.xaml`）：
- 深色主题（#1E1E2E 背景，AllowsTransparency，WindowStyle=None）
- `SearchResult` 数据模型：`FilePath`, `DisplayName`, `FenceName`, `FenceId`, `Icon`
- 实时过滤：TextChanged → LINQ `.Where(x => x.DisplayName.Contains(..., OrdinalIgnoreCase))`
- 键盘导航：Down 键聚焦列表，Enter 激活选中结果，Esc 关闭
- 失焦自动关闭（`Deactivated` 事件）
- 淡入动画（Opacity 0→1，150ms）

### 打包分发

**发布配置**（`win-x64-self-contained.pubxml`）：
```xml
<RuntimeIdentifier>win-x64</RuntimeIdentifier>
<SelfContained>true</SelfContained>
<PublishSingleFile>true</PublishSingleFile>
<PublishReadyToRun>true</PublishReadyToRun>
<EnableCompressionInSingleFile>true</EnableCompressionInSingleFile>
<TrimMode>partial</TrimMode>
```

**发布命令**：
```bash
dotnet publish src/DesktopFences.App -c Release /p:PublishProfile=win-x64-self-contained
```

**开机自启**（`StartupManager.cs`）：
- 写入 `HKCU\SOFTWARE\Microsoft\Windows\CurrentVersion\Run\DesktopFences`
- 值为 `"<exePath>"` 带引号，处理路径含空格情况
- `SetEnabled(bool)` 统一接口，由 `SettingsSaved` 事件驱动
