using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using AIQuotaMonitor;

var now = new DateTime(2026, 8, 25, 12, 0, 0, DateTimeKind.Local);
var failures = new List<string>();
var checks = 0;
I18n.Initialize(I18n.LangEn);

// Run independently of persistence checks so card failures remain observable.
var cardChecks = ServiceCardLoginTests.Run();
checks += cardChecks.Checks;
failures.AddRange(cardChecks.Failures);
foreach (var failure in cardChecks.Failures) Console.Error.WriteLine(failure);
var creditCardChecks = ServiceCardCreditsTests.Run();
checks += creditCardChecks.Checks;
failures.AddRange(creditCardChecks.Failures);
foreach (var failure in creditCardChecks.Failures) Console.Error.WriteLine(failure);
var creditHistoryChecks = CreditHistoryWindowTests.Run();
checks += creditHistoryChecks.Checks;
failures.AddRange(creditHistoryChecks.Failures);
foreach (var failure in creditHistoryChecks.Failures) Console.Error.WriteLine(failure);
var settingsCreditsChecks = SettingsCreditsTests.Run();
checks += settingsCreditsChecks.Checks;
failures.AddRange(settingsCreditsChecks.Failures);
foreach (var failure in settingsCreditsChecks.Failures) Console.Error.WriteLine(failure);
if (args.Contains("--card-login-only"))
{
    Console.WriteLine($"{(failures.Count == 0 ? "PASS" : "FAIL")} card login {checks - failures.Count}/{checks}");
    return failures.Count == 0 ? 0 : 1;
}

CheckReset("5天11时56分钟后刷新", now.AddDays(5).AddHours(11).AddMinutes(56));

checks++;
if (!ScrapeEngine.RequiresFreshDocument("https://chatgpt.com/#settings/Usage", "https://chatgpt.com/#settings/Usage") ||
    !ScrapeEngine.RequiresFreshDocument("https://chatgpt.com/#settings/Usage", "https://chatgpt.com/#settings/Billing") ||
    !ScrapeEngine.RequiresFreshDocument("https://chatgpt.com/settings/usage", "https://chatgpt.com/settings/usage") ||
    ScrapeEngine.RequiresFreshDocument("https://chatgpt.com/settings/usage", "https://chatgpt.com/settings/billing") ||
    ScrapeEngine.RequiresFreshDocument("https://chatgpt.com/settings/usage?a=1", "https://chatgpt.com/settings/usage?a=2") ||
    ScrapeEngine.RequiresFreshDocument("https://chatgpt.com/#settings/Usage", "https://example.com/#settings/Usage") ||
    ScrapeEngine.RequiresFreshDocument("about:blank", "about:blank") ||
    ScrapeEngine.RequiresFreshDocument("invalid", "https://chatgpt.com/#settings/Usage"))
    failures.Add("Fresh-document navigation must recover repeated/hash URLs without resetting unrelated documents or recursing on about:blank.");

CheckReset("28天11时56分钟后刷新", now.AddDays(28).AddHours(11).AddMinutes(56));
CheckReset("Refreshes in 2 hours 30 mins", now.AddHours(2).AddMinutes(30));
CheckReset("Resets in 4d 22h", now.AddDays(4).AddHours(22));
CheckReset("Resets in 4d", now.AddDays(4));
CheckReset("Resets in 22h 30m", now.AddHours(22).AddMinutes(30));
CheckReset("3 天后重置", now.AddDays(3));
CheckReset("2026-09-22 23:59 刷新", new DateTime(2026, 9, 22, 23, 59, 0, DateTimeKind.Local));

checks++;
if (ResetTimeParser.Parse("当前时段暂无调用", now) != null)
    failures.Add("An idle current session must not invent a reset time.");
if (!Regex.IsMatch("当前会话\n0 %\n当前时段暂无调用", QuotaRule.DefaultResetPattern, RegexOptions.IgnoreCase))
    failures.Add("The idle current-session status was not extractable from its quota row.");

checks++;
var weeklyRow = "近 1 周\n0 %\n5天11时56分钟后刷新";
var weeklyMatch = Regex.Match(weeklyRow, QuotaRule.DefaultResetPattern, RegexOptions.IgnoreCase).Value;
if (weeklyMatch != "5天11时56分钟后刷新")
    failures.Add($"Reset extraction included unrelated row text: {weeklyMatch}");

checks++;
var sameLineRow = "近 1 周 0 % 5天11时56分钟后刷新";
var sameLineMatch = Regex.Match(sameLineRow, QuotaRule.DefaultResetPattern, RegexOptions.IgnoreCase).Value;
if (sameLineMatch != "5天11时56分钟后刷新")
    failures.Add($"Same-line reset extraction included unrelated row text: {sameLineMatch}");

checks++;
if (Regex.IsMatch("Refresh failed - showing stale data", QuotaRule.DefaultResetPattern, RegexOptions.IgnoreCase))
    failures.Add("A refresh error was incorrectly recognized as a quota reset time.");

checks++;
var idleFiveHour = new RuleResult { Label = "5-hour usage", Percent = 0 };
ScrapeEngine.ApplyFiveHourIdleDetail(
    new QuotaRule { Label = "5-hour usage" }, idleFiveHour);
if (idleFiveHour.Detail != "No calls yet · the 5h timer starts on first use")
    failures.Add("An unused 5-hour quota without reset information did not show the idle timer hint.");

checks++;
var idleChineseFiveHour = new RuleResult { Label = "每5小时使用额度", Percent = 0 };
ScrapeEngine.ApplyFiveHourIdleDetail(
    new QuotaRule { Label = "每5小时使用额度" }, idleChineseFiveHour);
if (idleChineseFiveHour.Detail != "No calls yet · the 5h timer starts on first use")
    failures.Add("The Chinese 5-hour quota label did not receive the shared idle timer hint.");

checks++;
var alreadyDecoratedFiveHour = new RuleResult
{
    Label = "5-hour usage",
    Percent = 0,
    Detail = "No calls yet · the 5h timer starts on first use",
};
ScrapeEngine.ApplyFiveHourIdleDetail(
    new QuotaRule { Label = "5-hour usage" }, alreadyDecoratedFiveHour);
if (alreadyDecoratedFiveHour.Detail != "No calls yet · the 5h timer starts on first use")
    failures.Add("Applying the shared 5-hour idle hint twice duplicated its text.");

checks++;
var activeFiveHour = new RuleResult
{
    Label = "5-hour usage",
    Percent = 0,
    ResetText = "Resets in 4 hours",
    ResetAt = now.AddHours(4),
};
ScrapeEngine.ApplyFiveHourIdleDetail(
    new QuotaRule { Label = "5-hour usage" }, activeFiveHour);
if (activeFiveHour.Detail != null)
    failures.Add("The idle hint replaced an existing 5-hour reset countdown.");

checks++;
var usedFiveHour = new RuleResult { Label = "5-hour usage", Percent = 1 };
ScrapeEngine.ApplyFiveHourIdleDetail(
    new QuotaRule { Label = "5-hour usage" }, usedFiveHour);
if (usedFiveHour.Detail != null)
    failures.Add("A non-zero 5-hour quota was incorrectly described as unused.");

checks++;
var idleWeekly = new RuleResult { Label = "7-day usage", Percent = 0 };
ScrapeEngine.ApplyFiveHourIdleDetail(
    new QuotaRule { Label = "7-day usage" }, idleWeekly);
if (idleWeekly.Detail != null)
    failures.Add("The 5-hour idle hint leaked onto a non-5-hour quota.");

// 「剩余」语义自动换算：ChatGPT 2026-09 起 Usage 页显示「25% left」，需换算为已用 75%
void CheckPercent(string payload, bool invert, double expected, string note)
{
    checks++;
    var rr = new RuleResult();
    ScrapeEngine.ParseRulePayload(rr, new QuotaRule { Label = "Weekly limit", Invert = invert }, payload, out _);
    if (rr.Error != null || rr.Percent is not { } pct || Math.Abs(pct - expected) > 0.001)
        failures.Add($"Remaining-percent conversion wrong ({note}): got {rr.Error ?? rr.Percent?.ToString() ?? "null"}, expected {expected}");
}

CheckPercent("""{"ok":true,"groups":["25"],"text":"Weekly limit\nResets in 5d 20h\n25% left","mtext":"25% left","reset":"Resets in 5d 20h"}""", false, 75, "ChatGPT '25% left' must convert to 75% used");
CheckPercent("""{"ok":true,"groups":["25"],"text":"Weekly limit\n25% left","mtext":"25% left"}""", true, 75, "Explicit Invert must not double-invert a 'left' value");
CheckPercent("""{"ok":true,"groups":["42"],"text":"Weekly limit\n42% used","mtext":"42% used"}""", false, 42, "A used-style value must stay as-is");
CheckPercent("""{"ok":true,"groups":["73"],"text":"每周使用额度\n剩余 73%","mtext":"剩余 73%"}""", false, 27, "Chinese 剩余 must convert");
CheckPercent("""{"ok":true,"groups":["73"],"text":"Remaining: 73%","mtext":"Remaining: 73%"}""", false, 27, "'Remaining: 73%' must convert");
CheckPercent("""{"ok":true,"groups":["42"],"text":"Weekly limit\n42%\nCredits\n0 credits left","mtext":"42%"}""", false, 42, "A non-adjacent 'left' must not convert");
CheckPercent("""{"ok":true,"groups":["42"],"text":"Weekly limit 42% · 剩余 9% of credits","mtext":"42%"}""", false, 42, "A remaining label on a different number must not convert");
CheckPercent("""{"ok":true,"groups":["42"],"text":"Weekly limit\n42% used","mtext":"42% used"}""", true, 58, "Manual Invert must keep working without keywords");
CheckPercent("""{"ok":true,"groups":["25"],"text":"Weekly limit\nResets in 5d 20h\n25% left"}""", false, 75, "Selector-mode (no mtext) must detect '% left' in container text");
CheckPercent("""{"ok":true,"groups":["42"],"text":"Weekly usage\n3 hours left\n42%"}""", false, 42, "A reset countdown 'hours left' above the value must not convert (selector mode)");
CheckPercent("""{"ok":true,"groups":["42"],"text":"Weekly usage\n2 days remaining\n42%"}""", false, 42, "A reset countdown 'days remaining' must not convert (selector mode)");
CheckPercent("""{"ok":true,"groups":["42"],"text":"Weekly usage\n3 hours left\n42%","mtext":"42%"}""", false, 42, "Element text wins over container text when both exist");

checks++;
var fracRr = new RuleResult();
ScrapeEngine.ParseRulePayload(fracRr,
    new QuotaRule { Label = "7 天用量", Type = QuotaRule.TypeFraction, Pattern = QuotaRule.DefaultFractionPattern },
    """{"ok":true,"groups":["3","5"],"text":"3 / 5 left","mtext":"3 / 5 left"}""", out _);
if (fracRr.Percent != 60)
    failures.Add("Fraction-type quotas must not be affected by remaining-word detection.");

checks++;
if (!CookieStore.ShouldRestore(true, now.AddDays(-30), now) ||
    CookieStore.ShouldRestore(false, now.AddSeconds(-1), now) ||
    !CookieStore.ShouldRestore(false, now.AddSeconds(1), now) ||
    !CookieStore.ShouldRestore(false, now.ToUniversalTime().AddMinutes(5), now))
    failures.Add("Expired-cookie restore boundary is incorrect.");

checks++;
var persistencePath = Path.Combine(Path.GetTempPath(), "AIQuotaMonitor.RegressionTests", "cookie-roundtrip.dat");
var firstPayload = Encoding.UTF8.GetBytes("session-cookie-fixture-v1");
var secondPayload = Encoding.UTF8.GetBytes("session-cookie-fixture-v2");
CookieStore.WriteProtectedAtomically(persistencePath, firstPayload);
if (!CookieStore.ReadProtected(persistencePath).SequenceEqual(firstPayload))
    failures.Add("DPAPI create/read round trip failed.");
CookieStore.WriteProtectedAtomically(persistencePath, secondPayload);
if (!CookieStore.ReadProtected(persistencePath).SequenceEqual(secondPayload))
    failures.Add("DPAPI atomic replacement round trip failed.");

checks++;
File.WriteAllBytes(persistencePath, Encoding.UTF8.GetBytes("not-dpapi-ciphertext"));
try
{
    _ = CookieStore.ReadProtected(persistencePath);
    failures.Add("Corrupt persistence data was accepted as valid DPAPI ciphertext.");
}
catch (System.Security.Cryptography.CryptographicException)
{
    // Expected: corruption must be observable, not treated as a valid restored session.
}
finally
{
    CookieStore.WriteProtectedAtomically(persistencePath, secondPayload);
}

checks++;
if (!await MainWindow.CompletesWithinAsync(Task.CompletedTask, TimeSpan.FromMilliseconds(50)))
    failures.Add("Completed shutdown work was reported as timed out.");

checks++;
var neverCompletes = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
if (await MainWindow.CompletesWithinAsync(neverCompletes.Task, TimeSpan.FromMilliseconds(25)))
    failures.Add("Hung shutdown work bypassed the configured timeout.");

// ---------- 赠送重置次数（Codex / 智谱 GLM） ----------

// Codex：页面直列两条 Full reset，各自到期时间（英文月名 + AM/PM，无年份 → 当年）
checks++;
var codexPayload = """{"entries":[{"scope":"full reset","count":1,"expire":"Oct 4, 9:57 AM"},{"scope":"full reset","count":1,"expire":"Oct 5, 12:18 PM"}],"glm":{"count":0,"scopes":[]},"manage":false}""";
var codexResets = ScrapeEngine.ParseBonusPayload(codexPayload, now, out bool codexManage);
if (codexManage)
    failures.Add("Codex payload without a manage button was reported as having one.");
if (codexResets.Count != 2)
    failures.Add($"Codex payload should yield 2 bonus resets, got {codexResets.Count}.");
else
{
    if (codexResets[0].ExpireAt?.DateTime != new DateTime(2026, 10, 4, 9, 57, 0))
        failures.Add($"Codex reset #1 expiry parsed as {codexResets[0].ExpireAt}, expected 2026-10-04 09:57.");
    if (codexResets[1].ExpireAt?.DateTime != new DateTime(2026, 10, 5, 12, 18, 0))
        failures.Add($"Codex reset #2 expiry parsed as {codexResets[1].ExpireAt}, expected 2026-10-05 12:18 (PM must not be dropped).");
    if (codexResets[0].Count != 1 || codexResets[0].Scope != "full reset")
        failures.Add("Codex reset scope/count not preserved.");
}

// Date-only grants from the updated ChatGPT Usage page.
checks++;
var dateOnlyResets = ScrapeEngine.ParseBonusPayload(
    """{"entries":[{"scope":"full reset","count":1,"expire":"October 5"},{"scope":"full reset","count":1,"expire":"October 23"},{"scope":"full reset","count":1,"expire":"October 30"}]}""",
    new DateTime(2026, 10, 5, 22, 0, 0, DateTimeKind.Local), out _);
var expectedDates = new[] { 5, 23, 30 }.Select(day => new DateTime(2026, 10, day).AddDays(1).AddTicks(-1)).ToArray();
if (dateOnlyResets.Count != 3 || !dateOnlyResets.Select(r => r.ExpireAt?.LocalDateTime).SequenceEqual(expectedDates.Select(d => (DateTime?)d)))
    failures.Add("Date-only grants must preserve all three dates and remain available on the expiry day without rolling into next year.");

checks++;
if (ServiceCard.FormatBonusExpiry(new DateTimeOffset(new DateTime(2026, 10, 5)), "October 5",
    new AppConfig { UnifiedDateFormat = true, DateFormat = "yyyy-MM-dd HH:mm" }) != "October 5")
    failures.Add("Date-only bonus expiry must not invent a visible time when unified formatting is enabled.");

checks++;
if (ScrapeEngine.GetSubscriptionUrl(new ServiceConfig { Url = "https://chatgpt.com/settings/usage?tab=overview" }) != "https://chatgpt.com/settings/billing" ||
    ScrapeEngine.GetSubscriptionUrl(new ServiceConfig { Url = "https://chatgpt.com/codex/settings/usage/" }) != "https://chatgpt.com/settings/billing")
    failures.Add("ChatGPT usage pages without an override must automatically scan the separate Billing page.");

checks++;
if (!ScrapeEngine.IsChatGptUsage(new ServiceConfig { Url = "https://chatgpt.com/#settings/Usage" }) ||
    !ScrapeEngine.IsChatGptUsage(new ServiceConfig { Url = "https://chatgpt.com/#settings/Usage?tab=overview" }) ||
    !ScrapeEngine.IsChatGptUsage(new ServiceConfig { Url = "https://chatgpt.com/Settings/Usage" }) ||
    ScrapeEngine.GetSubscriptionUrl(new ServiceConfig { Url = "https://chatgpt.com/#settings/Usage" }) != "https://chatgpt.com/settings/billing" ||
    ScrapeEngine.IsChatGptUsage(new ServiceConfig { Url = "https://chatgpt.com/#settings/Billing" }) ||
    ScrapeEngine.IsChatGptUsage(new ServiceConfig { Url = "https://chatgpt.com/example/#settings/Usage" }) ||
    ScrapeEngine.IsChatGptUsage(new ServiceConfig { Url = "https://chatgpt.com.example.com/#settings/Usage" }))
    failures.Add("Legacy ChatGPT hash usage URLs must enable Credits/Billing scans without matching unrelated hosts or pages.");

checks++;
const string customSubscriptionUrl = "https://example.com/plan";
if (ScrapeEngine.GetSubscriptionUrl(new ServiceConfig { Url = "https://chatgpt.com/settings/usage", SubscriptionUrl = customSubscriptionUrl }) != customSubscriptionUrl ||
    ScrapeEngine.GetSubscriptionUrl(new ServiceConfig { Url = "https://chatgpt.com.example.com/settings/usage" }) != null ||
    ScrapeEngine.GetSubscriptionUrl(new ServiceConfig { Url = "https://example.com/settings/usage" }) != null ||
    ScrapeEngine.GetSubscriptionUrl(new ServiceConfig { Url = "invalid-url" }) != null)
    failures.Add("Automatic Billing navigation must preserve explicit overrides and apply only to the exact ChatGPT host.");

// 智谱 GLM：页面直显「1次 未使用」「周额度 1次」，无到期时间，有「重置管理」按钮
checks++;
var glmPayload = """{"entries":[],"glm":{"count":1,"scopes":[{"scope":"周额度","count":1}]},"manage":true}""";
var glmResets = ScrapeEngine.ParseBonusPayload(glmPayload, now, out bool glmManage);
if (!glmManage)
    failures.Add("GLM payload with a manage button was not detected.");
if (glmResets.Count != 1 || glmResets[0].Scope != "周额度" || glmResets[0].Count != 1 || glmResets[0].ExpireAt != null)
    failures.Add("GLM page payload should yield one 周额度 ×1 entry without expiry.");

// GLM 弹层：有效期至合并回页面条目（按范围标签匹配）
checks++;
var glmDialogPayload = """{"entries":[{"scope":"周额度","count":1,"expire":"2026-10-01 23:59:59"}]}""";
var glmDialog = ScrapeEngine.ParseBonusPayload(glmDialogPayload, now, out _);
var glmMerged = ScrapeEngine.MergeBonusResets(glmResets, glmDialog);
if (glmMerged.Count != 1 || glmMerged[0].ExpireAt?.DateTime != new DateTime(2026, 10, 1, 23, 59, 0))
    failures.Add($"GLM dialog expiry was not merged into the page entry: count={glmMerged.Count}, expiry={glmMerged.FirstOrDefault()?.ExpireAt}.");

// GLM 次数用完：「0次 未使用」不应产生任何条目
checks++;
var glmEmpty = ScrapeEngine.ParseBonusPayload(
    """{"entries":[],"glm":{"count":0,"scopes":[{"scope":"周额度","count":0}]},"manage":true}""", now, out _);
if (glmEmpty.Count != 0)
    failures.Add("A fully-used GLM reset quota must not produce bonus reset entries.");

// 损坏/空 payload 不抛异常、不产生条目
checks++;
if (ScrapeEngine.ParseBonusPayload("", now, out _).Count != 0 ||
    ScrapeEngine.ParseBonusPayload("not-json", now, out _).Count != 0)
    failures.Add("Malformed bonus-reset payloads must yield no entries.");

// Codex Credits：余额与自动充值开关解析、服务级上限配置与失败快照。
checks++;
var creditsNow = DateTimeOffset.Now;
var credits = ScrapeEngine.ParseCreditsPayload("""{"balance":"50,167","auto":"off"}""", creditsNow);
if (credits?.Balance != 50167 || credits.AutoReload != false || credits.FetchedAt != creditsNow)
    failures.Add("Standard Codex credits balance/auto-reload payload was not parsed correctly.");

checks++;
var creditsOn = ScrapeEngine.ParseCreditsPayload("""{"balance":"12500","auto":"on"}""", creditsNow);
var creditsZh = ScrapeEngine.ParseCreditsPayload("""{"balance":"12,345","auto":""}""", creditsNow);
if (creditsOn?.AutoReload != true || creditsZh?.Balance != 12345 || creditsZh.AutoReload != null ||
    ScrapeEngine.ParseCreditsPayload("not-json", creditsNow) != null ||
    ScrapeEngine.ParseCreditsPayload("""{"balance":"not-a-number","auto":"off"}""", creditsNow) != null)
    failures.Add("Credits parsing must support on/unknown switch states and reject malformed balances.");

checks++;
var creditHistoryEntries = ScrapeEngine.ParseCreditHistoryPayload(
    """{"history":["Credit usage history\nNov 11, 2026\nCodex\n107 credits\nNov 11, 2026\n23 credits\nNov 10, 2026\n0 credits"]}""", creditsNow);
if (creditHistoryEntries.Count != 2 ||
    creditHistoryEntries.Sum(e => e.Credits) != 130 ||
    creditHistoryEntries.Any(e => e.Day != new DateTime(2026, 11, 11)))
    failures.Add("Credit history parsing must keep dated positive usage entries and ignore zero-usage days.");

checks++;
const string accountAFirstPage = "Credit usage history\nNov 13, 2026\nDesktop (Work)\n57 credits\nNov 13, 2026\nDesktop (Work)\n178 credits\nNov 13, 2026\nDesktop (Work)\n90 credits\nNov 13, 2026\nWeb\n22 credits\nNov 12, 2026\nCLI\n9 credits";
var duplicateCreditHistory = ScrapeEngine.ParseCreditHistoryPayload(
    $$"""{"history":[{{System.Text.Json.JsonSerializer.Serialize(accountAFirstPage)}},{{System.Text.Json.JsonSerializer.Serialize(accountAFirstPage)}},{{System.Text.Json.JsonSerializer.Serialize(accountAFirstPage)}}]}""", creditsNow);
var duplicateCreditDays = CreditHistoryStore.AggregateDaily(duplicateCreditHistory);
if (duplicateCreditHistory.Count != 5 || duplicateCreditDays.Count != 2 ||
    duplicateCreditDays.Single(d => d.Day == new DateTime(2026, 11, 13)).Credits != 347 ||
    duplicateCreditDays.Single(d => d.Day == new DateTime(2026, 11, 12)).Credits != 9)
    failures.Add("Repeated DOM captures of the same Credit history page must not multiply daily usage.");

checks++;
string creditHistoryPath = Path.Combine(Path.GetTempPath(), "AIQuotaMonitor.RegressionTests", "credit-history.json");
var existingCreditEntry = new CreditUsageEntry { Key = "old", Day = new DateTime(2026, 11, 10), Credits = 50 };
CreditHistoryStore.MergeAt(creditHistoryPath, "codex-a", new[] { existingCreditEntry });
CreditHistoryStore.MergeAt(creditHistoryPath, "codex-a", new[]
{
    existingCreditEntry,
    new CreditUsageEntry { Key = "new", Day = new DateTime(2026, 11, 11), Credits = 107 },
});
var persistedCreditEntries = CreditHistoryStore.QueryAt(creditHistoryPath, "codex-a");
var persistedCreditDays = CreditHistoryStore.AggregateDaily(persistedCreditEntries);
if (persistedCreditEntries.Count != 2 || persistedCreditDays.Count != 2 ||
    persistedCreditDays[0].Day != new DateTime(2026, 11, 10) || persistedCreditDays[0].Credits != 50 ||
    persistedCreditDays[1].Day != new DateTime(2026, 11, 11) || persistedCreditDays[1].Credits != 107)
    failures.Add("Credit history must merge only new entries, persist them, and aggregate usage by day.");

checks++;
var legacyTriplicatedEntries = duplicateCreditHistory.SelectMany(entry => Enumerable.Range(0, 3).Select(occurrence =>
    new CreditUsageEntry
    {
        Key = $"{entry.Key[..entry.Key.LastIndexOf('|')]}|{occurrence}",
        Day = entry.Day,
        Credits = entry.Credits,
        Description = entry.Description,
    })).ToList();
CreditHistoryStore.MergeAt(creditHistoryPath, "codex-legacy", legacyTriplicatedEntries);
CreditHistoryStore.ReplaceAt(creditHistoryPath, "codex-legacy", duplicateCreditHistory);
var repairedCreditEntries = CreditHistoryStore.QueryAt(creditHistoryPath, "codex-legacy");
var repairedCreditDays = CreditHistoryStore.AggregateDaily(repairedCreditEntries);
if (repairedCreditEntries.Count != 5 ||
    repairedCreditDays.Single(d => d.Day == new DateTime(2026, 11, 13)).Credits != 347 ||
    repairedCreditDays.Single(d => d.Day == new DateTime(2026, 11, 12)).Credits != 9)
    failures.Add("A complete Credit history snapshot must replace legacy triplicated entries instead of preserving them.");

checks++;
string[] accountARemainingPages =
[
    "Credit usage history\nNov 12, 2026\nDesktop (Work)\n26 credits\nNov 12, 2026\nDesktop (Work)\n183 credits\nNov 12, 2026\nDesktop (Work)\n888 credits\nNov 12, 2026\nWeb\n33 credits",
    "Credit usage history\nNov 11, 2026\nCLI\n27 credits\nNov 11, 2026\nCLI\n1 credit\nNov 11, 2026\nCLI\n1 credit\nNov 11, 2026\nCLI\n282 credits\nNov 11, 2026\nCLI\n1 credit\nNov 11, 2026\nCLI\n1 credit\nNov 11, 2026\nCLI\n1 credit\nNov 11, 2026\nCLI\n1 credit\nNov 11, 2026\nDesktop (Work)\n14 credits",
    "Credit usage history\nNov 11, 2026\nDesktop (Work)\n107 credits\nNov 11, 2026\nDesktop (Work)\n1,567 credits\nNov 11, 2026\nWeb\n19 credits\nNov 10, 2026\nCLI\n8 credits\nNov 10, 2026\nCLI\n3 credits\nNov 10, 2026\nDesktop (Work)\n260 credits\nNov 10, 2026\nDesktop (Work)\n418 credits",
    "Credit usage history\nNov 6, 2026\nCLI\n6,367 credits\nNov 6, 2026\nCLI\n1 credit\nNov 6, 2026\nDesktop (Work)\n110 credits\nNov 6, 2026\nDesktop (Work)\n39 credits",
    "Credit usage history\nOct 21, 2026\nCLI\n4,733 credits\nOct 21, 2026\nCLI\n17 credits\nOct 21, 2026\nCLI\n5 credits\nOct 21, 2026\nDesktop (Work)\n137 credits\nOct 21, 2026\nDesktop (Work)\n93 credits",
];
var fullAccountAHistory = ScrapeEngine.ParseCreditHistoryPagePayloads(
    accountARemainingPages.Prepend(accountAFirstPage).Select(page =>
        $$"""{"history":[{{System.Text.Json.JsonSerializer.Serialize(page)}},{{System.Text.Json.JsonSerializer.Serialize(page)}},{{System.Text.Json.JsonSerializer.Serialize(page)}}]}"""), creditsNow);
var fullAccountADays = CreditHistoryStore.AggregateDaily(fullAccountAHistory);
var expectedAccountADays = new Dictionary<DateTime, long>
{
    [new DateTime(2026, 11, 13)] = 347,
    [new DateTime(2026, 11, 12)] = 1139,
    [new DateTime(2026, 11, 11)] = 2022,
    [new DateTime(2026, 11, 10)] = 689,
    [new DateTime(2026, 11, 6)] = 6517,
    [new DateTime(2026, 10, 21)] = 4985,
};
if (fullAccountAHistory.Count != 34 || fullAccountADays.Count != expectedAccountADays.Count ||
    expectedAccountADays.Any(expected => fullAccountADays.Single(d => d.Day == expected.Key).Credits != expected.Value))
    failures.Add("All paginated screenshot events, including identical 1-credit events, must produce the correct daily totals.");

checks++;
var defaultCreditsConfig = new ServiceConfig();
var customCreditsConfig = ConfigStore.Clone(new ServiceConfig { CreditsLimit = 75000 });
if (defaultCreditsConfig.CreditsLimit != 62500 || customCreditsConfig.CreditsLimit != 75000 ||
    new ServiceConfig { CreditsLimit = 0 }.CreditsLimit != 1)
    failures.Add("Credits limit must default to 62500, survive config cloning, and reject non-positive values.");

checks++;
string creditsConfigJson = System.Text.Json.JsonSerializer.Serialize(customCreditsConfig,
    new System.Text.Json.JsonSerializerOptions { PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase });
if (!creditsConfigJson.Contains("\"creditsLimit\":75000"))
    failures.Add("Custom Credits limit must serialize as camelCase creditsLimit.");

checks++;
var previousResult = new ServiceScrapeResult { Credits = new CreditBalanceInfo { Balance = 50167, AutoReload = false } };
var missingCredits = new ServiceScrapeResult();
MainWindow.MergeCredits(previousResult, missingCredits);
if (!ReferenceEquals(previousResult.Credits, missingCredits.Credits))
    failures.Add("A best-effort credits scan failure should retain the previous balance snapshot.");

// 多账户：独立会话开关随配置序列化（camelCase），编辑副本不丢
checks++;
var isolatedSvc = ConfigStore.Clone(new ServiceConfig { Name = "Codex Maja", IsolatedSession = true });
if (!isolatedSvc.IsolatedSession)
    failures.Add("IsolatedSession flag was lost in config clone round-trip.");
if (!System.Text.Json.JsonSerializer.Serialize(new ServiceConfig { IsolatedSession = true },
        new System.Text.Json.JsonSerializerOptions { PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase })
        .Contains("\"isolatedSession\":true"))
    failures.Add("IsolatedSession must serialize as camelCase isolatedSession.");

if (failures.Count > 0)
{
    Console.Error.WriteLine($"FAIL {failures.Count}/{checks}");
    foreach (var failure in failures) Console.Error.WriteLine(failure);
    return 1;
}

Console.WriteLine($"PASS {checks}/{checks}");
return 0;

void CheckReset(string text, DateTime expected)
{
    checks++;
    if (!Regex.IsMatch(text, QuotaRule.DefaultResetPattern, RegexOptions.IgnoreCase))
    {
        failures.Add($"Default reset extraction pattern did not match: {text}");
        return;
    }

    var actual = ResetTimeParser.Parse(text, now);
    if (actual != expected)
        failures.Add($"Reset parser returned {actual:O}; expected {expected:O} for: {text}");
}
