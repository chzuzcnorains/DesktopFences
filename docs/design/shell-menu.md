# Shell 右键菜单集成

## 1. 目标

在文件/桌面右键菜单中添加 "Move to Fence..." 选项

## 2. 实现方式

### 方案 1（推荐）：Windows 11 Sparse Package + COM Shell Extension

```
- 注册 IExplorerCommand 实现
- 通过 Sparse Package 获取 Shell Extension 权限
- .NET 8+ 支持 COM Source Generator
```

### 方案 2：经典 COM Shell Extension (C++ DLL)

```
- 实现 IContextMenu + IShellExtInit
- 需要单独的 C++ 项目 (FencesMenu64.dll)
- 通过 Named Pipe / Memory-Mapped File 与主进程通信
```

### 方案 3（MVP 阶段）：不做 Shell Extension

```
- 仅支持从 Fence 内右键操作
- 降低初始复杂度
```

## 3. 当前实现（IContextMenu）

`ShellContextMenu.cs` — 原生 Shell 上下文菜单，**按选区构建**（bug 43）：

- **单文件**：`SHParseDisplayName` → `SHBindToObject` → `IShellFolder.GetUIObjectOf`（cidl=1） → `IContextMenu`
- **多文件**（`Show(hwnd, IReadOnlyList<string>, x, y)` 重载）：逐个 `SHParseDisplayName` 取绝对 PIDL（单个失败跳过）→ `SHCreateShellItemArrayFromIDLists` → `IShellItemArray.BindToHandler(BHID_SFUIObject)` → `IContextMenu`。菜单动词（删除/打开/发送到…）作用于整个选区，与 Explorer 语义一致
  - 选用 `IShellItemArray` 而非扩展 `GetUIObjectOf(cidl=N)` 的原因：`GetUIObjectOf` 要求所有 PIDL 是**同一父文件夹**的直接子项，而选区可能跨用户桌面 + 公共桌面两个目录
  - 任一 COM 步骤失败 → 降级为首元素（被右键文件）的单文件菜单；调用方约定把被右键文件放在数组首位
- 两条路径共用 `TrackAndInvoke` 助手：`QueryContextMenu` 填充菜单 → `TrackPopupMenuEx` 显示 → `InvokeCommand` 执行
- 完整 COM 资源管理（`Marshal.FreeCoTaskMem`, `ReleaseComObject`, `DestroyMenu`，见 bug 28）

调用方（`FencePanel.FileItem_MouseRightButtonUp` / `DesktopIconOverlay.OnIconRightClick`）：右键**已选中**项保留多选并传整个选区；右键**未选中**项清空重选、传单文件。

## 4. 文件操作

`ShellFileOperations.cs` — 静态工具类：
- 双击打开：`Process.Start(UseShellExecute=true)`
- 删除到回收站：`SHFileOperation(FO_DELETE, FOF_ALLOWUNDO)`
- 重命名：`File.Move`
- 快捷方式目标解析 `IShellLink`（延迟到后续需要时实现）
