using System.Drawing;
using System.Windows.Forms;

namespace Planner.App.Services;

/// <summary>Создаёт иконку для трея: оранжевый круг с галочкой (прикольно и узнаваемо).</summary>
public static class TrayIconService
{
    public static Icon CreateTrayIcon()
    {
        const int size = 32;
        using var bmp = new Bitmap(size, size);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);
            // Оранжевый круг с лёгкой обводкой
            using var brush = new SolidBrush(Color.FromArgb(230, 81, 0));
            g.FillEllipse(brush, 2, 2, size - 4, size - 4);
            using var penOutline = new Pen(Color.FromArgb(200, 60, 0), 1f);
            g.DrawEllipse(penOutline, 2, 2, size - 4, size - 4);
            // Белая жирная галочка ✓
            using var pen = new Pen(Color.White, 3f);
            pen.StartCap = pen.EndCap = System.Drawing.Drawing2D.LineCap.Round;
            g.DrawLine(pen, 8, 16, 14, 23);
            g.DrawLine(pen, 14, 23, 25, 9);
        }
        var hIcon = bmp.GetHicon();
        var icon = Icon.FromHandle(hIcon);
        return (Icon)icon.Clone();
    }
}
