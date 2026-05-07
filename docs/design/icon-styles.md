# 文件图标风格 (Icon Style)

> Phase 12 — 由 `desktop-v2.html` 原型新增的「app vs system 双卡片」UI + Windows 经典自绘图标搬运到 WPF。
> Phase 13 — 在 Phase 12 的全局 picker 之上叠加「按 Fence 覆盖」能力,任意 fence 都可独立选择风格或回退到全局默认。

## 目标

让 Fence 内文件 tile 的视觉风格可在「App 自绘彩色 tile」与「System 经典 page-with-fold」之间一键切换；保留旧的 Shell 系统图标作为隐藏 fallback（仅可通过手编 `settings.json` 启用）。

每个 fence 还可以单独覆盖全局风格(Phase 13),用于「文档类用 App、下载类用 System」等混合场景。

## 三种风格

| 枚举值 | 视觉特征 | 资源路径 | DataTemplate |
|---|---|---|---|
| `App` | 彩色圆角 tile + 字母叠加（DOC / IMG / EXE…）；颜色由 `FileKindToIconConverter` 按扩展名映射 | `Themes/FileTypes.xaml` | `CustomFileTile`（FencePanel.xaml） |
| `System` | Windows 经典 page+fold 形状 + 底部彩色徽章 + 字母叠加；图片/视频/exe/folder 用专属造型 | `Themes/SystemFileTypes.xaml` | `SystemFileTile`（FencePanel.xaml） |
| `Shell` | `SHGetFileInfo` 抽出来的真实系统图标（与 Windows 资源管理器一致） | `Shell/Icon/ShellIconExtractor` | `ShellFileTile`（FencePanel.xaml） |

UI 上仅暴露 App / System 双卡片；Shell 仍保留代码路径作 fallback。

## 数据模型

`Core/Models/AppSettings.cs`：

```csharp
public enum FileIconStyle { App, System, Shell }

public class AppSettings
{
    // 兼容字段(旧 settings.json 只有这一个)
    public bool UseCustomFileIcons { get; set; } = true;

    // Phase 12 主字段。Nullable 是为了 detect "old JSON 没有这个键"。
    [JsonPropertyName("IconStyle")]
    public FileIconStyle? IconStyleRaw { get; set; }

    // 对外 facade —— 永远返回 non-null
    [JsonIgnore]
    public FileIconStyle IconStyle
    {
        get => IconStyleRaw ?? (UseCustomFileIcons ? FileIconStyle.App : FileIconStyle.Shell);
        set { IconStyleRaw = value; UseCustomFileIcons = value != FileIconStyle.Shell; }
    }
}
```

**迁移逻辑**:旧 JSON 没有 `IconStyle` 键 → `IconStyleRaw == null` → getter 退回 `UseCustomFileIcons`(true → App, false → Shell)。一旦 setter 被调用,`IconStyleRaw` 与 `UseCustomFileIcons` 双向同步。无需触碰 `JsonLayoutStore`。

## 分发到 UI

`App/App.xaml.cs::ApplyIconAppearance`:

```csharp
Resources["IconStyle"] = settings.IconStyle.ToString();   // "App" / "System" / "Shell"
Resources["UseCustomFileIcons"] = settings.UseCustomFileIcons;
```

`Controls/FileIconTemplateSelector.cs` 读 `Application.Resources["IconStyle"]` 并在三个 DataTemplate 中三向 switch；缺值时退回旧的 `UseCustomFileIcons` bool。模板切换由 `FencePanel.RefreshFileTileTemplate()` 触发(已有,无需改动)。

## System 图标资源结构

`Themes/SystemFileTypes.xaml` 提供 14 个 `SysFileIcon...` `DrawingImage`,viewBox 48×48:

| Key | 形状 | Badge 色 |
|---|---|---|
| `SysFileIconFolder` | 黄色文件夹(独立造型) | — |
| `SysFileIconDoc` | page+fold | `#2B5CAE` |
| `SysFileIconXls` | page+fold | `#1E7D4A` |
| `SysFileIconPpt` | page+fold | `#C43E1C` |
| `SysFileIconPdf` | page+fold | `#C02535` |
| `SysFileIconCode` | page+fold | `#3A6F8E` |
| `SysFileIconSql` | page+fold | `#2F7D7D` |
| `SysFileIconPs1` | page+fold | `#1E3A7A` |
| `SysFileIconTxt` | page+fold | `#5A6478` |
| `SysFileIconMd` | page+fold | `#2F4858` |
| `SysFileIconZip` | page+fold | `#7A6638` |
| `SysFileIconMusic` | page+fold | `#5A339B` |
| `SysFileIconLink` | page+fold | `#1E6698` |
| `SysFileIconTtf` | page+fold | `#167480` |
| `SysFileIconImg` | 紫色相片 + mountain 缩略图(独立造型,无字母) | — |
| `SysFileIconVideo` | 黑色 filmstrip + 播放三角(独立造型,无字母) | — |
| `SysFileIconExe` | 灰色 monitor + 蓝箭头 + 底座(独立造型,无字母) | — |

字母叠加由 `SystemFileTile` DataTemplate 中的 `TextBlock` 绑定 `FileItemViewModel.SystemBadgeText` 完成,而非烘焙进 SVG。

`UI/Converters/SystemFileKindToIconConverter.cs` 把 `Extension` 字符串映射到上面的 Resource Key(逻辑与 `FileKindToIconConverter` 完全平行,只是前缀 `SysFileIcon`)。

## SystemBadgeText 与 KindLabel 的差异

`FileItemViewModel.SystemBadgeText` 与 `KindLabel` 的差异:

| 类型 | KindLabel(App) | SystemBadgeText(System) | 原因 |
|---|---|---|---|
| `.png/.jpg/...` | `IMG` | `""` | 相片造型已自带 mountain 缩略图,字母会重复 |
| `.mp4/.mov/...` | `MP4` | `""` | filmstrip + 播放三角自带语义 |
| `.exe/.msi/...` | `EXE` | `""` | monitor + 蓝箭头自带语义 |
| `.sql` | `<>` | `SQL` | system 风格更具体地区分代码与 SQL |
| `.md` | `TXT` | `MD` | system 给 markdown 单独的徽章颜色 |
| 其他 | (相同) | (相同) | — |

## UI 入口

`Controls/Settings/AppearanceSettingsPane`:

- 「图标风格 · Icon style」卡片,2 列 UniformGrid 双卡片
- 卡片复用 `TabStyleTileStyle`(active 态有 accent 边框 + 浅蓝高亮)
- `_iconStyle` 字段在 `Load`/`Save`/`BuildSnapshot` 中流转
- `Load` 把持久化的 `Shell` clamp 回 `App`(picker 不暴露 Shell)

## 决策记录

1. **为什么不直接用 Win32 SHGetFileInfo + theming?**
   原型设计要求 system 风格是「Windows 经典」(Win98/XP 风格的 page+fold),不是当前 Win11 的 fluent shell 图标。自绘 DrawingImage 给我们对配色/字母叠加的完全控制。

2. **为什么 Shell 是 hidden fallback?**
   Shell 风格依赖 LRU cache + 异步加载,在 50+ 个文件的 Fence 上会闪烁;App / System 是 DrawingImage,瞬间渲染。但 Shell 对小众文件类型(自定义 ICO 关联程序)是唯一能拿到真图标的途径,所以保留 setter 渠道。

3. **为什么字母不烘焙进 SVG?**
   烘焙 14 × N 种字母组合会让 XAML 资源文件爆炸;由 DataTemplate 的 TextBlock 叠加,只需要一份 DrawingImage + 一个 `SystemBadgeText` 字符串。

4. **为什么把迁移逻辑写在 `IconStyle` getter 而不是 `JsonLayoutStore`?**
   nullable raw field + 计算属性是无状态的、可逆的、类型自描述的,不会在 JsonLayoutStore 里塞迁移分支(后续 settings.json 字段越多,迁移分支越乱)。

## Phase 13 — 按 Fence 覆盖

### 数据模型

`Core/Models/FenceDefinition.cs`:

```csharp
public FileIconStyle? IconStyleOverride { get; set; }
```

- `null` → 跟随全局 `AppSettings.IconStyle`(默认行为,与 Phase 12 完全一致)
- non-null → 该 fence 强制使用此风格,允许 `App` / `System` / `Shell` 三值
- 旧 `fences.json` 没有此字段 → 反序列化为 `null` → 自动跟随全局,无需迁移

### ViewModel facade

`UI/ViewModels/FencePanelViewModel.cs`:

```csharp
public FileIconStyle? IconStyleOverride { get; set; }   // 双向同步到 _model
public FileIconStyle EffectiveIconStyle { get; }        // override ?? global ?? App
```

- `IconStyleOverride` setter 同时触发 `OnPropertyChanged(nameof(EffectiveIconStyle))`
- `EffectiveIconStyle` 是只读派生属性:`override ?? Application.Resources["IconStyle"]`(string,Phase 12 由 `App.xaml.cs::ApplyIconAppearance` 推送) `?? FileIconStyle.App`

### Selector 路由(关键改动)

`UI/Controls/FileIconTemplateSelector.cs`:

```csharp
public override DataTemplate? SelectTemplate(object item, DependencyObject container)
{
    // 1) 上溯 visual + logical tree → FencePanel → ViewModel.EffectiveIconStyle
    if (FindAncestor<FencePanel>(container)?.DataContext is FencePanelViewModel vm)
        return Pick(vm.EffectiveIconStyle);

    // 2) 兜底:全局 Application resource(预览面板等无 fence 上下文的场景)
    if (app?.TryFindResource("IconStyle") is string s
        && Enum.TryParse<FileIconStyle>(s, out var parsed))
        return Pick(parsed);

    // 3) 最终兜底:Phase 11 之前的 UseCustomFileIcons bool
    var legacy = app?.TryFindResource("UseCustomFileIcons") is bool b ? b : true;
    return legacy ? CustomTemplate : ShellTemplate;
}
```

为什么用 visual tree 上溯而不是注入到 `FencePanel.Resources`?Application 资源是全局单例,不能按控件树分支;FrameworkElement.Resources 字典查找不一定经过 FencePanel(取决于 ItemContainer 生成时机)。**显式上溯最可控,且只在 Selector 这一处。**

### UI 入口

`FencePanel.ShowTitleBarMenu` 的「图标风格 ▶」二级菜单:

| 菜单项 | 写入 `IconStyleOverride` |
|---|---|
| 跟随全局 | `null` |
| App 自绘 | `FileIconStyle.App` |
| System 经典 | `FileIconStyle.System` |

`MenuItem.IsChecked` 实时读 `vm.IconStyleOverride`(`null` 对应「跟随全局」)。点击后:

1. `vm.IconStyleOverride = ...`
2. `RefreshFileTileTemplate()`(同步,不依赖 PropertyChanged 通知)
3. `InteractionEnded?.Invoke()` → 触发 `RequestAutoSave`

Shell 风格不在菜单中暴露,沿用 Phase 12 决策。

### 全局变化的联动

`App.xaml.cs::ApplyIconAppearance` 已对所有 host 调用 `RefreshFileTileTemplate()`:

- `IconStyleOverride == null` 的 fence:Selector 上溯 → ViewModel.EffectiveIconStyle 读到新的全局值 → ✓ 自动跟随
- `IconStyleOverride != null` 的 fence:Selector 返回 override 本身 → ✓ 不受影响

无需额外联动代码。

### PropertyChanged 订阅

`FencePanel.OnDataContextChanged` 订阅 `vm.PropertyChanged`,当 `EffectiveIconStyle` 触发时调 `RefreshFileTileTemplate()`,`OnUnloaded` 反订阅。这一步保证「外部代码改 ViewModel 也能同步刷新」(批量重置、API 等场景),菜单交互本身已在第 2 步显式调用刷新。
