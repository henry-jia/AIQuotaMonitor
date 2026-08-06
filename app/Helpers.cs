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

    /// <summary>开机自启：HKCU Run 键。启用时写当前 exe 路径（移动目录后启动会自愈），停用移除。</summary>
    public static class AutoStart
    {
        private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string Name = "AIQuotaMonitor";

        public static void Sync(bool enabled)
        {
            try
            {
                using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(RunKey, true);
                if (key == null) return;
                if (enabled) key.SetValue(Name, Environment.ProcessPath ?? "");
                else key.DeleteValue(Name, false);
            }
            catch
            {
                // 注册表不可写等场景尽力而为
            }
        }
    }

    /// <summary>命中源是否在按钮上（拖动/点击判定共用，避免各处复制视觉树遍历）。</summary>
    public static bool IsOnButton(System.Windows.DependencyObject? d)
    {
        while (d != null)
        {
            if (d is System.Windows.Controls.Primitives.ButtonBase) return true;
            d = System.Windows.Media.VisualTreeHelper.GetParent(d) ?? (d as System.Windows.FrameworkElement)?.Parent;
        }
        return false;
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
