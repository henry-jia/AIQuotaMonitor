using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Web.WebView2.Core;

namespace AIQuotaMonitor;

/// <summary>
/// 会话级 Cookie 持久化。
/// 背景：阿里云等站点的登录票据是「会话级 Cookie」——Chromium 只把它存在内存，
/// 浏览器进程退出即失（日常浏览器感觉不到，是因为它的进程常年不死）。
/// 做法：抓取后通过 CookieManager 导出全部 Cookie，DPAPI（当前用户）加密存盘；
/// 引擎初始化时还原，使会话 Cookie 跨进程存活。
/// 注意：只能解决客户端会话丢失；服务端会话到期（阿里云本身会话就短）仍需重新登录。
/// </summary>
public static class CookieStore
{
    private sealed record SavedCookie(
        string Name, string Value, string Domain, string Path,
        DateTime Expires, bool IsHttpOnly, bool IsSecure,
        CoreWebView2CookieSameSiteKind SameSite, bool IsSession);

    private static string StorePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "AIQuotaMonitor", "cookies.dat");

    /// <summary>导出全部 Cookie 并加密存盘。失败静默（不影响抓取主流程）。</summary>
    public static async Task SaveAsync(CoreWebView2 wv)
    {
        try
        {
            var cookies = await wv.CookieManager.GetCookiesAsync(null);
            var list = new List<SavedCookie>(cookies.Count);
            foreach (var c in cookies)
            {
                list.Add(new SavedCookie(
                    c.Name, c.Value, c.Domain, c.Path, c.Expires,
                    c.IsHttpOnly, c.IsSecure, c.SameSite, c.IsSession));
            }
            var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(list));
            var enc = System.Security.Cryptography.ProtectedData.Protect(
                bytes, null, System.Security.Cryptography.DataProtectionScope.CurrentUser);
            Directory.CreateDirectory(Path.GetDirectoryName(StorePath)!);
            File.WriteAllBytes(StorePath, enc);
        }
        catch
        {
            // 加密/写盘失败不影响主流程
        }
    }

    /// <summary>引擎初始化后调用：把上次保存的 Cookie 还原进 CookieManager。</summary>
    public static Task RestoreAsync(CoreWebView2 wv)
    {
        try
        {
            if (!File.Exists(StorePath)) return Task.CompletedTask;
            var enc = File.ReadAllBytes(StorePath);
            var bytes = System.Security.Cryptography.ProtectedData.Unprotect(
                enc, null, System.Security.Cryptography.DataProtectionScope.CurrentUser);
            var list = JsonSerializer.Deserialize<List<SavedCookie>>(bytes);
            if (list == null) return Task.CompletedTask;
            var mgr = wv.CookieManager;
            foreach (var c in list)
            {
                var cookie = mgr.CreateCookie(c.Name, c.Value, c.Domain, c.Path);
                cookie.IsHttpOnly = c.IsHttpOnly;
                cookie.IsSecure = c.IsSecure;
                cookie.SameSite = c.SameSite;
                if (!c.IsSession && c.Expires > DateTime.MinValue) cookie.Expires = c.Expires;
                mgr.AddOrUpdateCookie(cookie);
            }
        }
        catch
        {
            // 文件损坏/解密失败（如换机器）时忽略，重新登录即可
        }
        return Task.CompletedTask;
    }
}
