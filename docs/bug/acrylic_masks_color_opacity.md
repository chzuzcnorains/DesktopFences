# 模糊强度 > 0 时颜色/透明度调整失效

## 问题描述

外观设置中只要 **模糊强度 (FenceBlurRadius)** > 0，背景色调 (Hue)、透明度 (Opacity) 滑块的调整在 fence 上完全不可见 —— fence 始终呈现一种灰白磨砂玻璃的样子，不管你把 hue 拖到哪、opacity 拖到多高都没用。

把模糊强度拖到 0 后，hue/opacity 立刻恢复正常生效。

## 复现步骤

1. 打开任意一个 fence，确保它能看到 fence 主体颜色
2. 设置 → 外观，把：
   - 模糊强度拖到 30
   - 透明度拖到 0.90
   - 背景色调拖到 60（黄）
3. 保存
4. 观察：fence 还是灰白雾，看不到黄色，也看不到 90% 不透明的实色

## 根因

Phase 11 启用 DWM blur 时使用的是 `ACCENT_ENABLE_ACRYLICBLURBEHIND`(=4) + `AccentFlags=0x02`(DrawAllBorders) + `GradientColor=0x01000000`：

```csharp
// 旧代码
ApplyAccent(hwnd, AccentState.ACCENT_ENABLE_ACRYLICBLURBEHIND, 0x01000000);
// AccentFlags = 0x02
```

设计预期：`GradientColor` 的 alpha 取 1/255 → DWM 几乎不染色 → WPF 的 `FenceBackgroundBrush` 决定可见颜色。

**实际行为（Win11 22H2+）**：DWM 在 Acrylic 状态下会在 blur 层之上额外叠加一层 **luminosity tint**（系统主题驱动的明度修正层）。这一层不受 `GradientColor` 控制，alpha 也不为 0，会把 WPF 的 FenceBorder 半透明背景几乎全部覆盖掉。结果就是无论 WPF 怎么改 Background 的颜色和 alpha，可见输出都被 DWM 的 luminosity 层主导。

`AccentFlags=0x02` 在新版 Win11 上可能进一步加强这层覆盖（部分参考实现把 0x02 解读为 `DRAW_ALL_BORDERS`，但 DWM 内部实现行为已经变化）。

## 修复

切回老款 `ACCENT_ENABLE_BLURBEHIND`(=3) — Win10 时代的 Aero blur，**不带 luminosity tint**，DWM 只负责模糊背后桌面像素，不再添加任何染色层。WPF 的 FenceBorder.Background 重新成为唯一可见颜色源。

```csharp
public static void Enable(IntPtr hwnd, uint gradientArgb = 0x00000000)
{
    if (hwnd == IntPtr.Zero) return;
    ApplyAccent(hwnd, NativeMethods.AccentState.ACCENT_ENABLE_BLURBEHIND, gradientArgb);
}

private static void ApplyAccent(...)
{
    var policy = new NativeMethods.AccentPolicy
    {
        AccentState = state,
        AccentFlags = 0, // 不再用 0x02
        GradientColor = gradientArgb,
        AnimationId = 0,
    };
    ...
}
```

## 取舍

| 维度 | ACCENT_ENABLE_ACRYLICBLURBEHIND | ACCENT_ENABLE_BLURBEHIND（采用） |
|---|---|---|
| 背景模糊 | ✓ 强模糊（约 30 px） | ✓ 较轻模糊（约 12-16 px） |
| 视觉风格 | "Win11 Mica/Acrylic" 磨砂玻璃 | "Win10 Aero Glass" 较轻玻璃 |
| 颜色/透明度可控 | ✗ 被 luminosity tint 吃掉 | ✓ 完全由 WPF 决定 |
| Win10 兼容 | 1803+ | Vista+ 全部支持 |

牺牲了 Win11 风格的强磨砂感，换来色相/透明度滑块的实际可控性。对于桌面 Fence 这种"用户自定义颜色是核心功能"的产品，这个取舍合理。

## 验证

修复后按复现步骤操作：fence 立即变成 90% 不透明的黄色，背景仍带 Aero 模糊。把 hue 改成 120 → 立即变绿。把模糊强度拖到 0 → 模糊消失，fence 变成纯色矩形（fence 阴影边缘也消失）。

## 修复日期

2026-05-07

## 相关文件

- `src/DesktopFences.Shell/Desktop/AcrylicCompositor.cs` — Enable/Disable + ApplyAccent
- `docs/design/acrylic-blur.md` — Phase 11 设计决策（已同步更新）
- `docs/plan/phase-11.md` — Phase 11 计划（保留为历史记录）

## 经验教训

`SetWindowCompositionAttribute` 是私有 API，DWM 在 Win11 22H2+ 上对 `ACCENT_ENABLE_ACRYLICBLURBEHIND` 的实现已**事实性改变**：增加了不可关闭的 luminosity 层。任何依赖 "GradientColor.alpha=1 让 WPF 决定 tint" 的设计在新系统上都会失效。

如果未来需要恢复 Acrylic 风格，正确路径是 Win11 公开 API：`DwmSetWindowAttribute(DWMWA_SYSTEMBACKDROP_TYPE, DWMSBT_TRANSIENTWINDOW)`，但它要求 `WS_EX_NOREDIRECTIONBITMAP`，与 `WS_EX_TOOLWINDOW + AllowsTransparency=True` 不兼容（见 `docs/design/acrylic-blur.md` 选型表 D 项）。短期内 BlurBehind 是唯一可在桌面嵌入方案下保留模糊 + 保留颜色控制的选择。
