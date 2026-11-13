using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace AIQuotaMonitor;

public partial class CreditHistoryWindow : Window
{
    private const double MarginLeft = 64, MarginRight = 16, MarginTop = 16, MarginBottom = 32;
    private static readonly Color CreditColor = (Color)ColorConverter.ConvertFromString("#3FB950");

    private readonly ServiceConfig _service;
    private readonly AppConfig _config;
    private readonly IReadOnlyList<CreditUsageEntry>? _entriesOverride;
    private IReadOnlyList<CreditDailyUsage> _days;
    private bool _ready;

    public CreditHistoryWindow(ServiceConfig service, AppConfig config, IReadOnlyList<CreditUsageEntry>? entriesOverride = null)
    {
        InitializeComponent();
        Backdrop.Apply(this, Backdrop.Kind.Mica);
        _service = service;
        _config = config;
        _entriesOverride = entriesOverride;
        _days = LoadDays();
        StartDatePicker.SelectedDate = DateTime.Today.AddDays(-29);
        EndDatePicker.SelectedDate = DateTime.Today;
        _ready = true;
        ApplyTexts();
        I18n.Changed += OnI18nChanged;
        Unloaded += (_, _) => I18n.Changed -= OnI18nChanged;
    }

    private IReadOnlyList<CreditDailyUsage> LoadDays() =>
        CreditHistoryStore.AggregateDaily(_entriesOverride ??
            (_service.Id != null ? CreditHistoryStore.Query(_service.Id) : Array.Empty<CreditUsageEntry>()));

    protected override void OnActivated(EventArgs e)
    {
        base.OnActivated(e);
        if (_entriesOverride != null) return;
        _days = LoadDays();
        Redraw();
    }

    private void OnI18nChanged()
    {
        ApplyTexts();
        Redraw();
    }

    private void ApplyTexts()
    {
        string title = I18n.T("credit_history_title", _service.Name);
        Title = title;
        TitleText.Text = title;
        LocalTipText.Text = I18n.T("credit_history_local_tip");
        StartLabel.Text = I18n.T("credit_history_start");
        EndLabel.Text = I18n.T("credit_history_end");
        Last30Button.Content = I18n.T("history_range_30d");
    }

    private void Last30_Click(object sender, RoutedEventArgs e)
    {
        StartDatePicker.SelectedDate = DateTime.Today.AddDays(-29);
        EndDatePicker.SelectedDate = DateTime.Today;
        Redraw();
    }

    private void DateRange_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_ready) Redraw();
    }

    private void ChartCanvas_SizeChanged(object sender, SizeChangedEventArgs e) => Redraw();

    private void Redraw()
    {
        if (!_ready) return;
        ChartCanvas.Children.Clear();
        StatsPanel.Children.Clear();
        var start = (StartDatePicker.SelectedDate ?? DateTime.Today.AddDays(-29)).Date;
        var end = (EndDatePicker.SelectedDate ?? DateTime.Today).Date;
        if (start > end) (start, end) = (end, start);
        var selected = _days.Where(d => d.Day.Date >= start && d.Day.Date <= end && d.Credits > 0)
            .OrderBy(d => d.Day).ToList();
        BuildStats(selected);
        double w = ChartCanvas.ActualWidth, h = ChartCanvas.ActualHeight;
        if (w < 80 || h < 80) return;
        if (selected.Count == 0)
        {
            var empty = new TextBlock
            {
                Text = _days.Count == 0 && !_config.RecordHistory
                    ? I18n.T("credit_history_disabled")
                    : I18n.T("credit_history_no_data"),
                Foreground = Ui.Brush("#8A8A95"),
                FontSize = 12,
            };
            empty.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            Canvas.SetLeft(empty, Math.Max(0, (w - empty.DesiredSize.Width) / 2));
            Canvas.SetTop(empty, h / 2 - 8);
            ChartCanvas.Children.Add(empty);
            return;
        }

        double plotW = w - MarginLeft - MarginRight;
        double plotH = h - MarginTop - MarginBottom;
        long maximum = NiceMaximum(selected.Max(d => d.Credits));
        double Y(long credits) => MarginTop + (1 - credits / (double)maximum) * plotH;
        foreach (double fraction in new[] { 0.0, 0.5, 1.0 })
        {
            double y = MarginTop + (1 - fraction) * plotH;
            ChartCanvas.Children.Add(new Line
            {
                X1 = MarginLeft, X2 = w - MarginRight, Y1 = y, Y2 = y,
                Stroke = Ui.Brush("#22FFFFFF"), StrokeThickness = 1,
            });
            var label = new TextBlock
            {
                Text = ((long)Math.Round(maximum * fraction)).ToString("N0"),
                FontSize = 9.5,
                Foreground = Ui.Brush("#8A8A95"),
            };
            label.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            Canvas.SetLeft(label, MarginLeft - label.DesiredSize.Width - 8);
            Canvas.SetTop(label, y - 7);
            ChartCanvas.Children.Add(label);
        }

        int slotCount = (end - start).Days + 1;
        double slotWidth = plotW / slotCount;
        double barWidth = Math.Clamp(slotWidth * 0.62, 1.5, 48);
        var byDay = selected.ToDictionary(d => d.Day.Date);
        for (int i = 0; i < slotCount; i++)
        {
            var day = start.AddDays(i);
            if (!byDay.TryGetValue(day, out var usage)) continue;
            double x = MarginLeft + (i + 0.5) * slotWidth;
            double y = Y(usage.Credits);
            var bar = new Rectangle
            {
                Width = barWidth,
                Height = Math.Max(1, h - MarginBottom - y),
                Fill = new SolidColorBrush(CreditColor),
                RadiusX = 2,
                RadiusY = 2,
                Tag = usage,
                ToolTip = I18n.T("credits_recent_day", day.ToString("yyyy-MM-dd"), usage.Credits.ToString("N0")),
            };
            Canvas.SetLeft(bar, x - barWidth / 2);
            Canvas.SetTop(bar, y);
            ChartCanvas.Children.Add(bar);
        }

        int tickCount = Math.Min(6, slotCount);
        for (int i = 0; i < tickCount; i++)
        {
            int dayIndex = tickCount == 1 ? 0 : (int)Math.Round(i * (slotCount - 1) / (double)(tickCount - 1));
            var day = start.AddDays(dayIndex);
            double x = MarginLeft + (dayIndex + 0.5) * slotWidth;
            var label = new TextBlock
            {
                Text = day.ToString(slotCount > 365 ? "yyyy-MM-dd" : "MM-dd"),
                FontSize = 9.5,
                Foreground = Ui.Brush("#8A8A95"),
            };
            label.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            Canvas.SetLeft(label, Math.Clamp(x - label.DesiredSize.Width / 2, 0, w - label.DesiredSize.Width));
            Canvas.SetTop(label, h - MarginBottom + 8);
            ChartCanvas.Children.Add(label);
        }
    }

    private void BuildStats(IReadOnlyList<CreditDailyUsage> selected)
    {
        long total = selected.Sum(d => d.Credits);
        AddStat(I18n.T("credit_history_total", total.ToString("N0")));
        AddStat(I18n.T("credit_history_days", selected.Count));
        if (selected.Count > 0)
            AddStat(I18n.T("credit_history_average", (total / (double)selected.Count).ToString("N0")));
    }

    private void AddStat(string text) => StatsPanel.Children.Add(new TextBlock
    {
        Text = text,
        FontSize = 11.5,
        Foreground = Ui.Brush("#C9C9D1"),
        Margin = new Thickness(0, 0, 24, 0),
    });

    private static long NiceMaximum(long value)
    {
        if (value <= 1) return 1;
        double power = Math.Pow(10, Math.Floor(Math.Log10(value)));
        double scaled = value / power;
        double nice = scaled <= 1 ? 1 : scaled <= 2 ? 2 : scaled <= 5 ? 5 : 10;
        return (long)(nice * power);
    }
}
