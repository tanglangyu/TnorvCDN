using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using TnorvCdn.Api.Core;
using TnorvCdn.Api.Data;
using TnorvCdn.Api.Services;

namespace TnorvCdn.Api.Endpoints;

/// <summary>
/// 用户端单站设置端点通用设计：
/// 与管理端分层配置同构，但每个端点做归属校验（仅本人站点可读写）。
/// 裁剪项：路由规则/流量限制/其他设置为管理员专属；分组/套餐为用户端特有（见对应端点）。
/// </summary>
public static class UserServerSettingsEndpoints
{
    public static void MapUserServerSettingsEndpoints(this WebApplication app)
    {
        var grp = app.MapGroup("/api/user").RequireAuthorization();

        async Task<Server?> OwnServerAsync(CdnDbContext db, IConfiguration cfg, long id)
        {
            var uid = await UserIdAsync(cfg);
            if (uid == null) return null;
            return await db.Servers.FirstOrDefaultAsync(s => s.Id == id && s.UserId == uid.Value);
        }

        // 分层配置读取
        grp.MapGet("/servers/{id:long}/settings", async (long id, CdnDbContext db, IConfiguration cfg) =>
        {
            var s = await OwnServerAsync(db, cfg, id);
            if (s == null) return Results.NotFound();
            return Results.Ok(new
            {
                s.Id, s.Domains, s.ClusterId, s.UserId,
                s.SslMode, s.ForceHttps, s.HttpsPorts, s.HttpPorts, s.ServerType, s.Enabled,
                origin = s.Origin,
                cache = s.Cache,
                security = s.Security,
                advanced = s.Advanced,
                sslState = s.SslState, sslExpiresAt = s.SslExpiresAt,
                sslIssuedDomains = s.SslIssuedDomains, sslLastError = s.SslLastError,
            });
        });

        // HTTPS
        grp.MapPut("/servers/{id:long}/ssl", async (long id, SslSettingsRequest req, CdnDbContext db, IConfiguration cfg, ConfigPublishQueue pq) =>
        {
            var s = await OwnServerAsync(db, cfg, id);
            if (s == null) return Results.NotFound();
            var uidSsl = await UserCdnExtraEndpoints.UserIdAsyncPublic(cfg);
            if (req.SslMode != "off")
            {
                var (okSsl, msgSsl) = await Services.PlanGate.AllowAsync(db, uidSsl!.Value, "https");
                if (!okSsl) return Results.Json(new { code = 403, message = msgSsl });
            }
            s.SslMode = req.SslMode is "auto" or "manual" or "off" ? req.SslMode : "auto";
            s.ForceHttps = req.ForceHttps;
            s.HttpsPorts = s.SslMode == "off" ? [] : (req.HttpsPorts is { Length: > 0 } ? req.HttpsPorts : [443]);
            await db.SaveChangesAsync();
            pq.EnqueueUser(s.ClusterId, s.RealtimePublish);
            return Results.Ok(new { ok = true });
        });

        // 缓存
        grp.MapPut("/servers/{id:long}/cache", async (long id, CacheConfig input, CdnDbContext db, IConfiguration cfg, ConfigPublishQueue pq) =>
        {
            var s = await OwnServerAsync(db, cfg, id);
            if (s == null) return Results.NotFound();
            var uidCache = await UserCdnExtraEndpoints.UserIdAsyncPublic(cfg);
            if (input.Enabled)
            {
                var (okCache, msgCache) = await Services.PlanGate.AllowAsync(db, uidCache!.Value, "cache");
                if (!okCache) return Results.Json(new { code = 403, message = msgCache });
            }
            s.Cache = input;
            await db.SaveChangesAsync();
            pq.EnqueueUser(s.ClusterId, s.RealtimePublish);
            return Results.Ok(new { ok = true });
        });

        // 安全
        grp.MapPut("/servers/{id:long}/security", async (long id, SecurityConfig input, CdnDbContext db, IConfiguration cfg, ConfigPublishQueue pq) =>
        {
            var s = await OwnServerAsync(db, cfg, id);
            if (s == null) return Results.NotFound();
            var uidSec = await UserCdnExtraEndpoints.UserIdAsyncPublic(cfg);
            foreach (var (key, used) in new (string key, bool used)[]
            {
                ("waf", input.WafRules.Count > 0 || input.IpBlacklist.Count > 0 || input.UaRules.Count > 0),
                ("cc", input.Cc.Enabled),
                ("jsShield", input.JsShield.Enabled),
                ("urlAuth", input.UrlAuth.Enabled),
                ("referers", input.Referers.Enabled),
                ("aiGuard", input.AiGuard.Enabled),
                ("edgeScripts", input.EdgeScripts.Count > 0),
            })
            {
                if (!used) continue;
                var (okFeat, msgFeat) = await Services.PlanGate.AllowAsync(db, uidSec!.Value, key);
                if (!okFeat) return Results.Json(new { code = 403, message = msgFeat });
            }
            s.Security = input;
            await db.SaveChangesAsync();
            pq.EnqueueUser(s.ClusterId, s.RealtimePublish);
            return Results.Ok(new { ok = true });
        });

        // 高级
        grp.MapPut("/servers/{id:long}/advanced", async (long id, AdvancedConfig input, CdnDbContext db, IConfiguration cfg, ConfigPublishQueue pq) =>
        {
            var s = await OwnServerAsync(db, cfg, id);
            if (s == null) return Results.NotFound();
            var uidAdv = await UserCdnExtraEndpoints.UserIdAsyncPublic(cfg);
            var advFeatUsed = new (string key, bool used)[]
            {
                ("customPages", input.CustomPages != null && (input.CustomPages.NotFound != "" || input.CustomPages.ServerError != "" || input.CustomPages.Blocked != "")),
                ("webp", input.WebpOptimization),
                ("websocket", input.Websocket),
                ("grpc", input.Grpc),
                ("basicAuth", input.BasicAuth is { Enabled: true }),
                ("compression", input.Compression),
            };
            foreach (var (key, used) in advFeatUsed)
            {
                if (!used) continue;
                var (okFeat, msgFeat) = await Services.PlanGate.AllowAsync(db, uidAdv!.Value, key);
                if (!okFeat) return Results.Json(new { code = 403, message = msgFeat });
            }
            foreach (var u in input.BasicAuth?.Users ?? []) u.PasswordHash = SHA256(u.Plain ?? "");
            s.Advanced = input;
            await db.SaveChangesAsync();
            pq.EnqueueUser(s.ClusterId, s.RealtimePublish);
            return Results.Ok(new { ok = true });
        });

        // DNS 解析检测
        grp.MapGet("/servers/{id:long}/dns-check", async (long id, CdnDbContext db, IConfiguration cfg) =>
        {
            var s = await OwnServerAsync(db, cfg, id);
            if (s == null) return Results.NotFound();
            var domain = s.Domains.FirstOrDefault(d => !d.StartsWith('*')) ?? s.Domains.FirstOrDefault();
            if (domain == null) return Results.Ok(new { ok = false, error = "站点未绑定域名" });
            var clusterDns = await db.Clusters.Where(c => c.Id == s.ClusterId).Select(c => c.DnsDomain).FirstOrDefaultAsync() ?? "";
            var result = new List<object>();
            try
            {
                var ips = await System.Net.Dns.GetHostAddressesAsync(domain);
                foreach (var ip in ips.Take(5)) result.Add(new { type = "A", value = ip.ToString() });
            }
            catch (Exception ex)
            {
                return Results.Ok(new { ok = false, domain, error = "解析失败: " + ex.Message });
            }
            var pointed = false;
            if (clusterDns != "")
            {
                try
                {
                    var caddrs = await System.Net.Dns.GetHostAddressesAsync(clusterDns);
                    var cips = caddrs.Select(a => a.ToString()).ToHashSet();
                    pointed = result.Any(r => cips.Contains((string)((dynamic)r).value));
                }
                catch { }
            }
            return Results.Ok(new { ok = true, domain, clusterDns, records = result, pointsToCluster = pointed });
        });

        // 我的套餐（用户端特有子页）
        grp.MapGet("/my-plan", async (CdnDbContext db, IConfiguration cfg) =>
        {
            var uid = await UserIdAsync(cfg);
            if (uid == null) return Results.Unauthorized();
            var now = DateTime.UtcNow;
            var subs = await db.Subscriptions.Where(x => x.UserId == uid.Value && x.Enabled && x.ExpiresAt > now)
                .OrderByDescending(x => x.ExpiresAt).ToListAsync();
            var planIds = subs.Select(x => x.PlanId).ToList();
            var plans = await db.Plans.Where(p => planIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id);
            var monthStart = new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc);
            var myServers = await db.Servers.Where(s => s.UserId == uid.Value).Select(s => s.Id).ToListAsync();
            var monthBytes = await db.AccessLogs.Where(l => l.Timestamp >= monthStart && myServers.Contains(l.ServerId))
                .SumAsync(l => (long?)l.BytesSent) ?? 0;
            return Results.Ok(new
            {
                plans = subs.Select(x => new
                {
                    planName = plans.TryGetValue(x.PlanId, out var p) ? p.Name : "套餐#" + x.PlanId,
                    maxServers = plans.TryGetValue(x.PlanId, out var p2) ? p2.MaxServers : 0,
                    monthlyTrafficGB = plans.TryGetValue(x.PlanId, out var p3) ? p3.MonthlyTrafficMB / 1024.0 : 0,
                    expiresAt = x.ExpiresAt,
                }),
                usage = new { servers = myServers.Count, monthTrafficGB = Math.Round(monthBytes / 1073741824.0, 2) },
                features = await Services.PlanGate.MyFeaturesAsync(db, uid.Value), // 功能开关/配额矩阵（前端据此禁用 UI）
            });
        });

        // 站点分组（用户端特有：按域名后缀自定义分组标签，存于站点 Group 字段）
        grp.MapPut("/servers/{id:long}/group", async (long id, GroupRequest req, CdnDbContext db, IConfiguration cfg) =>
        {
            var s = await OwnServerAsync(db, cfg, id);
            if (s == null) return Results.NotFound();
            s.Group = req.Group ?? "";
            await db.SaveChangesAsync();
            return Results.Ok(new { ok = true });
        });

        // 站点启停（U2 卡片视图行内开关；停用仅下发配置不删除数据，与 PUT 整体替换语义解耦）
        grp.MapPut("/servers/{id:long}/enabled", async (long id, EnabledRequest req, CdnDbContext db, IConfiguration cfg, ConfigPublishQueue pq, DnsService dns) =>
        {
            var s = await OwnServerAsync(db, cfg, id);
            if (s == null) return Results.NotFound();
            s.Enabled = req.Enabled == true;
            await db.SaveChangesAsync();
            pq.EnqueueUser(s.ClusterId, s.RealtimePublish);
            _ = dns.SyncSiteDomainsAsync(s.ClusterId, s.Domains);
            return Results.Ok(new { ok = true });
        });

        // 证书续期（归属校验版，对齐管理员 ssl/renew 语义）
        grp.MapPost("/servers/{id:long}/ssl/renew", async (long id, CdnDbContext db, IConfiguration cfg, ConfigPublishQueue pq) =>
        {
            var s = await OwnServerAsync(db, cfg, id);
            if (s == null) return Results.NotFound();
            if (s.SslMode != "auto") return Results.BadRequest("仅自动签发（ACME）模式的网站支持发起续期");
            if (s.SslRenewRequestedAt != null && DateTime.UtcNow - s.SslRenewRequestedAt < TimeSpan.FromMinutes(10))
                return Results.BadRequest("已有续期请求正在执行中，请等待完成或超时后再试");
            if (s.HttpsPorts.Length == 0) s.HttpsPorts = [443];
            s.SslRenewRequestedAt = DateTime.UtcNow;
            await db.SaveChangesAsync();
            pq.EnqueueUser(s.ClusterId, s.RealtimePublish);
            return Results.Ok(new { ok = true });
        });

        // 复制配置通用设计
        grp.MapPost("/servers/{id:long}/copy-from", async (long id, CopyFromUserRequest req, CdnDbContext db, IConfiguration cfg, ConfigPublishQueue pq) =>
        {
            var target = await OwnServerAsync(db, cfg, id);
            if (target == null) return Results.NotFound();
            var uid = await UserIdAsync(cfg);
            var source = await db.Servers.FirstOrDefaultAsync(s => s.Id == req.FromId && s.UserId == uid.Value);
            if (source == null) return Results.NotFound("来源站点不存在或非本人站点");
            if (req.FromId == id) return Results.BadRequest("不能从自身复制");
            var parts = (req.Parts ?? ["cache", "security", "advanced"]).ToHashSet();
            if (parts.Contains("cache")) target.Cache = source.Cache;
            if (parts.Contains("security")) target.Security = source.Security;
            if (parts.Contains("advanced")) target.Advanced = source.Advanced;
            if (parts.Contains("locations")) target.Locations = source.Locations;
            if (parts.Contains("origin")) target.Origin = source.Origin;
            if (parts.Contains("https"))
            {
                target.SslMode = source.SslMode;
                target.ForceHttps = source.ForceHttps;
                target.HttpsPorts = source.HttpsPorts;
            }
            await db.SaveChangesAsync();
            pq.EnqueueUser(target.ClusterId, target.RealtimePublish);
            return Results.Ok(new { ok = true });
        });

        // 路由规则 locations（用户端可管理自己站点的 location 覆盖）
        grp.MapGet("/servers/{id:long}/locations", async (long id, CdnDbContext db, IConfiguration cfg) =>
        {
            var s = await OwnServerAsync(db, cfg, id);
            if (s == null) return Results.NotFound();
            return Results.Ok(s.Locations);
        });
        grp.MapPut("/servers/{id:long}/locations", async (long id, List<LocationRule> input, CdnDbContext db, IConfiguration cfg, ConfigPublishQueue pq) =>
        {
            var s = await OwnServerAsync(db, cfg, id);
            if (s == null) return Results.NotFound();
            var clean = input.Where(r => !string.IsNullOrWhiteSpace(r.PathPrefix)).ToList();
            if (clean.Select(r => r.PathPrefix).Distinct().Count() != clean.Count)
                return Results.BadRequest("路径前缀重复");
            var uidLoc = await UserCdnExtraEndpoints.UserIdAsyncPublic(cfg);
            if (clean.Count > 0)
            {
                var (okLoc, msgLoc) = await Services.PlanGate.AllowAsync(db, uidLoc!.Value, "locations");
                if (!okLoc) return Results.Json(new { code = 403, message = msgLoc });
                var maxLoc = await Services.PlanGate.QuotaAsync(db, uidLoc.Value, "maxLocations");
                if (maxLoc is > 0 && clean.Count > maxLoc) return Results.Json(new { code = 403, message = $"路径规则数超出套餐配额（{maxLoc} 条）" });
            }
            s.Locations = clean;
            await db.SaveChangesAsync();
            pq.EnqueueUser(s.ClusterId, s.RealtimePublish);
            return Results.Ok(new { ok = true, count = clean.Count });
        });

        // 费用计算器通用设计
        grp.MapGet("/fee/calculator", async (CdnDbContext db, IConfiguration cfg, int? gb, long? requests) =>
        {
            var uid = await UserIdAsync(cfg);
            if (uid == null) return Results.Unauthorized();
            // 计费基准：用户当前套餐单价；无套餐时按平台默认单价
            var now = DateTime.UtcNow;
            var sub = await db.Subscriptions.Where(x => x.UserId == uid.Value && x.Enabled && x.ExpiresAt > now)
                .OrderByDescending(x => x.ExpiresAt).FirstOrDefaultAsync();
            decimal pricePerGB = 0.20m;     // 默认 ¥0.20/GB
            decimal pricePer10kReq = 0.02m; // 默认 ¥0.02/万次请求
            if (sub != null)
            {
                var plan = await db.Plans.FindAsync(sub.PlanId);
                if (plan != null && plan.MonthlyTrafficMB > 0)
                {
                    // 套餐折算单价：月价 / 套餐流量（低至 0.08/GB 下限）
                    var implied = plan.PriceCents / 100m / (plan.MonthlyTrafficMB / 1024m);
                    if (implied > 0.08m) pricePerGB = Math.Round(implied, 3);
                }
            }
            var gbVal = Math.Max(0, gb ?? 0);
            var reqVal = Math.Max(0, requests ?? 0);
            var trafficCost = Math.Round(pricePerGB * gbVal, 2);
            var requestCost = Math.Round(pricePer10kReq * (reqVal / 10000m), 2);
            return Results.Ok(new
            {
                pricePerGB, pricePer10kReq,
                hasPlan = sub != null,
                estimate = new { gb = gbVal, requests = reqVal, trafficCost, requestCost, total = trafficCost + requestCost },
            });
        });

        // 公告（用户端：上架中的公告列表 + 弹窗）
        grp.MapGet("/posts", async (CdnDbContext db) =>
        {
            var list = await db.Posts.Where(p => p.Enabled && (p.Audience == "user" || p.Audience == "all"))
                .OrderByDescending(p => p.CreatedAt).Take(20)
                .Select(p => new { p.Id, p.Title, p.Content, p.Popup, p.CreatedAt }).ToListAsync();
            return Results.Ok(list);
        });

        // 实名认证通用设计：查看我的认证状态 + 提交
        grp.MapGet("/identity", async (CdnDbContext db, IConfiguration cfg) =>
        {
            var uid = await UserIdAsync(cfg);
            if (uid == null) return Results.Unauthorized();
            var i = await db.UserIdentities.Where(x => x.UserId == uid.Value)
                .OrderByDescending(x => x.CreatedAt).FirstOrDefaultAsync();
            return Results.Ok(i == null ? null : new { i.Type, i.RealName, i.EnterpriseName, i.Status, i.Note, i.CreatedAt });
        });
        grp.MapPost("/identity", async (IdentitySubmitRequest req, CdnDbContext db, IConfiguration cfg) =>
        {
            var uid = await UserIdAsync(cfg);
            if (uid == null) return Results.Unauthorized();
            if (string.IsNullOrWhiteSpace(req.RealName) || string.IsNullOrWhiteSpace(req.IdNumber))
                return Results.BadRequest("姓名与证件号必填");
            var existing = await db.UserIdentities
                .Where(x => x.UserId == uid.Value && (x.Status == "pending" || x.Status == "approved"))
                .AnyAsync();
            if (existing) return Results.BadRequest("已存在待审核或已通过的认证");
            var i = new UserIdentity
            {
                UserId = uid.Value,
                Type = req.Type == "enterprise" ? "enterprise" : "personal",
                RealName = req.RealName.Trim(),
                IdNumber = req.IdNumber.Trim(),
                EnterpriseName = req.EnterpriseName ?? "",
                Status = "pending",
                CreatedAt = DateTime.UtcNow,
            };
            db.UserIdentities.Add(i);
            await db.SaveChangesAsync();
            return Results.Ok(new { ok = true, id = i.Id });
        });
    }

    public record GroupRequest(string? Group);
    public record EnabledRequest(bool? Enabled);
    public record CopyFromUserRequest(long FromId, List<string>? Parts);
    public record IdentitySubmitRequest(string Type, string RealName, string IdNumber, string? EnterpriseName);

    private static string SHA256(string text)
    {
        var bytes = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(text));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    /// <summary>读用户功能开关（User.Features JSON；未配置/字段缺失=允许）。
    /// 通用设计
    internal static async Task<bool> FeatureEnabledAsync(CdnDbContext db, long userId, string feature)
    {
        var u = await db.Users.AsNoTracking().FirstOrDefaultAsync(x => x.Id == userId);
        if (u == null || string.IsNullOrEmpty(u.Features)) return true;
        try
        {
            var doc = System.Text.Json.JsonDocument.Parse(u.Features);
            return !doc.RootElement.TryGetProperty(feature, out var v) || v.ValueKind != System.Text.Json.JsonValueKind.False;
        }
        catch
        {
            return true;
        }
    }

    private static Task<long?> UserIdAsync(IConfiguration cfg)
    {
        var http = new IHttpContextAccessorShim().Current;
        var header = http?.Request.Headers.Authorization.FirstOrDefault();
        if (string.IsNullOrEmpty(header) || !header.StartsWith("Bearer ")) return null;
        try
        {
            var key = new Microsoft.IdentityModel.Tokens.SymmetricSecurityKey(
                System.Text.Encoding.UTF8.GetBytes(cfg["Jwt:Key"] ?? "dev-only-secret-change-me-32bytes!!"));
            var principal = new System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler().ValidateToken(
                header["Bearer ".Length..].Trim(),
                new Microsoft.IdentityModel.Tokens.TokenValidationParameters
                {
                    ValidateIssuer = true, ValidIssuer = "tnorv",
                    ValidateAudience = true, ValidAudience = "tnorv",
                    IssuerSigningKey = key, ValidateLifetime = true,
                }, out _);
            var raw = principal.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            return Task.FromResult<long?>(long.TryParse(raw, out var id) ? id : null);
        }
        catch { return Task.FromResult<long?>(null); }
    }

    /// <summary>轻量 HttpContext 访问器（避免依赖注 IHttpContextAccessor 注册差异）。</summary>
    private sealed class IHttpContextAccessorShim
    {
        public Microsoft.AspNetCore.Http.HttpContext? Current => new HttpContextAccessor().HttpContext;
    }
}
