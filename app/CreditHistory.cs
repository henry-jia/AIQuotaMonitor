using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AIQuotaMonitor;

public class CreditUsageEntry
{
    public string Key { get; set; } = "";
    public DateTime Day { get; set; }
    public long Credits { get; set; }
    public string? Description { get; set; }
}

public class CreditDailyUsage
{
    public DateTime Day { get; set; }
    public long Credits { get; set; }
}

public static class CreditHistoryStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private static readonly object Gate = new();

    public static string StorePath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "AIQuotaMonitor", "credit-history.json");

    public static void Merge(string serviceId, IReadOnlyList<CreditUsageEntry> fresh)
    {
        lock (Gate) MergeAt(StorePath, serviceId, fresh);
    }

    public static void Replace(string serviceId, IReadOnlyList<CreditUsageEntry> fresh)
    {
        lock (Gate) ReplaceAt(StorePath, serviceId, fresh);
    }

    internal static void ReplaceAt(string path, string serviceId, IReadOnlyList<CreditUsageEntry> fresh)
    {
        var all = Read(path);
        var keys = new HashSet<string>(StringComparer.Ordinal);
        all[serviceId] = fresh.Where(e => !string.IsNullOrWhiteSpace(e.Key) && keys.Add(e.Key)).ToList();
        Write(path, all);
    }

    public static IReadOnlyList<CreditUsageEntry> Query(string serviceId)
    {
        lock (Gate) return QueryAt(StorePath, serviceId);
    }

    public static List<CreditDailyUsage> QueryDaily(string serviceId) => AggregateDaily(Query(serviceId));

    internal static void MergeAt(string path, string serviceId, IReadOnlyList<CreditUsageEntry> fresh)
    {
        if (fresh.Count == 0) return;
        var all = Read(path);
        if (!all.TryGetValue(serviceId, out var existing))
            all[serviceId] = existing = new List<CreditUsageEntry>();
        var keys = existing.Select(e => e.Key).ToHashSet(StringComparer.Ordinal);
        var additions = fresh.Where(e => !string.IsNullOrWhiteSpace(e.Key) && keys.Add(e.Key)).ToList();
        if (additions.Count == 0) return;
        existing.AddRange(additions);
        Write(path, all);
    }

    internal static IReadOnlyList<CreditUsageEntry> QueryAt(string path, string serviceId) =>
        Read(path).TryGetValue(serviceId, out var entries)
            ? entries.OrderBy(e => e.Day).ThenBy(e => e.Key, StringComparer.Ordinal).ToList()
            : new List<CreditUsageEntry>();

    internal static List<CreditDailyUsage> AggregateDaily(IEnumerable<CreditUsageEntry> entries) =>
        entries.Where(e => e.Credits > 0)
            .GroupBy(e => e.Day.Date)
            .Select(g => new CreditDailyUsage { Day = g.Key, Credits = g.Sum(e => e.Credits) })
            .OrderBy(d => d.Day)
            .ToList();

    private static Dictionary<string, List<CreditUsageEntry>> Read(string path)
    {
        try
        {
            if (File.Exists(path))
                return JsonSerializer.Deserialize<Dictionary<string, List<CreditUsageEntry>>>(
                    File.ReadAllText(path), Options) ?? new Dictionary<string, List<CreditUsageEntry>>();
        }
        catch
        {
        }
        return new Dictionary<string, List<CreditUsageEntry>>();
    }

    private static void Write(string path, Dictionary<string, List<CreditUsageEntry>> entries)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            string tmp = path + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(entries, Options));
            if (File.Exists(path)) File.Replace(tmp, path, null);
            else File.Move(tmp, path);
        }
        catch
        {
        }
    }
}
