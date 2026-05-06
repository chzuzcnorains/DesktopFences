# DesktopFences · Claude Code 工作交接手册

> **交接对象**：Claude Code（接手 WPF 实现的 AI 工程师）
> **本文档作用**：让 Claude Code 在 **5 分钟内** 理解项目全貌、找到所有需要的资料、知道下一步做什么。
> **版本**：v1 · 2026-05-06
> **作者约定**：所有路径相对仓库根目录。中文为主，关键术语保留英文。

---

## 0 · 一句话项目说明

**DesktopFences** 是一个 Windows 11 桌面整理工具（WPF + .NET），灵感来自经典桌面分区类应用：把桌面图标按规则收纳进可拖动、可折叠、可标签化的"围栏"（Fence）面板里。

当前阶段：**设计交付完成 + WPF 落地中**。本仓库是设计与原型仓库，落地代码在 `src/DesktopFences.*` 项目里（不在本仓库 — 由 Claude Code 维护）。

---

## 1 · 仓库地图（你只需要看这些）

```
DesktopFences/
├── CLAUDE_CODE_HANDOFF.md     ← 你正在读 · 入口
│
├── desktop-v2.html            ★ 交互原型（最新 · 唯一权威）
├── src/                       ★ 原型源码（React / JSX，仅供 desktop-v2.html 引用）
│   ├── data.jsx               · seed 数据 + 图标颜色映射 + 扩展名对照
│   ├── system-icons.jsx       · Windows 经典风格自绘文件图标
│   ├── fence.jsx              · Fence 面板 + Tab + 文件 tile 组件
│   └── app.jsx                · 主应用：状态、右键菜单、设置/搜索窗、Tweaks
│
├── handoff/                   ★ 图标系统交接包（已就绪 · 直接落地）
│   ├── HANDOFF.md             · 10 步落地指令（权威）
│   ├── README.md              · 三分钟上手
│   ├── preview.html           · 浏览器预览所有图标
│   ├── icons/                 · SVG 矢量源（app-logo / actions / file-types）
│   └── xaml/                  · WPF 资源字典 + 转换器
│
└── uploads/                   · 用户提供的参考截图 · 仅供参考，不落地
```

**只有以上 10 个文件（夹）你需要关心。** 其他历史文件已清理。

---

## 2 · 你的工作分两块

### 块 A · 图标系统落地（高优先 · 已 100% 设计就绪）

➡ 直接看 [`handoff/HANDOFF.md`](handoff/HANDOFF.md)。
该文档包含 **10 个可独立编译通过** 的步骤，每步写明文件、行为、代码片段。完成后任务栏图标、所有窗口标题栏、右键菜单前缀、Fence 折叠按钮等全部切换到新图标系统。

预估工作量：1 个 PR · 2–3 小时（含 ImageMagick 生成 ICO）。

### 块 B · 主原型功能落地（迭代式）

➡ 看本文件第 3–7 节。下面是把原型 `desktop-v2.html` 的交互移植到 WPF 的方案与映射表。

---

## 3 · 原型快速上手

```bash
# 浏览器直接打开（无需构建）
open desktop-v2.html
```

主要交互（用于和 WPF 行为对照）：

| 操作 | 原型行为 | WPF 落点 |
|---|---|---|
| 拖动 Fence 标题栏 | Fence 跟随移动 | `FencePanel` `MouseDown` + DWM Hit-test |
| 拖动一个 Fence 重叠到另一个 | 高亮目标 → 松手合并为标签页 | `FenceWindow.OnDragMove` + 重叠区域 ≥35% 时显示 ghost |
| Fence 标题栏右上 ▴ | 折叠/展开（仅留 34px 标题栏） | `IsRolled` 属性 + 高度动画 |
| 右键 Fence / Tab / 文件 / 桌面 | 弹出 ContextMenu | WPF `ContextMenu` + `MenuItem` |
| `Ctrl+\`` | 打开搜索窗 | 全局热键 `RegisterHotKey` |
| `Alt+Space`（模拟 `Win+Space`） | Peek 桌面（半透明所有 Fence） | 所有 `FenceWindow` 同步降低不透明度 |
| 双击空白桌面 | 显示/隐藏所有 Fence | Shell 钩子检测桌面双击 |
| Tweaks 面板（右下角） | 实时改主题色/透明度/模糊… | 见 §5 |

---

## 4 · 设计 token 一览（WPF 落地必查）

原型 CSS 变量 ↔ `DarkTheme.xaml` brush 映射。新增变量请在 `DarkTheme.xaml` 顶部以注释分组。

| 原型 CSS 变量 | 默认值 | 含义 | WPF 资源 Key 建议 |
|---|---|---|---|
| `--bg-0` | `#0b1220` | 桌面背景深色基底 | （壁纸由系统提供，不落地） |
| `--fence-bg` | `oklch(20% 0.04 228 / 0.74)` | Fence 面板亚克力背景 | `FenceBackgroundBrush` |
| `--fence-border` | `rgba(255,255,255,0.09)` | Fence 边框 | `FenceBorderBrush` |
| `--titlebar-bg` | `rgba(255,255,255,0.04)` | 标题栏未激活底色 | `TitlebarInactiveBrush` |
| `--titlebar-active` | `oklch(55% 0.12 248 / 0.35)` | 标题栏激活底色 | `TitlebarActiveBrush` |
| `--text` | `#e8ecf4` | 主要文本 | `TextPrimaryBrush` ✅（已存在） |
| `--text-dim` | `#aeb8cc` | 次要文本/图标默认色 | `TextSecondaryBrush` ✅ |
| `--text-faint` | `#7b8296` | 弱化标签 | `TextTertiaryBrush` |
| `--accent` | `oklch(72% 0.12 248)` | 主题色（用户可调） | `AccentBrush` ✅ |
| `--accent-2` | `oklch(72% 0.12 30)` | 警告/合并提示 | `AccentSecondaryBrush` |
| `--danger` | `oklch(68% 0.17 25)` | 删除/关闭红 | `DangerBrush` |
| `--blur` | `36px` | Acrylic 模糊半径 | DWM `BlurBehind` 强度（用户可调） |
| `--icon-size` | `32px` | Fence 内图标尺寸 | `IconSize`（DependencyProperty） |
| `--tile-w` / `--tile-h` | `76 / 84` | 文件 tile 尺寸 | 由 `IconSize` 派生：`tile=max(72, IconSize+44)` |

**Tab 样式变体**（`data-tabstyle` 属性 → 4 选 1 · 用户可调）：

| 值 | 描述 | WPF 实现 |
|---|---|---|
| `flat` | 下划线激活，最低调 | `TabItemStyle.Flat` |
| `segmented` | 分段控件感，激活有底色 | `TabItemStyle.Segmented` |
| `rounded` | 顶部圆角胶囊 | `TabItemStyle.Rounded` |
| `menuOnly` | 只显示标签文字 + 下划线（默认） | `TabItemStyle.MenuOnly` |

`handoff/xaml/` 内 `TabStyles.xaml` 已实现前 3 种；如果还没有 menuOnly，参考原型 CSS `[data-tabstyle="menuOnly"] .tab` 段补一个。

---

## 5 · 用户可调项（Tweaks）落地清单

原型把这些放在右下角浮窗里实时调；WPF 落地到 **设置窗 → 外观** 标签页。所有值 **持久化到 `%APPDATA%\DesktopFences\settings.json`**。

| Key | 类型 | 范围 | 默认 | 影响 |
|---|---|---|---|---|
| `accent` | int (hue 度) | 0–360 | 248 | `--accent` 色相 |
| `bgHue` | int (hue 度) | 0–360 | 248 | Fence 背景色相 |
| `opacity` | float | 0.2–0.9 | 0.55 | Fence 不透明度 |
| `blur` | int (px) | 0–60 | 26 | Acrylic 模糊半径 |
| `iconSize` | int (px) | 28–64 | 44 | Fence 内图标 + 衍生 tile |
| `tabStyle` | enum | flat/segmented/rounded/menuOnly | menuOnly | Tab 视觉风格 |
| `iconStyle` | enum | app/system | app | 文件图标使用 App 自绘还是系统 SHGetFileInfo |

---

## 6 · 数据模型（WPF C# 类骨架）

原型 seed 在 `src/data.jsx` 的 `INITIAL_FENCES` 里。WPF 落地建议如下（仅用作参考，最终以 `DesktopFences.Core` 现有模型为准）：

```csharp
public sealed class FenceModel
{
    public string Id { get; init; }                    // "f1", "f2", ...
    public string Title { get; set; }
    public double X { get; set; }
    public double Y { get; set; }
    public double Width { get; set; }
    public double Height { get; set; }
    public bool   IsRolled { get; set; }                // 折叠状态
    public string? FolderPortalPath { get; set; }       // 设为文件夹映射时填写
    public ObservableCollection<TabModel> Tabs { get; } = new();
    public int    ActiveTabIndex { get; set; }
    public DateTimeOffset? ClosedAt { get; set; }       // 非空 = 已关闭，存于 ClosedFences 列表
}

public sealed class TabModel
{
    public string Id { get; init; }                    // "t1a", ...
    public string Title { get; set; }
    public ObservableCollection<FileItem> Files { get; } = new();
}

public sealed class FileItem
{
    public string FullPath { get; init; }              // 真实路径（实现态）
    public string FileName => Path.GetFileName(FullPath);
    public string Extension => Path.GetExtension(FullPath).ToLowerInvariant();
    public string KindLabel => DesktopFences.UI.Converters.ExtToLabel(Extension); // PDF/W/X/<>/...
}
```

**最近关闭列表（`ClosedFences`）**：原型保留最近 N 个关闭的 Fence，放在桌面右键菜单 "恢复最近关闭" 子菜单里（最多显示 5，超出走"查看全部"打开设置窗）。WPF 落地建议保 20 个，存 `closed-fences.json`。

---

## 7 · 右键菜单内容（直接抄）

原型在 `src/app.jsx` 第 95–170 行已经把所有 4 类（file / tab / fence / desktop）右键菜单完整列出。WPF 直接 1:1 翻译即可。**图标 column 走 `MenuItem.Icon`**，符号映射详见 [`handoff/HANDOFF.md` § 步骤 8](handoff/HANDOFF.md)。

快捷键提示（`Ctrl+X` 等）：用 `MenuItem.InputGestureText`，**不要** 真的注册全局热键（仅显示）。

---

## 8 · 工作流约定

1. **看到不确定的视觉/行为**：先打开 `desktop-v2.html` 在浏览器里实际操作一遍，再下笔
2. **新增设计 token**：先在原型 CSS 里加一个 `--xx`，确认效果再同步到 `DarkTheme.xaml`
3. **改 `DarkTheme.xaml` 现有 brush key**：禁止 — 只增不改，避免破坏其他窗口
4. **不修改 `DesktopFences.Core` / `DesktopFences.Shell`**：除非你解决的是核心模型问题；纯 UI/视觉改动只动 UI 层
5. **每个 PR 不超过一个主题**：图标落地一个 PR，Tweaks 持久化一个 PR，Fence 合并交互一个 PR…
6. **每步可编译通过**：`dotnet build DesktopFences.sln` 必须绿。中间状态崩了就把改动拆得更小

---

## 9 · 推荐落地顺序

```
1. ✅ 图标系统      ← 走 handoff/HANDOFF.md（已设计就绪）
2. □ Tweaks 持久化   ← 让设置窗外观页真正生效（§5）
3. □ 右键菜单大改造  ← 4 类菜单 1:1 翻译（§7）
4. □ Fence 拖动合并   ← 重叠 ≥35% 高亮 + 松手合并标签
5. □ 关闭/恢复 Fence ← ClosedFences 列表 + 桌面右键子菜单
6. □ Peek 模式       ← Win+Space 全局热键 + 所有 Fence 同步
7. □ 全局搜索窗      ← Ctrl+` 打开 + 跨 Fence 文件名 fuzzy 匹配
8. □ 文件夹映射      ← 一个 Tab 绑定到真实文件夹，自动跟随增删
9. □ 分类规则引擎    ← 桌面新增文件按规则自动入 Fence
```

每一步完成后在本文件该项前打 ✅ 并提交。

---

## 10 · 常见坑

- **Acrylic 模糊**：WPF 上用 `SetWindowCompositionAttribute` + `ACCENT_ENABLE_ACRYLICBLURBEHIND`。Win11 下默认有色调污染；`AccentPolicy.GradientColor` 设成 `0x01_00_00_00`（几乎透明黑）+ 自己画一层半透明 fence-bg 可获得可控效果
- **拖动跨屏**：`Window.Left/Top` 在多 DPI 屏环境下可能错位；用 `LogicalToScreen` 转换或直接 `WindowInteropHelper.Handle` + `SetWindowPos`
- **右键菜单 Icon 缩放**：`MenuItem.Icon` 默认 16×16，自定义 ContentControl 时务必显式 `Width="14" Height="14"` 不然继承大小会糊
- **Foreground 链断裂**：原型操作图标依赖 `currentColor`；WPF 里务必走 `handoff/xaml/Icons.xaml` 提供的 `IconTemplate`（用 `TemplateBinding Foreground`），不要直接 `<Path Fill="...">`
- **localStorage 在原型里**：`df_fences_v1` / `df_tweaks_v1` / `df_closed_v1` 是浏览器持久化键名 — WPF 落地后无关，但读 seed 时如果浏览器有缓存会覆盖 `INITIAL_FENCES`，调试时清掉

---

## 11 · 你不需要做的事

- ❌ **不要** 重画图标 — 全部已在 `handoff/icons/` 里
- ❌ **不要** 改 `desktop-v2.html` 或 `src/*.jsx` — 它们是设计权威，仅由设计师维护
- ❌ **不要** 添加新的色彩或字体到设计系统 — 有诉求先和设计师对齐
- ❌ **不要** 把原型 React 状态机直接搬进 WPF — 用 MVVM + ObservableCollection 重新建模

---

## 12 · 提问入口

- 视觉/交互不一致：先比对原型 → 确认是 bug 还是新需求 → 提 issue 并 @设计师
- 资源缺失（缺图标、缺颜色、缺动画曲线）：在 `handoff/` 提 issue
- 架构疑问（应该放 Core 还是 UI）：开会，不要猜

---

**祝早日完工。如果你正在读这一行 — 现在打开 `handoff/HANDOFF.md`，开干。**
