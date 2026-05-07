# Phase 11 Polish：4 个收尾任务

**目标**：Phase 11 (DWM Acrylic) 落地后 review 发现 4 个二阶问题（不影响主路径但都是"债"），一并清掉。

## 11.P.1 背景

Phase 11 的 hotfix（commit `108fcf0`）解决了 luminosity tint 污染 + 圆角丢失两个 bug，但留下：

1. `FenceBlurRadius:int` 滑块（0..60）但底层 DWM API 是二值开关，拖动无视觉差异 → UX 歧义
2. `SetWindowCompositionAttribute` 返回值被丢弃，私有 API 失败时静默 → 不可观测的故障模式
3. `App.xaml.cs` 三处 host 初始化重复 `host.SyncTabStripBackground / SetTabStyle / SetAcrylicBlur` 三连调用
4. `DesktopEmbedManager` 三处 hoist 重复 `foreach _managedWindows → SetWindowPos(HWND_TOPMOST, ...)`（commit `f2877ee` 修的就是"三处自愈分支不一致"，证明这是高频热点）

## 11.P.2 关键决策

### 11.P.2.1 任务 A：`FenceBlurRadius:int` → `FenceBlurEnabled:bool`

- AppSettings 新增 `FenceBlurEnabled:bool`（default `true`，对齐旧默认值 26 > 0 的语义），删除 `FenceBlurRadius`
- 实现 `IJsonOnDeserialized` 自动迁移老 JSON：旧字段 `FenceBlurRadiusLegacy:int?` 通过 `[JsonPropertyName("FenceBlurRadius")]` 反序列化，OnDeserialized 中把 `legacy > 0` 转为 `FenceBlurEnabled`，再清空 legacy 字段（`JsonIgnoreCondition.WhenWritingDefault` 确保下次序列化不写出）
- AppearanceSettingsPane 模糊强度 Slider + ValueLabel 替换为 CheckBox "启用背景模糊"
- FenceHost.SetAcrylicBlur(int) 改为 SetAcrylicBlur(bool)；内部 `_acrylicBlur:int` → `_acrylicBlurEnabled:bool`
- ApplyFenceShadow 的 DropShadow 半径在 blur 启用时沿用旧默认值 26（视觉延续性），禁用时 0

### 11.P.2.2 任务 B：API 失败可观测 + 圆角降级

- `AcrylicCompositor.Enable / Disable / ApplyAccent` 返回 `bool`；ApplyAccent 捕获 `SetWindowCompositionAttribute` 的 int 返回值，非零即成功
- 失败时 `System.Diagnostics.Debug.WriteLine` 输出 `[AcrylicCompositor] ... failed`，包含 state 和 hwnd（沿用项目既有 Debug.WriteLine 习惯，不引入 ILogger 依赖）
- `FenceHost` 新增 `_acrylicBlurApplied:bool` 字段（区分用户意图 `_acrylicBlurEnabled` 与真实状态）
- `ApplyWindowRoundedRegion` 仅在 `_acrylicBlurApplied=true` 时调 `ApplyRoundedRegion`，否则调 `ClearRegion` 保持矩形窗口 — WPF 透明角已让 fence 视觉上是圆角，无须 SetWindowRgn；避免"裁了圆角但角落看不到 blur"的视觉怪象

### 11.P.2.3 任务 C：`App.ApplyHostStyle(host, settings)` helper

- 抽 `private static void ApplyHostStyle(FenceHost host, AppSettings settings)`：内部调 `host.SyncTabStripBackground / SetTabStyle(settings.TabStyle) / SetAcrylicBlur(settings.FenceBlurEnabled)`
- `SpawnFenceWindow` / `DetachTab` / `OnSettingsSaved` 三处统一调用 helper
- `OnSettingsSaved` 特有的 `RefreshFileTileTemplate / SnapThreshold` 留在循环里不放进 helper（仅设置变更场景需要）

### 11.P.2.4 任务 D：`DesktopEmbedManager.HoistAboveDesktop` helpers

- `private static void HoistSingleAboveDesktop(IntPtr hwnd)` — 单窗口（无 IsVisible 守卫，调用方决定）
- `private void HoistAllAboveDesktop()` — 遍历 `_managedWindows`，每个先 IsVisible 守卫再调 single
- 三处替换：`StartZOrderRecoveryTimer` 桌面分支 → `HoistAllAboveDesktop()`；`OnForegroundChanged` 桌面分支 → `HoistAllAboveDesktop()`；`EnsureVisibleAboveDesktop` 单窗口 SetWindowPos → `HoistSingleAboveDesktop(hwnd)`
- 共享的 SWP flags：`SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE | SWP_SHOWWINDOW`

## 11.P.3 实施顺序

3 个独立 commit：

1. **commit `f7deb32`** — `feat(blur): 启用模糊改为 bool 开关，消除滑块语义歧义`（任务 A）
2. **commit `daa318b`** — `refactor: 抽取 ApplyHostStyle / HoistAboveDesktop helper`（任务 C+D）
3. **commit `7208c29`** — `fix(blur): SetWindowCompositionAttribute 失败可观测 + 圆角降级`（任务 B）

任务 A 优先：`FenceHost.SetAcrylicBlur` 签名变化是其它任务的前提。任务 B 最后：返回值/字段/日志改动只发生在已经简化过的代码上。

## 11.P.4 验证

- `dotnet build -c Release`：0 错 0 警
- `dotnet test`：66 个单测全部通过（原 63 + 任务 A 新增 3 个 JSON 迁移测试）
  - `AppSettings_LegacyFenceBlurRadius_NonZero_MigratesToBlurEnabled`
  - `AppSettings_LegacyFenceBlurRadius_Zero_MigratesToBlurDisabled`
  - `AppSettings_RoundTrip_DoesNotEmitLegacyFenceBlurRadius`
- 手动场景（Windows 真机）：
  1. 旧 `settings.json` 含 `"FenceBlurRadius": 26` → 启动后 `FenceBlurEnabled=true`，Save 后文件不再有 `FenceBlurRadius` 字段
  2. 设置面板「外观」看到 CheckBox 而非滑块
  3. blur 启用时 fence 圆角正常；禁用时窗口仍以 WPF 透明角呈现圆角
  4. Spawn 新 fence / Detach tab / Settings Saved 三个路径外观一致
  5. 截图后关闭截图工具 → fence 不消失（HoistAllAboveDesktop 路径）

## 11.P.5 不做的事

- 不引入 `ILogger` / Serilog（项目无日志框架，此修复非引入框架时机）
- 不加 Windows 版本检测（`ACCENT_ENABLE_BLURBEHIND` 自 Win10 RTM 就支持）
- 不重新引入 `DropShadowEffect` 作为 blur fallback（Phase 11 已淘汰该路径）
- 不合并 `BringNewWindowToFront` / `SendToBottom` 进新 helper 体系（分支语义不同）
