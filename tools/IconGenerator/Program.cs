using System.Drawing;
using System.Drawing.Drawing2D;

var outputPath = Path.GetFullPath(Path.Combine(
    AppDomain.CurrentDomain.BaseDirectory,
    "..", "..", "..", "..", "..",
    "src", "DesktopFences.App", "Assets", "app.ico"));

if (args.Length > 0)
    outputPath = args[0];

var sizes = new[] { 16, 32, 48, 256 };

static GraphicsPath RoundedRect(RectangleF rect, float radius)
{
    var path = new GraphicsPath();
    float d = radius * 2;
    if (d > rect.Width) d = rect.Width;
    if (d > rect.Height) d = rect.Height;
    path.AddArc(rect.X, rect.Y, d, d, 180, 90);
    path.AddArc(rect.Right - d, rect.Y, d, d, 270, 90);
    path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
    path.AddArc(rect.X, rect.Bottom - d, d, d, 90, 90);
    path.CloseFigure();
    return path;
}

static Bitmap DrawIcon(int size)
{
    var bmp = new Bitmap(size, size, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
    using var g = Graphics.FromImage(bmp);
    g.SmoothingMode = SmoothingMode.AntiAlias;
    g.PixelOffsetMode = PixelOffsetMode.HighQuality;
    g.Clear(Color.Transparent);

    // Background rounded rect
    float pad = size * 0.04f;
    float bgRadius = size * 0.18f;
    var bgRect = new RectangleF(pad, pad, size - pad * 2, size - pad * 2);
    using var bgBrush = new SolidBrush(Color.FromArgb(245, 22, 22, 36));
    using var bgPath = RoundedRect(bgRect, bgRadius);
    g.FillPath(bgBrush, bgPath);

    // Subtle border on background
    using var borderPen = new Pen(Color.FromArgb(60, 100, 140, 200), Math.Max(1, size * 0.02f));
    g.DrawPath(borderPen, bgPath);

    // 2x2 grid of rounded rects (desktop partition metaphor)
    float inset = size * 0.17f;
    float gap = size * 0.07f;
    float cellW = (size - inset * 2 - gap) / 2f;
    float cellH = (size - inset * 2 - gap) / 2f;
    float cellRadius = Math.Max(1, size * 0.07f);

    // Blue gradient colors for each cell
    var colors = new[]
    {
        (Color.FromArgb(230, 55, 120, 200), Color.FromArgb(230, 75, 145, 220)),   // top-left
        (Color.FromArgb(210, 80, 150, 220), Color.FromArgb(210, 100, 170, 235)),   // top-right
        (Color.FromArgb(200, 70, 140, 215), Color.FromArgb(200, 90, 160, 230)),    // bottom-left
        (Color.FromArgb(190, 95, 165, 235), Color.FromArgb(190, 115, 180, 245)),   // bottom-right
    };

    for (int r = 0; r < 2; r++)
    {
        for (int c = 0; c < 2; c++)
        {
            float x = inset + c * (cellW + gap);
            float y = inset + r * (cellH + gap);
            var cellRect = new RectangleF(x, y, cellW, cellH);
            var (c1, c2) = colors[r * 2 + c];

            using var gradBrush = new LinearGradientBrush(
                new PointF(x, y), new PointF(x + cellW, y + cellH), c1, c2);
            using var cellPath = RoundedRect(cellRect, cellRadius);
            g.FillPath(gradBrush, cellPath);

            // Subtle inner highlight
            using var highlightPen = new Pen(Color.FromArgb(40, 255, 255, 255), Math.Max(0.5f, size * 0.01f));
            g.DrawPath(highlightPen, cellPath);
        }
    }

    return bmp;
}

// Generate ICO file
using var ms = new MemoryStream();
using var writer = new BinaryWriter(ms);

// ICO header
writer.Write((short)0);      // reserved
writer.Write((short)1);      // type: icon
writer.Write((short)sizes.Length); // count

var imageDataList = new List<byte[]>();
foreach (var size in sizes)
{
    using var bmp = DrawIcon(size);
    using var pngMs = new MemoryStream();
    bmp.Save(pngMs, System.Drawing.Imaging.ImageFormat.Png);
    imageDataList.Add(pngMs.ToArray());
}

// Directory entries
int offset = 6 + sizes.Length * 16;
for (int i = 0; i < sizes.Length; i++)
{
    byte w = (byte)(sizes[i] >= 256 ? 0 : sizes[i]);
    writer.Write(w);          // width
    writer.Write(w);          // height
    writer.Write((byte)0);    // color palette
    writer.Write((byte)0);    // reserved
    writer.Write((short)1);   // color planes
    writer.Write((short)32);  // bits per pixel
    writer.Write(imageDataList[i].Length);
    writer.Write(offset);
    offset += imageDataList[i].Length;
}

foreach (var data in imageDataList)
    writer.Write(data);

Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
File.WriteAllBytes(outputPath, ms.ToArray());
Console.WriteLine($"Generated: {outputPath} ({ms.Length} bytes)");
