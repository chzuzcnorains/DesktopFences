# Phase 12: iconStyle 双卡片选择器 + System 图标资源

**目标**:把 `desktop-v2.html` v2 原型最近新增的「app vs system 两种图标风格 + 实时预览双卡片」UI 与 Windows 经典 page-with-fold + colored badge 自绘图标搬到 WPF。让用户在「**App 自绘彩色 tile**」和「**System 经典页角折叠**」之间切换。

## 12.1 背景

- 原型 `src/system-icons.jsx`(2026-05-06 入库)实现了 SVG 版的 system 风格图标:14 类(folder/doc/xls/ppt/pdf/img/video/code/sql/ps1/txt/md/exe/zip),其中 img/video/exe/folder 为特殊形状(无字母叠加),其余为 page+fold+colored badge + 字母。
- 原型 `desktop-v2.html` 配合 `iconStyle: 'app' | 'system'` tweaks 字段加了双卡片选择器,带实时 2×2 预览。
- WPF 端目前只有 `UseCustomFileIcons`(bool)分 App/Shell 两种,且 AppearanceSettingsPane 没有该字段的可视入口。

## 12.2 关键决策

### 12.2.1 数据模型:引入枚举

- 新增 `FileIconStyle` 枚举: `App` / `System` / `Shell`,默认 `App`
- 新增 `AppSettings.IconStyle: FileIconStyle` 字段
- 旧字段 `UseCustomFileIcons` 保留;反序列化时迁移:
  - `IconStyle` 不为 null → 用之
  - `IconStyle` null + `UseCustomFileIcons==false` → `Shell`
  - 其他 → `App`(默认)
- 写回 settings.json 时 `IconStyle` 为权威字段;`UseCustomFileIcons` 同步更新以兼容老逻辑

### 12.2.2 UI 暴露 2 张卡片(对齐原型)

- AppearanceSettingsPane 增加「图标风格 · Icon style」卡片,内含 2×1 的双卡片
- 每张卡片左侧 2×2 mini 预览(4 个示例 tile)+ 右侧 单选点 + 名称 + 一句描述
- 卡片 1: **App 自绘** — 彩色 tile + 字母叠加(现行默认)
- 卡片 2: **System 经典** — Windows 页角折叠 + 颜色徽标
- `Shell`(SHGetFileInfo)模式不在双卡片中暴露,作为隐藏选项保留(可手改 settings.json),以贴合原型的 2 选 1 设计意图

### 12.2.3 资源:SystemFileTypes.xaml 新建,与 FileTypes.xaml 并列

- 14 个 `DrawingImage` 资源,key 命名 `SysFileIconFolder`、`SysFileIconDoc`...,与现行 `FileIconXxx` 一一对应
- 字母采用与 App 风格相同的策略: **不烘焙进 GeometryDrawing**,由 `SystemFileTile` DataTemplate 的 `TextBlock` 叠加 — 跟随主题字体、易于调尺寸
- img / video / exe / folder 几个特殊形状(图片含 mountain、video 含 filmstrip、exe 含 monitor、folder 不是 page 形)单独写,字母叠加层通过空字符串隐藏

### 12.2.4 模板与选择器

- `FencePanel.xaml` 资源里新增 `SystemFileTile` DataTemplate,与 `CustomFileTile` 平级
- `FileIconTemplateSelector`(`Controls/FileIconTemplateSelector.cs`)从二选一改为三选一,新增 `SystemTemplate` 属性,按 `Application.Resources["IconStyle"]` 路由
- `App.xaml.cs` `ApplyIconAppearance` 同步推 `IconStyle` 到 `Application.Current.Resources`

### 12.2.5 Letter 映射差异

- App 风格的 `KindLabel`(W/X/PDF/IMG/MP4/♪/<>/ZIP/EXE/TXT/↗/Aa)沿用
- System 风格映射略不同(`<>`/`SQL`/`>_`/`MD`/`PDF`/`ZIP`...),img/video/exe/folder 应隐藏
- 在 `FileItemViewModel` 增 `SystemBadgeText` 派生字段,System DataTemplate 绑此字段;复用现有 `LabelLenToFontSizeConverter`(length==0 时 size=0,自然隐藏)

## 12.3 实现要点

| 步骤 | 文件 | 动作 |
|---|---|---|
| 1 | `Core/Models/AppSettings.cs` | 新增 `FileIconStyle` 枚举与 `IconStyle` 属性,保留 `UseCustomFileIcons` |
| 2 | `Core/Services/JsonLayoutStore.cs`(或 `LoadSettingsAsync` 处) | 反序列化迁移:`UseCustomFileIcons==false` → `IconStyle=Shell` |
| 3 | `UI/ViewModels/FileItemViewModel.cs` | 新增 `SystemBadgeText` 派生属性 |
| 4 | `UI/Themes/SystemFileTypes.xaml`(新) | 14 个 SysFileIcon DrawingImage(page+fold+badge / 特殊形状) |
| 5 | `UI/Converters/SystemFileKindToIconConverter.cs`(新) | 与 FileKindToIconConverter 同结构,key 走 SysFileIconXxx |
| 6 | `UI/Themes/FileTile.xaml` | 注册 `SysKindToIcon` Converter 实例 |
| 7 | `UI/Controls/FencePanel.xaml` | 新增 `SystemFileTile` DataTemplate;FileIconSelector 增加 `SystemTemplate` 绑定 |
| 8 | `UI/Controls/FileIconTemplateSelector.cs` | 改三路 switch,读 `Application.Resources["IconStyle"]`(string) |
| 9 | `UI/Controls/Settings/AppearanceSettingsPane.xaml` | 新增「图标风格」卡片 + 容器 grid |
| 10 | `UI/Controls/Settings/AppearanceSettingsPane.xaml.cs` | 双卡片构建 + Load/Save IconStyle |
| 11 | `App/App.xaml` | MergedDictionaries 加入 SystemFileTypes.xaml |
| 12 | `App/App.xaml.cs` `ApplyIconAppearance` | 推 `IconStyle` 字符串到 Application.Resources;同步 `UseCustomFileIcons` 兼容 |

## 12.4 风险与回退

- **资源字典加载顺序**:SystemFileTypes.xaml 需在 FencePanel.xaml `<UserControl.Resources>` 前已合并入 Application.Resources。已通过 App.xaml 集中合并控制顺序。
- **现有 settings.json 兼容**:迁移逻辑在反序列化层处理;旧用户首次启动后 `IconStyle=App`(默认),写回时新增字段。
- **System DrawingImage 渲染开销**:14 个 viewBox 48×48 的 GeometryDrawing 静态资源,无运行时构造开销,与现有 FileTypes.xaml 等价。
- **小尺寸下 letter 模糊**:Phase 9c 已通过 `RenderOptions.BitmapScalingMode="HighQuality"` 处理 — 复用即可。

## 12.5 文档与回写

- `docs/design/icon-styles.md`(新):说明 App / System / Shell 三种风格的设计意图、资源约定、何时切换
- `docs/plan/complete.md` 加入 Phase 12 ✅
- `docs/plan/currentplan.md` / `currenttasks.md` 同步状态

## 12.6 不在本 Phase 范围

- **Shell 风格的可视入口**:不在 Appearance pane 双卡片中暴露;若以后需要,作为 Advanced pane 的一个高级选项另开 Phase
- **每个 fence 单独覆盖 IconStyle**:目前是全局设置;按 fence 覆盖属于 Phase 13+ 范围
