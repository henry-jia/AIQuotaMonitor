using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace AIQuotaMonitor;

/// <summary>
/// Win11 背景材质：Acrylic（悬浮小部件用）走 ACCENT 接口——纯模糊+自定义 tint，
/// 没有系统材质固定的亮度混合（白膜感）、失焦不退化成灰白实底；失败时回退 SYSTEMBACKDROP_TYPE。
/// Mica（应用窗口用）走 SYSTEMBACKDROP_TYPE。Win10 回退 XAML 实色背景。
/// 开启材质时把渲染目标清屏色与窗口 Background 设为透明，让模糊透出；XAML 里的实色仅作旧系统兜底。
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
    private const int DWMSBT_TRANSIENTWINDOW = 3; // Acrylic（回退路径用）

    private const int WCA_ACCENT_POLICY = 19;
    private const int ACCENT_ENABLE_ACRYLICBLURBEHIND = 4;

    private const int WM_DPICHANGED = 0x02E0;
    private const int WM_EXITSIZEMOVE = 0x0232;
    private const int WM_DWMCOMPOSITIONCHANGED = 0x031E;

    [StructLayout(LayoutKind.Sequential)]
    private struct MARGINS { public int Left, Right, Top, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    private struct AccentPolicy { public int AccentState; public int AccentFlags; public int GradientColor; public int AnimationId; }

    [StructLayout(LayoutKind.Sequential)]
    private struct WindowCompositionAttributeData { public int Attribute; public IntPtr Data; public int SizeOfData; }

    [DllImport("dwmapi.dll")]
    private static extern int DwmExtendFrameIntoClientArea(IntPtr hwnd, ref MARGINS margins);

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref uint value, int size);

    [DllImport("user32.dll")]
    private static extern bool SetWindowCompositionAttribute(IntPtr hwnd, ref WindowCompositionAttributeData data);

    /// <summary>在 SourceInitialized 挂钩应用（hwnd 就绪时）；自移除避免重复调用叠加回调。</summary>
    public static void Apply(Window window, Kind kind, bool hideBorder = false)
    {
        void OnSourceInitialized(object? s, EventArgs e)
        {
            window.SourceInitialized -= OnSourceInitialized;
            ApplyNow(window, kind, hideBorder);
            HookReapply(window, kind);
            // 首帧渲染后重应用一次：窗口未显示前设置的材质可能不生效（启动即全黑）
            void OnContentRendered(object? s2, EventArgs e2)
            {
                window.ContentRendered -= OnContentRendered;
                var hwnd = new WindowInteropHelper(window).Handle;
                if (hwnd != IntPtr.Zero) Reapply(hwnd, kind);
            }
            window.ContentRendered += OnContentRendered;
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

        // WPF 渲染目标先用 HwndTarget.BackgroundColor（默认不透明黑）清屏再画可视树，
        // DWM 材质只在 alpha=0 处透出——不设透明则启动全黑、局部重绘区域变成不透明黑块。
        if (PresentationSource.FromVisual(window) is HwndSource { CompositionTarget: { } target })
            target.BackgroundColor = Colors.Transparent;

        // 窗口背景转透明让模糊透出（内容层的 tint 负责压暗）
        window.Background = Brushes.Transparent;

        // 小部件 Acrylic 走 ACCENT 接口：不能与玻璃帧扩展共存，成功即返回；失败回退系统材质
        if (kind == Kind.Acrylic && TryApplyAccentAcrylic(hwnd)) return;

        // DWM 画布铺满客户区 + 系统材质（Mica，或 Acrylic 回退路径）
        var margins = new MARGINS { Left = -1, Right = -1, Top = -1, Bottom = -1 };
        DwmExtendFrameIntoClientArea(hwnd, ref margins);
        int type = kind == Kind.Acrylic ? DWMSBT_TRANSIENTWINDOW : DWMSBT_MAINWINDOW;
        DwmSetWindowAttribute(hwnd, DWMWA_SYSTEMBACKDROP_TYPE, ref type, 4);
    }

    /// <summary>
    /// ACCENT 亚克力：模糊窗口后方内容；GradientColor（ABGR）只给极轻 tint，压暗交给 XAML tint。
    /// alpha 不能为 0（触发 DWM 渲染异常），取最小值 0x01。
    /// </summary>
    private static bool TryApplyAccentAcrylic(IntPtr hwnd)
    {
        var accent = new AccentPolicy
        {
            AccentState = ACCENT_ENABLE_ACRYLICBLURBEHIND,
            AccentFlags = 0,
            GradientColor = 0x0114141B,
            AnimationId = 0,
        };
        var ptr = Marshal.AllocHGlobal(Marshal.SizeOf(accent));
        try
        {
            Marshal.StructureToPtr(accent, ptr, false);
            var data = new WindowCompositionAttributeData
            {
                Attribute = WCA_ACCENT_POLICY,
                Data = ptr,
                SizeOfData = Marshal.SizeOf(accent),
            };
            return SetWindowCompositionAttribute(hwnd, ref data);
        }
        finally
        {
            Marshal.FreeHGlobal(ptr);
        }
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

    /// <summary>Acrylic 重设 ACCENT；Mica（或 ACCENT 失败的回退）重铺玻璃帧 + 关开材质，强制 DWM 重算整块背景。</summary>
    private static void Reapply(IntPtr hwnd, Kind kind)
    {
        if (kind == Kind.Acrylic && TryApplyAccentAcrylic(hwnd)) return;
        var margins = new MARGINS { Left = -1, Right = -1, Top = -1, Bottom = -1 };
        DwmExtendFrameIntoClientArea(hwnd, ref margins);
        int off = DWMSBT_NONE;
        DwmSetWindowAttribute(hwnd, DWMWA_SYSTEMBACKDROP_TYPE, ref off, 4);
        int type = kind == Kind.Acrylic ? DWMSBT_TRANSIENTWINDOW : DWMSBT_MAINWINDOW;
        DwmSetWindowAttribute(hwnd, DWMWA_SYSTEMBACKDROP_TYPE, ref type, 4);
    }
}
