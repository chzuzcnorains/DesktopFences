# 桌面「查看/排序/刷新」联动完全失效：IShellWindows 晚绑定 DISP_E_MEMBERNOTFOUND

## 现象

桌面右键「查看→大图标」后 overlay 图标不变大，「排序方式」「刷新」也全部无反应；
启动时也不应用用户已设置的尺寸/排序。功能像完全不存在。

## 排查过程

1. 独立探针直接调用 `DesktopViewMonitor.TryGetIconSize` → `ok=False`，确认 COM 读取链路不通
   （`TryReadViewState` 的 catch 把异常吞成 `null`，在应用内表现为静默无事发生）。
2. 把链路逐步拆开打印，断点落在第一环：

```
[2] FindWindowSW(CSIDL_DESKTOP, SWC_DESKTOP)...
EXCEPTION: COMException: 找不到成员。 (0x80020003 (DISP_E_MEMBERNOTFOUND))
   at System.RuntimeType.InvokeMember(...)
   at System.RuntimeType.ForwardCallToInvokeMember("FindWindowSW", ...)
```

## 真因

`IShellWindows` 最初声明为 `[InterfaceType(ComInterfaceType.InterfaceIsIDispatch)]` + `[DispId(5)]`，
期望 CLR 按 DispId 走 `IDispatch::Invoke`。**实际上 .NET 的 RCW 对 IDispatch-only 接口走的是
「按方法名」的晚绑定**（栈帧可见 `ForwardCallToInvokeMember("FindWindowSW")` → `GetIDsOfNames`），
而 explorer 的 ShellWindows 对 `"FindWindowSW"` 名字解析失败，返回 `DISP_E_MEMBERNOTFOUND`。
`[DispId]` 特性在这条路径上根本不被使用。

## 修复

`IShellWindows` 改为 **dual/vtable 早绑定**（`ComInterfaceType.InterfaceIsDual`）：CLR 自动跳过
IDispatch 的 7 个槽位，之后按声明顺序对位 vtable。需按 ExDisp.h 把 `FindWindowSW` 之前的
8 个方法（`get_Count / Item / _NewEnum / Register / RegisterPending / Revoke / OnNavigate /
OnActivated`）全部占位声明，`FindWindowSW` 本体用完整签名（`ppdispOut` 是最后一个 out 参数，
不再是 retval 返回值）：

```csharp
[ComImport, Guid("85CB6900-4D95-11CF-960C-0080C7F4EE85")]
[InterfaceType(ComInterfaceType.InterfaceIsDual)]
private interface IShellWindows
{
    [PreserveSig] int get_Count(out int count);
    // ... 7 个占位 ...
    [PreserveSig] int FindWindowSW(
        [In, MarshalAs(UnmanagedType.Struct)] ref object pvarLoc,
        [In, MarshalAs(UnmanagedType.Struct)] ref object pvarLocRoot,
        int swClass, out int phwnd, int swfwOptions,
        [MarshalAs(UnmanagedType.IDispatch)] out object? ppdispOut);
}
```

修复后探针验证：`TryGetIconSize ok=True size=96`、`TryGetSort ok=True key=DateModified
ascending=False`，与桌面实际设置一致。

## 经验

1. **.NET（Core）里声明 COM 接口优先用 vtable 早绑定**（`InterfaceIsIUnknown` / `InterfaceIsDual`），
   `InterfaceIsIDispatch` 的晚绑定按名字解析、对 shell 这类老接口不可靠；`[DispId]` 在该路径不生效。
   本项目已有先例全部是 vtable 声明（`ShellContextMenu` 的 IContextMenu/IShellFolder）。
2. **dual 接口占位声明只需要槽位数量与顺序正确**，占位方法的参数签名无所谓（绝不调用）。
3. **catch-all 吞异常的 COM 探测代码，开发期要先用独立探针跑通再集成**——静默降级会把
   "接口声明写错"这类硬 bug 伪装成"功能没反应"。

## 涉及文件

- `src/DesktopFences.Shell/Desktop/DesktopViewMonitor.cs` — IShellWindows 声明 + 调用点

## 关联

- 功能设计：[desktop-icon-overlay.md §12](../design/desktop-icon-overlay.md)（查看/排序/刷新联动）
- COM 释放规范先例：[shell_context_menu_com_leak.md](shell_context_menu_com_leak.md)（bug 28）
