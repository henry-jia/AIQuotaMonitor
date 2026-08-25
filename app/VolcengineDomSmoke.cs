using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace AIQuotaMonitor;

internal static class VolcengineDomSmoke
{
    internal static async Task<bool> RunAsync(string outputPath)
    {
        Window? host = null;
        WebView2? webView = null;
        string? errorCode = null;
        var actual = new Dictionary<string, string?>();
        try
        {
            string profilePath = Path.Combine(Path.GetTempPath(), "AIQuotaMonitor.VolcDomSmoke",
                Guid.NewGuid().ToString("N"), "WebView2");
            var env = await CoreWebView2Environment.CreateAsync(null, profilePath,
                new CoreWebView2EnvironmentOptions { Language = "zh-CN" });
            host = new Window
            {
                Width = 1280,
                Height = 800,
                Left = -32000,
                Top = -32000,
                WindowStyle = WindowStyle.None,
                ShowInTaskbar = false,
                ShowActivated = false,
            };
            host.Show();
            webView = new WebView2();
            host.Content = webView;
            await webView.EnsureCoreWebView2Async(env);

            var navigation = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            void OnNavigation(object? sender, CoreWebView2NavigationCompletedEventArgs args) =>
                navigation.TrySetResult(args.IsSuccess);
            webView.CoreWebView2.NavigationCompleted += OnNavigation;
            webView.NavigateToString("""
                <!doctype html><html><body>
                  <main id="quota-panel">
                    <section class="quota-row">
                      <div class="label">当前会话</div>
                      <div class="metric"><span style="font-size:20px">0%</span></div>
                      <div class="status">当前时段暂无调用</div>
                    </section>
                    <section class="quota-row">
                      <div class="label">近 1 周</div>
                      <div class="metric"><span style="font-size:20px">0%</span></div>
                      <div class="refresh"><span>5天11时56分钟后刷新</span></div>
                    </section>
                    <section class="quota-row">
                      <div class="label">近 1 月</div>
                      <div class="metric"><span style="font-size:20px">6.69%</span></div>
                      <div class="refresh"><span>28天11时56分钟后刷新</span></div>
                    </section>
                    <aside>Refresh failed - showing stale data</aside>
                  </main>
                </body></html>
                """);
            if (!await navigation.Task) throw new InvalidOperationException("FixtureNavigationFailed");
            webView.CoreWebView2.NavigationCompleted -= OnNavigation;

            var rules = new List<QuotaRule>
            {
                new() { Label = "当前会话" },
                new() { Label = "近1周" },
                new() { Label = "近1月" },
            };
            foreach (var rule in rules)
            {
                var others = rules.Where(r => !ReferenceEquals(r, rule)).Select(r => r.Label).ToList();
                string script = ScrapeEngine.BuildAutoExtractScript(rule, QuotaRule.DefaultResetPattern, others);
                string raw = await webView.CoreWebView2.ExecuteScriptAsync(script);
                string payload = JsonSerializer.Deserialize<string>(raw) ?? raw;
                using var result = JsonDocument.Parse(payload);
                var root = result.RootElement;
                if (!root.GetProperty("ok").GetBoolean())
                    throw new InvalidOperationException(root.GetProperty("err").GetString());
                actual[rule.Label] = root.GetProperty("reset").GetString();
            }
        }
        catch (Exception ex)
        {
            errorCode = ex.GetType().Name;
        }
        finally
        {
            webView?.Dispose();
            host?.Close();
        }

        bool success = errorCode == null &&
            actual.GetValueOrDefault("当前会话") == "当前时段暂无调用" &&
            actual.GetValueOrDefault("近1周") == "5天11时56分钟后刷新" &&
            actual.GetValueOrDefault("近1月") == "28天11时56分钟后刷新";
        string fullOutputPath = Path.GetFullPath(outputPath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullOutputPath)!);
        File.WriteAllText(fullOutputPath, JsonSerializer.Serialize(new
        {
            success,
            currentSession = actual.GetValueOrDefault("当前会话"),
            weekly = actual.GetValueOrDefault("近1周"),
            monthly = actual.GetValueOrDefault("近1月"),
            errorCode,
        }, new JsonSerializerOptions { WriteIndented = true }));
        return success;
    }
}
