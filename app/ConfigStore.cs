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
            // 配置损坏：原档搬到 .corrupt-<时间戳> 供用户恢复，再回退默认配置
            try
            {
                if (File.Exists(ConfigPath))
                    File.Move(ConfigPath, ConfigPath + ".corrupt-" + DateTime.Now.ToString("yyyyMMddHHmmss"), true);
            }
            catch
            {
                // 备份尽力而为，失败仍回退默认
            }
        }
        var def = CreateDefault();
        Save(def);
        return def;
    }

    /// <summary>原子写入（temp + Replace），崩溃/断电不会截断 config.json。返回是否成功，
    /// 用户主动保存的调用方应提示失败；后台自动保存可忽略。</summary>
    public static bool Save(AppConfig config)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(ConfigPath)!);
            string tmp = ConfigPath + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(config, Options));
            if (File.Exists(ConfigPath)) File.Replace(tmp, ConfigPath, null);
            else File.Move(tmp, ConfigPath);
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>通过 JSON 深拷贝（设置界面编辑副本 / 测试抓取快照）。</summary>
    public static T Clone<T>(T obj) =>
        JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(obj, Options), Options)!;

    /// <summary>配置的序列化指纹，用于判断保存前后某个服务是否有变化。</summary>
    public static string Fingerprint<T>(T obj) => JsonSerializer.Serialize(obj, Options);

    /// <summary>默认配置：含一个未启用的示例服务（名称/标签/重置正则随界面语言）。</summary>
    public static AppConfig CreateDefault()
    {
        bool zh = I18n.Current == I18n.LangZh;
        return new AppConfig
        {
            Services = new List<ServiceConfig>
            {
                new ServiceConfig
                {
                    Id = Guid.NewGuid().ToString("N"),
                    Name = zh ? "示例服务（设置中启用）" : "Sample service (enable in Settings)",
                    Enabled = false,
                    Url = "https://example.com/usage",
                    Rules = new List<QuotaRule>
                    {
                        new QuotaRule
                        {
                            Id = Guid.NewGuid().ToString("N"),
                            Label = zh ? "5 小时用量" : "5-hour usage",
                            Type = QuotaRule.TypePercent,
                        },
                        new QuotaRule
                        {
                            Id = Guid.NewGuid().ToString("N"),
                            Label = zh ? "7 天用量" : "7-day usage",
                            Type = QuotaRule.TypeFraction,
                            Pattern = QuotaRule.DefaultFractionPattern,
                            // 留空走自动识别，中英文重置文本都能认
                            ResetPattern = zh ? @"(\d+\s*天后重置)" : null,
                        },
                    },
                },
            },
        };
    }
}
