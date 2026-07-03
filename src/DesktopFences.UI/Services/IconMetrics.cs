namespace DesktopFences.UI.Services;

/// <summary>
/// fence tile 图标的当前请求像素尺寸（FencePanel 按「最大 tile DIP × 实际 DPI」
/// 量化后回写）。App 动态往 fence 添加文件时复用此值，保证命中同一个
/// ShellIconExtractor LRU 尺寸桶，而不是为几乎相同的尺寸另开一桶。
/// 初值 96 = 旧行为兜底（正常流程 FencePanel 首次加载图标先于任何动态添加）。
/// </summary>
public static class IconMetrics
{
    public static int TileIconRequestPx { get; set; } = 96;
}
