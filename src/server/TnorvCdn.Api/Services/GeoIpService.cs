using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using IP2Region.Net.Abstractions;
using IP2Region.Net.XDB;

namespace TnorvCdn.Api.Services;

/// <summary>IP → 国家/省份 解析：本地 ip2region xdb 离线库（Content 全量缓存，微秒级）为主，
/// ip-api.com 批量端点为兜底（xdb 未命中时）。解析结果本地缓存永久有效。
/// 用于数据看板的地区分布排行。私有/保留地址直接标"内网"。</summary>
public class GeoIpService(HttpClient http, ILogger<GeoIpService> logger)
{
    private readonly ConcurrentDictionary<string, (string Country, string Province)> _cache = new();
    private readonly object _lock = new();
    private ISearcher? _searcher;

    /// <summary>xdb 数据文件路径（环境变量 Tnorv__GeoIp__XdbPath 可覆盖）。</summary>
    public static string XdbPath { get; } =
        Environment.GetEnvironmentVariable("Tnorv__GeoIp__XdbPath")
        ?? Path.Combine(AppContext.BaseDirectory, "geoip", "ip2region.xdb");

    private static readonly string[] XdbSources =
    [
        "https://cdn.jsdelivr.net/gh/lionsoul2014/ip2region@master/data/ip2region.xdb",
        "https://raw.githubusercontent.com/lionsoul2014/ip2region/master/data/ip2region.xdb",
    ];

    /// <summary>加载 xdb（已加载则跳过）；文件缺失时尝试自动下载后加载。返回是否可用。</summary>
    public async Task<bool> EnsureLoadedAsync(CancellationToken ct = default)
    {
        if (_searcher != null) return true;
        lock (_lock)
        {
            if (_searcher != null) return true;
            if (File.Exists(XdbPath))
            {
                try
                {
                    _searcher = new Searcher(CachePolicy.Content, XdbPath);
                    logger.LogInformation("GeoIP xdb 已加载：{Path}", XdbPath);
                    return true;
                }
                catch (Exception ex) { logger.LogWarning(ex, "GeoIP xdb 加载失败：{Path}", XdbPath); }
            }
        }
        // 文件缺失/损坏 → 下载
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(XdbPath)!);
            foreach (var url in XdbSources)
            {
                try
                {
                    var bytes = await http.GetByteArrayAsync(url, ct);
                    if (bytes.Length < 1024 * 100) continue; // 异常小文件拒收
                    await File.WriteAllBytesAsync(XdbPath, bytes, ct);
                    logger.LogInformation("GeoIP xdb 下载完成（{Size} KB）：{Url}", bytes.Length / 1024, url);
                    lock (_lock)
                    {
                        _searcher = new Searcher(CachePolicy.Content, XdbPath);
                    }
                    return true;
                }
                catch (Exception ex) { logger.LogWarning(ex, "GeoIP xdb 下载失败：{Url}", url); }
            }
        }
        catch (Exception ex) { logger.LogWarning(ex, "GeoIP xdb 下载初始化异常"); }
        return false;
    }

    /// <summary>同步解析（xdb 命中即时返回；未加载/未命中标"未知"，由后台服务兜底批量解析）。</summary>
    public (string Country, string Province) Resolve(string ip)
    {
        if (string.IsNullOrEmpty(ip)) return ("未知", "");
        if (ip.StartsWith("10.") || ip.StartsWith("192.168.") || ip.StartsWith("172.16.") || ip.StartsWith("127.") || ip.StartsWith("::1") || ip.StartsWith("fc") || ip.StartsWith("fe80"))
            return ("内网", "");
        if (_cache.TryGetValue(ip, out var v)) return v;
        var searcher = _searcher;
        if (searcher == null) return ("未知", "");
        try
        {
            var raw = searcher.Search(ip);
            var (country, province) = Parse(raw);
            _cache[ip] = (country, province);
            return (country, province);
        }
        catch { return ("未知", ""); }
    }

    /// <summary>xdb 结果格式：国家|区域|省份|城市|ISP，如 中国|0|广东省|深圳市|电信；未知段为 "0"。</summary>
    internal static (string Country, string Province) Parse(string? raw)
    {
        if (string.IsNullOrEmpty(raw)) return ("未知", "");
        var p = raw.Split('|');
        var country = (p.Length > 0 && p[0] != "0" && p[0] != "") ? p[0] : "未知";
        var province = (p.Length > 2 && p[2] != "0" && p[2] != "") ? p[2] : "";
        return (country, province);
    }

    /// <summary>批量兜底解析未缓存的 IP（ip-api batch，最多 100/请求，限速 15 批/分钟）。</summary>
    public async Task EnqueueBatchAsync(IEnumerable<string> ips)
    {
        var pending = ips.Where(ip => !string.IsNullOrEmpty(ip) && !_cache.ContainsKey(ip)
            && !ip.StartsWith("10.") && !ip.StartsWith("192.168.") && !ip.StartsWith("172.16.") && !ip.StartsWith("127."))
            .Distinct().Take(100).ToList();
        if (pending.Count == 0) return;
        try
        {
            var payload = pending.Select(ip => new { query = ip, fields = "query,country,regionName,status" }).ToList();
            var resp = await http.PostAsync("http://ip-api.com/batch?fields=query,country,regionName,status",
                new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json"));
            if (!resp.IsSuccessStatusCode) return;
            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
            foreach (var item in doc.RootElement.EnumerateArray())
            {
                var q = item.GetProperty("query").GetString() ?? "";
                if (item.TryGetProperty("status", out var st) && st.GetString() == "success")
                {
                    var country = item.TryGetProperty("country", out var c) ? c.GetString() ?? "未知" : "未知";
                    var region = item.TryGetProperty("regionName", out var r) ? r.GetString() ?? "" : "";
                    _cache[q] = (country, region);
                }
                else
                {
                    _cache[q] = ("未知", "");
                }
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "GeoIP 批量解析失败");
        }
    }

    /// <summary>近 24h 独立 IP 的地区分布（已解析部分）。</summary>
    public List<(string Country, int Count)> Distribution(IEnumerable<string> ips)
    {
        var dict = new Dictionary<string, int>();
        foreach (var ip in ips)
        {
            var (country, _) = Resolve(ip);
            dict[country] = dict.GetValueOrDefault(country) + 1;
        }
        return dict.OrderByDescending(kv => kv.Value).Select(kv => (kv.Key, kv.Value)).ToList();
    }
}
