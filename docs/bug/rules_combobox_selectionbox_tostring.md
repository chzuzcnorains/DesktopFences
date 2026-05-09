# 设置-分类规则 下拉框选中后展示与下拉项不一致

## 问题描述

打开 设置 → 分类规则 → 选中任意一条规则。

- 「匹配方式」与「目标 Fence」两个 ComboBox 下拉打开时，列表项正常显示中文/Fence 标题（如「扩展名」「文件名通配符」、Fence 的 Title）。
- 但下拉框关闭后，输入框里展示的是对象的 `ToString()`：
  - `MatchTypeOption` 是 record，显示成 `MatchTypeOption { Display = 扩展名, Hint = 逗号分隔..., Type = Extension }`。
  - `FenceDefinition` 没有重写 ToString，显示成 `DesktopFences.Core.Models.FenceDefinition`。

## 问题根源

`DarkComboBoxStyle` 的自定义 ControlTemplate 中，闭合态显示用：

```xml
<ContentPresenter x:Name="ContentSite"
                  Content="{TemplateBinding SelectionBoxItem}"
                  ContentTemplate="{TemplateBinding SelectionBoxItemTemplate}"
                  ... />
```

而 `RulesSettingsPane` 两个 ComboBox 只设置了 `DisplayMemberPath`（`"Display"` / `"Title"`），没有显式 `ItemTemplate`。

WPF 在这种组合下的行为差异：

1. **下拉项**：`DarkComboBoxItemStyle` 模板里的 `<ContentPresenter />` 处于 `ComboBoxItem`（ContentControl 派生）的 Template 内部，会自动从 templated parent 继承 `Content` 与 `ContentTemplate`。WPF 给 `ComboBoxItem.ContentTemplate` 注入了由 `DisplayMemberPath` 自动生成的内部 DataTemplate，于是绑定 `{Binding Display}` 生效，列表项渲染正常。
2. **闭合态 ContentSite**：它处于 `ComboBox` 自身 ControlTemplate 内，不会自动继承上面的内部模板，只能依赖 `SelectionBoxItem` / `SelectionBoxItemTemplate` 这两个 TemplateBinding。WPF 默认控件模板下这俩值由内部代码协同填充；但当用户替换了 ControlTemplate 又只用 `DisplayMemberPath` 时，`SelectionBoxItemTemplate` 实际为 `null`，`SelectionBoxItem` 则是原始数据对象。`ContentPresenter` 在 Content 非空、ContentTemplate 为空时回退到 `ToString()`，因此把 record / class 的默认字符串吐了出来。

简单说：`DisplayMemberPath` 在自定义 ControlTemplate 的闭合显示路径上不是可靠的契约；需要用显式 `ItemTemplate` 才能让闭合态与下拉项共用同一个模板。

## 修复方式

把 `DisplayMemberPath` 替换成显式 `ItemTemplate`，绑定到对应字段。WPF 在没有 ItemTemplate 而设置了 DisplayMemberPath 时不会自动填充 SelectionBoxItemTemplate，但只要存在 ItemTemplate，闭合态 ContentSite 就会用同一个模板渲染。

[`RulesSettingsPane.xaml`](../../src/DesktopFences.UI/Controls/Settings/RulesSettingsPane.xaml)：

```xml
<ComboBox x:Name="CboMatchType" ...>
    <ComboBox.ItemTemplate>
        <DataTemplate>
            <TextBlock Text="{Binding Display}" />
        </DataTemplate>
    </ComboBox.ItemTemplate>
    ...
</ComboBox>

<ComboBox x:Name="CboTargetFence" ...>
    <ComboBox.ItemTemplate>
        <DataTemplate>
            <TextBlock Text="{Binding Title}" />
        </DataTemplate>
    </ComboBox.ItemTemplate>
    ...
</ComboBox>
```

并在 [`RulesSettingsPane.xaml.cs`](../../src/DesktopFences.UI/Controls/Settings/RulesSettingsPane.xaml.cs) 删除两处冗余的运行期 `DisplayMemberPath = ...` 设置。

## 经验教训

- 自定义 `ComboBox` 的 ControlTemplate 时，**只用 `DisplayMemberPath` 不够**——闭合态依赖 `SelectionBoxItemTemplate`，而它不一定从 DisplayMemberPath 自动派生。优先使用 `ItemTemplate`。
- 项目内任何后续要复用 `DarkComboBoxStyle` 且数据项是非字符串对象的地方，统一走 `ItemTemplate` 路线，避免再次踩坑。
- 单字段绑定可以用 `<TextBlock Text="{Binding XX}"/>` 直接写在 ItemTemplate 里；本仓库已有 `DarkComboBoxItemStyle` 负责容器样式（高亮/选中底色），DataTemplate 只需关心内容。

## 修复版本

2026-05-09
