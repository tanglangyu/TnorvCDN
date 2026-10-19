using System.Collections.Concurrent;

namespace TnorvCdn.Api.Services;

/// <summary>
/// 统计聚合短缓存（P5 性能项）：AccessLogs 按 (serverIds指纹, days) 聚合查询代价高，
/// 高频仪表盘/用量页直接打表。缓存 5 分钟内相同条件的聚合结果，过期后重算。
/// 值为序列化后的 JSON 字符串，避免并发请求重复聚合（simpleflight 语义）。
/// </summary>
public static class StatsCache
{
    private sealed record Entry(string Json, DateTime At);
    private static readonly ConcurrentDictionary<string, Entry> _cache = new();
    private static readonly TimeSpan Ttl = TimeSpan.FromMinutes(5);

    /// <summary>取缓存（未命中返回 null）。key 需已包含用户/条件维度。</summary>
    public static string? Get(string key)
    {
        if (_cache.TryGetValue(key, out var e) && DateTime.UtcNow - e.At < Ttl)
            return e.Json;
        if (e != null) _cache.TryRemove(key, out _);
        return null;
    }

    /// <summary>写缓存。race 时保留先到者即可（5 分钟窗口内结果一致）。</summary>
    public static void Set(string key, string json)
    {
        _cache[key] = new Entry(json, DateTime.UtcNow);
        // 粗粒度防膨胀：超过 512 条清理过期项
        if (_cache.Count > 512)
        {
            var now = DateTime.UtcNow;
            foreach (var (k, v) in _cache)
                if (now - v.At >= Ttl) _cache.TryRemove(k, out _);
        }
    }
}
