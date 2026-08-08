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
    private const int DWMSBT_NONE = 1;            // 关闭材质（重应用时先关再开）
    private const int DWMSBT_MAINWINDOW = 2;      // Mica
    private const int DWMSBT_TRANSIENTWINDOW = 3; // Acrylic

    private const int WM_DPICHANGED = 0x02E0;
    private const int WM_EXITSIZEMOVE = 0x0232;
    private const int WM_DWMCOMPOSITIONCHANGED = 0x031E;

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
            HookReapply(window, kind);
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

    /// <summary>
    /// 跨屏拖动 / DPI 变化 / DWM 重组时材质可能脱落（透明区渲染成黑色斑块），
    /// 监听对应消息并重应用材质。hook 挂在 HwndSource 上，随窗口关闭自动释放。
    /// </summary>
    private static void HookReapply(Window window, Kind kind)
    {
        if (Environment.OSVersion.Version.Build < 22000 || kind == Kind.None) return;
        if (PresentationSource.FromVisual(window) is not HwndSource source) return;
        source.AddHook((IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled) =>
        {
            if (msg == WM_EXITSIZEMOVE || msg == WM_DPICHANGED || msg == WM_DWMCOMPOSITIONCHANGED)
                Reapply(hwnd, kind);
            return IntPtr.Zero;
        });
    }

    /// <summary>重铺玻璃帧 + 关开材质，强制 DWM 重算整块背景。</summary>
    private static void Reapply(IntPtr hwnd, Kind kind)
    {
        var margins = new MARGINS { Left = -1, Right = -1, Top = -1, Bottom = -1 };
        DwmExtendFrameIntoClientArea(hwnd, ref margins);
        int off = DWMSBT_NONE;
        DwmSetWindowAttribute(hwnd, DWMWA_SYSTEMBACKDROP_TYPE, ref off, 4);
        int type = kind == Kind.Acrylic ? DWMSBT_TRANSIENTWINDOW : DWMSBT_MAINWINDOW;
        DwmSetWindowAttribute(hwnd, DWMWA_SYSTEMBACKDROP_TYPE, ref type, 4);
    }
}
