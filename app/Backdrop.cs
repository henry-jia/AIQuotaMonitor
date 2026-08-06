using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace AIQuotaMonitor;

/// <summary>
/// Win11 DWM 背景材质：Acrylic（悬浮小部件用，含噪点毛玻璃）/ Mica（应用窗口用）。
/// 22621+ 用 SYSTEMBACKDROP_TYPE；22000 仅圆角+深色边框；Win10 回退 XAML 实色背景。
/// 开启材质时把窗口 Background 设为透明，让 DWM 画布透出；XAML 里的实色仅作旧系统兜底。
/// </summary>
public static class Backdrop
{
    public enum Kind { None, Mica, Acrylic }

    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
    private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
    private const int DWMWA_BORDER_COLOR = 34;
    private const int DWMWA_SYSTEMBACKDROP_TYPE = 38;
    private const int DWMWCP_ROUND = 2;
    private const uint DWMWA_COLOR_NONE = 0xFFFFFFFE;
    private const int DWMSBT_MAINWINDOW = 2;      // Mica
    private const int DWMSBT_TRANSIENTWINDOW = 3; // Acrylic

    [StructLayout(LayoutKind.Sequential)]
    private struct MARGINS { public int Left, Right, Top, Bottom; }

    [DllImport("dwmapi.dll")]
    private static extern int DwmExtendFrameIntoClientArea(IntPtr hwnd, ref MARGINS margins);

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref uint value, int size);

    /// <summary>在 SourceInitialized 挂钩应用（hwnd 就绪时）；自移除避免重复调用叠加回调。</summary>
    public static void Apply(Window window, Kind kind, bool hideBorder = false)
    {
        void OnSourceInitialized(object? s, EventArgs e)
        {
            window.SourceInitialized -= OnSourceInitialized;
            ApplyNow(window, kind, hideBorder);
        }
        window.SourceInitialized += OnSourceInitialized;
    }

    private static void ApplyNow(Window window, Kind kind, bool hideBorder)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero) return;

        int dark = 1;
        DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref dark, 4);
        int round = DWMWCP_ROUND;
        DwmSetWindowAttribute(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref round, 4);
        if (hideBorder)
        {
            uint none = DWMWA_COLOR_NONE;
            DwmSetWindowAttribute(hwnd, DWMWA_BORDER_COLOR, ref none, 4);
        }

        int build = Environment.OSVersion.Version.Build;
        if (build < 22000 || kind == Kind.None) return; // Win10：保留 XAML 实色背景兜底

        // DWM 画布铺满客户区；窗口背景转透明让材质透出（内容层的 tint 负责压暗）
        window.Background = Brushes.Transparent;
        var margins = new MARGINS { Left = -1, Right = -1, Top = -1, Bottom = -1 };
        DwmExtendFrameIntoClientArea(hwnd, ref margins);
        int type = kind == Kind.Acrylic ? DWMSBT_TRANSIENTWINDOW : DWMSBT_MAINWINDOW;
        DwmSetWindowAttribute(hwnd, DWMWA_SYSTEMBACKDROP_TYPE, ref type, 4);
    }
}
