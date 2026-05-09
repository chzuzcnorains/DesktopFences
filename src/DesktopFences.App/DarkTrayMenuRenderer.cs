using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace DesktopFences.App;

/// <summary>
/// 让 NotifyIcon 弹出的 WinForms <see cref="ContextMenuStrip"/> 与 WPF DarkTheme.xaml
/// 中的 <c>DarkContextMenuStyle</c>/<c>DarkMenuItemStyle</c> 视觉一致：暗色背景、
/// 与 fence panel 一致的 8px 圆角、放大字号。
/// WinForms GDI+ 不支持 alpha 通道，下面的颜色是把 WPF 半透明色叠加到 #1A2036 后
/// 折算出的不透明等价值。圆角通过 SetWindowRgn 给 popup HWND 设置裁剪区域实现，
/// 与 fence panel (AcrylicCompositor) 同方案。
/// </summary>
internal static class DarkTrayMenuRenderer
{
    // 与 DarkTheme.xaml 对齐：FenceBaseColor #1A2036 作为底色，菜单略微提亮。
    private static readonly Color Background      = Color.FromArgb(0x1C, 0x20, 0x30);
    // hover：DarkTheme 的 #33FFFFFF over #1C2030 ≈
    private static readonly Color HoverBackground = Color.FromArgb(0x3A, 0x4E, 0x6E);
    private static readonly Color BorderColor     = Color.FromArgb(0x3A, 0x3F, 0x52);
    private static readonly Color SeparatorColor  = Color.FromArgb(0x3A, 0x3F, 0x52);
    private static readonly Color Foreground      = Color.FromArgb(0xE8, 0xEC, 0xF4); // TextPrimary
    private static readonly Color DisabledFore    = Color.FromArgb(0x6A, 0x72, 0x86);

    // WPF DarkContextMenuStyle 用 CornerRadius=8；这里同步。
    private const int CornerRadius = 8;
    // WPF DarkMenuItemStyle 用 FontSize=12.5（≈16.7px @ 96DPI）；放大原 9pt 字号到 10.5pt
    // 让中文菜单项可读性更接近 WPF 同款字。
    private const float FontSizePt = 10.5f;
    private const string FontFamily = "Microsoft YaHei UI";

    /// <summary>给整棵根菜单（含全部已有子菜单）应用暗色样式。</summary>
    public static void Apply(ContextMenuStrip menu)
    {
        menu.Renderer = new DarkRenderer();
        menu.BackColor = Background;
        menu.ForeColor = Foreground;
        menu.ShowImageMargin = false;
        menu.Font = new Font(FontFamily, FontSizePt);
        AttachRoundCorner(menu);
        ApplyToItems(menu.Items);
    }

    /// <summary>
    /// 递归地刷子菜单项前景/背景色 + 字体 + 子菜单的圆角。
    /// 动态填充的子菜单（最近关闭、布局快照）应在内容变更后调用一次。
    /// </summary>
    public static void ApplyToItems(ToolStripItemCollection items)
    {
        foreach (ToolStripItem item in items)
        {
            item.ForeColor = Foreground;
            item.BackColor = Background;

            if (item is ToolStripMenuItem mi)
            {
                if (mi.DropDown is ToolStripDropDownMenu dm)
                {
                    dm.BackColor = Background;
                    dm.ForeColor = Foreground;
                    dm.ShowImageMargin = false;
                    dm.Font = new Font(FontFamily, FontSizePt);
                    AttachRoundCorner(dm);
                }

                if (mi.HasDropDownItems)
                    ApplyToItems(mi.DropDownItems);
            }
        }
    }

    /// <summary>
    /// 给一个 popup（根菜单或任意子菜单）挂上「Opened 时给 HWND 设圆角 region」逻辑。
    /// 必须在 Opened 时（而非 HandleCreated）设置 — Handle 创建早于 Layout，此时 Width/Height 仍为 0。
    /// 用 Tag 防重，避免重复 attach 多次 SetWindowRgn。
    /// </summary>
    private static void AttachRoundCorner(ToolStripDropDown dropDown)
    {
        if (dropDown.Tag is bool attached && attached) return;
        dropDown.Tag = true;
        dropDown.Opened += static (s, _) =>
        {
            if (s is not ToolStripDropDown dd || dd.Handle == IntPtr.Zero) return;
            // CreateRoundRectRgn ellipse 参数是 full diameter，所以是 CornerRadius * 2。
            // x2/y2 +1 与 fence panel AcrylicCompositor 一致：避免 1px 边缘被切掉。
            var hRgn = CreateRoundRectRgn(0, 0, dd.Width + 1, dd.Height + 1,
                                          CornerRadius * 2, CornerRadius * 2);
            // SetWindowRgn 接管 hRgn 句柄 — 不要 DeleteObject。
            SetWindowRgn(dd.Handle, hRgn, true);
        };
    }

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateRoundRectRgn(int x1, int y1, int x2, int y2, int cx, int cy);

    [DllImport("user32.dll")]
    private static extern int SetWindowRgn(IntPtr hWnd, IntPtr hRgn, [MarshalAs(UnmanagedType.Bool)] bool bRedraw);

    private sealed class DarkRenderer : ToolStripProfessionalRenderer
    {
        public DarkRenderer() : base(new DarkColorTable())
        {
            RoundedEdges = false;
        }

        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            e.TextColor = e.Item.Enabled ? Foreground : DisabledFore;
            base.OnRenderItemText(e);
        }

        protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e)
        {
            e.ArrowColor = e.Item.Enabled ? Foreground : DisabledFore;
            base.OnRenderArrow(e);
        }

        protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
        {
            using var pen = new Pen(SeparatorColor);
            int y = e.Item.Height / 2;
            e.Graphics.DrawLine(pen, 8, y, e.Item.Width - 8, y);
        }

        protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
        {
            using var brush = new SolidBrush(Background);
            e.Graphics.FillRectangle(brush, e.AffectedBounds);
        }

        protected override void OnRenderImageMargin(ToolStripRenderEventArgs e)
        {
            using var brush = new SolidBrush(Background);
            e.Graphics.FillRectangle(brush, e.AffectedBounds);
        }

        protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
        {
            // 圆角 region 已经裁掉超出区域，这里不再画方形 1px 边框（否则四角会显方）。
        }
    }

    private sealed class DarkColorTable : ProfessionalColorTable
    {
        public override Color ToolStripDropDownBackground       => Background;
        public override Color MenuStripGradientBegin            => Background;
        public override Color MenuStripGradientEnd              => Background;
        public override Color MenuItemSelected                  => HoverBackground;
        public override Color MenuItemSelectedGradientBegin     => HoverBackground;
        public override Color MenuItemSelectedGradientEnd       => HoverBackground;
        public override Color MenuItemPressedGradientBegin      => HoverBackground;
        public override Color MenuItemPressedGradientMiddle     => HoverBackground;
        public override Color MenuItemPressedGradientEnd        => HoverBackground;
        public override Color MenuItemBorder                    => HoverBackground;
        public override Color MenuBorder                        => BorderColor;
        public override Color ImageMarginGradientBegin          => Background;
        public override Color ImageMarginGradientMiddle         => Background;
        public override Color ImageMarginGradientEnd            => Background;
        public override Color SeparatorDark                     => SeparatorColor;
        public override Color SeparatorLight                    => SeparatorColor;
        public override Color CheckBackground                   => HoverBackground;
        public override Color CheckSelectedBackground           => HoverBackground;
        public override Color CheckPressedBackground            => HoverBackground;
        public override Color ButtonSelectedHighlight           => HoverBackground;
        public override Color ButtonSelectedHighlightBorder     => HoverBackground;
    }
}
