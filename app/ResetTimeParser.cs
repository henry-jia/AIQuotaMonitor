using System;
using System.Globalization;
using System.Text.RegularExpressions;

namespace AIQuotaMonitor;

/// <summary>
/// 把各供应商五花八门的重置时间文本解析为绝对时间，并按全局设置格式化展示。
/// 支持的原文示例：「重置时间：16:33」「重置时间：2026-07-26 10:00」「07-23 14:44 后重置」
/// 「2026-08-17 后重置」「Resets in 07-23 14:44」「3 天后重置」「2 小时 30 分后重置」等。
/// </summary>
public static class ResetTimeParser
{
    private static readonly Regex FullDateTime = new(
        @"(?<y>\d{4})\s*[-/年.]\s*(?<mo>\d{1,2})\s*[-/月.]\s*(?<d>\d{1,2})\s*日?\s+(?<h>\d{1,2})\s*[:：]\s*(?<mi>\d{2})",
        RegexOptions.Compiled);
    private static readonly Regex MonthDayTime = new(
        @"(?<mo>\d{1,2})\s*[-/月.]\s*(?<d>\d{1,2})\s*日?\s+(?<h>\d{1,2})\s*[:：]\s*(?<mi>\d{2})",
        RegexOptions.Compiled);
    private static readonly Regex FullDate = new(
        @"(?<y>\d{4})\s*[-/年.]\s*(?<mo>\d{1,2})\s*[-/月.]\s*(?<d>\d{1,2})\s*日?",
        RegexOptions.Compiled);
    private static readonly Regex MonthDay = new(
        @"(?<mo>\d{1,2})\s*[-/月.]\s*(?<d>\d{1,2})\s*日",
        RegexOptions.Compiled);
    private static readonly Regex RelDays = new(
        @"(\d+)\s*(?:天|days?\b)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex RelHours = new(
        @"(\d+)\s*(?:个?小时|hours?\b|hrs?\b)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex RelMinutes = new(
        @"(\d+)\s*(?:分钟|分|minutes?\b|mins?\b)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex TimeOnly = new(
        @"(?<h>\d{1,2})\s*[:：]\s*(?<mi>\d{2})", RegexOptions.Compiled);
    private static readonly Regex LeadWords = new(
        @"^\s*(?:重置(?:时间|日期)?|Resets?(?:\s+in)?)\s*[：:]?\s*",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex LooksLikeFullDate = new(
        @"\d{4}|[A-Za-z]{3,}\s+\d{1,2}\b", RegexOptions.Compiled);

    /// <summary>解析重置时间文本为绝对时间（本地时间）。无法解析返回 null。</summary>
    public static DateTime? Parse(string? raw, DateTime now)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;

        var m = FullDateTime.Match(raw);
        if (m.Success && TryBuild(G(m, "y"), G(m, "mo"), G(m, "d"), G(m, "h"), G(m, "mi"), out var dt))
            return dt;

        m = MonthDayTime.Match(raw);
        if (m.Success && TryBuild(now.Year, G(m, "mo"), G(m, "d"), G(m, "h"), G(m, "mi"), out dt))
        {
            if (dt < now.AddHours(-1)) dt = dt.AddYears(1); // 已过去则视为明年
            return dt;
        }

        m = FullDate.Match(raw);
        if (m.Success && TryBuild(G(m, "y"), G(m, "mo"), G(m, "d"), 0, 0, out dt))
            return dt;

        m = MonthDay.Match(raw);
        if (m.Success && TryBuild(now.Year, G(m, "mo"), G(m, "d"), 0, 0, out dt))
        {
            if (dt < now.Date.AddDays(-1)) dt = dt.AddYears(1);
            return dt;
        }

        // 相对时间：N 天 / N 小时 / N 分钟（可组合，中英文）
        int days = 0, hours = 0, minutes = 0;
        bool any = false;
        m = RelDays.Match(raw);
        if (m.Success) { days = int.Parse(m.Groups[1].Value); any = true; }
        m = RelHours.Match(raw);
        if (m.Success) { hours = int.Parse(m.Groups[1].Value); any = true; }
        m = RelMinutes.Match(raw);
        if (m.Success) { minutes = int.Parse(m.Groups[1].Value); any = true; }
        if (any && days + hours + minutes > 0)
            return now.AddDays(days).AddHours(hours).AddMinutes(minutes);

        // 兜底：英文月名等 BCL 能认的格式（如「Resets Jul 29, 2026 1:04 AM」）。
        // 仅在文本确实像完整日期（含 4 位年份或英文月名+日）时尝试，避免吃掉纯时间
        var cleaned = LeadWords.Replace(raw, "").Trim();
        if (LooksLikeFullDate.IsMatch(cleaned) &&
            DateTime.TryParse(cleaned, CultureInfo.InvariantCulture,
                DateTimeStyles.AllowWhiteSpaces, out dt))
        {
            dt = DateTime.SpecifyKind(dt, DateTimeKind.Local);
            if (dt < now.AddHours(-1)) dt = dt.AddYears(1); // 无年份日期已过去则视为明年
            return dt;
        }

        m = TimeOnly.Match(raw);
        if (m.Success && TryBuild(now.Year, now.Month, now.Day, G(m, "h"), G(m, "mi"), out dt))
        {
            if (dt <= now) dt = dt.AddDays(1); // 今天已过则视为明天
            return dt;
        }

        return null;
    }

    /// <summary>
    /// 按全局设置格式化重置时间展示：
    /// showRemaining 开启时临近重置显示「N 小时/分后重置」，超过 thresholdDays 天则直接显示具体日期；
    /// unified 开启时具体日期按 fmt 统一格式化。解析失败一律回退原文。
    /// </summary>
    public static string? Format(string? raw, DateTime? resetAt, DateTime now,
        bool unified, string? fmt, bool showRemaining, int thresholdDays = 7)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var dt = resetAt ?? Parse(raw, now);
        if (dt == null) return raw;

        string Formatted()
        {
            try
            {
                return dt.Value.ToString(
                    string.IsNullOrWhiteSpace(fmt) ? "MM-dd HH:mm" : fmt,
                    CultureInfo.CurrentCulture);
            }
            catch (FormatException)
            {
                return raw; // 用户自定义格式串非法时回退原文
            }
        }

        var delta = dt.Value - now;
        if (delta < TimeSpan.FromHours(-6)) return raw; // 解析结果明显过期，回退原文
        if (delta < TimeSpan.Zero) return I18n.T("resetting_soon");

        if (!showRemaining) return unified ? Formatted() : raw;

        if (delta.TotalMinutes < 60)
            return I18n.T("resets_in_min", Math.Max(1, (int)delta.TotalMinutes));
        if (delta.TotalHours < 24)
        {
            int h = (int)delta.TotalHours, mi = delta.Minutes;
            return mi > 0 ? I18n.T("resets_in_hours_min", h, mi) : I18n.T("resets_in_hours", h);
        }
        if (delta.TotalDays < thresholdDays)
        {
            int d = (int)delta.TotalDays, h = delta.Hours;
            return h > 0 ? I18n.T("resets_in_days_hours", d, h) : I18n.T("resets_in_days", d);
        }
        // 还很久：直接显示具体日期（格式随统一设置）
        return unified ? Formatted() : raw;
    }

    private static int G(Match m, string name) => int.Parse(m.Groups[name].Value);

    private static bool TryBuild(int y, int mo, int d, int h, int mi, out DateTime dt)
    {
        try
        {
            dt = new DateTime(y, mo, d, h, mi, 0, DateTimeKind.Local);
            return true;
        }
        catch (ArgumentOutOfRangeException)
        {
            dt = default;
            return false;
        }
    }
}
