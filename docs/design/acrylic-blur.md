# DWM 背景模糊

## 1. 设计目标

让 Fence 窗口呈现真正的毛玻璃效果(窗口背后桌面壁纸/图标被模糊),而非仅靠 `DropShadowEffect` 近似。对齐 `desktop-v2.html` 原型的 CSS `backdrop-filter: blur(36px)` 视觉语言。

## 2. 选型对比

| 方案 | 原理 | 优点 | 缺点 |
|---|---|---|---|
| A. DropShadowEffect 近似 | WPF 内置 effect | 无需 P/Invoke | 不模糊背景内容,只软化边缘 |
| B. WPF 自绘 BlurEffect 应用到 Background | `BlurEffect` 作用于像素 | 真模糊 | 仅作用于自身像素,无法穿透到桌面壁纸 |
| C. DWM `ACCENT_ENABLE_ACRYLICBLURBEHIND` | Win10 1803+ 私有 API,DWM 处理窗口背后所有像素的模糊 + Acrylic 磨砂 | 视觉强,GPU 加速 | **Win11 22H2+ 会强加 luminosity tint 层覆盖 WPF Background**,与本项目"用户自定义颜色 + 透明度"核心功能直接冲突(见 `docs/bug/acrylic_masks_color_opacity.md`) |
| **C'. DWM `ACCENT_ENABLE_BLURBEHIND`(选定)** | Win10 时代的 Aero Glass blur,无 tint 层 | 真背景模糊,GPU 加速,WPF 背景完全可控,Vista+ 全系兼容 | 模糊核较轻(~12-16 px),非 Win11 风格的强磨砂感 |
| D. Win11 Mica/Acrylic Backdrop | DWM `DwmSetWindowAttribute(DWMWA_SYSTEMBACKDROP_TYPE)` | 官方公开 API | 仅 Win11,且要求 `WS_EX_NOREDIRECTIONBITMAP` 等条件,与 `WS_EX_TOOLWINDOW + AllowsTransparency=True` 冲突 |

**结论**:选方案 C' — `ACCENT_ENABLE_BLURBEHIND` via `SetWindowCompositionAttribute`。
- Phase 11 初版选 C(ACCENT_ENABLE_ACRYLICBLURBEHIND),实测 Win11 22H2 上 luminosity tint 把颜色/透明度滑块直接吃掉(bug #12)。
- 现切 C' BlurBehind:DWM 只负责模糊像素,不再叠 tint;WPF 的 `FenceBackgroundBrush` 完全决定可见颜色。视觉上轻微弱化磨砂感,但保住了"用户调色"这个核心交互。
- 与 Phase 0 选定的桌面嵌入方案(`WS_EX_TOOLWINDOW` 浮窗)依然兼容。

## 3. 关键参数

### 3.1 AccentState

```csharp
public enum AccentState
{
    ACCENT_DISABLED = 0,                  // 关闭 blur — FenceBlurRadius==0 时使用
    ACCENT_ENABLE_GRADIENT = 1,
    ACCENT_ENABLE_TRANSPARENTGRADIENT = 2,
    ACCENT_ENABLE_BLURBEHIND = 3,         // 选定 — Aero Glass blur,无 luminosity tint 层
    ACCENT_ENABLE_ACRYLICBLURBEHIND = 4,  // 弃用 — Win11 22H2+ 加 luminosity tint 覆盖 WPF Background
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

`ACCENT_ENABLE_BLURBEHIND` 不读取 `GradientColor`,DWM 仅做模糊不做染色。本项目传 `0x00000000`(全 0)。WPF 的 `FenceBorder.Background`(由 hue/opacity 滑块计算)是唯一的可见颜色源,无 DWM tint 干扰。

### 3.3 AccentFlags

设为 `0` —— `BlurBehind` 不识别 `DrawAllBorders` 等 flag,清零避免在新版 Win11 上触发未文档化的副作用(早期 Phase 11 用过 `0x02`,实测在 22H2+ 加重 luminosity 干扰)。

## 4. 行为表

| `FenceBlurRadius` | DWM 动作 | DropShadow Opacity |
|---|---|---|
| 0 | `ACCENT_DISABLED` — 关闭 blur | 0(关闭) |
| 1–60 | `ACCENT_ENABLE_BLURBEHIND` + `AccentFlags=0` + `GradientColor=0x00000000` | 0.45(沿用 Phase 10 公式) |

注意:DWM 内部 BlurBehind 模糊核大小**不可由用户控制**(系统约 12-16 像素,比 Acrylic 的 ~30 像素弱),因此 1 与 60 在"模糊强度"维度无视觉差。1–60 之间的差异通过 DropShadow 半径承担(给阴影更软的边缘)。

## 5. 调用时机

| 时机 | 处理 |
|---|---|
| `FenceHost.OnLoaded` | 拿到 hwnd → 若启动设置中 `FenceBlurRadius>0` 调 `AcrylicCompositor.Enable`,否则 `Disable` |
| `App.OnSettingsSaved` | 遍历 `_fenceWindows`,每个 host 调 `host.SetAcrylicBlur(settings.FenceBlurRadius)` 实时生效 |
| `App.LoadFencesAsync` 创建第一个 fence 之前 | 通过 `ApplyFenceShadow` 设置 DropShadow,Acrylic 由 host.OnLoaded 自行处理(不需 App 层提前推) |

## 6. 与桌面嵌入(Phase 0)的兼容性

- `WS_EX_TOOLWINDOW + WS_EX_NOACTIVATE + AllowsTransparency=True` 与 `SetWindowCompositionAttribute(ACCENT_POLICY)` 完全兼容(Phase 0 demo 已验证)。
- Z-order 切换(`HWND_BOTTOM` ↔ `HWND_TOPMOST`)不影响 BlurBehind 状态;BlurBehind 是窗口属性,不随 z-order 变化。
- 拖动时通过 `WM_NCLBUTTONDOWN(HTCAPTION)`(Phase 6 替换 DragMove 后),DWM 持续重绘背后内容,blur 跟随 — 不需特别处理。

## 6.1 圆角剪裁(SetWindowRgn)

DWM blur 在合成管线上**早于 WPF 渲染**,WPF 的 `FenceBorder.CornerRadius="10"` 只裁 Border 自己内部的内容,不能阻止 blur 在窗口矩形的圆角外溢出 —— 不处理的话 blur 启用后 fence 看起来是直角矩形(见 `docs/bug/blur_corners_squared.md`)。

修复:用 `SetWindowRgn` + `CreateRoundRectRgn` 给 Win32 window 本身设圆角 region,让整条合成链(blur + WPF)都被 clip:

```csharp
// AcrylicCompositor.ApplyRoundedRegion(hwnd, w, h, radius)
var hRgn = CreateRoundRectRgn(0, 0, w+1, h+1, radius*2, radius*2);
SetWindowRgn(hwnd, hRgn, true); // 接管 hRgn 所有权
```

`FenceHost.OnLoaded` 中挂 `SizeChanged += ApplyWindowRoundedRegion`,resize / rollup 动画都跟着更新。region 的尺寸/半径必须乘 `VisualTreeHelper.GetDpi(this).DpiScaleX/Y` 转成物理像素,否则高 DPI 下 region 偏小露出窗口边缘。

无论 `FenceBlurRadius` 是否 > 0 都应用 region(blur 关时也无害),保证视觉一致。

## 7. 实现位置一览

| 文件 | 内容 |
|---|---|
| `Shell/Interop/NativeMethods.cs` | `SetWindowCompositionAttribute` P/Invoke + `AccentState / AccentPolicy / WindowCompositionAttributeData / WindowCompositionAttribute`;`SetWindowRgn` / `CreateRoundRectRgn` / `DeleteObject` |
| `Shell/Desktop/AcrylicCompositor.cs` | `static class` — `Enable(hwnd, gradientArgb)` / `Disable(hwnd)` / `ApplyRoundedRegion(hwnd, w, h, radius)` / `ClearRegion(hwnd)`(类名沿用 Phase 11,内部已切 BlurBehind + 圆角 region) |
| `UI/Controls/FenceHost.xaml.cs` | `OnLoaded` 中应用 blur + 挂 `SizeChanged` 同步 region;`SetAcrylicBlur(int)` / `ApplyWindowRoundedRegion()` |
| `App.xaml.cs` | `OnSettingsSaved` 遍历 `_fenceWindows.SetAcrylicBlur` |

## 8. 局限性

- **不支持自定义模糊半径**:DWM BlurBehind 的模糊强度固定(~12-16 px,比 Acrylic 弱)。如未来需要可调模糊半径,可考虑切方案 B(WPF BlurEffect 离屏渲染)+ 拍快照方案,代价大幅上升。
- **非 Win11 强磨砂感**:用 BlurBehind 而不是 Acrylic 后,视觉风格更接近 Win10 Aero Glass。这是为了保住"用户调色"功能而做的取舍(见 `docs/bug/acrylic_masks_color_opacity.md`)。
- **私有 API**:`SetWindowCompositionAttribute` 一直未被 Microsoft 正式公开,Win 主版本升级有可能改变行为。`ACCENT_ENABLE_BLURBEHIND` 在 Vista+ 全代际表现一致,稳定性优于 `ACCENT_ENABLE_ACRYLICBLURBEHIND`。
- **低端机性能**:多个 Fence 同开 BlurBehind 时 GPU 占用升高。`FenceBlurRadius=0` 即可整体关闭,作为 fallback。
