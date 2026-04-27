# DesktopFences 图标交接包 · 快速开始

本目录是 Claude Code 的落地包。**只需告诉 Claude Code：**
> 按照 `HANDOFF.md` 执行 10 步落地流程。

## 📋 清单

```
HANDOFF.md                        ← 主指令（10 步 · 每步可编译通过）
README.md                         ← 你正在读的文件
preview.html                      ← 在浏览器打开即可预览所有图标
icons/
  ├─ app-logo.svg                 主 Logo · 48×48 viewBox · 渐变/高光/四宫格
  ├─ app-logo-mono.svg            主 Logo 单色版 · 托盘用
  ├─ actions.sprite.svg           20 个操作图标（搜索/设置/折叠/…）
  └─ file-types.sprite.svg        14 个文件类型图标（文件夹/doc/pdf/…）
xaml/
  ├─ AppLogo.xaml                 主 Logo 的 DrawingImage 资源
  ├─ Icons.xaml                   20 个 Geometry + IconTemplate + 按钮样式
  ├─ FileTypes.xaml               14 个文件类型的 DrawingImage 资源
  └─ FileKindToIconConverter.cs   扩展名 → 图标资源的值转换器
```

## ⚡ 三分钟上手

1. 把 `xaml/*.xaml` 拷进 `src/DesktopFences.UI/Themes/`
2. 把 `FileKindToIconConverter.cs` 拷进 `src/DesktopFences.UI/Converters/`
3. `App.xaml` 合并四个字典（见 HANDOFF.md § 2.2.2，多一个 `FileTypes.xaml`）
4. 用 ImageMagick 从 `app-logo.svg` 生成 `app.ico` → 替换 `Assets/app.ico`
5. 按 HANDOFF.md 步骤 4–9 替换现有占位

## ✅ 先打开 preview.html 看一眼

`preview.html` 用浏览器打开即可，无需部署；你能看到所有图标的实际渲染效果，用于与最终 WPF 产出对比。

## ⚠️ XAML 中文字层的说明

`FileTypes.xaml` 中的文档图标**不含字母层**（W/X/P/PDF/MP4 等）。原因：`GlyphRun` 在 XAML 里需要字体 URI 和字形索引，维护成本高。

**推荐做法**：在 `FencePanel.xaml` 的 ItemTemplate 里，用 Grid 叠 Image + TextBlock：

```xml
<Grid Width="24" Height="24">
    <Image Source="{Binding Kind, Converter={StaticResource KindToIcon}}"/>
    <TextBlock Text="{Binding KindLabel}"
               Foreground="White"
               FontFamily="Segoe UI" FontWeight="Bold"
               FontSize="{Binding KindLabel, Converter={StaticResource LabelLenToFontSize}}"
               HorizontalAlignment="Center" VerticalAlignment="Bottom"
               Margin="0,0,0,4"/>
</Grid>
```

`KindLabel` 对照表（在 ViewModel 里定义）：

| Kind | Label |
|------|-------|
| doc  | W     |
| xls  | X     |
| ppt  | P     |
| pdf  | PDF   |
| img  | IMG   |
| video| MP4   |
| music| ♪     |
| code | <>    |
| zip  | ZIP   |
| exe  | EXE   |
| txt  | TXT   |
| link | ↗     |
| ttf  | Aa    |

`LabelLenToFontSize` 转换器：`length <= 2 → 8.5`，`length > 2 → 7`。

## 🎨 想换主色？

只要改 `AppLogo.xaml` 顶部两行：
```xml
<Color x:Key="AppLogoTopColor">#7AA7E6</Color>
<Color x:Key="AppLogoBottomColor">#3D58B1</Color>
```

操作图标自动跟随 `Foreground` —— 在 `DarkTheme.xaml` 改 `TextSecondaryBrush` 即可。

文件类型图标的色调是语义固定的（PDF=红，Excel=绿…），**不要**统一改，会破坏识别性。

---

有疑问先看 `HANDOFF.md`，它涵盖了所有边界情况。
