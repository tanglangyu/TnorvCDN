using Microsoft.EntityFrameworkCore;
using TnorvCdn.Api.Data;

namespace TnorvCdn.Api.Services;

/// <summary>
/// DNS 编排服务：多厂商三方 DNS 接入 + 集群域名解析。
///   1. 网站创建/更新后自动分配 CNAME（网站域名 → 集群子域名，通用设计。
///   2. 集群 DnsDomain 的 A 记录自动维护（指向在线边缘节点 IP）。
///   3. 厂商经 Dns:Provider 配置选择（aggregated/aliyun/dnspod/huawei/cloudflare），凭证在 Dns:Providers:{厂商}:* 节。
/// </summary>
public class DnsService
{
    private readonly IServiceScopeFactory _scopes;
    private readonly DnsSettingsService _settings;
    private readonly ILogger<DnsService> _logger;
    private readonly IEnumerable<IDnsProvider> _providers;

    public DnsService(IEnumerable<IDnsProvider> providers, IServiceScopeFactory scopes, DnsSettingsService settings, ILogger<DnsService> logger)
    {
        _providers = providers; _scopes = scopes; _settings = settings; _logger = logger;
    }

    private IDnsProvider? Provider => _providers.FirstOrDefault(p => p.Name.Equals(_settings.Provider, StringComparison.OrdinalIgnoreCase));

    public string? DefaultDomainId => _settings.DefaultDomainId;

    public bool Enabled => _settings.Enabled && Provider != null && !string.IsNullOrEmpty(DefaultDomainId);

    /// <summary>网站创建/更新后调用：为每个绑定域名建立 CNAME → 集群子域名。</summary>
    public async Task SyncSiteDomainsAsync(long clusterId, string[] domains)
    {
        if (!Enabled || domains.Length == 0) return;
        var domainId = DefaultDomainId!;
        var provider = Provider!;
        var clusterDns = await GetClusterDnsDomainAsync(clusterId);
        if (string.IsNullOrEmpty(clusterDns)) return;
        var root = await ResolveRootDomainAsync(provider, domainId);
        if (root == null) return;

        foreach (var d in domains)
        {
            if (string.IsNullOrWhiteSpace(d) || d.StartsWith('*')) continue; // 通配符跳过
            var host = GetHostPart(d, root);
            if (host == null) continue;
            try
            {
                await provider.UpsertRecordAsync(domainId, new DnsRecordSpec(host, "CNAME", clusterDns));
            }
            catch (Exception ex)
            {
                _logger.LogWarning("网站 CNAME 同步失败 {Domain}: {Message}", d, ex.Message);
            }
        }
    }

    /// <summary>集群域名解析：把集群 DnsDomain 的 A 记录指向在线边缘节点 IP。</summary>
    public async Task SyncClusterRecordsAsync(long clusterId)
    {
        if (!Enabled) return;
        var domainId = DefaultDomainId!;
        var provider = Provider!;
        var clusterDns = await GetClusterDnsDomainAsync(clusterId);
        if (string.IsNullOrEmpty(clusterDns)) return;
        var root = await ResolveRootDomainAsync(provider, domainId);
        if (root == null) return;
        var host = GetHostPart(clusterDns, root);
        if (host == null) return;

        await using var scope = _scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<CdnDbContext>();
        var nodes = await db.Nodes
            .Where(n => n.ClusterId == clusterId && n.IsOnline && n.Role == "edge")
            .Select(n => new { n.PublicIps, n.Line })
            .ToListAsync();
        if (nodes.Count == 0) return;

        // 智能 DNS：按线路分组，每条线路取首个在线节点 IP 建立分线路 A 记录
        var lineIp = nodes
            .Where(n => n.PublicIps.Length > 0)
            .GroupBy(n => string.IsNullOrWhiteSpace(n.Line) ? "default" : n.Line)
            .ToDictionary(g => g.Key, g => g.First().PublicIps[0]);

        foreach (var (line, ip) in lineIp)
        {
            try
            {
                await provider.UpsertRecordAsync(domainId, new DnsRecordSpec(host, "A", ip, Line: line));
                _logger.LogInformation("集群解析已同步 {ClusterDns} [{Line}] -> {Ip}", clusterDns, line, ip);
            }
            catch (Exception ex)
            {
                _logger.LogWarning("集群解析同步失败 {ClusterDns}[{Line}]: {Message}", clusterDns, line, ex.Message);
            }
        }
    }

    /// <summary>同步用户智能 DNS 记录到三方 DNS（仅当记录属于默认主域名时真实生效）。
    /// 返回 'synced'（已生效）/ 'local'（仅本站，域名不在主域下）/ 'failed:...'。</summary>
    public async Task<string> SyncUserRecordAsync(string fqdn, string type, string value, int ttl, string line)
    {
        if (!Enabled) return "local";
        var domainId = DefaultDomainId!;
        var provider = Provider!;
        var root = await ResolveRootDomainAsync(provider, domainId);
        if (root == null) return "local";
        var host = GetHostPart(fqdn, root);
        if (host == null) return "local"; // 不属于托管主域：仅本站记录
        try
        {
            await provider.UpsertRecordAsync(domainId, new DnsRecordSpec(host, type, value, ttl, string.IsNullOrWhiteSpace(line) ? "default" : line));
            return "synced";
        }
        catch (Exception ex)
        {
            _logger.LogWarning("用户记录同步失败 {Fqdn}: {Message}", fqdn, ex.Message);
            return "failed:" + ex.Message;
        }
    }

    /// <summary>删除用户记录在三方 DNS 的对应解析（不存在返回 ok）。</summary>
    public async Task<string> DeleteUserRecordAsync(string fqdn, string type, string line)
    {
        if (!Enabled) return "local";
        var domainId = DefaultDomainId!;
        var provider = Provider!;
        var root = await ResolveRootDomainAsync(provider, domainId);
        if (root == null) return "local";
        var host = GetHostPart(fqdn, root);
        if (host == null) return "local";
        try
        {
            var recordId = await provider.FindRecordIdAsync(domainId, new DnsRecordSpec(host, type, "", 600, line ?? "default"));
            if (recordId != null) await provider.DeleteRecordAsync(domainId, recordId);
            return "synced";
        }
        catch (Exception ex)
        {
            return "failed:" + ex.Message;
        }
    }

    /// <summary>通过 provider 列域名，把 domainId 解析成域名名（不同厂商 domainId 语义不同）。</summary>
    private async Task<string?> ResolveRootDomainAsync(IDnsProvider provider, string domainId)
    {
        try
        {
            var domains = await provider.ListDomainsAsync();
            var hit = domains.FirstOrDefault(d => d.DomainId == domainId || d.Domain == domainId);
            return hit.Domain ?? domains.FirstOrDefault().Domain;
        }
        catch (Exception ex)
        {
            _logger.LogWarning("DNS 域名列表获取失败（{Provider}）: {Message}", provider.Name, ex.Message);
            return null;
        }
    }

    private async Task<string?> GetClusterDnsDomainAsync(long clusterId)
    {
        await using var scope = _scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<CdnDbContext>();
        return await db.Clusters.Where(c => c.Id == clusterId).Select(c => c.DnsDomain).FirstOrDefaultAsync();
    }

    /// <summary>从完整域名提取相对 DNS 主域名的主机部分；不同后缀返回 null。</summary>
    private static string? GetHostPart(string fqdn, string rootDomain)
    {
        var fq = fqdn.Trim().TrimEnd('.').ToLowerInvariant();
        if (fq.EndsWith("." + rootDomain, StringComparison.Ordinal))
            return fq[..^(rootDomain.Length + 1)];
        if (fq == rootDomain)
            return "@";
        return null;
    }
}
