using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace TnorvCdn.Api.Services.Dns;

/// <summary>
/// 腾讯云 DNSPod API v3 实现（POST JSON + TC3-HMAC-SHA256 签名）。
/// 产品：dnspod.tencentcloudapi.com，版本 2021-03-23。
/// 接口：DescribeDomainList / RecordList / CreateRecord / ModifyRecord / DeleteRecord
/// </summary>
public class DnspodProvider : IDnsProvider
{
    private const string Host = "dnspod.tencentcloudapi.com";
    private const string Version = "2021-03-23";

    private readonly DnsSettingsService _settings;
    private readonly IHttpClientFactory _httpFactory;
    private readonly ILogger<DnspodProvider> _logger;

    public DnspodProvider(DnsSettingsService settings, IHttpClientFactory httpFactory, ILogger<DnspodProvider> logger)
    { _settings = settings; _httpFactory = httpFactory; _logger = logger; }

    public string Name => "dnspod";

    private (string Id, string Key) Creds => (
        _settings.Field("dnspod", "secretId") ?? "",
        _settings.Field("dnspod", "secretKey") ?? "");

    public async Task<List<(string Domain, string DomainId)>> ListDomainsAsync()
    {
        var body = await ApiAsync("DescribeDomainList", new { Limit = 100 });
        var list = new List<(string, string)>();
        foreach (var d in GetArray(body, "DomainList"))
        {
            var name = d.TryGetProperty("Name", out var n) ? n.GetString() : null;
            var id = d.TryGetProperty("DomainId", out var i) ? i.GetRawText() : name;
            if (name != null) list.Add((name, id ?? name));
        }
        return list;
    }

    public async Task<string?> UpsertRecordAsync(string domainId, DnsRecordSpec spec)
    {
        // DNSPod 按域名管理；domainId 这里传域名名
        string? existingId = null; string? existingLine = "DEFAULT";
        var lb = await ApiAsync("RecordList", new { Domain = domainId, Subdomain = spec.Host, RecordType = spec.Type });
        foreach (var r in GetArray(lb, "RecordList"))
        {
            var name = r.TryGetProperty("Name", out var n) ? n.GetString() : null;
            var type = r.TryGetProperty("Type", out var t) ? t.GetString() : null;
            if (name == (spec.Host == "@" ? "@" : spec.Host) && type == spec.Type)
            {
                existingId = r.TryGetProperty("RecordId", out var i) ? i.GetRawText() : null;
                existingLine = r.TryGetProperty("Line", out var l) ? l.GetString() : "DEFAULT";
                break;
            }
        }

        if (existingId != null && long.TryParse(existingId, out var recId))
        {
            var mb = await ApiAsync("ModifyRecord", new
            {
                Domain = domainId, RecordId = recId, SubDomain = spec.Host, RecordType = spec.Type,
                RecordLine = LineRegistry.ForDnspod(spec.Line), Value = spec.Value, TTL = spec.Ttl,
            });
            if (IsOk(mb)) return existingId;
            _logger.LogWarning("[DNS-dnspod] 更新失败: {Body}", mb);
            return null;
        }

        var cb = await ApiAsync("CreateRecord", new
        {
            Domain = domainId, SubDomain = spec.Host, RecordType = spec.Type,
            RecordLine = LineRegistry.ForDnspod(spec.Line), Value = spec.Value, TTL = spec.Ttl,
        });
        if (IsOk(cb))
        {
            try { using var d = JsonDocument.Parse(cb); return d.RootElement.GetProperty("RecordId").GetRawText(); }
            catch { return null; }
        }
        _logger.LogWarning("[DNS-dnspod] 新增失败: {Body}", cb);
        return null;
    }

    public async Task<bool> DeleteRecordAsync(string domainId, string recordId)
    {
        if (!long.TryParse(recordId, out var id)) return false;
        var body = await ApiAsync("DeleteRecord", new { Domain = domainId, RecordId = id });
        return IsOk(body);
    }

    // ---- TC3-HMAC-SHA256 签名 ----
    private async Task<string> ApiAsync(string action, object payload)
    {
        var (id, key) = Creds;
        var json = JsonSerializer.Serialize(payload);
        var ts = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var date = DateTime.UtcNow.ToString("yyyy-MM-dd");

        var canonicalRequest = $"POST\n/\n\ncontent-type:application/json; charset=utf-8\nhost:{Host}\n\ncontent-type;host\n{Sha256Hex(json)}";
        var stringToSign = $"TC3-HMAC-SHA256\n{ts}\n{date}/dnspod/tc3_request\n{Sha256Hex(canonicalRequest)}";

        var kDate = Hmac(Encoding.UTF8.GetBytes("TC3" + key), date);
        var kService = Hmac(kDate, "dnspod");
        var kSigning = Hmac(kService, "tc3_request");
        var signature = Convert.ToHexString(Hmac(kSigning, stringToSign)).ToLowerInvariant();

        var auth = $"TC3-HMAC-SHA256 Credential={id}/{date}/dnspod/tc3_request, SignedHeaders=content-type;host, Signature={signature}";

        var req = new HttpRequestMessage(HttpMethod.Post, $"https://{Host}/")
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };
        req.Headers.Add("Authorization", auth);
        req.Headers.Add("X-TC-Action", action);
        req.Headers.Add("X-TC-Version", Version);
        req.Headers.Add("X-TC-Timestamp", ts.ToString());
        req.Headers.Add("X-TC-Region", "");

        var resp = await _httpFactory.CreateClient("dns").SendAsync(req);
        return await resp.Content.ReadAsStringAsync();
    }

    private static byte[] Hmac(byte[] key, string data)
    {
        using var h = new HMACSHA256(key);
        return h.ComputeHash(Encoding.UTF8.GetBytes(data));
    }

    private static string Sha256Hex(string s) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(s))).ToLowerInvariant();

    private static bool IsOk(string body) => !body.Contains("\"Error\"");

    private static JsonElement[] GetArray(string body, string field)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("Response", out var r) && r.TryGetProperty(field, out var arr) && arr.ValueKind == JsonValueKind.Array)
                return arr.EnumerateArray().ToArray();
        }
        catch { }
        return Array.Empty<JsonElement>();
    }
}
