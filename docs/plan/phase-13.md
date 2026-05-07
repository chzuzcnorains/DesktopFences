# Phase 13: 按 Fence 覆盖 IconStyle

**目标**:让每个 Fence 可以独立选择文件图标风格(App / System / 跟随全局),Phase 12 在全局层完成的「双卡片选择器」自然延展到单 fence 维度。用户右键 fence 标题菜单即可切换;不覆盖时跟随全局设置。

## 13.1 背景

- Phase 12 把 `FileIconStyle` 枚举(App / System / Shell)接入 `AppSettings.IconStyle`,所有 fence 共用同一种风格。
- 用户场景:有人喜欢「文档类 fence 用 App 彩色 tile」一目了然,「下载/临时类 fence 用 System 经典」与 Win11 资源管理器一致 —— 一刀切的全局风格无法兼顾。
- 实现层面:`FileIconTemplateSelector` 当前直接读 `Application.Resources["IconStyle"]`(string),只需把数据源从「全局 resource」换成「per-fence ViewModel + 全局 fallback」。

## 13.2 关键决策

### 13.2.1 数据模型:nullable override

`FenceDefinition` 新增字段:

```csharp
public FileIconStyle? IconStyleOverride { get; set; }
```

- `null` → 跟随全局 `AppSettings.IconStyle`(默认行为)
- non-null → 该 fence 强制使用此风格;允许 `App` / `System` / `Shell` 三值(高级用户可手编 JSON 选 Shell,与 Phase 12 决策一致)
- JSON 序列化时为 null 的字段会因 `JsonIgnoreCondition.WhenWritingDefault` 在反序列化层省略,无需迁移逻辑

### 13.2.2 ViewModel:暴露 override + EffectiveIconStyle

`FencePanelViewModel` 新增:

```csharp
public FileIconStyle? IconStyleOverride { get; set; }   // 双向同步到 _model
public FileIconStyle EffectiveIconStyle { get; }        // override ?? 全局
```

- `IconStyleOverride` setter 同时触发 `OnPropertyChanged(nameof(EffectiveIconStyle))`
- `EffectiveIconStyle` getter 读 `Application.Current.Resources["IconStyle"]`(string),解析失败回退 `App`
- `EffectiveIconStyle` 仅 getter — 不持久化、不需要 INotify 来源 setter

### 13.2.3 Selector:从 container 上溯到 FencePanel

`FileIconTemplateSelector.SelectTemplate(item, container)`:

1. 从 `container`(ListBoxItem)沿 visual tree 上溯,找到最近的 `FencePanel`
2. 取 `panel.DataContext as FencePanelViewModel` → `vm.EffectiveIconStyle`
3. 找不到 fence 时(理论上不会发生)回退到现有逻辑读全局 `Application.Resources["IconStyle"]`

为什么不用 `Application.Resources` 注入 per-fence?Application 资源是全局单例,不能按控件树分支;FrameworkElement.Resources 字典查找会经过父级 → root,但 ListBoxItem 不一定走 FencePanel.Resources(取决于 ItemContainer 生成时机)。**显式上溯最可控。**

### 13.2.4 UI 入口:FencePanel 标题菜单子菜单

`FencePanel.ShowTitleBarMenu` 在「重命名」之后插入「图标风格 ▶」二级菜单:

- ✓ 跟随全局 (`null`)
- App 自绘 (`App`)
- System 经典 (`System`)

勾选状态:`MenuItem.IsChecked = (vm.IconStyleOverride == ...)`(`null` 对应「跟随全局」)。
点击后:`vm.IconStyleOverride = ...; RefreshFileTileTemplate(); InteractionEnded?.Invoke();`(后者触发 `RequestAutoSave`)

Shell 风格不在菜单中暴露,沿用 Phase 12 决策。

### 13.2.5 全局变化联动

`App.xaml.cs::ApplyIconAppearance` 已在所有 host 上调 `RefreshFileTileTemplate()`。
override == null 的 fence:Selector 上溯 → ViewModel.EffectiveIconStyle → 读到新的全局 resource → 自动跟随 ✓
override != null 的 fence:Selector 上溯 → ViewModel.EffectiveIconStyle 返回 override 本身 → 不受影响 ✓

不需要额外联动代码。

### 13.2.6 ViewModel PropertyChanged → 模板刷新

用户在标题菜单切风格时,代码直接调 `RefreshFileTileTemplate()`,不依赖 ViewModel 通知。但若以后有其他路径修改 `IconStyleOverride`(脚本、批量操作等),应保持「数据变化 → UI 刷新」的可靠性:

`FencePanel.OnDataContextChanged` 中订阅 `vm.PropertyChanged`,当 `EffectiveIconStyle` 触发时调 `RefreshFileTileTemplate()`。Unloaded 时反订阅。

## 13.3 实现步骤

| 步骤 | 文件 | 动作 |
|---|---|---|
| 1 | `Core/Models/FenceDefinition.cs` | 新增 `FileIconStyle? IconStyleOverride` |
| 2 | `UI/ViewModels/FencePanelViewModel.cs` | 新增 `IconStyleOverride` setter + `EffectiveIconStyle` getter |
| 3 | `UI/Controls/FileIconTemplateSelector.cs` | 上溯 FencePanel,读 `EffectiveIconStyle`;找不到回退全局 |
| 4 | `UI/Controls/FencePanel.xaml.cs::ShowTitleBarMenu` | 加「图标风格」子菜单 |
| 5 | `UI/Controls/FencePanel.xaml.cs::OnDataContextChanged` / `OnUnloaded` | 订阅/反订阅 `vm.PropertyChanged` |
| 6 | `tests/DesktopFences.Core.Tests/JsonLayoutStoreTests.cs` | 新增 round-trip 测试覆盖 `IconStyleOverride` |
| 7 | `docs/design/icon-styles.md` | 加「按 fence 覆盖」一节 |
| 8 | `docs/plan/complete.md` / `currentplan.md` / `currenttasks.md` | 同步状态 |

## 13.4 风险与回退

- **Visual tree 上溯失败**:理论上 ListBoxItem 永远在 FencePanel 内,但若以后引入 popup 模板,上溯会断。Selector 的全局 fallback 兜底,不会渲染异常。
- **菜单状态与数据漂移**:菜单每次重建,`IsChecked` 实时读 vm 字段,不存在缓存问题。
- **JSON 兼容**:旧 settings.json 没有 `IconStyleOverride` 键 → 反序列化为 null → 跟随全局,与现状一致。
- **Shell 风格 + override**:用户手编 JSON 把 override 设为 Shell,菜单会同时取消三个勾选状态 —— 视觉上能看出「外部覆盖」,符合 Phase 12「Shell 是隐藏 fallback」的定位。

## 13.5 测试

**单元测试**(`JsonLayoutStoreTests.cs`):
- `SaveAndLoadFences_PreservesIconStyleOverride_Null` — 不设 override,反序列化后 == null
- `SaveAndLoadFences_PreservesIconStyleOverride_System` — 设 System,反序列化后 == System

**手动验证**:
1. 双 fence 各设不同 override(App / System)→ 独立生效
2. 全局切到 System,override == null 的 fence 跟随,override == App 的 fence 不变
3. override == App 的 fence 切回「跟随全局」→ 显示当前全局风格
4. 重启应用 → 状态保留
5. 模板切换无 ListBox 闪烁(Items.Refresh 已有路径)

## 13.6 不在本 Phase 范围

- **Appearance pane 显示「N 个 fence 已覆盖」提示** — 推迟,picker 默认假设全局生效即可
- **每个 tab 独立 override** — 当前每个 tab 是一个独立 `FenceDefinition`,自然支持(无需额外代码),仅 UI 入口在 fence 标题菜单
- **批量重置所有 override** — 手动清空全部 override 用户极少用,推迟

## 13.7 文档与回写

- `docs/design/icon-styles.md` 增「按 fence 覆盖」一节,说明 `IconStyleOverride` 字段、UI 入口、Effective 计算
- `docs/plan/complete.md` 加入 Phase 13 ✅
- `docs/plan/currentplan.md` / `currenttasks.md` 完成时清空
