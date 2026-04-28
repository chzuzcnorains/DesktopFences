# DesktopFences

Windows 桌面整理工具，对标 Stardock Fences。基于 C# / .NET 9 / WPF。
本项目用于验证 AI 复刻能力，**非商业使用**；如有侵权请联系作者。

## 功能

- **Fence 容器** — 桌面分区窗格管理文件，`Win+D` 后仍可见。
- **自动分类规则** — 按扩展名 / 路径 / 是否目录把新落到桌面的图标分流到目标 Fence。
- **拖放整理** — 文件拖入 / 拖出 Fence，自动同步桌面状态。
- **折叠 (Rollup)** — 双击标题栏卷起到只剩标题条；鼠标悬停可临时展开。
- **Peek (`Win+Space`)** — 一键将所有 Fence 抬到最上层供查看。
- **Snap 吸附** — 拖动 / 缩放时自动对齐其他 Fence 与屏幕边缘，按 Alt 关闭。
- **Tab 标签组** — Fence 重叠落入即合并为标签页；拖出标签恢复独立窗。
- **Folder Portal** — 将真实文件夹映射为 Fence，内容跟随文件系统变化。
- **多显示器** — 按显示器配置哈希持久化布局，分辨率切换自动还原。
- **桌面分页** — 数据层支持，当前由 Windows 虚拟桌面接管显示。
- **搜索 (`Ctrl+\``)** — 全文模糊搜索所有 Fence 内的文件。
- **布局快照 / 导入 / 导出** — 命名快照即时切换；`.dfences.json` 跨机迁移。
- **最近关闭恢复** — 误关 Fence 自动入栈，托盘菜单一键找回（FIFO ≤20）。
- **外观主题** — 6 套 Accent + 自定义 Hue / Opacity / Blur，Tab 样式 4 变体；预览实时反馈。

## 构建与运行

```bash
# 整套解决方案
dotnet build DesktopFences.sln

# 直接运行
dotnet run --project src/DesktopFences.App

# 单元测试
dotnet test tests/DesktopFences.Core.Tests

# Release 自包含发布
dotnet publish src/DesktopFences.App -c Release -r win-x64 --self-contained
```

托盘图标提供新建 Fence、布局快照、恢复最近关闭、自动整理开关、设置入口与退出。

## 项目结构

```
DesktopFences.App    WPF 入口、托盘、DI、启动流水
DesktopFences.UI     WPF 控件、ViewModel（FencePanel / FenceHost / SettingsWindow ...）
DesktopFences.Shell  Win32 P/Invoke：桌面嵌入、热键钩子、Shell 图标、文件监控、拖放 COM
DesktopFences.Core   纯 C#：模型、规则引擎、布局持久化（无 UI/OS 依赖）
docs/design/         架构与功能设计文档（单一事实来源）
docs/plan/           Phase 计划（已完成 / 待完成 / 当前任务）
```

## 数据存储

`%APPDATA%\DesktopFences\`

- `fences.json` — 当前 Fence 布局（按显示器 hash 分文件存）
- `rules.json` — 分类规则
- `settings.json` — AppSettings + RecentClosedFences FIFO
- `snapshots/*.json` — 命名快照
- `pages.json` — 桌面分页

所有写入采用临时文件 → rename 的原子写策略，防止崩溃损坏配置。

## 开发说明

- UI 层 MVVM；ViewModel 在 `ViewModels/`，View 在 `Controls/`。
- Core 项目零平台依赖；所有 Win32 调用集中在 Shell 项目（`AllowUnsafeBlocks`）。
- 每次功能 / 方案变更同步回写到 `docs/design/` 与 `docs/plan/phase-N.md`。
- 沟通使用中文；Phase 全部完成情况见 [docs/plan/complete.md](docs/plan/complete.md)。
