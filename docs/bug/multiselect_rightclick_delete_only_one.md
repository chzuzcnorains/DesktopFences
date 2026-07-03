# 多选后右键"删除"只删除 1 个文件

**状态**：已修复
**日期**：2026-07-02
**影响范围**：`FencePanel`（fence 内图标）与 `DesktopIconOverlay`（未归档桌面图标）的右键 Shell 菜单——不止删除，"打开 / 发送到 / 复制"等全部菜单动词都只作用于单文件

## 现象

在 Fence 或桌面 overlay 中框选 / Ctrl 多选多个图标后，右键其中一个 → 菜单点"删除"，只有被右键点中的那 1 个文件进了回收站，其余选中项不受影响；overlay 侧其余选中项即使被外部删除也会残留幽灵图标（菜单返回后的存在性检查只查被右键的单个文件）。

期望：与 Explorer 一致，菜单动词作用于**整个选区**。

## 真因

右键菜单是 Shell 原生 `IContextMenu` COM 菜单，而它构建时**只传入了被右键的单个文件**：

1. `ShellContextMenu.Show(IntPtr, string filePath, int, int)` 签名只收单文件；内部 `IShellFolder.GetUIObjectOf(hwndOwner, 1, pidls, …)` 的 `cidl=1`。Shell 只知道 1 个文件，"删除"自然只删 1 个。
2. 两个调用点（`FencePanel.FileItem_MouseRightButtonUp`、`DesktopIconOverlay.OnIconRightClick`）虽然正确**保留**了多选状态（右键选中项不清空选区），却都只把 `item.FilePath` 传给菜单——注释里明写 "(Shell menu still operates on the single right-clicked file.)"，是当时的已知设计限制。
3. 多选状态本身没问题：overlay 的 **Delete 键**路径 `DeleteSelected()` 一直正确遍历 `_selectedPaths` 逐个删除——只有右键菜单路径漏了。

## 修复

### 1. `ShellContextMenu` 新增多文件重载（`src/DesktopFences.Shell/Desktop/ShellContextMenu.cs`）

- `Show(IntPtr, IReadOnlyList<string>, int, int)`：逐个 `SHParseDisplayName` 取绝对 PIDL（单个解析失败跳过，不中断整批）→ `SHCreateShellItemArrayFromIDLists` → `IShellItemArray.BindToHandler(BHID_SFUIObject)` → `IContextMenu`。
- **不选**扩展 `GetUIObjectOf(cidl=N)` 的原因：该 API 要求所有 PIDL 是**同一父文件夹**的直接子项，而本应用同时监控用户桌面 + 公共桌面（`App.xaml.cs`），选区可能跨两个目录；`IShellItemArray` 天然支持跨文件夹。
- 任一 COM 步骤失败 → 降级为数组首元素的单文件菜单（= 修复前行为）；**调用方约定把被右键文件放首位**。
- 单文件路径零改动（battle-tested，bug 28 修过泄漏），只把菜单跟踪/执行段抽成 `TrackAndInvoke` 助手供两条路径复用；新增 PIDL/COM 全部 `finally` 释放。

### 2. 两个调用点传整个选区

- `FencePanel.FileItem_MouseRightButtonUp`：镜像组拖拽的既有写法收集 `Files.Where(f => f.IsSelected)`，多于 1 项时传"被右键文件在首位"的数组。删除后的条目清理沿用既有 `DesktopFileMonitor.FilesRemoved → RemoveFileFromAllFences` 链，无需新增逻辑。
- `DesktopIconOverlay.OnIconRightClick`：弹菜单前对 `_selectedPaths` 取快照传入；菜单返回后的存在性检查从"只查被右键文件"改为**遍历快照全部路径**，已删文件即时 `RemoveIcon` + `FileDeleted`（`RemoveIcon` 自带 `_selectedPaths.Remove`）。

### 3. 行为不变的部分

- 右键**未选中**图标：清空选区、单选该图标、弹单文件菜单（Explorer 语义，原有逻辑保留）。
- 附带收益：多选后"打开"打开全部、"属性"显示合并属性——与 Explorer 一致。

## 经验教训

1. **Shell `IContextMenu` 按"构建时传入的 PIDL 集合"决定动词作用域**——UI 层保留了多选高亮但只传单文件，用户看到的选区和菜单实际作用对象脱节，是"看着选了全部、实际只操作一个"这类 bug 的典型形态。同类功能（键盘 Delete、组拖拽都已按集合处理）中只要有一条路径漏传集合就会不一致。
2. 跨文件夹多项上下文菜单用 `SHCreateShellItemArrayFromIDLists` + `IShellItemArray.BindToHandler(BHID_SFUIObject)`，不要试图给 `GetUIObjectOf` 传跨父目录的 PIDL。
3. 菜单/对话框返回后的"事后同步检查"要覆盖**操作可能波及的全部对象**，而不是只查触发交互的那一个。
