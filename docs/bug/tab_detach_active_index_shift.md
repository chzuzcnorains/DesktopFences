# bug 42：撕离活跃标签之前的标签后活跃标签漂移（评审发现）

> 来源：整体代码评审主动发现的潜在问题（未现场复现）。

## 问题描述

多标签 Fence（tab group）中，把**活跃标签之前**的某个非活跃标签拖拽撕离（tear-off）后，
原 host 的活跃标签会错误地跳到下一个标签。例：标签 [A, B, C]、当前活跃 B，
把 A 垂直拖出标签条撕离 → 原 host 里被激活显示的变成 C，而不是用户正在看的 B。

## 根因

`FenceHost.RemoveTab(index)` 从 `_tabs` 移除条目后，只处理了"活跃索引越界"的钳位
（`_activeTabIndex >= _tabs.Count` → 取最后一个），**没有处理 `index < _activeTabIndex`
的情况**——删除前面的标签会让后面所有标签的索引整体前移一位，`_activeTabIndex`
不跟着递减就指向了原活跃标签的"下一个"。

该 bug 的可达路径是拖拽撕离：

- 标签按钮的 `PreviewMouseLeftButtonDown` 只记录 `_tabDragFromIndex`，**不激活**该标签
  （激活发生在 `Click`，而拖走时 Click 不触发）；
- 垂直越界撕离 → `TabDetachRequested?.Invoke(_tabs[from])` → `App.DetachTab` →
  `host.RemoveTab(idx)`，此处 `idx` 可以是任意（非活跃）索引。

菜单路径（「分离为独立 Fence」「关闭 Fence」）都固定传 `_activeTabIndex`，不触发此 bug。

## 修复

`RemoveTab` 在钳位之前补一条索引平移：

```csharp
_tabs.RemoveAt(index);

// 删除活跃标签之前的标签会让后续索引整体前移，递减保持同一标签活跃
if (index < _activeTabIndex)
    _activeTabIndex--;

if (_activeTabIndex >= _tabs.Count)
    _activeTabIndex = _tabs.Count - 1;
```

涉及文件：`src/DesktopFences.UI/Controls/FenceHost.xaml.cs`。

## 验证

- `dotnet build` 通过；`dotnet test` 全绿（UI 层无单测项目，逻辑靠手工走查）。
- 手工走查：三标签 [A,B,C] 激活 B → 撕离 A → 原 host 活跃仍是 B；
  撕离活跃标签本身 → 激活下一个（原行为）；关闭最后一个标签 → 无回归。

## 教训

按索引维护"当前选中项"的集合，任何 `RemoveAt(i)` 都必须考虑 `i` 与选中索引的
三种相对位置（之前/相等/之后），只处理越界钳位会漏掉"之前"这一档。
入口不止一个时（菜单只删活跃项、拖拽可删任意项），要以**最宽的调用契约**做防护。
