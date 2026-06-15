# 当前执行计划

> Phase 14（Fence 呈现方式切换 + 排序）代码实现全部完成，等待用户手动验收。

## Phase 14: Fence 呈现方式切换 + 排序

为 Fence 面板添加类似 Explorer 的呈现方式切换（图标/列表/详细）与排序（字段排序 + 手动拖拽）能力，每个 fence 独立设置并持久化。详见 [phase-14.md](phase-14.md)。

- **14a 排序基础** — ✅ 已完成（FileSorter + 元数据 + ResortAfterChange + 排序菜单）
- **14b 视图模式** — ✅ 已完成（List/Detail 模板 + 面板切换 + 列头 + 呈现方式菜单）
- **14c 手动拖拽重排** — ✅ 已完成（SourceFenceId + OnDrop 重排分支 + 命中测试）

**状态**：构建通过（仅余既有无关警告），90 个单元测试通过（原 71 + 19 新），启动烟雾验证无 XAML 运行时异常。**待用户手动走查**三视图切换 / 列头排序 / 拖拽重排（见 [phase-14.md](phase-14.md) 验收清单）。

---
*Phase 0–13 + Phase 11 / 12 / 13 Polish 已全部完成，详见 [complete.md](complete.md)。*
