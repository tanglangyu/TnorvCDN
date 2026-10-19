using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace TnorvCdn.Api.Services.Dns;

/// <summary>
/// 华为云 DNS API 实现（域名解析服务 DNS，控制面 endpoint dns.myhuaweicloud.com）。
/// 签名：SDK-HMAC-SHA256（AK/SK，CanonicalRequest → StringToSign → HMAC-SHA256）。
/// 接口：GET /v2/zones、GET/POST/PATCH/DELETE /v2/zones/{zone_id}/recordsets
/// </summary>
public class HuaweiDnsProvider : IDnsProvider
{
    private readonly DnsSettingsService _settings;
    private readonly IHttpClientFactory _httpFactory;
    private readonly ILogger<HuaweiDnsProvider> _logger;

    public HuaweiDnsProvider(DnsSettingsService settings, IHttpClientFactory httpFactory, ILogger<HuaweiDnsProvider> logger)
    { _settings = settings; _httpFactory = httpFactory; _logger = logger; }

    public string Name => "huawei";

    private (string Ak, string Sk) Creds => (
        _settings.Field("huawei", "accessKeyId") ?? "",
        _settings.Field("huawei", "secretAccessKey") ?? "");

    public async Task<List<(string Domain, string DomainId)>> ListDomainsAsync()
    {
        var body = await ApiAsync(HttpMethod.Get, "/v2/zones?limit=500", null);
        var list = new List<(string, string)>();
        foreach (var z in GetArray(body, "zones"))
        {
            var name = z.TryGetProperty("name", out var n) ? n.GetString()?.TrimEnd('.') : null;
            var id = z.TryGetProperty("id", out var i) ? i.GetString() : null;
            if (name != null && id != null) list.Add((name, id));
        }
        return list;
    }

    public async Task<string?> UpsertRecordAsync(string domainId, DnsRecordSpec spec)
    {
        // 华为云 zone id 形如 "2c9eb..."；name 为 FQDN（recordset name）
        var fqdn = spec.Host == "@" ? "" : spec.Host + ".";
        // 查现有 recordset
        string? existingId = null;
        var lb = await ApiAsync(HttpMethod.Get, $"/v2/zones/{domainId}/recordsets?limit=500&type={spec.Type}", null);
        foreach (var r in GetArray(lb, "recordsets"))
        {
            var name = r.TryGetProperty("name", out var n) ? n.GetString() : null;
            var type = r.TryGetProperty("type", out var t) ? t.GetString() : null;
            // zone 下的 FQDN：host.zone
            if (type == spec.Type && (name == fqdn + _zoneNameCache || name?.StartsWith(fqdn) == true && name.Contains(_zoneNameCache)))
            {
                var rname = name!.TrimEnd('.');
                var hostPart = rname.EndsWith("." + _zoneNameCache) ? rname[..^(_zoneNameCache.Length + 1)] : "@";
                if (hostPart == spec.Host)
                {
                    existingId = r.TryGetProperty("id", out var i) ? i.GetString() : null;
                    break;
                }
            }
        }

        if (existingId != null)
        {
            var pb = await ApiAsync(HttpMethod.Patch, $"/v2/zones/{domainId}/recordsets/{existingId}",
                new { ttl = spec.Ttl, records = new[] { spec.Value } });
            if (pb != null) return existingId;
            return null;
        }

        var rrName = (spec.Host == "@" ? _zoneNameCache : spec.Host + "." + _zoneNameCache) + ".";
        var cb = await ApiAsync(HttpMethod.Post, $"/v2/zones/{domainId}/recordsets",
            new { name = rrName, type = spec.Type, ttl = spec.Ttl, records = new[] { spec.Value } });
        try { using var d = JsonDocument.Parse(cb!); return d.RootElement.GetProperty("id").GetString(); }
        catch { return null; }
    }

    // zone 名缓存（Upsert 需要 zone 名拼 FQDN；ListDomains 或首次 Upsert 时回填）
    private string _zoneNameCache = "";

    /// <summary>设置 zone 名（华为云接口需要 zone 名拼 FQDN），由 DnsService 在调用前通过 ListDomainsAsync 匹配回填。</summary>
    public void SetZoneName(string zoneName) => _zoneNameCache = zoneName;

    public async Task<bool> DeleteRecordAsync(string domainId, string recordId)
    {
        var body = await ApiAsync(HttpMethod.Delete, $"/v2/zones/{domainId}/recordsets/{recordId}", null);
        return body != null;
    }

    // ---- SDK-HMAC-SHA256 签名 ----
    private async Task<string?> ApiAsync(HttpMethod method, string pathAndQuery, object? payload)
    {
        var (ak, sk) = Creds;
        if (string.IsNullOrEmpty(ak)) { _logger.LogWarning("[DNS-huawei] 未配置凭证"); return null; }
        var endpoint = "dns.myhuaweicloud.com";
        var json = payload == null ? "" : JsonSerializer.Serialize(payload);
        var ts = DateTime.UtcNow.ToString("yyyyMMdd'T'HHmmss'Z'");

        var canonicalHeaders = $"host:{endpoint}\nx-sdk-date:{ts}\n";
        var signedHeaders = "host;x-sdk-date";
        var bodyHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json))).ToLowerInvariant();
        var canonicalRequest = $"{method.Method}\n{Uri.EscapeDataString(pathAndQuery.Split('?')[0])}\n{pathAndQuery.Split('?').Skip(1).FirstOrDefault() ?? ""}\n{canonicalHeaders}\n{signedHeaders}\n{bodyHash}";
        var stringToSign = $"SDK-HMAC-SHA256\n{ts}\n{Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonicalRequest))).ToLowerInvariant()}";
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(sk));
        var signature = Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes(stringToSign))).ToLowerInvariant();
        var auth = $"SDK-HMAC-SHA256 Access={ak}, SignedHeaders={signedHeaders}, Signature={signature}";

        var req = new HttpRequestMessage(method, $"https://{endpoint}{pathAndQuery}");
        if (json.Length > 0) req.Content = new StringContent(json, Encoding.UTF8, "application/json");
        req.Headers.Add("X-Sdk-Date", ts);
        req.Headers.Add("Authorization", auth);

        var resp = await _httpFactory.CreateClient("dns").SendAsync(req);
        return await resp.Content.ReadAsStringAsync();
    }

    private static JsonElement[] GetArray(string body, string field)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty(field, out var arr) && arr.ValueKind == JsonValueKind.Array)
                return arr.EnumerateArray().ToArray();
        }
        catch { }
        return Array.Empty<JsonElement>();
    }
}
