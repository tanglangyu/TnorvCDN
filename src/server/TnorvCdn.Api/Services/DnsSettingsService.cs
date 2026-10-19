using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TnorvCdn.Api.Core;
using TnorvCdn.Api.Data;

namespace TnorvCdn.Api.Services;

/// <summary>
/// DNS 三方接入配置读取服务（存 DnsSettings 表，管理员经 /admin/dns/settings 可编辑）。
/// appsettings.json 的 Dns 节作为兜底（未在库中配置过的字段回落到文件值，平滑迁移）。
/// 内存缓存 30s 或更新即失效，避免每次解析都查库。
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
    private readonly IConfiguration _cfg;
    private readonly ILogger<DnsSettingsService> _logger;
    private readonly ConcurrentDictionary<string, byte> _invalidate = new();
    private (DnsSettings? Value, DateTime LoadedAt) _cache;
    private static readonly TimeSpan CacheTtl = TimeSpan.FromSeconds(30);

    public DnsSettingsService(IServiceScopeFactory scopes, IConfiguration cfg, ILogger<DnsSettingsService> logger)
    { _scopes = scopes; _cfg = cfg; _logger = logger; }

    /// <summary>使缓存失效（管理端保存后调用，立即生效）。</summary>
    public void Invalidate() => _cache = default;

    /// <summary>读取当前配置（库优先，无行时返回 null 由调用方走文件兜底）。</summary>
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
            _logger.LogWarning("读取 DNS 配置失败（走文件兜底）: {Message}", ex.Message);
            return null;
        }
    }

    // ---- 兜底合并读取：库值 ?? 文件值 ----
    public bool Enabled => Get()?.Enabled
        ?? (_cfg["Dns:Enabled"] ?? "true").Equals("true", StringComparison.OrdinalIgnoreCase);

    public string Provider => Get()?.Provider ?? _cfg["Dns:Provider"] ?? "aggregated";

    public string? DefaultDomainId => string.IsNullOrEmpty(Get()?.DefaultDomainId)
        ? _cfg["Dns:DefaultDomainId"] : Get()!.DefaultDomainId;

    /// <summary>读某厂商某凭证字段：DB ProvidersJson 优先，回落 appsettings.json。</summary>
    public string? Field(string provider, string field)
    {
        var row = Get();
        if (!string.IsNullOrEmpty(row?.ProvidersJson))
        {
            try
            {
                using var doc = JsonDocument.Parse(row.ProvidersJson);
                if (doc.RootElement.TryGetProperty(provider, out var p) &&
                    p.TryGetProperty(field, out var v) && v.ValueKind == JsonValueKind.String)
                {
                    var s = v.GetString();
                    if (!string.IsNullOrEmpty(s)) return s;
                }
            }
            catch { /* ProvidersJson 损坏时走文件兜底 */ }
        }
        // 文件路径映射：camelCase 字段 → appsettings 的 PascalCase 节（Dns:Providers:Aggregated:ApiBase）
        var pascal = char.ToUpperInvariant(field[0]) + field[1..];
        return _cfg[$"Dns:Providers:{Pascal(provider)}:{pascal}"];
    }

    private static string Pascal(string s) => char.ToUpperInvariant(s[0]) + s[1..];
}
