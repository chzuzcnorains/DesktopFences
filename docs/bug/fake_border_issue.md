# 虚伪边框的bug

## 问题描述
Fence面板周围有一个多余的透明边框，看起来像是有一个边框但实际上没有功能，导致窗口实际大小比内容大，视觉上不协调。

## 产生原因
1. **边距设计冗余**：FenceHost.xaml中的根Grid设置了`Margin="4"`，为了给 drop shadow 预留空间，但实际上这个设计导致窗口内容和窗口边界之间有4像素的透明边距。
2. **大小计算不一致**：代码中在计算窗口大小时都额外加了8像素（左右各4）来适应这个边距，导致窗口实际大小比内容大8像素。
3. **多重边框叠加**：标签栏（TabStrip）和面板主体（FencePanel）都设置了边框，导致边框重叠，视觉上看起来边框更粗。
4. **标签模式缝隙**：当有多个标签时（ShowTitleBar=False），标签栏和面板主体之间有一个30像素的空行，导致出现明显的缝隙。

## 修复方案
### 方案1：移除多余边距
- 将FenceHost.xaml中的根Grid Margin从"4"改为"0"，消除透明边距
- 移除所有代码中额外加8像素的大小计算逻辑，包括：
  - 窗口初始化时的Width和Height计算
  - 调整大小后的尺寸计算
  - 折叠/展开时的高度计算
  - 拖拽结束后的尺寸同步

### 方案2：统一边框设计
- 将TabStripBorder的BorderThickness设置为0，移除标签栏的边框
- 将FenceBorderStyle的BorderThickness设置为0，移除面板主体的边框
- 所有边框效果统一由窗口的DropShadowEffect实现

### 方案3：消除标签模式缝隙
- 新增OnShowTitleBarChanged依赖属性回调
- 当ShowTitleBar为False时，将TitleBarRow的高度设置为0，消除标签栏和面板主体之间的缝隙
- 在控件Loaded时重新同步TitleBarRow的高度，确保初始状态正确

## 核心代码修改
```xaml
<!-- FenceHost.xaml -->
<Grid Margin="0">  <!-- 从Margin="4"改为0 -->
```

```csharp
// 窗口初始化大小计算
Width = viewModel.Width;   // 原来加8，现在直接使用viewModel的宽度
Height = viewModel.Height; // 原来加8，现在直接使用viewModel的高度

// 折叠/展开高度计算
RollupChanged?.Invoke(false, targetHeight); // 原来加8，现在直接使用目标高度
RollupChanged?.Invoke(true, RolledUpHeight); // 原来加8，现在直接使用折叠高度

// 标签显示状态变化处理
private static void OnShowTitleBarChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
{
    if (d is FencePanel panel && panel.TitleBarRow is not null)
    {
        panel.TitleBarRow.Height = (bool)e.NewValue
            ? new GridLength(30)
            : new GridLength(0);
    }
}
```

## 影响范围
- Fence窗口的视觉外观
- 窗口大小计算逻辑
- 多标签模式的显示效果
- 折叠/展开动画的高度计算
