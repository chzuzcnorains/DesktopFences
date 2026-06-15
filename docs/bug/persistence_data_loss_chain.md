# 持久化数据丢失链：损坏 JSON / 退出竞态 / 并发写冲突

## 问题描述

系统性排查（2026-06-12）发现的数据丢失风险链，包含四个相互放大的缺陷：

1. **损坏 JSON 静默丢数据**：`JsonFileStore.ReadAsync` 无异常处理，`fences.json` 损坏时抛 `JsonException`；而 `App_OnStartup` 用 `_ = LoadFencesAsync()` fire-and-forget，异常被静默吞掉，应用以"零 fence"状态继续运行（托盘可用）。此时托盘"新建 Fence"等任何触发 `RequestAutoSave` 的操作会**用空列表覆盖 fences.json**——原有布局永久丢失，且用户没有任何提示。
2. **退出保存竞态**：托盘"退出"执行 `_ = SaveFencesAsync(); Shutdown();`，保存是 fire-and-forget，而 `SaveFencesAsync` 内部 `await Dispatcher.InvokeAsync(...)` 依赖正在关闭的 dispatcher——最后一次布局变更可能永远写不出去。
3. **并发写同一 .tmp**：auto-save 定时器（线程池线程）与直接调用（UI 线程）可并发执行 `SaveFencesAsync`，两个写入者同时 `File.Create("fences.json.tmp")` → `IOException` → 被 `catch { }` 吞掉 → 保存静默丢失。
4. **.tmp 残留**：`WriteAtomicAsync` 序列化中途异常时临时文件残留磁盘且无清理。

## 真因

- 持久化原语（JsonFileStore）只实现了"进程被杀时不损坏"的原子写，没有覆盖"读到损坏数据""并发写""异常清理"三类失败模式。
- App 层所有 Save/Load 的 `catch { }` 把 IO/序列化失败变成静默行为，错误既不可见也不可恢复。
- 关闭窗口波次中每个 `FenceHost.Closed` 都会 `_fenceWindows.Remove + RequestAutoSave`，若最后一个"空列表"的延迟保存在退出前触发，同样会清空布局。

## 修复

1. **`JsonLayoutStore.ReadResilientAsync`**：把损坏文件备份为 `{name}.corrupt-{时间戳}`，回退默认值，失败记录在 `LoadFailures` 列表；App 启动后 `NotifyLoadFailures()` 弹窗告知用户备份位置。**数据永远不会被静默销毁。**
   - **补强（2026-06-15）：区分"内容损坏"与"瞬时 IO 错误"。** 初版把 `JsonException/IOException/UnauthorizedAccessException` 一并捕获回退默认。问题：`fences.json` 被杀软/备份/同步工具**临时独占锁定**时抛 `IOException`，文件本身是好的，但会被当成"损坏"→ 回退空状态，而此路径不设 `_loadFailed`（异常被 store 吞掉）→ auto-save 仍开启 → 锁释放后**用空列表覆盖好数据**（虽有 `.corrupt-*` 备份但属误报）。修复：`ReadResilientAsync` **只捕获 `JsonException`**（内容确实坏）做备份+回退；`IOException/UnauthorizedAccessException` **故意上抛**，经 `App.LoadFencesAsync` 冒泡到启动 try-catch → 设 `_loadFailed` → 全程禁止写盘，磁盘上的好文件原样保留。
2. **`App_OnStartup` 改 `async void` + `await LoadFencesAsync()` 包 try-catch**：未知加载异常 → 设 `_loadFailed` 标志 → `SaveFencesAsync/SavePagesAsync/SaveRulesAsync` 全部拒绝写盘，且不启动 auto-save，防止用残缺状态覆盖磁盘。
3. **托盘退出改 `async` lambda**：`await SaveFencesAsync()` 完成后再 `Shutdown()`；另注册 `SessionEnding`（注销/关机时窗口仍存活）用 `Task.Run(...).GetAwaiter().GetResult()` 同步完成最终保存（Task.Run 避免 await 续体回投 dispatcher 死锁）。
4. **`RequestAutoSave` 在 `_isShuttingDown` 时拒绝**：防止关闭波次最后的空列表延迟保存。
5. **`JsonLayoutStore` 内置 `SemaphoreSlim(1,1)` 串行化所有写入**（`WriteLockedAsync`），并发保存不再竞争同一 .tmp。
6. **`WriteAtomicAsync` try/catch 清理 .tmp 后 rethrow**；App 层 `catch { }` 全部补 `Debug.WriteLine` 日志。

## 关键经验

- **fire-and-forget (`_ = XxxAsync()`) + `catch { }` 是数据丢失的标配组合**：加载路径必须 await + 显式失败分支；保存路径必须可观测（日志/提示）。
- **"加载失败 → 禁止保存"是底线**：内存状态不完整时写盘 = 把磁盘上的好数据换成坏数据。
- 损坏文件先备份再回退默认，比"拒绝启动"和"静默重置"都好——既能用又不丢数据。
- **"读失败"要分两类对待，否则回退默认反而成了数据丢失的帮凶**：`JsonException`（内容损坏）才该备份+回退默认；`IOException/UnauthorizedAccessException`（多是文件被临时锁定/无权限的瞬时态）应**上抛 → 禁止写盘**，绝不能回退默认——文件可能完好，回退空状态 + 自动保存就把好数据覆盖了。判据：能确定"内容坏"才重置，"读不到"一律保守不写。
- OnExit 阶段窗口已全部 Closed、`_fenceWindows` 已被清空，**不能**在 OnExit 做"兜底保存"（会写出空列表）；最终保存必须发生在窗口销毁之前（托盘退出 await / SessionEnding）。

## 验证

- 单元测试：损坏 fences.json 回退+备份、损坏 settings.json 回退、损坏后再保存不毁备份、20 路并发保存无异常且结果合法、序列化失败清理 .tmp 且原文件完好。
- **补强测试（2026-06-15）**：`LoadFences_TransientIoLock_Propagates_AndDoesNotResetOrBackUp` —— 用 `FileShare.None` 独占锁定 `fences.json`，断言 `LoadFencesAsync` 抛 `IOException`、**不**记录 `LoadFailures`、**不**生成 `.corrupt-*` 备份、原文件内容原样保留（91/91 通过）。
- 手动：故意写坏 `%APPDATA%\DesktopFences\fences.json` → 启动弹窗提示 + 生成 `.corrupt-*` 备份；新建 fence 后备份仍在。

| 项目 | 内容 |
|------|------|
| 修复日期 | 2026-06-12（瞬时 IO 区分补强 2026-06-15） |
| 涉及文件 | JsonFileStore.cs, JsonLayoutStore.cs, App.xaml.cs |
