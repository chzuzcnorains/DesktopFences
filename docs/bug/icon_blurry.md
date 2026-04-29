# 未归档 icon 显示模糊修复

## 问题描述
未归档的桌面图标在 `DesktopIconOverlay` 中显示不够锐利，比 Windows 11 原生桌面图标模糊。此外，图标显示尺寸也与原生桌面不一致。

## 问题根源

### 1. DPI 缩放处理不当
- 在 150% DPI 设置下，WPF 使用设备无关像素 (DIP) 作为单位，1 DIP = 1.5 物理像素
- 直接从 `SHGetFileInfo` 获取的图标带有系统 DPI 信息，但 WPF 渲染时没有正确处理

### 2. 渲染模式选择
- `BitmapScalingMode.HighQuality` 对于像素级别的图标可能导致过度平滑
- `Stretch.None` 配合手动尺寸设置容易导致缩放计算错误

### 3. 初始修复的问题
- 早期尝试手动读取图标 DPI 并重新创建 BitmapSource 导致质量损失
- 硬编码尺寸时没有考虑实际测量的原生显示尺寸

## 最终修复方案

### 修改 1：DesktopIconOverlay.xaml.cs
在 `CreateIconElement` 方法中：

```csharp
var image = new Image
{
    Source = icon,
    HorizontalAlignment = HorizontalAlignment.Center,
    VerticalAlignment = VerticalAlignment.Center,
    Stretch = Stretch.Uniform, // 均匀缩放以填充目标尺寸
    SnapsToDevicePixels = true,
    UseLayoutRounding = true
};

// 使用高质量渲染模式
RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.HighQuality);
RenderOptions.SetClearTypeHint(image, ClearTypeHint.Enabled);

// 关键：直接使用测量的 Windows 原生尺寸！
// 在 150% DPI 下，Windows 11 原生桌面图标显示为 72 物理像素
// 72 / 1.5 = 48 DIP
image.Width = 48;
image.Height = 48;
```

### 修改 2：ShellIconExtractor.cs
保持简洁的 `SHGetFileInfo` 方式，移除复杂的 `SHGetImageList` 尝试：

```csharp
private static ImageSource? ExtractIcon(string filePath, bool large)
{
    var flags = NativeMethods.SHGFI_ICON |
                NativeMethods.SHGFI_ADDOVERLAYS |
                (large ? NativeMethods.SHGFI_LARGEICON : NativeMethods.SHGFI_SMALLICON);

    if (!File.Exists(filePath) && !Directory.Exists(filePath))
        flags |= NativeMethods.SHGFI_USEFILEATTRIBUTES;

    var shfi = new NativeMethods.SHFILEINFO();
    var result = NativeMethods.SHGetFileInfo(
        filePath,
        NativeMethods.FILE_ATTRIBUTE_NORMAL,
        ref shfi,
        (uint)System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.SHFILEINFO>(),
        flags);

    if (result == IntPtr.Zero || shfi.hIcon == IntPtr.Zero)
        return null;

    try
    {
        var source = Imaging.CreateBitmapSourceFromHIcon(
            shfi.hIcon,
            Int32Rect.Empty,
            BitmapSizeOptions.FromEmptyOptions());
        source.Freeze();
        return source;
    }
    finally
    {
        NativeMethods.DestroyIcon(shfi.hIcon);
    }
}
```

## 修复关键点

### 1. 实际测量，不要猜测
- 通过实际截图测量 Windows 11 原生桌面图标在 150% DPI 下为 72 物理像素
- 不要依赖假设的 DPI 缩放行为

### 2. 正确的 DIP 转换
- 物理像素 → DIP 转换：物理像素 / DPI 缩放系数
- 72 物理像素 / 1.5 = 48 DIP

### 3. 渲染模式选择
- `Stretch.Uniform` - 让 WPF 进行高质量缩放填充目标尺寸
- `BitmapScalingMode.HighQuality` - 对于缩放图标效果最好
- `SnapsToDevicePixels` + `UseLayoutRounding` - 确保像素对齐

### 4. 简洁胜于复杂
- 简单的 `SHGetFileInfo` 配合正确的显示设置，比复杂的 `SHGetImageList` 方案更稳定

## 修复效果
- ✅ 图标尺寸与 Windows 11 原生桌面完全一致
- ✅ 图标清晰度与原生桌面一致
- ✅ 外容器 cell 大小保持固定（`Width=90, Height=90`），布局不受影响

## 经验总结

### DPI 缩放开发要点
1. **理解 WPF 的 DIP 单位** - WPF 使用 96 DPI 基准，1 DIP 在 100% DPI 下 = 1 物理像素
2. **实际测量验证** - 不要只靠公式计算，要实际测量 UI 显示效果
3. **像素对齐很重要** - `SnapsToDevicePixels` 和 `UseLayoutRounding` 确保不会出现亚像素模糊

### 图标渲染最佳实践
1. **优先用系统 API** - `SHGetFileInfo` 提取的图标最符合系统风格
2. **正确使用 Stretch** - `Uniform` 配合固定尺寸通常效果最好
3. **适当的缩放模式** - `HighQuality` 对于图标缩放效果最佳

## 相关文件
- `src/DesktopFences.UI/Controls/DesktopIconOverlay.xaml.cs`
- `src/DesktopFences.Shell/Desktop/ShellIconExtractor.cs`
