using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace TnorvCdn.Api.Services.Dns;

/// <summary>
/// 聚合DNS管理系统 API 实现（聚合DNS管理系统 API（showdoc 协议））。
/// 要点（文档+实测验证）：form-urlencoded；sign=md5(uid+timestamp+key) 小写；
/// 成功 {"code":0}；记录参数名 recordid（不是 id）；add 必须带 line（默认 "default"）。
/// </summary>
public class AggregatedDnsProvider : IDnsProvider
{
    private readonly DnsSettingsService _settings;
    private readonly IHttpClientFactory _httpFactory;
    private readonly ILogger<AggregatedDnsProvider> _logger;

    public AggregatedDnsProvider(DnsSettingsService settings, IHttpClientFactory httpFactory, ILogger<AggregatedDnsProvider> logger)
    {
        _settings = settings; _httpFactory = httpFactory; _logger = logger;
    }

    public string Name => "aggregated";

    private (string Base, string Uid, string Key) Creds => (
        _settings.Field("aggregated", "apiBase") ?? "https://your-dns-api.example.com/",
        _settings.Field("aggregated", "uid") ?? "",
        _settings.Field("aggregated", "key") ?? "");

    private FormUrlEncodedContent SignForm(Dictionary<string, string> extra)
    {
        var (_, uid, key) = Creds;
        var ts = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
        var sign = Md5(uid + ts + key);
        var form = new Dictionary<string, string> { ["uid"] = uid, ["timestamp"] = ts, ["sign"] = sign };
        foreach (var kv in extra) form[kv.Key] = kv.Value;
        return new FormUrlEncodedContent(form);
    }

    public async Task<List<(string Domain, string DomainId)>> ListDomainsAsync()
    {
        var (base_, _, _) = Creds;
        var resp = await _httpFactory.CreateClient("dns").PostAsync($"{base_.TrimEnd('/')}/api/domain", SignForm(new()));
        var body = await resp.Content.ReadAsStringAsync();
        return ParseRows(body, "id", "name")
            .Select(r => (r.name, r.id)).ToList();
    }

    public async Task<string?> UpsertRecordAsync(string domainId, DnsRecordSpec spec)
    {
        var (base_, _, _) = Creds;
        // 1. 查询现有记录（同主机名+同类型+同线路）
        var existingId = await FindRecordLineAsync(domainId, spec);
        if (existingId != null)
        {
            var form = new Dictionary<string, string>
            {
                ["recordid"] = existingId, ["name"] = spec.Host, ["type"] = spec.Type,
                ["value"] = spec.Value, ["line"] = spec.Line, ["ttl"] = spec.Ttl.ToString(),
            };
            var body = await PostAsync($"{base_.TrimEnd('/')}/api/record/update/{domainId}", form);
            if (IsOk(body))
            {
                _logger.LogInformation("[DNS-aggregated] 记录已更新 {Host} -> {Value}", spec.Host, spec.Value);
                return existingId;
            }
            // 聚合DNS update 唯一性校验会把"更新为自身当前值"也判为 already exists —— 值已一致视为成功
            if (body.Contains("already exists"))
            {
                var sameValue = await FindRecordValueAsync(domainId, spec);
                if (sameValue != null)
                {
                    _logger.LogInformation("[DNS-aggregated] 记录值已一致（幂等）{Host} -> {Value}", spec.Host, spec.Value);
                    return sameValue;
                }
            }
            _logger.LogWarning("[DNS-aggregated] 更新失败: {Body}", body);
            return null;
        }

        // 2. 新增
        var addForm = new Dictionary<string, string>
        {
            ["name"] = spec.Host, ["type"] = spec.Type,
            ["value"] = spec.Value, ["line"] = spec.Line, ["ttl"] = spec.Ttl.ToString(),
        };
        var addBody = await PostAsync($"{base_.TrimEnd('/')}/api/record/add/{domainId}", addForm);
        if (IsOk(addBody))
        {
            _logger.LogInformation("[DNS-aggregated] 记录已创建 {Host} -> {Value}", spec.Host, spec.Value);
            return await FindRecordLineAsync(domainId, spec); // add 响应不含 ID，回查
        }
        _logger.LogWarning("[DNS-aggregated] 新增失败: {Body}", addBody);
        return null;
    }

    public async Task<bool> DeleteRecordAsync(string domainId, string recordId)
    {
        var (base_, _, _) = Creds;
        var body = await PostAsync($"{base_.TrimEnd('/')}/api/record/delete/{domainId}",
            new Dictionary<string, string> { ["recordid"] = recordId });
        return IsOk(body);
    }

    private async Task<string> PostAsync(string url, Dictionary<string, string> form)
    {
        var resp = await _httpFactory.CreateClient("dns").PostAsync(url, SignForm(form));
        return await resp.Content.ReadAsStringAsync();
    }

    private async Task<string?> FindRecordAsync(string domainId, DnsRecordSpec spec)
    {
        var (base_, _, _) = Creds;
        var body = await PostAsync($"{base_.TrimEnd('/')}/api/record/data/{domainId}", new Dictionary<string, string>
        {
            ["subdomain"] = spec.Host, ["type"] = spec.Type, ["limit"] = "100",
        });
        foreach (var (id, name, type, _) in ParseRecordRows(body))
        {
            if (name == spec.Host && type == spec.Type) return id;
        }
        return null;
    }

    /// <summary>查找指定线路的记录（智能 DNS：同主机名按线路并存多条）。</summary>
    public Task<string?> FindRecordIdAsync(string domainId, DnsRecordSpec spec) => FindRecordLineAsync(domainId, spec);

    /// <summary>查找同主机名+类型+线路+值的记录 id（幂等判断用）。</summary>
    private async Task<string?> FindRecordValueAsync(string domainId, DnsRecordSpec spec)
    {
        var (base_, _, _) = Creds;
        var body = await PostAsync($"{base_.TrimEnd('/')}/api/record/data/{domainId}", new Dictionary<string, string>
        {
            ["subdomain"] = spec.Host, ["type"] = spec.Type, ["line"] = spec.Line, ["limit"] = "100",
        });
        foreach (var (id, name, type, value) in ParseRecordRows(body))
        {
            if (name == spec.Host && type == spec.Type && value == spec.Value) return id;
        }
        return null;
    }

    private async Task<string?> FindRecordLineAsync(string domainId, DnsRecordSpec spec)
    {
        var (base_, _, _) = Creds;
        var body = await PostAsync($"{base_.TrimEnd('/')}/api/record/data/{domainId}", new Dictionary<string, string>
        {
            ["subdomain"] = spec.Host, ["type"] = spec.Type, ["line"] = spec.Line, ["limit"] = "100",
        });
        foreach (var (id, name, type, _) in ParseRecordRows(body))
        {
            if (name == spec.Host && type == spec.Type) return id;
        }
        return null;
    }

    private static List<(string id, string name)> ParseRows(string body, string idField, string nameField)
    {
        var out_ = new List<(string, string)>();
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("rows", out var data) && data.ValueKind == JsonValueKind.Array)
            {
                foreach (var rec in data.EnumerateArray())
                {
                    var id = rec.TryGetProperty(idField, out var i)
                        ? (i.ValueKind == JsonValueKind.Number ? i.GetRawText() : i.GetString())
                        : null;
                    var name = rec.TryGetProperty(nameField, out var n) ? n.GetString() : null;
                    if (id != null && name != null) out_.Add((id, name));
                }
            }
        }
        catch { }
        return out_;
    }

    private static List<(string id, string name, string type, string value)> ParseRecordRows(string body)
    {
        var out_ = new List<(string, string, string, string)>();
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("rows", out var data) && data.ValueKind == JsonValueKind.Array)
            {
                foreach (var rec in data.EnumerateArray())
                {
                    var id = rec.TryGetProperty("RecordId", out var i) ? i.GetString() : null;
                    var name = rec.TryGetProperty("Name", out var n) ? n.GetString() : null;
                    var type = rec.TryGetProperty("Type", out var t) ? t.GetString() : null;
                    var value = rec.TryGetProperty("Value", out var v) ? v.GetString() : null;
                    if (id != null && name != null && type != null) out_.Add((id, name, type, value ?? ""));
                }
            }
        }
        catch { }
        return out_;
    }

    private static bool IsOk(string body) => body.Contains("\"code\":0") || body.Contains("\"code\": 0");

    private static string Md5(string s) => Convert.ToHexStringLower(MD5.HashData(Encoding.UTF8.GetBytes(s)));
}
