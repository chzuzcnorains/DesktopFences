# 当前任务列表

## Phase 11 — DWM Acrylic 背景模糊（已完成）

- [x] `Shell/Interop/NativeMethods.cs` 新增 `SetWindowCompositionAttribute` P/Invoke + `AccentPolicy / AccentState / WindowCompositionAttributeData / WindowCompositionAttribute`
- [x] `Shell/Desktop/AcrylicCompositor.cs`（新）— 封装 `Enable(hwnd, gradientArgb)` / `Disable(hwnd)`,默认 `GradientColor=0x01000000` 让 WPF 控制色调
- [x] `UI/Controls/FenceHost.xaml.cs` — 新增 `_acrylicBlur` 字段 + `SetAcrylicBlur(int)` 公开方法,`OnLoaded` 中按设置惰性应用
- [x] `App.xaml.cs` — `SpawnFenceWindow` / `DetachTab` 创建时应用,`OnSettingsSaved` 遍历 `_fenceWindows` 实时刷新
- [x] 文档收尾：`phase-11.md` 已建,`complete.md` 加入 Phase 11,`todo.md` 划掉 Phase 8 DWM 模糊条目,`win32-embedding.md` 「关键 Win32 API」补充,`CLAUDE.md` 更新阶段与 API 列表
- [x] 编译验证 + 61 个单元测试全部通过

---
*此文件由 CLAUDE.md 结构拆分时创建*
*当有新的任务时,在此记录任务名称、负责人和截止时间*
