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

`ShellContextMenu.cs` — 原生 Shell 上下文菜单：
- `SHParseDisplayName` → `SHBindToObject` → `IShellFolder.GetUIObjectOf` → `IContextMenu`
- `QueryContextMenu` 填充菜单 → `TrackPopupMenuEx` 显示 → `InvokeCommand` 执行
- 完整 COM 资源管理（`Marshal.FreeCoTaskMem`, `DestroyMenu`）

## 4. 文件操作

`ShellFileOperations.cs` — 静态工具类：
- 双击打开：`Process.Start(UseShellExecute=true)`
- 删除到回收站：`SHFileOperation(FO_DELETE, FOF_ALLOWUNDO)`
- 重命名：`File.Move`
- 快捷方式目标解析 `IShellLink`（延迟到后续需要时实现）
