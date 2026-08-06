using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AIQuotaMonitor;

/// <summary>用量历史的一条样本（history.jsonl 一行）：某服务某配额在某时刻的已用百分比。</summary>
public class HistorySample
{
    public DateTimeOffset T { get; set; }
    /// <summary>服务稳定 id（ServiceConfig.Id）。</summary>
    public string Svc { get; set; } = "";
    /// <summary>规则标签（显示名；序列关联优先用 RuleId）。</summary>
    public string Rule { get; set; } = "";
    /// <summary>规则稳定 id（QuotaRule.Id）；旧样本可空，回退按标签关联。</summary>
    public string? RuleId { get; set; }
    /// <summary>已用百分比 0–100（invert 规则为换算后的显示值）。</summary>
    public double Pct { get; set; }
    public string? Detail { get; set; }
    public DateTimeOffset? ResetAt { get; set; }
}

/// <summary>
/// 用量历史的本地存储（%LOCALAPPDATA%\AIQuotaMonitor\history.jsonl，append-only）。
/// 保留 30 天；超过 7 天的样本按（服务, 规则, 小时）桶稀释为每小时一条。仅记录百分比，存本机不上传。
/// 内存列表为唯一真源，全部 IO 加锁且静默容错（同 ConfigStore/CookieStore 风格）。
/// </summary>
public static class HistoryStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private static readonly object Gate = new();
    private static List<HistorySample>? _samples;
    private static DateTime _lastDecimate = DateTime.MinValue;

    public static string StorePath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "AIQuotaMonitor", "history.jsonl");

    /// <summary>幂等懒加载：读入 + 稀释（有变化才重写文件）。</summary>
    public static void Load()
    {
        lock (Gate)
        {
            if (_samples != null) return;
            var list = new List<HistorySample>();
            try
            {
                if (File.Exists(StorePath))
                {
                    foreach (var line in File.ReadLines(StorePath))
                    {
                        if (string.IsNullOrWhiteSpace(line)) continue;
                        try
                        {
                            var s = JsonSerializer.Deserialize<HistorySample>(line, Options);
                            if (s != null) list.Add(s);
                        }
                        catch
                        {
                            // 单行损坏跳过
                        }
                    }
                }
            }
            catch
            {
                // 文件不可读按空历史处理
            }
            list.Sort((a, b) => a.T.CompareTo(b.T));
            var decimated = Decimate(list, DateTimeOffset.Now);
            _samples = decimated;
            if (decimated.Count != list.Count) Rewrite(decimated);
            _lastDecimate = DateTime.Now;
        }
    }

    /// <summary>某服务的样本快照（按时间升序），供历史窗口绘图。</summary>
    public static IReadOnlyList<HistorySample> Query(string serviceId)
    {
        Load();
        lock (Gate)
            return _samples!.Where(s => s.Svc == serviceId).OrderBy(s => s.T).ToList();
    }

    /// <summary>追加样本（内存 + 文件）；每小时 opportunistic 触发一次稀释重写（后台线程）。</summary>
    public static void Append(IReadOnlyList<HistorySample> fresh)
    {
        if (fresh.Count == 0) return;
        Load();
        List<HistorySample>? toRewrite = null;
        lock (Gate)
        {
            _samples!.AddRange(fresh);
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(StorePath)!);
                File.AppendAllLines(StorePath, fresh.Select(s => JsonSerializer.Serialize(s, Options)));
            }
            catch
            {
                // 写失败静默，不影响主流程
            }
            if (DateTime.Now - _lastDecimate >= TimeSpan.FromHours(1))
            {
                _lastDecimate = DateTime.Now;
                var decimated = Decimate(_samples, DateTimeOffset.Now);
                if (decimated.Count != _samples.Count)
                {
                    _samples = decimated;
                    toRewrite = decimated;
                }
            }
        }
        if (toRewrite != null)
        {
            var list = toRewrite;
            System.Threading.Tasks.Task.Run(() => { lock (Gate) Rewrite(list); })
                .ContinueWith(t => { _ = t.Exception; }, System.Threading.Tasks.TaskContinuationOptions.OnlyOnFaulted);
        }
    }

    /// <summary>全量重写（temp + Replace）。调用方须持有 Gate。</summary>
    private static void Rewrite(List<HistorySample> samples)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(StorePath)!);
            string tmp = StorePath + ".tmp";
            File.WriteAllLines(tmp, samples.Select(s => JsonSerializer.Serialize(s, Options)));
            if (File.Exists(StorePath)) File.Replace(tmp, StorePath, null);
            else File.Move(tmp, StorePath);
        }
        catch
        {
            // 重写失败静默，下次稀释再试
        }
    }

    /// <summary>保留稀释（纯函数，输入须按 T 升序）：&gt;30 天丢弃；&gt;7 天按（服务, 规则, 小时）桶留最早；近 7 天原样。</summary>
    internal static List<HistorySample> Decimate(List<HistorySample> samples, DateTimeOffset now)
    {
        var result = new List<HistorySample>(samples.Count);
        var seen = new HashSet<string>();
        foreach (var s in samples)
        {
            var age = now - s.T;
            if (age > TimeSpan.FromDays(30)) continue;
            if (age > TimeSpan.FromDays(7))
            {
                string bucket = $"{s.Svc}|{s.RuleId ?? s.Rule}|{s.T.LocalDateTime:yyyyMMddHH}";
                if (!seen.Add(bucket)) continue;
            }
            result.Add(s);
        }
        return result;
    }
}
