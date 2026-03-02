using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;

static void DrawIcon(Graphics g, int size)
{
    g.SmoothingMode = SmoothingMode.AntiAlias;
    g.Clear(Color.Transparent);
    var pad = Math.Max(2, size / 16);
    using (var brush = new SolidBrush(Color.FromArgb(230, 81, 0)))
        g.FillEllipse(brush, pad, pad, size - 2 * pad, size - 2 * pad);
    using (var penOutline = new Pen(Color.FromArgb(200, 60, 0), Math.Max(1, size / 64)))
        g.DrawEllipse(penOutline, pad, pad, size - 2 * pad, size - 2 * pad);
    using (var pen = new Pen(Color.White, Math.Max(2, size / 10)))
    {
        pen.StartCap = pen.EndCap = LineCap.Round;
        g.DrawLine(pen, size * 8 / 32, size * 16 / 32, size * 14 / 32, size * 23 / 32);
        g.DrawLine(pen, size * 14 / 32, size * 23 / 32, size * 25 / 32, size * 9 / 32);
    }
}

string outPath = args.Length > 0 ? args[0] : Path.Combine("Planner.App", "app.ico");
outPath = Path.GetFullPath(outPath);
var dir = Path.GetDirectoryName(outPath);
if (!string.IsNullOrEmpty(dir))
    Directory.CreateDirectory(dir);

using (var bmp = new Bitmap(256, 256))
{
    using (var g = Graphics.FromImage(bmp))
        DrawIcon(g, 256);
    var icon = Icon.FromHandle(bmp.GetHicon());
    using (var cloned = (Icon)icon.Clone())
    using (var fs = File.Create(outPath))
        cloned.Save(fs);
}
Console.WriteLine("Created: " + outPath);
