using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using AIQuotaMonitor;

internal static class CreditHistoryWindowTests
{
    internal static (int Checks, List<string> Failures) Run()
    {
        int checks = 0;
        var failures = new List<string>();
        var thread = new Thread(() =>
        {
            try
            {
                I18n.Initialize(I18n.LangEn);
                var service = new ServiceConfig { Id = "codex-history", Name = "Codex" };
                var entries = new List<CreditUsageEntry>
                {
                    new() { Key = "old", Day = DateTime.Today.AddDays(-40), Credits = 50 },
                    new() { Key = "a", Day = DateTime.Today.AddDays(-3), Credits = 100 },
                    new() { Key = "b", Day = DateTime.Today.AddDays(-3), Credits = 20 },
                    new() { Key = "zero", Day = DateTime.Today.AddDays(-2), Credits = 0 },
                    new() { Key = "recent", Day = DateTime.Today.AddDays(-1), Credits = 40 },
                };
                var window = new CreditHistoryWindow(service, new AppConfig(), entries);
                Check(window.StartDatePicker.SelectedDate == DateTime.Today.AddDays(-29) &&
                    window.EndDatePicker.SelectedDate == DateTime.Today,
                    "Credit history must default to the most recent 30 calendar days.");
                Layout(window);
                Check(StatText(window).Contains("160") && StatText(window).Contains("2 days"),
                    "The default chart must total same-day entries and exclude zero-usage days.");
                Check(ChartBars(window).Count() == 2 && ChartBars(window).All(r => r.Tag is CreditDailyUsage),
                    "The chart must draw one bar per active day in the selected range.");
                Check(ChartBars(window).All(r => r.Fill is SolidColorBrush b && b.Color.ToString() == "#FF3FB950"),
                    "Daily Credit bars must use the same green as the card.");

                window.StartDatePicker.SelectedDate = DateTime.Today.AddDays(-40);
                Layout(window);
                Check(StatText(window).Contains("210") && ChartBars(window).Count() == 3,
                    "Changing the date range must include all matching locally stored days.");
            }
            catch (Exception ex)
            {
                failures.Add($"Credit history window test failed: {ex.GetType()}: {ex.Message}");
            }
            finally
            {
                I18n.Initialize(I18n.LangEn);
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        return (checks, failures);

        void Check(bool passed, string message)
        {
            checks++;
            if (!passed) failures.Add(message);
        }
    }

    private static void Layout(Window window)
    {
        var content = (FrameworkElement)window.Content;
        content.Measure(new Size(window.Width, window.Height));
        content.Arrange(new Rect(0, 0, window.Width, window.Height));
        content.UpdateLayout();
    }

    private static string StatText(CreditHistoryWindow window) =>
        string.Join("\n", window.StatsPanel.Children.OfType<TextBlock>().Select(t => t.Text));

    private static IEnumerable<Rectangle> ChartBars(CreditHistoryWindow window) =>
        window.ChartCanvas.Children.OfType<Rectangle>().Where(r => r.Tag is CreditDailyUsage);
}
