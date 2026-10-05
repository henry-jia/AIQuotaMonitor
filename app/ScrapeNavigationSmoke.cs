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

internal static class ScrapeNavigationSmoke
{
    internal static async Task<bool> RunAsync(string outputPath)
    {
        var checks = new Dictionary<string, bool>();
        var hosts = new List<Window>();
        var views = new List<WebView2>();
        string? errorCode = null;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        try
        {
            string root = Path.Combine(Path.GetTempPath(), "AIQuotaMonitor.NavigationSmoke", Guid.NewGuid().ToString("N"));
            string pages = Path.Combine(root, "pages");
            Directory.CreateDirectory(pages);
            File.WriteAllText(Path.Combine(pages, "index.html"), """
                <!doctype html><html><head><title>Quota fixture</title></head><body><main id="quota"></main><script>
                const remaining = localStorage.getItem('fixtureRemaining');
                document.getElementById('quota').innerHTML = remaining === null
                    ? '<p>Your session has expired. Please log in again to continue using the app.</p>'
                    : '<section><h2>Weekly limit</h2><p>Resets in 6d 23h</p><p>' + remaining + '% left</p></section>';
                window.fixtureDocumentId = crypto.randomUUID();
                </script></body></html>
                """);
            var env = await CoreWebView2Environment.CreateAsync(null, Path.Combine(root, "profile"));
            var isolatedEnv = await CoreWebView2Environment.CreateAsync(null, Path.Combine(root, "other-profile"));
            var background = await CreateView(env);
            var interactive = await CreateView(env);
            var otherAccount = await CreateView(isolatedEnv);
            var engine = new ScrapeEngine();
            const string usageUrl = "https://quota-fixture.example/index.html#settings/Usage";
            const string billingUrl = "https://quota-fixture.example/index.html#settings/Billing";
            var rule = new QuotaRule { Label = "Weekly usage", MatchText = "Weekly limit" };

            await engine.NavigateAsync(background, usageUrl, 5, timeout.Token);
            checks["initial-session-expired"] = await ReadPercent(background) == null;
            string firstDocument = await background.ExecuteScriptAsync("window.fixtureDocumentId");

            // The login window shares persistent storage, but the already-loaded background SPA
            // retains its old in-memory view until a new document loads.
            await engine.NavigateAsync(interactive, usageUrl, 5, timeout.Token);
            await interactive.ExecuteScriptAsync("localStorage.setItem('fixtureRemaining', '100')");
            interactive.Reload();
            await WaitForPercent(interactive, 0);
            checks["login-window-has-current-quota"] = await ReadPercent(interactive) == 0;
            checks["background-still-has-expired-view"] = await ReadPercent(background) == null;

            await engine.NavigateAsync(background, usageUrl, 5, timeout.Token);
            checks["same-url-refresh-recovers-after-login"] = await ReadPercent(background) == 0;
            checks["same-url-refresh-loads-new-document"] =
                await background.ExecuteScriptAsync("window.fixtureDocumentId") != firstDocument;

            await interactive.ExecuteScriptAsync("localStorage.setItem('fixtureRemaining', '9')");
            await engine.NavigateAsync(background, billingUrl, 5, timeout.Token);
            checks["hash-route-refresh-reads-current-quota"] = await ReadPercent(background) == 91;

            await engine.NavigateAsync(otherAccount, usageUrl, 5, timeout.Token);
            checks["other-profile-remains-signed-out"] = await ReadPercent(otherAccount) == null;

            await interactive.ExecuteScriptAsync("localStorage.removeItem('fixtureRemaining')");
            await engine.NavigateAsync(background, billingUrl, 5, timeout.Token);
            checks["genuine-logout-remains-unreadable"] = await ReadPercent(background) == null;

            string sourceBeforeCancel = background.Source;
            string documentBeforeCancel = await background.ExecuteScriptAsync("window.fixtureDocumentId");
            using var canceled = new CancellationTokenSource();
            canceled.Cancel();
            bool cancellationObserved = false;
            try { await engine.NavigateAsync(background, usageUrl, 5, canceled.Token); }
            catch (OperationCanceledException) { cancellationObserved = true; }
            await Task.Delay(150, timeout.Token);
            checks["canceled-navigation-does-not-change-document"] = cancellationObserved &&
                background.Source == sourceBeforeCancel &&
                await background.ExecuteScriptAsync("window.fixtureDocumentId") == documentBeforeCancel;

            async Task<CoreWebView2> CreateView(CoreWebView2Environment environment)
            {
                var host = new Window
                {
                    Width = 1280, Height = 800, Left = -32000, Top = -32000,
                    WindowStyle = WindowStyle.None, ShowInTaskbar = false, ShowActivated = false,
                };
                hosts.Add(host);
                host.Show();
                var view = new WebView2();
                views.Add(view);
                host.Content = view;
                await view.EnsureCoreWebView2Async(environment);
                view.CoreWebView2.SetVirtualHostNameToFolderMapping("quota-fixture.example", pages,
                    CoreWebView2HostResourceAccessKind.DenyCors);
                return view.CoreWebView2;
            }

            async Task<double?> ReadPercent(CoreWebView2 view)
            {
                string raw = await view.ExecuteScriptAsync(ScrapeEngine.BuildAutoExtractScript(rule,
                    QuotaRule.DefaultResetPattern, Array.Empty<string>()));
                string payload = JsonSerializer.Deserialize<string>(raw)!;
                var result = new RuleResult();
                ScrapeEngine.ParseRulePayload(result, rule, payload, out _);
                return result.Percent;
            }

            async Task WaitForPercent(CoreWebView2 view, double expected)
            {
                while (await ReadPercent(view) != expected)
                    await Task.Delay(50, timeout.Token);
            }
        }
        catch (Exception ex) { errorCode = ex.GetType().Name; }
        finally
        {
            foreach (var view in views) view.Dispose();
            foreach (var host in hosts) host.Close();
        }

        bool success = errorCode == null && checks.Count == 9 && checks.Values.All(v => v);
        string fullOutputPath = Path.GetFullPath(outputPath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullOutputPath)!);
        File.WriteAllText(fullOutputPath, JsonSerializer.Serialize(new { success, checks, errorCode },
            new JsonSerializerOptions { WriteIndented = true }));
        return success;
    }
}
