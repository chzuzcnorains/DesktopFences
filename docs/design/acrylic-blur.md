# DWM Acrylic 背景模糊

## 1. 设计目标

让 Fence 窗口呈现真正的 Acrylic 毛玻璃效果(窗口背后桌面壁纸/图标被模糊),而非仅靠 `DropShadowEffect` 近似。对齐 `desktop-v2.html` 原型的 CSS `backdrop-filter: blur(36px)` 视觉语言。

## 2. 选型对比

| 方案 | 原理 | 优点 | 缺点 |
|---|---|---|---|
| A. DropShadowEffect 近似 | WPF 内置 effect | 无需 P/Invoke | 不模糊背景内容,只软化边缘 |
| B. WPF 自绘 BlurEffect 应用到 Background | `BlurEffect` 作用于像素 | 真模糊 | 仅作用于自身像素,无法穿透到桌面壁纸 |
| **C. DWM `SetWindowCompositionAttribute`(选定)** | Win10 1803+ 暴露 ACCENT_ENABLE_ACRYLICBLURBEHIND,DWM 处理窗口背后所有像素的模糊 | 真背景模糊,GPU 加速,免费跟随系统 | 私有 API,Win11 22H2 下需特别处理色调污染 |
| D. Win11 Mica/Acrylic Backdrop | DWM `DwmSetWindowAttribute(DWMWA_SYSTEMBACKDROP_TYPE)` | 官方公开 API | 仅 Win11,且要求 `WS_EX_NOREDIRECTIONBITMAP` 等条件,与 `WS_EX_TOOLWINDOW + AllowsTransparency=True` 冲突 |

**结论**:选方案 C — Acrylic via `SetWindowCompositionAttribute`,与 Phase 0 选定的桌面嵌入方案(WS_EX_TOOLWINDOW 浮窗)兼容。

## 3. 关键参数

### 3.1 AccentState

```csharp
public enum AccentState
{
    ACCENT_DISABLED = 0,                  // 关闭 Acrylic — FenceBlurRadius==0 时使用
    ACCENT_ENABLE_GRADIENT = 1,
    ACCENT_ENABLE_TRANSPARENTGRADIENT = 2,
    ACCENT_ENABLE_BLURBEHIND = 3,         // 旧版 Aero blur(更轻量,Win10 早期版本可降级)
    ACCENT_ENABLE_ACRYLICBLURBEHIND = 4,  // Acrylic — FenceBlurRadius>0 时使用
    ACCENT_ENABLE_HOSTBACKDROP = 5,
}
```

### 3.2 AccentPolicy.GradientColor

ABGR 32 位(注意不是 ARGB):

```
0x AA BB GG RR
   ↑  ↑  ↑  ↑
   alpha 透明度,蓝,绿,红
```

**取 `0x01000000`(几乎透明黑)的原因**:
- DWM 在 Acrylic 状态下会把 `GradientColor` 与模糊后的背景做 alpha 混合输出。alpha 取 1/255 接近不染色,让 WPF 层的 `FenceBackgroundBrush`(由 hue/opacity 滑块计算)负责可见的颜色着色。
- 这样 hue/opacity 设置仍然完全可控,DWM 仅负责"模糊"这一个动作。
- handoff §10 第 1 条专门指出此坑:不设这一条会导致 Win11 上出现明显色调污染。

### 3.3 AccentFlags

设 `0x02`(`DrawAllBorders`,在某些资料中也叫 `DRAW_LEFT_BORDER`...的位组合)以画出窗口四边的细边。本项目 Fence 已自绘 BorderBrush,因此设 `0x02` 还是 `0` 视觉差异极小,统一传 `0x02`(与开源参考实现一致)。

## 4. 行为表

| `FenceBlurRadius` | DWM 动作 | DropShadow Opacity |
|---|---|---|
| 0 | `ACCENT_DISABLED` — 调用 SetWindowCompositionAttribute 禁用 Acrylic | 0(关闭) |
| 1–60 | `ACCENT_ENABLE_ACRYLICBLURBEHIND` + `GradientColor=0x01000000` | 0.45(沿用 Phase 10 公式) |

注意:DWM 内部 Acrylic 模糊核大小**不可由用户控制**(系统约 30 像素),因此 1 与 60 在"模糊强度"维度无视觉差。1–60 之间的差异通过 DropShadow 半径承担(给阴影更软的边缘)。

## 5. 调用时机

| 时机 | 处理 |
|---|---|
| `FenceHost.OnLoaded` | 拿到 hwnd → 若启动设置中 `FenceBlurRadius>0` 调 `AcrylicCompositor.Enable`,否则 `Disable` |
| `App.OnSettingsSaved` | 遍历 `_fenceWindows`,每个 host 调 `host.SetAcrylicBlur(settings.FenceBlurRadius)` 实时生效 |
| `App.LoadFencesAsync` 创建第一个 fence 之前 | 通过 `ApplyFenceShadow` 设置 DropShadow,Acrylic 由 host.OnLoaded 自行处理(不需 App 层提前推) |

## 6. 与桌面嵌入(Phase 0)的兼容性

- `WS_EX_TOOLWINDOW + WS_EX_NOACTIVATE + AllowsTransparency=True` 与 `SetWindowCompositionAttribute(ACCENT_POLICY)` 完全兼容(Phase 0 demo 已验证)。
- Z-order 切换(`HWND_BOTTOM` ↔ `HWND_TOPMOST`)不影响 Acrylic 状态;Acrylic 是窗口属性,不随 z-order 变化。
- 拖动时通过 `WM_NCLBUTTONDOWN(HTCAPTION)`(Phase 6 替换 DragMove 后),DWM 持续重绘背后内容,Acrylic 跟随 — 不需特别处理。

## 7. 实现位置一览

| 文件 | 内容 |
|---|---|
| `Shell/Interop/NativeMethods.cs` | `SetWindowCompositionAttribute` P/Invoke + `AccentState / AccentPolicy / WindowCompositionAttributeData / WindowCompositionAttribute` |
| `Shell/Desktop/AcrylicCompositor.cs` | `static class` — `Enable(hwnd, gradientArgb)` / `Disable(hwnd)` |
| `UI/Controls/FenceHost.xaml.cs` | `OnLoaded` 中应用;`SetAcrylicBlur(int)` 公开方法 |
| `App.xaml.cs` | `OnSettingsSaved` 遍历 `_fenceWindows.SetAcrylicBlur` |

## 8. 局限性

- **不支持自定义模糊半径**:DWM Acrylic 的模糊强度固定。如未来需要可调模糊半径,可考虑切方案 B(WPF BlurEffect 离屏渲染)+ 拍快照方案,代价大幅上升。
- **私有 API**:`SetWindowCompositionAttribute` 一直未被 Microsoft 正式公开,Win 主版本升级有可能改变行为。已通过参数稳定的 `0x01000000` 减少风险面。
- **低端机性能**:多个 Fence 同开 Acrylic 时 GPU 占用升高。`FenceBlurRadius=0` 即可整体关闭,作为 fallback。
