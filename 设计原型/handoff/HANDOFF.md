# DesktopFences · 图标系统落地交接文档

> **交接对象**：Claude Code
> **目标**：将本目录内的图标资产落到 `DesktopFences.UI` / `DesktopFences.App` 项目，替换现有占位 emoji、ASCII 字符、硬编码 SVG。
> **作者约定**：文件路径全部相对仓库根目录。不涉及的项目（Core / Shell）**不要动**。
> **最终效果**：一次编译通过，运行后任务栏图标、主窗口图标、设置窗口标题栏 logo、所有工具条按钮、右键菜单前缀全部切换到新图标系统，且支持主题色切换。

---

## 0 · 交付物总览

```
handoff/
├── HANDOFF.md                      ← 本文档（权威落地指令）
├── README.md                       ← 快速开始 + 清单
├── preview.html                    ← 浏览器预览所有图标
├── icons/
│   ├── app-logo.svg                ← 主 Logo 矢量源（48×48 viewBox，含渐变）
│   ├── app-logo-mono.svg           ← 托盘单色版本（currentColor）
│   ├── actions.sprite.svg          ← 20 个操作图标 sprite 源（24×24）
│   └── file-types.sprite.svg       ← 14 个文件类型图标 sprite 源（48×48）
└── xaml/
    ├── AppLogo.xaml                ← 主 Logo 的 DrawingImage 资源字典
    ├── Icons.xaml                  ← 20 个操作图标 Geometry + 模板 + 按钮样式
    ├── FileTypes.xaml              ← 14 个文件类型图标 DrawingImage
    └── FileKindToIconConverter.cs  ← 扩展名 → 图标资源 Key 的转换器
```

除上述文件外，本项目根目录还保留 `desktop-v2.html`（交互原型，集成后效果参考）与 `icons.html`（三套方案并列对比，不落地）。

---

## 1 · 设计决策摘要（为何这么做）

| 选择 | 方案 | 理由 |
|---|---|---|
| **主图标（App Logo）** | A · Fluent Acrylic（四宫格围栏 + 蓝色渐变 + 高光） | 直接呼应 Fence 的核心隐喻（桌面分区）；Windows 11 Fluent 风；256px 下细节最丰富，16px 下仍然辨识度高 |
| **操作图标（Action Icons）** | C · Glass Outline（`stroke-width=1.8`，圆角端点） | 线条纤细、密度低，在 16–24px 工具条中不会糊成一团；与 `DarkTheme.xaml` 的视觉密度吻合 |
| **文件类型图标** | 14 个自绘 Fluent 文档图标 + ViewModel 层叠文字标签 | 与主 Logo 风格一致；免除对 Shell32.dll 依赖；色彩语义明确（PDF=红，Excel=绿）。Fence 默认显示自绘图标；用户可在设置里切换到"系统图标"（调 `SHGetFileInfo`） |
| **色彩 token** | 继续使用 `DarkTheme.xaml` 中 `AccentBrush = #6688CC` | 不新增变量；Logo 内部写死 `#7AA7E6 / #3D58B1`（亮/暗），但暴露为 `AppLogoTopColor / AppLogoBottomColor` 以便后续换色 |

---

## 2 · 应用主图标（App Logo）

### 2.1 规格

- **viewBox**：`0 0 48 48`
- **外形**：圆角方形 `rx=10.5`
- **主色渐变**：`#7AA7E6 → #3D58B1`（竖向）
- **四宫格**：`14×14` 白色方块，`rx=2.5`，左上/右下 95% 不透明、右上/左下 55% 不透明（体现"层次差异"）
- **高光**：左上到右下的白色斜向渐变，0%→60% 淡出
- **边缘**：`1px` 白色 25% 不透明的内描边，强化"贴纸感"

### 2.2 落地步骤

#### 2.2.1 替换 `app.ico`

现有 `src/DesktopFences.App/Assets/app.ico` 是占位文件。请按下表重新生成多尺寸 ICO：

| 尺寸 | 用途 |
|---|---|
| 16 | 任务栏小图标、Alt+Tab 缩略图 |
| 20 | 资源管理器细节视图 |
| 24 | 标题栏 |
| 32 | 标准桌面图标 |
| 40 | 大图标视图 |
| 48 | 超大图标视图 |
| 64 | 安装程序 |
| 128 | 商店 |
| 256 | 4K 资源管理器大图标 |

**推荐生成命令**（任选其一，在 `handoff/icons/` 目录执行）：

```powershell
# 方案 A：ImageMagick（推荐）
magick -background none app-logo.svg `
    -define icon:auto-resize=256,128,64,48,40,32,24,20,16 `
    ../../src/DesktopFences.App/Assets/app.ico

# 方案 B：Inkscape 逐尺寸导出 + IcoFX / icotool 合并
inkscape app-logo.svg -w 256 -h 256 -o tmp-256.png
inkscape app-logo.svg -w 128 -h 128 -o tmp-128.png
# ... 其他尺寸
icotool -c -o ../../src/DesktopFences.App/Assets/app.ico tmp-*.png
```

覆盖后**无需修改 .csproj**：现有 `<ApplicationIcon>Assets\app.ico</ApplicationIcon>` 会自动生效。

#### 2.2.2 注入 `AppLogo.xaml` 资源字典

1. 把 `handoff/xaml/AppLogo.xaml` 复制到 `src/DesktopFences.UI/Themes/AppLogo.xaml`
2. 在 `.csproj` 中它会被 MSBuild 自动识别为 Page（因为 Themes/ 已有同类）
3. 在 `src/DesktopFences.App/App.xaml` 中合并：

```xml
<Application.Resources>
    <ResourceDictionary>
        <ResourceDictionary.MergedDictionaries>
            <ResourceDictionary Source="/DesktopFences.UI;component/Themes/DarkTheme.xaml"/>
            <ResourceDictionary Source="/DesktopFences.UI;component/Themes/TabStyles.xaml"/>
            <!-- 新增 ↓↓↓ -->
            <ResourceDictionary Source="/DesktopFences.UI;component/Themes/AppLogo.xaml"/>
            <ResourceDictionary Source="/DesktopFences.UI;component/Themes/Icons.xaml"/>
            <ResourceDictionary Source="/DesktopFences.UI;component/Themes/FileTypes.xaml"/>
        </ResourceDictionary.MergedDictionaries>
    </ResourceDictionary>
</Application.Resources>
```

#### 2.2.3 在 UI 中使用 Logo

| 位置 | 代码 |
|---|---|
| 托盘菜单头、关于页面、任何"品牌展示"位 | `<Image Source="{StaticResource AppLogoImage}" Width="48" Height="48"/>` |
| 设置窗口自定义标题栏左上角 | `<Image Source="{StaticResource AppLogoImage}" Width="16" Height="16" Margin="0,0,8,0"/>` |
| 搜索面板空态 | `<Image Source="{StaticResource AppLogoImage}" Width="64" Height="64" Opacity="0.6"/>` |

---

## 3 · 操作图标（20 个）

### 3.1 清单

| 资源 Key | 中文名 | 使用位置（prototype 对应） |
|---|---|---|
| `IconSearch` | 搜索 | 任务栏搜索框 / 设置窗左侧搜索 / SearchWindow |
| `IconSettings` | 设置 | 右键菜单"设置"/ Tab 条设置按钮 |
| `IconPin` | 置顶 / 钉住 | Fence 右键"置顶"（未来功能） |
| `IconHide` | 隐藏 | 右键菜单"显示/隐藏所有 Fence" |
| `IconRollup` | 折叠 | Fence 标题栏右上、右键菜单"折叠" |
| `IconPeek` | Peek 桌面 | 右键菜单 `Win+Space` 条目 |
| `IconAdd` | 新建 | 设置窗右上"新建规则"/"新建 Fence" |
| `IconLock` | 锁定 | Fence 右键"锁定位置"（未来功能） |
| `IconMerge` | 合并标签 | Tab 拖拽合并提示 |
| `IconSplit` | 拆分标签 | Tab 右键"从 Fence 分离" |
| `IconTrash` | 删除 | 右键菜单"关闭/删除 Fence"；回收站空态 |
| `IconRule` | 分类规则 | 设置侧栏"分类规则"项 |
| `IconPortal` | Folder Portal | 右键"设为文件夹映射" |
| `IconTheme` | 主题色 | 右键菜单"主题颜色..." |
| `IconClose` | × | 所有窗口标题栏关闭 |
| `IconMin` | − | 窗口标题栏最小化 |
| `IconMax` | □ | 窗口标题栏最大化 |
| `IconKeyboard` | 快捷键 | 设置侧栏"快捷键" |
| `IconGrid` | Fence 管理 / 布局 | 设置侧栏"Fence 管理"、关于页面装饰 |
| `IconInfo` | 关于 | 设置侧栏"关于" |

### 3.2 落地步骤

#### 3.2.1 注入 `Icons.xaml`

同 2.2.2 步骤。

#### 3.2.2 引用示例

```xml
<!-- 独立图标：20px 见方，跟随当前 Foreground -->
<ContentControl Template="{StaticResource IconTemplate}"
                Tag="{StaticResource IconSearch}"
                Width="20" Height="20"
                Foreground="{DynamicResource TextSecondaryBrush}"/>

<!-- 带悬停的图标按钮 -->
<Button Style="{StaticResource DarkIconButtonStyle}"
        Command="{Binding ToggleRollupCommand}"
        ToolTip="折叠">
    <ContentControl Template="{StaticResource IconTemplate}"
                    Tag="{StaticResource IconRollup}"
                    Width="14" Height="14"/>
</Button>

<!-- 右键菜单项前缀图标（替换现有 Text="✎"/"◎" 等） -->
<MenuItem Header="搜索..." InputGestureText="Ctrl+`">
    <MenuItem.Icon>
        <ContentControl Template="{StaticResource IconTemplate}"
                        Tag="{StaticResource IconSearch}"
                        Width="14" Height="14"
                        Foreground="{DynamicResource TextSecondaryBrush}"/>
    </MenuItem.Icon>
</MenuItem>
```

---

## 4 · 文件级改动清单（Claude Code 按顺序执行）

> 每一步改完都能编译。顺序不要颠倒。

### ☐ 步骤 1 — 放入资产文件

```
cp handoff/xaml/AppLogo.xaml              src/DesktopFences.UI/Themes/
cp handoff/xaml/Icons.xaml                src/DesktopFences.UI/Themes/
cp handoff/xaml/FileTypes.xaml            src/DesktopFences.UI/Themes/
cp handoff/xaml/FileKindToIconConverter.cs src/DesktopFences.UI/Converters/
```

`.csproj` 中 `Themes/*.xaml` 若有 `<Page>` 通配则不用改；若显式列举，追加三项 `<Page Update="Themes\...xaml">`。

`FileKindToIconConverter.cs` 的命名空间是 `DesktopFences.UI.Converters`，若你项目里现有转换器在别处，改 namespace。

### ☐ 步骤 2 — 重新生成 app.ico

见 2.2.1。结果文件路径：

```
src/DesktopFences.App/Assets/app.ico
```

构建时 `dotnet build DesktopFences.sln`，用资源监视器确认 `.exe` 输出含有新图标。

### ☐ 步骤 3 — 在 App.xaml 合并两个字典

文件：`src/DesktopFences.App/App.xaml`
见 2.2.2 代码片段。

### ☐ 步骤 4 — 替换 `FencePanel.xaml` 中的占位字符

定位 `src/DesktopFences.UI/Controls/FencePanel.xaml`，查找下列内容并替换：

| 原 | 改为 |
|---|---|
| 标题栏折叠按钮（可能是 `▲` / `▴` Text） | `<ContentControl Template="{StaticResource IconTemplate}" Tag="{StaticResource IconRollup}" Width="14" Height="14"/>` |
| 标题栏设置/菜单入口（可能是 `⋯` / `⚙`） | `{StaticResource IconSettings}`，大小 14 |
| Tab 条上的"+"按钮 | `{StaticResource IconAdd}`，大小 14 |
| Fence 关闭按钮（若有） | `{StaticResource IconClose}`，大小 14 |

**所有图标按钮**统一套 `Style="{StaticResource DarkIconButtonStyle}"` 获得悬停效果。

### ☐ 步骤 5 — 替换 `SettingsWindow.xaml` 的标题栏和侧栏

文件：`src/DesktopFences.UI/Controls/SettingsWindow.xaml`

**标题栏左上 Logo**（当前可能是 emoji 或 Text）：
```xml
<Image Source="{StaticResource AppLogoImage}" Width="14" Height="14" Margin="16,0,8,0"
       VerticalAlignment="Center"/>
```

**标题栏右上三个按钮**：使用 `CaptionButtonStyle` / `CaptionCloseButtonStyle`：
```xml
<StackPanel Orientation="Horizontal" HorizontalAlignment="Right">
    <Button Style="{StaticResource CaptionButtonStyle}" Click="OnMinimize">
        <ContentControl Template="{StaticResource IconTemplate}" Tag="{StaticResource IconMin}" Width="14" Height="14"/>
    </Button>
    <Button Style="{StaticResource CaptionButtonStyle}" Click="OnMaximize">
        <ContentControl Template="{StaticResource IconTemplate}" Tag="{StaticResource IconMax}" Width="12" Height="12"/>
    </Button>
    <Button Style="{StaticResource CaptionCloseButtonStyle}" Click="OnClose">
        <ContentControl Template="{StaticResource IconTemplate}" Tag="{StaticResource IconClose}" Width="14" Height="14"/>
    </Button>
</StackPanel>
```

**侧栏导航项**：当前 ViewModel 若有 `Icon` 字段（emoji 字符），将类型改为 `Geometry`，并在 ViewModel 构造函数中赋 `StaticResource`；或直接在 XAML 里 switch：

```xml
<ItemsControl ItemsSource="{Binding NavItems}">
    <ItemsControl.ItemTemplate>
        <DataTemplate>
            <RadioButton Style="{StaticResource NavRadioStyle}" IsChecked="{Binding IsActive}">
                <StackPanel Orientation="Horizontal">
                    <ContentControl Template="{StaticResource IconTemplate}"
                                    Tag="{Binding IconGeometry}"
                                    Width="16" Height="16" Margin="0,0,10,0"/>
                    <TextBlock Text="{Binding Label}"/>
                </StackPanel>
            </RadioButton>
        </DataTemplate>
    </ItemsControl.ItemTemplate>
</ItemsControl>
```

ViewModel 里映射表（建议写成静态常量）：

```csharp
public static readonly Dictionary<SettingsTab, string> NavIconKeys = new()
{
    [SettingsTab.General]     = "IconSettings",
    [SettingsTab.Appearance]  = "IconTheme",
    [SettingsTab.Rules]       = "IconRule",
    [SettingsTab.Fences]      = "IconGrid",
    [SettingsTab.Shortcuts]   = "IconKeyboard",
    [SettingsTab.About]       = "IconInfo",
};
```

### ☐ 步骤 6 — 替换 `SearchWindow.xaml`

文件：`src/DesktopFences.UI/Controls/SearchWindow.xaml`
- 搜索框前缀图标：`{StaticResource IconSearch}` Width="16"
- 空态图标：`<Image Source="{StaticResource AppLogoImage}" Width="48" Height="48" Opacity="0.55"/>`

### ☐ 步骤 7 — 替换 `RuleEditorWindow.xaml` 标题栏关闭按钮

用 `CaptionCloseButtonStyle`（同步骤 5）。

### ☐ 步骤 8 — 替换右键菜单（ContextMenu）的 MenuItem.Icon

排查所有 `new ContextMenu` 代码位 + XAML `<ContextMenu>`。当前 `src/app.jsx` 原型中使用的 emoji / 符号对照表：

| 原 | 替换为资源 Key |
|---|---|
| `✎` 重命名 | (不替换，保留编辑字符) |
| `+` 新建标签页 | `IconAdd` |
| `📁` 设为文件夹映射 | `IconPortal` |
| `▴` 展开/折叠 | `IconRollup` |
| `🎨` 主题颜色 | `IconTheme` |
| `◎` 显示/隐藏 | `IconHide` |
| `◉` Peek 桌面 | `IconPeek` |
| `⌕` 搜索 | `IconSearch` |
| `⚙` 设置/分类规则 | `IconSettings` / `IconRule` |
| `×` / 删除 | `IconTrash` |

### ☐ 步骤 9 — 托盘图标

检查 `src/DesktopFences.App/App.xaml.cs` 中 NotifyIcon 设置。如果是用 `app.ico`，步骤 2 已完成。如果是手动传 16×16 PNG，改为从 `AppLogoImage` DrawingImage 渲染到 Bitmap：

```csharp
private static Icon DrawingImageToIcon(DrawingImage img, int size)
{
    var visual = new DrawingVisual();
    using (var ctx = visual.RenderOpen())
    {
        ctx.DrawImage(img, new Rect(0, 0, size, size));
    }
    var rtb = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
    rtb.Render(visual);
    var enc = new PngBitmapEncoder();
    enc.Frames.Add(BitmapFrame.Create(rtb));
    using var ms = new MemoryStream();
    enc.Save(ms);
    ms.Position = 0;
    using var bmp = new Bitmap(ms);
    return Icon.FromHandle(bmp.GetHicon());
}
```

然后 `notifyIcon.Icon = DrawingImageToIcon((DrawingImage)Application.Current.Resources["AppLogoImage"], 32);`

### ☐ 步骤 10 — Fence 中的文件图标（新）

在 `FencePanel.xaml` 的文件列表 `ItemTemplate` 里，用 Grid 叠 Image + TextBlock（因为 XAML 的 GlyphRun 不好维护，字母标签走 TextBlock 更灵活）：

```xml
<UserControl.Resources>
    <conv:FileKindToIconConverter x:Key="KindToIcon"/>
    <conv:LabelLenToFontSizeConverter x:Key="LabelToSize"/>
</UserControl.Resources>

<DataTemplate x:Key="FileItemTemplate">
    <StackPanel Orientation="Horizontal">
        <Grid Width="24" Height="24" Margin="0,0,8,0">
            <Image Source="{Binding Extension, Converter={StaticResource KindToIcon}}"/>
            <TextBlock Text="{Binding KindLabel}"
                       Foreground="White" FontWeight="Bold"
                       FontFamily="Segoe UI"
                       FontSize="{Binding KindLabel, Converter={StaticResource LabelToSize}}"
                       HorizontalAlignment="Center" VerticalAlignment="Bottom"
                       Margin="0,0,0,3"/>
        </Grid>
        <TextBlock Text="{Binding FileName}" VerticalAlignment="Center"
                   Foreground="{DynamicResource TextPrimaryBrush}"/>
    </StackPanel>
</DataTemplate>
```

**ViewModel 改动**：给文件项添加 `Extension`（`.pdf` / `.docx` 等）和 `KindLabel` 两个属性。`KindLabel` 对照表：

```csharp
private static readonly Dictionary<string, string> ExtToLabel = new(StringComparer.OrdinalIgnoreCase)
{
    [".doc"] = "W", [".docx"] = "W", [".rtf"] = "W",
    [".xls"] = "X", [".xlsx"] = "X", [".csv"] = "X",
    [".ppt"] = "P", [".pptx"] = "P",
    [".pdf"] = "PDF",
    [".png"] = "IMG", [".jpg"] = "IMG", [".jpeg"] = "IMG", [".gif"] = "IMG", [".bmp"] = "IMG", [".webp"] = "IMG",
    [".mp4"] = "MP4", [".mov"] = "MP4", [".mkv"] = "MP4", [".avi"] = "MP4",
    [".mp3"] = "♪", [".wav"] = "♪", [".flac"] = "♪", [".m4a"] = "♪",
    [".cs"] = "<>", [".js"] = "<>", [".ts"] = "<>", [".jsx"] = "<>", [".tsx"] = "<>",
    [".py"] = "<>", [".go"] = "<>", [".rs"] = "<>", [".json"] = "<>", [".xml"] = "<>",
    [".zip"] = "ZIP", [".7z"] = "ZIP", [".rar"] = "ZIP",
    [".exe"] = "EXE", [".msi"] = "EXE", [".dll"] = "EXE",
    [".txt"] = "TXT", [".md"] = "TXT", [".log"] = "TXT",
    [".lnk"] = "↗", [".url"] = "↗",
    [".ttf"] = "Aa", [".otf"] = "Aa", [".woff"] = "Aa",
};
```

`LabelLenToFontSizeConverter` 逻辑：`length <= 2 → 8.5` ，`length > 2 → 7` ，`length == 0（文件夹）→ 0`。

---

### ☐ 步骤 11 — 验证

```powershell
dotnet build DesktopFences.sln -c Release
dotnet run --project src/DesktopFences.App
dotnet test tests/DesktopFences.Core.Tests       # 保证未破坏现有 61 个测试
```

视觉验证清单：
- [ ] 任务栏图标为新四宫格 Logo
- [ ] 设置窗标题栏左上角显示 Logo，右上角关闭按钮在悬停时变红
- [ ] 所有 Fence 上的折叠/设置按钮为细线条风格，悬停背景为 `HoverBrush`
- [ ] 右键菜单前缀图标整齐对齐（不再是 emoji 宽度不一致）
- [ ] 切换深色主题下图标跟随 `TextSecondaryBrush` 颜色变化

---

## 5 · 主题色扩展（可选，未来功能）

如果后续要支持"用户自定义主题色"：

1. `DarkTheme.xaml` 里把 `AccentColor` 改为 `DynamicResource`
2. `AppLogo.xaml` 顶部的 `AppLogoTopColor / AppLogoBottomColor` 改为根据 accent 动态计算（WPF 里可以用 `ValueConverter`）
3. Icons.xaml 无需改动（线条已跟随 `Foreground`）

暂不实施，仅保留可扩展性。

---

## 6 · 约束与注意事项

- **不要修改 `DesktopFences.Core` / `DesktopFences.Shell`**：本次只涉及 UI 层
- **不要动 `DarkTheme.xaml` 的现有 brush key**：新资源只添加，不覆盖
- **右键菜单图标**必须用 `ContentControl + IconTemplate`，**不要**用 `<Path>` 直接写 — 否则 Foreground 继承链会断
- 任何 `Width/Height` 建议使用偶数（14/16/18/20），避免 1.5px 对齐模糊
- 落地完成后按 `CLAUDE.md` 的约定，将本次变更追加到 `docs/DESIGN.md`

---

## 7 · 附录：快速映射表（Prototype ↔ WPF）

| Prototype (HTML) | WPF |
|---|---|
| `<svg><use href="#icon-app-logo"/></svg>` | `<Image Source="{StaticResource AppLogoImage}"/>` |
| `<svg><use href="#ic-search"/></svg>` | `<ContentControl Template="{StaticResource IconTemplate}" Tag="{StaticResource IconSearch}"/>` |
| `.ts-btn` CSS class | `Style="{StaticResource DarkIconButtonStyle}"` |
| `.sw-wc` caption button | `Style="{StaticResource CaptionButtonStyle}"` |
| `.sw-wc.close` 关闭按钮 | `Style="{StaticResource CaptionCloseButtonStyle}"` |
| `color: var(--text-dim)` | `Foreground="{DynamicResource TextSecondaryBrush}"` |
| 悬停 `background: rgba(255,255,255,0.1)` | `HoverBrush` (已在 DarkTheme.xaml) |

---

**本文档足以独立完成落地，无需再回问任何设计细节。如遇 XAML 命名空间冲突，统一用 `xmlns:ui="clr-namespace:DesktopFences.UI;assembly=DesktopFences.UI"`。**
