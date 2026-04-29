# 滚动条样式与暗色设计不匹配的 bug

## 问题描述
设置面板和 Fence 面板内容区域的滚动条都是 Windows 原生灰白滚动条（带上下箭头按钮），与项目整体暗色 UI 风格严重不协调。

## 产生原因
`Themes/DarkTheme.xaml` 早期定义了一个名为 `DarkScrollBarStyle` 的 `ScrollBar` 样式，但带了 `x:Key="DarkScrollBarStyle"`——这是一个**显式样式**，必须通过 `Style="{StaticResource DarkScrollBarStyle}"` 才会生效。代码里没有任何位置引用过它（`grep DarkScrollBarStyle` 只命中定义本身），所以默认渲染回退到 `themes\Aero2.NormalColor.xaml` 的系统原生 ScrollBar。

此外，原样式只为垂直方向写了 `Width=4`，没有 `Orientation` 触发器，水平滚动条会出现宽度退化的渲染问题。

## 修复方案
在 `Themes/DarkTheme.xaml` 重写成**隐式样式**（去掉 `x:Key`），并补齐水平/垂直两套 `ControlTemplate`。`DarkTheme.xaml` 在 `App.xaml` 的 `Application.Resources.MergedDictionaries` 全局合并，因此应用内所有 `ScrollBar`（含 `ScrollViewer` 内部、`ListBox` 默认模板内的 `ScrollBar`）都会自动套用。

样式要点：
- 厚度 8px，半透明 thumb（`#55FFFFFF` 默认 / `#88FFFFFF` hover / `#AAFFFFFF` 拖动），圆角 3px
- 两端的 `RepeatButton`（默认箭头按钮）用宽高 0 的隐形样式替换，避免出现原生上下箭头
- 轨道背景透明，整体观感是"覆盖在内容之上的细条"，与暗色卡片 UI 协调
- `Style.Triggers` 按 `Orientation` 切换垂直 / 水平 `ControlTemplate`，水平模式下 thumb 的 Margin 在垂直方向收 2px、垂直模式下水平方向收 2px，保证 hover 不顶到边缘

## 影响范围
- 设置面板（SettingsWindow）右侧内容区滚动条
- Fence 面板内容区（ListBox 内置 ScrollViewer）的滚动条
- 任何其他通过 `ScrollViewer` / `ListBox` / `ListView` / `DataGrid` 暴露的 `ScrollBar`，都自动跟随

不影响显式引用过自定义 `Style` 的 ScrollBar——本项目目前没有这种点。

## 经验总结
- WPF 隐式样式（`<Style TargetType="...">` 不带 `x:Key`）才会被未指定 `Style` 的控件自动套用；带 `x:Key` 的样式必须显式引用，写完不引用等于白写。
- 重写 `ScrollBar` 模板时务必用 `Orientation` 触发器分别提供水平 / 垂直两套 `ControlTemplate`，否则会在水平滚动场景下出现尺寸异常。
- `DarkTheme.xaml` 这种全局合并的 ResourceDictionary 是放隐式控件样式的理想位置，新加的全局 UI 风格应优先放这里。
