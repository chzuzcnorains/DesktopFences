# bug 40：保存设置时 settings 被 UI 线程并发修改致序列化竞态（评审发现）

> 来源：整体代码评审主动发现的潜在问题（未现场复现），编号 F2。

## 问题描述

`App` 多处以 fire-and-forget 方式保存设置：`_ = _layoutStore!.SaveSettingsAsync(_appSettings)`
（如 `RecordRecentlyClosedFences` / `RestoreClosedFenceById` / `ClearRecentClosedFences` /
`OnSettingsSaved` 等）。这些调用把**活的** `_appSettings` 对象直接交给后台序列化。

## 根因

`SaveSettingsAsync → WriteLockedAsync → JsonSerializer.SerializeAsync(stream, settings, …)`
在**线程池线程**上枚举 `settings.RecentClosedFences`（`List<string>`）。与此同时 UI 线程可能在
`RecordRecentlyClosedFences`（`Insert(0, …)`）/ `RestoreClosedFenceById`、`DeleteClosedFenceById`
（`RemoveAt(…)`）里修改**同一个** list——典型触发是"连续关闭多个 fence 的关闭波次"。

`JsonLayoutStore` 内的 `SemaphoreSlim _writeLock` 只串行化**写操作**，挡不住 UI 线程对 list 的
并发修改。`List<T>` 边读（后台枚举）边写（UI 改）→ 抛 `InvalidOperationException`。因为调用是
fire-and-forget，异常落到未观察的 `Task` 被吞掉 → **该次设置保存静默失败**（下次保存能补回，
故非数据丢失，但属真实隐患）。

## 修复

在交给后台序列化**之前**，于调用方（UI）线程同步快照可变集合：

- `AppSettings.CloneForPersist()`：`MemberwiseClone` 复制标量字段 + 新建 `RecentClosedFences`
  list 副本。
- `JsonLayoutStore.SaveSettingsAsync` 改为 `WriteLockedAsync(SettingsPath, settings.CloneForPersist())`。
  `CloneForPersist()` 在 `WriteLockedAsync` 的首个 `await` 之前同步求值（UI 线程单线程，与 list
  的修改不会交错），后台只读私有副本 → 消除边读边写竞态。

涉及文件：
- `src/DesktopFences.Core/Models/AppSettings.cs`（新增 `CloneForPersist`）
- `src/DesktopFences.Core/Services/JsonLayoutStore.cs`（`SaveSettingsAsync` 改用快照）

## 验证

- 新增单测 `AppSettingsTests.CloneForPersist_SnapshotsMutableCollection_IndependentOfOriginal`、
  `SaveAndLoadSettings_PreservesRecentClosedFences`。
- `dotnet test tests/DesktopFences.Core.Tests` 全绿（93 通过）。
- 冒烟：连续关闭多个 fence 后托盘"恢复最近关闭"列表正确、`settings.json` 正常写出。

## 教训

fire-and-forget 把**可被 UI 线程修改的可变状态**交给后台异步序列化时，必须先在拥有该状态的
线程上快照。`store` 层的写锁只保证"写不并发"，不保证"被序列化对象在序列化期间不被改"。
