using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace AIQuotaMonitor;

internal static class SessionPersistenceSmoke
{
    internal static async Task<bool> RunAsync(string outputPath)
    {
        Window? host = null;
        WebView2? webView = null;
        string? errorCode = null;
        double? expiryDeltaSeconds = null;
        bool saved = false, restored = false, sessionPreserved = false, expiryPreserved = false;
        try
        {
            string root = Path.Combine(Path.GetTempPath(), "AIQuotaMonitor.SessionSmoke", Guid.NewGuid().ToString("N"));
            string profilePath = Path.Combine(root, "WebView2");
            string storePath = Path.Combine(root, "cookies.dat");
            var env = await CoreWebView2Environment.CreateAsync(null, profilePath,
                new CoreWebView2EnvironmentOptions { Language = "en-US" });
            host = new Window
            {
                Width = 320,
                Height = 240,
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

            var manager = webView.CoreWebView2.CookieManager;
            var session = manager.CreateCookie("aqm_session_smoke", "opaque-fixture", ".example.invalid", "/");
            manager.AddOrUpdateCookie(session);
            var persistent = manager.CreateCookie("aqm_persistent_smoke", "opaque-fixture", ".example.invalid", "/");
            var expectedExpiry = DateTime.Now.AddMinutes(5);
            persistent.Expires = expectedExpiry;
            manager.AddOrUpdateCookie(persistent);
            var save = await CookieStore.SaveAsync(webView.CoreWebView2, storePath);
            saved = save.Success;
            if (!saved) errorCode = save.ErrorCode;
            manager.DeleteAllCookies();
            var restore = await CookieStore.RestoreAsync(webView.CoreWebView2, storePath);
            restored = restore.Success;
            if (!restored) errorCode = restore.ErrorCode;

            var cookies = await manager.GetCookiesAsync("https://example.invalid/");
            var restoredSession = cookies.FirstOrDefault(c => c.Name == "aqm_session_smoke");
            var restoredPersistent = cookies.FirstOrDefault(c => c.Name == "aqm_persistent_smoke");
            sessionPreserved = restoredSession?.IsSession == true;
            if (restoredPersistent is { IsSession: false })
            {
                expiryDeltaSeconds = (restoredPersistent.Expires.ToUniversalTime() -
                    expectedExpiry.ToUniversalTime()).TotalSeconds;
                expiryPreserved = Math.Abs(expiryDeltaSeconds.Value) < 1;
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

        bool success = saved && restored && sessionPreserved && expiryPreserved && errorCode == null;
        string fullOutputPath = Path.GetFullPath(outputPath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullOutputPath)!);
        File.WriteAllText(fullOutputPath, JsonSerializer.Serialize(new
        {
            success,
            saved,
            restored,
            sessionPreserved,
            expiryPreserved,
            expiryDeltaSeconds,
            errorCode,
        }, new JsonSerializerOptions { WriteIndented = true }));
        return success;
    }
}
