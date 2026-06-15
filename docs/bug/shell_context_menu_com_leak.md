# ShellContextMenu COM 对象未释放（每次右键泄漏引用）

## 问题描述

每次右键 fence 内文件弹出 Shell 原生菜单，`ShellContextMenu.Show` 都会泄漏两个 COM 引用（`IShellFolder`、`IContextMenu`），失败提前 return 的路径同样泄漏。长时间运行 + 频繁右键会累积大量未释放的 RCW，依赖 GC finalizer 延迟回收。

## 真因

`SHBindToObject` 返回的 `folderObj` 和 `GetUIObjectOf` 返回的 `ctxObj` 都是引用计数的 COM 对象，但方法中只有 PIDL 的 `FreeCoTaskMem`，从未调用 `Marshal.ReleaseComObject`。.NET RCW 最终会被 GC finalize 释放，但确定性释放缺失意味着 Shell 扩展（第三方右键菜单处理器）对象的生命周期完全不可控。

## 修复

`folderObj` / `ctxObj` 各包一层 try/finally `Marshal.ReleaseComObject`，与既有的 PIDL / HMENU 释放层级嵌套，保证所有路径（含失败提前 return）都确定性释放。

## 关键经验

P/Invoke 出来的 COM 接口对象（`[MarshalAs(UnmanagedType.Interface)] out object`）必须像 PIDL/GDI 句柄一样用 try/finally 确定性释放——"GC 最终会回收"不等于没有泄漏，第三方 Shell 扩展可能在 Release 时才释放自己的资源。

## 验证

右键 fence 文件反复弹出/关闭菜单，功能正常；代码审查确认所有 return 路径都被 finally 覆盖。

| 项目 | 内容 |
|------|------|
| 修复日期 | 2026-06-12 |
| 涉及文件 | ShellContextMenu.cs |
