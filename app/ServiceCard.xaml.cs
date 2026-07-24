using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace AIQuotaMonitor;

/// <summary>单个服务的配额卡片：正常 / 需要登录 / 抓取失败三种状态。</summary>
public partial class ServiceCard : UserControl
{
    private static readonly Color WarnColor = (Color)ColorConverter.ConvertFromString("#E0A050");
    private static readonly Color DangerColor = (Color)ColorConverter.ConvertFromString("#E5534B");

    public event Action<ServiceConfig>? RequestLogin;
    public event Action<ServiceConfig>? RequestRefresh;
    public event Action<ServiceConfig>? RequestPauseToggle;
    private ServiceConfig? _service;
    private bool _paused;
    private bool _busy;
    /// <summary>当前主题（Bind 时按配置解析；未 Bind 前用默认 azure，仅影响圆点）。</summary>
    private ResolvedTheme _theme = ColorTheme.Resolve(new AppConfig());

    public ServiceCard()
    {
        InitializeComponent();
        ApplyTexts();
    }

    /// <summary>按当前语言刷新卡片上的固定文案（构造时与语言切换重建时调用）。</summary>
    public void ApplyTexts()
    {
        CardRefreshButton.ToolTip = I18n.T("refresh_this_service");
        PausedBadge.Text = I18n.T("paused_badge");
        CardPauseButton.ToolTip = I18n.T(_paused ? "resume_this_service" : "pause_this_service");
        NeedLoginText.Text = I18n.T("need_login_hint");
        NeedLoginButton.Content = I18n.T("go_login");
        ErrorLoginButton.Content = I18n.T("go_login");
    }

    /// <summary>刷新进行中禁用本卡刷新按钮（由主窗口统一控制）。</summary>
    public void SetBusy(bool busy)
    {
        _busy = busy;
        UpdateButtons();
    }

    /// <summary>暂停状态：置灰卡片、显示「已暂停」徽标、刷新按钮禁用、暂停按钮变为 ▶。</summary>
    public void SetPaused(bool paused)
    {
        _paused = paused;
        CardRoot.Opacity = paused ? 0.55 : 1.0;
        PausedBadge.Visibility = paused ? Visibility.Visible : Visibility.Collapsed;
        CardPauseButton.Content = paused ? "▶" : "⏸";
        CardPauseButton.ToolTip = I18n.T(paused ? "resume_this_service" : "pause_this_service");
        UpdateButtons();
    }

    private void UpdateButtons() => CardRefreshButton.IsEnabled = !_busy && !_paused;

    private void CardRefresh_Click(object sender, RoutedEventArgs e)
    {
        if (_service != null) RequestRefresh?.Invoke(_service);
    }

    private void CardPause_Click(object sender, RoutedEventArgs e)
    {
        if (_service != null) RequestPauseToggle?.Invoke(_service);
    }

    public void BindLoading(ServiceConfig service)
    {
        _service = service;
        SetHeader(service);
        RowsPanel.Children.Clear();
        NeedLoginPanel.Visibility = Visibility.Collapsed;
        ErrorPanel.Visibility = Visibility.Collapsed;
        RefreshTimeText.Visibility = Visibility.Collapsed;
        RowsPanel.Children.Add(new TextBlock
        {
            Text = I18n.T("loading"),
            Foreground = Ui.Brush("#8A8A95"),
            FontSize = 11.5,
        });
    }

    public void Bind(ServiceScrapeResult result, AppConfig cfg)
    {
        var svc = result.Service;
        _service = svc;
        _theme = ColorTheme.Resolve(cfg);
        SetHeader(svc);
        BindSubscription(result.Subscription);
        // 最后刷新时间：今天只显示时分，跨天带日期（强迫症友好）
        var t = result.Time.LocalDateTime;
        RefreshTimeText.Text = t.Date == DateTime.Today ? t.ToString("HH:mm") : t.ToString("MM-dd HH:mm");
        RefreshTimeText.ToolTip = I18n.T("last_refresh_tip", t.ToString("yyyy-MM-dd HH:mm:ss"));
        RefreshTimeText.Visibility = Visibility.Visible;
        RowsPanel.Children.Clear();
        NeedLoginPanel.Visibility = result.Status == ScrapeStatus.NeedLogin ? Visibility.Visible : Visibility.Collapsed;
        ErrorPanel.Visibility = result.Status == ScrapeStatus.Error ? Visibility.Visible : Visibility.Collapsed;

        if (result.Status == ScrapeStatus.NeedLogin) return;

        if (result.Status == ScrapeStatus.Error)
        {
            string msg = result.ErrorMessage ?? I18n.T("unknown_error");
            ErrorText.Text = msg.Length > 90 ? msg[..90] + "…" : msg;
            ErrorText.ToolTip = msg;
            ErrorLoginButton.Visibility = result.SuggestLogin ? Visibility.Visible : Visibility.Collapsed;
            return;
        }

        if (result.Rules.Count == 0)
        {
            RowsPanel.Children.Add(new TextBlock
            {
                Text = I18n.T("no_rules_configured"),
                Foreground = Ui.Brush("#8A8A95"),
                FontSize = 11.5,
                TextWrapping = TextWrapping.Wrap,
            });
            return;
        }
        foreach (var rule in result.Rules)
            RowsPanel.Children.Add(BuildRow(rule, _theme, cfg));
    }

    private void SetHeader(ServiceConfig svc)
    {
        Dot.Fill = new SolidColorBrush(_theme.Accent);
        NameText.Text = svc.Name;
    }

    /// <summary>订阅信息行：「N 天后到期 · 自动续费 开/关」，临近到期变色提醒。</summary>
    private void BindSubscription(SubscriptionInfo? sub)
    {
        SubText.Visibility = Visibility.Collapsed;
        if (sub == null) return;
        var parts = new List<string>();
        Color color = (Color)ColorConverter.ConvertFromString("#8A8A95");
        if (sub.ExpireAt is { } expire)
        {
            int days = (int)Math.Ceiling((expire - DateTime.Now).TotalDays);
            parts.Add(days < 0 ? I18n.T("subscription_expired") : I18n.T("subscription_expires_in_days", days));
            if (days < 0 || days <= 3) color = DangerColor;
            else if (days <= 7) color = WarnColor;
        }
        if (sub.AutoRenew is { } auto)
            parts.Add(I18n.T(auto ? "auto_renew_on" : "auto_renew_off"));
        if (parts.Count == 0) return;
        SubText.Text = string.Join(" · ", parts);
        SubText.Foreground = new SolidColorBrush(color);
        SubText.Visibility = Visibility.Visible;
    }

    private void Login_Click(object sender, RoutedEventArgs e)
    {
        if (_service != null) RequestLogin?.Invoke(_service);
    }

    private static FrameworkElement BuildRow(RuleResult rule, ResolvedTheme theme, AppConfig cfg)
    {
        double pct = Math.Clamp(rule.Percent ?? 0, 0, 100);
        // 时间基准：窗口按时间均匀消耗此刻应到的位置（无标签窗口信息或无重置时间时为 null）
        double? elapsed = PaceBaseline.Elapsed(rule.Label, rule.ResetAt, DateTime.Now);

        // 状态色：≥90% 告警 > 超出基准 > 接近基准（10 个百分点以内）> 正常；无基准时 ≥60% 视为接近
        Color barColor;
        if (rule.Percent >= 90)
            barColor = theme.Critical;
        else if (elapsed is { } el)
        {
            double usage = pct / 100.0;
            barColor = usage > el ? theme.Ahead : usage >= el - 0.10 ? theme.Near : theme.Normal;
        }
        else
            barColor = rule.Percent >= 60 ? theme.Near : theme.Normal;

        var panel = new StackPanel { Margin = new Thickness(0, 0, 0, 9) };

        // 第一行：标签 + 百分比
        var top = new Grid();
        top.Children.Add(new TextBlock
        {
            Text = rule.Label,
            Foreground = Ui.Brush("#C9C9D1"),
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center,
        });
        var pctText = new TextBlock
        {
            FontSize = 12,
            FontWeight = FontWeights.SemiBold,
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        if (rule.Error != null)
        {
            pctText.Text = "—";
            pctText.Foreground = Ui.Brush("#8A8A95");
        }
        else
        {
            pctText.Text = $"{rule.Percent ?? 0:0.#}%";
            pctText.Foreground = new SolidColorBrush(barColor);
        }
        top.Children.Add(pctText);
        panel.Children.Add(top);

        // 第二行：细进度条（7px 圆角，星号列宽表达填充比例，离屏渲染也能正确布局）
        if (rule.Error == null)
        {
            var grid = new Grid { Height = 7, Margin = new Thickness(0, 5, 0, 0) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(pct, 0.0001), GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(100 - pct, 0.0001), GridUnitType.Star) });
            var track = new Border { CornerRadius = new CornerRadius(3.5), Background = Ui.Brush("#1FFFFFFF") };
            Grid.SetColumnSpan(track, 2);
            var fill = new Border
            {
                CornerRadius = new CornerRadius(3.5),
                Background = new SolidColorBrush(barColor),
            };
            grid.Children.Add(track);
            grid.Children.Add(fill);

            // 时间基准竖线：窗口按时间均匀消耗此刻应到的位置，与实际用量同条对比
            if (cfg.ShowPaceBaseline && elapsed is { } el0 && el0 > 0.001 && el0 < 0.999)
            {
                bool ahead = pct / 100.0 > el0; // 用量跑在时间前面 = 用得偏快
                grid.ToolTip = I18n.T(ahead ? "pace_tooltip_ahead" : "pace_tooltip_behind",
                    el0.ToString("P0"), $"{pct:0.#}%");
                var overlay = new Grid { IsHitTestVisible = false };
                overlay.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(el0, GridUnitType.Star) });
                overlay.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1 - el0, GridUnitType.Star) });
                var tick = new Border
                {
                    Width = 3,
                    CornerRadius = new CornerRadius(1),
                    // 超前：红芯白描边（在红色进度条上仍可见）；未超前：白芯深描边
                    Background = ahead
                        ? new SolidColorBrush(DangerColor)
                        : new SolidColorBrush(Color.FromArgb(0xE6, 0xFF, 0xFF, 0xFF)),
                    BorderBrush = ahead
                        ? new SolidColorBrush(Color.FromArgb(0xE6, 0xFF, 0xFF, 0xFF))
                        : new SolidColorBrush(Color.FromArgb(0xB3, 0x00, 0x00, 0x00)),
                    BorderThickness = new Thickness(1),
                    HorizontalAlignment = HorizontalAlignment.Left,
                };
                Grid.SetColumn(tick, 1);
                overlay.Children.Add(tick);
                Grid.SetColumnSpan(overlay, 2);
                grid.Children.Add(overlay);
            }
            panel.Children.Add(grid);
        }
        else
        {
            panel.Children.Add(new TextBlock
            {
                Text = rule.Error,
                Foreground = Ui.Brush("#E5534B"),
                FontSize = 10.5,
                Margin = new Thickness(0, 4, 0, 0),
                TextWrapping = TextWrapping.Wrap,
                ToolTip = string.IsNullOrEmpty(rule.RawText) ? rule.Error : rule.Error + "\n\n" + I18n.T("raw_text_header") + "\n" + rule.RawText,
            });
        }

        // 第三行：用量明细 / 重置时间（按全局设置统一格式 / 显示剩余时间）
        string? resetDisplay = ResetTimeParser.Format(
            rule.ResetText, rule.ResetAt, DateTime.Now,
            cfg.UnifiedDateFormat, cfg.DateFormat, cfg.ShowRemainingTime, cfg.RemainingThresholdDays);
        string? small = (rule.Detail, resetDisplay) switch
        {
            (not null, not null) => $"{rule.Detail} · {resetDisplay}",
            (not null, null) => rule.Detail,
            (null, not null) => resetDisplay,
            _ => null,
        };
        if (small != null)
        {
            // 重置临近提醒：窗口大于 24 小时（7 天/30 天类）且重置时间已进入 24 小时内 → 黄色，
            // 提醒用户窗口快重置、剩余额度尽快用。5 小时等短窗口恒 <24h，不参与以免长期黄色。
            bool resetSoon = false;
            if (rule.ResetAt is { } ra)
            {
                var windowHours = PaceBaseline.WindowHours(rule.Label);
                resetSoon = ra > DateTime.Now.AddHours(-6) && ra <= DateTime.Now.AddHours(24) &&
                            (windowHours == null || windowHours > 24);
            }
            panel.Children.Add(new TextBlock
            {
                Text = small,
                Foreground = new SolidColorBrush(resetSoon ? theme.Near : (Color)ColorConverter.ConvertFromString("#8A8A95")),
                FontSize = 10.5,
                Margin = new Thickness(0, 3, 0, 0),
            });
        }
        return panel;
    }
}
