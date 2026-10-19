using Microsoft.EntityFrameworkCore;
using TnorvCdn.Api.Core;
using TnorvCdn.Api.Data;
using TnorvCdn.Api.Services;

namespace TnorvCdn.Api.Endpoints;

/// <summary>
/// 用户端网站自助管理：新增/编辑/删除自己的站点。
/// 受套餐配额约束（MaxServers/MaxDomains），中国大陆区域强制走备案审核流程。
/// </summary>
public static class UserServerCrudEndpoints
{
    public static void MapUserServerCrudEndpoints(this WebApplication app)
    {
        var grp = app.MapGroup("/api/user");

        // 新增网站（自动绑默认集群；域名数/站点数受套餐配额限制）
        grp.MapPost("/servers", async (UserServerRequest req, CdnDbContext db, IConfiguration cfg, ConfigPublisher publisher, ConfigPublishQueue publishQueue, AuditService audit) =>
        {
            var uid = await UserCdnExtraEndpoints.UserIdAsyncPublic(cfg);
            if (uid == null) return Results.Json(new { code = 401, message = "未登录" });
            if (req.ServerType != null && req.ServerType != "http")
            {
                var (okLb, msgLb) = await Services.PlanGate.AllowAsync(db, uid.Value, "lb");
                if (!okLb) return Results.Json(new { code = 403, message = msgLb });
            }
            var u = await db.Users.FindAsync(uid);

            // 套餐配额
            var sub = await db.Subscriptions.FirstOrDefaultAsync(s => s.UserId == uid && s.Enabled && s.ExpiresAt > DateTime.UtcNow);
            if (sub == null) return Results.Json(new { code = 403, message = "请先购买套餐后再接入网站" });
            var plan = await db.Plans.FindAsync(sub.PlanId);
            if (plan == null || !plan.Enabled) return Results.Json(new { code = 403, message = "套餐不可用" });

            var domains = req.Domains?.Select(d => d.Trim().ToLowerInvariant()).Where(d => d.Length > 0).Distinct().ToList() ?? [];
            if (domains.Count == 0) return Results.Json(new { code = 400, message = "请填写域名" });

            var myCount = await db.Servers.CountAsync(s => s.UserId == uid);
            if (myCount >= plan.MaxServers)
                return Results.Json(new { code = 403, message = $"已达到套餐网站数上限（{plan.MaxServers} 个），请升级套餐" });
            if (domains.Count > plan.MaxDomains)
                return Results.Json(new { code = 403, message = $"单站点域名数超出套餐上限（{plan.MaxDomains} 个）" });

            // 域名占用检查（全网唯一）
            var allDomainsJson = await db.Servers.AsNoTracking().Select(s => s.DomainsJson).ToListAsync();
            var taken = allDomainsJson.SelectMany(dj => (IEnumerable<string>)ParseDomains(dj)).Intersect(domains).ToList();
            if (taken.Count > 0)
                return Results.Json(new { code = 409, message = $"域名已被占用：{string.Join(", ", taken)}" });

            // 中国大陆区域必须先备案：一级域名有 approved 记录才允许创建（继承 approved），否则直接拒绝
            var region = req.Region is "global" or "cn" or "overseas" ? req.Region : "global";
            string icpStatus = "none";
            if (region == "cn")
            {
                var apexes = domains.Where(d => !d.StartsWith('*')).Select(d => IcpGate.RegistrableDomain(d)).Distinct().ToList();
                var approvedServers = await db.Servers.AsNoTracking().Where(s => s.IcpStatus == "approved").Select(s => s.Domains).ToListAsync();
                var missing = apexes.Where(apex => !approvedServers.Any(ds =>
                    ds.Any(d => IcpGate.RegistrableDomain(d) == apex))).ToList();
                if (missing.Count > 0)
                    return Results.Json(new { code = 403, message = $"一级域名 {string.Join(", ", missing)} 尚未通过备案审核，无法创建中国大陆区域网站。请先提交备案并等待审核通过。" });
                icpStatus = "approved"; // 一级域名已备案，站点直接可用
            }

            var server = new Server
            {
                ClusterId = 1,
                UserId = uid.Value,
                Domains = domains.ToArray(),
                Region = region,
                ServerType = req.ServerType is "http" or "tcp" or "tls" or "udp" ? req.ServerType : "http",
                HttpPorts = req.ServerType is "tcp" or "udp" ? [] : (req.HttpPorts is { Length: > 0 } ? req.HttpPorts : [80]),
                HttpsPorts = req.SslMode == "off" ? [] : (req.HttpsPorts is { Length: > 0 } ? req.HttpsPorts : [443]),
                ForceHttps = req.ForceHttps == true,
                SslMode = req.SslMode is "auto" or "manual" or "off" ? req.SslMode : "auto",
                AccessMode = req.AccessMode is "cname" or "ns" ? req.AccessMode : "cname",
                // 一级域名已备案（icpStatus=approved）时直接启用；否则等备案审核通过后由审核动作启用
                Enabled = icpStatus == "approved",
                IcpStatus = icpStatus,
                IcpNumber = req.IcpNumber ?? "",
                Origin = new OriginConfig
                {
                    Primaries = [new Origin
                    {
                        Host = req.OriginHost ?? "",
                        Port = req.OriginPort > 0 ? req.OriginPort.Value : 80,
                        Scheme = req.OriginScheme is "http" or "https" or "s3" ? req.OriginScheme : "http",
                        Weight = 1,
                        Enabled = true,
                    }],
                    ScheduleAlgorithm = "round-robin",
                },
                Cache = new CacheConfig { Enabled = req.CacheEnabled == true },
                Security = new SecurityConfig { Cc = new CcConfig { Enabled = false, Threshold = 300, WindowSeconds = 60, BanSeconds = 60 } },
                CreatedAt = DateTime.UtcNow,
            };
            if (string.IsNullOrWhiteSpace(server.Origin.Primaries[0].Host))
                return Results.Json(new { code = 400, message = "请填写源站地址" });

            db.Servers.Add(server);
            await db.SaveChangesAsync();
            // NS 托管接入：创建时直接生成委派型解析记录（与 PUT 切换逻辑一致）
            if (server.AccessMode == "ns")
            {
                await AccessModeHelper.UpsertDelegationsAsync(db, server, uid!.Value);
                await db.SaveChangesAsync();
            }
            publishQueue.EnqueueUser(server.ClusterId, server.RealtimePublish);
            _ = audit.LogAsync($"user.server.create {server.Id}", $"uid={uid} domains={string.Join(",", domains)} region={region}");
            return Results.Ok(new
            {
                id = server.Id,
                domains = server.Domains,
                region = server.Region,
                enabled = server.Enabled,
                icpStatus = server.IcpStatus,
                message = region == "cn" ? "已创建，中国大陆区域需备案审核通过后自动启用" : "创建成功",
            });
        });

        // 编辑自己的站点（源站/缓存/HTTPS 开关；域名与区域变更走重新校验）
        grp.MapPut("/servers/{id:long}", async (long id, UserServerRequest req, CdnDbContext db, IConfiguration cfg, ConfigPublisher publisher, ConfigPublishQueue publishQueue) =>
        {
            var uid = await UserCdnExtraEndpoints.UserIdAsyncPublic(cfg);
            if (uid == null) return Results.Json(new { code = 401, message = "未登录" });
            var server = await db.Servers.FindAsync(id);
            if (server == null || server.UserId != uid) return Results.Json(new { code = 404, message = "站点不存在" });

            if (req.OriginHost != null)
            {
                if (server.Origin.Primaries.Count > 0)
                {
                    var p0 = server.Origin.Primaries[0];
                    p0.Host = req.OriginHost;
                    if (req.OriginPort > 0) p0.Port = req.OriginPort.Value;
                    if (req.OriginScheme != null && req.OriginScheme is "http" or "https" or "s3") p0.Scheme = req.OriginScheme;
                    server.Origin = new OriginConfig
                    {
                        Primaries = server.Origin.Primaries,
                        Backups = server.Origin.Backups,
                        ScheduleAlgorithm = server.Origin.ScheduleAlgorithm,
                        HostOverride = server.Origin.HostOverride,
                        FollowOriginHost = server.Origin.FollowOriginHost,
                        PathRules = server.Origin.PathRules,
                    };
                }
                else
                {
                    // 源站为空时（如快速接入向导仅传 originHost）新增首个源站；重新赋值确保 JSON 列变更被 EF 检测
                    server.Origin = new OriginConfig
                    {
                        Primaries = [new Origin
                        {
                            Scheme = req.OriginScheme is "http" or "https" or "s3" ? req.OriginScheme : "http",
                            Host = req.OriginHost,
                            Port = req.OriginPort is > 0 ? req.OriginPort.Value : 80,
                        }],
                        Backups = server.Origin.Backups,
                        ScheduleAlgorithm = server.Origin.ScheduleAlgorithm,
                        HostOverride = server.Origin.HostOverride,
                        FollowOriginHost = server.Origin.FollowOriginHost,
                        PathRules = server.Origin.PathRules,
                    };
                }
            }
            if (req.CacheEnabled != null) server.Cache.Enabled = req.CacheEnabled.Value;
            if (req.SslMode != null && req.SslMode is "auto" or "manual" or "off")
            {
                server.SslMode = req.SslMode;
                server.HttpsPorts = req.SslMode == "off" ? [] : (req.HttpsPorts is { Length: > 0 } ? req.HttpsPorts : [443]);
                // 关闭 HTTPS 或切手动证书时清除自动签发状态；重新切回 auto 时节点会重新走 DNS 校验+签发
                if (req.SslMode != "auto")
                {
                    server.SslState = "";
                    server.SslLastError = "";
                }
                else if (server.SslState == "failed" || server.SslState == "pending_dns")
                {
                    server.SslState = ""; // 重置后重新尝试签发
                }
            }
            if (req.ForceHttps != null) server.ForceHttps = req.ForceHttps == true;
            // 接入方式切换：cname ↔ ns。NS 托管时自动为站点域名创建委派型智能 DNS 记录（节点直接应答可用节点 IP，无 CNAME 中间层）
            if (req.AccessMode is "cname" or "ns" && req.AccessMode != server.AccessMode)
            {
                server.AccessMode = req.AccessMode;
                server.AccessModeChangedAt = DateTime.UtcNow;
                if (req.AccessMode == "ns")
                {
                    await AccessModeHelper.UpsertDelegationsAsync(db, server, uid!.Value);
                }
                else
                {
                    // 切回 CNAME：移除本站点的委派型解析记录
                    var dels = await db.NsDomains.Where(d => d.ServerId == server.Id && d.Kind == "delegation").ToListAsync();
                    db.NsDomains.RemoveRange(dels);
                }
            }
            await db.SaveChangesAsync();
            publishQueue.EnqueueUser(server.ClusterId, server.RealtimePublish);
            return Results.Ok(new { ok = true });
        });

        // 删除自己的站点
        grp.MapDelete("/servers/{id:long}", async (long id, CdnDbContext db, IConfiguration cfg, ConfigPublisher publisher, ConfigPublishQueue publishQueue, AuditService audit) =>
        {
            var uid = await UserCdnExtraEndpoints.UserIdAsyncPublic(cfg);
            if (uid == null) return Results.Json(new { code = 401, message = "未登录" });
            var server = await db.Servers.FindAsync(id);
            if (server == null || server.UserId != uid) return Results.Json(new { code = 404, message = "站点不存在" });
            db.Servers.Remove(server);
            // 站点删除时同步清理其委派型解析记录，避免孤儿数据
            var orphanDels = await db.NsDomains.Where(d => d.ServerId == id && d.Kind == "delegation").ToListAsync();
            db.NsDomains.RemoveRange(orphanDels);
            await db.SaveChangesAsync();
            publishQueue.EnqueueUser(server.ClusterId, server.RealtimePublish);
            _ = audit.LogAsync($"user.server.delete {id}", $"uid={uid}");
            return Results.Ok(new { ok = true });
        });
    }

    private static string[] ParseDomains(string json)
    {
        try { return System.Text.Json.JsonSerializer.Deserialize<string[]>(json) ?? []; }
        catch { return []; }
    }

    /// <summary>接入方式公共逻辑：为 NS 托管站点生成委派型 A 记录（各线路节点 IP，DNS 拉平无 CNAME）</summary>
    public static class AccessModeHelper
    {
        public static async Task UpsertDelegationsAsync(CdnDbContext db, Server server, long uid)
        {
            var nodes = await db.Nodes.Where(n => n.ClusterId == server.ClusterId && n.IsOnline && n.Role == "edge")
                .Select(n => new { n.PublicIps, n.Line }).ToListAsync();
            var lineIp = nodes.Where(n => n.PublicIps.Length > 0)
                .GroupBy(n => string.IsNullOrWhiteSpace(n.Line) ? "default" : n.Line)
                .ToDictionary(g => g.Key, g => g.First().PublicIps[0]);
            foreach (var domain in server.Domains)
            {
                foreach (var (line, ip) in lineIp)
                {
                    db.NsDomains.Add(new NsDomain
                    {
                        UserId = uid, Name = domain, Type = "A", Value = ip,
                        Ttl = 600, Line = line, Status = "active", Kind = "delegation",
                        ServerId = server.Id, SyncStatus = "synced", CreatedAt = DateTime.UtcNow,
                    });
                }
                // 无 default 线路节点时补一行兜底（取首个节点 IP），保证任何查询来源都有应答
                if (!lineIp.ContainsKey("default") && lineIp.Count > 0)
                {
                    db.NsDomains.Add(new NsDomain
                    {
                        UserId = uid, Name = domain, Type = "A", Value = lineIp.First().Value,
                        Ttl = 600, Line = "default", Status = "active", Kind = "delegation",
                        ServerId = server.Id, SyncStatus = "synced", CreatedAt = DateTime.UtcNow,
                    });
                }
            }
        }
    }

    public record UserServerRequest(
        List<string>? Domains,
        string? Region,
        string? ServerType,
        int[]? HttpPorts,
        int[]? HttpsPorts,
        bool ForceHttps,
        string? SslMode,
        string? OriginScheme,
        string? OriginHost,
        int? OriginPort,
        bool? CacheEnabled,
        string? IcpNumber,
        string? AccessMode);
}
