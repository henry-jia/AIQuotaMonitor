using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace AIQuotaMonitor;

/// <summary>
/// 用量历史窗口：每条规则一条百分比趋势线（24h/7d/30d 切换）+ 统计（当前 / 区间增量 / 速率 / 按近 24h 速率预测重置时）。
/// WPF 手绘 Polyline，不引依赖；重置锯齿按段绘制，段界画虚线标记。
/// </summary>
public partial class HistoryWindow : Window
{
    private const double MarginLeft = 36, MarginRight = 8, MarginTop = 8, MarginBottom = 20;

    private readonly AppConfig _cfg;
    private readonly ResolvedTheme _theme;
    private readonly List<HistorySample> _samples;
    private readonly List<string> _rules;
    private readonly string _svcName;
    private string _selectedRule;
    private int _rangeHours = 24;

    private readonly List<(int Hours, Border Chip, TextBlock Text)> _rangeChips = new();
    private readonly List<(string Rule, StackPanel Chip, TextBlock Text, Rectangle Swatch)> _legendChips = new();

    /// <summary>samplesOverride 仅供测试截图注入合成数据，正常路径走 HistoryStore。</summary>
    public HistoryWindow(ServiceConfig svc, string ruleLabel, AppConfig cfg,
        IReadOnlyList<HistorySample>? samplesOverride = null)
    {
        InitializeComponent();
        _cfg = cfg;
        _svcName = svc.Name;
        _theme = ColorTheme.Resolve(cfg);
        _samples = (samplesOverride ?? HistoryStore.Query(svc.Id!)).OrderBy(s => s.T).ToList();
        _rules = _samples.Select(s => s.Rule).Distinct().ToList();
        if (!_rules.Contains(ruleLabel)) _rules.Insert(0, ruleLabel);
        _selectedRule = ruleLabel;

        BuildRangeChips();
        BuildLegendChips();
        ApplyTexts();
        I18n.Changed += OnI18nChanged;
        Unloaded += (_, _) => I18n.Changed -= OnI18nChanged;
    }

    /// <summary>同服务窗口已打开时由主窗口调用：切到对应规则并重绘。</summary>
    public void FocusRule(string ruleLabel)
    {
        if (!_rules.Contains(ruleLabel)) return;
        _selectedRule = ruleLabel;
        RefreshLegendChips();
        Redraw();
    }

    private void OnI18nChanged()
    {
        ApplyTexts();
        Redraw();
    }

    private void ApplyTexts()
    {
        string title = I18n.T("history_title", _svcName);
        Title = title;
        TitleText.Text = title;
        string[] keys = { "history_range_24h", "history_range_7d", "history_range_30d" };
        for (int i = 0; i < _rangeChips.Count; i++)
            _rangeChips[i].Text.Text = I18n.T(keys[i]);
    }

    // ---------- 头部 chips ----------

    private void BuildRangeChips()
    {
        foreach (int hours in new[] { 24, 168, 720 })
        {
            var text = new TextBlock { FontSize = 11 };
            var chip = new Border
            {
                CornerRadius = new CornerRadius(5),
                Padding = new Thickness(9, 3, 9, 3),
                Margin = new Thickness(4, 0, 0, 0),
                Cursor = Cursors.Hand,
                Child = text,
            };
            chip.MouseLeftButtonUp += (s, e) =>
            {
                _rangeHours = hours;
                RefreshRangeChips();
                Redraw();
            };
            RangePanel.Children.Add(chip);
            _rangeChips.Add((hours, chip, text));
        }
        RefreshRangeChips();
    }

    private void RefreshRangeChips()
    {
        foreach (var (hours, chip, text) in _rangeChips)
        {
            bool active = hours == _rangeHours;
            chip.Background = Ui.Brush(active ? "#4F8CFF" : "#2C2C38");
            text.Foreground = Ui.Brush(active ? "#FFFFFF" : "#C9C9D1");
        }
    }

    private void BuildLegendChips()
    {
        foreach (var rule in _rules)
        {
            var swatch = new Rectangle
            {
                Width = 10,
                Height = 3,
                RadiusX = 1.5,
                RadiusY = 1.5,
                VerticalAlignment = VerticalAlignment.Center,
            };
            var text = new TextBlock { FontSize = 11, Margin = new Thickness(5, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            var chip = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(0, 0, 14, 0),
                Cursor = Cursors.Hand,
            };
            chip.Children.Add(swatch);
            chip.Children.Add(text);
            chip.MouseLeftButtonUp += (s, e) =>
            {
                _selectedRule = rule;
                RefreshLegendChips();
                Redraw();
            };
            LegendPanel.Children.Add(chip);
            _legendChips.Add((rule, chip, text, swatch));
        }
        RefreshLegendChips();
    }

    private void RefreshLegendChips()
    {
        foreach (var (rule, _, text, swatch) in _legendChips)
        {
            text.Text = rule;
            swatch.Fill = new SolidColorBrush(RuleColor(rule));
            text.Foreground = Ui.Brush(rule == _selectedRule ? "#EDEDF2" : "#8A8A95");
        }
    }

    /// <summary>规则基色：首条主题强调色，其余按语义色轮转；选中只改线宽/透明度，不改色。</summary>
    private Color RuleColor(string rule)
    {
        var gray = (Color)ColorConverter.ConvertFromString("#8A8A95");
        var palette = new[] { _theme.Accent, _theme.Near, _theme.Ahead, _theme.Critical, gray };
        int i = Math.Max(0, _rules.IndexOf(rule));
        return palette[i % palette.Length];
    }

    // ---------- 图表 ----------

    private void ChartCanvas_SizeChanged(object sender, SizeChangedEventArgs e) => Redraw();

    private void Redraw()
    {
        var canvas = ChartCanvas;
        canvas.Children.Clear();
        StatsPanel.Children.Clear();
        double w = canvas.ActualWidth, h = canvas.ActualHeight;
        if (w < 60 || h < 60) return;
        double plotW = w - MarginLeft - MarginRight;
        double plotH = h - MarginTop - MarginBottom;
        var now = DateTimeOffset.Now;
        var xMin = now.AddHours(-_rangeHours);

        double X(DateTimeOffset t) => MarginLeft + (t - xMin).TotalHours / _rangeHours * plotW;
        double Y(double pct) => MarginTop + (1 - Math.Clamp(pct, 0, 100) / 100.0) * plotH;

        // 横向网格线 + Y 轴标签
        foreach (int g in new[] { 0, 25, 50, 75, 100 })
        {
            canvas.Children.Add(new Line
            {
                X1 = MarginLeft,
                X2 = w - MarginRight,
                Y1 = Y(g),
                Y2 = Y(g),
                Stroke = Ui.Brush("#22FFFFFF"),
                StrokeThickness = 1,
            });
            var label = new TextBlock { Text = g + "%", FontSize = 9, Foreground = Ui.Brush("#6A6A75") };
            Canvas.SetLeft(label, 2);
            Canvas.SetTop(label, Y(g) - 6);
            canvas.Children.Add(label);
        }

        // X 轴刻度
        foreach (var (t, label) in BuildTicks(xMin, now))
        {
            double x = X(t);
            canvas.Children.Add(new Line
            {
                X1 = x,
                X2 = x,
                Y1 = h - MarginBottom,
                Y2 = h - MarginBottom + 3,
                Stroke = Ui.Brush("#6A6A75"),
                StrokeThickness = 1,
            });
            var tb = new TextBlock { Text = label, FontSize = 9, Foreground = Ui.Brush("#6A6A75") };
            Canvas.SetLeft(tb, Math.Clamp(x - 16, 0, w - 34));
            Canvas.SetTop(tb, h - MarginBottom + 5);
            canvas.Children.Add(tb);
        }

        // 各规则序列（选中最后画，保证在最上层）
        bool anyInRange = false;
        foreach (var rule in _rules.OrderBy(r => r == _selectedRule ? 1 : 0))
        {
            bool selected = rule == _selectedRule;
            var all = _samples.Where(s => s.Rule == rule && s.T <= now).ToList();
            var inRange = all.Where(s => s.T >= xMin).ToList();
            if (inRange.Count > 0) anyInRange = true;
            // 区间外最后一个点也带上，折线延伸到左缘
            var pts = inRange;
            if (inRange.Count > 0 && all.Count > inRange.Count)
                pts = new List<HistorySample> { all[all.Count - inRange.Count - 1] }.Concat(inRange).ToList();

            var (segments, resetMarks) = Segmentize(pts, _rangeHours);
            var color = RuleColor(rule);
            var brush = new SolidColorBrush(color);
            double thickness = selected ? 2.2 : 1.3;
            double opacity = selected ? 1.0 : 0.45;

            foreach (var seg in segments)
            {
                if (seg.Count == 1)
                {
                    var p = seg[0];
                    var dot = new Ellipse { Width = 3, Height = 3, Fill = brush, Opacity = opacity };
                    Canvas.SetLeft(dot, X(p.T) - 1.5);
                    Canvas.SetTop(dot, Y(p.Pct) - 1.5);
                    canvas.Children.Add(dot);
                    continue;
                }
                // 过密序列 stride 抽稀（保留首尾），避免 30 天视图上万点
                var draw = StrideDecimate(seg);
                var line = new Polyline
                {
                    Stroke = brush,
                    StrokeThickness = thickness,
                    Opacity = opacity,
                };
                foreach (var p in draw) line.Points.Add(new Point(X(p.T), Y(p.Pct)));
                canvas.Children.Add(line);
            }

            // 选中规则的重置段界：虚线垂直标记
            if (selected)
            {
                foreach (var m in resetMarks)
                {
                    double x = X(m.T);
                    canvas.Children.Add(new Line
                    {
                        X1 = x,
                        X2 = x,
                        Y1 = MarginTop,
                        Y2 = h - MarginBottom,
                        Stroke = Ui.Brush("#44FFFFFF"),
                        StrokeThickness = 1,
                        StrokeDashArray = new DoubleCollection { 3, 3 },
                        ToolTip = I18n.T("history_reset_tip", m.T.LocalDateTime.ToString("MM-dd HH:mm")),
                    });
                }
            }
        }

        if (!anyInRange)
        {
            var empty = new TextBlock
            {
                Text = _samples.Count == 0 && !_cfg.RecordHistory
                    ? I18n.T("history_disabled")
                    : I18n.T("history_no_data"),
                Foreground = Ui.Brush("#8A8A95"),
                FontSize = 12,
            };
            empty.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            Canvas.SetLeft(empty, Math.Max(0, (w - empty.DesiredSize.Width) / 2));
            Canvas.SetTop(empty, h / 2 - 8);
            canvas.Children.Add(empty);
            return;
        }

        BuildStats(now, xMin);
    }

    /// <summary>X 轴刻度：24h 每 4 小时 / 7d 每天 / 30d 每 5 天。</summary>
    private List<(DateTimeOffset, string)> BuildTicks(DateTimeOffset xMin, DateTimeOffset now)
    {
        var ticks = new List<(DateTimeOffset, string)>();
        if (_rangeHours <= 24)
        {
            var t = new DateTime(xMin.LocalDateTime.Year, xMin.LocalDateTime.Month, xMin.LocalDateTime.Day,
                xMin.LocalDateTime.Hour / 4 * 4, 0, 0);
            while (t < xMin.LocalDateTime) t = t.AddHours(4);
            for (; t <= now.LocalDateTime; t = t.AddHours(4))
                ticks.Add((new DateTimeOffset(t), t.ToString("HH:mm")));
        }
        else if (_rangeHours <= 168)
        {
            var t = xMin.LocalDateTime.Date.AddDays(1);
            for (; t <= now.LocalDateTime; t = t.AddDays(1))
                ticks.Add((new DateTimeOffset(t), t.ToString("MM-dd")));
        }
        else
        {
            var t = xMin.LocalDateTime.Date.AddDays(5);
            for (; t <= now.LocalDateTime; t = t.AddDays(5))
                ticks.Add((new DateTimeOffset(t), t.ToString("MM-dd")));
        }
        return ticks;
    }

    /// <summary>锯齿轮重置分段：pct 下降 &gt;2pp / ResetAt 回退 = 重置断段；间隔 &gt;range/8 = 断线（不画误导直线）。</summary>
    private static (List<List<HistorySample>> Segments, List<HistorySample> ResetMarks) Segmentize(
        List<HistorySample> pts, int rangeHours)
    {
        var segments = new List<List<HistorySample>>();
        var resetMarks = new List<HistorySample>();
        var cur = new List<HistorySample>();
        double maxGapHours = rangeHours / 8.0;
        foreach (var p in pts)
        {
            if (cur.Count > 0)
            {
                var prev = cur[^1];
                bool drop = p.Pct < prev.Pct - 2.0;
                bool resetBack = prev.ResetAt != null && p.ResetAt != null && p.ResetAt < prev.ResetAt;
                bool gap = (p.T - prev.T).TotalHours > maxGapHours;
                if (drop || resetBack || gap)
                {
                    segments.Add(cur);
                    cur = new List<HistorySample>();
                    if (drop || resetBack) resetMarks.Add(p);
                }
            }
            cur.Add(p);
        }
        if (cur.Count > 0) segments.Add(cur);
        return (segments, resetMarks);
    }

    /// <summary>段内过密时 stride 抽稀，保留首尾点。</summary>
    private static List<HistorySample> StrideDecimate(List<HistorySample> seg)
    {
        if (seg.Count <= 800) return seg;
        int stride = (seg.Count + 799) / 800;
        var result = new List<HistorySample>(800 + 2);
        for (int i = 0; i < seg.Count; i += stride) result.Add(seg[i]);
        if (result[^1] != seg[^1]) result.Add(seg[^1]);
        return result;
    }

    // ---------- 统计行 ----------

    private void BuildStats(DateTimeOffset now, DateTimeOffset xMin)
    {
        var inRange = _samples.Where(s => s.Rule == _selectedRule && s.T >= xMin && s.T <= now).ToList();
        if (inRange.Count == 0) return;
        var last = inRange[^1];

        AddStat(I18n.T("history_now", $"{last.Pct:0.#}%"), null, null);

        // 区间增量：自上次重置后的累计（最后一段的首点为锚点，跨重置不累加）
        var (segments, _) = Segmentize(inRange, _rangeHours);
        var lastSeg = segments[^1];
        double delta = last.Pct - lastSeg[0].Pct;
        AddStat(I18n.T("history_delta", SignedPct(delta)), I18n.T("history_delta_tip"), null);

        // 速率：近 24h 段内点最小平方斜率（%/h）；不足两点回退整段端点斜率
        var pacePts = lastSeg.Where(s => s.T >= now.AddHours(-24)).ToList();
        if (pacePts.Count < 2) pacePts = lastSeg;
        double? slope = SlopePerHour(pacePts) ?? (lastSeg.Count >= 2 ? SlopePerHour(lastSeg) : null);
        if (slope is { } sp)
            AddStat(I18n.T("history_pace", SignedPct(sp)), null, null);

        // 预测：按当前速率推到重置时间
        if (slope is { } s2 && last.ResetAt is { } ra && ra > now)
        {
            double proj = last.Pct + s2 * (ra - now).TotalHours;
            AddStat(I18n.T("history_projection", $"{proj:0.#}%"), null, proj >= 90 ? _theme.Critical : null);
        }
        else
        {
            AddStat(I18n.T("history_projection_no_reset"), null, null);
        }
    }

    private void AddStat(string text, string? toolTip, Color? color)
    {
        StatsPanel.Children.Add(new TextBlock
        {
            Text = text,
            FontSize = 11.5,
            Foreground = color is { } c ? new SolidColorBrush(c) : Ui.Brush("#C9C9D1"),
            Margin = new Thickness(0, 0, 18, 0),
            ToolTip = toolTip,
        });
    }

    private static string SignedPct(double v) => (v > 0 ? "+" : "") + v.ToString("0.#") + "%";

    /// <summary>最小平方斜率（%/h）；x 全同退化为端点斜率。</summary>
    private static double? SlopePerHour(List<HistorySample> pts)
    {
        if (pts.Count < 2) return null;
        var t0 = pts[0].T;
        double sx = 0, sy = 0, sxy = 0, sxx = 0;
        foreach (var p in pts)
        {
            double x = (p.T - t0).TotalHours;
            sx += x;
            sy += p.Pct;
            sxy += x * p.Pct;
            sxx += x * x;
        }
        int n = pts.Count;
        double denom = n * sxx - sx * sx;
        if (Math.Abs(denom) < 1e-9)
        {
            double dt = (pts[^1].T - t0).TotalHours;
            return dt > 0 ? (pts[^1].Pct - pts[0].Pct) / dt : null;
        }
        return (n * sxy - sx * sy) / denom;
    }
}
