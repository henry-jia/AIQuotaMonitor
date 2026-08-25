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
