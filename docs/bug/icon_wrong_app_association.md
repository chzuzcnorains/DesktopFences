# 文件图标显示与系统关联不一致

## 问题描述

桌面 fence 面板中 `.docx` 文件显示的是 Microsoft Word 风格的红色 W 图标,而用户系统已经把 WPS Office 设为 `.docx` 默认打开方式,期望看到 WPS 注册的蓝色 W 图标。

## 问题根源

这其实是两个独立问题叠加,需要分别修复:

### 问题 A — Shell 抽图链路质量差

`ShellIconExtractor` 用的是:

```csharp
SHGetFileInfo(..., SHGFI_ICON | SHGFI_LARGEICON, ...)
```

`SHGFI_LARGEICON` 等价于 `SHIL_LARGE` (32×32),被 WPF 拉到 48 DIP / 72 物理像素 (150% DPI) 后明显模糊;而且 32 px image list 的资源往往是系统从更大尺寸自动降采样得到的,质量和风格都不如 256 px 的原图。所以即使切到 Shell 风格,看到的图也是糊的旧图。

### 问题 B — 用户实际在 System 风格,根本不走 Shell

用户的 `settings.json` 里 `"IconStyle": "System"`。System 风格走 `SystemFileTile` 模板 + `SystemFileKindToIconConverter`,后者把扩展名硬映射到 `Themes/SystemFileTypes.xaml` 里**手绘**的 page+fold 图(按 office 类型上不同徽章色)——**完全不读系统的文件关联**。所以无论 WPS 还是 MS Word 装没装,显示的永远是这套手绘图。

按设计文档 `docs/design/icon-styles.md` Phase 12 决策,Shell 风格当时被刻意藏起来,理由是抽图模糊 + 异步加载闪烁。

## 修复方案

两段式修复:

### 修复 A — 改用 IShellItemImageFactory

`Shell/Desktop/ShellIconExtractor.ExtractIcon` 主路径改为:

```csharp
SHCreateItemFromParsingName(path, ..., IID_IShellItemImageFactory, out item);
((IShellItemImageFactory)item).GetImage(new SIZE(96, 96),
    SIIGBF.IconOnly | SIIGBF.BiggerSizeOk, out IntPtr hbitmap);
// CreateBitmapSourceFromHBitmap → BitmapSource
DeleteObject(hbitmap);
```

要点:

- 这是 Vista+ 微软推荐 API,与 Windows 资源管理器走同一套图标解析链路,**自动反映已安装应用的关联**
- 请求 96×96:在所有常见 DPI 档位 (100%–200%) WPF 都只做 downscale,源图永远 ≥ 目标物理像素,清晰度有保证
- `SIIGBF.IconOnly` 关闭 thumbnail (我们按扩展名缓存,thumbnail 会全部撞 key)
- `SIIGBF.BiggerSizeOk` 让 shell 自由返回更大原图,我们再缩
- 路径不存在时回退到原本的 `SHGetFileInfo + HICON` 兜底(因为 `SHCreateItemFromParsingName` 不接受不存在的路径,但 `SHGFI_USEFILEATTRIBUTES` 可以按扩展名查)

新增 P/Invoke 在 `Interop/NativeMethods.cs`: `IShellItemImageFactory` COM 接口、`SIIGBF` flags、`SIZE` 结构、`IID_IShellItemImageFactory` GUID。

修复后未归档 overlay (`DesktopIconOverlay`,直接调 ShellIconExtractor) 立即变锐利且正确。

### 修复 B — 把 Shell 风格暴露到 UI

设计文档说"Shell 是隐藏 fallback,只能手编 settings.json"——但用户根本不知道有这选项。Phase 12 藏 Shell 的理由(抽图模糊)被修复 A 解决了,所以解禁:

1. **`AppearanceSettingsPane.xaml`** — 把 IconStyleGrid 从 2 列改 3 列;副标题 + 描述提到 Shell
2. **`AppearanceSettingsPane.xaml.cs`** — `IconStyleEntries` 加 `(FileIconStyle.Shell, "Shell", "Explorer 真实系统图标")`;删掉 `Load()` 里 `IconStyle == Shell ? App : ...` 的 clamp
3. **`FencePanel.xaml.cs::BuildIconStyleSubmenu`** — 加 `AddChoice(..., "Shell 真实", FileIconStyle.Shell, ...)`,fence 标题栏菜单也能按 fence 切

不需要数据迁移 (FileIconStyle 枚举本来就是 3 个值,旧 settings.json 已能存 Shell)。

## 修复效果

- ✅ 切到 Shell 风格后,`.docx` 显示 WPS 注册的高分辨率蓝色 W,与 Explorer 完全一致
- ✅ 各文件类型自动跟随系统已安装应用 (PDF / 图片 / 视频 / 自定义 ICO 关联程序)
- ✅ 高 DPI 显示器下不再模糊 (96 px 源 → 48–96 px 目标只做 downscale)
- ✅ 外观设置三卡 picker 可视化切换,fence 右键菜单也可按 fence 覆盖
- ✅ 默认风格仍为 App (老用户升级行为不变)

## 经验总结

1. **看到"图标不对"先分清两层**:① 抽图 API 对不对 (Shell 抽图链路) ② 模板有没有走 Shell 抽图 (Phase 12 引入的 picker)。两个都对了显示才对。
2. **`IShellItemImageFactory > SHGetImageList(SHIL_JUMBO) > SHGetFileInfo(SHGFI_LARGEICON)`**:JUMBO 看似分辨率高,但许多文件类型在 256 image list 里是带透明 padding 的小图,缩到目标尺寸内容只占很小一块更糊;而 IShellItemImageFactory 让 shell 自己决定从哪个资源取并缩多少,处理 padding/overlay 逻辑健壮。
3. **"隐藏 fallback"是反 UX 的**:藏起来用户找不到、问的时候要靠开发者指点 settings.json。一旦底层质量问题修了,就该把选项暴露出来。设计文档也要同步把"为什么藏"的决策更新成"为什么解禁"。

## 相关文件

- `src/DesktopFences.Shell/Desktop/ShellIconExtractor.cs` (修复 A)
- `src/DesktopFences.Shell/Interop/NativeMethods.cs` (新 COM 声明)
- `src/DesktopFences.UI/Controls/Settings/AppearanceSettingsPane.xaml` + `.xaml.cs` (修复 B picker)
- `src/DesktopFences.UI/Controls/FencePanel.xaml.cs` (修复 B 菜单)
- `docs/design/icon-styles.md` (设计文档同步)
- 关联 bug:[icon_blurry.md](icon_blurry.md) (Phase 11 时期的 DPI/渲染层修复)
