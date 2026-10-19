using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace TnorvCdn.Api.Services.Dns;

/// <summary>
/// Cloudflare API v4 实现（Bearer API Token）。
/// 接口：GET /zones、GET/POST/PUT/DELETE /zones/{zone_id}/dns_records
/// </summary>
public class CloudflareDnsProvider : IDnsProvider
{
    private readonly DnsSettingsService _settings;
    private readonly IHttpClientFactory _httpFactory;
    private readonly ILogger<CloudflareDnsProvider> _logger;

    public CloudflareDnsProvider(DnsSettingsService settings, IHttpClientFactory httpFactory, ILogger<CloudflareDnsProvider> logger)
    { _settings = settings; _httpFactory = httpFactory; _logger = logger; }

    public string Name => "cloudflare";

    private string Token => _settings.Field("cloudflare", "apiToken") ?? "";

    private HttpRequestMessage Req(HttpMethod m, string path, object? payload = null)
    {
        var req = new HttpRequestMessage(m, $"https://api.cloudflare.com/client/v4{path}");
        req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", Token);
        if (payload != null)
            req.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
        return req;
    }

    public async Task<List<(string Domain, string DomainId)>> ListDomainsAsync()
    {
        var list = new List<(string, string)>();
        var resp = await _httpFactory.CreateClient("dns").SendAsync(Req(HttpMethod.Get, "/zones?per_page=100"));
        var body = await resp.Content.ReadAsStringAsync();
        foreach (var z in ResultArray(body))
        {
            var name = z.TryGetProperty("name", out var n) ? n.GetString() : null;
            var id = z.TryGetProperty("id", out var i) ? i.GetString() : null;
            if (name != null && id != null) list.Add((name, id));
        }
        return list;
    }

    public async Task<string?> UpsertRecordAsync(string zoneId, DnsRecordSpec spec)
    {
        // 查现有记录
        string? existingId = null;
        var resp = await _httpFactory.CreateClient("dns").SendAsync(
            Req(HttpMethod.Get, $"/zones/{zoneId}/dns_records?type={spec.Type}&name={(spec.Host == "@" ? "" : spec.Host + ".")}{_zoneName(zoneId)}&per_page=100"));
        var body = await resp.Content.ReadAsStringAsync();
        foreach (var r in ResultArray(body))
        {
            existingId = r.TryGetProperty("id", out var i) ? i.GetString() : null;
            break;
        }

        if (existingId != null)
        {
            var put = await _httpFactory.CreateClient("dns").SendAsync(Req(HttpMethod.Put,
                $"/zones/{zoneId}/dns_records/{existingId}",
                new { type = spec.Type, name = spec.Host, content = spec.Value, ttl = spec.Ttl }));
            var putBody = await put.Content.ReadAsStringAsync();
            if (IsOk(putBody)) return existingId;
            _logger.LogWarning("[DNS-cloudflare] 更新失败: {Body}", putBody);
            return null;
        }

        var post = await _httpFactory.CreateClient("dns").SendAsync(Req(HttpMethod.Post,
            $"/zones/{zoneId}/dns_records",
            new { type = spec.Type, name = spec.Host, content = spec.Value, ttl = spec.Ttl }));
        var postBody = await post.Content.ReadAsStringAsync();
        if (IsOk(postBody))
        {
            try
            {
                using var d = JsonDocument.Parse(postBody);
                return d.RootElement.GetProperty("result").GetProperty("id").GetString();
            }
            catch { return null; }
        }
        _logger.LogWarning("[DNS-cloudflare] 新增失败: {Body}", postBody);
        return null;
    }

    public async Task<bool> DeleteRecordAsync(string zoneId, string recordId)
    {
        var resp = await _httpFactory.CreateClient("dns").SendAsync(Req(HttpMethod.Delete, $"/zones/{zoneId}/dns_records/{recordId}"));
        var body = await resp.Content.ReadAsStringAsync();
        return IsOk(body);
    }

    // zone 名缓存（查询过滤需要 FQDN；ListDomains 匹配时回填）
    private readonly Dictionary<string, string> _zoneNames = new();

    /// <summary>回填 zone 名（zoneId → 域名名），供查询过滤拼 FQDN。</summary>
    public void SetZoneName(string zoneId, string zoneName) => _zoneNames[zoneId] = zoneName;

    private string _zoneName(string zoneId) => _zoneNames.TryGetValue(zoneId, out var n) ? n : "";

    private static bool IsOk(string body) => body.Contains("\"success\":true");

    private static JsonElement[] ResultArray(string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("result", out var arr) && arr.ValueKind == JsonValueKind.Array)
                return arr.EnumerateArray().ToArray();
        }
        catch { }
        return Array.Empty<JsonElement>();
    }
}
