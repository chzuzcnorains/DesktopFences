# 设置窗口打开后未归档图标和 panel 图标无法选中修复

## 问题描述

打开设置窗口（托盘菜单 → 设置... / 分类规则...）之后：

- 单击 fence panel 内的文件图标 → 无法选中
- 单击未归档区域（`DesktopIconOverlay`）内的桌面图标 → 无法选中
- 双击同样无效；点击的"反应"是 SettingsWindow 的标题栏闪一下

期望：设置窗口打开后，fence panel 和未归档图标仍可正常单击 / 双击 / 拖拽。

## 真正根因

`App.ShowSettings(...)` 用 `settingsWindow.ShowDialog()` 把 SettingsWindow 当作**模态对话框**打开。

WPF 的 `ShowDialog()` 在 Win32 层会对**当前线程内所有其他顶层窗口**调用 `EnableWindow(hwnd, FALSE)`，包括：

- 所有 `FenceHost`（每个 fence 一个顶层窗口）
- `DesktopIconOverlay`（未归档图标层）
- `SnapGuideOverlay` 等

被 disable 的窗口在 OS 层面就拒绝接收 `WM_LBUTTONDOWN` / `WM_LBUTTONDBLCLK` —— 这是 Windows 模态对话框的内置行为：
点击被禁用的兄弟窗口时，OS 不把消息送进 WPF，而是让 active modal 的标题栏闪一下提醒用户。

所以表象是"图标无法选中"，本质是窗口级别的 `WM_LBUTTONDOWN` 根本没进 WPF 消息泵。

> ⚠️ 这跟 bug 19（cell 空白区域无法选中）**不同层**：
> - bug 19 是 `AllowsTransparency=True` layered window 的**像素级 alpha=0 click-through** —— 命中测试在 OS 合成时被丢弃。
> - 本 bug 是**窗口级 EnableWindow(FALSE)** —— 整个窗口被 OS 排除在输入路由之外。

## 修复方案

`SettingsWindow` 通过 `SettingsSaved` / `RulesSaved` / `RestoreClosedFenceRequested` 等事件回调通知 `App`，**不依赖 `DialogResult`**，因此可以安全改为非模态。

把 `ShowDialog()` 改成 `Show()`，并用一个字段缓存当前实例，避免多次点击托盘菜单生成多个设置窗口。

### 关键代码（[App.xaml.cs](../../src/DesktopFences.App/App.xaml.cs)）

```csharp
private SettingsWindow? _settingsWindow;

private void ShowSettings(int tabIndex = 0)
{
    // 已有窗口则复用：切到目标 tab、激活并置前。
    if (_settingsWindow is not null)
    {
        _settingsWindow.SelectTab(tabIndex);
        if (_settingsWindow.WindowState == WindowState.Minimized)
            _settingsWindow.WindowState = WindowState.Normal;
        _settingsWindow.Activate();
        return;
    }

    var settingsWindow = new SettingsWindow(/* … */);
    settingsWindow.SelectTab(tabIndex);

    // … 事件回调挂接保持不变 …

    settingsWindow.Closed += (_, _) => _settingsWindow = null;

    _settingsWindow = settingsWindow;
    settingsWindow.Show();   // ← 关键：Show 而不是 ShowDialog
}
```

## 修复关键点

1. **`ShowDialog()` 在 Win32 层会对同线程其他顶层窗口调用 `EnableWindow(FALSE)`** —— 这对常驻托盘 + 多个浮动窗口（fence/overlay）的应用是致命行为。
2. **永远不要在常驻托盘应用里把"设置/管理"类窗口做成模态**：模态是 OS 强制阻塞所有兄弟窗口的输入，与 fence 类应用"用户随时可与桌面元素交互"的需求直接冲突。
3. **改成非模态后必须用字段缓存当前实例**，否则反复点击托盘菜单会创建多个 SettingsWindow；`Closed` 事件里清空字段。
4. **需要"修改后立即关闭"的场景**（`ResetLayoutRequested` / `ClearRulesRequested` / `RestoreDefaultsRequested`）原本就调用 `settingsWindow.Close()`，非模态下行为保持一致。

## 修复效果

- ✅ 设置窗口打开后，fence panel 内文件图标可正常单击 / 双击 / 拖拽
- ✅ 设置窗口打开后，未归档图标层（`DesktopIconOverlay`）可正常选中 / 拖拽
- ✅ 反复点击托盘"设置..." 不会生成多个窗口，已有窗口被激活并切到目标 tab
- ✅ "重置布局 / 清空规则 / 恢复默认"等"修改后关闭"的按钮行为不变
- ✅ 设置保存 / 规则保存 / Fence 管理操作经事件回调流回 `App`，逻辑不变

## 相关文件

- [src/DesktopFences.App/App.xaml.cs](../../src/DesktopFences.App/App.xaml.cs) —— `ShowSettings` 改为非模态
- [src/DesktopFences.UI/Controls/SettingsWindow.xaml.cs](../../src/DesktopFences.UI/Controls/SettingsWindow.xaml.cs) —— 设计上就只用事件回调，无需改动

## 同类提醒

项目里其他从托盘菜单或全局快捷键弹出的"长生命周期"窗口（例如未来可能加的搜索结果窗、规则编辑独立窗等），如果它们与桌面 fence 元素并存且事件回调可异步通信，**默认应使用 `Show()` 而非 `ShowDialog()`**。只有需要"立即返回结果且过程中绝对禁止操作其他窗口"的场景（如重命名输入框 [`RenameWindow`](../../src/DesktopFences.UI/Controls/RenameWindow.xaml.cs)）才用 `ShowDialog()`。
