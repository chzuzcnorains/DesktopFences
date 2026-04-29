# 最近关闭 Fence 列表无法删除 + 关闭时间不准的 bug

## 问题描述
设置 → Fence 管理 → 最近关闭：
1. 每张卡片只有"恢复"按钮，没有"删除"按钮——FIFO 上限是 20，但用户主动想清理某条没有手段，列表只会越积越多直到顶到 20。
2. 卡片副标题里"X 分钟前"始终显示的是**打开设置面板的那一刻**，与真实关闭时间无关。即使一周前关掉的 fence 也会显示"刚刚"。

## 产生原因
1. **没有删除入口**：`FencesManageSettingsPane.BuildClosedCard` 只生成了 "恢复" 按钮（`RestoreClosedFenceRequested`），没有 "删除" 通道。
2. **关闭时间未持久化**：`AppSettings.RecentClosedFences` 是 `List<string>`，每条仅存 `JsonSerializer.Serialize(FenceDefinition)`——`FenceDefinition` 没有时间字段。`App.ParseRecentClosedFences` 在每次构造 `ClosedFenceRecord` 时直接 `ClosedAt: DateTimeOffset.Now`，结果就是渲染时间永远等于"现在"。

## 修复方案

### 1. 持久化关闭时间（向后兼容旧数据）
新增 App 内部 wrapper：

```csharp
private sealed class RecentClosedFenceEntry
{
    public FenceDefinition Definition { get; set; } = new();
    public DateTimeOffset ClosedAt { get; set; } = DateTimeOffset.Now;
}
```

`RecentClosedFences[i]` 现在序列化的是 wrapper（带 `Definition` 和 `ClosedAt`）。
统一反序列化 helper `DeserializeRecentClosedEntry(json)`：
- 用 `JsonDocument` 检测根级是否有 `"Definition"` 字段——有则按新 wrapper 解析
- 否则按旧的 bare-`FenceDefinition` 格式解析（首次升级兼容），`ClosedAt` 兜底为当前时间

`RecordRecentlyClosedFences` / `ParseRecentClosedFences` / `RestoreClosedFenceById` / `RefreshRecentClosedMenu` 全部走该 helper，避免重复处理逻辑。

### 2. 添加删除按钮 + 即时刷新
- `FencesManageSettingsPane`
  - 新增事件 `DeleteClosedFenceRequested`
  - `BuildClosedCard` 在"恢复"旁加"删除"按钮，写入同一个 `StackPanel`
  - 新增 `RemoveClosedFenceRecord(Guid)` 让外部按 id 就地移除一张卡片，并刷新计数 + 网格
- `SettingsWindow`
  - 转发 `DeleteClosedFenceRequested`
  - 暴露 `NotifyClosedFenceRemoved(Guid)` 调用上面的就地刷新方法
- `App.xaml.cs`
  - 新增 `DeleteClosedFenceById(Guid)`：从 `RecentClosedFences` 摘除对应条目 → 持久化 → 重建托盘"恢复最近关闭"子菜单
  - `ShowSettings` 同时订阅 `DeleteClosedFenceRequested` 和 `RestoreClosedFenceRequested`，每次都调 `settingsWindow.NotifyClosedFenceRemoved(id)` 让"卡片"立即消失，无需关闭重开设置

### 兼容性说明
- 旧设置文件（条目是 bare `FenceDefinition` JSON）依然可以读取，只是 `ClosedAt` 缺失会被填为打开时间——下次关闭新条目就会写入真实时间，自然过渡。
- 序列化新格式后再用旧版本读取也不会崩溃（旧逻辑直接 `Deserialize<FenceDefinition>` 一个 wrapper 会得到默认值 fence；可见 fallback 不优雅，但用户不应该升级后再降级，验收时不必为其设计路径）。

## 影响范围
- 设置 → Fence 管理 → 最近关闭：每张卡多一个"删除"按钮、时间显示真实
- 托盘菜单 → 恢复最近关闭：行为不变（不显示时间，标签仍是 `Title · 文件数`）
- `RecentClosedFences` JSON 结构变化（新增 wrapper），有向后兼容

## 经验总结
- 任何"展示给用户的时间"必须**和事件一起**写入持久化层，不要在渲染时取 `DateTimeOffset.Now`——这是个看似无害但每个项目都会犯一次的反模式。
- 给 FIFO 队列加上限的同时也要给用户**主动删除**入口；否则 20 条上限只会让最旧的悄悄掉队，而用户想清掉的中间条目永远没有出口。
