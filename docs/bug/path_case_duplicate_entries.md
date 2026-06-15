# 路径比较大小写不一致导致潜在重复条目

## 问题描述

`FencePanelViewModel.AddFile` 的去重用 `List.Contains`（Ordinal 大小写敏感），`RemoveFile` 同样用 `==` 比较；而项目其余路径比较（`DesktopFileMonitor` 的 known-files 集合、`FenceHostExtensions.ContainsFile`、`MatchFileToPosition` 等）全部用 `OrdinalIgnoreCase`。Windows 路径不区分大小写——同一文件经不同来源（FSW 事件 / 全量扫描 / 拖拽 / 规则分类）给出的路径大小写可能不同（典型如盘符 `C:` vs `c:`、Public 桌面路径），会绕过去重产生重复条目，删除时也可能删不掉。

## 真因

`FencePanelViewModel` 是早期代码，未跟进项目统一的 `OrdinalIgnoreCase` 路径比较约定。

## 修复

1. 新增 `FencePanelViewModel.ContainsFile(path)`（`Contains(path, StringComparer.OrdinalIgnoreCase)`），`AddFile` 去重改用它；
2. `RemoveFile` 改 `RemoveAll + string.Equals(..., OrdinalIgnoreCase)`；
3. 顺带：`FencePanel.FileItem_MouseRightButtonUp` 的 `Window.GetWindow(this)!` 强制解包改判空提前 return（fence 关闭瞬间右键不再 NRE）。

## 关键经验

Windows 路径比较**必须**统一 `StringComparer.OrdinalIgnoreCase` / `StringComparison.OrdinalIgnoreCase`，新增任何含路径集合的类时对照此约定。排查方法：`grep "FilePaths.Contains\|FilePath =="`。

## 验证

构建 + 71 个单元测试通过；以不同大小写路径调用 AddFile 不再产生重复条目。

| 项目 | 内容 |
|------|------|
| 修复日期 | 2026-06-12 |
| 涉及文件 | FencePanelViewModel.cs, FencePanel.xaml.cs |
