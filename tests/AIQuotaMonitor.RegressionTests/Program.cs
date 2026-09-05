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

CheckReset("5天11时56分钟后刷新", now.AddDays(5).AddHours(11).AddMinutes(56));
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
