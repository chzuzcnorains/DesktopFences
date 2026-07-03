# bug 41：图标 LRU 重复键累积致活跃缓存被提前淘汰（评审发现）

> 来源：整体代码评审主动发现的潜在问题（未现场复现），编号 F4。

## 问题描述

`ShellIconExtractor` 用 `ConcurrentDictionary _cache` + `LinkedList<string> _lruOrder` 维护
按扩展名/路径的图标缓存（LRU 上限 500）。`_lruOrder` 可能为同一个 key 累积多个节点，使
`_lruOrder.Count` 虚高，进而把仍在 `_cache` 中的活跃项提前淘汰（表现为图标被无谓地重新抽取）。

## 根因

`AddToLru` 无条件 `_lruOrder.AddFirst(key)`，不像 `TouchLru` 先 `Remove(key)` 再 `AddFirst`。
而 `GetIcon` 的"先查缓存、miss 再抽取写入"是 check-then-act、**非原子**：

- 两个线程同时 miss 同一 key（如 `GetIconAsync` 走 `Task.Run`，多 tile 并发加载同扩展名），
  各自走到 `_cache[key] = icon; AddToLru(key)` → `_lruOrder` 出现该 key 两个节点；
- 某 key 被淘汰后再次被请求加入，也会再插一个节点。

`_lruOrder.Count` 因重复节点大于实际不同 key 数，`while (_lruOrder.Count > _maxCacheSize)`
触发更早，`RemoveLast` 把某个 key 的"较旧那个节点"移除并 `_cache.TryRemove` —— 该 key 仍可能
是活跃项，于是被误淘汰。

## 修复

`AddToLru` 在 `AddFirst` 前先 `_lruOrder.Remove(key)`（与 `TouchLru` 对齐），保证同一 key 在
链表中至多一个节点：

```csharp
private void AddToLru(string key)
{
    lock (_lruLock)
    {
        _lruOrder.Remove(key);   // 去重：防止同 key 多节点
        _lruOrder.AddFirst(key);
        while (_lruOrder.Count > _maxCacheSize) { … }
    }
}
```

涉及文件：`src/DesktopFences.Shell/Desktop/ShellIconExtractor.cs`。

## 验证

- `dotnet build` 通过；`dotnet test` 全绿。
- 冒烟：大量不同类型文件的 fence 图标显示正常、滚动不丢图。

## 教训

LRU 链表的"加入"和"访问刷新"都必须保证 key 唯一（先 Remove 再 AddFirst）；
在 check-then-act 非原子的缓存写入路径上，重复键会破坏基于 `Count` 的淘汰判断。
