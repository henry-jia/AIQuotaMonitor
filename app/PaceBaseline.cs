using System;
using System.Text.RegularExpressions;

namespace AIQuotaMonitor;

/// <summary>
/// 时间基准线：对时间窗口型配额（5 小时 / 7 天 / 30 天），计算「按时间均匀消耗，
/// 此刻用量应该到达的比例」，用于在进度条上画基准竖线与实际用量对比。
/// 窗口长度从规则标签推断；窗口起点 = 重置时间 − 窗口长度。
/// </summary>
public static class PaceBaseline
{
    // (?<![A-Za-z]) 防止吃掉单词内数字（如 "100dpi" 误判为 100 天窗口）
    private static readonly Regex Hours = new(
        @"(?<![A-Za-z])(\d+(?:\.\d+)?)\s*(?:个?小时|hours?\b|hrs?\b|h\b)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex Days = new(
        @"(?<![A-Za-z])(\d+(?:\.\d+)?)\s*(?:天|日|days?\b|d\b)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex Week = new(@"周|week", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex Month = new(@"月|month", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>从标签推断窗口长度（小时）。无法推断返回 null。
    /// 识别：「5 小时」「5-hour」「7 天」「7-day」「每周/周/week」→7 天、「每月/月/month」→30 天。</summary>
    public static double? WindowHours(string? label)
    {
        if (string.IsNullOrWhiteSpace(label)) return null;
        var m = Hours.Match(label);
        if (m.Success && double.TryParse(m.Groups[1].Value, out var h) && h > 0) return h;
        m = Days.Match(label);
        if (m.Success && double.TryParse(m.Groups[1].Value, out var d) && d > 0) return d * 24;
        if (Week.IsMatch(label)) return 7 * 24;
        if (Month.IsMatch(label)) return 30 * 24;
        return null;
    }

    /// <summary>窗口已过去的比例（0~1）。缺重置时间返回 null（不画基准线）。
    /// 标签推断失败时，按剩余时间归入最近的窗口档（5h / 7d / 30d），
    /// 让「总使用量」这类无窗口关键词的标签也能有基准线。</summary>
    public static double? Elapsed(string? label, DateTime? resetAt, DateTime now)
    {
        if (resetAt == null) return null;
        double remainingHours = (resetAt.Value - now).TotalHours;
        var windowHours = WindowHours(label) ?? InferFromRemaining(remainingHours);
        if (windowHours == null) return null;
        return Math.Clamp(1 - remainingHours / windowHours.Value, 0, 1);
    }

    /// <summary>按剩余时间归入最近的配额窗口档；超出 30 天或已过期返回 null。</summary>
    private static double? InferFromRemaining(double remainingHours)
    {
        if (remainingHours <= 0) return null;
        if (remainingHours <= 5) return 5;
        if (remainingHours <= 7 * 24) return 7 * 24;
        if (remainingHours <= 30 * 24) return 30 * 24;
        return null;
    }
}
