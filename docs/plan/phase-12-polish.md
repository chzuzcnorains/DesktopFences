# Phase 12 Polish：Shell 抽图现代化 + Shell 风格解禁

**目标**:Phase 12 引入了 App / System / Shell 三种 IconStyle,但因为 Shell 抽图模糊只暴露 App / System 两卡。Phase 12 Polish 用 `IShellItemImageFactory` 把 Shell 抽图链路升级到 Explorer 同款,然后把 Shell 解禁为 UI 一等选项。触发于 2026-05-09 用户 bug 反馈:`.docx` 显示 MS Word 红 W,期望已设默认的 WPS 蓝 W。

## 12.P.1 背景

Phase 12 决策记录 #2 ([icon-styles.md](../design/icon-styles.md)) 把 Shell 风格藏起来,理由是:

1. `SHGetFileInfo + SHGFI_LARGEICON` 抽 32×32 图,被 WPF 拉到高 DPI 物理像素后明显模糊
2. 50+ 文件 fence 上的异步加载会闪烁

但 2026-05-09 用户报 bug:fence 面板显示的 .docx 是红色 MS Word 风格,期望是已装 WPS 注册的蓝色 W。**根因有两层**:

- **抽图层**:`SHGetFileInfo` 32×32 图含义模糊不清晰,且某些文件类型在小尺寸 image list 里的资源跟大尺寸版本风格不一致
- **模板层**:用户在 System 风格,走 `SystemFileKindToIconConverter` 硬映射到手绘 page+fold,**完全不读系统关联**,所以无论装什么,显示永远不变

试错记录:第一次试 `SHGetImageList(SHIL_JUMBO)` 取 256×256,反而**整体更糊**——许多文件类型在 jumbo image list 里只有透明 padding 包裹的小图,缩到 72px 后实际内容只占小一块。用户反馈"系统模糊,方向错了",立即还原。

## 12.P.2 关键决策

### 12.P.2.1 任务 A:`ShellIconExtractor` 改用 `IShellItemImageFactory::GetImage`

- 主路径:`SHCreateItemFromParsingName(IID_IShellItemImageFactory)` → `GetImage(SIZE(96, 96), SIIGBF.IconOnly | SIIGBF.BiggerSizeOk)` → `CreateBitmapSourceFromHBitmap`
- 兜底链:路径不存在 → 退回原 `SHGetFileInfo + HICON + SHGFI_USEFILEATTRIBUTES`(因为 `SHCreateItemFromParsingName` 不接受不存在的路径,但按扩展名查仍需要)
- 请求 96×96 的考量:在所有常见 DPI 档位(100%–200%)WPF 都只 downscale 不 upscale(48 / 60 / 72 / 84 / 96 物理像素 ← 96 源),downscale 是天然清晰的
- `SIIGBF.IconOnly`:关闭 thumbnail——我们按扩展名缓存,thumbnail 会全部撞 key,且与现有 cache 行为一致
- `SIIGBF.BiggerSizeOk`:让 shell 自由返回更大原图,我们再缩
- 资源管理:`Marshal.ReleaseComObject(item)` 释放 COM 引用,`DeleteObject(hbitmap)` 释放 GDI 句柄

新增 P/Invoke 集中在 [NativeMethods.cs:225-263](../../src/DesktopFences.Shell/Interop/NativeMethods.cs#L225-L263):`IShellItemImageFactory` COM 接口、`SIIGBF` flags、`SIZE` 结构、`IID_IShellItemImageFactory` GUID。

### 12.P.2.2 任务 B:`AppearanceSettingsPane` 加 Shell 卡片

- `IconStyleEntries` 加第三项 `(FileIconStyle.Shell, "Shell", "Explorer 真实系统图标")`
- XAML `IconStyleGrid` `Columns="2"` → `Columns="3"`,卡片样式 `TabStyleTileStyle` 不变(MinHeight=64 在 3 卡布局下仍合适)
- 副标题文案从"App + System"扩展为"App + System + Shell"
- 删除 `Load()` 里的 clamp `_iconStyle = s.IconStyle == FileIconStyle.Shell ? FileIconStyle.App : s.IconStyle;`,直接 `_iconStyle = s.IconStyle`

### 12.P.2.3 任务 C:`FencePanel` 标题菜单加 Shell 选项

- `BuildIconStyleSubmenu` 从 3 项扩展到 4 项:`跟随全局 / App 自绘 / System 经典 / Shell 真实`
- Phase 13 的 visual-tree 上溯 + `IconStyleOverride` 双向同步逻辑无需改动
- XML doc 注释更新,删除"Shell intentionally not exposed here"

### 12.P.2.4 任务 D:设计文档同步

- [icon-styles.md](../design/icon-styles.md) 更新:
  - "目标"段:三风格平等,不再说 Shell 是 fallback
  - "三种风格"表格:Shell 行从 `SHGetFileInfo` 改为 `IShellItemImageFactory::GetImage`,补充"自动反映已安装应用的关联(如 WPS 的 .docx 蓝图标)"
  - "UI 入口":picker 从 2 卡改 3 卡,删 clamp 描述
  - "决策记录 #2":从"为什么 Shell 是 hidden fallback"改写为"为什么后来把 Shell 暴露到 UI",记录抽图升级 + 用户诉求双驱动
  - "Phase 13 菜单表":加 Shell 真实 行,删"Shell 不暴露"

## 12.P.3 实施顺序

按"先底层后 UI 后文档"顺序,3 步:

1. **任务 A** — `ShellIconExtractor` + `NativeMethods` 新声明。先在 overlay 验证抽图清晰度(unfenced icon 直接走 extractor,与 picker 解耦,可单独测)
2. **任务 B + C** — UI 解禁。任务 A 验证通过后才有意义暴露 Shell
3. **任务 D** — 设计文档同步,bug 修复文档收尾

试错插曲(`SHGetImageList(SHIL_JUMBO)` 失败)夹在任务 A 之前,被还原后重走。

## 12.P.4 验证

- `dotnet build -c Debug` Shell + UI 项目:0 错 0 警(完整 sln build 因 App.exe 运行中导致 dll 复制锁,代码编译已验证)
- `dotnet test`:66 个单测全部通过
- 视觉验证(用户手动):
  - 未归档 overlay icon 切到 IShellItemImageFactory 后立即变锐利且正确
  - Shell 风格 picker 三卡可切换
  - .docx 在 Shell 风格下显示 WPS 蓝 W

## 12.P.5 经验沉淀

1. **看到"图标不对"先分清两层**:抽图 API 对不对 vs 模板有没有走 Shell 抽图。两个都对了显示才对——只动一层会"修了一处另一处仍然错"
2. **`IShellItemImageFactory > SHGetImageList(SHIL_JUMBO) > SHGetFileInfo(SHGFI_LARGEICON)`**:JUMBO 看似分辨率高,但许多文件类型在 256 image list 里是带透明 padding 的小图,缩到目标尺寸内容只占很小一块更糊;IShellItemImageFactory 让 shell 自己决定从哪个资源取并缩多少,处理 padding/overlay 健壮
3. **"隐藏 fallback"是反 UX 的**:藏起来用户找不到、问的时候要靠开发者指点 settings.json。一旦底层质量问题修了,就该把选项暴露出来。设计文档也要同步把"为什么藏"的决策更新成"为什么解禁",避免未来读者困惑
4. **方向回滚很重要**:第一次试 SHIL_JUMBO 失败后果断还原,而不是叠 patch 强救。用户的"方向错了"是关键信号

## 12.P.6 关联文档

- 修复文档:[docs/bug/icon_wrong_app_association.md](../bug/icon_wrong_app_association.md)
- 设计文档:[docs/design/icon-styles.md](../design/icon-styles.md)(已同步)
- 相关历史 bug:[docs/bug/icon_blurry.md](../bug/icon_blurry.md)(Phase 11 时期 DPI/渲染层修复)
- 上游 Phase:[docs/plan/phase-12.md](phase-12.md) / [docs/plan/phase-13.md](phase-13.md)
