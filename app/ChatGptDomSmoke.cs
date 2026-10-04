using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace AIQuotaMonitor;

/// <summary>Exercises the production scanners against screenshot-equivalent DOM and delayed rendering.</summary>
internal static class ChatGptDomSmoke
{
    internal static async Task<bool> RunAsync(string outputPath)
    {
        Window? host = null;
        WebView2? webView = null;
        var checks = new Dictionary<string, bool>();
        string? errorCode = null;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(90));
        try
        {
            string profilePath = Path.Combine(Path.GetTempPath(), "AIQuotaMonitor.ChatGptDomSmoke",
                Guid.NewGuid().ToString("N"), "WebView2");
            var env = await CoreWebView2Environment.CreateAsync(null, profilePath);
            host = new Window
            {
                Width = 1280, Height = 800, Left = -32000, Top = -32000,
                WindowStyle = WindowStyle.None, ShowInTaskbar = false, ShowActivated = false,
            };
            host.Show();
            webView = new WebView2();
            host.Content = webView;
            await webView.EnsureCoreWebView2Async(env);
            var wv = webView.CoreWebView2;
            var engine = new ScrapeEngine();
            var service = new ServiceConfig { ExtraWaitSeconds = 3 };

            async Task Navigate(string body)
            {
                var navigation = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                void OnNavigation(object? sender, CoreWebView2NavigationCompletedEventArgs args) =>
                    navigation.TrySetResult(args.IsSuccess);
                wv.NavigationCompleted += OnNavigation;
                try
                {
                    wv.NavigateToString("<!doctype html><html><body>" + body + "</body></html>");
                    if (!await navigation.Task.WaitAsync(timeout.Token))
                        throw new InvalidOperationException("FixtureNavigationFailed");
                }
                finally { wv.NavigationCompleted -= OnNavigation; }
            }

            async Task CheckSubscription(string name, string body, DateTime? expiry, bool? auto)
            {
                await Navigate(body);
                var sub = await engine.ScanSubscriptionAsync(wv, service, timeout.Token);
                checks[name] = sub?.ExpireAt?.LocalDateTime == expiry && sub?.AutoRenew == auto;
            }

            const string canceled = "Your plan is canceled and won't renew. You'll continue to have access to ChatGPT Pro 200 until";
            await CheckSubscription("billing-oct19", $"<h1>Billing</h1><section>ChatGPT Pro 200<p>{canceled}<br>Oct 19, 2026</p><button>Renew plan</button></section><aside>Transaction history<p>ChatGPT Pro 200 9/19/2026 Paid</p></aside>",
                new DateTime(2026, 10, 19), false);
            await CheckSubscription("billing-oct5", $"<h1>Billing</h1><section>ChatGPT Pro 200<p>{canceled}<br>Oct 5, 2026</p><button>Renew plan</button></section>",
                new DateTime(2026, 10, 5), false);
            await CheckSubscription("legacy-cancellation", "<section>Your plan will be canceled on October 19, 2026.</section>",
                new DateTime(2026, 10, 19), false);
            await CheckSubscription("delayed-expiry", "<section>Your plan is canceled and won't renew.<p id='detail'></p></section><script>setTimeout(function(){ document.getElementById('detail').textContent=\"You'll continue to have access to ChatGPT Pro 200 until Oct 19, 2026\"; },1200);</script>",
                new DateTime(2026, 10, 19), false);
            await CheckSubscription("unrelated-until-date", "<section>ChatGPT Pro 200</section><aside>Support available until Oct 30, 2026</aside>", null, null);
            await CheckSubscription("credits-reload-is-not-subscription", "<section>Credits 62,500 credits remaining <div>Automatic reload <input type='checkbox' checked></div></section>", null, null);

            async Task CheckResets(string name, string body, params string?[] dates)
            {
                await Navigate(body);
                var result = new ServiceScrapeResult { Service = service };
                await engine.ScanBonusResetsAsync(wv, result, timeout.Token);
                checks[name] = result.BonusResets.Count == dates.Length &&
                    result.BonusResets.Select(r => r.RawText).SequenceEqual(dates) &&
                    result.BonusResets.All(r => r.Count == 1);
            }

            const string header = "<h2>Usage limit resets</h2><p>Use a reset to restore your 5-hour limit, weekly limit, or both</p>";
            static string Row(string date) => $"<section><p>Full reset</p><p>Expires {date}</p><button>Use reset</button></section>";
            await CheckResets("available-three-date-only", header + "<div>Available 3 History</div>" + Row("October 5") + Row("October 23") + Row("October 30"),
                "October 5", "October 23", "October 30");
            await CheckResets("available-two-date-only", header + "<div>Available 2 History</div>" + Row("October 23") + Row("October 30"),
                "October 23", "October 30");
            await CheckResets("legacy-timed-resets", header + Row("Oct 4, 9:57 AM") + Row("Oct 5, 12:18 PM"),
                "Oct 4, 9:57 AM", "Oct 5, 12:18 PM");
            await CheckResets("explicit-year-and-24h", header + Row("October 23, 2026") + Row("Oct 30, 2026, 16:42"),
                "October 23, 2026", "Oct 30, 2026, 16:42");
            await CheckResets("no-cross-row-expiry", header + "<section><p>Full reset</p><button>Use reset</button></section>" + Row("October 23"),
                null, "October 23");
            await CheckResets("delayed-reset-list", header + "<div>Available 3 History</div><div id='rows'></div><script>setTimeout(function(){document.getElementById('rows').innerHTML=" +
                JsonSerializer.Serialize(Row("October 5") + Row("October 23") + Row("October 30")) + ";},1200);</script>",
                "October 5", "October 23", "October 30");
            await CheckResets("partial-reset", header + "<section>Partial reset<p>Expires October 23</p><button>Use reset</button></section>", "October 23");
            await CheckResets("available-zero", header + "<div>Available 0 History</div>");
            await CheckResets("available-zero-with-history", header + "<div>Available 0 History</div>" + Row("October 5"));
            await CheckResets("progressive-reset-list", header + "<div>Available 3 History</div><div id='rows'>" + Row("October 5") + "</div><script>setTimeout(function(){document.getElementById('rows').innerHTML=" +
                JsonSerializer.Serialize(Row("October 5") + Row("October 23") + Row("October 30")) + ";},1200);</script>",
                "October 5", "October 23", "October 30");
            await CheckResets("glm-page", "<section>用量重置额度<p>1次 未使用</p><p>周额度 1次</p></section>", (string?)null);
        }
        catch (Exception ex) { errorCode = ex.GetType().Name; }
        finally
        {
            webView?.Dispose();
            host?.Close();
        }

        bool success = errorCode == null && checks.Count == 17 && checks.Values.All(v => v);
        string fullOutputPath = Path.GetFullPath(outputPath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullOutputPath)!);
        File.WriteAllText(fullOutputPath, JsonSerializer.Serialize(new { success, checks, errorCode },
            new JsonSerializerOptions { WriteIndented = true }));
        return success;
    }
}
