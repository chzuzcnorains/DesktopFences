// C# Script to generate app.ico for DesktopFences
// Design: 2x2 rounded rectangle grid (desktop partition metaphor), blue #4488CC on dark background
// Run with: dotnet-script GenerateIcon.csx

using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Collections.Generic;

var sizes = new[] { 16, 32, 48, 256 };
var outputPath = Path.Combine(Path.GetDirectoryName(Args.Count > 0 ? Args[0] : "."),
    "..", "src", "DesktopFences.App", "Assets", "app.ico");

if (Args.Count > 0)
    outputPath = Args[0];
else
    outputPath = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,
        "..", "..", "..", "src", "DesktopFences.App", "Assets", "app.ico"));

Bitmap DrawIcon(int size)
{
    var bmp = new Bitmap(size, size, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
    using var g = Graphics.FromImage(bmp);
    g.SmoothingMode = SmoothingMode.AntiAlias;
    g.Clear(Color.Transparent);

    // Background circle/rounded rect
    float pad = size * 0.06f;
    float bgRadius = size * 0.2f;
    var bgRect = new RectangleF(pad, pad, size - pad * 2, size - pad * 2);
    using var bgBrush = new SolidBrush(Color.FromArgb(240, 24, 24, 38)); // #1E1E26
    using var bgPath = RoundedRect(bgRect, bgRadius);
    g.FillPath(bgBrush, bgPath);

    // 2x2 grid of rounded rects
    float inset = size * 0.18f;
    float gap = size * 0.06f;
    float cellW = (size - inset * 2 - gap) / 2f;
    float cellH = (size - inset * 2 - gap) / 2f;
    float cellRadius = size * 0.08f;

    var colors = new[] {
        Color.FromArgb(220, 68, 136, 204),  // #4488CC - main blue
        Color.FromArgb(200, 88, 156, 224),  // lighter blue
        Color.FromArgb(180, 78, 146, 214),  // medium blue
        Color.FromArgb(160, 98, 166, 234),  // lightest blue
    };

    for (int r = 0; r < 2; r++)
    {
        for (int c = 0; c < 2; c++)
        {
            float x = inset + c * (cellW + gap);
            float y = inset + r * (cellH + gap);
            var cellRect = new RectangleF(x, y, cellW, cellH);
            using var cellBrush = new SolidBrush(colors[r * 2 + c]);
            using var cellPath = RoundedRect(cellRect, cellRadius);
            g.FillPath(cellBrush, cellPath);
        }
    }

    return bmp;
}

GraphicsPath RoundedRect(RectangleF rect, float radius)
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

// Generate ICO file
using var ms = new MemoryStream();
using var writer = new BinaryWriter(ms);

// ICO header
writer.Write((short)0);     // reserved
writer.Write((short)1);     // type: icon
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
int offset = 6 + sizes.Length * 16; // header + entries
for (int i = 0; i < sizes.Length; i++)
{
    byte w = (byte)(sizes[i] >= 256 ? 0 : sizes[i]);
    byte h = w;
    writer.Write(w);          // width
    writer.Write(h);          // height
    writer.Write((byte)0);    // color palette
    writer.Write((byte)0);    // reserved
    writer.Write((short)1);   // color planes
    writer.Write((short)32);  // bits per pixel
    writer.Write(imageDataList[i].Length); // size
    writer.Write(offset);     // offset
    offset += imageDataList[i].Length;
}

// Image data
foreach (var data in imageDataList)
    writer.Write(data);

File.WriteAllBytes(outputPath, ms.ToArray());
Console.WriteLine($"Generated: {outputPath} ({ms.Length} bytes)");
