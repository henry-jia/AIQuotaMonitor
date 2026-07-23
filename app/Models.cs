using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace AIQuotaMonitor;

public abstract class ObservableObject : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        return true;
    }
}

/// <summary>应用配置（持久化到 config.json，camelCase）。</summary>
public class AppConfig : ObservableObject
{
    private IList<ServiceConfig> _services = new List<ServiceConfig>();
    public IList<ServiceConfig> Services { get => _services; set => Set(ref _services, value); }

    private int _refreshIntervalMinutes = 5;
    public int RefreshIntervalMinutes { get => _refreshIntervalMinutes; set => Set(ref _refreshIntervalMinutes, Math.Max(1, value)); }

    /// <summary>"vertical" 纵向罗列 / "horizontal" 横向铺开。</summary>
    private string _layout = "vertical";
    public string Layout { get => _layout; set => Set(ref _layout, value); }

    private bool _topmost = true;
    public bool Topmost { get => _topmost; set => Set(ref _topmost, value); }

    private double _opacity = 0.92;
    public double Opacity { get => _opacity; set => Set(ref _opacity, Math.Clamp(value, 0.3, 1.0)); }

    public double WindowLeft { get; set; } = double.NaN;
    public double WindowTop { get; set; } = double.NaN;

    /// <summary>统一重置时间的显示格式（DateFormat 生效）。</summary>
    private bool _unifiedDateFormat = true;
    public bool UnifiedDateFormat { get => _unifiedDateFormat; set => Set(ref _unifiedDateFormat, value); }

    /// <summary>重置时间显示格式（.NET 格式串），设置界面提供预设也允许自定义。</summary>
    private string _dateFormat = "MM-dd HH:mm";
    public string DateFormat { get => _dateFormat; set => Set(ref _dateFormat, value); }

    /// <summary>显示剩余重置时间：临近显示「N 小时/分后重置」，超过阈值（RemainingThresholdDays）直接显示具体日期。</summary>
    private bool _showRemainingTime = true;
    public bool ShowRemainingTime { get => _showRemainingTime; set => Set(ref _showRemainingTime, value); }

    /// <summary>在进度条上显示时间基准竖线（按时间均匀消耗此刻应到的位置）。</summary>
    private bool _showPaceBaseline = true;
    public bool ShowPaceBaseline { get => _showPaceBaseline; set => Set(ref _showPaceBaseline, value); }

    /// <summary>全局暂停抓取（不访问任何供应商）；界面倒计时/基准线照常每分钟更新。</summary>
    private bool _scrapingPaused;
    public bool ScrapingPaused { get => _scrapingPaused; set => Set(ref _scrapingPaused, value); }

    /// <summary>剩余时间倒计时显示的阈值（天）：重置时间距今少于此值显示倒计时，否则显示具体日期。</summary>
    private int _remainingThresholdDays = 7;
    public int RemainingThresholdDays { get => _remainingThresholdDays; set => Set(ref _remainingThresholdDays, Math.Clamp(value, 1, 365)); }

    /// <summary>UI 语言："auto" 跟随系统 / "zh" 中文 / "en" English。</summary>
    private string _language = "auto";
    public string Language { get => _language; set => Set(ref _language, value); }

    /// <summary>全局颜色主题（预设 key 或自定义 hex）。缺省蔚蓝。</summary>
    private ThemeConfig _theme = new();
    public ThemeConfig Theme { get => _theme; set => Set(ref _theme, value); }

    [System.Text.Json.Serialization.JsonIgnore]
    public bool IsHorizontal => Layout == "horizontal";
}

/// <summary>
/// 全局颜色主题：Name 为预设 key（azure/emerald/violet/sunset/graphite）或 "custom"。
/// custom 时下面五个 hex 生效（可空，空则回落预设 azure 对应色）。
/// </summary>
public class ThemeConfig : ObservableObject
{
    private string _name = "azure";
    public string Name { get => _name; set => Set(ref _name, value); }

    private string? _accent;
    public string? Accent { get => _accent; set => Set(ref _accent, value); }

    private string? _normal;
    public string? Normal { get => _normal; set => Set(ref _normal, value); }

    private string? _near;
    public string? Near { get => _near; set => Set(ref _near, value); }

    private string? _ahead;
    public string? Ahead { get => _ahead; set => Set(ref _ahead, value); }

    private string? _critical;
    public string? Critical { get => _critical; set => Set(ref _critical, value); }
}

public class ServiceConfig : ObservableObject
{
    private string _name = "";
    public string Name { get => _name; set => Set(ref _name, value); }

    /// <summary>历史遗留：旧版的服务级主题色。现由全局颜色主题（AppConfig.Theme）取代，
    /// 字段仅为兼容旧 config.json 保留，渲染与设置界面均不再使用。</summary>
    private string _color = "#4F8CFF";
    public string Color { get => _color; set => Set(ref _color, value); }

    private string _url = "";
    public string Url { get => _url; set => Set(ref _url, value); }

    private bool _enabled = true;
    public bool Enabled { get => _enabled; set => Set(ref _enabled, value); }

    /// <summary>暂停该服务的抓取（不访问供应商）；界面倒计时/基准线照常更新。</summary>
    private bool _paused;
    public bool Paused { get => _paused; set => Set(ref _paused, value); }

    /// <summary>页面加载完成后的额外等待秒数（SPA 异步渲染）。</summary>
    private double _extraWaitSeconds = 2;
    public double ExtraWaitSeconds { get => _extraWaitSeconds; set => Set(ref _extraWaitSeconds, value); }

    /// <summary>可选：页面上命中该选择器即判定为未登录。</summary>
    private string? _loginIndicatorSelector;
    public string? LoginIndicatorSelector { get => _loginIndicatorSelector; set => Set(ref _loginIndicatorSelector, value); }

    /// <summary>可选：覆盖全局刷新间隔（分钟）。</summary>
    private int? _refreshIntervalMinutes;
    public int? RefreshIntervalMinutes { get => _refreshIntervalMinutes; set => Set(ref _refreshIntervalMinutes, value); }

    /// <summary>可选：订阅到期/自动续费信息所在页面。留空 = 在用量页面上查找；
    /// 信息在其他页面时填写（如智谱的套餐概览页、Codex 的 Billing 页）。</summary>
    private string? _subscriptionUrl;
    public string? SubscriptionUrl { get => _subscriptionUrl; set => Set(ref _subscriptionUrl, value); }

    private IList<QuotaRule> _rules = new List<QuotaRule>();
    public IList<QuotaRule> Rules { get => _rules; set => Set(ref _rules, value); }
}

public class QuotaRule : ObservableObject
{
    public const string TypePercent = "percent";
    public const string TypeFraction = "fraction";
    public const string DefaultPercentPattern = @"(\d+(?:\.\d+)?)\s*%";
    public const string DefaultFractionPattern = @"(\d+)\s*/\s*(\d+)";
    /// <summary>自动定位模式下未填重置正则时使用的默认识别
    /// （「重置时间：…」「…后重置」「将于…重置」「2026-07-23 15:42:00 重置」「Resets in …」「Resets Jul 29…」）。</summary>
    public const string DefaultResetPattern =
        @"(?:重置(?:时间|日期)?[：:]|Resets?(?:\s+in)?|将于)\s*[^\n]{1,40}|[^\n]{1,30}后重置" +
        @"|\d{4}\s*[-/年.]\s*\d{1,2}\s*[-/月.]\s*\d{1,2}\s*日?[^\n]{0,20}?重置";

    private string _label = "";
    public string Label { get => _label; set => Set(ref _label, value); }

    /// <summary>可选。自动定位用的锚点文本，留空 = 用标签定位；
    /// 多语言页面可填多个别名，用 | 分隔（如「5 小时用量|5-hour usage」），命中任意一个即可。</summary>
    private string? _matchText;
    public string? MatchText { get => _matchText; set => Set(ref _matchText, value); }

    /// <summary>CSS 选择器（高级用法）；留空 = 自动定位模式，按「标签」文字在页面上定位数值所在区域。</summary>
    private string? _selector;
    public string? Selector { get => _selector; set => Set(ref _selector, value); }

    private string _pattern = DefaultPercentPattern;
    public string Pattern { get => _pattern; set => Set(ref _pattern, value); }

    /// <summary>percent 百分比 / fraction 已用与总量。</summary>
    private string _type = TypePercent;
    public string Type { get => _type; set => Set(ref _type, value); }

    /// <summary>percent 类型下，页面数值为「剩余」百分比时开启：显示为 100 - 值（如 73% remaining → 已用 27%）。</summary>
    private bool _invert;
    public bool Invert { get => _invert; set => Set(ref _invert, value); }

    private string? _resetSelector;
    public string? ResetSelector { get => _resetSelector; set => Set(ref _resetSelector, value); }

    private string? _resetPattern;
    public string? ResetPattern { get => _resetPattern; set => Set(ref _resetPattern, value); }
}

public enum ScrapeStatus { Ok, NeedLogin, Error }

/// <summary>订阅信息：到期时间与自动续费状态（页面智能扫描，无需配置规则）。</summary>
public class SubscriptionInfo
{
    public DateTime? ExpireAt;
    /// <summary>true 开 / false 关 / null 未识别。</summary>
    public bool? AutoRenew;
}

public class RuleResult
{
    public string Label = "";
    public double? Percent;
    public string? Detail;
    public string? ResetText;
    /// <summary>抓取时解析出的绝对重置时间（本地时间），用于统一格式化与剩余时间计算。</summary>
    public DateTime? ResetAt;
    public string? RawText;
    public string? Error;
}

public class ServiceScrapeResult
{
    public ServiceConfig Service = new();
    public ScrapeStatus Status = ScrapeStatus.Ok;
    public string? ErrorMessage;
    /// <summary>失败原因可能是未登录，卡片上同样给出「去登录」按钮。</summary>
    public bool SuggestLogin;
    public List<RuleResult> Rules = new();
    public SubscriptionInfo? Subscription;
    public DateTimeOffset? SubscriptionFetchedAt;
    public DateTimeOffset Time = DateTimeOffset.Now;
}
