using System.Text.Json;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using TnorvCdn.Api.Data;
using TnorvCdn.Api.Core;
using TnorvCdn.Api.Services;


namespace TnorvCdn.Api.Endpoints;

public static class AdminEndpointJson
{
    public static readonly System.Text.Json.JsonSerializerOptions JsonOpts = new(System.Text.Json.JsonSerializerDefaults.Web);
}

/// <summary>
/// 管理 REST API（Minimal API 分组）。
/// 涵盖 P0 主控端：认证 / 用户 / 集群 / 节点 / 网站（域名、源站、缓存、安全）/ 日志查询。
/// </summary>
public static class AdminEndpoints
{
    public static void MapAdminEndpoints(this WebApplication app)
    {
        var api = app.MapGroup("/api");
        var auth = api.MapGroup("/auth");

        // ---- 认证 ----
        auth.MapPost("/login", async (HttpRequest httpReq, CdnDbContext db, IConfiguration cfg, Services.SmsService sms) =>
        {
            // 双协议统一登录：{username,password}（原生管理端）与 {userName,password}（RapidDev 控制台）
            // 以及手机号验证码登录：{phone, smsCode, loginType:1}
            string? username = null, password = null, phone = null, smsCode = null;
            int? loginType = null;
            try
            {
                using var doc = await System.Text.Json.JsonDocument.ParseAsync(httpReq.Body);
                var root = doc.RootElement;
                if (root.TryGetProperty("username", out var u1)) username = u1.GetString();
                if (root.TryGetProperty("userName", out var u2)) username ??= u2.GetString();
                if (root.TryGetProperty("password", out var p1)) password = p1.GetString();
                if (root.TryGetProperty("phone", out var ph)) phone = ph.GetString();
                if (root.TryGetProperty("smsCode", out var sc)) smsCode = sc.GetString();
                if (root.TryGetProperty("loginType", out var lt) && lt.TryGetInt32(out var lti)) loginType = lti;
            }
            catch { }

            User? user;
            if (loginType == 1 || (!string.IsNullOrEmpty(phone) && !string.IsNullOrEmpty(smsCode)))
            {
                // 手机号验证码登录：凭绑定手机号 + 短信验证码免密登录
                if (string.IsNullOrEmpty(phone) || string.IsNullOrEmpty(smsCode))
                    return Results.Json(new { code = 400, message = "请输入手机号和验证码", data = (object?)null }, statusCode: 200);
                var normalizedPhone = Services.PhoneUtil.Normalize(phone);
                if (!sms.VerifyCode(normalizedPhone, "phone-login", smsCode))
                    return Results.Json(new { code = 400, message = "验证码错误或已过期", data = (object?)null }, statusCode: 200);
                user = await db.Users.FirstOrDefaultAsync(u => u.SmsPhone == normalizedPhone && u.Enabled);
                if (user == null)
                    return Results.Json(new { code = 400, message = "该手机号未绑定任何账号", data = (object?)null }, statusCode: 200);
            }
            else
            {
                LoginRequest req = new LoginRequest(username, password);
                user = await db.Users.FirstOrDefaultAsync(u => u.Username == username && u.Enabled);
                if (user == null || !BCrypt.Net.BCrypt.Verify(req.Password, user.PasswordHash))
                    return Results.Json(new { code = 400, message = "用户名或密码错误", data = (object?)null }, statusCode: 200);
            }

            var key = new SymmetricSecurityKey(
                Encoding.UTF8.GetBytes(cfg["Jwt:Key"] ?? "dev-only-secret-change-me-32bytes!!"));
            var claims = new List<Claim>
            {
                new(ClaimTypes.NameIdentifier, user.Id.ToString()),
                new(ClaimTypes.Role, user.IsAdmin ? "admin" : "user")
            };
            var expires = DateTime.UtcNow.AddHours(8);
            var token = new JwtSecurityToken(
                issuer: "tnorv", audience: "tnorv",
                claims: claims,
                expires: expires,
                signingCredentials: new SigningCredentials(key, SecurityAlgorithms.HmacSha256));
            var jwt = new JwtSecurityTokenHandler().WriteToken(token);
            var expiresAt = new DateTimeOffset(expires).ToUnixTimeSeconds();
            var sessionId = Guid.NewGuid().ToString("N");
            await SessionEndpoints.RegisterAsync(db, user.Id, sessionId, httpReq); // 登录设备登记

            // 统一响应：同时携带原生字段（token/user）与 RapidDev 字段（accessToken/refreshToken/expiresAt/sessionId），
            // 两类前端都能取到所需字段；外层再加 {code,message} 包装（原生端消费者忽略）
            return Results.Ok(new
            {
                code = 200,
                message = "操作成功",
                token = jwt,
                user = new { id = user.Id, name = user.Username, isAdmin = user.IsAdmin },
                data = new
                {
                    accessToken = jwt,
                    refreshToken = jwt,
                    expiresAt,
                    sessionId,
                },
            });
        });

        var admin = api.MapGroup("/admin").RequireAuthorization();
        var users = admin.MapGroup("/users");
        var clusters = admin.MapGroup("/clusters");
        var nodes = admin.MapGroup("/nodes");
        var servers = admin.MapGroup("/servers");
        var logs = admin.MapGroup("/logs");

        // ---- 用户管理 ----
        // 列表：分页 + 关键词模糊（q=用户名/手机号）；不传 page 时返回全量数组（老前端兼容）
        users.MapGet("", async (CdnDbContext db, int? page, int? pageSize, string? q) =>
        {
            var kw = q?.ToLower();
            var query = db.Users.AsNoTracking()
                .Where(u => kw == null || kw == "" || u.Username.ToLower().Contains(kw) || u.SmsPhone.Contains(q!))
                .OrderByDescending(u => u.Id);
            var serverCounts = await db.Servers.AsNoTracking().GroupBy(s => s.UserId)
                .Select(g => new { g.Key, Cnt = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.Cnt);
            var sessionCounts = await db.UserSessions.AsNoTracking().GroupBy(s => s.UserId)
                .Select(g => new { g.Key, Cnt = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.Cnt);
            var shaped = query.Select(u => new
            {
                u.Id, u.Username, u.IsAdmin, u.Enabled, u.CreatedAt, u.BalanceCents, u.Features,
                smsPhone = u.SmsPhone == "" ? "" : Services.PhoneUtil.Mask(u.SmsPhone),
                serverCount = (int?)serverCounts.GetValueOrDefault(u.Id, 0),
                sessionCount = (int?)sessionCounts.GetValueOrDefault(u.Id, 0),
            });
            if (page == null && pageSize == null)
                return Results.Ok(await shaped.ToListAsync());
            var p = Math.Clamp(page ?? 1, 1, 1000);
            var ps = Math.Clamp(pageSize ?? 20, 1, 200);
            var total = await query.CountAsync();
            var items = await shaped.Skip((p - 1) * ps).Take(ps).ToListAsync();
            return Results.Ok(new { total, page = p, pageSize = ps, items });
        });

        users.MapPost("", async (CreateUserRequest req, CdnDbContext db, AuditService audit) =>
        {
            if (await db.Users.AnyAsync(u => u.Username == req.Username))
                return Results.Conflict("用户名已存在");
            var user = new User
            {
                Username = req.Username,
                PasswordHash = BCrypt.Net.BCrypt.HashPassword(req.Password),
                IsAdmin = req.IsAdmin
            };
            db.Users.Add(user);
            await db.SaveChangesAsync();
            _ = audit.LogAsync("users.post ", "ok");
            return Results.Created($"/api/admin/users/{user.Id}", new { user.Id, user.Username });
        });

        users.MapPut("/{id:long}", async (long id, HttpRequest http, CdnDbContext db, AuditService audit) =>
        {
            var user = await db.Users.FindAsync(id);
            if (user == null) return Results.NotFound();
            using var doc = await System.Text.Json.JsonDocument.ParseAsync(http.Body);
            var root = doc.RootElement;
            if (root.TryGetProperty("password", out var pw) && pw.GetString() is { Length: > 0 } np)
                user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(np);
            if (root.TryGetProperty("enabled", out var en) && en.ValueKind is System.Text.Json.JsonValueKind.True or System.Text.Json.JsonValueKind.False)
                user.Enabled = en.ValueKind == System.Text.Json.JsonValueKind.True;
            if (root.TryGetProperty("isAdmin", out var ia) && ia.ValueKind is System.Text.Json.JsonValueKind.True or System.Text.Json.JsonValueKind.False)
                user.IsAdmin = ia.ValueKind == System.Text.Json.JsonValueKind.True;
            // 手机号（管理员直绑/清空）：归一化校验 + 唯一性
            if (root.TryGetProperty("smsPhone", out var sp))
            {
                var mobile = Services.PhoneUtil.Normalize(sp.GetString());
                if (mobile == "") user.SmsPhone = "";
                else if (!Services.PhoneUtil.IsValid(mobile))
                    return Results.BadRequest("手机号格式不正确");
                else
                {
                    if (await db.Users.AnyAsync(x => x.Id != id && x.SmsPhone == mobile))
                        return Results.Conflict("该手机号已被其他账号绑定");
                    user.SmsPhone = mobile;
                }
            }
            // 用户级功能开关：null=跟随套餐；对象=显式覆盖
            if (root.TryGetProperty("features", out var ft))
                user.Features = ft.ValueKind == System.Text.Json.JsonValueKind.Null ? null : ft.GetRawText();
            await db.SaveChangesAsync();
            _ = audit.LogAsync($"users.put /{id}", "ok");
            return Results.Ok();
        });

        // 用户全量详情（/cdn/users 行内「详情」抽屉）
        users.MapGet("/{id:long}/detail", async (long id, CdnDbContext db) =>
        {
            var u = await db.Users.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
            if (u == null) return Results.NotFound();
            var identity = await db.UserIdentities.AsNoTracking().FirstOrDefaultAsync(x => x.UserId == id);
            var subs = await db.Subscriptions.Where(x => x.UserId == id).OrderByDescending(x => x.ExpiresAt).Take(10).ToListAsync();
            var planIds = subs.Select(x => x.PlanId).ToList();
            var plans = await db.Plans.Where(pl => planIds.Contains(pl.Id)).ToDictionaryAsync(pl => pl.Id);
            var servers = await db.Servers.Where(s => s.UserId == id)
                .Select(s => new { s.Id, s.Domains, s.Enabled, s.ServerType, status = s.SslState, s.CreatedAt }).ToListAsync();
            var sessions = await db.UserSessions.Where(x => x.UserId == id).OrderByDescending(x => x.LastSeenAt).Take(20).ToListAsync();
            var smsLogs = await db.SmsLogs.Where(x => x.UserId == id).OrderByDescending(x => x.CreatedAt).Take(20).ToListAsync();
            var orders = await db.Orders.Where(x => x.UserId == id).OrderByDescending(x => x.CreatedAt).Take(10).ToListAsync();
            var tickets = await db.Tickets.Where(x => x.UserId == id).ToListAsync();
            return Results.Ok(new
            {
                u.Id, u.Username, u.IsAdmin, u.Enabled, u.BalanceCents, u.Features, u.CreatedAt,
                smsPhone = u.SmsPhone == "" ? "" : Services.PhoneUtil.Mask(u.SmsPhone),
                smsEvents = u.SmsEvents,
                identity = identity == null ? null : new { identity.Type, identity.RealName, identity.EnterpriseName, identity.Status, identity.CreatedAt },
                subscriptions = subs.Select(x => new
                {
                    planName = plans.TryGetValue(x.PlanId, out var p1) ? p1.Name : "套餐#" + x.PlanId,
                    x.Enabled, x.AutoRenew, x.StartsAt, x.ExpiresAt,
                }),
                servers, sessions = sessions.Select(s => new { s.SessionId, s.Ip, device = s.UserAgent.Length > 60 ? s.UserAgent[..60] : s.UserAgent, s.CreatedAt, s.LastSeenAt }),
                smsLogs = smsLogs.Select(l => new { l.Event, l.Mobile, l.Status, l.Error, l.CreatedAt }),
                orders = orders.Select(o => new { o.Id, o.OrderNo, o.Kind, o.AmountCents, o.Status, o.CreatedAt }),
                ticketCount = tickets.Count,
                openTickets = tickets.Count(t => t.Status != "closed"),
            });
        });

        // 强制下线：删除全部登录会话并推送踢出
        users.MapPost("/{id:long}/force-logout", async (long id, CdnDbContext db, IHubContext<NotificationHub> hub, AuditService audit) =>
        {
            var all = await db.UserSessions.Where(x => x.UserId == id).ToListAsync();
            db.UserSessions.RemoveRange(all);
            await db.SaveChangesAsync();
            foreach (var s in all)
                await hub.Clients.Group("user:" + id).SendAsync("session_kicked",
                    System.Text.Json.JsonSerializer.Serialize(new { type = "session_kicked", title = "会话已下线", content = "您的账号已被管理员强制下线", sessionId = s.SessionId }));
            _ = audit.LogAsync($"users.force-logout /{id}", $"revoked={all.Count}");
            return Results.Ok(new { ok = true, revoked = all.Count });
        });

        // 该用户的短信日志
        users.MapGet("/{id:long}/sms-logs", async (long id, CdnDbContext db, int page = 1, int pageSize = 20) =>
        {
            var u = await db.Users.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
            if (u == null) return Results.NotFound();
            var q = db.SmsLogs.AsNoTracking().Where(l => l.UserId == id || (u.SmsPhone != "" && l.Mobile == u.SmsPhone));
            var p = Math.Clamp(page, 1, 1000);
            var ps = Math.Clamp(pageSize, 1, 100);
            var total = await q.CountAsync();
            var items = await q.OrderByDescending(l => l.CreatedAt).Skip((p - 1) * ps).Take(ps)
                .Select(l => new { l.Event, l.Mobile, l.Status, l.Error, l.CreatedAt }).ToListAsync();
            return Results.Ok(new { total, items });
        });

        users.MapDelete("/{id:long}", async (long id, CdnDbContext db, AuditService audit) =>
        {
            var user = await db.Users.FindAsync(id);
            if (user == null) return Results.NotFound();
            if (user.Username == "admin") return Results.BadRequest("不允许删除内置管理员");
            db.Users.Remove(user);
            await db.SaveChangesAsync();
            _ = audit.LogAsync("users.delete /{id:long}", "ok");
            return Results.Ok();
        });

        // ---- 集群 ----
        clusters.MapGet("", async (CdnDbContext db, int? page, int? pageSize, string? q) =>
        {
            var query = db.Clusters.AsNoTracking()
                .Where(c => q == null || q == "" || c.Name.ToLower().Contains(q.ToLower()))
                .OrderByDescending(c => c.Id);
            List<Cluster> items;
            int total;
            if (page == null && pageSize == null) { items = await query.ToListAsync(); total = items.Count; }
            else
            {
                var p = Math.Clamp(page ?? 1, 1, 1000);
                var ps = Math.Clamp(pageSize ?? 20, 1, 200);
                total = await query.CountAsync();
                items = await query.Skip((p - 1) * ps).Take(ps).ToListAsync();
            }
            // A6：内联节点实时指标（免跳转）
            var clusterIds = items.Select(c => c.Id).ToList();
            var nodes = await db.Nodes.Where(n => clusterIds.Contains(n.ClusterId))
                .Select(n => new { n.Id, n.ClusterId, n.Name, n.IsOnline }).ToListAsync();
            var fresh = Services.NodeMetricsRegistry.Fresh().ToDictionary(x => x.NodeId, x => x.E);
            var metrics = nodes.GroupBy(n => n.ClusterId).ToDictionary(g => g.Key, g => g.Select(n =>
            {
                var cpu = 0d; var mem = 0d; var conns = 0L;
                if (fresh.TryGetValue(n.Id, out var e) && e != null)
                {
                    cpu = e.CpuUsage; mem = e.MemUsage; conns = e.Connections;
                }
                return new
                {
                    n.Id, n.Name, n.IsOnline,
                    cpu, mem, conns,
                };
            }).ToList());
            var shaped = items.Select(c => new
            {
                c.Id, c.Name, c.DnsDomain, c.ConfigVersion, c.CreatedAt,
                nodes = (object)(metrics.TryGetValue(c.Id, out var ms) ? ms : new List<object>()),
            }).ToList();
            if (page == null && pageSize == null) return Results.Ok(shaped);
            var p2 = Math.Clamp(page ?? 1, 1, 1000);
            return Results.Ok(new { total, page = p2, pageSize = Math.Clamp(pageSize ?? 20, 1, 200), items = shaped });
        });

        clusters.MapPost("", async (Cluster req, CdnDbContext db, AuditService audit) =>
        {
            if (string.IsNullOrWhiteSpace(req.Name)) return Results.BadRequest("名称必填");
            db.Clusters.Add(req);
            await db.SaveChangesAsync();
            _ = audit.LogAsync("clusters.post ", "ok");
            return Results.Created($"/api/admin/clusters/{req.Id}", req);
        });

        clusters.MapPut("/{id:long}", async (long id, Cluster input, CdnDbContext db, DnsService dns, ConfigPublisher publisher, ConfigPublishQueue publishQueue, AuditService audit) =>
        {
            var cluster = await db.Clusters.FindAsync(id);
            if (cluster == null) return Results.NotFound();
            cluster.Name = input.Name;
            cluster.DnsDomain = input.DnsDomain;
            cluster.Ddos = input.Ddos;
            await db.SaveChangesAsync();
            _ = audit.LogAsync("clusters.put /{id:long}", "ok");
            publishQueue.Enqueue(id); // DDoS 配置变更推送到节点
            _ = dns.SyncClusterRecordsAsync(id); // 集群域名解析维护
            return Results.Ok();
        });

        // 集群运维配置（阈值告警 + 健康检查参数）
        admin.MapGet("/clusters/{id:long}/ops", async (long id, CdnDbContext db) =>
        {
            var c = await db.Clusters.FindAsync(id);
            if (c == null) return Results.NotFound();
            return Results.Ok(c.Ops ?? new Core.OpsConfig());
        });

        admin.MapPut("/clusters/{id:long}/ops", async (long id, Core.OpsConfig input, CdnDbContext db, ConfigPublishQueue publishQueue, AuditService audit) =>
        {
            var c = await db.Clusters.FindAsync(id);
            if (c == null) return Results.NotFound();
            c.Ops = input;
            await db.SaveChangesAsync();
            _ = audit.LogAsync($"clusters.ops {id}", $"cpu={input.CpuPercent} mem={input.MemPercent} load={input.Load}");
            publishQueue.Enqueue(id); // probe 参数随配置下发
            return Results.Ok(new { ok = true });
        });

        // 集群级默认配置套件通用设计
        admin.MapGet("/clusters/{id:long}/defaults", async (long id, CdnDbContext db) =>
        {
            var c = await db.Clusters.FindAsync(id);
            if (c == null) return Results.NotFound();
            return Results.Ok(c.Defaults ?? new Core.ClusterDefaults());
        });

        admin.MapPut("/clusters/{id:long}/defaults", async (long id, Core.ClusterDefaults input, CdnDbContext db, ConfigPublishQueue publishQueue, AuditService audit) =>
        {
            var c = await db.Clusters.FindAsync(id);
            if (c == null) return Results.NotFound();
            c.Defaults = input;
            await db.SaveChangesAsync();
            _ = audit.LogAsync($"clusters.defaults {id}", $"cache={input.Cache?.Enabled} cc={(input.Security?.Cc?.Enabled == true)} wafRules={input.Security?.WafRules?.Count ?? 0}");
            publishQueue.Enqueue(id); // 立即重发布：兜底合并进快照
            return Results.Ok(new { ok = true });
        });

        // ---- 审计日志查询 ----
        admin.MapGet("/audit", async (CdnDbContext db, int? page, int? size) =>
        {
            var pg = Math.Max(1, page ?? 1); var sz = (size == null || size <= 0) ? 20 : Math.Clamp(size.Value, 1, 100);
            var q = db.AuditLogs.AsNoTracking().OrderByDescending(a => a.Timestamp);
            var total = await q.CountAsync();
            var items = await q.Skip((pg - 1) * sz).Take(sz).ToListAsync();
            return Results.Ok(new { total, items });
        });

        // ---- 高防 IP 管理 ----
        var prot = admin.MapGroup("/protection");
        prot.MapGet("", async (CdnDbContext db) => await db.ProtectionInstances.OrderByDescending(p => p.CreatedAt).ToListAsync());

        // 线路库目录（节点 Line 字段/智能 DNS 分线路下拉取用）
        api.MapGet("/admin/line-catalog", () => Results.Ok(
            TnorvCdn.Api.Services.LineRegistry.Catalog.Select(c => new { name = c.Name, desc = c.Desc })));
        prot.MapPost("", async (ProtectionInstance inst, CdnDbContext db) =>
        {
            if (string.IsNullOrWhiteSpace(inst.Name) || string.IsNullOrWhiteSpace(inst.Ip))
                return Results.BadRequest("名称和高防 IP 必填");
            db.ProtectionInstances.Add(inst);
            await db.SaveChangesAsync();
            return Results.Created($"/api/admin/protection/{inst.Id}", inst);
        });
        prot.MapPut("/{id:long}", async (long id, ProtectionInstance input, CdnDbContext db) =>
        {
            var inst = await db.ProtectionInstances.FindAsync(id);
            if (inst == null) return Results.NotFound();
            inst.Name = input.Name; inst.Ip = input.Ip; inst.Line = input.Line;
            inst.PeakMbps = input.PeakMbps; inst.ProtectedMbps = input.ProtectedMbps;
            inst.ProtectedTargets = input.ProtectedTargets; inst.Status = input.Status;
            inst.ExpireAt = input.ExpireAt;
            await db.SaveChangesAsync();
            return Results.Ok();
        });
        prot.MapDelete("/{id:long}", async (long id, CdnDbContext db) =>
        {
            var inst = await db.ProtectionInstances.FindAsync(id);
            if (inst == null) return Results.NotFound();
            db.ProtectionInstances.Remove(inst);
            await db.SaveChangesAsync();
            return Results.Ok();
        });

        // ---- 区域监控 ----
        var monitor = admin.MapGroup("/monitor");
        // 最近 10 分钟各站点按节点聚合的探测可用性
        monitor.MapGet("/probes", async (CdnDbContext db) =>
        {
            var since = DateTime.UtcNow.AddMinutes(-10);
            var rows = await db.ProbeRecords
                .Where(pr => pr.Timestamp >= since)
                .OrderByDescending(pr => pr.Timestamp)
                .Select(pr => new { pr.NodeId, pr.ServerId, pr.Ok, pr.Status, pr.LatencyMs, pr.Error, pr.Timestamp })
                .ToListAsync();
            var agg = rows.GroupBy(r => new { r.ServerId, r.NodeId })
                .Select(g => new
                {
                    serverId = g.Key.ServerId,
                    nodeId = g.Key.NodeId,
                    okRate = g.Count(x => x.Ok) * 100.0 / g.Count(),
                    avgLatencyMs = g.Average(x => x.LatencyMs),
                    lastError = g.FirstOrDefault(x => !x.Ok)?.Error ?? "",
                    lastSeen = g.Max(x => x.Timestamp),
                })
                .ToList();
            return Results.Ok(new { window = "10m", probes = agg });
        });

        // ---- 三方 DNS ----
        var dnsGroup = admin.MapGroup("/dns");
        dnsGroup.MapGet("/status", (DnsService dns) => Results.Ok(new { enabled = dns.Enabled }));
        dnsGroup.MapPost("/clusters/{id:long}/sync", async (long id, CdnDbContext db, DnsService dns) =>
        {
            if (await db.Clusters.FindAsync(id) == null) return Results.NotFound();
            await dns.SyncClusterRecordsAsync(id);
            return Results.Ok(new { ok = true });
        });

        clusters.MapDelete("/{id:long}", async (long id, CdnDbContext db, AuditService audit) =>
        {
            var cluster = await db.Clusters.FindAsync(id);
            if (cluster == null) return Results.NotFound();
            if (await db.Nodes.AnyAsync(n => n.ClusterId == id)) return Results.BadRequest("集群下仍有节点，无法删除");
            if (await db.Servers.AnyAsync(s => s.ClusterId == id)) return Results.BadRequest("集群下仍有网站，无法删除");
            db.Clusters.Remove(cluster);
            await db.SaveChangesAsync();
            _ = audit.LogAsync("clusters.delete /{id:long}", "ok");
            return Results.Ok();
        });

        // ---- 节点 ----
        nodes.MapGet("", async (CdnDbContext db) => await db.Nodes.Select(n => new
        {
            n.Id, n.Name, n.ClusterId, n.Hostname, n.Version, n.IsOnline, n.LastHeartbeat, n.PublicIps, n.Role, n.Line
        }).ToListAsync());

        nodes.MapPost("", async (CreateNodeRequest req, CdnDbContext db) =>
        {
            var node = new Node
            {
                Name = req.Name,
                ClusterId = req.ClusterId,
                InstallToken = Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant()
            };
            db.Nodes.Add(node);
            await db.SaveChangesAsync();
            // 返回 install token 供安装脚本使用通用设计
            return Results.Created($"/api/admin/nodes/{node.Id}", new
            {
                node.Id, node.Name, node.InstallToken,
                registerHint = "节点启动时携带 install_token 调用 NodeService/Register"
            });
        });

        // 节点自升级指令（写入节点待升级状态，随配置快照下发）
        admin.MapPost("/nodes/{id:long}/upgrade", async (long id, UpgradeNodeRequest req, CdnDbContext db, IConfiguration cfg, ConfigPublisher publisher, ConfigPublishQueue publishQueue) =>
        {
            var node = await db.Nodes.FindAsync(id);
            if (node == null) return Results.NotFound();
            var version = req.Version ?? "";
            var url = req.Url ?? $"{cfg["Node:DownloadBaseUrl"] ?? "/downloads"}/tnorv-node-{version}-linux-amd64";
            node.Version = $"pending-{version}";
            await db.SaveChangesAsync();
            publishQueue.Enqueue(node.ClusterId);
            return Results.Ok(new { ok = true, version, url });
        });

        nodes.MapPut("/{id:long}", async (long id, UpdateNodeRequest req, CdnDbContext db, ConfigPublisher publisher, ConfigPublishQueue publishQueue) =>
        {
            var node = await db.Nodes.FindAsync(id);
            if (node == null) return Results.NotFound();
            if (req.Name != null) node.Name = req.Name;
            if (req.Line != null) node.Line = req.Line;   // 智能 DNS 线路
            await db.SaveChangesAsync();
            publishQueue.Enqueue(node.ClusterId);
            return Results.Ok();
        });

        nodes.MapDelete("/{id:long}", async (long id, CdnDbContext db, ConfigPublishQueue publishQueue, AuditService audit) =>
        {
            var node = await db.Nodes.FindAsync(id);
            if (node == null) return Results.NotFound();
            var cid = node.ClusterId;
            db.Nodes.Remove(node);
            await db.SaveChangesAsync();
            publishQueue.Enqueue(cid); // 移除后重发布：同集群节点表更新（SiblingNodes 等）
            _ = audit.LogAsync("nodes.delete", node.Name);
            return Results.Ok();
        });

        // ---- 网站 ----
        servers.MapGet("", async (CdnDbContext db, long? clusterId, long? userId, string? q, string? sslExpiring,
            int? page, int? pageSize, string? sort, string? order) =>
        {
            var q2 = db.Servers.AsNoTracking();
            if (clusterId.HasValue) q2 = q2.Where(s => s.ClusterId == clusterId);
            if (userId.HasValue) q2 = q2.Where(s => s.UserId == userId);
            if (sslExpiring == "1")
            {
                var horizon = DateTime.UtcNow.AddDays(14);
                q2 = q2.Where(s => s.SslMode != "off" && s.SslExpiresAt != null && s.SslExpiresAt < horizon);
            }
            var list = await q2.ToListAsync();
            if (!string.IsNullOrWhiteSpace(q))
            {
                var kw = q.Trim().ToLower();
                list = list.Where(s => s.Domains.Any(d => d.ToLower().Contains(kw))).ToList();
            }
            // 排序（内存）：domains/requests 代理为 id/当前序；真实流量排序看板负责
            list = (sort, order) switch
            {
                ("id", "asc") => list.OrderBy(s => s.Id).ToList(),
                ("id", "desc") => list.OrderByDescending(s => s.Id).ToList(),
                ("domains", "asc") => list.OrderBy(s => s.Domains.FirstOrDefault() ?? "").ToList(),
                ("domains", "desc") => list.OrderByDescending(s => s.Domains.FirstOrDefault() ?? "").ToList(),
                ("ssl", "asc") => list.OrderBy(s => s.SslExpiresAt ?? DateTime.MaxValue).ToList(),
                ("ssl", "desc") => list.OrderByDescending(s => s.SslExpiresAt ?? DateTime.MinValue).ToList(),
                ("created", "asc") => list.OrderBy(s => s.CreatedAt).ToList(),
                _ => list.OrderByDescending(s => s.Id).ToList(),
            };
            var total = list.Count;
            var hasPaging = (page.HasValue && page.Value > 0) || (pageSize.HasValue && pageSize.Value > 0);
            if (hasPaging)
            {
                var pg = Math.Max(1, page.Value);
                var sz = Math.Clamp(pageSize.Value <= 0 ? 20 : pageSize.Value, 1, 200);
                list = list.Skip((pg - 1) * sz).Take(sz).ToList();
            }
            // 兼容旧消费者（下拉选项等直接当数组用）：无分页参数 → 纯数组
            return hasPaging
                ? Results.Ok(new { total, page, pageSize = pageSize.HasValue && pageSize.Value > 0 ? pageSize.Value : 20, items = list })
                : Results.Ok(list);
        });

        // 单站点详情（分层配置页用）
        servers.MapGet("/{id:long}", async (long id, CdnDbContext db) =>
        {
            var server = await db.Servers.AsNoTracking().FirstOrDefaultAsync(s => s.Id == id);
            if (server == null) return Results.NotFound();
            return Results.Ok(server);
        });

        servers.MapPost("", async (Server server, CdnDbContext db, ConfigPublisher publisher, ConfigPublishQueue publishQueue, DnsService dns, AuditService audit) =>
        {
            if (server.Domains.Length == 0) return Results.BadRequest("至少绑定一个域名");
            if (server.Origin.Primaries.Count == 0) return Results.BadRequest("至少配置一个主源站");
            if (server.SslMode is not ("auto" or "manual" or "off")) server.SslMode = "auto";
            if (server.SslMode == "off") server.HttpsPorts = [];
            if (server.UserId == 0) server.UserId = 1;
            // 一级域名备案门禁：中国大陆区域要求其一级域名已有备案通过记录，否则拒绝创建
            if (server.Region == "cn" && server.IcpStatus != "approved")
            {
                var apexOk = await CheckApexIcpAsync(db, server.Domains, server.Id);
                if (!apexOk)
                    return Results.BadRequest("一级域名尚未通过备案审核，无法创建中国大陆区域网站。请先在「备案审核」提交并通过一级域名的备案。");
            }
            // 中国大陆区域默认进入备案待审核（未提交备案号时），审核通过前不启用
            if (server.Region == "cn" && server.IcpStatus == "none" && server.Enabled)
            {
                server.IcpStatus = "pending";
                server.Enabled = false;
            }
            db.Servers.Add(server);
            await db.SaveChangesAsync();
            _ = audit.LogAsync("servers.post ", "ok");
            publishQueue.Enqueue(server.ClusterId); var version = await db.Clusters.Where(c => c.Id == server.ClusterId).Select(c => (long?)c.ConfigVersion).FirstOrDefaultAsync() ?? 0;
            _ = dns.SyncSiteDomainsAsync(server.ClusterId, server.Domains); // 三方 DNS 自动 CNAME（异步不阻塞）
            return Results.Created($"/api/admin/servers/{server.Id}", new { server.Id, configVersion = version });
        });

        servers.MapPut("/{id:long}", async (long id, Server input, CdnDbContext db, ConfigPublisher publisher, ConfigPublishQueue publishQueue, DnsService dns, AuditService audit) =>
        {
            var server = await db.Servers.FindAsync(id);
            if (server == null) return Results.NotFound();
            server.Domains = input.Domains;
            server.ServerType = input.ServerType;
            // ICP 字段仅在有值时覆盖，避免前端编辑表单（不含备案信息）把已有备案状态清掉
            if (!string.IsNullOrEmpty(input.IcpStatus)) server.IcpStatus = input.IcpStatus;
            if (input.IcpNumber != null) server.IcpNumber = input.IcpNumber;
            if (input.IcpNote != null) server.IcpNote = input.IcpNote;
            server.L4Ports = input.L4Ports;
            server.L4ProxyProtocol = input.L4ProxyProtocol;
            server.HttpPorts = input.HttpPorts;
            server.HttpsPorts = input.HttpsPorts;
            server.SslMode = input.SslMode is "auto" or "manual" or "off" ? input.SslMode : "auto";
            if (server.SslMode == "off") server.HttpsPorts = [];
            server.ForceHttps = input.ForceHttps;
            server.Enabled = input.Enabled;
            server.Origin = input.Origin;
            server.Cache = input.Cache;
            server.Security = input.Security;
            server.RealtimePublish = input.RealtimePublish;
            await db.SaveChangesAsync();
            _ = audit.LogAsync("servers.put /{id:long}", "ok");
            publishQueue.Enqueue(server.ClusterId); var version = await db.Clusters.Where(c => c.Id == server.ClusterId).Select(c => (long?)c.ConfigVersion).FirstOrDefaultAsync() ?? 0;
            _ = dns.SyncSiteDomainsAsync(server.ClusterId, server.Domains);
            return Results.Ok(new { server.Id, configVersion = version });
        });

        servers.MapDelete("/{id:long}", async (long id, CdnDbContext db, ConfigPublisher publisher, ConfigPublishQueue publishQueue, AuditService audit) =>
        {
            var server = await db.Servers.FindAsync(id);
            if (server == null) return Results.NotFound();
            db.Servers.Remove(server);
            await db.SaveChangesAsync();
            _ = audit.LogAsync("servers.delete /{id:long}", "ok");
            publishQueue.Enqueue(server.ClusterId);
            return Results.Ok();
        });

        // ---- 下发策略：站点级实时下发开关 + 立即下发（默认用户侧变更走延迟批量发布）----
        servers.MapPut("/{id:long}/realtime", async (long id, RealtimePublishRequest req, CdnDbContext db, ConfigPublishQueue publishQueue, AuditService audit) =>
        {
            var server = await db.Servers.FindAsync(id);
            if (server == null) return Results.NotFound();
            server.RealtimePublish = req.Enabled == true;
            await db.SaveChangesAsync();
            _ = audit.LogAsync($"servers.realtime {id}", $"enabled={server.RealtimePublish}");
            return Results.Ok(new { ok = true, realtimePublish = server.RealtimePublish });
        });
        servers.MapPost("/{id:long}/publish", async (long id, CdnDbContext db, ConfigPublishQueue publishQueue, AuditService audit) =>
        {
            var server = await db.Servers.FindAsync(id);
            if (server == null) return Results.NotFound();
            publishQueue.Enqueue(server.ClusterId);
            _ = audit.LogAsync($"servers.publish {id}", "immediate");
            await db.SaveChangesAsync();
            var version = await db.Clusters.Where(c => c.Id == server.ClusterId).Select(c => (long?)c.ConfigVersion).FirstOrDefaultAsync() ?? 0;
            return Results.Ok(new { ok = true, configVersion = version });
        });


        // ---- 域名备案审核（接入备案 API 前由管理员人工审核；中国大陆节点要求已备案）----
        servers.MapGet("/icp", async (CdnDbContext db, string? status) =>
        {
            var q = db.Servers.AsNoTracking().Where(s2 => s2.IcpStatus != "approved" || s2.IcpStatus == null);
            if (!string.IsNullOrEmpty(status)) q = q.Where(s2 => s2.IcpStatus == status);
            var list = await q.OrderBy(s2 => s2.Id)
                .Select(s2 => new { s2.Id, s2.Domains, s2.Region, s2.IcpStatus, s2.IcpNumber, s2.IcpNote, s2.Enabled, s2.UserId })
                .ToListAsync();
            return Results.Ok(list);
        });

        servers.MapPost("/icp/{id:long}/review", async (long id, IcpReviewRequest req, CdnDbContext db, ConfigPublisher publisher, ConfigPublishQueue publishQueue, AuditService audit, Services.SmsService sms) =>
        {
            var server = await db.Servers.FindAsync(id);
            if (server == null) return Results.NotFound();
            if (req.Status is not ("pending" or "approved" or "rejected")) return Results.BadRequest("status 须为 pending/approved/rejected");
            server.IcpStatus = req.Status;
            server.IcpNumber = req.IcpNumber ?? server.IcpNumber;
            server.IcpNote = req.Note ?? server.IcpNote;
            // 备案审核结果联动站点启停：通过→自动启用；驳回→自动停用（中国大陆合规）
            if (req.Status == "approved") { server.Enabled = true; }
            else if (req.Status == "rejected") { server.Enabled = false; }
            await db.SaveChangesAsync();
            _ = audit.LogAsync($"servers.icp.review {id}", $"status={req.Status} icp={server.IcpNumber} enabled={server.Enabled}");
            publishQueue.Enqueue(server.ClusterId);
            // 短信提醒备案结果（内容不带域名，短信风控禁止）
            _ = sms.NotifyAsync(server.UserId, "icp_result", req.Status == "approved"
                ? "您的备案审核已通过，网站已自动启用。"
                : req.Status == "rejected"
                    ? "您的备案审核未通过，网站已停用。请登录控制台查看原因并重新提交。"
                    : "您的备案状态已更新，请登录控制台查看。", $"icp:{server.Id}:{req.Status}");
            return Results.Ok(new { server.Id, server.IcpStatus, server.IcpNumber, server.IcpNote });
        });

        servers.MapPost("/icp/submit", async (IcpSubmitRequest req, CdnDbContext db, AuditService audit) =>
        {
            var server = await db.Servers.FindAsync(req.ServerId);
            if (server == null) return Results.NotFound();
            server.IcpStatus = "pending";
            server.IcpNumber = req.IcpNumber ?? "";
            server.IcpNote = req.Note ?? "";
            await db.SaveChangesAsync();
            _ = audit.LogAsync($"servers.icp.submit {req.ServerId}", $"icp={server.IcpNumber}");
            return Results.Ok(new { server.Id, server.IcpStatus, server.IcpNumber });
        });

        // ---- 批量替换源站（勾选多站点，统一改主源站 Host/Port）----
        // 批量操作：启用/停用/删除/更换集群通用设计
        servers.MapPost("/batch", async (ServerBatchRequest req, CdnDbContext db, ConfigPublishQueue publishQueue, AuditService audit) =>
        {
            if (req.Ids == null || req.Ids.Count == 0) return Results.BadRequest("ids required");
            var list = await db.Servers.Where(x => req.Ids.Contains(x.Id)).ToListAsync();
            switch ((req.Action ?? "").ToLowerInvariant())
            {
                case "enable": list.ForEach(x => x.Enabled = true); break;
                case "disable": list.ForEach(x => x.Enabled = false); break;
                case "delete": db.Servers.RemoveRange(list); break;
                case "move-cluster":
                    if (!req.ClusterId.HasValue) return Results.BadRequest("clusterId required");
                    list.ForEach(x => x.ClusterId = req.ClusterId.Value);
                    break;
                default: return Results.BadRequest("action 支持 enable/disable/delete/move-cluster");
            }
            await db.SaveChangesAsync();
            foreach (var cid in list.Select(x => x.ClusterId).Distinct()) publishQueue.Enqueue(cid);
            _ = audit.LogAsync("servers.batch", $"{req.Action} ids={string.Join(',', req.Ids)}");
            return Results.Ok(new { ok = true, affected = list.Count });
        });

        servers.MapPost("/batch-origin", async (BatchOriginRequest req, CdnDbContext db, ConfigPublisher publisher, ConfigPublishQueue publishQueue, AuditService audit) =>
        {
            if (req.ServerIds == null || req.ServerIds.Count == 0) return Results.BadRequest("请选择站点");
            if (string.IsNullOrWhiteSpace(req.NewHost)) return Results.BadRequest("请填写新源站地址");
            var changed = 0;
            var clusterIds = new HashSet<long>();
            foreach (var sid in req.ServerIds)
            {
                var s = await db.Servers.FindAsync(sid);
                if (s == null) continue;
                var primaries = s.Origin.Primaries.Where(o => o.Enabled).OrderBy(o => o.Weight).ToList();
                if (primaries.Count == 0)
                {
                    s.Origin.Primaries.Add(new Origin { Host = req.NewHost, Port = req.NewPort ?? 80, Enabled = true, Weight = 1 });
                }
                else
                {
                    var p0 = primaries[0];
                    p0.Host = req.NewHost;
                    if (req.NewPort != null) p0.Port = req.NewPort.Value;
                }
                clusterIds.Add(s.ClusterId);
                changed++;
            }
            await db.SaveChangesAsync();
            foreach (var cid in clusterIds) publishQueue.Enqueue(cid);
            _ = audit.LogAsync("servers.batch-origin", $"changed={changed} host={req.NewHost}");
            return Results.Ok(new { changed });
        });

        // ---- 复制配置通用设计----
        servers.MapPost("/{id:long}/copy-from", async (long id, CopyFromRequest req, CdnDbContext db, ConfigPublishQueue publishQueue, AuditService audit) =>
        {
            if (req.FromId == id) return Results.BadRequest("不能从自身复制");
            var target = await db.Servers.FindAsync(id);
            var source = await db.Servers.FindAsync(req.FromId);
            if (target == null || source == null) return Results.NotFound("站点不存在");
            var parts = (req.Parts ?? ["cache", "security", "advanced"]).ToHashSet();
            if (parts.Contains("cache")) target.Cache = source.Cache;
            if (parts.Contains("security")) target.Security = source.Security;
            if (parts.Contains("advanced")) target.Advanced = source.Advanced;
            if (parts.Contains("locations")) target.Locations = source.Locations;
            if (parts.Contains("https"))
            {
                target.SslMode = source.SslMode;
                target.ForceHttps = source.ForceHttps;
                target.HttpsPorts = source.HttpsPorts;
            }
            if (parts.Contains("origin")) target.Origin = source.Origin;
            await db.SaveChangesAsync();
            publishQueue.Enqueue(target.ClusterId);
            _ = audit.LogAsync($"servers.copy-from {id}", $"from={req.FromId} parts={string.Join(',', parts)}");
            return Results.Ok(new { ok = true });
        });

        // ---- 路由规则 locations（location 级覆盖：缓存/CC/IP名单/防盗链/请求限制，通用设计----
        servers.MapGet("/{id:long}/locations", async (long id, CdnDbContext db) =>
        {
            var s = await db.Servers.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
            if (s == null) return Results.NotFound();
            return Results.Ok(s.Locations);
        });
        servers.MapPut("/{id:long}/locations", async (long id, List<LocationRule> input, CdnDbContext db, ConfigPublishQueue publishQueue, AuditService audit) =>
        {
            var s = await db.Servers.FindAsync(id);
            if (s == null) return Results.NotFound();
            // 校验：前缀必填、去重
            var clean = input.Where(r => !string.IsNullOrWhiteSpace(r.PathPrefix)).ToList();
            if (clean.Select(r => r.PathPrefix).Distinct().Count() != clean.Count)
                return Results.BadRequest("路径前缀重复");
            s.Locations = clean;
            await db.SaveChangesAsync();
            publishQueue.Enqueue(s.ClusterId);
            _ = audit.LogAsync($"servers.locations {id}", $"rules={clean.Count}");
            return Results.Ok(new { ok = true, count = clean.Count });
        });

        // ---- 缓存刷新（URL/目录/域名/全部）----
        servers.MapPost("/cache/purge", async (PurgeRequest req, CdnDbContext db, PurgeRegistry purges, AuditService audit) =>
        {
            if (req.MatchType is not ("url" or "directory" or "host" or "all"))
                return Results.BadRequest("matchType 须为 url / directory / host / all");
            if (req.MatchType != "all" && string.IsNullOrWhiteSpace(req.Host))
                return Results.BadRequest("须指定 host");
            var targets = await db.Nodes.Select(n => n.ClusterId).Distinct().ToListAsync();
            var ids = new List<long>();
            foreach (var cid in targets)
                ids.Add(purges.Enqueue(cid, req.MatchType, req.Host ?? "", req.Value ?? ""));
            return Results.Ok(new { queued = ids.Count });
        });

        // ---- 解析测试：向集群节点内建权威 DNS 发真实查询 ----
        admin.MapPost("/dns/resolve-test", async (ResolveTestRequest req, CdnDbContext db) =>
        {
            var name = (req.Name ?? "").Trim();
            if (name == "") return Results.BadRequest("name 必填");
            // 选在线节点公网 IP 作为 DNS 服务器（内建权威监听 :53）；无在线节点则回退本机 127.0.0.1
            var nodeIp = (await db.Nodes.AsNoTracking().Where(n => n.IsOnline).ToListAsync())
                .SelectMany(n => n.PublicIps).FirstOrDefault(ip => System.Net.IPAddress.TryParse(ip, out _));
            var serverIp = nodeIp ?? "127.0.0.1";
            try { return Results.Ok(await DnsQueryAsync(serverIp, name, string.IsNullOrWhiteSpace(req.Type) ? "A" : req.Type)); }
            catch (Exception ex) { return Results.Ok(new { ok = false, server = serverIp, error = ex.Message }); }
        });

        // ---- 多租户计费：套餐 / 订阅 / 用量 ----
        var plans = admin.MapGroup("/plans");
        plans.MapGet("", async (CdnDbContext db, int? page, int? pageSize, string? q) =>
        {
            var query = db.Plans.AsNoTracking()
                .Where(p => q == null || q == "" || p.Name.ToLower().Contains(q.ToLower()))
                .OrderByDescending(p => p.Id);
            if (page == null && pageSize == null)
                return Results.Ok(await query.OrderBy(p => p.PriceCents).ToListAsync());
            var p = Math.Clamp(page ?? 1, 1, 1000);
            var ps = Math.Clamp(pageSize ?? 20, 1, 200);
            var total = await query.CountAsync();
            var items = await query.Skip((p - 1) * ps).Take(ps).ToListAsync();
            return Results.Ok(new { total, page = p, pageSize = ps, items });
        });
        plans.MapPost("", async (Plan plan, CdnDbContext db, AuditService audit) =>
        {
            if (string.IsNullOrWhiteSpace(plan.Name)) return Results.BadRequest("名称必填");
            db.Plans.Add(plan);
            await db.SaveChangesAsync();
            _ = audit.LogAsync("plans.post ", "ok");
            return Results.Created($"/api/admin/plans/{plan.Id}", plan);
        });
        plans.MapPut("/{id:long}", async (long id, Plan input, CdnDbContext db, AuditService audit) =>
        {
            var plan = await db.Plans.FindAsync(id);
            if (plan == null) return Results.NotFound();
            plan.Name = input.Name; plan.MaxServers = input.MaxServers; plan.MaxDomains = input.MaxDomains;
            plan.MonthlyTrafficMB = input.MonthlyTrafficMB; plan.PriceCents = input.PriceCents; plan.Enabled = input.Enabled;
            plan.Features = input.Features; // 功能开关/配额矩阵 JSON（空=全开）
            await db.SaveChangesAsync();
            _ = audit.LogAsync("plans.put /{id:long}", "ok");
            return Results.Ok();
        });
        plans.MapDelete("/{id:long}", async (long id, CdnDbContext db, AuditService audit) =>
        {
            var plan = await db.Plans.FindAsync(id);
            if (plan == null) return Results.NotFound();
            if (await db.Subscriptions.AnyAsync(s => s.PlanId == id)) return Results.BadRequest("有用户订阅该套餐，无法删除");
            db.Plans.Remove(plan);
            await db.SaveChangesAsync();
            _ = audit.LogAsync("plans.delete /{id:long}", "ok");
            return Results.Ok();
        });

        var subs = admin.MapGroup("/subscriptions");
        subs.MapGet("", async (CdnDbContext db, long? userId, bool? enabled, int? page, int? pageSize) =>
        {
            var q = db.Subscriptions.AsNoTracking().AsQueryable();
            if (userId.HasValue) q = q.Where(s => s.UserId == userId);
            if (enabled == true) q = q.Where(s => s.Enabled); else if (enabled == false) q = q.Where(s => !s.Enabled);
            q = q.OrderByDescending(s => s.Id);
            if (page == null && pageSize == null) return Results.Ok(await q.Take(500).ToListAsync());
            var p = Math.Clamp(page ?? 1, 1, 1000); var ps = Math.Clamp(pageSize ?? 20, 1, 200);
            var total = await q.CountAsync();
            var items = await q.Skip((p - 1) * ps).Take(ps).ToListAsync();
            return Results.Ok(new { total, page = p, pageSize = ps, items });
        });
        subs.MapPost("", async (Subscription sub, CdnDbContext db) =>
        {
            if (!await db.Plans.AnyAsync(p => p.Id == sub.PlanId)) return Results.BadRequest("套餐不存在");
            if (!await db.Users.AnyAsync(u => u.Id == sub.UserId)) return Results.BadRequest("用户不存在");
            db.Subscriptions.Add(sub);
            await db.SaveChangesAsync();
            return Results.Created($"/api/admin/subscriptions/{sub.Id}", sub);
        });
        // 订阅管理：续期（延长到期日）/改套餐/启停/自动续费开关
        subs.MapPut("/{id:long}", async (long id, Subscription input, CdnDbContext db) =>
        {
            var sub = await db.Subscriptions.FindAsync(id);
            if (sub == null) return Results.NotFound();
            if (input.PlanId != 0 && input.PlanId != sub.PlanId)
            {
                if (!await db.Plans.AnyAsync(p => p.Id == input.PlanId)) return Results.BadRequest("套餐不存在");
                sub.PlanId = input.PlanId;
            }
            if (input.ExpiresAt != default) sub.ExpiresAt = input.ExpiresAt;
            if (input.StartsAt != default) sub.StartsAt = input.StartsAt;
            sub.Enabled = input.Enabled;
            sub.AutoRenew = input.AutoRenew;
            await db.SaveChangesAsync();
            return Results.Ok(new { ok = true });
        });
        subs.MapDelete("/{id:long}", async (long id, CdnDbContext db) =>
        {
            var sub = await db.Subscriptions.FindAsync(id);
            if (sub == null) return Results.NotFound();
            db.Subscriptions.Remove(sub);
            await db.SaveChangesAsync();
            return Results.Ok();
        });

        // 用量统计：按用户聚合当月请求数与流量（日志实时计算 + 用量表落库）
        admin.MapGet("/usage", async (CdnDbContext db, long? userId, int? year, int? month) =>
        {
            var now = DateTime.UtcNow;
            var y = year ?? now.Year;
            var m = month ?? now.Month;
            var from = new DateTime(y, m, 1, 0, 0, 0, DateTimeKind.Utc);
            var to = from.AddMonths(1);
            var q = db.AccessLogs.AsNoTracking().Where(l => l.Timestamp >= from && l.Timestamp < to);
            if (userId.HasValue)
            {
                var serverIds = await db.Servers.Where(s => s.UserId == userId).Select(s => s.Id).ToListAsync();
                q = q.Where(l => serverIds.Contains(l.ServerId));
            }
            var rows = await q.GroupBy(l => l.ServerId)
                .Select(g => new { ServerId = g.Key, Requests = g.Count(), Bytes = g.Sum(x => (long?)x.BytesSent) ?? 0 })
                .ToListAsync();
            var serverUser = await db.Servers.Select(s => new { s.Id, s.UserId }).ToDictionaryAsync(x => x.Id, x => x.UserId);
            var byUser = rows.GroupBy(r => serverUser.GetValueOrDefault(r.ServerId, 0))
                .Select(g => new { userId = g.Key, requests = g.Sum(x => x.Requests), trafficMB = g.Sum(x => x.Bytes) / 1048576 })
                .OrderByDescending(x => x.trafficMB)
                .ToList();
            return Results.Ok(new { year = y, month = m, usage = byUser });
        });

        // ---- 证书管理：手动上传（PEM 解析域名与到期时间）+ 到期提醒 ----
        var certs = admin.MapGroup("/certificates");
        certs.MapGet("", async (CdnDbContext db, string? q, int? page, int? pageSize) =>
        {
            var query = db.Certificates.AsNoTracking().AsQueryable();
            if (!string.IsNullOrWhiteSpace(q)) { var kw = q.ToLower(); query = query.Where(c => c.Domains.ToLower().Contains(kw)); }
            query = query.OrderByDescending(c => c.Id);
            var proj = query.Select(c => new { c.Id, c.ClusterId, c.Domains, c.ExpiresAt, c.Source, c.CreatedAt });
            if (page == null && pageSize == null) return Results.Ok(await proj.Take(500).ToListAsync());
            var p = Math.Clamp(page ?? 1, 1, 1000); var ps = Math.Clamp(pageSize ?? 20, 1, 200);
            var total = await query.CountAsync();
            var items = await proj.Skip((p - 1) * ps).Take(ps).ToListAsync();
            return Results.Ok(new { total, page = p, pageSize = ps, items });
        });

        certs.MapPost("", async (CertUpload req, CdnDbContext db, NotificationService notify) =>
        {
            if (string.IsNullOrWhiteSpace(req.CertPem) || string.IsNullOrWhiteSpace(req.KeyPem))
                return Results.BadRequest("证书与私钥必填");

            // 解析证书：提取域名与到期时间（不落盘私钥明文到日志）
            System.Security.Cryptography.X509Certificates.X509Certificate2 cert;
            try
            {
                cert = System.Security.Cryptography.X509Certificates.X509Certificate2.CreateFromPem(req.CertPem, req.KeyPem);
            }
            catch (Exception ex)
            {
                return Results.BadRequest($"证书解析失败: {ex.Message}");
            }
            var domains = cert.GetNameInfo(System.Security.Cryptography.X509Certificates.X509NameType.DnsFromAlternativeName, false);
            if (string.IsNullOrEmpty(domains))
                domains = cert.GetNameInfo(System.Security.Cryptography.X509Certificates.X509NameType.SimpleName, false);

            var entity = new Certificate
            {
                ClusterId = req.ClusterId,
                Domains = domains,
                CertPem = req.CertPem,
                KeyPem = req.KeyPem,
                ExpiresAt = cert.NotAfter.ToUniversalTime(),
                Source = "manual",
            };
            db.Certificates.Add(entity);
            await db.SaveChangesAsync();
            return Results.Created($"/api/admin/certificates/{entity.Id}", new { entity.Id, entity.Domains, entity.ExpiresAt });
        });

        // A7：证书续期请求（设置 SslRenewRequestedAt，节点轮询后重新走 ACME；手动证书返回错误）
        certs.MapPost("/{id:long}/renew", async (long id, CdnDbContext db, ConfigPublishQueue pq) =>
        {
            var cert = await db.Certificates.FindAsync(id);
            if (cert == null) return Results.NotFound();
            if (cert.Source != "acme") return Results.BadRequest("手动上传的证书请在源站更新后重新上传");
            var domain = cert.Domains.Split(',').FirstOrDefault(d => !string.IsNullOrWhiteSpace(d)) ?? cert.Domains;
            var servers = await db.Servers.Where(s => s.SslMode == "auto").Select(s => new { s.Id, s.Domains, s.ClusterId }).ToListAsync();
            var server = servers.FirstOrDefault(s => s.Domains.Any(d => d == domain || d.Replace("*.", "") == domain.Replace("*.", "")));
            Server? matched = null;
            if (server != null) matched = await db.Servers.FindAsync(server.Id);
            if (matched == null) return Results.BadRequest("未找到该证书对应的自动签发站点");
            if (matched.SslRenewRequestedAt != null && DateTime.UtcNow - matched.SslRenewRequestedAt < TimeSpan.FromMinutes(10))
                return Results.BadRequest("已有续期请求正在执行中，请等待完成或超时后再试");
            matched.SslRenewRequestedAt = DateTime.UtcNow;
            await db.SaveChangesAsync();
            pq.Enqueue(matched.ClusterId);
            return Results.Ok(new { ok = true });
        });

        certs.MapDelete("/{id:long}", async (long id, CdnDbContext db) =>
        {
            var cert = await db.Certificates.FindAsync(id);
            if (cert == null) return Results.NotFound();
            db.Certificates.Remove(cert);
            await db.SaveChangesAsync();
            return Results.Ok();
        });

        // ---- ACME 签发账户（运营配置，存库，管理员可编辑）----
        var acmeAccounts = admin.MapGroup("/acme-accounts");
        acmeAccounts.MapGet("", async (CdnDbContext db) =>
            await db.AcmeAccounts.OrderBy(a => a.Id).ToListAsync());

        acmeAccounts.MapPost("", async (AcmeAccount input, CdnDbContext db, ConfigPublishQueue publishQueue) =>
        {
            if (string.IsNullOrWhiteSpace(input.DefaultEmail) && string.IsNullOrWhiteSpace(input.EmailTemplate))
                return Results.BadRequest("邮箱或邮箱模板至少填一项");
            // 模板占位符校验：仅允许 {userId}/{serverId}
            var bad = System.Text.RegularExpressions.Regex.Matches(input.EmailTemplate ?? "", @"\{([^}]*)\}")
                .Select(m => m.Groups[1].Value).Where(x => x != "userId" && x != "serverId").ToList();
            if (bad.Count > 0) return Results.BadRequest($"不支持的占位符: {string.Join(", ", bad)}（仅支持 {{userId}}/{{serverId}}）");
            db.AcmeAccounts.Add(input);
            await db.SaveChangesAsync();
            publishQueue.EnqueueAll();  // 邮箱变更需通知所有集群节点重建 ACME 账户
            return Results.Created($"/api/admin/acme-accounts/{input.Id}", input);
        });

        acmeAccounts.MapPut("/{id:long}", async (long id, AcmeAccount input, CdnDbContext db, ConfigPublishQueue publishQueue) =>
        {
            var acc = await db.AcmeAccounts.FindAsync(id);
            if (acc == null) return Results.NotFound();
            acc.Name = input.Name;
            acc.EmailTemplate = input.EmailTemplate;
            acc.DefaultEmail = input.DefaultEmail;
            acc.Enabled = input.Enabled;
            acc.Note = input.Note;
            await db.SaveChangesAsync();
            publishQueue.EnqueueAll();
            return Results.Ok(acc);
        });

        acmeAccounts.MapDelete("/{id:long}", async (long id, CdnDbContext db, ConfigPublishQueue publishQueue) =>
        {
            var acc = await db.AcmeAccounts.FindAsync(id);
            if (acc == null) return Results.NotFound();
            db.AcmeAccounts.Remove(acc);
            await db.SaveChangesAsync();
            publishQueue.EnqueueAll();
            return Results.Ok();
        });

        // ---- 证书签发事件日志（管理员可查看的签发时间线）----
        var certEvents = admin.MapGroup("/cert-events");
        certEvents.MapGet("", async (CdnDbContext db, long? serverId, string? status, string? domain, int page = 1, int size = 30) =>
        {
            var pg = Math.Max(1, page); var sz = Math.Clamp(size, 1, 200);
            var q = db.CertEvents.AsNoTracking().AsQueryable();
            if (serverId.HasValue) q = q.Where(e => e.ServerId == serverId);
            if (!string.IsNullOrEmpty(status)) q = q.Where(e => e.Status == status);
            if (!string.IsNullOrEmpty(domain)) q = q.Where(e => e.Domain.Contains(domain));
            var total = await q.CountAsync();
            var items = await q.OrderByDescending(e => e.CreatedAt).Skip((pg - 1) * sz).Take(sz)
                .Select(e => new
                {
                    e.Id, e.ServerId, e.NodeId, e.Domain, e.Status, e.Error, e.CreatedAt,
                    serverDomains = db.Servers.Where(s => s.Id == e.ServerId).Select(s => s.Domains).FirstOrDefault(),
                }).ToListAsync();
            return Results.Ok(new { total, page = pg, size = sz, items });
        });

        // 证书状态总览（全部 auto 站点：状态/最近尝试/错误，管理员排障用）
        admin.MapGet("/ssl/status", async (CdnDbContext db) =>
        {
            var servers = await db.Servers.AsNoTracking().Where(s => s.SslMode == "auto").ToListAsync();
            return Results.Ok(servers.Select(s => new
            {
                serverId = s.Id,
                domains = s.Domains,
                sslState = s.SslState,
                lastAttemptAt = s.SslLastAttemptAt,
                expiresAt = s.SslExpiresAt,
                lastError = s.SslLastError,
                renewPending = s.SslRenewRequestedAt != null,
            }));
        });

        // 管理员批量重试：对 failed/pending_dns 的站点重新发起（等价 renew，跳过冲突检查由 renew 自己判断）
        servers.MapPost("/{id:long}/ssl/retry", async (long id, CdnDbContext db, ConfigPublishQueue publishQueue) =>
        {
            var server = await db.Servers.FindAsync(id);
            if (server == null) return Results.NotFound();
            if (server.SslMode != "auto") return Results.BadRequest("仅自动签发模式的网站支持重试");
            if (server.HttpsPorts.Length == 0) server.HttpsPorts = [443]; // auto 模式必须监听 443，否则节点跳过签发
            server.SslRenewRequestedAt = DateTime.UtcNow;
            server.SslState = "pending_issuing";
            server.SslLastError = "";
            server.SslLastAttemptAt = DateTime.UtcNow;
            db.CertEvents.Add(new CertEvent { ServerId = server.Id, NodeId = 0, Domain = string.Join(",", server.Domains), Status = "renew_requested", Error = "管理员发起重试", CreatedAt = DateTime.UtcNow });
            await db.SaveChangesAsync();
            publishQueue.Enqueue(server.ClusterId);
            return Results.Ok(new { ok = true });
        });

        // ---- 网站分层配置（HTTPS / 缓存 / 安全 独立端点，前端分层配置页使用）----
        // HTTPS/SSL 证书开关：独立于网站编辑表单
        servers.MapPut("/{id:long}/ssl", async (long id, SslSettingsRequest req, CdnDbContext db, ConfigPublisher publisher, ConfigPublishQueue publishQueue, AuditService audit) =>
        {
            var server = await db.Servers.FindAsync(id);
            if (server == null) return Results.NotFound();
            if (req.SslMode is not ("auto" or "manual" or "off")) return Results.BadRequest("sslMode 须为 auto/manual/off");
            server.SslMode = req.SslMode;
            server.HttpsPorts = req.HttpsPorts is { Length: > 0 } ? req.HttpsPorts : (req.SslMode == "off" ? [] : [443]);
            if (server.SslMode == "off") server.HttpsPorts = [];
            if (req.SslMode != "auto")
            {
                server.SslState = "";  // 关闭/切手动时清除自动签发状态
                server.SslLastError = "";
            }
            else if (server.SslState == "failed" || server.SslState == "pending_dns")
            {
                server.SslState = "";  // 切回 auto 重新尝试签发
            }
            server.ForceHttps = req.ForceHttps;
            if (server.SslMode == "auto" && server.SslState == "")
                db.CertEvents.Add(new CertEvent { ServerId = server.Id, NodeId = 0, Domain = string.Join(",", server.Domains), Status = "renew_requested", Error = "开启自动签发，等待节点尝试", CreatedAt = DateTime.UtcNow });
            await db.SaveChangesAsync();
            _ = audit.LogAsync($"servers.ssl {id}", $"sslMode={server.SslMode} forceHttps={server.ForceHttps}");
            publishQueue.Enqueue(server.ClusterId); var version = await db.Clusters.Where(c => c.Id == server.ClusterId).Select(c => (long?)c.ConfigVersion).FirstOrDefaultAsync() ?? 0;
            return Results.Ok(new { ok = true, configVersion = version, server.SslMode, server.ForceHttps });
        });

        // 管理员主动发起证书续期：打标记随配置下发，节点清除本地证书缓存后重新签发。
        // 10 分钟内视为"更新中"（前端禁用按钮），超时自动失效可再次发起。
        servers.MapPost("/{id:long}/ssl/renew", async (long id, CdnDbContext db, ConfigPublishQueue publishQueue) =>
        {
            var server = await db.Servers.FindAsync(id);
            if (server == null) return Results.NotFound();
            if (server.SslMode != "auto") return Results.BadRequest("仅自动签发（ACME）模式的网站支持发起续期");
            // 更新中不允许再次点击：未超时的续期请求或正在进行中的签发都拦截
            if (server.SslRenewRequestedAt != null && DateTime.UtcNow - server.SslRenewRequestedAt < TimeSpan.FromMinutes(10))
                return Results.BadRequest("已有续期请求正在执行中，请等待完成或超时后再试");
            // 签发中但超过 10 分钟无进展：视为卡死（如无访问触发握手），允许重新发起
            if (server.SslState == "pending_issuing" &&
                !(server.SslLastAttemptAt != null && DateTime.UtcNow - server.SslLastAttemptAt > TimeSpan.FromMinutes(10)))
                return Results.BadRequest("证书正在签发中，请等待完成或失败后再试");
            if (server.HttpsPorts.Length == 0) server.HttpsPorts = [443];
            server.SslRenewRequestedAt = DateTime.UtcNow;
            server.SslState = "pending_issuing";
            server.SslLastError = "";
            db.CertEvents.Add(new CertEvent { ServerId = server.Id, NodeId = 0, Domain = string.Join(",", server.Domains), Status = "renew_requested", Error = "", CreatedAt = DateTime.UtcNow });
            await db.SaveChangesAsync();
            publishQueue.Enqueue(server.ClusterId);
            var version = await db.Clusters.Where(c => c.Id == server.ClusterId).Select(c => (long?)c.ConfigVersion).FirstOrDefaultAsync() ?? 0;
            return Results.Ok(new { ok = true, configVersion = version });
        });

        // 缓存配置：独立端点
        servers.MapPut("/{id:long}/cache", async (long id, CacheConfig input, CdnDbContext db, ConfigPublisher publisher, ConfigPublishQueue publishQueue, AuditService audit) =>
        {
            var server = await db.Servers.FindAsync(id);
            if (server == null) return Results.NotFound();
            server.Cache = input;
            await db.SaveChangesAsync();
            _ = audit.LogAsync($"servers.cache {id}", $"enabled={input.Enabled} rules={input.Rules.Count}");
            publishQueue.Enqueue(server.ClusterId); var version = await db.Clusters.Where(c => c.Id == server.ClusterId).Select(c => (long?)c.ConfigVersion).FirstOrDefaultAsync() ?? 0;
            return Results.Ok(new { ok = true, configVersion = version });
        });

        // 高级配置：URL跳转/重写/HTTP报头/访问鉴权/压缩/WebP/请求限制（独立端点，分层配置页使用）
        servers.MapPut("/{id:long}/advanced", async (long id, AdvancedConfig input, CdnDbContext db, ConfigPublishQueue publishQueue, AuditService audit) =>
        {
            var server = await db.Servers.FindAsync(id);
            if (server == null) return Results.NotFound();
            // BasicAuth 明文转 SHA256
            foreach (var u in input.BasicAuth.Users)
            {
                if (!string.IsNullOrEmpty(u.Plain))
                    u.PasswordHash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(u.Plain))).ToLowerInvariant();
                u.Plain = "";
            }
            server.Advanced = input;
            await db.SaveChangesAsync();
            _ = audit.LogAsync($"servers.advanced {id}", $"redirects={input.Redirects.Count} rewrites={input.Rewrites.Count} headers={input.Headers.Count} basicAuth={input.BasicAuth.Enabled}");
            publishQueue.Enqueue(server.ClusterId); var version = await db.Clusters.Where(c => c.Id == server.ClusterId).Select(c => (long?)c.ConfigVersion).FirstOrDefaultAsync() ?? 0;
            return Results.Ok(new { ok = true, configVersion = version });
        });

        // 安全配置：独立端点（CC/5秒盾/防盗链/IP名单/UA规则等）
        servers.MapPut("/{id:long}/security", async (long id, SecurityConfig input, CdnDbContext db, ConfigPublisher publisher, ConfigPublishQueue publishQueue, AuditService audit) =>
        {
            var server = await db.Servers.FindAsync(id);
            if (server == null) return Results.NotFound();
            server.Security = input;
            await db.SaveChangesAsync();
            _ = audit.LogAsync($"servers.security {id}", $"cc={input.Cc.Enabled} jsShield={input.JsShield.Enabled} urlAuth={input.UrlAuth.Enabled}");
            publishQueue.Enqueue(server.ClusterId); var version = await db.Clusters.Where(c => c.Id == server.ClusterId).Select(c => (long?)c.ConfigVersion).FirstOrDefaultAsync() ?? 0;
            return Results.Ok(new { ok = true, configVersion = version });
        });

        // 单站点配置读取（分层配置页拉取完整配置）
        servers.MapGet("/{id:long}/settings", async (long id, CdnDbContext db) =>
        {
            var server = await db.Servers.AsNoTracking().FirstOrDefaultAsync(s => s.Id == id);
            if (server == null) return Results.NotFound();
            return Results.Ok(new
            {
                server.Id, server.Domains, server.ClusterId, server.UserId,
                server.SslMode, server.ForceHttps, server.HttpsPorts, server.HttpPorts,
                server.ServerType, server.Enabled,
                origin = server.Origin,
                cache = server.Cache,
                security = server.Security,
                advanced = server.Advanced,
                sslState = server.SslState, sslExpiresAt = server.SslExpiresAt,
                sslIssuedDomains = server.SslIssuedDomains, sslLastError = server.SslLastError,
            });
        });

        // DNS 解析检测：站点首个域名的解析结果是否指向集群入口（CNAME/A）
        servers.MapGet("/{id:long}/dns-check", async (long id, CdnDbContext db) =>
        {
            var server = await db.Servers.AsNoTracking().FirstOrDefaultAsync(s => s.Id == id);
            if (server == null) return Results.NotFound();
            var domain = server.Domains.FirstOrDefault(d => !d.StartsWith('*')) ?? server.Domains.FirstOrDefault();
            if (domain == null) return Results.Ok(new { ok = false, error = "站点未绑定域名" });
            var clusterDns = await db.Clusters.Where(c => c.Id == server.ClusterId).Select(c => c.DnsDomain).FirstOrDefaultAsync() ?? "";
            var result = new List<object>();
            try
            {
                // CNAME 链 + A 记录
                var ips = await System.Net.Dns.GetHostAddressesAsync(domain);
                foreach (var ip in ips.Take(5)) result.Add(new { type = "A", value = ip.ToString() });
                var aliases = await LookupCnameAsync(domain);
                foreach (var alias in aliases.Take(5)) result.Add(new { type = "CNAME", value = alias });
            }
            catch (Exception ex)
            {
                return Results.Ok(new { ok = false, domain, error = "解析失败: " + ex.Message });
            }
            var pointed = result.Any(r => clusterDns != "" && ((dynamic)r).value?.ToString().Equals(clusterDns, StringComparison.OrdinalIgnoreCase) == true)
                || (clusterDns != "" && await ClusterDnsPointsHereAsync(clusterDns, result));
            return Results.Ok(new { ok = true, domain, clusterDns, records = result, pointsToCluster = pointed });
        });

        // ---- 配置模板：一键应用预设配置 ----
        var templates = admin.MapGroup("/site-templates");
        templates.MapGet("", async (CdnDbContext db) =>
            await db.SiteConfigTemplates.OrderBy(t => t.BuiltIn ? 0 : 1).ThenBy(t => t.Id).ToListAsync());

        templates.MapPost("", async (SiteConfigTemplate input, CdnDbContext db, AuditService audit) =>
        {
            if (string.IsNullOrWhiteSpace(input.Name)) return Results.BadRequest("模板名必填");
            input.BuiltIn = false;
            db.SiteConfigTemplates.Add(input);
            await db.SaveChangesAsync();
            _ = audit.LogAsync("site-templates.post ", input.Name);
            return Results.Created($"/api/admin/site-templates/{input.Id}", input);
        });

        templates.MapDelete("/{id:long}", async (long id, CdnDbContext db, AuditService audit) =>
        {
            var t = await db.SiteConfigTemplates.FindAsync(id);
            if (t == null) return Results.NotFound();
            if (t.BuiltIn) return Results.BadRequest("内置模板不可删除");
            db.SiteConfigTemplates.Remove(t);
            await db.SaveChangesAsync();
            _ = audit.LogAsync("site-templates.delete ", id.ToString());
            return Results.Ok();
        });

        // 一键应用模板到站点（只覆盖模板中出现的配置块）
        templates.MapPost("/{id:long}/apply", async (long id, ApplyTemplateRequest req, CdnDbContext db, ConfigPublisher publisher, ConfigPublishQueue publishQueue, AuditService audit) =>
        {
            var t = await db.SiteConfigTemplates.FindAsync(id);
            if (t == null) return Results.NotFound();
            var server = await db.Servers.FindAsync(req.ServerId);
            if (server == null) return Results.NotFound();

            var changed = new List<string>();
            try
            {
                using var doc = System.Text.Json.JsonDocument.Parse(t.ConfigJson);
                var root = doc.RootElement;
                if (root.TryGetProperty("sslMode", out var sslEl))
                {
                    var mode = sslEl.GetString();
                    if (mode is "auto" or "manual" or "off")
                    {
                        server.SslMode = mode;
                        if (mode == "off") server.HttpsPorts = [];
                        else if (server.HttpsPorts.Length == 0) server.HttpsPorts = [443];
                        changed.Add("sslMode");
                    }
                }
                if (root.TryGetProperty("forceHttps", out var fhEl))
                {
                    server.ForceHttps = fhEl.GetBoolean();
                    changed.Add("forceHttps");
                }
                if (root.TryGetProperty("cache", out var cacheEl) && cacheEl.ValueKind == System.Text.Json.JsonValueKind.Object)
                {
                    server.Cache = System.Text.Json.JsonSerializer.Deserialize<CacheConfig>(cacheEl.GetRawText(), AdminEndpointJson.JsonOpts) ?? server.Cache;
                    changed.Add("cache");
                }
                if (root.TryGetProperty("security", out var secEl) && secEl.ValueKind == System.Text.Json.JsonValueKind.Object)
                {
                    server.Security = System.Text.Json.JsonSerializer.Deserialize<SecurityConfig>(secEl.GetRawText(), AdminEndpointJson.JsonOpts) ?? server.Security;
                    changed.Add("security");
                }
            }
            catch (System.Text.Json.JsonException ex)
            {
                return Results.BadRequest($"模板配置解析失败: {ex.Message}");
            }

            await db.SaveChangesAsync();
            _ = audit.LogAsync($"site-templates.apply {t.Name}", $"server={req.ServerId} changed={string.Join(",", changed)}");
            publishQueue.Enqueue(server.ClusterId); var version = await db.Clusters.Where(c => c.Id == server.ClusterId).Select(c => (long?)c.ConfigVersion).FirstOrDefaultAsync() ?? 0;
            return Results.Ok(new { ok = true, applied = changed, configVersion = version });
        });


        // ---- CC 事件（从访问日志聚合 WAFAction=block 且原因含 CC 的记录）----
        // CC 解封：向全部集群下发 unban 指令，节点清除该 IP 的 CC 封禁状态
        admin.MapPost("/cc/unban", async (UnbanRequest req, CdnDbContext db, PurgeRegistry purges, AuditService audit) =>
        {
            var ip = (req.Ip ?? "").Trim();
            if (ip == "") return Results.BadRequest("ip 必填");
            var clusterIds = await db.Nodes.Select(n => n.ClusterId).Distinct().ToListAsync();
            foreach (var cid in clusterIds) purges.Enqueue(cid, "cc_unban", "", ip);
            _ = audit.LogAsync("cc.unban", ip);
            return Results.Ok(new { ok = true, ip, clusters = clusterIds.Count });
        });

        admin.MapGet("/cc", async (CdnDbContext db, int? page, int? pageSize) =>
        {
            var pg = Math.Max(1, page ?? 1); var sz = Math.Clamp(pageSize ?? 20, 1, 100);
            var q = db.AccessLogs.AsNoTracking().Where(l => l.WafAction == "block");
            var total = await q.CountAsync();
            var items = await q.OrderByDescending(l => l.Timestamp).Skip((pg - 1) * sz).Take(sz)
                .Select(l => new { l.Id, l.Host, l.Uri, l.RemoteAddr, l.BlockReason, l.Timestamp })
                .ToListAsync();
            return Results.Ok(new { total, items });
        });

        // ---- IP 名单聚合（各站点安全配置中的 IP 黑/白名单 + 区域封禁 CIDR）----
        admin.MapGet("/iplists", async (CdnDbContext db) =>
        {
            var servers = await db.Servers.AsNoTracking()
                .Select(s => new { s.Id, Domains = s.Domains, s.Security })
                .ToListAsync();
            var blacklist = servers.SelectMany(s => s.Security.IpBlacklist.Select(r => new
            {
                ip = r.Cidr, note = r.Note ?? "", serverId = s.Id, server = string.Join(",", s.Domains), type = "black",
            }));
            var whitelist = servers.SelectMany(s => s.Security.IpWhitelist.Select(r => new
            {
                ip = r.Cidr, note = r.Note ?? "", serverId = s.Id, server = string.Join(",", s.Domains), type = "white",
            }));
            return Results.Ok(blacklist.Concat(whitelist).ToList());
        });

        // ---- 边缘脚本（各站点 EdgeScripts 概览）----
        admin.MapGet("/user-scripts", async (CdnDbContext db) =>
        {
            // EdgeScripts 是 JSON 列成员，无法翻译成 SQL —— 先取全量再客户端过滤
            var servers = (await db.Servers.AsNoTracking()
                .Select(s => new { s.Id, Domains = s.Domains, s.Security })
                .ToListAsync())
                .Where(s => s.Security.EdgeScripts.Count > 0)
                .Select(s => new { s.Id, s.Domains, s.Security.EdgeScripts })
                .ToList();
            return Results.Ok(servers.Select(s => new
            {
                serverId = s.Id, server = string.Join(",", s.Domains),
                scripts = s.EdgeScripts,
            }));
        });

        // ---- DNS 三方接入配置（存 DnsSettings 表，管理员可编辑；凭证脱敏回显）----
        admin.MapGet("/dns/settings", async (CdnDbContext db, DnsSettingsService settings) =>
        {
            var row = await db.DnsSettings.FindAsync(1L);
            if (row == null)
            {
                // 兜底：启动种子未覆盖到时（历史库），建立空白默认行 —— 配置只存数据库，不读文件
                row = new DnsSettings { Id = 1, Enabled = false, Provider = "aggregated", ProvidersJson = "{}" };
                db.DnsSettings.Add(row);
                await db.SaveChangesAsync();
            }
            // 脱敏回显：非空 secret 字段返回占位符，前端不回填就不覆盖
            var providers = new Dictionary<string, object>();
            foreach (var (name, schema) in DnsSettingsService.ProviderSchema)
            {
                var fields = new Dictionary<string, string>();
                foreach (var k in schema.Keys.Concat(schema.SecretKeys))
                {
                    var v = settings.Field(name, k) ?? "";
                    fields[k] = schema.SecretKeys.Contains(k) && v != "" ? "******" : v;
                }
                providers[name] = fields;
            }
            return Results.Ok(new
            {
                enabled = row.Enabled, provider = row.Provider, defaultDomainId = row.DefaultDomainId,
                providers, updatedAt = row.UpdatedAt,
            });
        });

        admin.MapPut("/dns/settings", async (DnsSettingsInput input, CdnDbContext db, DnsSettingsService settings, DnsService dns) =>
        {
            var row = await db.DnsSettings.FindAsync(1L);
            if (row == null)
            {
                row = new DnsSettings { Id = 1 };
                db.DnsSettings.Add(row);
            }
            row.Enabled = input.Enabled;
            row.Provider = input.Provider;
            row.DefaultDomainId = input.DefaultDomainId ?? "";
            // 合并凭证：占位符 "******" 或空 = 保留原值（不覆盖已有凭证）
            var dict = string.IsNullOrEmpty(row.ProvidersJson)
                ? new Dictionary<string, JsonElement>() : JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(row.ProvidersJson) ?? new();
            foreach (var (provider, fields) in input.Providers ?? new())
            {
                if (!DnsSettingsService.ProviderSchema.ContainsKey(provider)) continue;
                var existing = dict.TryGetValue(provider, out var e) ? e :
                    JsonSerializer.SerializeToElement(new Dictionary<string, string>());
                var obj = new Dictionary<string, string>();
                if (existing.ValueKind == JsonValueKind.Object)
                    foreach (var kv in existing.EnumerateObject())
                        if (kv.Value.ValueKind == JsonValueKind.String) obj[kv.Name] = kv.Value.GetString() ?? "";
                foreach (var kv in fields)
                {
                    if (DnsSettingsService.ProviderSchema[provider].Keys.Concat(DnsSettingsService.ProviderSchema[provider].SecretKeys).All(k => k != kv.Key)) continue;
                    if (kv.Value == "******" || string.IsNullOrWhiteSpace(kv.Value)) continue; // 占位/空=保留
                    obj[kv.Key] = kv.Value.Trim();
                }
                dict[provider] = JsonSerializer.SerializeToElement(obj);
            }
            row.ProvidersJson = JsonSerializer.Serialize(dict);
            row.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync();
            settings.Invalidate(); // 立即生效
            _ = Task.Run(() => dns.SyncClusterRecordsAsync(1)); // 切换厂商/域名后重新同步集群解析
            return Results.Ok(new { ok = true });
        });

        // ---- DNS 凭证验证：用当前厂商列域名，成功即凭证有效 ----
        admin.MapPost("/dns/verify", async (CdnDbContext db, DnsSettingsService settings, IEnumerable<IDnsProvider> providers) =>
        {
            var name = settings.Provider;
            var provider = providers.FirstOrDefault(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
            if (provider == null) return Results.Ok(new { ok = false, error = $"未知厂商 {name}" });
            try
            {
                var domains = await provider.ListDomainsAsync();
                if (domains.Count == 0) return Results.Ok(new { ok = false, error = "凭证可能无效：未返回任何域名" });
                return Results.Ok(new { ok = true, provider = name, domains = domains.Count,
                    names = domains.Take(10).Select(d => d.Domain) });
            }
            catch (Exception ex)
            {
                return Results.Ok(new { ok = false, error = ex.Message });
            }
        });

        // ---- DNS 服务商账号状态（脱敏：仅返回厂商与启用态）----
        admin.MapGet("/dns/providers", (DnsSettingsService settings, IEnumerable<IDnsProvider> providers) =>
        {
            var active = settings.Provider;
            var list = providers.Select(p => new
            {
                name = p.Name,
                active = p.Name.Equals(active, StringComparison.OrdinalIgnoreCase),
                enabled = settings.Enabled && p.Name.Equals(active, StringComparison.OrdinalIgnoreCase),
            });
            return Results.Ok(new { enabled = settings.Enabled, active, items = list });
        });
        // ---- 日志查询 ----
        logs.MapGet("", async (CdnDbContext db, long? serverId, int? status, string? cacheStatus,
            string? remoteAddr, DateTime? from, DateTime? to, int page = 1, int size = 50) =>
        {
            var take = Math.Clamp(size, 1, 500);
            var q = db.AccessLogs.AsNoTracking().OrderByDescending(l => l.Timestamp);
            if (serverId.HasValue) q = (IOrderedQueryable<AccessLogRecord>)q.Where(l => l.ServerId == serverId);
            if (status.HasValue) q = (IOrderedQueryable<AccessLogRecord>)q.Where(l => l.Status == status);
            if (!string.IsNullOrEmpty(cacheStatus)) q = (IOrderedQueryable<AccessLogRecord>)q.Where(l => l.CacheStatus == cacheStatus);
            if (!string.IsNullOrEmpty(remoteAddr)) q = (IOrderedQueryable<AccessLogRecord>)q.Where(l => l.RemoteAddr == remoteAddr);
            if (from.HasValue) q = (IOrderedQueryable<AccessLogRecord>)q.Where(l => l.Timestamp >= from);
            if (to.HasValue) q = (IOrderedQueryable<AccessLogRecord>)q.Where(l => l.Timestamp <= to);

            var total = await q.CountAsync();
            var items = await q.Skip((page - 1) * take).Take(take).ToListAsync();
            return Results.Ok(new { total, page, size = take, items });
        });

        // ---- 统计 ----
        logs.MapGet("/stats", async (CdnDbContext db, DateTime? from, DateTime? to) =>
        {
            var f = from ?? DateTime.UtcNow.AddDays(-7); var t = to ?? DateTime.UtcNow;
            var q = db.AccessLogs.AsNoTracking().Where(l => l.Timestamp >= f && l.Timestamp <= t);
            var total = await q.CountAsync();
            var bytes = await q.SumAsync(l => (long?)l.BytesSent) ?? 0;
            var cacheHit = await q.CountAsync(l => l.CacheStatus == "HIT");
            var status4xx = await q.CountAsync(l => l.Status >= 400 && l.Status < 500);
            var status5xx = await q.CountAsync(l => l.Status >= 500);
            return Results.Ok(new
            {
                totalRequests = total, totalBytes = bytes,
                cacheHitRate = total == 0 ? 0 : Math.Round(cacheHit * 100.0 / total, 2),
                status4xx, status5xx
            });
        });
    }

    /// <summary>检查指定域名列表的一级域名是否有备案通过的站点（含本站自身、含通配符覆盖）。</summary>
    private static async Task<bool> CheckApexIcpAsync(CdnDbContext db, string[] domains, long selfId)
    {
        var apexes = domains.Where(d => !string.IsNullOrWhiteSpace(d) && !d.StartsWith('*'))
            .Select(d => IcpGate.RegistrableDomain(d)).Distinct().ToList();
        if (apexes.Count == 0) return true; // 仅通配符域名时不门禁
        var allServers = await db.Servers.AsNoTracking()
            .Where(s => s.IcpStatus == "approved")
            .Select(s => new { s.Id, s.Domains })
            .ToListAsync();
        foreach (var apex in apexes)
        {
            var hit = allServers.Any(s =>
                s.Domains.Any(d =>
                    IcpGate.RegistrableDomain(d) == apex ||
                    (d.StartsWith('*') && IcpGate.WildcardCovers(d, "check." + apex))));
            if (!hit) return false;
        }
        return true;
    }


    /// <summary>把集群入口域名再解析一次，比对 IP 是否同源（CNAME 链间接指向场景）。</summary>
    private static Task<List<string>> LookupCnameAsync(string domain) => Task.FromResult(new List<string>());

    private static async Task<bool> ClusterDnsPointsHereAsync(string clusterDns, List<object> records)
    {
        try
        {
            var addrs = await System.Net.Dns.GetHostAddressesAsync(clusterDns);
            var ips = addrs.Select(a => a.ToString()).ToHashSet();
            return records.Any(r => ips.Contains(((dynamic)r).value?.ToString()));
        }
        catch { return false; }
    }

    public record LoginRequest(string? Username, string? Password);

    public record CreateUserRequest(string Username, string Password, bool IsAdmin);
    public record UpdateUserRequest(string? Password, bool? IsAdmin, bool? Enabled);
    public record UnbanRequest(string Ip);

    /// <summary>向指定 DNS 服务器（节点内建权威 :53）发一次最小 UDP 查询，返回解析结果或错误。</summary>
    private static async Task<object> DnsQueryAsync(string serverIp, string name, string type, CancellationToken ct = default)
    {
        var qtype = type.ToUpperInvariant() switch { "AAAA" => 28, "CNAME" => 5, "NS" => 2, "TXT" => 16, _ => 1 };
        // 构造 DNS 报文：Header + QDCount=1
        var rnd = System.Security.Cryptography.RandomNumberGenerator.GetInt32(1, short.MaxValue);
        var buf = new List<byte> { (byte)(rnd >> 8), (byte)rnd, 0x01, 0x00, 0, 1, 0, 0, 0, 0, 0, 0 };
        foreach (var label in name.TrimEnd('.').Split('.'))
        {
            if (label.Length == 0 || label.Length > 63) return new { ok = false, error = "域名格式不正确" };
            buf.Add((byte)label.Length);
            buf.AddRange(System.Text.Encoding.ASCII.GetBytes(label));
        }
        buf.Add(0); // root
        buf.Add((byte)(qtype >> 8)); buf.Add((byte)qtype); // QTYPE
        buf.Add(0); buf.Add(1); // QCLASS=IN
        using var udp = new System.Net.Sockets.UdpClient();
        udp.Client.ReceiveTimeout = 4000;
        var server = System.Net.IPAddress.TryParse(serverIp, out var sip) ? sip : System.Net.IPAddress.Parse(serverIp);
        await udp.SendAsync(buf.ToArray(), buf.Count, new System.Net.IPEndPoint(server, 53));
        while (true)
        {
            var res = await udp.ReceiveAsync(ct);
            var data = res.Buffer;
            if (data.Length < 12) continue;
            if ((data[0] << 8 | data[1]) != rnd) continue; // 丢弃非本次事务 ID 的报文
            var rcode = data[3] & 0x0F;
            var anCount = data[6] << 8 | data[7];
            var answers = new List<string>();
            int pos = 12;
            // 跳过 Question
            int SkipName(int p) { while (p < data.Length && data[p] != 0) { if ((data[p] & 0xC0) == 0xC0) return p + 2; p += data[p] + 1; } return p + 1; }
            pos = SkipName(pos); pos += 4;
            for (int i = 0; i < anCount && pos + 10 <= data.Length; i++)
            {
                pos = SkipName(pos);
                int rtype = data[pos] << 8 | data[pos + 1];
                int rdLen = data[pos + 8] << 8 | data[pos + 9];
                int rd = pos + 10;
                if (rtype == 1 && rdLen == 4) answers.Add(string.Join(".", data[rd], data[rd + 1], data[rd + 2], data[rd + 3]));
                else if (rtype == 28 && rdLen == 16) answers.Add(System.Net.IPAddress.Parse(new byte[16].Select((_, k) => data[rd + k]).ToArray()).ToString());
                else if (rtype == 5 || rtype == 2 || rtype == 12)
                {
                    // 解析（可能压缩的）域名
                    var parts = new List<string>(); int q = rd, guard = 0;
                    bool jumped = false; int end = rd + rdLen;
                    while (q < data.Length && data[q] != 0 && guard++ < 64)
                    {
                        if ((data[q] & 0xC0) == 0xC0) { if (!jumped) end = Math.Min(end, q + 2); q = (data[q] & 0x3F) << 8 | data[q + 1]; jumped = true; continue; }
                        parts.Add(System.Text.Encoding.ASCII.GetString(data, q + 1, data[q])); q += data[q] + 1;
                    }
                    answers.Add(string.Join(".", parts));
                    pos = jumped ? end : rd + rdLen;
                    continue;
                }
                pos = rd + rdLen;
            }
            return new { ok = rcode == 0, server = serverIp, rcode, answers };
        }
    }

    public record ResolveTestRequest(string Name, string Type);

    public record CreateNodeRequest(string Name, long ClusterId);
    public record UpdateNodeRequest(string? Name, string? Line);
    public record UpgradeNodeRequest(string? Version, string? Url, string? Sha256);
    public record BatchOriginRequest(List<long> ServerIds, string NewHost, int? NewPort);
    public record CopyFromRequest(long FromId, List<string>? Parts);    public record PurgeRequest(string MatchType, string? Host, string? Value);
    public record CertUpload(long ClusterId, string CertPem, string KeyPem);
}

public record IcpReviewRequest(string Status, string? IcpNumber, string? Note);
public record IcpSubmitRequest(long ServerId, string? IcpNumber, string? Note);

public record ServerBatchRequest(List<long>? Ids, string? Action, long? ClusterId);
    public record DnsSettingsInput(bool Enabled, string Provider, string? DefaultDomainId, Dictionary<string, Dictionary<string, string>>? Providers);
public record SslSettingsRequest(string SslMode, int[]? HttpsPorts, bool ForceHttps);
public record ApplyTemplateRequest(long ServerId);

public sealed record RealtimePublishRequest(bool? Enabled);
