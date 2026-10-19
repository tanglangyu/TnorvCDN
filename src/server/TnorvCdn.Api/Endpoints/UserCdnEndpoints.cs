using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using TnorvCdn.Api.Core;
using TnorvCdn.Api.Data;
using TnorvCdn.Api.Services;

namespace TnorvCdn.Api.Endpoints;

/// <summary>
/// 用户端 CDN 端点（/api/user/*）：RapidDev UserConsole 数据源。
/// 按 JWT 用户过滤网站；仪表盘聚合（网站数/今日请求/流量/拦截）。
/// 复用主控 JWT（iss=tnorv），与 RapidDevAuth 同一登录态。
/// </summary>
public static class UserCdnEndpoints
{
    public static void MapUserCdnEndpoints(this WebApplication app)
    {
        var grp = app.MapGroup("/api/user");

        // 我的网站（当前用户的网站列表，含精简状态）
        grp.MapGet("/servers", async (CdnDbContext db, IConfiguration cfg, bool? stats, string? q, int? page, int? pageSize) =>
        {
            var uid = await CurrentUserIdAsync(cfg, db);
            if (uid == null) return Results.Json(new { code = 401, message = "未登录" });
            var servers = await db.Servers.AsNoTracking()
                .Where(s => s.UserId == uid)
                .OrderBy(s => s.Id)
                .ToListAsync();
            if (!string.IsNullOrWhiteSpace(q))
            {
                var kw = q.ToLower();
                servers = servers.Where(sv => sv.Domains.Any(d => d.ToLower().Contains(kw))).ToList();
            }
            var clusters = await db.Clusters.AsNoTracking().ToListAsync();
            var nodes = await db.Nodes.AsNoTracking().Where(n => n.IsOnline).ToListAsync();
            // 运营模式：集群 Ops.ShowNodesOnline=false 时不向用户端暴露节点在线数（字段整体不返回，前端随之隐藏）
            var showNodesByCluster = clusters.ToDictionary(
                c => c.Id,
                c => string.IsNullOrEmpty(c.OpsJson)
                    ? true
                    : System.Text.Json.JsonSerializer.Deserialize<Core.OpsConfig>(c.OpsJson, (System.Text.Json.JsonSerializerOptions?)null)?.ShowNodesOnline ?? true);
            // 今日统计（stats=true 时按站点聚合今日请求/流量/命中率，站点卡片视图用）
            Dictionary<long, (int Req, long Bytes, int Hit)>? todayStats = null;
            if (stats == true)
            {
                var todayStart = DateTime.UtcNow.Date;
                var ids = servers.Select(s => s.Id).ToList();
                todayStats = await db.AccessLogs.AsNoTracking()
                    .Where(l => ids.Contains(l.ServerId) && l.Timestamp >= todayStart)
                    .GroupBy(l => l.ServerId)
                    .Select(g => new { ServerId = g.Key, Req = g.Count(), Bytes = g.Sum(x => x.BytesSent), Hit = g.Count(x => x.CacheStatus == "HIT") })
                    .ToDictionaryAsync(x => x.ServerId, x => (x.Req, x.Bytes, x.Hit));
            }
            var all = servers.Select(s =>
                {
                    var t = todayStats?.GetValueOrDefault(s.Id) ?? (0, 0, 0);
                    var online = nodes.Count(n => n.ClusterId == s.ClusterId);
                    object item = new
                    {
                        s.Id,
                        s.Domains,
                        clusterId = s.ClusterId,
                        clusterName = clusters.FirstOrDefault(c => c.Id == s.ClusterId)?.Name ?? "",
                        origin = s.Origin.Primaries.Select(o => $"{o.Host}:{o.Port}").ToList(),
                        originFull = s.Origin.Primaries.Select(o => new { o.Scheme, o.Host, o.Port }).ToList(),
                        s.Enabled,
                        s.ForceHttps,
                        s.SslMode,
                        s.ServerType,
                        s.Region,
                        s.IcpStatus,
                        s.IcpNumber,
                        s.IcpNote,
                        s.Group,
                        s.AccessMode,
                        s.RealtimePublish,
                        cache = new { enabled = s.Cache.Enabled },
                        nodesOnline = online,
                        nodesTotal = online + db.Nodes.Count(n => n.ClusterId == s.ClusterId && !n.IsOnline),
                        // 用户侧站点状态（不承诺同步时长，仅展示当前状态）
                        status = ComputeUserStatus(s, online),
                        sslState = s.SslState,
                        sslExpiresAt = s.SslExpiresAt,
                        todayRequests = t.Item1,
                        todayBytes = t.Item2,
                        todayHitRate = t.Item1 > 0 ? Math.Round(t.Item3 * 100.0 / t.Item1, 1) : 0,
                        s.CreatedAt,
                    };
                    // 隐藏节点数：序列化后剔除 nodesOnline/nodesTotal（匿名类型无法按集群条件省字段，走 JObject 剔除）
                    if (!showNodesByCluster.GetValueOrDefault(s.ClusterId, true))
                    {
                        var jo = System.Text.Json.Nodes.JsonNode.Parse(System.Text.Json.JsonSerializer.Serialize(item)) as System.Text.Json.Nodes.JsonObject;
                        jo?.Remove("nodesOnline");
                        jo?.Remove("nodesTotal");
                        item = jo ?? item;
                    }
                    return item;
                }).ToList();
            if (page != null || pageSize != null)
            {
                var pg = Math.Clamp(page ?? 1, 1, 1000);
                var psz = Math.Clamp(pageSize ?? 20, 1, 200);
                var total = all.Count;
                var items = all.Skip((pg - 1) * psz).Take(psz).ToList();
                return Results.Json(new { code = 200, message = "操作成功", data = new { total, page = pg, pageSize = psz, items } });
            }
            return Results.Json(new { code = 200, message = "操作成功", data = all });
        });

        // 我的用量统计（近 7 天按天：请求数/流量/拦截数）
        grp.MapGet("/traffic-stats", async (CdnDbContext db, IConfiguration cfg, long? serverId, int? days) =>
        {
            var uid = await CurrentUserIdAsync(cfg, db);
            if (uid == null) return Results.Json(new { code = 401, message = "未登录" });
            var dayCount = Math.Clamp(days ?? 7, 1, 90);
            var since = DateTime.UtcNow.AddDays(-dayCount);
            var myServers = await db.Servers.Where(s => s.UserId == uid)
                .Select(s => new { s.Id, s.Domains }).ToListAsync();
            var allowed = myServers.Select(s => s.Id).ToList();
            if (serverId.HasValue && allowed.Contains(serverId.Value))
            {
                myServers = myServers.Where(s => s.Id == serverId.Value).ToList();
                allowed = [serverId.Value];
            }
            var logs = db.AccessLogs.Where(l => allowed.Contains(l.ServerId) && l.Timestamp >= since);
            // P5 性能项：聚合结果 5 分钟短缓存（key 带用户与条件维度），仪表盘/用量页高频查询不打表
            var cacheKey = $"traffic-stats:{uid}:{serverId?.ToString() ?? "all"}:{dayCount}:{string.Join(',', allowed)}";
            var cached = Services.StatsCache.Get(cacheKey);
            if (cached != null) return Results.Content(cached, "application/json");
            var daily = await logs
                .GroupBy(l => l.Timestamp.Date)
                .Select(g => new
                {
                    date = g.Key,
                    requests = g.Count(),
                    hits = g.Count(l => l.CacheStatus == "HIT"),
                    blocked = g.Count(l => l.WafAction == "block"),
                    bytes = g.Sum(l => l.BytesSent),
                })
                .ToListAsync();
            var payload = System.Text.Json.JsonSerializer.Serialize(new
            {
                code = 200,
                message = "操作成功",
                data = daily.OrderBy(d => d.date).ToList(),
                servers = myServers.Select(s => new { id = s.Id, domain = s.Domains.FirstOrDefault() ?? ("站点#" + s.Id) }),
            });
            Services.StatsCache.Set(cacheKey, payload);
            return Results.Content(payload, "application/json");
        });

        // 我的拦截日志（WAF/CC）
        grp.MapGet("/waf/logs", async (CdnDbContext db, IConfiguration cfg, int? page, int? pageSize) =>
        {
            var uid = await CurrentUserIdAsync(cfg, db);
            if (uid == null) return Results.Json(new { code = 401, message = "未登录" });
            var pg = Math.Max(1, page ?? 1); var sz = (pageSize == null || pageSize <= 0) ? 20 : Math.Clamp(pageSize.Value, 1, 100);
            var serverIds = await db.Servers.Where(s => s.UserId == uid).Select(s => s.Id).ToListAsync();
            var q = db.AccessLogs.Where(l => serverIds.Contains(l.ServerId) && l.WafAction == "block");
            var total = await q.CountAsync();
            var logs = await q.OrderByDescending(l => l.Timestamp).Skip((pg - 1) * sz).Take(sz)
                .Select(l => new
                {
                    l.Id, l.Host, l.Uri, l.Status, l.BlockReason, l.RemoteAddr, l.Timestamp,
                })
                .ToListAsync();
            return Results.Json(new { code = 200, message = "操作成功", data = new { total, items = logs } });
        });

        // 我的证书
        grp.MapGet("/certs", async (CdnDbContext db, IConfiguration cfg, string? q, int? page, int? pageSize) =>
        {
            var uid = await CurrentUserIdAsync(cfg, db);
            if (uid == null) return Results.Json(new { code = 401, message = "未登录" });
            // 证书列表：手动 + ACME 自动签发（只读；acme 证书实体在节点，仅展示元数据）
            // Domains 为转换列无法在 SQL 侧 SelectMany——先取 id/domain 全量再内存匹配
            var myServers = await db.Servers.Where(s => s.UserId == uid)
                .Select(s => new { s.Id, s.Domains }).ToListAsync();
            var myDomains = myServers.SelectMany(s => s.Domains).ToList();
            var apexes = myDomains.Where(d => !d.StartsWith('*')).Select(d => IcpGate.RegistrableDomain(d)).ToHashSet();
            var all = await db.Certificates.AsNoTracking()
                .Where(c => c.ClusterId > 0)
                .OrderByDescending(c => c.CreatedAt)
                .ToListAsync();
            var certs = all.Where(c =>
            {
                var cd = c.Domains.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                return cd.Any(d => myDomains.Contains(d) || apexes.Contains(IcpGate.RegistrableDomain(d)));
            }).ToList();
            var shaped = certs.Select(c => new
            {
                c.Id, c.Domains, c.ExpiresAt, c.Source, c.CreatedAt,
                sourceLabel = c.Source == "acme" ? "自动签发" : "手动上传",
            }).ToList();
            if (!string.IsNullOrWhiteSpace(q))
            {
                var kw = q.ToLower();
                shaped = shaped.Where(c => c.Domains.ToLower().Contains(kw)).ToList();
            }
            if (page != null || pageSize != null)
            {
                var pg = Math.Clamp(page ?? 1, 1, 1000);
                var psz = Math.Clamp(pageSize ?? 20, 1, 200);
                var total = shaped.Count;
                var items = shaped.Skip((pg - 1) * psz).Take(psz).ToList();
                return Results.Json(new { code = 200, message = "操作成功", data = new { total, page = pg, pageSize = psz, items } });
            }
            return Results.Json(new { code = 200, message = "操作成功", data = shaped });
        });

        // 我的证书签发日志（时间线，排障用）
        grp.MapGet("/cert-events", async (CdnDbContext db, IConfiguration cfg, int? page, int? pageSize) =>
        {
            var uid = await CurrentUserIdAsync(cfg, db);
            if (uid == null) return Results.Json(new { code = 401, message = "未登录" });
            var pg = Math.Max(1, page ?? 1); var sz = (pageSize == null || pageSize <= 0) ? 20 : Math.Clamp(pageSize.Value, 1, 100);
            var serverIds = await db.Servers.Where(s => s.UserId == uid).Select(s => s.Id).ToListAsync();
            var q = db.CertEvents.AsNoTracking().Where(e => serverIds.Contains(e.ServerId));
            var total = await q.CountAsync();
            var items = await q.OrderByDescending(e => e.CreatedAt).Skip((pg - 1) * sz).Take(sz)
                .Select(e => new { e.Id, e.ServerId, e.Domain, e.Status, e.Error, e.CreatedAt }).ToListAsync();
            return Results.Json(new { code = 200, message = "操作成功", data = new { total, items } });
        });

        // 我的仪表盘
        grp.MapGet("/dashboard", async (CdnDbContext db, IConfiguration cfg) =>
        {
            var uid = await CurrentUserIdAsync(cfg, db);
            if (uid == null) return Results.Json(new { code = 401, message = "未登录" });
            var serverIds = await db.Servers.Where(s => s.UserId == uid).Select(s => s.Id).ToListAsync();
            var today = DateTime.UtcNow.Date;
            // P5 性能项：仪表盘今日聚合 5 分钟短缓存（按 UTC 日期切换 key）
            var cacheKey = $"dashboard:{uid}:{today:yyyyMMdd}:{string.Join(',', serverIds)}";
            var cached = Services.StatsCache.Get(cacheKey);
            if (cached != null) return Results.Content(cached, "application/json");
            var todayLogs = db.AccessLogs.Where(l => serverIds.Contains(l.ServerId) && l.Timestamp >= today);
            var requests = await todayLogs.CountAsync();
            var blocked = await todayLogs.CountAsync(l => l.WafAction == "block");
            var hits = await todayLogs.CountAsync(l => l.CacheStatus == "HIT");
            var bytes = await todayLogs.SumAsync(l => (long?)l.BytesSent) ?? 0;
            var dashPayload = System.Text.Json.JsonSerializer.Serialize(new
            {
                code = 200,
                message = "操作成功",
                data = new
                {
                    servers = serverIds.Count,
                    requestsToday = requests,
                    blockedToday = blocked,
                    cacheHitRate = requests > 0 ? Math.Round(hits * 100.0 / requests, 1) : 0,
                    bytesToday = bytes,
                },
            });
            Services.StatsCache.Set(cacheKey, dashPayload);
            return Results.Content(dashPayload, "application/json");
        });
    }

    private static async Task<long?> CurrentUserIdAsync(IConfiguration cfg, CdnDbContext db)
    {
        var http = new HttpContextAccessor().HttpContext;
        var header = http?.Request.Headers.Authorization.FirstOrDefault();
        if (string.IsNullOrEmpty(header) || !header.StartsWith("Bearer ")) return null;
        var token = header["Bearer ".Length..].Trim();
        try
        {
            var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(cfg["Jwt:Key"] ?? "dev-only-secret-change-me-32bytes!!"));
            var principal = new JwtSecurityTokenHandler().ValidateToken(token, new TokenValidationParameters
            {
                ValidateIssuer = true, ValidIssuer = "tnorv",
                ValidateAudience = true, ValidAudience = "tnorv",
                IssuerSigningKey = key, ValidateLifetime = true,
            }, out _);
            var raw = principal.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            return long.TryParse(raw, out var id) ? id : null;
        }
        catch { return null; }
    }

    /// <summary>用户侧站点状态：综合备案/启停/证书签发状态，只展示不承诺时间。</summary>
    private static string ComputeUserStatus(Server s, int nodesOnline)
    {
        if (!s.Enabled)
            return s.Region == "cn" && s.IcpStatus != "approved" ? "configuring" : "disabled";
        if (nodesOnline == 0) return "updating";          // 集群无在线节点：配置待生效
        if (s.SslMode == "auto" && s.SslState == "pending_dns") return "not_resolved";
        if (s.SslMode == "auto" && s.SslState == "pending_issuing") return "issuing";
        if (s.SslMode == "auto" && s.SslState == "failed") return "cert_failed";
        if (s.SslMode == "auto" && s.SslState != "issued" && s.SslState != "") return "issuing";
        return "normal";
    }
}
