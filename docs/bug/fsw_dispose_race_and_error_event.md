# FileSystemWatcher 关闭竞态崩溃 + Error 事件未处理

## 问题描述

1. **退出时偶发进程崩溃**：应用退出（或 portal fence 切换路径）时，若恰有文件变更事件在途，`DesktopFileMonitor.OnFileEvent` / `FolderPortalWatcher.OnChanged` 会在防抖 timer 已被 `Stop()/Dispose()` 销毁后调用 `timer.Stop()/Start()` → `ObjectDisposedException` 抛在 FSW 的线程池线程上 → 未处理异常 = 进程崩溃（`DispatcherUnhandledException` 只覆盖 UI 线程，救不了线程池线程）。
2. **批量文件操作后监控静默失效**：FileSystemWatcher 内部缓冲区（默认 8KB）在桌面瞬间批量增删文件时溢出，溢出后 FSW 触发 `Error` 事件并**丢弃所有未处理事件**；两个 watcher 类都未订阅 `Error`，丢失的变更最长要等 30 秒兜底全量扫描才能补上。

## 真因

- FSW 事件回调运行在线程池线程，与 UI 线程的 `Stop()/Dispose()` 天然并发；`_debounceTimer?.Stop()` 的 `?.` 只防 null 不防"已 Dispose"——读到非 null 字段后对象可能已被并发销毁。
- `Error` 事件是 FSW 缓冲区溢出的唯一通知渠道，不订阅 = 静默丢事件。

## 修复

1. **timer 操作全部移入 `lock (_lock)` 并加 `_disposed` 检查**；`Stop()`/`Dispose()` 的销毁逻辑同锁。锁内一致性保证"检查-使用"原子化。
2. **两个类的所有 watcher 订阅 `Error` 事件**：`DesktopFileMonitor` 触发 `PerformFullScan()` 立即自愈，`FolderPortalWatcher` 触发 `RaiseContentsChanged()` 全量刷新。

## 关键经验

- **FSW / System.Timers.Timer 的回调在线程池线程**：与 Dispose 的竞态必须用锁 + disposed 标志，`?.` 空条件操作符不能防 ObjectDisposedException。
- 线程池线程上的未处理异常会直接终止进程，UI 层的全局异常兜底覆盖不到——后台组件必须自己保证不抛。
- 用 FSW 必须订阅 `Error`，并准备好全量扫描自愈路径（本项目两个类都已有现成的全量刷新方法，订阅即可）。

## 验证

构建 + 71 个单元测试通过；退出应用同时向桌面批量复制文件无崩溃；向桌面瞬间解压数百个文件后 fence 同步及时（不再等 30 秒）。

| 项目 | 内容 |
|------|------|
| 修复日期 | 2026-06-12 |
| 涉及文件 | DesktopFileMonitor.cs, FolderPortalWatcher.cs |
