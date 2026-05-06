# Phase 11: DWM Acrylic 背景模糊

**目标**:让 `AppSettings.FenceBlurRadius` 不再仅驱动 `DropShadowEffect` 近似,而通过 DWM `SetWindowCompositionAttribute` 启用真 Acrylic 毛玻璃,对齐 `desktop-v2.html` 原型的 `--blur` 视觉语言(CSS `backdrop-filter: blur()`)。

**背景**:
- Phase 8/10 用 `DropShadowEffect` 近似玻璃质感,只能给出"边缘软阴影",窗口背后内容并未真模糊。
- handoff §10 第 1 条特别提示了 Win11 `SetWindowCompositionAttribute` + `ACCENT_ENABLE_ACRYLICBLURBEHIND` 的色调污染坑。
- Phase 8 todo.md 长期挂账"DWM 背景模糊"。

## 11.1 关键决策

- **API 选用**: `user32.dll!SetWindowCompositionAttribute` + `AccentState.ACCENT_ENABLE_ACRYLICBLURBEHIND`(Win10 1803+)。
- **色调通道由 WPF 提供**: `AccentPolicy.GradientColor = 0x01000000`(几乎全透明黑),前景着色完全交给 `FenceBackgroundBrush`,确保 hue/opacity 滑块继续可控。
- **滑块语义**:
  - `FenceBlurRadius == 0` → `ACCENT_DISABLED` + 旧 DropShadow `Opacity=0` (沿用 Phase 10 行为)。
  - `FenceBlurRadius > 0` → Acrylic 开 + DropShadow 半径线性映射。DWM 自身模糊核大小不可调,故 1 与 60 之间在 Acrylic 视觉上没有差异;差异由 DropShadow 半径承担。
- **作用范围**: 仅 `FenceHost`(Fence 窗口主体)。`DesktopIconOverlay`(全屏透明覆盖层)保持透明,不应用 Acrylic。

## 11.2 实现要点

### 11.2.1 P/Invoke 层

`Shell/Interop/NativeMethods.cs` 新增:

```csharp
[DllImport("user32.dll")]
public static extern int SetWindowCompositionAttribute(IntPtr hwnd, ref WindowCompositionAttributeData data);

public enum AccentState
{
    ACCENT_DISABLED = 0,
    ACCENT_ENABLE_GRADIENT = 1,
    ACCENT_ENABLE_TRANSPARENTGRADIENT = 2,
    ACCENT_ENABLE_BLURBEHIND = 3,
    ACCENT_ENABLE_ACRYLICBLURBEHIND = 4,
    ACCENT_ENABLE_HOSTBACKDROP = 5,
}

[StructLayout(LayoutKind.Sequential)]
public struct AccentPolicy
{
    public AccentState AccentState;
    public uint AccentFlags;
    public uint GradientColor;        // ABGR
    public uint AnimationId;
}

[StructLayout(LayoutKind.Sequential)]
public struct WindowCompositionAttributeData
{
    public WindowCompositionAttribute Attribute;
    public IntPtr Data;
    public int SizeOfData;
}

public enum WindowCompositionAttribute { WCA_ACCENT_POLICY = 19 }
```

### 11.2.2 AcrylicCompositor

`Shell/Desktop/AcrylicCompositor.cs`(新)封装两个静态方法:

```csharp
public static class AcrylicCompositor
{
    public static void Enable(IntPtr hwnd, uint gradientArgb = 0x01000000) { ... }
    public static void Disable(IntPtr hwnd) { ... }
}
```

`gradientArgb` 默认 `0x01000000`(几乎透明黑) — 让 WPF 的 `FenceBackgroundBrush` 决定可见着色。

### 11.2.3 FenceHost 接入

`UI/Controls/FenceHost.xaml.cs`:
- `OnLoaded` 中拿到 hwnd 后,如果 `_currentBlur > 0` 调 `AcrylicCompositor.Enable`。
- 新增 `public void SetAcrylicBlur(int blur)`,缓存 `_currentBlur` 并 Enable/Disable。

### 11.2.4 App 层接入

`App.xaml.cs`:
- `LoadFencesAsync` 末尾(已有 `ApplyFenceShadow(_appSettings)` 之后)对每个 spawn 出来的 host 应用一次 Acrylic。
- `OnSettingsSaved` 在调 `ApplyFenceShadow(settings)` 后,遍历 `_fenceWindows` 调 `host.SetAcrylicBlur(settings.FenceBlurRadius)`。

## 11.3 风险与回退

- **DWM 在 Win11 22H2+ 行为不同**: ACCENT_ENABLE_ACRYLICBLURBEHIND 在某些版本会出现色调污染。已通过 `GradientColor = 0x01000000` 规避。如果用户报告色调异常,可降级到 `ACCENT_ENABLE_BLURBEHIND`。
- **多 Acrylic 窗口性能**: 大量 Fence 同时启用 Acrylic 在低端机上可能掉帧。当前不做特殊处理 — `FenceBlurRadius` 默认 26,用户可滑到 0 关闭。
- **拖动时模糊抖动**: 不影响交互,WPF 拖动时 DWM 会持续重绘。WM_MOVING 不影响 Acrylic 状态。

## 11.4 文档与回写

- `docs/design/acrylic-blur.md` — 设计文档(API 选用、色调通道分工、关键参数取值)。
- `docs/plan/complete.md` — 加入 Phase 11 ✅。
- `docs/plan/todo.md` — 移除/划掉 Phase 8 DWM 背景模糊条目。
- `docs/design/win32-embedding.md` — 在"关键 Win32 API"小节追加 `SetWindowCompositionAttribute`。
