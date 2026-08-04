using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AIQuotaMonitor;

/// <summary>
/// 每个服务最后一次成功抓取结果的持久化（%LOCALAPPDATA%\AIQuotaMonitor\lastgood.json，key = 服务稳定 id）。
/// 启动时恢复为「上次会话数据」陈旧视图，刷新失败时作为回退数据源，避免卡片整张变错误红字。
/// </summary>
public static class LastGoodStore
{
    // ServiceScrapeResult / RuleResult / SubscriptionInfo 均为 public 字段，必须 IncludeFields 才能序列化
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        IncludeFields = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public static string StorePath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "AIQuotaMonitor", "lastgood.json");

    /// <summary>读取上次成功结果（key = 服务 id）。文件缺失/损坏返回空字典，不影响启动。</summary>
    public static Dictionary<string, ServiceScrapeResult> Load()
    {
        try
        {
            if (File.Exists(StorePath))
            {
                var map = JsonSerializer.Deserialize<Dictionary<string, ServiceScrapeResult>>(
                    File.ReadAllText(StorePath), Options);
                if (map != null) return map;
            }
        }
        catch
        {
            // 损坏时回退空结果，不影响主流程
        }
        return new Dictionary<string, ServiceScrapeResult>();
    }

    /// <summary>原子写入（temp + Replace）。写前对副本剥离调试用 RawText，控制文件体积。</summary>
    public static void Save(IReadOnlyDictionary<string, ServiceScrapeResult> map)
    {
        try
        {
            // 深拷贝后剥离 RawText，避免改动内存中正在展示的结果
            var snapshot = new Dictionary<string, ServiceScrapeResult>(map.Count);
            foreach (var kv in map)
            {
                var copy = JsonSerializer.Deserialize<ServiceScrapeResult>(
                    JsonSerializer.Serialize(kv.Value, Options), Options)!;
                foreach (var r in copy.Rules) r.RawText = null;
                snapshot[kv.Key] = copy;
            }
            Directory.CreateDirectory(Path.GetDirectoryName(StorePath)!);
            string tmp = StorePath + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(snapshot, Options));
            if (File.Exists(StorePath)) File.Replace(tmp, StorePath, null);
            else File.Move(tmp, StorePath);
        }
        catch
        {
            // 目录只读等场景下静默失败，不影响主流程
        }
    }
}
