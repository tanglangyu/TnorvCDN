using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace TnorvCdn.Api.Services.Dns;

/// <summary>
/// 阿里云 DNS（云解析）API 实现。
/// RPC 签名协议：GET/POST + 公共参数（AccessKeyId, SignatureMethod=HMAC-SHA1, SignatureNonce, Timestamp ISO8601...），
/// 签名 = PercentEncode(stringToSign)，stringToSign = "POST&%2F&" + percentEncode(sortedQuery)。
/// 接口：DescribeDomains / DescribeDomainRecords / AddDomainRecord / UpdateDomainRecord / DeleteDomainRecord
/// </summary>
public class AliyunDnsProvider : IDnsProvider
{
    private readonly DnsSettingsService _settings;
    private readonly IHttpClientFactory _httpFactory;
    private readonly ILogger<AliyunDnsProvider> _logger;

    public AliyunDnsProvider(DnsSettingsService settings, IHttpClientFactory httpFactory, ILogger<AliyunDnsProvider> logger)
    { _settings = settings; _httpFactory = httpFactory; _logger = logger; }

    public string Name => "aliyun";

    private (string Ak, string Sk, string Endpoint) Creds => (
        _settings.Field("aliyun", "accessKeyId") ?? "",
        _settings.Field("aliyun", "accessKeySecret") ?? "",
        _settings.Field("aliyun", "endpoint") ?? "https://alidns.aliyuncs.com/");

    public async Task<List<(string Domain, string DomainId)>> ListDomainsAsync()
    {
        var body = await RpcAsync("DescribeDomains", new Dictionary<string, string> { ["PageSize"] = "100" });
        var list = new List<(string, string)>();
        foreach (var d in GetArray(body, "Domains", "Domain"))
        {
            var name = d.TryGetProperty("DomainName", out var n) ? n.GetString() : null;
            var id = d.TryGetProperty("DomainId", out var i) ? i.GetString() : null;
            if (name != null && id != null) list.Add((name, id));
        }
        return list;
    }

    public async Task<string?> UpsertRecordAsync(string domainId, DnsRecordSpec spec)
    {
        var (_, _, _) = Creds;
        var domainName = domainId; // 阿里云接口用域名名（DomainName）作为参数
        // 查现有记录（同 RR + Type）
        string? existingId = null; string? existingLine = null;
        var body = await RpcAsync("DescribeDomainRecords", new Dictionary<string, string>
        {
            ["DomainName"] = domainName, ["RRKeyWord"] = spec.Host, ["TypeKeyWord"] = spec.Type, ["PageSize"] = "100",
        });
        foreach (var r in GetArray(body, "DomainRecords", "Record"))
        {
            var rr = r.TryGetProperty("RR", out var rrEl) ? rrEl.GetString() : null;
            var type = r.TryGetProperty("Type", out var tEl) ? tEl.GetString() : null;
            var line = r.TryGetProperty("Line", out var lEl) ? lEl.GetString() : "default";
            if (rr == spec.Host && type == spec.Type)
            {
                existingId = r.TryGetProperty("RecordId", out var idEl) ? idEl.GetString() : null;
                existingLine = line;
                break;
            }
        }

        if (existingId != null)
        {
            var ub = await RpcAsync("UpdateDomainRecord", new Dictionary<string, string>
            {
                ["RecordId"] = existingId, ["RR"] = spec.Host, ["Type"] = spec.Type,
                ["Value"] = spec.Value, ["Line"] = existingLine ?? LineRegistry.ForAliyun(spec.Line), ["TTL"] = spec.Ttl.ToString(),
            });
            if (IsOk(ub)) return existingId;
            _logger.LogWarning("[DNS-aliyun] 更新失败: {Body}", ub);
            return null;
        }

        var ab = await RpcAsync("AddDomainRecord", new Dictionary<string, string>
        {
            ["DomainName"] = domainName, ["RR"] = spec.Host, ["Type"] = spec.Type,
            ["Value"] = spec.Value, ["Line"] = LineRegistry.ForAliyun(spec.Line), ["TTL"] = spec.Ttl.ToString(),
        });
        if (IsOk(ab))
        {
            try { using var d = JsonDocument.Parse(ab); return d.RootElement.GetProperty("RecordId").GetString(); }
            catch { return null; }
        }
        _logger.LogWarning("[DNS-aliyun] 新增失败: {Body}", ab);
        return null;
    }

    public async Task<bool> DeleteRecordAsync(string domainId, string recordId)
    {
        var body = await RpcAsync("DeleteDomainRecord", new Dictionary<string, string> { ["RecordId"] = recordId });
        return IsOk(body);
    }

    // ---- 阿里云 RPC 签名 ----
    private async Task<string> RpcAsync(string action, Dictionary<string, string> biz)
    {
        var (ak, sk, endpoint) = Creds;
        var p = new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
            ["AccessKeyId"] = ak,
            ["Action"] = action,
            ["Format"] = "JSON",
            ["RegionId"] = "cn-hangzhou",
            ["SignatureMethod"] = "HMAC-SHA1",
            ["SignatureNonce"] = Guid.NewGuid().ToString(),
            ["SignatureVersion"] = "1.0",
            ["Timestamp"] = DateTime.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'"),
            ["Version"] = "2015-01-09",
        };
        foreach (var kv in biz) p[kv.Key] = kv.Value;
        var canonical = string.Join("&", p.Select(kv => $"{PercentEncode(kv.Key)}={PercentEncode(kv.Value)}"));
        var stringToSign = "POST&%2F&" + PercentEncode(canonical);
        using var hmac = new HMACSHA1(Encoding.UTF8.GetBytes(sk + "&"));
        var signature = Convert.ToBase64String(hmac.ComputeHash(Encoding.UTF8.GetBytes(stringToSign)));
        p["Signature"] = signature;
        var content = new FormUrlEncodedContent(p);
        var resp = await _httpFactory.CreateClient("dns").PostAsync(endpoint, content);
        return await resp.Content.ReadAsStringAsync();
    }

    private static string PercentEncode(string s) =>
        Uri.EscapeDataString(s).Replace("+", "%20").Replace("*", "%2A").Replace("%7E", "~");

    private static bool IsOk(string body) => !body.Contains("\"Code\"") && !body.Contains("\"Message\":\"");

    private static JsonElement[] GetArray(string body, params string[] path)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            var cur = doc.RootElement;
            foreach (var seg in path)
            {
                if (!cur.TryGetProperty(seg, out cur)) return Array.Empty<JsonElement>();
            }
            return cur.ValueKind == JsonValueKind.Array ? cur.EnumerateArray().ToArray() : Array.Empty<JsonElement>();
        }
        catch { return Array.Empty<JsonElement>(); }
    }
}
