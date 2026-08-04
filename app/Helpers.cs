using System;
using System.Windows.Media;
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

/// <summary>托盘图标：复用 exe 图标资源 app.ico（多尺寸仪表盘标记）。</summary>
public static class IconHelper
{
    public static System.Drawing.Icon CreateIcon()
    {
        var stream = System.Windows.Application.GetResourceStream(
            new Uri("pack://application:,,,/app.ico"))!.Stream;
        return new System.Drawing.Icon(stream, 32, 32);
    }
}
