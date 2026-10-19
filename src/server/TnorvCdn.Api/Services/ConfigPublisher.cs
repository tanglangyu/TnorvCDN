using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TnorvCdn.Api.Core;
using TnorvCdn.Api.Data;

namespace TnorvCdn.Api.Services;

/// <summary>
/// 配置发布服务：采用"版本化配置 + 快照下发"模式。
/// 每次配置变更生成新版本号；节点按版本号拉取完整快照，原子热更新，零停机。
/// </summary>
public class ConfigPublisher(CdnDbContext db, PurgeRegistry purges, ChallengeRegistry challenges, IConfiguration cfg, ILogger<ConfigPublisher> logger)
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };


    /// <summary>计算月流量超限的站点集合（TrafficLimit.MonthlyGB，自然月口径）。
    /// 结果缓存 60s：PublishAsync 与每次节点拉取（BuildNodeConfigAsync）都会调用，
    /// AccessLogs 聚合是全管线最重的查询，不缓存会在多节点轮询下放大为持续全表扫描。</summary>
    private static HashSet<long> _trafficBlockedCache = [];
    private static DateTime _trafficBlockedAt = DateTime.MinValue;
    private static readonly SemaphoreSlim _tbLock = new(1, 1);

    private async Task<HashSet<long>> TrafficBlockedAsync(List<Server> servers)
    {
        var limited = servers.Where(s => s.Advanced?.TrafficLimit != null && s.Advanced.TrafficLimit.MonthlyGB > 0).ToList();
        if (limited.Count == 0) return [];
        if (DateTime.UtcNow - _trafficBlockedAt < TimeSpan.FromSeconds(60)) return _trafficBlockedCache;
        await _tbLock.WaitAsync();
        try
        {
            if (DateTime.UtcNow - _trafficBlockedAt < TimeSpan.FromSeconds(60)) return _trafficBlockedCache;
            var blocked = new HashSet<long>();
            var monthStart = new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1, 0, 0, 0, DateTimeKind.Utc);
            var ids = limited.Select(s => s.Id).ToList();
            var monthBytes = await db.AccessLogs
                .Where(l => l.Timestamp >= monthStart && ids.Contains(l.ServerId))
                .GroupBy(l => l.ServerId)
                .Select(g => new { ServerId = g.Key, Bytes = g.Sum(x => x.BytesSent) })
                .ToDictionaryAsync(x => x.ServerId, x => x.Bytes);
            foreach (var s in limited)
            {
                if (monthBytes.GetValueOrDefault(s.Id) > s.Advanced.TrafficLimit.MonthlyGB * 1024L * 1024 * 1024)
                    blocked.Add(s.Id);
            }
            _trafficBlockedCache = blocked;
            _trafficBlockedAt = DateTime.UtcNow;
            return blocked;
        }
        finally { _tbLock.Release(); }
    }
    /// <summary>集群级默认配置兜底合并通用设计：
    /// 站点级优先——缓存未启用用集群默认；安全子块为空补集群默认名单/规则；高级零值字段补集群默认。
    /// 返回合并后的 (cache, security, advanced)。</summary>
    private static (CacheConfig cache, SecurityConfig security, AdvancedConfig advanced) MergeClusterDefaults(
        TnorvCdn.Api.Core.Server s, ClusterDefaults? defs)
    {
        var cache = s.Cache;
        var security = s.Security;
        var advanced = s.Advanced;
        if (defs == null) return (cache, security, advanced);

        // 缓存：站点未启用缓存时用集群默认
        if (!cache.Enabled && defs.Cache is { Enabled: true })
            cache = defs.Cache;

        // 安全：逐子块兜底（站点该块为空/未启用时补集群默认）
        var dsec = defs.Security;
        if (dsec != null)
        {
            if (security.IpWhitelist.Count == 0 && dsec.IpWhitelist.Count > 0) security.IpWhitelist = dsec.IpWhitelist;
            if (security.IpBlacklist.Count == 0 && dsec.IpBlacklist.Count > 0) security.IpBlacklist = dsec.IpBlacklist;
            if (!security.Cc.Enabled && dsec.Cc.Enabled) security.Cc = dsec.Cc;
            if (!security.JsShield.Enabled && dsec.JsShield.Enabled) security.JsShield = dsec.JsShield;
            if (security.UaRules.Count == 0 && dsec.UaRules.Count > 0) security.UaRules = dsec.UaRules;
            if (security.WafRules.Count == 0 && dsec.WafRules.Count > 0) security.WafRules = dsec.WafRules;
            if (!security.UrlAuth.Enabled && dsec.UrlAuth.Enabled) security.UrlAuth = dsec.UrlAuth;
            if ((security.Referers == null || !security.Referers.Enabled) && dsec.Referers is { Enabled: true })
                security.Referers = dsec.Referers;
        }

        // firewall-actions：拦截动作状态码（站点 0=默认 403，集群默认可覆盖为 404 等）
        if (security.BlockStatus == 0 && dsec != null && dsec.BlockStatus > 0)
            security.BlockStatus = dsec.BlockStatus;

        // 高级：字段级兜底（站点零值/未配置时补集群默认）
        var dadv = defs.Advanced;
        if (dadv != null)
        {
            if (advanced.Charset == "" && dadv.Charset != "") advanced.Charset = dadv.Charset;
            if (!advanced.Compression && dadv.Compression) advanced.Compression = true;
            if (!advanced.WebpOptimization && dadv.WebpOptimization) advanced.WebpOptimization = true;
            if (advanced.RequestLimitPerMinute == 0 && dadv.RequestLimitPerMinute > 0) advanced.RequestLimitPerMinute = dadv.RequestLimitPerMinute;
            if (advanced.MaxBodySizeMB == 0 && dadv.MaxBodySizeMB > 0) advanced.MaxBodySizeMB = dadv.MaxBodySizeMB;
            if (advanced.CustomPages == null && dadv.CustomPages != null) advanced.CustomPages = dadv.CustomPages;
            if (advanced.Multimedia == null && dadv.Multimedia != null) advanced.Multimedia = dadv.Multimedia;
            if (advanced.RemoteAddrHeader == "" && dadv.RemoteAddrHeader != "") advanced.RemoteAddrHeader = dadv.RemoteAddrHeader;
            if (advanced.Headers.Count == 0 && dadv.Headers.Count > 0) advanced.Headers = dadv.Headers;
        }
        return (cache, security, advanced);
    }

    /// <summary>IP 库清单合并通用设计：deny→IpBlacklist、allow→IpWhitelist 追加到全集群站点。</summary>
    private async Task MergeIpLibraryAsync(List<Server> servers)
    {
        var items = await db.IpLibrary.AsNoTracking().ToListAsync();
        if (items.Count == 0) return;
        foreach (var s in servers)
        {
            foreach (var item in items)
            {
                var cidrs = item.Cidrs.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => x.Trim()).Where(x => x.Length > 0)
                    .Select(x => new TnorvCdn.Api.Core.IpRule { Cidr = x, Note = item.Name });
                if (item.Kind == "allow")
                    s.Security.IpWhitelist.AddRange(cidrs.Where(c => !s.Security.IpWhitelist.Any(e => e.Cidr == c.Cidr)));
                else
                    s.Security.IpBlacklist.AddRange(cidrs.Where(c => !s.Security.IpBlacklist.Any(e => e.Cidr == c.Cidr)));
            }
        }
    }

    /// <summary>发布指定集群的配置：将集群下所有启用网站序列化为节点快照并落库。</summary>
    public async Task<long> PublishAsync(long clusterId)
    {
        var servers = await db.Servers
            .Where(s => s.ClusterId == clusterId && s.Enabled)
            .AsNoTracking()
            .ToListAsync();

        var cluster0 = await db.Clusters.AsNoTracking().FirstOrDefaultAsync(c => c.Id == clusterId);
        var defs = cluster0?.Defaults;

        var trafficBlocked = await TrafficBlockedAsync(servers);
        await MergeIpLibraryAsync(servers);

        var snapshot = new
        {
            generatedAt = DateTime.UtcNow,
            servers = servers.Select(s =>
            {
                var m = MergeClusterDefaults(s, defs);
                return new
                {
                    id = s.Id,
                    serverType = s.ServerType,
                    domains = s.Domains,
                    httpPorts = s.HttpPorts,
                    httpsPorts = s.HttpsPorts,
                    l4Ports = s.L4Ports,
                    l4ProxyProtocol = s.L4ProxyProtocol,
                    forceHttps = s.ForceHttps,
                    sslMode = s.SslMode,
                    origin = s.Origin,
                    cache = m.cache,
                    security = m.security,
                    locations = s.Locations.Count > 0 ? s.Locations : null,
                    advanced = m.advanced,
                    blocked = trafficBlocked.Contains(s.Id)
                };
            })
        };

        var json = JsonSerializer.Serialize(snapshot, JsonOpts);

        // 版本号取当前时间戳（单调）；持久化到集群行，节点拉取时与本地版本比对决定是否 changed
        var version = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var cluster = await db.Clusters.FindAsync(clusterId);
        if (cluster != null)
        {
            cluster.ConfigVersion = version;
            await db.SaveChangesAsync();
        }

        var existing = await db.Nodes.Where(n => n.ClusterId == clusterId).ToListAsync();
        logger.LogInformation("已发布集群 {ClusterId} 配置版本 {Version}（{Count} 个网站，{Nodes} 个节点待同步）",
            clusterId, version, servers.Count, existing.Count);

        return version;
    }

    /// <summary>为指定节点生成完整节点配置 JSON（含集群所有启用网站 + 待执行刷新指令）。</summary>
    public async Task<string?> BuildNodeConfigAsync(long nodeId)
    {
        var node = await db.Nodes.FindAsync(nodeId);
        if (node == null) return null;
        var cluster = await db.Clusters.FindAsync(node.ClusterId);

        var servers = await db.Servers
            .Where(s => s.ClusterId == node.ClusterId && s.Enabled)
            .AsNoTracking()
            .ToListAsync();

        // L2 节点（就近回源）：附加同集群其它节点的缓存服务地址（边缘→L2→源站分层）
        // 边缘缓存共享：同集群全部在线边缘节点（含自身，节点侧按 NodeId 排除）
        var edgePeers = await db.Nodes
            .Where(n => n.ClusterId == node.ClusterId && n.Role == "edge" && n.IsOnline)
            .Select(n => new { id = n.Id, name = n.Name, ips = n.PublicIps })
            .ToListAsync();

        var siblings = node.Role == "l2"
            ? []
            : await db.Nodes.Where(n => n.ClusterId == node.ClusterId && n.Role == "l2" && n.IsOnline)
                .Select(n => new { n.Id, n.Name, n.PublicIps })
                .ToListAsync();

        var pendingPurges = purges.Drain(node.ClusterId);

        // ACME 签发账户（运营配置，存 AcmeAccounts 表）：节点签发证书用。
        // 邮箱模板支持 {userId} 占位符 —— 单邮箱 ACME 账户过多会被 CA 限流，按用户派生邮箱分散账户。
        var acmeAccount = await db.AcmeAccounts.AsNoTracking()
            .Where(a => a.Enabled).OrderBy(a => a.Id).FirstOrDefaultAsync();
        string acmeEmail = "";
        if (acmeAccount != null)
        {
            var tpl = string.IsNullOrWhiteSpace(acmeAccount.EmailTemplate) ? acmeAccount.DefaultEmail : acmeAccount.EmailTemplate;
            var sampleServer = servers.FirstOrDefault();
            acmeEmail = (tpl ?? "")
                .Replace("{userId}", sampleServer != null ? sampleServer.UserId.ToString() : "0")
                .Replace("{serverId}", node.Id.ToString());
        }

        // 手动上传证书（PEM 下发节点 SNI；私钥仅走节点通道，不落日志）
        var certs = await db.Certificates
            .Where(c => c.ClusterId == node.ClusterId)
            .Select(c => new { c.Domains, c.CertPem, c.KeyPem, c.ExpiresAt })
            .ToListAsync();

        var peerAddrToNode = edgePeers.SelectMany(n => n.ips.Select(ip => new { n.id, addr = ip.Contains(':') ? ip : ip + ":3128" }))
            .GroupBy(x => x.addr).ToDictionary(g => g.Key, g => g.First().id);

        var trafficBlocked = await TrafficBlockedAsync(servers);
        await MergeIpLibraryAsync(servers);
        var defs = cluster?.Defaults;

        var snapshot = new
        {
            generatedAt = DateTime.UtcNow,
            servers = servers.Select(s =>
            {
                var m = MergeClusterDefaults(s, defs);
                return new
                {
                    id = s.Id,
                    serverType = s.ServerType,
                    domains = s.Domains,
                    httpPorts = s.HttpPorts,
                    httpsPorts = s.HttpsPorts,
                    l4Ports = s.L4Ports,
                    l4ProxyProtocol = s.L4ProxyProtocol,
                    forceHttps = s.ForceHttps,
                    sslMode = s.SslMode,
                    origin = s.Origin,
                    cache = m.cache,
                    security = m.security,
                    locations = s.Locations.Count > 0 ? s.Locations : null,
                    advanced = m.advanced,
                    blocked = trafficBlocked.Contains(s.Id)
                };
            }),
            purges = pendingPurges,
            manualCerts = certs,
            challenges = challenges.DrainValid(node.ClusterId).Select(c => new { path = c.Path, keyAuth = c.KeyAuth }).ToList(),
            acme = (string.IsNullOrWhiteSpace(acmeEmail) && acmeAccount == null) ? null : new
            {
                email = acmeEmail,
                provider = acmeAccount?.Provider ?? "letsencrypt",
                directoryUrl = acmeAccount?.DirectoryUrl ?? "",
                backupDirectoryUrl = acmeAccount?.BackupDirectoryUrl ?? "",
                eabKid = acmeAccount?.EabKid ?? "",
                eabHmacKey = acmeAccount?.EabHmacKey ?? "",
            },
            renew = servers
                .Where(s => s.SslRenewRequestedAt != null && DateTime.UtcNow - s.SslRenewRequestedAt < TimeSpan.FromMinutes(10))
                .Select(s => new { serverId = s.Id }).ToList(),
            l2Nodes = siblings.Select(n => new { id = n.Id, name = n.Name, ips = n.PublicIps }),
            siblingNodes = edgePeers,
            ops = cluster.Ops,
            ddos = cluster.Ddos,
            dnsRecords = await db.NsDomains.AsNoTracking()
                .Where(d => d.Status == "active")
                .Select(d => new { id = d.Id, name = d.Name, type = d.Type, value = d.Value, ttl = d.Ttl, line = d.Line })
                .ToListAsync(),
            routing = ComputeRouting(node, servers, peerAddrToNode),
            upgrade = InUpgradeWindow(cluster.Ops) && node.Version != null && node.Version.StartsWith("pending-")
                ? new { version = node.Version["pending-".Length..], url = $"{cfg["Node:DownloadBaseUrl"] ?? "/downloads"}/tnorv-node-{node.Version["pending-".Length..]}-linux-amd64", sha256 = (string?)null }
                : null
        };

        return JsonSerializer.Serialize(snapshot, JsonOpts);
    }

    /// <summary>拨测驱动的动态回源路径：直连 vs 经兄弟节点两跳，延迟低 30%+ 才绕行。</summary>
    private object? ComputeRouting(TnorvCdn.Api.Core.Node node, List<TnorvCdn.Api.Core.Server> servers,
        Dictionary<string, long> peerMap)
    {
        var primary = servers.SelectMany(s => s.Origin.Primaries)
            .Where(o => o.Enabled)
            .OrderBy(o => o.Weight)
            .FirstOrDefault();
        if (primary == null) return null;
        var originAddr = $"{primary.Host}:{primary.Port}";
        var (mode, via, directMs, viaMs) = RoutingSchedulerService.ComputeRoutingMapped(node.Id, originAddr, peerMap);
        return new { mode, via, directMs, viaMs, origin = originAddr };
    }

    /// <summary>升级维护窗口判定（schedule）：未配置(-1)视为随时可升级；跨零点窗口（如 22~6）正确处理。</summary>
    internal static bool InUpgradeWindow(TnorvCdn.Api.Core.OpsConfig? ops)
    {
        if (ops == null || ops.UpgradeWindowStart < 0 || ops.UpgradeWindowEnd < 0) return true;
        var h = DateTime.UtcNow.Hour;
        return ops.UpgradeWindowStart <= ops.UpgradeWindowEnd
            ? h >= ops.UpgradeWindowStart && h < ops.UpgradeWindowEnd
            : h >= ops.UpgradeWindowStart || h < ops.UpgradeWindowEnd;
    }

    /// <summary>配置内容摘要（节点比对用，防止误判版本未变）。</summary>
    public static string HashConfig(string json)
    {
        var bytes = SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(json));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
