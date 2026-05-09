# 未归档 cell 空白区域单击无法选中修复

## 问题描述

`DesktopIconOverlay` 中的"未归档"图标 cell：

- 单击 icon 不透明像素 → 正常选中
- 单击文字字形 → 正常选中
- **单击 cell 内 icon 与文字之外的空白区域 → 无任何反应，cell 不被选中**

期望：单击 cell 86×90 范围内任意位置都能选中（背景出现高亮）。

## 真正根因（前两次修复白做的原因）

> 关键点：`AllowsTransparency=True` 的 WPF 窗口本质上是 Windows 的 **layered window**。
> Windows 在 OS 层就根据**每像素 alpha** 决定 WM_LBUTTONDOWN 送给哪个窗口：
> **alpha = 0 的像素直接被判为 click-through，根本不会送进 WPF 进程**。
> 这一步发生在 WPF 的命中测试之前，任何 WPF 层面的改动（`IsHitTestVisible`、加 Background 给 Grid 等）都拦不住。

cell 视觉树：

```
DesktopIconOverlay (Window, AllowsTransparency=True, Background="Transparent")
└─ IconCanvas (Background={x:Null})           ← 不绘制像素
   └─ Border (Background=Brushes.Transparent) ← 绘制 alpha=0 像素
      └─ Grid → Image / TextBlock
```

`Brushes.Transparent` 实际颜色是 `Color.FromArgb(0, 255, 255, 255)`：**alpha = 0**。
WPF 把 cell 的整个 86×90 矩形按 alpha=0 写入 layered window 的位图。OS 看到这块都是 alpha=0 的像素 → 整张 cell 的空白区域全被 OS 透传到桌面壁纸下面。WPF 永远收不到这些位置的 click。

只有 `Image` 的不透明像素和 `TextBlock` 的字形（这两个有非零 alpha 的渲染）才会被 OS 送进 WPF —— 这正是用户观察到的"只能点 icon/文字、空白点不中"。

为对比：`FenceHost` 内层 `FenceBorder` 的 `Background="#CC1E1E2E"`（alpha=0xCC=204），整张 fence 都是非零 alpha 的像素，所以从来不存在这个问题。

## 修复方案

**用 alpha=1 的 near-transparent 画刷代替 `Brushes.Transparent`**。
1/255 alpha 视觉上完全不可感知（人眼分不出 alpha=0 和 alpha=1），但 OS 会把这些像素归给本窗口、把 click 送到 WPF。WPF 的命中测试再正常按 visual tree 路由，事件冒泡到 Border 的 `OnIconMouseDown` —— cell 选中。

### 关键代码（[DesktopIconOverlay.xaml.cs](../../src/DesktopFences.UI/Controls/DesktopIconOverlay.xaml.cs)）

```csharp
// AllowsTransparency=True 的层叠窗口下，Windows OS 用每像素 alpha 决定 click 走向：
// alpha=0 的像素直接被判为 click-through，根本不会送到 WPF 的命中测试。
// Brushes.Transparent 的 alpha 就是 0，所以"透明背景但可点"的 WPF 经典写法
// 在 AllowsTransparency 窗口里失效。用 alpha=1 的画刷（视觉不可感知）做兜底。
private static readonly SolidColorBrush ClickableTransparentBrush =
    new(Color.FromArgb(1, 0, 0, 0));

static DesktopIconOverlay()
{
    ClickableTransparentBrush.Freeze();
}

// CreateIconElement 中：
var border = new Border
{
    Width = CellWidth,
    Height = CellHeight,
    CornerRadius = new CornerRadius(4),
    Background = ClickableTransparentBrush,   // ← 不能用 Brushes.Transparent
    // …
};

// ClearSelection 中也必须用同一画刷，否则取消选中后又变回 alpha=0：
private void ClearSelection()
{
    foreach (var element in _iconElements.Values)
    {
        element.Background = ClickableTransparentBrush;
    }
}
```

`Image` / `TextBlock` 没有任何改动；`Grid` 也没有加 Background。整个修复就在 cell 的 Border `Background` 上，加 cleanup 时同步用同一画刷。

## 排查弯路 (前两次失败的修复)

- **第一次**：怀疑 `Grid.Background = null` 让命中穿透不可靠 → 给 Grid 加 `Brushes.Transparent`。无效，因为 OS 层 click 根本没进 WPF。
- **第二次**：怀疑 `Image` / `TextBlock` 在子级吸走 click 不冒泡 → 给它们加 `IsHitTestVisible = false`。同样无效，原因同上。

教训：在 `AllowsTransparency=True` 窗口里，"WPF 经典的可点透明背景 = `Brushes.Transparent`"是错误经验。**每像素 alpha 由 OS 决定**，必须 ≥ 1 才能保证可点。

## 修复关键点

1. **`AllowsTransparency=True` ⇒ layered window，OS 按每像素 alpha 决定 click 走向**：alpha=0 像素直接 click-through，WPF 的任何命中测试改动都没用。
2. **`Brushes.Transparent` 的 alpha 是 0**：在普通窗口里它确实可点（因为不走 layered window 的 per-pixel 判定），但在 `AllowsTransparency=True` 的窗口里失效。
3. **alpha=1 是视觉不可感知 + OS 可命中的最小值**：`Color.FromArgb(1, 0, 0, 0)` 是这个场景里最干净的"透明可点"写法。
4. **同一画刷需要在所有 cleanup 路径里复用**：`ClearSelection` 不能再设回 `Brushes.Transparent`，否则反选后 cell 又变成 click-through。

## 修复效果

- ✅ cell 内任意位置（icon、文字、空白角落、icon 周围、文字两侧/下方）单击都能选中
- ✅ 取消选中后再次点击同一 cell 仍能选中（ClearSelection 用同一 alpha=1 画刷）
- ✅ Canvas 空白区域（cell 之外的桌面区域）click-through 行为保持不变 —— 那里仍然是 `IconCanvas.Background={x:Null}`，OS 视为不绘制
- ✅ 拖动、右键菜单、双击打开等交互不受影响（命中链没变，只是改了 Border 背景画刷）
- ✅ 视觉无任何可见变化（人眼分不出 alpha=0 和 alpha=1）

## 相关文件

- [src/DesktopFences.UI/Controls/DesktopIconOverlay.xaml.cs](../../src/DesktopFences.UI/Controls/DesktopIconOverlay.xaml.cs)

## 同类提醒

项目里其它 `AllowsTransparency=True` 的窗口（`FenceHost`、`SnapGuideOverlay` 等）若以后想做"透明可点"的局部区域，**不要用 `Brushes.Transparent`**，统一参考此处的 `ClickableTransparentBrush` 写法。`FenceHost` 当前没踩坑是因为内层 `FenceBorder` 用了 `#CC1E1E2E`（alpha=204）—— 整张 fence 都是非零 alpha 像素。
