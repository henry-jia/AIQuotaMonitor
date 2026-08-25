using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Web.WebView2.Core;

namespace AIQuotaMonitor;

internal readonly record struct CookiePersistenceResult(bool Success, string? ErrorCode)
{
    public static CookiePersistenceResult Ok() => new(true, null);
    public static CookiePersistenceResult Failed(Exception ex) => new(false, ex.GetType().Name);
}

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
    private static readonly SemaphoreSlim IoLock = new(1, 1);

    private sealed record SavedCookie(
        string Name, string Value, string Domain, string Path,
        DateTime Expires, bool IsHttpOnly, bool IsSecure,
        CoreWebView2CookieSameSiteKind SameSite, bool IsSession);

    private static string StorePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "AIQuotaMonitor", "cookies.dat");

    /// <summary>导出全部 Cookie 并加密存盘。失败不打断抓取，但返回安全错误码并写 Trace。</summary>
    internal static async Task<CookiePersistenceResult> SaveAsync(CoreWebView2 wv, string? storePath = null)
    {
        await IoLock.WaitAsync();
        try
        {
            var cookies = await wv.CookieManager.GetCookiesAsync(null);
            var list = new List<SavedCookie>(cookies.Count);
            foreach (var c in cookies)
            {
                // 跳过已过期 Cookie，避免 cookies.dat 随时间无限增长
                if (!c.IsSession && c.Expires <= DateTimeOffset.Now) continue;
                list.Add(new SavedCookie(
                    c.Name, c.Value, c.Domain, c.Path, c.Expires,
                    c.IsHttpOnly, c.IsSecure, c.SameSite, c.IsSession));
            }
            var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(list));
            WriteProtectedAtomically(storePath ?? StorePath, bytes);
            return CookiePersistenceResult.Ok();
        }
        catch (Exception ex)
        {
            // 只记录异常类型，不记录路径、Cookie 名称或值。
            Trace.TraceError("[CookieStore] Save failed: {0}", ex.GetType().Name);
            return CookiePersistenceResult.Failed(ex);
        }
        finally
        {
            IoLock.Release();
        }
    }

    /// <summary>引擎初始化后调用：把上次保存且尚未过期的 Cookie 还原进 CookieManager。</summary>
    internal static async Task<CookiePersistenceResult> RestoreAsync(CoreWebView2 wv, string? storePath = null)
    {
        await IoLock.WaitAsync();
        try
        {
            string path = storePath ?? StorePath;
            if (!File.Exists(path)) return CookiePersistenceResult.Ok();
            var bytes = ReadProtected(path);
            var list = JsonSerializer.Deserialize<List<SavedCookie>>(bytes);
            if (list == null) return CookiePersistenceResult.Ok();
            var mgr = wv.CookieManager;
            foreach (var c in list)
            {
                if (!ShouldRestore(c.IsSession, c.Expires, DateTime.Now)) continue;
                var cookie = mgr.CreateCookie(c.Name, c.Value, c.Domain, c.Path);
                cookie.IsHttpOnly = c.IsHttpOnly;
                cookie.IsSecure = c.IsSecure;
                cookie.SameSite = c.SameSite;
                if (!c.IsSession && c.Expires > DateTime.MinValue) cookie.Expires = c.Expires;
                mgr.AddOrUpdateCookie(cookie);
            }
            return CookiePersistenceResult.Ok();
        }
        catch (Exception ex)
        {
            // 文件损坏/换机器等失败不阻断浏览器启动，但必须留下不含敏感值的诊断信号。
            Trace.TraceError("[CookieStore] Restore failed: {0}", ex.GetType().Name);
            return CookiePersistenceResult.Failed(ex);
        }
        finally
        {
            IoLock.Release();
        }
    }

    internal static bool ShouldRestore(bool isSession, DateTime expires, DateTime now) =>
        isSession || expires.ToUniversalTime() > now.ToUniversalTime();

    internal static void WriteProtectedAtomically(string path, byte[] plaintext)
    {
        var encrypted = System.Security.Cryptography.ProtectedData.Protect(
            plaintext, null, System.Security.Cryptography.DataProtectionScope.CurrentUser);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string tempPath = path + ".tmp";
        File.WriteAllBytes(tempPath, encrypted);
        if (File.Exists(path)) File.Replace(tempPath, path, null);
        else File.Move(tempPath, path);
    }

    internal static byte[] ReadProtected(string path)
    {
        var encrypted = File.ReadAllBytes(path);
        return System.Security.Cryptography.ProtectedData.Unprotect(
            encrypted, null, System.Security.Cryptography.DataProtectionScope.CurrentUser);
    }
}
