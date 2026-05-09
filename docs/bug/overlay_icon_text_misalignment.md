# Cell 内 icon 与文字水平/垂直中心不一致修复（Overlay + FencePanel）

## 问题描述
两处图标渲染路径都存在同样的对齐错位：

1. **`DesktopIconOverlay`**（未归档桌面图标）— cell 内 icon 与文字位置随文件名长短漂移
2. **`FencePanel` 的三个 file tile DataTemplate**（`CustomFileTile` / `SystemFileTile` / `ShellFileTile`）— Fence 内文件 tile 同样漂移

具体症状：

- 同一行（同一 row）多个 cell 的 icon 水平中心对不齐
- 同一列（同一 col）多个 cell 的 icon 垂直中心对不齐
- 文字（TextBlock）的水平中心同样不一致
- 文字 wrap 一行 vs 两行时，整张 cell 内的 icon 会跟着上下挪位

## 问题根源

两处布局虽然容器不同（一个 StackPanel、一个 Grid），但**共同点**都是让中间容器的尺寸由内容反推：

### Overlay 原布局
`Border(86×90) → StackPanel(垂直, HorizontalAlignment=Center, VerticalAlignment=Center, Margin=4) → Image(48×48) + TextBlock`

StackPanel 的尺寸由子项的期望尺寸推导：

1. **StackPanel 宽度 = 子项中最宽者的期望宽度**：TextBlock `TextWrapping=Wrap` 没有显式宽度，measure 时的可用宽度由父级 layout slot 决定，不同 cell 的 StackPanel 宽度随文字长度浮动。叠加 `Margin(4)` + 缺失 `UseLayoutRounding/SnapsToDevicePixels`，亚像素偏移导致视觉错位。
2. **StackPanel 高度随文字行数变化**：1 行 vs 2 行下 StackPanel 总高度不同，`VerticalAlignment=Center` 让整块在 Border 内上下位移 → icon 的垂直中心相对 cell 不固定。
3. **icon 与文字同处一个 stack**：文字尺寸的任何变动都会同时影响 icon 在 cell 中的位置，无法独立约束。

### FencePanel 原布局
`Border(FileTileWidth × FileTileHeight) → Grid HorizontalAlignment=Center VerticalAlignment=Center Margin=4 → Row 0 Auto(icon) + Row 1 *(text)`

虽然有 Grid + RowDefinitions，但**外 Grid 自身被设了 `HorizontalAlignment=Center / VerticalAlignment=Center`**，所以外 Grid 的实际宽高由"row 中最大子项的期望尺寸"推导，并不充满 Border。结果完全等价于 StackPanel 的 case：

1. 文字短 → 外 Grid 宽 ≈ icon 宽；文字长且 wrap → 外 Grid 宽 ≈ wrap 文字宽。
2. 外 Grid 在 Border 内 Center 对齐，但其宽度在不同 cell 间不一致 → 视觉上 icon 中线漂浮（哪怕内部 icon 子 Grid `HorizontalAlignment=Center` 再次居中一次也救不了亚像素偏移）。
3. row 1 文字 1 行 vs 2 行让外 Grid 总高变化，`VerticalAlignment=Center` 让整块上下位移 → icon 垂直中心也漂。

## 修复方案

通用思路：**把 cell 拆成 icon 区 + 文字区两块独立固定槽位**，让中间容器始终撑满 Border、行高跟内容解耦。

### Overlay（代码方式构造）

- Border 仍为 `CellWidth(86) × CellHeight(90)`
- Row 0 高度固定 `IconRowHeight=54`：`Image 48×48`，`HorizontalAlignment=Center`，`VerticalAlignment=Center`
- Row 1 高度固定 `TextRowHeight=36`：`TextBlock`，`HorizontalAlignment=Stretch`，`VerticalAlignment=Top`，`TextAlignment=Center`，`TextWrapping=Wrap`

### FencePanel（DataTemplate）

`FileTileIconSize / FileTileWidth / FileTileHeight` 是 DynamicResource（受 `AppSettings.IconSize` 驱动），不能写死。改法是去掉外 Grid 的 `HorizontalAlignment=Center / VerticalAlignment=Center` 让它默认 Stretch 撑满 Border：

- Row 0 = `Auto`：因 icon 子 Grid 显式 `Width=Height=FileTileIconSize`，Auto 等价于 IconSize 固定值
- Row 1 = `*`：吃掉剩余高度（即 `FileTileHeight - IconSize`），TextBlock `HorizontalAlignment=Stretch + VerticalAlignment=Top + TextAlignment=Center` 自然居中
- icon 子 Grid 增加 `VerticalAlignment=Center`，确保它锁定在 row 0 中央

两处都给参与布局的元素（Border / Grid / Image / TextBlock）启用 `SnapsToDevicePixels=True` 与 `UseLayoutRounding=True`，杜绝亚像素偏移。

### 关键代码（[DesktopIconOverlay.xaml.cs](../../src/DesktopFences.UI/Controls/DesktopIconOverlay.xaml.cs)）

```csharp
private const double CellWidth = 86;
private const double CellHeight = 90;
private const double IconRowHeight = 54;
private const double TextRowHeight = 36;

private Border CreateIconElement(string filePath)
{
    // ...
    var image = new Image
    {
        Source = icon,
        Width = 48, Height = 48,
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center,
        Stretch = Stretch.Uniform,
        SnapsToDevicePixels = true,
        UseLayoutRounding = true
    };
    Grid.SetRow(image, 0);

    var text = new TextBlock
    {
        Text = displayName,
        TextAlignment = TextAlignment.Center,
        TextWrapping = TextWrapping.Wrap,
        TextTrimming = TextTrimming.CharacterEllipsis,
        HorizontalAlignment = HorizontalAlignment.Stretch,
        VerticalAlignment = VerticalAlignment.Top,
        Margin = new Thickness(2, 2, 2, 0),
        SnapsToDevicePixels = true,
        UseLayoutRounding = true,
        // ...
    };
    Grid.SetRow(text, 1);

    var grid = new Grid { SnapsToDevicePixels = true, UseLayoutRounding = true };
    grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(IconRowHeight) });
    grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(TextRowHeight) });
    grid.Children.Add(image);
    grid.Children.Add(text);

    var border = new Border
    {
        Width = CellWidth, Height = CellHeight,
        Child = grid,
        SnapsToDevicePixels = true,
        UseLayoutRounding = true,
        // ...
    };
    return border;
}
```

### 关键代码（[FencePanel.xaml](../../src/DesktopFences.UI/Controls/FencePanel.xaml)，三个 DataTemplate 同样处理）

```xml
<Border Width="{DynamicResource FileTileWidth}"
        Height="{DynamicResource FileTileHeight}"
        SnapsToDevicePixels="True" UseLayoutRounding="True"
        ...>
    <!-- 去掉了 HorizontalAlignment=Center 和 VerticalAlignment=Center；默认 Stretch 撑满 Border -->
    <Grid Margin="4" SnapsToDevicePixels="True" UseLayoutRounding="True">
        <Grid.RowDefinitions>
            <RowDefinition Height="Auto"/>
            <RowDefinition Height="*"/>
        </Grid.RowDefinitions>
        <Grid Grid.Row="0"
              Width="{DynamicResource FileTileIconSize}"
              Height="{DynamicResource FileTileIconSize}"
              HorizontalAlignment="Center"
              VerticalAlignment="Center">
            <!-- icon 内容 -->
        </Grid>
        <TextBlock Grid.Row="1"
                   TextAlignment="Center" TextWrapping="Wrap"
                   HorizontalAlignment="Stretch" VerticalAlignment="Top"
                   SnapsToDevicePixels="True" UseLayoutRounding="True"
                   .../>
    </Grid>
</Border>
```

## 修复关键点

1. **icon 与文字必须独立排版**：把 cell 划成两个固定槽位，任一槽位的内容变化都不会拖动另一槽位。
2. **容器必须撑满 Border，绝不能让尺寸由内容反推**：
   - 不要在外层布局容器上设 `HorizontalAlignment=Center / VerticalAlignment=Center`（默认 Stretch 才对）
   - 不要用 StackPanel 当外层容器（StackPanel 永远按子项 measure 自适应）
   - Grid 行高用显式数值或与已固定尺寸的子项配合的 `Auto`，避免行高随文字行数浮动
3. **像素对齐**：所有参与 cell 布局的元素（Border / Grid / Image / TextBlock）都启用 `SnapsToDevicePixels` 与 `UseLayoutRounding`，避免不同 cell 因亚像素 offset 视觉错位。
4. **拉伸 + 文本居中**：文字行用 `HorizontalAlignment=Stretch` 撑满 cell 宽度，再用 `TextAlignment=Center` 控制文字水平居中。这样不同长度文字的视觉中心始终是 cell 中线，跟 icon 中线天然对齐。

## 修复效果

- ✅ 同一行 cell 的 icon 水平中心精确对齐
- ✅ 同一列 cell 的 icon 垂直中心精确对齐
- ✅ 文字水平中心始终在 cell 中线，不再因文字长短漂移
- ✅ 文字 1 行 / 2 行切换时 icon 位置保持稳定
- ✅ 视觉与 Windows 11 原生桌面图标排布一致

## 相关文件

- [src/DesktopFences.UI/Controls/DesktopIconOverlay.xaml.cs](../../src/DesktopFences.UI/Controls/DesktopIconOverlay.xaml.cs)
- [src/DesktopFences.UI/Controls/FencePanel.xaml](../../src/DesktopFences.UI/Controls/FencePanel.xaml)（CustomFileTile / SystemFileTile / ShellFileTile 三个 DataTemplate）
