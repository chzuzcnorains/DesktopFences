# 当前执行计划

## Phase 11 — DWM Acrylic 背景模糊（2026-05-06，已完成）

**目标**：让 `AppSettings.FenceBlurRadius` 真正驱动 Fence 窗口的 DWM Acrylic 毛玻璃,对齐 `desktop-v2.html` 原型的 `--blur` 视觉语言。

**已完成**：
- P/Invoke：`SetWindowCompositionAttribute` + `AccentState/AccentPolicy/WindowCompositionAttributeData/WindowCompositionAttribute`（`Shell/Interop/NativeMethods.cs`）
- 封装：`Shell/Desktop/AcrylicCompositor.cs` — `Enable(hwnd, gradientArgb=0x01000000)` / `Disable(hwnd)`,色调由 WPF 的 `FenceBackgroundBrush` 控制
- `FenceHost` 接入：`_acrylicBlur` 字段 + `SetAcrylicBlur(int)` 公开方法,`OnLoaded` 中惰性应用
- App 层：`SpawnFenceWindow` / `DetachTab` 创建时应用,`OnSettingsSaved` 实时刷新
- 文档：`docs/design/acrylic-blur.md`、`docs/plan/phase-11.md`、`docs/design/win32-embedding.md`「关键 Win32 API」章节、`CLAUDE.md`
- 验证：`dotnet build` 0 错 0 警；61 个单元测试全部通过

**待用户运行时验证**：多显示器 / DPI 表现 + Win11 22H2+ 色调污染观察。如出现色调污染,可调 `AcrylicCompositor.Enable` 的默认 `gradientArgb` 至 `0x00000000`。

---
*此文件由 CLAUDE.md 结构拆分时创建*
*当有新的计划开始时,在此记录计划名称、目标和时间*
