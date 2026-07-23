using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Media;
using DBrush = System.Drawing.SolidBrush;
using DColor = System.Drawing.Color;
using WColor = System.Windows.Media.Color;

namespace AIQuotaMonitor;

public static class Ui
{
    /// <summary>解析 #RRGGBB 颜色文本，失败时返回回退色。</summary>
    public static WColor ParseColor(string? text, WColor fallback)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(text))
                return (WColor)System.Windows.Media.ColorConverter.ConvertFromString(text);
        }
        catch
        {
            // 用户输入非法颜色时静默回退
        }
        return fallback;
    }

    public static SolidColorBrush Brush(string hex)
    {
        var b = new SolidColorBrush((WColor)System.Windows.Media.ColorConverter.ConvertFromString(hex));
        b.Freeze();
        return b;
    }
}

/// <summary>运行时绘制托盘图标（避免打包 ico 资源）。</summary>
public static class IconHelper
{
    public static Icon CreateIcon()
    {
        using var bmp = new Bitmap(32, 32);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using var path = RoundedRect(new Rectangle(1, 1, 30, 30), 9);
            using var bg = new DBrush(DColor.FromArgb(255, 20, 20, 27));
            using var dot = new DBrush(DColor.FromArgb(255, 79, 140, 255));
            g.FillPath(bg, path);
            g.FillEllipse(dot, 9, 9, 14, 14);
        }
        return Icon.FromHandle(bmp.GetHicon());
    }

    private static GraphicsPath RoundedRect(Rectangle bounds, int radius)
    {
        int d = radius * 2;
        var path = new GraphicsPath();
        path.AddArc(bounds.X, bounds.Y, d, d, 180, 90);
        path.AddArc(bounds.Right - d, bounds.Y, d, d, 270, 90);
        path.AddArc(bounds.Right - d, bounds.Bottom - d, d, d, 0, 90);
        path.AddArc(bounds.X, bounds.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }
}
