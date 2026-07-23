using System.Collections.Generic;
using System.Windows.Media;

namespace AIQuotaMonitor;

/// <summary>一套解析完成的主题色（渲染用）。</summary>
public sealed class ResolvedTheme
{
    public Color Accent;
    public Color Normal;
    public Color Near;
    public Color Ahead;
    public Color Critical;
}

/// <summary>
/// 全局颜色主题：内置预设 + 自定义。
/// 语义色：Accent 强调（圆点）/ Normal 正常 / Near 接近时间基准 / Ahead 超出时间基准 / Critical ≥90% 告警。
/// </summary>
public static class ColorTheme
{
    public const string CustomKey = "custom";

    /// <summary>预设主题（key, 显示名 i18n key, Accent, Normal, Near, Ahead, Critical）。</summary>
    public static readonly IReadOnlyList<(string Key, string NameKey, string Accent, string Normal, string Near, string Ahead, string Critical)> Presets =
        new List<(string, string, string, string, string, string, string)>
        {
            ("azure",    "theme_azure",    "#4F8CFF", "#4F8CFF", "#E0B050", "#E5783A", "#E5534B"),
            ("emerald",  "theme_emerald",  "#34C08E", "#34C08E", "#D9B84A", "#E58A3C", "#E5534B"),
            ("violet",   "theme_violet",   "#8B7CF6", "#8B7CF6", "#E0B050", "#E5783A", "#E5534B"),
            ("sunset",   "theme_sunset",   "#F0A35E", "#F0A35E", "#F2C14E", "#E5733A", "#D94F4F"),
            ("graphite", "theme_graphite", "#9AA4B2", "#9AA4B2", "#D9B84A", "#E58A3C", "#E5534B"),
        };

    private static (string Key, string NameKey, string Accent, string Normal, string Near, string Ahead, string Critical) FindPreset(string? key)
    {
        foreach (var p in Presets)
            if (p.Key == key) return p;
        return Presets[0]; // 未知 key 回落 azure
    }

    /// <summary>按配置解析出渲染用的五个颜色。custom 时读 Theme 里的 hex，缺项/非法回落 azure 对应色。</summary>
    public static ResolvedTheme Resolve(AppConfig cfg)
    {
        var theme = cfg.Theme ?? new ThemeConfig();
        if (theme.Name == CustomKey)
        {
            var fb = Presets[0]; // azure 兜底
            return new ResolvedTheme
            {
                Accent = Ui.ParseColor(theme.Accent, C(fb.Accent)),
                Normal = Ui.ParseColor(theme.Normal, C(fb.Normal)),
                Near = Ui.ParseColor(theme.Near, C(fb.Near)),
                Ahead = Ui.ParseColor(theme.Ahead, C(fb.Ahead)),
                Critical = Ui.ParseColor(theme.Critical, C(fb.Critical)),
            };
        }
        var p = FindPreset(theme.Name);
        return new ResolvedTheme
        {
            Accent = C(p.Accent),
            Normal = C(p.Normal),
            Near = C(p.Near),
            Ahead = C(p.Ahead),
            Critical = C(p.Critical),
        };
    }

    private static Color C(string hex) => (Color)ColorConverter.ConvertFromString(hex);

    public static string Hex(Color c) => $"#{c.R:X2}{c.G:X2}{c.B:X2}";
}
