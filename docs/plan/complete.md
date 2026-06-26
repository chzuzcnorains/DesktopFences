# 已完成 Phase

以下 Phase 全部完成：

- [Phase 0: 基础骨架](phase-0.md) ✅
- [Phase 1: 核心交互](phase-1.md) ✅
- [Phase 2: 文件管理](phase-2.md) ✅
- [Phase 3: 自动化](phase-3.md) ✅
- [Phase 4: 高级功能](phase-4.md) ✅
- [Phase 5: 布局管理](phase-5.md) ✅
- [Phase 6: 精细打磨](phase-6.md) ✅
- [Phase 7: Tab 标签组](phase-7.md) ✅
- [Phase 8: Bug 修复与功能增强](phase-8.md) ✅
- [Phase 9a: 应用程序图标](phase-9a.md) ✅
- [Phase 9b: DarkTheme 深化](phase-9b.md) ✅
- [Phase 9c: 图标系统](phase-9c.md) ✅
- [Phase 10: 视觉系统升级](phase-10.md) ✅
- [Phase 11: DWM Acrylic 背景模糊](phase-11.md) ✅
- [Phase 12: iconStyle 双卡片选择器 + System 图标资源](phase-12.md) ✅
- [Phase 13: 按 Fence 覆盖 IconStyle](phase-13.md) ✅
- [Phase 11 Polish: blur API 二值化 + 失败降级 + helper 抽取](phase-11-polish.md) ✅
- [Phase 12 Polish: Shell 抽图改用 IShellItemImageFactory + Shell 风格解禁到 UI](phase-12-polish.md) ✅
- [Phase 13 Polish: Tab 拖拽排序 + Portal 规则隔离修复](phase-13-polish.md) ✅
- [Phase 14: Fence 呈现方式切换（图标/列表/详细）+ 排序（字段 + 手动拖拽）](phase-14.md) ✅（代码完成，待手动验收）
- 桌面框选（Rubber-band 多选）：低侵入钩子 `DesktopMarqueeManager` + 覆盖层自绘选框/多选/组拖入 fence/Delete 批删/Ctrl-Shift 增量，设计见 [desktop-icon-overlay.md §11](../design/desktop-icon-overlay.md) ✅（代码完成，待手动验收）
- Fence 内框选 + 多选：标准 WPF 鼠标捕获 + `MarqueeLayer`，复用 `FileItemViewModel.IsSelected`，Ctrl/Shift 手势 + 整组拖拽 + 全局互斥单一选区（`DesktopSelectionCoordinator`），设计见 [fence-container.md §2.5](../design/fence-container.md) ✅（代码完成，待手动验收）
