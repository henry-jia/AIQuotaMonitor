using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using AIQuotaMonitor;

internal static class ServiceCardCreditsTests
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
                var service = new ServiceConfig { Id = "codex", Name = "Codex" };
                var card = new ServiceCard();
                var rows = card.FindName("RowsPanel") as StackPanel;
                Check(rows != null, "Credits test requires the service card rows panel.");
                if (rows == null) return;

                card.Bind(new ServiceScrapeResult
                {
                    Service = service,
                    Rules = new List<RuleResult>
                    {
                        new() { Label = "5-hour usage", Percent = 100 },
                        new() { Label = "Weekly usage", Percent = 100 },
                    },
                    Credits = new CreditBalanceInfo
                    {
                        Balance = 50167,
                        ReferenceBalance = 62500,
                        AutoReload = false,
                        FetchedAt = DateTimeOffset.Now,
                    },
                }, new AppConfig());

                Check(rows.Children.Count == 3, "Credits must add exactly one row after the weekly quota.");
                var creditRow = rows.Children[2] as StackPanel;
                Check(creditRow != null, "The row immediately after weekly usage must be Credits.");
                if (creditRow != null)
                {
                    Check(AutomationProperties.GetName(creditRow)?.Contains("50,167") == true,
                        "Credits must expose the current balance to accessibility tools.");
                    var texts = DescendantTexts(creditRow).ToList();
                    Check(texts.Contains("Credits balance"), "Credits row must have a localized balance label.");
                    Check(texts.Any(t => t.Contains("Used 12,333") && t.Contains("Auto-reload off")),
                        "Credits row must show local consumption and automatic reload state.");
                    var bar = creditRow.Children.OfType<Grid>().FirstOrDefault(g => g.ColumnDefinitions.Count == 2);
                    Check(bar != null && Math.Abs(bar.ColumnDefinitions[0].Width.Value - 50167 * 100.0 / 62500) < 0.001,
                        "Credits bar must represent the remaining share of the local high balance.");
                }

                card.Bind(new ServiceScrapeResult
                {
                    Service = service,
                    Rules = new List<RuleResult> { new() { Label = "Custom quota", Percent = 10 } },
                    Credits = new CreditBalanceInfo { Balance = 100, ReferenceBalance = 100, AutoReload = true },
                }, new AppConfig());
                Check(rows.Children.Count == 2 && AutomationProperties.GetName(rows.Children[1])?.Contains("100") == true,
                    "Credits must fall back to the card end when no weekly label can be identified.");

                I18n.Initialize(I18n.LangZh);
                card.Bind(new ServiceScrapeResult
                {
                    Service = service,
                    Rules = new List<RuleResult> { new() { Label = "每周用量", Percent = 100 } },
                    Credits = new CreditBalanceInfo { Balance = 100, ReferenceBalance = 100 },
                }, new AppConfig());
                Check(rows.Children.Count == 2 && DescendantTexts(rows.Children[1]).Contains("Credits 余额"),
                    "Credits label must follow the Chinese UI language.");
            }
            catch (Exception ex)
            {
                failures.Add($"Card credits UI test failed: {ex.GetType()}: {ex.Message}");
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

    private static IEnumerable<string> DescendantTexts(DependencyObject root)
    {
        if (root is TextBlock text) yield return text.Text;
        for (int i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(root); i++)
            foreach (string child in DescendantTexts(System.Windows.Media.VisualTreeHelper.GetChild(root, i)))
                yield return child;
    }
}
