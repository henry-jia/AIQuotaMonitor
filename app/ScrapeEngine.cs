using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace AIQuotaMonitor;

public class ScrapeException : Exception
{
    public ScrapeException(string message) : base(message) { }
}

/// <summary>
/// 抓取引擎：一个隐藏的 1x1 移出屏幕的宿主窗口承载共享 WebView2，
/// 所有服务共用一个用户数据目录（同一域名只需登录一次）。
/// 所有公开方法必须在 UI 线程调用。
/// </summary>
public sealed class ScrapeEngine
{
    private Window? _host;
    private WebView2? _webView;
    private CoreWebView2Environment? _env;
    private readonly SemaphoreSlim _initLock = new(1, 1);
    private readonly SemaphoreSlim _scrapeLock = new(1, 1);

    public static string UserDataFolder =>
        System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "AIQuotaMonitor", "WebView2UserData");

    public CoreWebView2Environment? Env => _env;

    /// <summary>检测 WebView2 Runtime 是否可用，缺失时抛出带本地化提示的异常。</summary>
    public static void CheckRuntime()
    {
        try
        {
            CoreWebView2Environment.GetAvailableBrowserVersionString();
        }
        catch (WebView2RuntimeNotFoundException)
        {
            throw new ScrapeException(I18n.T("webview2_missing"));
        }
    }

    public async Task EnsureInitializedAsync()
    {
        if (_webView?.CoreWebView2 != null) return;
        await _initLock.WaitAsync();
        try
        {
            if (_webView?.CoreWebView2 != null) return;
            CheckRuntime();
            // Language 设为中文：让按浏览器语言渲染的网站（如 Kimi）直接显示中文页面，
            // 与中文标签/默认识别规则保持一致
            _env = await CoreWebView2Environment.CreateAsync(null, UserDataFolder,
                new CoreWebView2EnvironmentOptions { Language = "zh-CN" });
            // 宿主窗口：移出屏幕但保持正常视口尺寸。
            // 不能用 1x1/透明——懒加载的 SPA（如阿里云控制台微前端）靠视口/可见性
            // 触发渲染，视口过小就不生成内容 DOM，抓取永远落空
            _host = new Window
            {
                Width = 1280,
                Height = 800,
                Left = -32000,
                Top = -32000,
                WindowStyle = WindowStyle.None,
                ShowInTaskbar = false,
                ShowActivated = false,
            };
            _host.Show();
            _webView = new WebView2();
            _host.Content = _webView;
            await _webView.EnsureCoreWebView2Async(_env);
            // 还原上次保存的 Cookie（含会话级登录票据，见 CookieStore 注释）
            await CookieStore.RestoreAsync(_webView.CoreWebView2!);
        }
        finally
        {
            _initLock.Release();
        }
    }

    /// <summary>抓取单个服务，单个服务失败不影响其他服务。
    /// fetchSubscription=false 时跳过订阅信息扫描（主窗口按缓存时效决定）。</summary>
    public async Task<ServiceScrapeResult> ScrapeAsync(ServiceConfig service, bool fetchSubscription = true,
        int timeoutSeconds = 30, CancellationToken ct = default)
    {
        var result = new ServiceScrapeResult { Service = service, Time = DateTimeOffset.Now };
        await _scrapeLock.WaitAsync(ct);
        try
        {
            await EnsureInitializedAsync();
            var wv = _webView!.CoreWebView2!;

            // 1) 导航并等待完成（导航报错时检查页面实况，重定向链误报则继续）
            await NavigateAsync(wv, service.Url, timeoutSeconds, ct);

            // 2) 等待内容渲染：轮询任一定位文本出现即提前结束，「额外等待秒数」为超时上限
            await WaitForContentAsync(wv, service, ct);

            // 3) 未登录判定（配置了指示选择器时）
            if (!string.IsNullOrWhiteSpace(service.LoginIndicatorSelector))
            {
                string checkJs = $"(function(){{try{{return !!document.querySelector({JsString(service.LoginIndicatorSelector)})}}catch(e){{return false}}}})()";
                if (await EvalRawAsync(wv, checkJs) == "true")
                {
                    result.Status = ScrapeStatus.NeedLogin;
                    return result;
                }
            }

            // 4) 逐条规则提取
            foreach (var rule in service.Rules)
                result.Rules.Add(await ScrapeRuleAsync(wv, service, rule));

            // 4b) 全部抓不到：内容可能在跨域 iframe 里（同源策略导致 JS 读不到，
            //     登录窗口里能看到是因为渲染不受限）。把隐藏浏览器直接导航到
            //     读不到的 iframe 地址再抓一遍，任一 iframe 出数即采用
            bool navigatedIntoFrame = false;
            if (result.Rules.Count > 0 && result.Rules.TrueForAll(r => r.Percent == null))
            {
                foreach (var src in await GetUnreadableFrameSrcsAsync(wv))
                {
                    try
                    {
                        await NavigateAsync(wv, src, timeoutSeconds, ct);
                        await WaitForContentAsync(wv, service, ct);
                        var retry = new List<RuleResult>();
                        foreach (var rule in service.Rules)
                            retry.Add(await ScrapeRuleAsync(wv, service, rule));
                        if (retry.Any(r => r.Percent != null))
                        {
                            result.Rules = retry;
                            navigatedIntoFrame = true;
                            break;
                        }
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        // 单个 iframe 失败，继续下一个
                    }
                }
            }

            // 5) 汇总状态：所有规则都抓不到值时，区分「未登录」与「已登录但页面结构变化」——
            //    后者不该再引导用户去登录，而是说明定位文本失配并给「查看页面」入口
            if (result.Rules.Count > 0 && result.Rules.TrueForAll(r => r.Percent == null))
            {
                result.Status = ScrapeStatus.Error;
                // 页面实况：标题 + 正文开头 + 密码框数量。既附进错误信息，也用于登录态推断
                string? title = null, pageText = null;
                int pwdCount = 0;
                try
                {
                    const string infoJs =
                        "JSON.stringify({title:document.title||''," +
                        "text:((document.body&&document.body.innerText)||'').substring(0,300)," +
                        "pwd:document.querySelectorAll('input[type=password]').length})";
                    var info = await EvalStringAsync(wv, infoJs);
                    using var doc = JsonDocument.Parse(info);
                    title = doc.RootElement.GetProperty("title").GetString();
                    pageText = doc.RootElement.GetProperty("text").GetString();
                    pwdCount = doc.RootElement.GetProperty("pwd").GetInt32();
                }
                catch (JsonException) { /* JSON 损坏按「可能未登录」保守处理；其他异常（如 WebView 崩溃）外抛暴露真实原因 */ }

                // 登录态推断：配置了登录指示选择器且走到这里 = 指示未命中 = 已登录；
                // 页面有密码框，或短页面带登录关键词 → 大概率未登录；页面空白无法判断时保守归为未登录
                bool likelyLoggedOut;
                if (!string.IsNullOrWhiteSpace(service.LoginIndicatorSelector))
                    likelyLoggedOut = false;
                else if (string.IsNullOrWhiteSpace(pageText))
                    likelyLoggedOut = true;
                else
                {
                    bool loginHint = System.Text.RegularExpressions.Regex.IsMatch(
                        title + " " + pageText, @"登录|登陆|扫码|sign[\s-]?in|log[\s-]?in|password|密码",
                        System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                    likelyLoggedOut = pwdCount > 0 || (pageText.Length < 800 && loginHint);
                }
                result.SuggestLogin = likelyLoggedOut;

                var sb = new System.Text.StringBuilder(
                    I18n.T(likelyLoggedOut ? "all_rules_failed" : "all_rules_failed_structure"));
                // 逐条规则的具体失败原因（配置错误、正则未命中等），不再被笼统提示吞掉
                foreach (var r in result.Rules.Where(r => r.Error != null))
                    sb.Append("\n" + I18n.T("rule_error_line", r.Label, r.Error!));
                if (!string.IsNullOrWhiteSpace(title)) sb.Append("\n" + I18n.T("page_title_line", title));
                if (!string.IsNullOrWhiteSpace(pageText))
                    sb.Append("\n" + I18n.T("page_body_line", pageText.Replace("\r", " ").Replace("\n", " ⏎ ")));
                sb.Append("\n" + I18n.T(likelyLoggedOut ? "error_footer" : "error_footer_view"));
                result.ErrorMessage = sb.ToString();
            }
            else
            {
                result.Status = ScrapeStatus.Ok;
            }

            // 6) 订阅信息智能扫描（到期时间 + 自动续费）：
            //    SubscriptionUrl 为空时在用量页面上扫（零额外导航）；否则导航到订阅页
            if (result.Status == ScrapeStatus.Ok && fetchSubscription)
            {
                // 4b 若已导航进跨域 iframe，「顺带扫」前须回到原用量页，否则订阅扫描会扫到 iframe 页面
                if (navigatedIntoFrame && string.IsNullOrWhiteSpace(service.SubscriptionUrl))
                {
                    try
                    {
                        await NavigateAsync(wv, service.Url, timeoutSeconds, ct);
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        // 回不去就在当前页 best-effort 扫一次
                    }
                }
                if (!string.IsNullOrWhiteSpace(service.SubscriptionUrl))
                {
                    try
                    {
                        await NavigateAsync(wv, service.SubscriptionUrl!, timeoutSeconds, ct);
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        // 订阅页打不开不影响配额结果，仍尝试在当前页扫一次
                    }
                }
                result.Subscription = await ScanSubscriptionAsync(wv, service, ct);
                if (result.Subscription != null)
                    result.SubscriptionFetchedAt = DateTimeOffset.Now;
            }

            // 7) 抓取后保存 Cookie：登录成功/票据续期后及时落盘（含会话级票据）
            await CookieStore.SaveAsync(wv);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            result.Status = ScrapeStatus.Error;
            result.ErrorMessage = ex.Message;
        }
        finally
        {
            _scrapeLock.Release();
        }
        return result;
    }

    /// <summary>导航并等待完成；导航报错时检查页面实况（重定向链误报失败但页面已加载则继续）。</summary>
    private async Task NavigateAsync(CoreWebView2 wv, string url, int timeoutSeconds, CancellationToken ct)
    {
        var navDone = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        void OnNav(object? s, CoreWebView2NavigationCompletedEventArgs e) => navDone.TrySetResult(e.IsSuccess);
        wv.NavigationCompleted += OnNav;
        // 取消时中止进行中的导航，避免 WebView 在后台继续加载、下次导航落在不确定状态
        using var stopOnCancel = ct.Register(() => { try { wv.Stop(); } catch { /* WebView 已 dispose 等 */ } });
        try
        {
            wv.Navigate(url);
            using var delayCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            var timeout = Task.Delay(TimeSpan.FromSeconds(timeoutSeconds), delayCts.Token);
            if (await Task.WhenAny(navDone.Task, timeout) != navDone.Task)
                throw new ScrapeException(I18n.T("page_load_timeout", timeoutSeconds));
            delayCts.Cancel(); // 导航已完成，释放悬空计时器
            if (navDone.Task.Result) return;

            await Task.Delay(TimeSpan.FromMilliseconds(1500), ct);
            const string stateJs =
                "JSON.stringify({url:location.href,title:document.title||''," +
                "len:((document.body&&document.body.innerText)||'').trim().length})";
            try
            {
                using var doc = JsonDocument.Parse(await EvalStringAsync(wv, stateJs));
                if (doc.RootElement.GetProperty("len").GetInt32() > 50) return;
                var finalUrl = doc.RootElement.GetProperty("url").GetString();
                var title = doc.RootElement.GetProperty("title").GetString();
                throw new ScrapeException(I18n.T("nav_failed_detail", finalUrl ?? "", title ?? ""));
            }
            catch (ScrapeException) { throw; }
            catch
            {
                throw new ScrapeException(I18n.T("nav_failed"));
            }
        }
        finally
        {
            wv.NavigationCompleted -= OnNav;
        }
    }

    /// <summary>等待内容渲染：自动定位规则直接跑提取脚本，要求「成功且质量达标」
    /// （真实数值已渲染，而非进度条轴刻度先行出现）；选择器模式退化为定位文本出现即继续。</summary>
    private async Task WaitForContentAsync(CoreWebView2 wv, ServiceConfig service, CancellationToken ct)
    {
        if (service.Rules.Count == 0)
        {
            if (service.ExtraWaitSeconds > 0)
                await Task.Delay(TimeSpan.FromSeconds(service.ExtraWaitSeconds), ct);
            return;
        }
        var autoRule = service.Rules.FirstOrDefault(r => string.IsNullOrWhiteSpace(r.Selector));
        string? pollJs = null;
        if (autoRule == null)
        {
            var anchors = service.Rules
                .Select(r => string.IsNullOrWhiteSpace(r.MatchText) ? r.Label : r.MatchText!)
                .ToList();
            pollJs = $$"""
            (function () {
              try {
                var labels = {{JsonSerializer.Serialize(anchors)}}
                  .join("|").split("|")
                  .map(function (s) { return s.replace(/\s+/g, ""); })
                  .filter(function (s) { return s.length > 0; });
                // 收集主文档 + 同源 iframe + 开放的 shadow root（shadow root 无 body，用根节点文本）
                var docs = [document];
                for (var di = 0; di < docs.length; di++) {
                  var frames = docs[di].querySelectorAll("iframe");
                  for (var fi = 0; fi < frames.length; fi++) {
                    try { var cd = frames[fi].contentDocument; if (cd) docs.push(cd); } catch (e) {}
                  }
                  var els = docs[di].querySelectorAll("*");
                  for (var si = 0; si < els.length; si++) {
                    if (els[si].shadowRoot) docs.push(els[si].shadowRoot);
                  }
                }
                var t = "";
                for (var d0 = 0; d0 < docs.length; d0++) {
                  try { t += ((docs[d0].body && docs[d0].body.innerText) || docs[d0].textContent || ""); } catch (e) {}
                }
                t = t.replace(/\s+/g, "");
                for (var i = 0; i < labels.length; i++) { if (t.indexOf(labels[i]) >= 0) return "hit"; }
                return "";
              } catch (e) { return ""; }
            })()
            """;
        }
        var deadline = DateTime.Now + TimeSpan.FromSeconds(Math.Max(2, service.ExtraWaitSeconds));
        while (DateTime.Now < deadline)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(500), ct);
            if (autoRule != null)
            {
                if (await PollQualityAsync(wv, autoRule)) break;
            }
            else if (await EvalStringAsync(wv, pollJs!) == "hit")
            {
                break;
            }
        }
    }

    /// <summary>订阅信息智能扫描：在页面全文（含同源 iframe）里识别到期时间与自动续费状态。
    /// 扫描不到时按「额外等待秒数」轮询重试（SPA 异步渲染）。</summary>
    private async Task<SubscriptionInfo?> ScanSubscriptionAsync(CoreWebView2 wv, ServiceConfig service, CancellationToken ct)
    {
        const string js = """
        (function () {
          try {
            var docs = [document];
            for (var di = 0; di < docs.length; di++) {
              var frames = docs[di].querySelectorAll("iframe");
              for (var fi = 0; fi < frames.length; fi++) {
                try { var cd = frames[fi].contentDocument; if (cd) docs.push(cd); } catch (e) {}
              }
              var els = docs[di].querySelectorAll("*");
              for (var si = 0; si < els.length; si++) {
                if (els[si].shadowRoot) docs.push(els[si].shadowRoot);
              }
            }
            var t = "";
            for (var d0 = 0; d0 < docs.length; d0++) { try { t += (((docs[d0].body && docs[d0].body.innerText) || docs[d0].textContent || "")) + "\n"; } catch (e) {} }

            // 到期时间：绝对日期 / 剩余天数 / 下次自动续费时间 / 英文 canceled/expires/renews
            var expire = "";
            var pats = [
              /(?:结束时间|到期时间|到期日|有效期至)\s*[：:]?\s*(\d{4}\s*[-/年]\s*\d{1,2}\s*[-/月]\s*\d{1,2}\s*日?(?:\s+\d{1,2}[:：]\d{2}(?:[:：]\d{2})?)?)/,
              /剩余(?:天数|时间)\s*[：:]?\s*(\d+)\s*天/,
              /下次自动续费时间\s*[：:]\s*(\d{4}\s*[-/年]\s*\d{1,2}\s*[-/月]\s*\d{1,2}\s*日?)/,
              /(\d{1,2}\s*月\s*\d{1,2}\s*日)\s*自动续费/,
              /将于\s*(\d{4}\s*年\s*\d{1,2}\s*月\s*\d{1,2}\s*日(?:\s*\d{1,2}[:：]\d{2})?)\s*(?:取消|到期|续费|续订)/,
              /(?:will be canceled on|cancels? on|expires? on|renews? on)\s+([A-Za-z]+\s+\d{1,2},?\s+\d{4})/i
            ];
            for (var i = 0; i < pats.length; i++) { var m = t.match(pats[i]); if (m) { expire = m[1].trim(); break; } }

            // 自动续费：先查文本（注意排除「未开启」「开启自动续费」按钮文案），再查开关控件状态
            var auto = "";
            if (/自动续费[\s\S]{0,10}(未开启|未开通|已关闭|关闭)/.test(t) ||
                /(?:套餐|订阅|计划|plan)[\s\S]{0,15}(已取消|将被取消|取消)|(?:不会|不再)自动续费/i.test(t) ||
                /will be canceled|cancellation effective/i.test(t)) auto = "off";
            else if (/自动续费[\s\S]{0,10}(?<!未)(已开启|开启)/.test(t)) auto = "on";
            else if (/下次自动续费时间|\d{1,2}\s*月\s*\d{1,2}\s*日\s*自动续费|将自动续费|自动续费中|会自动续费|自动续订|renews automatically|auto-?renew(al)?\s*(is\s+)?on|next billing/i.test(t)) auto = "on";
            if (!auto) {
              var all = document.querySelectorAll("body *");
              for (var i = 0; i < all.length; i++) {
                var txt = (all[i].innerText || "").replace(/\s+/g, "");
                if (!txt || txt.length > 12 || txt.indexOf("自动续费") < 0) continue;
                var scope = all[i].parentElement || all[i];
                var sw = scope.querySelector("[role=switch], input[type=checkbox], [aria-checked]");
                if (sw) {
                  auto = (sw.getAttribute("aria-checked") === "true" || sw.checked === true) ? "on" : "off";
                  break;
                }
                var sw2 = scope.querySelector("[class*=switch], [class*=Switch], [class*=toggle], [class*=Toggle]");
                if (sw2) {
                  auto = /on|active|checked/i.test(String(sw2.className || "")) ? "on" : "off";
                  break;
                }
              }
            }
            return JSON.stringify({ expire: expire, auto: auto });
          } catch (e) { return JSON.stringify({ expire: "", auto: "" }); }
        })()
        """;

        var deadline = DateTime.Now + TimeSpan.FromSeconds(Math.Max(2, service.ExtraWaitSeconds));
        while (true)
        {
            string expireText = "", autoText = "";
            try
            {
                using var doc = JsonDocument.Parse(await EvalStringAsync(wv, js));
                expireText = doc.RootElement.GetProperty("expire").GetString() ?? "";
                autoText = doc.RootElement.GetProperty("auto").GetString() ?? "";
            }
            catch { /* 本轮扫描失败，按超时重试 */ }

            if ((!string.IsNullOrEmpty(expireText) || !string.IsNullOrEmpty(autoText)) || DateTime.Now >= deadline)
            {
                if (string.IsNullOrEmpty(expireText) && string.IsNullOrEmpty(autoText)) return null;
                var now = DateTime.Now;
                var info = new SubscriptionInfo();
                if (!string.IsNullOrEmpty(expireText))
                {
                    info.ExpireAt = System.Text.RegularExpressions.Regex.IsMatch(expireText, @"^\d{1,4}$")
                        ? now.AddDays(int.Parse(expireText))   // 「剩余天数 31 天」这类纯数字
                        : ResetTimeParser.Parse(expireText, now);
                }
                info.AutoRenew = autoText switch { "on" => true, "off" => false, _ => null };
                return info.ExpireAt == null && info.AutoRenew == null ? null : info;
            }
            await Task.Delay(TimeSpan.FromMilliseconds(500), ct);
        }
    }

    /// <summary>收集页面里 JS 读不到内容的 iframe 地址（跨域 iframe，配额内容可能在其中）。</summary>
    private async Task<IReadOnlyList<string>> GetUnreadableFrameSrcsAsync(CoreWebView2 wv)
    {
        const string js = """
        (function () {
          try {
            var srcs = [];
            (function walk(doc) {
              var frames = doc.querySelectorAll("iframe");
              for (var i = 0; i < frames.length; i++) {
                var cd = null;
                try { cd = frames[i].contentDocument; } catch (e) {}
                if (cd) { walk(cd); continue; }
                var s = "";
                try { s = frames[i].src || ""; } catch (e) {}
                if (s.indexOf("http") === 0 && srcs.indexOf(s) < 0) srcs.push(s);
              }
            })(document);
            return JSON.stringify(srcs.slice(0, 5));
          } catch (e) { return "[]"; }
        })()
        """;
        try
        {
            var json = await EvalStringAsync(wv, js);
            return JsonSerializer.Deserialize<List<string>>(json) ?? new List<string>();
        }
        catch
        {
            return new List<string>();
        }
    }

    private async Task<RuleResult> ScrapeRuleAsync(CoreWebView2 wv, ServiceConfig service, QuotaRule rule)
    {
        // 选择器留空 = 自动定位模式：用「标签」文字在页面上找到锚点，
        // 再向上找第一个匹配正则的容器，在容器内取数值和重置时间。
        if (string.IsNullOrWhiteSpace(rule.Selector))
        {
            var others = service.Rules
                .Where(r => !ReferenceEquals(r, rule))
                .Select(r => string.IsNullOrWhiteSpace(r.MatchText) ? r.Label : r.MatchText!)
                .ToList();
            return await ScrapeRuleAutoAsync(wv, rule, others);
        }
        return await ScrapeRuleBySelectorAsync(wv, rule);
    }

    /// <summary>自动定位模式：标签即页面上的文字（如「每周使用额度」），无需 CSS 知识。
    /// otherAnchors = 其他规则的锚点，用于拒绝混入兄弟配额的共享祖先容器。</summary>
    private async Task<RuleResult> ScrapeRuleAutoAsync(CoreWebView2 wv, QuotaRule rule,
        System.Collections.Generic.IReadOnlyList<string> otherAnchors)
    {
        var rr = new RuleResult { Label = rule.Label };
        var resetPat = string.IsNullOrWhiteSpace(rule.ResetPattern)
            ? QuotaRule.DefaultResetPattern
            : rule.ResetPattern;
        string payload;
        try
        {
            payload = await EvalStringAsync(wv, BuildAutoExtractScript(rule, resetPat, otherAnchors));
        }
        catch (Exception ex)
        {
            rr.Error = I18n.T("script_failed", ex.Message);
            return rr;
        }
        ParseRulePayload(rr, rule, payload, out string? reset);
        if (rr.Error == null && !string.IsNullOrWhiteSpace(reset))
        {
            rr.ResetText = reset;
            rr.ResetAt = ResetTimeParser.Parse(reset, DateTime.Now);
        }
        return rr;
    }

    /// <summary>自动定位提取脚本：锚点定位 → 容器内取「字号最大的匹配元素」→ 同区域找重置时间。
    /// 返回 { ok, groups, text, reset, quality }；quality 供等待轮询判断真实数值是否已渲染。</summary>
    private static string BuildAutoExtractScript(QuotaRule rule, string resetPat,
        System.Collections.Generic.IReadOnlyList<string> otherAnchors) => $$"""
        (function () {
          try {
            var labels = {{JsString(string.IsNullOrWhiteSpace(rule.MatchText) ? rule.Label : rule.MatchText)}}
              .split("|")
              .map(function (s) { return s.replace(/\s+/g, ""); })
              .filter(function (s) { return s.length > 0; });
            var others = {{JsonSerializer.Serialize(otherAnchors)}};
            if (!labels.length) return JSON.stringify({ ok:false, err:"labels_empty" });
            var re = new RegExp({{JsString(rule.Pattern)}}, "i");
            // 文档收集：主文档 + 同源 iframe + 所有开放的 shadow root
            // （ChatGPT 设置页改版后使用 shadow DOM，querySelectorAll 默认穿不透）
            function collectDocs(root) {
              var docs = [root];
              for (var di = 0; di < docs.length; di++) {
                var frames = docs[di].querySelectorAll("iframe");
                for (var fi = 0; fi < frames.length; fi++) {
                  try { var cd = frames[fi].contentDocument; if (cd) docs.push(cd); } catch (e) {}
                }
                var els = docs[di].querySelectorAll("*");
                for (var si = 0; si < els.length; si++) {
                  if (els[si].shadowRoot) docs.push(els[si].shadowRoot);
                }
              }
              return docs;
            }
            // 向上找父元素，可跨越 shadow root 边界（shadow 内元素的 parentElement 为 null）
            function parentOf(el) {
              if (!el) return null;
              if (el.parentElement) return el.parentElement;
              try { var r = el.getRootNode && el.getRootNode(); if (r && r.host) return r.host; } catch (e) {}
              return null;
            }
            var docs = collectDocs(document);
            // 1) 找包含任一定位文本的最内层元素作为锚点
            var anchor = null, anchorLen = 1e15;
            for (var d0 = 0; d0 < docs.length; d0++) {
              var all = docs[d0].querySelectorAll("*");
              for (var i = 0; i < all.length; i++) {
                var t = (all[i].innerText || "").replace(/\s+/g, "");
                if (!t) continue;
                var hit = false;
                for (var j = 0; j < labels.length; j++) { if (t.indexOf(labels[j]) >= 0) { hit = true; break; } }
                if (!hit) continue;
                if (t.length < anchorLen) { anchor = all[i]; anchorLen = t.length; }
              }
            }
            if (!anchor) return JSON.stringify({ ok:false, err:"anchor_not_found", arg:labels.join(" / ") });
            // 2) 从锚点向上找第一个匹配正则的容器（最多 12 层，改版后的页面 DOM 更深）
            var node = anchor, text = "", container = null;
            for (var d = 0; d <= 12 && node; d++) {
              text = (node.innerText || "").trim();
              if (re.test(text)) { container = node; break; }
              node = parentOf(node);
            }
            if (!container) return JSON.stringify({ ok:false, err:"no_match", arg:labels.join(" / "), text:(anchor.innerText||"").substring(0,600) });
            // 3) 容器内可能有多个数字（如进度条轴刻度 0% 50% 90% 100% 先于真实数值渲染），
            //    取「自身文本匹配正则且字号最大」的元素；无元素级匹配则回退容器整体匹配
            var m = null, bestSize = -1, bestLen = 1e15, matchCount = 0;
            var els = container.querySelectorAll("*");
            for (var i = 0; i < els.length; i++) {
              var et = (els[i].innerText || "").trim();
              if (!et || et.length > 60) continue;
              var mm = et.match(re);
              if (!mm) continue;
              matchCount++;
              var fs = 0;
              try { fs = parseFloat(getComputedStyle(els[i]).fontSize) || 0; } catch (e) {}
              if (fs > bestSize || (fs === bestSize && et.length < bestLen)) { m = mm; bestSize = fs; bestLen = et.length; }
            }
            if (!m) m = text.match(re);
            if (!m) return JSON.stringify({ ok:false, err:"no_match", arg:labels.join(" / "), text:text.substring(0,600) });
            // 质量标记：唯一匹配，或最佳匹配是大字号（真实数值）；用于等待轮询判断数据是否已渲染
            var quality = (matchCount <= 1) || (bestSize >= 16);
            var g = [];
            for (var i = 1; i < m.length; i++) g.push(m[i] == null ? "" : m[i]);
            // 4) 重置时间：先在数值容器内匹配；没有则向上再找 4 层
            //    （有的页面重置时间在更大的区块里，如「总用量」的重置日期在区块右侧）。
            //    但祖先里若混入其他配额的标签（共享容器），拒绝接受——
            //    否则 0% 用量时会把兄弟配额的重置日期抓过来（智谱/阿里 5 小时行的教训）
            var reset = "";
            var rre = new RegExp({{JsString(resetPat)}}, "i");
            var rnode = container, rfound = false;
            for (var d2 = 0; d2 <= 4 && rnode && !rfound; d2++) {
              var rt = (rnode.innerText || "").trim();
              if (d2 > 0) {
                var rtFlat = rt.replace(/\s+/g, "");
                var blocked = false;
                for (var oi = 0; oi < others.length; oi++) {
                  if (others[oi] && rtFlat.indexOf(others[oi]) >= 0) { blocked = true; break; }
                }
                if (blocked) break;
              }
              var rm = rt.match(rre);
              if (rm) { reset = (rm.length > 1 && rm[1] ? rm[1] : rm[0]).trim(); rfound = true; break; }
              rnode = parentOf(rnode);
            }
            return JSON.stringify({ ok:true, groups:g, text:text.substring(0,600), reset:reset, quality:quality });
          } catch (e) { return JSON.stringify({ ok:false, err:"script_exception", arg:String(e && e.message || e) }); }
        })()
        """;

    /// <summary>等待轮询用：跑一次自动定位提取，要求成功且质量达标（真实数值已渲染）。</summary>
    private async Task<bool> PollQualityAsync(CoreWebView2 wv, QuotaRule rule)
    {
        var resetPat = string.IsNullOrWhiteSpace(rule.ResetPattern)
            ? QuotaRule.DefaultResetPattern
            : rule.ResetPattern;
        try
        {
            var payload = await EvalStringAsync(wv, BuildAutoExtractScript(rule, resetPat,
                System.Array.Empty<string>()));
            using var doc = JsonDocument.Parse(payload);
            var root = doc.RootElement;
            if (!root.TryGetProperty("ok", out var ok) || !ok.GetBoolean()) return false;
            return !root.TryGetProperty("quality", out var q) || q.GetBoolean();
        }
        catch
        {
            return false;
        }
    }

    /// <summary>选择器模式：只在该 CSS 选择器匹配的元素内查找（高级用法）。</summary>
    private async Task<RuleResult> ScrapeRuleBySelectorAsync(CoreWebView2 wv, QuotaRule rule)
    {
        var rr = new RuleResult { Label = rule.Label };
        var selectorJs = JsString(rule.Selector);
        string script = $$"""
        (function () {
          try {
            var el = document.querySelector({{selectorJs}});
            if (!el) return JSON.stringify({ ok:false, err:"selector_no_match" });
            var text = (el.innerText || el.textContent || "");
            var m = text.match(new RegExp({{JsString(rule.Pattern)}}, "i"));
            if (!m) return JSON.stringify({ ok:false, err:"regex_no_match", text:text.substring(0,600) });
            var g = [];
            for (var i = 1; i < m.length; i++) g.push(m[i] == null ? "" : m[i]);
            return JSON.stringify({ ok:true, groups:g, text:text.substring(0,600) });
          } catch (e) { return JSON.stringify({ ok:false, err:"script_exception", arg:String(e && e.message || e) }); }
        })()
        """;

        string payload;
        try
        {
            payload = await EvalStringAsync(wv, script);
        }
        catch (Exception ex)
        {
            rr.Error = I18n.T("script_failed", ex.Message);
            return rr;
        }

        ParseRulePayload(rr, rule, payload, out _);
        if (rr.Error != null) return rr;

        // 可选：抓取重置时间文本（只展示原文）
        if (!string.IsNullOrWhiteSpace(rule.ResetSelector) || !string.IsNullOrWhiteSpace(rule.ResetPattern))
        {
            rr.ResetText = await ScrapeResetAsync(wv, rule);
            rr.ResetAt = ResetTimeParser.Parse(rr.ResetText, DateTime.Now);
        }
        return rr;
    }

    /// <summary>把 JS 返回的错误码映射为本地化文案（旧版中文原文则原样透传）。</summary>
    private static string MapJsError(string code, string? arg) => code switch
    {
        "labels_empty" => I18n.T("js_labels_empty"),
        "anchor_not_found" => I18n.T("js_anchor_not_found", arg ?? ""),
        "no_match" => I18n.T("js_no_match", arg ?? ""),
        "selector_no_match" => I18n.T("js_selector_no_match"),
        "regex_no_match" => I18n.T("js_regex_no_match"),
        "script_exception" => I18n.T("js_script_exception", arg ?? ""),
        _ => code,
    };

    /// <summary>解析两种模式共用的 JS 返回（ok/groups/text/reset），算出百分比与明细。</summary>
    private static void ParseRulePayload(RuleResult rr, QuotaRule rule, string payload, out string? reset)
    {
        reset = null;
        try
        {
            using var doc = JsonDocument.Parse(payload);
            var root = doc.RootElement;
            if (root.TryGetProperty("text", out var t)) rr.RawText = t.GetString();
            if (root.TryGetProperty("reset", out var rs)) reset = rs.GetString();
            if (!root.TryGetProperty("ok", out var ok) || !ok.GetBoolean())
            {
                rr.Error = root.TryGetProperty("err", out var err)
                    ? MapJsError(err.GetString() ?? "", root.TryGetProperty("arg", out var a) ? a.GetString() : null)
                    : I18n.T("unknown_error");
                return;
            }

            var groups = new List<string>();
            foreach (var g in root.GetProperty("groups").EnumerateArray())
                groups.Add(g.GetString() ?? "");

            if (rule.Type == QuotaRule.TypeFraction)
            {
                if (groups.Count < 2)
                {
                    rr.Error = I18n.T("fraction_needs_two_groups");
                    return;
                }
                if (!TryNum(groups[0], out double a) || !TryNum(groups[1], out double b) || b <= 0)
                {
                    rr.Error = I18n.T("fraction_parse_failed", groups[0], groups[1]);
                    return;
                }
                rr.Percent = a / b * 100.0;
                rr.Detail = $"{groups[0]} / {groups[1]}";
            }
            else
            {
                if (groups.Count < 1 || !TryNum(groups[0], out double p))
                {
                    rr.Error = I18n.T("percent_parse_failed", groups.Count > 0 ? groups[0] : I18n.T("no_capture_group"));
                    return;
                }
                rr.Percent = rule.Invert ? 100 - p : p; // Invert：页面显示的是「剩余」百分比
            }
        }
        catch (Exception ex)
        {
            rr.Error = I18n.T("parse_failed", ex.Message);
        }
    }

    private static async Task<string?> ScrapeResetAsync(CoreWebView2 wv, QuotaRule rule)
    {
        var selectorJs = string.IsNullOrWhiteSpace(rule.ResetSelector) ? "null" : JsString(rule.ResetSelector);
        var patternJs = string.IsNullOrWhiteSpace(rule.ResetPattern) ? "null" : JsString(rule.ResetPattern);
        string script = $$"""
        (function () {
          try {
            var sel = {{selectorJs}};
            var el = sel ? document.querySelector(sel) : document.body;
            if (!el) return "";
            var text = (el.innerText || el.textContent || "").trim();
            var pat = {{patternJs}};
            if (!pat) return text.substring(0,100);
            var m = text.match(new RegExp(pat, "i"));
            if (!m) return "";
            return (m.length > 1 && m[1] ? m[1] : m[0]).trim();
          } catch (e) { return ""; }
        })()
        """;
        try
        {
            var s = await EvalStringAsync(wv, script);
            return string.IsNullOrWhiteSpace(s) ? null : s;
        }
        catch
        {
            return null;
        }
    }

    // ExecuteScriptAsync 返回 JSON 编码结果：脚本的字符串返回值会被再包一层引号
    private static async Task<string> EvalStringAsync(CoreWebView2 wv, string script)
    {
        var raw = await EvalRawAsync(wv, script);
        if (string.IsNullOrEmpty(raw) || raw == "null") return "";
        try { return JsonSerializer.Deserialize<string>(raw) ?? raw; }
        catch { return raw; }
    }

    private static async Task<string> EvalRawAsync(CoreWebView2 wv, string script) =>
        await wv.ExecuteScriptAsync(script);

    /// <summary>把 C# 字符串转义为 JS 字符串字面量（JSON 序列化恰好兼容）。</summary>
    private static string JsString(string? s) => JsonSerializer.Serialize(s ?? "");

    private static bool TryNum(string s, out double value) =>
        double.TryParse(s.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out value) ||
        double.TryParse(s.Trim(), NumberStyles.Float, CultureInfo.CurrentCulture, out value);
}
