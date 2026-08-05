using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AIQuotaMonitor;

/// <summary>配置读写：exe 同目录 config.json，camelCase，缺省值齐全。</summary>
public static class ConfigStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static string ConfigPath =>
        Path.Combine(Path.GetDirectoryName(Environment.ProcessPath) ?? AppContext.BaseDirectory, "config.json");

    public static AppConfig Load()
    {
        try
        {
            if (File.Exists(ConfigPath))
            {
                var cfg = JsonSerializer.Deserialize<AppConfig>(File.ReadAllText(ConfigPath), Options);
                if (cfg != null) return cfg;
            }
        }
        catch
        {
            // 配置损坏时回退到默认配置，避免程序无法启动
        }
        var def = CreateDefault();
        Save(def);
        return def;
    }

    public static void Save(AppConfig config)
    {
        try
        {
            File.WriteAllText(ConfigPath, JsonSerializer.Serialize(config, Options));
        }
        catch
        {
            // 目录只读等场景下静默失败，不影响主流程
        }
    }

    /// <summary>通过 JSON 深拷贝（设置界面编辑副本 / 测试抓取快照）。</summary>
    public static T Clone<T>(T obj) =>
        JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(obj, Options), Options)!;

    /// <summary>配置的序列化指纹，用于判断保存前后某个服务是否有变化。</summary>
    public static string Fingerprint<T>(T obj) => JsonSerializer.Serialize(obj, Options);

    /// <summary>默认配置：含一个未启用的示例服务。</summary>
    public static AppConfig CreateDefault() => new()
    {
        Services = new List<ServiceConfig>
        {
            new ServiceConfig
            {
                Id = Guid.NewGuid().ToString("N"),
                Name = "示例服务（设置中启用）",
                Enabled = false,
                Color = "#4F8CFF",
                Url = "https://example.com/usage",
                Rules = new List<QuotaRule>
                {
                    new QuotaRule { Id = Guid.NewGuid().ToString("N"), Label = "5 小时用量", Type = QuotaRule.TypePercent },
                    new QuotaRule
                    {
                        Id = Guid.NewGuid().ToString("N"),
                        Label = "7 天用量",
                        Type = QuotaRule.TypeFraction,
                        Pattern = QuotaRule.DefaultFractionPattern,
                        ResetPattern = @"(\d+\s*天后重置)",
                    },
                },
            },
        },
    };
}
