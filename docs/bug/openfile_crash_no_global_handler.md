# 双击打开文件可崩溃整个应用（无全局异常兜底）

## 问题描述

双击 fence 内或未归档 overlay 上的文件图标时，如果目标文件已被删除、没有关联程序、或 UAC 提权被取消，整个应用进程直接崩溃。崩溃时桌面图标层 (SysListView32) 处于隐藏状态，用户桌面图标"全部消失"，必须等下次启动的 crash recovery 才能恢复。

## 真因

1. `ShellFileOperations.OpenFile` 直接 `Process.Start(UseShellExecute = true)`，上述场景会抛 `Win32Exception`；
2. 三个调用点（`FencePanel.FileItem_MouseLeftButtonDown`、`DesktopIconOverlay`、`App.OnSearchResultSelected`）都在 UI 线程鼠标事件处理器中，且无 try-catch；
3. 项目没有注册任何 `DispatcherUnhandledException` 处理器——UI 线程未处理异常 = 进程终止。

文件存在性由 10 秒定时器（`_fileExistenceTimer`）清理，删除后的窗口期内 tile 仍可被双击，极易触发。

## 修复

1. `ShellFileOperations.OpenFile` 改为返回 `bool`，内部 try-catch + `Debug.WriteLine`；
2. App 调用点失败时 `ShowToast("无法打开：xxx")`；UI 层调用点静默忽略（文件很快会被存在性定时器清掉）；
3. `App_OnStartup` 注册 `DispatcherUnhandledException` 全局兜底：记录日志、`e.Handled = true`、Toast 提示。**吞掉而不是崩溃**——本应用崩溃的代价（桌面图标层无法恢复）远大于单次操作失败。

## 关键经验

- 凡是隐藏/接管了系统 UI 资源（本项目：桌面图标层）的常驻应用，**必须有全局异常兜底**，否则任何一个 UI 事件处理器的异常都会把系统资源带进坟墓。
- `Process.Start(UseShellExecute=true)` 是会抛异常的——文件删除、无关联、UAC 取消都是用户日常操作，不是边缘情况。

## 验证

双击 fence 内一个已被删除的文件 → 不崩溃，Toast 提示；搜索结果打开已删除文件 → 同样不崩溃。

| 项目 | 内容 |
|------|------|
| 修复日期 | 2026-06-12 |
| 涉及文件 | ShellFileOperations.cs, App.xaml.cs |
