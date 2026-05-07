# 设置模糊强度后 panel 圆角丢失

## 问题描述

修复了 bug #12（Acrylic 切回 BlurBehind）之后，新问题浮现：只要 `FenceBlurRadius > 0` 启用 DWM blur，fence 的四个圆角就消失了 —— 看起来是个直角矩形磨砂玻璃，FenceBorder 的 `CornerRadius="10"` 完全失效。

把模糊强度拖回 0 后，圆角立刻恢复。

## 复现步骤

1. 任何一个 fence 上
2. 设置 → 外观 → 模糊强度拖到 30
3. 保存
4. 观察 fence 的四个角：方的，看不到 10 px 的圆角

## 根因

`ACCENT_ENABLE_BLURBEHIND`（以及 Acrylic）是 **DWM 在窗口客户区整个矩形上画 blur 层**，blur 比 WPF 渲染更早发生在合成管线里。

WPF 的 `FenceBorder.CornerRadius="10"` 只裁 Border **自己内部的内容**（圆角外的像素 WPF 不画 = 透明）。在 blur 关闭时，透明就是真透明，看不到圆角外的矩形痕迹。但开了 BlurBehind 之后：

```
窗口矩形（含圆角外的 4 个三角区）
  └─ DWM blur 层（铺满整个矩形）
     └─ WPF 渲染（圆角外是 alpha=0 的透明像素）
        └─ 用户看到：blur 透过 WPF 的"透明角" 显示出来 → 视觉上变方角
```

WPF 的透明像素不会挡住 DWM blur，所以圆角外的 4 个三角区域显示成"模糊的桌面壁纸" —— 用户感知就是"fence 变方了"。

## 修复

用 `SetWindowRgn` + `CreateRoundRectRgn` 给 **窗口本身**（不是 WPF 元素）设置一个圆角剪裁区域。Win32 的 window region 在 DWM 合成更前一层，blur 也会被这层 clip 掉。

新增 `Shell/Interop/NativeMethods.cs` P/Invoke：

```csharp
[DllImport("gdi32.dll")]
public static extern IntPtr CreateRoundRectRgn(int x1, int y1, int x2, int y2, int cx, int cy);

[DllImport("gdi32.dll")]
public static extern bool DeleteObject(IntPtr hObject);

[DllImport("user32.dll")]
public static extern int SetWindowRgn(IntPtr hwnd, IntPtr hRgn, bool bRedraw);
```

`Shell/Desktop/AcrylicCompositor.cs` 加 `ApplyRoundedRegion(hwnd, w, h, radius)` / `ClearRegion(hwnd)`。

`UI/Controls/FenceHost.xaml.cs` 在 `OnLoaded` 里挂 `SizeChanged += (_, _) => ApplyWindowRoundedRegion()`，并在 `SetAcrylicBlur` 末尾再调一次：

```csharp
private void ApplyWindowRoundedRegion()
{
    var helper = new WindowInteropHelper(this);
    if (helper.Handle == IntPtr.Zero) return;
    var dpi = VisualTreeHelper.GetDpi(this);
    int w = (int)Math.Round(ActualWidth  * dpi.DpiScaleX);
    int h = (int)Math.Round(ActualHeight * dpi.DpiScaleY);
    int radius = (int)Math.Round(10 * dpi.DpiScaleX);
    AcrylicCompositor.ApplyRoundedRegion(helper.Handle, w, h, radius);
}
```

关键细节：
- **DPI 转换**：`ActualWidth/Height` 是 DIP，`SetWindowRgn` 要物理像素，必须乘 `DpiScaleX/Y`。150% DPI 下漏掉这一步会让 region 比窗口小一截，露出窗口边缘。
- **CornerRadius 同步**：region 半径用 `10 * dpi.DpiScaleX`，与 `FencePanel.xaml` 里 `FenceBorderStyle.CornerRadius="10"` 对齐。后续如果改 FenceBorder 的圆角值，这里要一起改。
- **SetWindowRgn 接管 hRgn 所有权**：成功后不能 `DeleteObject`；只在 `SetWindowRgn` 返回 0（失败）时才清理。
- **+1 偏移**：`CreateRoundRectRgn(0, 0, w+1, h+1, ...)` 第三/四个参数是 *exclusive* 边界（GDI 习惯），不 +1 会让最右/最下一列像素被裁掉。
- **总是应用**：blur 关闭时这层 region 也无害（圆角形状本来就和 WPF 一致），所以不分支；`SizeChanged` 一统覆盖 resize / rollup 动画 / 拖拽。

## 验证

- 模糊强度 0 → 圆角正常
- 模糊强度 30 → 圆角正常 ✓
- 拖动调整 fence 尺寸 → 圆角跟随新尺寸 ✓
- 折叠/展开（高度变化） → 圆角形态正确 ✓
- 切显示器 / DPI（未单独验证，但 `VisualTreeHelper.GetDpi` 调用时机是即时的，理论上跟随）

## 修复日期

2026-05-07

## 相关文件

- `src/DesktopFences.Shell/Interop/NativeMethods.cs` — `SetWindowRgn` / `CreateRoundRectRgn` / `DeleteObject` P/Invoke
- `src/DesktopFences.Shell/Desktop/AcrylicCompositor.cs` — `ApplyRoundedRegion` / `ClearRegion`
- `src/DesktopFences.UI/Controls/FenceHost.xaml.cs` — `OnLoaded` 中挂 `SizeChanged`、`SetAcrylicBlur` 末尾、`ApplyWindowRoundedRegion()`

## 经验教训

任何把视觉效果合成进 DWM 的操作（Acrylic / BlurBehind / Mica / DropShadow Behind / 反射等）**都早于 WPF 渲染**，WPF 元素的 `Clip` / `CornerRadius` 不能截断它们。需要圆角时唯一可靠的办法是给 Win32 window 本身设 region，让整条合成链都被 clip。

如果未来改用 Win11 公开的 Mica（`DwmSetWindowAttribute(DWMWA_SYSTEMBACKDROP_TYPE)`），同时还可以用 `DWMWA_WINDOW_CORNER_PREFERENCE` 让 DWM 自己管圆角；但那要求 `WS_EX_NOREDIRECTIONBITMAP`，与本项目的 `WS_EX_TOOLWINDOW + AllowsTransparency=True` 桌面嵌入方案冲突，所以暂时只能继续用 `SetWindowRgn` 这个 Vista 时代的老 API。
