using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TnorvCdn.Api.Core;
using TnorvCdn.Api.Data;

namespace TnorvCdn.Api.Services;

/// <summary>
/// DNS 三方接入配置服务：配置只存 DnsSettings 表（Id=1 单行），管理员经 /admin/dns/settings 编辑，保存即生效。
/// appsettings.json 的 Dns 节仅作为首次启动的一次性种子（SeedFromConfigIfMissingAsync），
/// 迁移入库后运行时不再读取文件。内存缓存 30s 或更新即失效，避免每次解析都查库。
/// </summary>
public class DnsSettingsService
{
    // 厂商 → 凭证键值（前端表单与 DB ProvidersJson 的形状）
    public record ProviderFields(string[] Keys, string[] SecretKeys);

    public static readonly Dictionary<string, ProviderFields> ProviderSchema = new()
    {
        ["aggregated"] = new(new[] { "apiBase" }, new[] { "uid", "key" }),
        ["aliyun"] = new(new[] { "endpoint" }, new[] { "accessKeyId", "accessKeySecret" }),
        ["dnspod"] = new(Array.Empty<string>(), new[] { "secretId", "secretKey" }),
        ["huawei"] = new(Array.Empty<string>(), new[] { "accessKeyId", "secretAccessKey" }),
        ["cloudflare"] = new(Array.Empty<string>(), new[] { "apiToken" }),
    };

    private readonly IServiceScopeFactory _scopes;
    private readonly IConfiguration _cfg;   // 仅启动种子迁移使用
    private readonly ILogger<DnsSettingsService> _logger;
    private (DnsSettings? Value, DateTime LoadedAt) _cache;
    private static readonly TimeSpan CacheTtl = TimeSpan.FromSeconds(30);

    public DnsSettingsService(IServiceScopeFactory scopes, IConfiguration cfg, ILogger<DnsSettingsService> logger)
    { _scopes = scopes; _cfg = cfg; _logger = logger; }

    /// <summary>使缓存失效（管理端保存后调用，立即生效）。</summary>
    public void Invalidate() => _cache = default;

    /// <summary>
    /// 启动时调用：DnsSettings 表无 Id=1 行时，把 appsettings.json 的 Dns 节一次性迁移入库。
    /// 此后配置的唯一来源是数据库，运行时不再读文件。
    /// </summary>
    public async Task SeedFromConfigIfMissingAsync()
    {
        try
        {
            using var scope = _scopes.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<CdnDbContext>();
            if (await db.DnsSettings.AnyAsync(s => s.Id == 1)) return;

            var providers = new Dictionary<string, Dictionary<string, string>>();
            foreach (var (name, schema) in ProviderSchema)
            {
                var obj = new Dictionary<string, string>();
                foreach (var k in schema.Keys.Concat(schema.SecretKeys))
                {
                    // appsettings 的 PascalCase 节（Dns:Providers:Aggregated:ApiBase）→ camelCase 键
                    var v = _cfg[$"Dns:Providers:{Pascal(name)}:{Pascal(k)}"];
                    if (!string.IsNullOrEmpty(v)) obj[k] = v;
                }
                providers[name] = obj;
            }

            db.DnsSettings.Add(new DnsSettings
            {
                Id = 1,
                Enabled = (_cfg["Dns:Enabled"] ?? "false").Equals("true", StringComparison.OrdinalIgnoreCase),
                Provider = _cfg["Dns:Provider"] ?? "aggregated",
                DefaultDomainId = _cfg["Dns:DefaultDomainId"] ?? "",
                ProvidersJson = JsonSerializer.Serialize(providers),
            });
            await db.SaveChangesAsync();
            _logger.LogInformation("已把 appsettings.json 的 Dns 配置一次性迁移入 DnsSettings 表（此后以数据库为准）");
        }
        catch (Exception ex)
        {
            _logger.LogWarning("DNS 配置种子迁移失败（可在管理端「DNS服务商」页重新配置）: {Message}", ex.Message);
        }
    }

    /// <summary>读取当前配置行（唯一来源：数据库；无行时返回 null，读取方走安全默认值）。</summary>
    public DnsSettings? Get()
    {
        var (cached, at) = _cache;
        if (cached != null && DateTime.UtcNow - at < CacheTtl) return cached;
        try
        {
            using var scope = _scopes.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<CdnDbContext>();
            var row = db.DnsSettings.AsNoTracking().FirstOrDefault(s => s.Id == 1);
            _cache = (row, DateTime.UtcNow);
            return row;
        }
        catch (Exception ex)
        {
            _logger.LogWarning("读取 DNS 配置失败: {Message}", ex.Message);
            return null;
        }
    }

    // ---- 只读数据库（缺省：关闭/aggregated/空域名），不再回落 appsettings.json ----
    public bool Enabled => Get()?.Enabled ?? false;

    public string Provider => Get()?.Provider ?? "aggregated";

    public string? DefaultDomainId => Get()?.DefaultDomainId;

    /// <summary>读某厂商某凭证字段：仅从 DB ProvidersJson 读取，未配置返回 null。</summary>
    public string? Field(string provider, string field)
    {
        var row = Get();
        if (string.IsNullOrEmpty(row?.ProvidersJson)) return null;
        try
        {
            using var doc = JsonDocument.Parse(row.ProvidersJson);
            if (doc.RootElement.TryGetProperty(provider, out var p) &&
                p.TryGetProperty(field, out var v) && v.ValueKind == JsonValueKind.String)
            {
                var s = v.GetString();
                return string.IsNullOrEmpty(s) ? null : s;
            }
        }
        catch { /* ProvidersJson 损坏时按未配置处理 */ }
        return null;
    }

    private static string Pascal(string s) => char.ToUpperInvariant(s[0]) + s[1..];
}
