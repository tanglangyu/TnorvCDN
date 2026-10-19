using Microsoft.EntityFrameworkCore;
using TnorvCdn.Api.Core;
using System.Security.Claims;
using TnorvCdn.Api.Data;
using TnorvCdn.Api.Services;

namespace TnorvCdn.Api.Endpoints;

/// <summary>
/// 用户端 CDN 端点第二批：套餐/高防/负载均衡/WAF概览/域名/账单/工单/消息/缓存刷新 —— 支撑 UserConsole 全部页面真实数据。
/// </summary>
public static class UserCdnExtraEndpoints
{
    public static void MapUserCdnExtraEndpoints(this WebApplication app)
    {
        var grp = app.MapGroup("/api/user");

        // 套餐列表（可购套餐）
        grp.MapGet("/plans", async (CdnDbContext db) =>
        {
            var plans = await db.Plans.OrderBy(p => p.PriceCents).ToListAsync();
            return Results.Json(new { code = 200, message = "操作成功", data = plans });
        });

        // 我的订阅（费用账单）
        grp.MapGet("/subscriptions", async (CdnDbContext db, IConfiguration cfg) =>
        {
            var uid = await UserIdAsync(cfg);
            if (uid == null) return Unauthorized();
            var subs = await db.Subscriptions.Where(s => s.UserId == uid).OrderByDescending(s => s.Id).ToListAsync();
            var planNames = await db.Plans.ToDictionaryAsync(p => p.Id, p => p.Name);
            return Results.Json(new
            {
                code = 200, message = "操作成功",
                data = subs.Select(s => new
                {
                    s.Id, s.PlanId, s.StartsAt, s.ExpiresAt, s.Enabled, s.AutoRenew,
                    planName = planNames.GetValueOrDefault(s.PlanId, "套餐#" + s.PlanId),
                }).ToList(),
            });
        });

        // 自动续费开关（退订 = 关闭自动续费，到期自然失效）
        grp.MapPost("/subscriptions/{id:long}/auto-renew", async (long id, CdnDbContext db, IConfiguration cfg, Dictionary<string, bool> body) =>
        {
            var uid = await UserIdAsync(cfg);
            if (uid == null) return Unauthorized();
            var sub = await db.Subscriptions.FirstOrDefaultAsync(s => s.Id == id && s.UserId == uid);
            if (sub == null) return Results.Json(new { code = 404, message = "订阅不存在" });
            var on = body.TryGetValue("autoRenew", out var v) && v;
            sub.AutoRenew = on;
            await db.SaveChangesAsync();
            return Results.Json(new { code = 200, message = on ? "已开启自动续费" : "已关闭自动续费（到期自然失效）" });
        });

        // 升级套餐：差价 = (新-旧)月价 × 剩余天数/30；正差价余额扣款，负差价按比例延长到期日
        grp.MapPost("/subscriptions/upgrade", async (UpgradeRequest req, CdnDbContext db, IConfiguration cfg) =>
        {
            var uid = await UserIdAsync(cfg);
            if (uid == null) return Unauthorized();
            var sub = await db.Subscriptions.FirstOrDefaultAsync(s => s.Id == req.SubscriptionId && s.UserId == uid);
            if (sub == null) return Results.Json(new { code = 404, message = "订阅不存在" });
            if (!sub.Enabled) return Results.Json(new { code = 400, message = "订阅已停用，无法升级" });
            var newPlan = await db.Plans.FindAsync(req.PlanId);
            if (newPlan == null) return Results.Json(new { code = 404, message = "目标套餐不存在" });
            if (newPlan.Id == sub.PlanId) return Results.Json(new { code = 400, message = "与当前套餐相同" });
            var oldPlan = await db.Plans.FindAsync(sub.PlanId);
            var oldPrice = oldPlan?.PriceCents ?? 0;
            var now = DateTime.UtcNow;
            var remainDays = Math.Max(0, (decimal)(sub.ExpiresAt - now).TotalDays);
            var diff = Math.Round((newPlan.PriceCents - oldPrice) * remainDays / 30m);
            if (diff > 0)
            {
                var u = await db.Users.FindAsync(uid);
                if (u == null) return Results.Json(new { code = 404, message = "用户不存在" });
                if (u.BalanceCents < diff) return Results.Json(new { code = 400, message = $"升级差价 {(diff / 100m):0.00} 元，余额不足，请先充值" });
                u.BalanceCents -= diff;
                db.Orders.Add(new Order
                {
                    UserId = uid.Value,
                    OrderNo = "U" + now.ToString("yyyyMMddHHmmss") + Random.Shared.Next(1000, 9999),
                    Kind = "plan", PlanId = req.PlanId, Months = 0,
                    AmountCents = diff, Status = "paid", PayMethod = "balance", PaidAt = now,
                });
            }
            else if (diff < 0)
            {
                // 负差价按比例延长到期日（剩余天数 × 新旧价比延长）
                var extend = Math.Round(-diff / (newPlan.PriceCents > 0 ? newPlan.PriceCents : 1) * 30m);
                sub.ExpiresAt = (sub.ExpiresAt > now ? sub.ExpiresAt : now).AddDays((double)extend);
            }
            sub.PlanId = req.PlanId;
            await db.SaveChangesAsync();
            return Results.Json(new
            {
                code = 200, message = "升级成功",
                data = new { planName = newPlan.Name, chargedCents = Math.Max(0, diff), extendedDays = diff < 0 ? Math.Round(-diff / (newPlan.PriceCents > 0 ? newPlan.PriceCents : 1) * 30m) : 0 },
            });
        });

        // 高防实例（生效中）
        grp.MapGet("/protection", async (CdnDbContext db) =>
        {
            var list = await db.ProtectionInstances.Where(p => p.Status == "active").OrderByDescending(p => p.PeakMbps).ToListAsync();
            return Results.Json(new { code = 200, message = "操作成功", data = list });
        });

        // 负载均衡（L4 站点）
        grp.MapGet("/lb", async (CdnDbContext db, IConfiguration cfg) =>
        {
            var uid = await UserIdAsync(cfg);
            if (uid == null) return Unauthorized();
            var servers = await db.Servers.Where(s => s.UserId == uid && s.ServerType != "http").ToListAsync();
            return Results.Json(new
            {
                code = 200, message = "操作成功",
                data = servers.Select(s => new
                {
                    s.Id, s.Domains, s.ServerType, s.L4Ports, s.Enabled,
                    origin = s.Origin.Primaries.Select(o => $"{o.Host}:{o.Port}").ToList(),
                }).ToList(),
            });
        });

        // WAF 概览（近24h 我的站点拦截统计）
        grp.MapGet("/waf/summary", async (CdnDbContext db, IConfiguration cfg) =>
        {
            var uid = await UserIdAsync(cfg);
            if (uid == null) return Unauthorized();
            var since = DateTime.UtcNow.AddHours(-24);
            var serverIds = await db.Servers.Where(s => s.UserId == uid).Select(s => s.Id).ToListAsync();
            // P5 性能项：WAF 概览聚合 5 分钟短缓存（key 按 30 分钟桶对齐，跨桶自动失效）
            var bucket = DateTimeOffset.UtcNow.ToUnixTimeSeconds() / 1800;
            var cacheKey = $"waf-summary:{uid}:{bucket}:{string.Join(',', serverIds)}";
            var cached = Services.StatsCache.Get(cacheKey);
            if (cached != null) return Results.Content(cached, "application/json");
            var byReason = await db.AccessLogs
                .Where(l => serverIds.Contains(l.ServerId) && l.Timestamp >= since && l.WafAction == "block")
                .GroupBy(l => l.BlockReason ?? "unknown")
                .Select(g => new { reason = g.Key, count = g.Count() })
                .ToListAsync();
            // U4：按站点分组（防护矩阵页/概览下钻用）
            var rawByServer = await db.AccessLogs
                .Where(l => serverIds.Contains(l.ServerId) && l.Timestamp >= since && l.WafAction == "block")
                .GroupBy(l => l.ServerId)
                .Select(g => new { ServerId = g.Key, Count = g.Count() })
                .ToListAsync();
            var svNames = await db.Servers.Where(x => serverIds.Contains(x.Id))
                .ToDictionaryAsync(x => x.Id, x => x.Domains.FirstOrDefault() ?? ("站点#" + x.Id));
            var byServer = rawByServer.Select(x => new { serverId = x.ServerId, name = svNames.GetValueOrDefault(x.ServerId, "站点#" + x.ServerId), count = x.Count })
                .OrderByDescending(x => x.count).ToList();
            var wafPayload = System.Text.Json.JsonSerializer.Serialize(new { code = 200, message = "操作成功", data = new { blocked24h = byReason.Sum(r => r.count), byReason, byServer } });
            Services.StatsCache.Set(cacheKey, wafPayload);
            return Results.Content(wafPayload, "application/json");
        });

        // U4 防护矩阵：本人各站点的 CC/5秒盾/防盗链/IP黑名单 启用状态
        grp.MapGet("/waf/matrix", async (CdnDbContext db, IConfiguration cfg) =>
        {
            var uid = await UserIdAsync(cfg);
            if (uid == null) return Unauthorized();
            var servers = await db.Servers.Where(s => s.UserId == uid.Value && s.Enabled).OrderBy(s => s.Id).ToListAsync();
            return Results.Json(new
            {
                code = 200, message = "操作成功",
                data = servers.Select(s => new
                {
                    serverId = s.Id,
                    name = s.Domains.FirstOrDefault() ?? ("站点#" + s.Id),
                    ccEnabled = s.Security.Cc.Enabled,
                    ccThreshold = s.Security.Cc.Threshold,
                    jsShieldEnabled = s.Security.JsShield.Enabled,
                    refererEnabled = s.Security.Referers.Enabled,
                    ipBlacklist = s.Security.IpBlacklist.Count,
                    uaRules = s.Security.UaRules.Count,
                }).ToList(),
            });
        });

        // U4 拦截IP页「加黑名单」：写本人站点 Security.IpBlacklist（可指定单站，缺省全部启用站点）
        grp.MapPost("/waf/blacklist", async (WafBlacklistRequest req, CdnDbContext db, IConfiguration cfg, ConfigPublishQueue pq) =>
        {
            var uid = await UserIdAsync(cfg);
            if (uid == null) return Unauthorized();
            if (string.IsNullOrWhiteSpace(req.Ip)) return Results.Json(new { code = 400, message = "IP 必填" });
            var servers = await db.Servers.Where(s => s.UserId == uid.Value && s.Enabled).ToListAsync();
            if (req.ServerId is > 0) servers = servers.Where(s => s.Id == req.ServerId.Value).ToList();
            if (servers.Count == 0) return Results.Json(new { code = 404, message = "没有可应用的站点" });
            foreach (var s in servers)
            {
                var bl = s.Security.IpBlacklist;
                if (bl.Any(r => r.Cidr == req.Ip)) continue;
                bl.Add(new IpRule { Cidr = req.Ip, Note = req.Note ?? "来自拦截记录" });
                // 重新赋值确保 EF 检测 JSON 列变更
                s.Security = new SecurityConfig
                {
                    IpWhitelist = s.Security.IpWhitelist, IpBlacklist = bl,
                    Cc = s.Security.Cc, DeniedCountries = s.Security.DeniedCountries,
                    DeniedProvinces = s.Security.DeniedProvinces, DeniedCidrs = s.Security.DeniedCidrs,
                    UaRules = s.Security.UaRules, WafRules = s.Security.WafRules,
                    UrlAuth = s.Security.UrlAuth, JsShield = s.Security.JsShield,
                    Referers = s.Security.Referers, EdgeScripts = s.Security.EdgeScripts,
                    AiGuard = s.Security.AiGuard, BlockStatus = s.Security.BlockStatus,
                };
                pq.EnqueueUser(s.ClusterId, s.RealtimePublish);
            }
            await db.SaveChangesAsync();
            return Results.Json(new { code = 200, message = $"已加入 {servers.Count} 个站点的黑名单", ok = true });
        });

        // 我的域名
        grp.MapGet("/ns/domains-list", async (CdnDbContext db, IConfiguration cfg) =>
        {
            var uid = await UserIdAsync(cfg);
            if (uid == null) return Unauthorized();
            var servers = await db.Servers.Where(s => s.UserId == uid).ToListAsync();
            var domains = servers.SelectMany(s => s.Domains).Distinct()
                .Select(d => new { name = d, status = "active", line = "default", group = "CDN站点" })
                .ToList();
            return Results.Json(new { code = 200, message = "操作成功", data = domains });
        });

        // 我的工单
        grp.MapGet("/tickets", async (CdnDbContext db, IConfiguration cfg, string? q, string? status, int? page, int? pageSize) =>
        {
            var uid = await UserIdAsync(cfg);
            if (uid == null) return Unauthorized();
            var query = db.Tickets.Where(t => t.UserId == uid).AsQueryable();
            if (!string.IsNullOrWhiteSpace(status)) query = query.Where(t => t.Status == status);
            if (!string.IsNullOrWhiteSpace(q)) { var kw = q.ToLower(); query = query.Where(t => t.Title.ToLower().Contains(kw) || t.Content.ToLower().Contains(kw)); }
            query = query.OrderByDescending(t => t.CreatedAt);
            if (page != null || pageSize != null)
            {
                var pg = Math.Clamp(page ?? 1, 1, 1000);
                var psz = Math.Clamp(pageSize ?? 20, 1, 200);
                var total = await query.CountAsync();
                var items = await query.Skip((pg - 1) * psz).Take(psz).ToListAsync();
                return Results.Json(new { code = 200, message = "操作成功", data = new { total, page = pg, pageSize = psz, items } });
            }
            return Results.Json(new { code = 200, message = "操作成功", data = await query.Take(500).ToListAsync() });
        });
        grp.MapPost("/tickets", async (Ticket t, CdnDbContext db, IConfiguration cfg) =>
        {
            var uid = await UserIdAsync(cfg);
            if (uid == null) return Unauthorized();
            if (!await UserServerSettingsEndpoints.FeatureEnabledAsync(db, uid.Value, "tickets"))
                return Results.Json(new { code = 403, message = "管理员已停用您的工单功能，请联系管理员" });
            if (string.IsNullOrWhiteSpace(t.Title)) return Results.Json(new { code = 400, message = "标题必填" });
            t.Id = 0; t.UserId = uid.Value; t.Status = "open"; t.CreatedAt = DateTime.UtcNow;
            // U6：首条消息进入对话时间线（气泡式详情用）
            t.Messages = [new TicketMessage { From = "user", Content = t.Content, At = DateTime.UtcNow }];
            db.Tickets.Add(t);
            await db.SaveChangesAsync();
            return Results.Json(new { code = 200, message = "工单已提交", data = t });
        });

        // U6 工单详情（对话时间线；兼容旧 Reply 字段展开为客服消息）
        grp.MapGet("/tickets/{id:long}", async (long id, CdnDbContext db, IConfiguration cfg) =>
        {
            var uid = await UserIdAsync(cfg);
            if (uid == null) return Unauthorized();
            var t = await db.Tickets.FirstOrDefaultAsync(x => x.Id == id && x.UserId == uid.Value);
            if (t == null) return Results.Json(new { code = 404, message = "工单不存在" });
            var msgs = t.Messages ?? [];
            if (msgs.Count == 0 && !string.IsNullOrWhiteSpace(t.Content))
                msgs = [new TicketMessage { From = "user", Content = t.Content, At = t.CreatedAt }];
            if (!string.IsNullOrWhiteSpace(t.Reply))
                msgs = [.. msgs, new TicketMessage { From = "staff", Content = t.Reply, At = t.CreatedAt }];
            return Results.Json(new
            {
                code = 200, message = "操作成功",
                data = new { t.Id, t.Title, t.Status, t.Priority, t.Category, t.CreatedAt, messages = msgs },
            });
        });

        // U6 用户回复工单（追加到时间线；关闭工单不可回复；回复后状态回 open）
        grp.MapPost("/tickets/{id:long}/reply", async (long id, TicketReplyRequest req, CdnDbContext db, IConfiguration cfg) =>
        {
            var uid = await UserIdAsync(cfg);
            if (uid == null) return Unauthorized();
            var t = await db.Tickets.FirstOrDefaultAsync(x => x.Id == id && x.UserId == uid.Value);
            if (t == null) return Results.Json(new { code = 404, message = "工单不存在" });
            if (t.Status == "closed") return Results.Json(new { code = 400, message = "工单已关闭，如需继续请重新提交" });
            if (string.IsNullOrWhiteSpace(req.Content)) return Results.Json(new { code = 400, message = "回复内容必填" });
            t.Messages = [.. (t.Messages ?? []), new TicketMessage { From = "user", Content = req.Content, At = DateTime.UtcNow }];
            if (t.Status == "replied") t.Status = "open";
            await db.SaveChangesAsync();
            return Results.Json(new { code = 200, message = "已回复", ok = true });
        });

        // U6 用户关闭工单
        grp.MapPost("/tickets/{id:long}/close", async (long id, CdnDbContext db, IConfiguration cfg) =>
        {
            var uid = await UserIdAsync(cfg);
            if (uid == null) return Unauthorized();
            var t = await db.Tickets.FirstOrDefaultAsync(x => x.Id == id && x.UserId == uid.Value);
            if (t == null) return Results.Json(new { code = 404, message = "工单不存在" });
            t.Status = "closed";
            await db.SaveChangesAsync();
            return Results.Json(new { code = 200, message = "已关闭", ok = true });
        });

        // 消息（通知中心，预留空列表）
        grp.MapGet("/messages", () => Results.Json(new { code = 200, message = "操作成功", data = Array.Empty<object>() }));

        // 我的账单（订单作为账单条目）
        grp.MapGet("/finance/bills", async (CdnDbContext db, IConfiguration cfg, int? page, int? pageSize) =>
        {
            var uid = await UserIdAsync(cfg);
            if (uid == null) return Results.Json(new { code = 401, message = "未登录" });
            var pg = Math.Max(1, page ?? 1); var sz = (pageSize == null || pageSize <= 0) ? 20 : Math.Clamp(pageSize.Value, 1, 100);
            var q = db.Orders.AsNoTracking().Where(o => o.UserId == uid);
            var total = await q.CountAsync();
            var items = await q.OrderByDescending(o => o.CreatedAt).Skip((pg - 1) * sz).Take(sz).ToListAsync();
            return Results.Json(new
            {
                code = 200,
                message = "操作成功",
                data = new
                {
                    total,
                    items = items.Select(o => new
                    {
                        o.Id, o.OrderNo, o.Kind, o.Months, o.AmountCents, o.Status, o.PayMethod, o.CouponCode, o.CreatedAt, o.PaidAt,
                        kindLabel = o.Kind switch { "recharge" => "余额充值", "plan" => "购买套餐", "flow_package" => "购买流量包", _ => o.Kind },
                        amount = o.AmountCents / 100m,
                    }),
                },
            });
        });

        // 收支明细（同账单数据，按收支方向标注）
        grp.MapGet("/finance/logs", async (CdnDbContext db, IConfiguration cfg, int? page, int? pageSize) =>
        {
            var uid = await UserIdAsync(cfg);
            if (uid == null) return Results.Json(new { code = 401, message = "未登录" });
            var pg = Math.Max(1, page ?? 1); var sz = (pageSize == null || pageSize <= 0) ? 20 : Math.Clamp(pageSize.Value, 1, 100);
            var q = db.Orders.AsNoTracking().Where(o => o.UserId == uid && o.Status == "paid");
            var total = await q.CountAsync();
            var items = await q.OrderByDescending(o => o.CreatedAt).Skip((pg - 1) * sz).Take(sz).ToListAsync();
            return Results.Json(new
            {
                code = 200,
                message = "操作成功",
                data = new
                {
                    total,
                    items = items.Select(o => new
                    {
                        o.Id, o.OrderNo, o.Kind, o.AmountCents, o.CreatedAt, o.PaidAt,
                        direction = o.Kind == "recharge" ? "income" : "expense",
                        amount = o.AmountCents / 100m,
                        kindLabel = o.Kind switch { "recharge" => "充值", "plan" => "套餐消费", "flow_package" => "流量包消费", _ => o.Kind },
                    }),
                },
            });
        });

        // 可领取优惠券列表（含领取状态）
        // 优惠券兑换（D4 P4）：校验并绑定到当前用户
        grp.MapPost("/finance/coupons/redeem", async (CouponRedeemRequest req, CdnDbContext db, IConfiguration cfg) =>
        {
            var uid = await UserIdAsyncPublic(cfg);
            if (uid == null) return Results.Json(new { code = 401, message = "未登录" });
            var coupon = await db.Coupons.FirstOrDefaultAsync(c => c.Code == req.Code && c.Enabled && c.ExpireAt > DateTime.UtcNow && c.Used < c.MaxUses);
            if (coupon == null) return Results.Json(new { code = 400, message = "优惠券无效或已用完" });
            coupon.Used++;
            await db.SaveChangesAsync();
            return Results.Json(new { code = 200, message = "兑换成功", data = new { coupon.Code, coupon.DiscountPercent, coupon.ExpireAt } });
        });

        grp.MapGet("/finance/coupons", async (CdnDbContext db, IConfiguration cfg) =>
        {
            var uid = await UserIdAsync(cfg);
            if (uid == null) return Results.Json(new { code = 401, message = "未登录" });
            var coupons = await db.Coupons.AsNoTracking().OrderByDescending(c => c.CreatedAt).ToListAsync();
            return Results.Json(new { code = 200, message = "操作成功", data = coupons });
        });

        // 财务总览（U5）：余额 + 本月消费/充值 + 本月用量（对照套餐）+ 近 6 笔订单
        grp.MapGet("/finance/summary", async (CdnDbContext db, IConfiguration cfg) =>
        {
            var uid = await UserIdAsync(cfg);
            if (uid == null) return Results.Json(new { code = 401, message = "未登录" });
            var u = await db.Users.AsNoTracking().FirstOrDefaultAsync(x => x.Id == uid);
            if (u == null) return Results.Json(new { code = 404, message = "用户不存在" });

            var now = DateTime.UtcNow;
            var monthStart = DateTime.SpecifyKind(new DateTime(now.Year, now.Month, 1), DateTimeKind.Utc);
            var prevStart = monthStart.AddMonths(-1);
            var paid = db.Orders.AsNoTracking().Where(o => o.UserId == uid && o.Status == "paid");

            var monthOrders = await paid.Where(o => o.PaidAt >= monthStart).ToListAsync();
            var prevOrders = await paid.Where(o => o.PaidAt >= prevStart && o.PaidAt < monthStart).ToListAsync();
            var monthSpend = monthOrders.Where(o => o.Kind != "recharge").Sum(o => o.AmountCents);
            var monthRecharge = monthOrders.Where(o => o.Kind == "recharge").Sum(o => o.AmountCents);
            var prevSpend = prevOrders.Where(o => o.Kind != "recharge").Sum(o => o.AmountCents);

            // 本月用量（UsageRecord 由节点日志聚合任务按月 MERGE；缺失时 0）
            var usage = await db.UsageRecords.AsNoTracking()
                .FirstOrDefaultAsync(x => x.UserId == uid && x.Year == now.Year && x.Month == now.Month);

            // 当前套餐（用于流量进度环）
            var sub = await db.Subscriptions.AsNoTracking()
                .Where(s => s.UserId == uid && s.Enabled && s.ExpiresAt > now)
                .OrderByDescending(s => s.ExpiresAt).FirstOrDefaultAsync();
            var plan = sub == null ? null : await db.Plans.AsNoTracking().FirstOrDefaultAsync(p => p.Id == sub.PlanId);
            var trafficMb = plan == null ? 0 : plan.MonthlyTrafficMB;

            var recentRaw = await db.Orders.AsNoTracking().Where(o => o.UserId == uid)
                .OrderByDescending(o => o.Id).Take(6)
                .Select(o => new { o.OrderNo, o.Kind, o.AmountCents, o.Status, o.CreatedAt, o.PaidAt })
                .ToListAsync();
            var recentOrders = recentRaw.Select(o => new {
                o.OrderNo, o.Kind, o.AmountCents, o.Status, o.CreatedAt, o.PaidAt,
                kindLabel = o.Kind switch { "recharge" => "余额充值", "plan" => "购买套餐", "flow_package" => "购买流量包", _ => o.Kind },
                amount = o.AmountCents / 100m,
            }).ToList();

            return Results.Json(new
            {
                code = 200,
                message = "操作成功",
                data = new
                {
                    balance = u.BalanceCents / 100m,
                    monthSpend = monthSpend / 100m,
                    monthRecharge = monthRecharge / 100m,
                    spendGrowth = prevSpend > 0 ? Math.Round((monthSpend - prevSpend) * 100m / prevSpend, 0) : (object?)null,
                    usage = new
                    {
                        trafficMB = usage?.TrafficMB ?? 0,
                        requests = usage?.Requests ?? 0,
                        planTrafficMB = trafficMb,
                        trafficPercent = trafficMb > 0 ? Math.Round((usage?.TrafficMB ?? 0) * 100m / trafficMb, 1) : 0,
                        planName = plan?.Name ?? "",
                    },
                    recentOrders,
                },
            });
        });

        // 我的智能 DNS 域名（列表 + 分组过滤）
        grp.MapGet("/ns/domains", async (CdnDbContext db, IConfiguration cfg, string? group, string? q, int? page, int? pageSize) =>
        {
            var uid = await UserIdAsync(cfg);
            if (uid == null) return Results.Json(new { code = 401, message = "未登录" });
            var query = db.NsDomains.AsNoTracking().AsQueryable();
            if (!string.IsNullOrEmpty(group)) query = query.Where(d => d.Group == group);
            if (!string.IsNullOrWhiteSpace(q)) { var kw = q.ToLower(); query = query.Where(d => d.Name.ToLower().Contains(kw) || d.Value.ToLower().Contains(kw)); }
            query = query.OrderByDescending(d => d.CreatedAt);
            if (page != null || pageSize != null)
            {
                var pg = Math.Clamp(page ?? 1, 1, 1000);
                var psz = Math.Clamp(pageSize ?? 20, 1, 200);
                var total = await query.CountAsync();
                var items = await query.Skip((pg - 1) * psz).Take(psz).ToListAsync();
                return Results.Json(new { code = 200, message = "操作成功", data = new { total, page = pg, pageSize = psz, items } });
            }
            return Results.Json(new { code = 200, message = "操作成功", data = await query.Take(500).ToListAsync() });
        });

        // 我的线路列表（线路目录）
        grp.MapGet("/ns/routes", () =>
            Results.Json(new { code = 200, message = "操作成功", data = LineRegistry.Catalog.Select(c => new { c.Name, c.Desc }) }));

        // 我的 DNS 套餐（复用平台套餐，标记为 DNS 类）
        grp.MapGet("/ns/plans", async (CdnDbContext db) =>
        {
            var plans = await db.Plans.AsNoTracking().Where(p => p.Enabled).OrderBy(p => p.PriceCents).ToListAsync();
            return Results.Json(new { code = 200, message = "操作成功", data = plans });
        });

        // 我的高防实例
        grp.MapGet("/anti-ddos/instances", async (CdnDbContext db, IConfiguration cfg) =>
        {
            var uid = await UserIdAsync(cfg);
            var items = await db.ProtectionInstances.AsNoTracking().OrderByDescending(p => p.CreatedAt).ToListAsync();
            return Results.Json(new { code = 200, message = "操作成功", data = items });
        });

        // 高防可购套餐（流量包充当）
        grp.MapGet("/anti-ddos/packages", async (CdnDbContext db) =>
        {
            var items = await db.FlowPackages.AsNoTracking().OrderBy(p => p.PriceCents).ToListAsync();
            return Results.Json(new { code = 200, message = "操作成功", data = items });
        });

        // 高防购买（D4 P4）：余额购买流量包 → 创建/续期高防实例
        grp.MapPost("/anti-ddos/purchase", async (AntiDdosPurchaseRequest req, CdnDbContext db, IConfiguration cfg) =>
        {
            var uid = await UserIdAsyncPublic(cfg);
            if (uid == null) return Results.Json(new { code = 401, message = "未登录" });
            var u = await db.Users.FindAsync(uid);
            if (u == null) return Results.Json(new { code = 404, message = "用户不存在" });
            var pkg = await db.FlowPackages.FindAsync(req.PackageId);
            if (pkg == null || !pkg.Enabled) return Results.Json(new { code = 404, message = "高防套餐不存在" });
            if (u.BalanceCents < pkg.PriceCents) return Results.Json(new { code = 400, message = "余额不足，请先充值" });

            // 实例名 + 高防 IP 由节点池分配（演示环境取节点公网 IP）
            var nodeIp = await db.Nodes.Where(n => n.IsOnline).OrderBy(n => n.Id).Select(n => n.PublicIps).FirstOrDefaultAsync();
            var ip = (nodeIp != null && nodeIp.Length > 0) ? nodeIp[0] : "";
            var existing = await db.ProtectionInstances.FirstOrDefaultAsync(p => p.UserId == uid && p.Status == "active" && p.Name.StartsWith("我的高防"));
            u.BalanceCents -= pkg.PriceCents;
            if (existing != null)
            {
                existing.PeakMbps += pkg.Bytes * 8 / 1000000; // 按流量包大小折算带宽提升
                existing.ProtectedMbps += pkg.Bytes * 8 / 10000000;
                existing.ExpireAt = (existing.ExpireAt > DateTime.UtcNow ? existing.ExpireAt : DateTime.UtcNow).Value.AddDays(30);
                existing.ProtectedTargets = string.IsNullOrWhiteSpace(req.TargetDomains) ? existing.ProtectedTargets : req.TargetDomains;
            }
            else
            {
                db.ProtectionInstances.Add(new ProtectionInstance
                {
                    UserId = uid.Value,
                    Name = "我的高防-" + DateTime.UtcNow.ToString("yyyyMMddHHmm"),
                    Ip = ip,
                    Line = "BGP",
                    PeakMbps = pkg.Bytes * 8 / 1000000,
                    ProtectedMbps = Math.Max(100, pkg.Bytes * 8 / 10000000),
                    ProtectedTargets = req.TargetDomains ?? "",
                    Status = "active",
                    ExpireAt = DateTime.UtcNow.AddDays(30),
                });
            }
            db.Orders.Add(new Order
            {
                UserId = uid.Value,
                OrderNo = "A" + DateTime.UtcNow.ToString("yyyyMMddHHmmss") + Random.Shared.Next(1000, 9999),
                Kind = "flow_package", FlowPackageId = pkg.Id, Months = 1,
                AmountCents = pkg.PriceCents, Status = "paid", PayMethod = "balance", PaidAt = DateTime.UtcNow,
            });
            await db.SaveChangesAsync();
            return Results.Json(new { code = 200, message = "购买成功", data = new { peakMbps = pkg.Bytes * 8 / 1000000 } });
        });

        // 证书上传（用户端 P4）：PEM 解析域名/到期 → 归属校验 → 入库并触发集群配置下发
        grp.MapPost("/certs/upload", async (CertUploadRequest req, CdnDbContext db, IConfiguration cfg, ConfigPublishQueue pq) =>
        {
            var uid = await UserIdAsyncPublic(cfg);
            if (uid == null) return Results.Json(new { code = 401, message = "未登录" });
            if (string.IsNullOrWhiteSpace(req.CertPem) || string.IsNullOrWhiteSpace(req.KeyPem))
                return Results.Json(new { code = 400, message = "证书与私钥均必填" });

            System.Security.Cryptography.X509Certificates.X509Certificate2 cert;
            try
            {
                cert = System.Security.Cryptography.X509Certificates.X509Certificate2.CreateFromPem(req.CertPem, req.KeyPem);
            }
            catch (Exception ex)
            {
                return Results.Json(new { code = 400, message = $"证书解析失败: {ex.Message}" });
            }
            var domains = cert.GetNameInfo(System.Security.Cryptography.X509Certificates.X509NameType.DnsFromAlternativeName, false);
            if (string.IsNullOrEmpty(domains))
                domains = cert.GetNameInfo(System.Security.Cryptography.X509Certificates.X509NameType.SimpleName, false);

            // 归属校验：证书域名须属于当前用户的站点域名（含泛域名父域）
            var myServers = await db.Servers.Where(s => s.UserId == uid).Select(s => s.Domains).ToListAsync();
            var myDomains = myServers.SelectMany(d => d).ToList();
            var apexes = myDomains.Where(d => !d.StartsWith('*')).Select(IcpGate.RegistrableDomain).ToHashSet();
            var cd = (domains ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (cd.Length == 0 || !cd.Any(d => myDomains.Contains(d) || apexes.Contains(IcpGate.RegistrableDomain(d))))
                return Results.Json(new { code = 403, message = "证书域名不属于你名下任何站点，禁止上传" });

            // 同域重复上传：替换旧的手动证书
            var old = await db.Certificates.FirstOrDefaultAsync(c => c.Source == "manual" && c.Domains == domains);
            if (old != null)
            {
                old.CertPem = req.CertPem; old.KeyPem = req.KeyPem;
                old.ExpiresAt = cert.NotAfter.ToUniversalTime();
                old.CreatedAt = DateTime.UtcNow;
            }
            else
            {
                var clusterId = await db.Clusters.Where(c => c.Enabled).OrderBy(c => c.Id).Select(c => (long)c.Id).FirstOrDefaultAsync();
                db.Certificates.Add(new Certificate
                {
                    ClusterId = clusterId, Domains = domains ?? "", CertPem = req.CertPem, KeyPem = req.KeyPem,
                    ExpiresAt = cert.NotAfter.ToUniversalTime(), Source = "manual",
                });
            }
            await db.SaveChangesAsync();
            var clusterIds = await db.Clusters.Select(c => c.Id).ToListAsync();
            foreach (var cid in clusterIds) pq.Enqueue(cid);
            return Results.Json(new { code = 200, message = "证书已上传并下发", data = new { domains, expiresAt = cert.NotAfter.ToUniversalTime() } });
        });

        // 我的智能 DNS 域名 —— 记录 CRUD（用户侧记录级管理）
        grp.MapPost("/ns/domains", async (NsDomain d, CdnDbContext db, IConfiguration cfg, Services.DnsService dns) =>
        {
            var uid = await UserIdAsync(cfg);
            if (uid == null) return Unauthorized();
            var (okNs, msgNs) = await Services.PlanGate.AllowAsync(db, uid.Value, "ns");
            if (!okNs) return Results.Json(new { code = 403, message = msgNs });
            if (string.IsNullOrWhiteSpace(d.Name)) return Results.Json(new { code = 400, message = "域名必填" });
            d.UserId = uid.Value;
            if (string.IsNullOrEmpty(d.Type)) d.Type = "A";
            if (string.IsNullOrEmpty(d.Line)) d.Line = "default";
            if (d.Ttl <= 0) d.Ttl = 600;
            d.Status = "active";
            d.SyncStatus = "";
            // NS托管接入页添加的委派型记录：标记 Kind=delegation，委派检测状态才能回写
            if (d.Kind != "record") d.Kind = "delegation";
            db.NsDomains.Add(d);
            await db.SaveChangesAsync();
            d.SyncStatus = await dns.SyncUserRecordAsync(d.Name, d.Type, d.Value, d.Ttl, d.Line);
            await db.SaveChangesAsync();
            await BumpDnsVersionAsync(db);
            return Results.Json(new { code = 200, message = d.SyncStatus == "synced" ? "已创建并生效" : "已创建", data = d });
        });

        grp.MapPut("/ns/domains/{id:long}", async (long id, NsDomain input, CdnDbContext db, IConfiguration cfg, Services.DnsService dns) =>
        {
            var uid = await UserIdAsync(cfg);
            if (uid == null) return Unauthorized();
            var d = await db.NsDomains.FirstOrDefaultAsync(x => x.Id == id && x.UserId == uid.Value);
            if (d == null) return Results.Json(new { code = 404, message = "记录不存在" }, statusCode: 200);
            d.Name = input.Name; d.Group = input.Group; d.Line = input.Line;
            d.Type = input.Type; d.Value = input.Value; d.Ttl = input.Ttl <= 0 ? 600 : input.Ttl;
            await db.SaveChangesAsync();
            d.SyncStatus = await dns.SyncUserRecordAsync(d.Name, d.Type, d.Value, d.Ttl, d.Line);
            await db.SaveChangesAsync();
            await BumpDnsVersionAsync(db);
            return Results.Json(new { code = 200, message = d.SyncStatus == "synced" ? "已保存并生效" : "已保存", data = d });
        });

        grp.MapDelete("/ns/domains/{id:long}", async (long id, CdnDbContext db, IConfiguration cfg, Services.DnsService dns) =>
        {
            var uid = await UserIdAsync(cfg);
            if (uid == null) return Unauthorized();
            var d = await db.NsDomains.FirstOrDefaultAsync(x => x.Id == id && x.UserId == uid.Value);
            if (d == null) return Results.Json(new { code = 200, message = "记录不存在" });
            db.NsDomains.Remove(d);
            await db.SaveChangesAsync();
            // 同步删除三方 DNS 记录（主控先删库，DNS 删除失败不影响本地）
            await dns.DeleteUserRecordAsync(d.Name, d.Type, d.Line);
            await BumpDnsVersionAsync(db);
            return Results.Json(new { code = 200, message = "已删除" });
        });

        // 批量操作：批量启用/停用/删除
        // NS 记录重新同步（归属校验；失败重试入口）
        grp.MapPost("/ns/domains/{id:long}/sync", async (long id, CdnDbContext db, IConfiguration cfg, Services.DnsService dns) =>
        {
            var uid = await UserIdAsync(cfg);
            if (uid == null) return Unauthorized();
            var d = await db.NsDomains.FirstOrDefaultAsync(x => x.Id == id && x.UserId == uid.Value);
            if (d == null) return Results.NotFound();
            var status = await dns.SyncUserRecordAsync(d.Name, d.Type, d.Value, d.Ttl, d.Line);
            d.SyncStatus = status;
            await db.SaveChangesAsync();
            return Results.Ok(new { ok = status == "synced", status });
        });

        grp.MapPost("/ns/domains/batch", async (NsBatchRequest req, CdnDbContext db, IConfiguration cfg) =>
        {
            var uid = await UserIdAsync(cfg);
            if (uid == null) return Unauthorized();
            var ids = req.Ids ?? [];
            var items = await db.NsDomains.Where(x => x.UserId == uid.Value && ids.Contains(x.Id)).ToListAsync();
            foreach (var d in items)
            {
                switch ((req.Action ?? "").ToLowerInvariant())
                {
                    case "delete": db.NsDomains.Remove(d); break;
                    case "enable": d.Status = "active"; break;
                    case "disable": d.Status = "paused"; break;
                }
            }
            await db.SaveChangesAsync();
            if (items.Count > 0) await BumpDnsVersionAsync(db);
            return Results.Json(new { code = 200, message = $"已处理 {items.Count} 条" });
        });

        // ---- NS 托管接入（智能DNS接入）：委派检测 + 接入信息 ----

        // 可用接入节点（NS 托管时用户把子域 NS 指到这些主机）
        grp.MapGet("/ns/access-info", async (CdnDbContext db, IConfiguration cfg) =>
        {
            // NS 接入主机名：取集群配置的 DnsDomain（需先在集群设置里填写），无则提示配置
            var dnsDomain = await db.Clusters.AsNoTracking()
                .Where(c => c.DnsDomain != null && c.DnsDomain != "")
                .Select(c => c.DnsDomain).FirstOrDefaultAsync();
            var clusterDnsHost = string.IsNullOrEmpty(dnsDomain) ? "ns1.<YOUR_DNS_HOST>" : "ns1." + dnsDomain;
            var nodes = await db.Nodes.AsNoTracking()
                .Where(n => n.IsOnline && n.Role == "edge")
                .OrderBy(n => n.Id)
                .Select(n => new { n.Id, n.Name, n.ClusterId, ips = n.PublicIps, n.Line })
                .ToListAsync();
            return Results.Json(new
            {
                code = 200, message = "操作成功",
                data = new
                {
                    nsHosts = nodes.Select(n => new { name = clusterDnsHost, ip = n.ips.FirstOrDefault() ?? "", node = n.Name, line = string.IsNullOrWhiteSpace(n.Line) ? "default" : n.Line })
                        .Where(x => x.ip != "").Distinct().ToList(),
                    nodes,
                },
            });
        });

        // 委派检测：查询权威链是否已把该域名指到本系统节点（纯 UDP DNS 探测，不依赖外部 API）
        grp.MapPost("/ns/delegation-check", async (DelegationCheckRequest req, CdnDbContext db, IConfiguration cfg) =>
        {
            var uid = await UserIdAsync(cfg);
            if (uid == null) return Unauthorized();
            var name = (req.Name ?? "").Trim().ToLowerInvariant();
            if (name.Length < 4) return Results.Json(new { code = 400, message = "域名无效" });

            var nodeIps = await db.Nodes.AsNoTracking().Where(n => n.IsOnline && n.Role == "edge")
                .SelectMany(n => n.PublicIps).Distinct().ToListAsync();
            if (nodeIps.Count == 0) return Results.Json(new { code = 500, message = "无在线接入节点" });

            // 用系统 DNS 解析该域名的 A 记录，命中任一节点 IP 即视为委派生效
            var resolved = new List<string>();
            string? err = null;
            try
            {
                var addrs = await System.Net.Dns.GetHostAddressesAsync(name);
                resolved = addrs.Select(a => a.ToString()).ToList();
            }
            catch (Exception ex) { err = ex.Message; }

            var hit = resolved.Any(r => nodeIps.Contains(r));
            // 同步更新委派记录状态
            var rec = await db.NsDomains.FirstOrDefaultAsync(d => d.Name == name && (d.Kind == "delegation" || d.Type == "NS"));
            if (rec != null)
            {
                rec.Delegated = hit ? "ok" : "failed";
                rec.DelegatedCheckedAt = DateTime.UtcNow;
                await db.SaveChangesAsync();
            }
            return Results.Json(new
            {
                code = 200, message = "操作成功",
                data = new { name, delegated = hit, resolved, nodeIps, error = err,
                    checkedAt = DateTime.UtcNow, hint = hit ? "委派已生效，智能 DNS 由本系统接管" : "未检测到本系统节点 IP，请检查 NS 记录是否已指向接入主机并等待生效（TTL 内可能延迟）" },
            });
        });

        // 我的概览（用户端看板统计卡）：站点数/今日流量/请求数/拦截/命中率
        grp.MapGet("/overview", async (CdnDbContext db, IConfiguration cfg) =>
        {
            var uid = await UserIdAsync(cfg);
            if (uid == null) return Unauthorized();
            var sinceToday = DateTime.UtcNow.Date;
            var serverIds = await db.Servers.Where(s => s.UserId == uid.Value).Select(s => s.Id).ToListAsync();
            var logs = db.AccessLogs.AsNoTracking().Where(l => serverIds.Contains(l.ServerId) && l.Timestamp >= sinceToday);
            var requests = await logs.CountAsync();
            var hits = await logs.CountAsync(l => l.CacheStatus == "HIT");
            var blocked = await logs.CountAsync(l => l.WafAction == "block");
            var bytes = await logs.SumAsync(l => (long?)l.BytesSent) ?? 0;
            return Results.Json(new
            {
                code = 200, data = new
                {
                    servers = serverIds.Count,
                    todayRequests = requests,
                    todayBlocked = blocked,
                    todayBytes = bytes,
                    cacheHitRate = requests > 0 ? Math.Round(hits * 100.0 / requests, 1) : 0,
                },
            });
        });

        // 我的访客地区分布（近 24h，GeoIP）
        grp.MapGet("/geo/summary", async (CdnDbContext db, IConfiguration cfg, GeoIpService geo, int? hours) =>
        {
            var uid = await UserIdAsync(cfg);
            if (uid == null) return Unauthorized();
            var since = DateTime.UtcNow.AddHours(-(hours ?? 24));
            var serverIds = await db.Servers.Where(s => s.UserId == uid.Value).Select(s => s.Id).ToListAsync();
            var ips = await db.AccessLogs.AsNoTracking()
                .Where(l => l.Timestamp >= since && serverIds.Contains(l.ServerId))
                .Select(l => l.RemoteAddr)
                .Distinct()
                .ToListAsync();
            var dist = geo.Distribution(ips);
            return Results.Json(new { code = 200, message = "操作成功", data = new { totalIps = ips.Count, items = dist.Select(d => new { country = d.Country, count = d.Count }) } });
        });

        // 我的访问日志（全部站点，分页）
        grp.MapGet("/access-logs", async (CdnDbContext db, IConfiguration cfg, int? page, int? pageSize) =>
        {
            var uid = await UserIdAsync(cfg);
            if (uid == null) return Results.Json(new { code = 401, message = "未登录" });
            var pg = Math.Max(1, page ?? 1); var sz = (pageSize == null || pageSize <= 0) ? 20 : Math.Clamp(pageSize.Value, 1, 100);
            var serverIds = await db.Servers.Where(s => s.UserId == uid).Select(s => s.Id).ToListAsync();
            var q = db.AccessLogs.AsNoTracking().Where(l => serverIds.Contains(l.ServerId));
            var total = await q.CountAsync();
            var items = await q.OrderByDescending(l => l.Timestamp).Skip((pg - 1) * sz).Take(sz)
                .Select(l => new { l.Id, l.Host, l.Uri, l.Status, l.CacheStatus, l.RemoteAddr, l.Timestamp }).ToListAsync();
            return Results.Json(new { code = 200, message = "操作成功", data = new { total, items } });
        });

        // 刷新缓存（仅自己的域名）
        grp.MapPost("/servers/cache/purge", async (PurgeUserRequest req, CdnDbContext db, IConfiguration cfg, PurgeRegistry purges, ConfigPublishQueue publishQueue) =>
        {
            var uid = await UserIdAsync(cfg);
            if (uid == null) return Unauthorized();
            if (req.MatchType is not ("url" or "directory" or "host" or "all"))
                return Results.Json(new { code = 400, message = "matchType 无效" });
            if (req.MatchType != "all" && !string.IsNullOrEmpty(req.Host))
            {
                // Domains 为值转换的集合，EF 无法翻译 Contains —— 取到客户端再判断
                var myDomains = await db.Servers.Where(s => s.UserId == uid).Select(s => s.Domains).ToListAsync();
                if (!myDomains.Any(ds => ds.Contains(req.Host))) return Results.Json(new { code = 403, message = "无权刷新该域名" });
            }
            var clusterIds = await db.Nodes.Select(n => n.ClusterId).Distinct().ToListAsync();
            var queued = 0;
            foreach (var cid in clusterIds)
            {
                purges.Enqueue(cid, req.MatchType, req.Host ?? "", req.Value ?? "");
                publishQueue.Enqueue(cid); // 指令随配置快照下发；不 bump 版本节点拉取时早退收不到刷新
                queued++;
            }
            return Results.Json(new { code = 200, message = "刷新指令已下发", data = new { queued } });
        });
    }

    public record PurgeUserRequest(string MatchType, string? Host, string? Value);
    public record NsBatchRequest(long[]? Ids, string? Action);
    public record DelegationCheckRequest(string? Name);
    public record UpgradeRequest(long SubscriptionId, long PlanId);

    private static IResult Unauthorized() => Results.Json(new { code = 401, message = "未登录" });

    internal static Task<long?> UserIdAsyncPublic(IConfiguration cfg) => UserIdAsync(cfg);

    private static async Task<long?> UserIdAsync(IConfiguration cfg)
    {
        var http = new HttpContextAccessor().HttpContext;
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
            var raw = principal.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            return long.TryParse(raw, out var id) ? id : null;
        }
        catch { return null; }
    }

    public sealed record WafBlacklistRequest(string Ip, string? Note, long? ServerId);
    public sealed record TicketReplyRequest(string Content);
    // 智能 DNS 记录变更后 bumped 所有集群配置版本 → 节点拉取到新 dnsRecords 快照
    private static async Task BumpDnsVersionAsync(CdnDbContext db)
    {
        await db.Clusters.ExecuteUpdateAsync(u => u.SetProperty(c => c.ConfigVersion, DateTimeOffset.UtcNow.ToUnixTimeSeconds()));
    }

    public record AntiDdosPurchaseRequest(long PackageId, string? TargetDomains);

    public record CouponRedeemRequest(string Code);

    // 证书上传（用户端）：解析 PEM 提取域名/到期，归属校验后入库；复用集群配置下发到节点
    public sealed record CertUploadRequest(string CertPem, string KeyPem);

}
/// <summary>
/// 余额/订单/支付端点：余额充值（支付宝/微信扫码为模拟网关，回调即入账）、套餐购买、流量包购买。
/// 生产对接真实支付时仅替换 PayGateway 的二维码生成与回调验签。
/// </summary>
public static class UserPayEndpoints
{
    public static void MapUserPayEndpoints(this WebApplication app)
    {
        var grp = app.MapGroup("/api/user");

        // 可购流量包（用户端）
        grp.MapGet("/flow-packages", async (CdnDbContext db) =>
            await db.FlowPackages.Where(p => p.Enabled).OrderBy(p => p.PriceCents).ToListAsync());

        // 账户信息（余额）
        grp.MapGet("/account", async (CdnDbContext db, IConfiguration cfg) =>
        {
            var uid = await UidAsync(cfg);
            if (uid == null) return Results.Json(new { code = 401, message = "未登录" });
            if (!await UserServerSettingsEndpoints.FeatureEnabledAsync(db, uid.Value, "finance"))
                return Results.Json(new { code = 403, message = "管理员已停用您的购买功能，请联系管理员" });
            var u = await db.Users.FindAsync(uid);
            if (u == null) return Results.Json(new { code = 404, message = "用户不存在" });
            return Results.Ok(new { id = u.Id, username = u.Username, balanceCents = u.BalanceCents });
        });

        // 我的订单
        grp.MapGet("/orders", async (CdnDbContext db, IConfiguration cfg) =>
        {
            var uid = await UidAsync(cfg);
            if (uid == null) return Results.Json(new { code = 401, message = "未登录" });
            var list = await db.Orders.Where(o => o.UserId == uid).OrderByDescending(o => o.Id)
                .Take(200).Select(o => new { o.Id, o.OrderNo, o.Kind, o.Months, o.AmountCents, o.Status, o.PayMethod, o.CouponCode, o.CreatedAt, o.PaidAt }).ToListAsync();
            return Results.Ok(list);
        });

        // 创建订单：充值 / 购买套餐 / 购买流量包。payMethod=balance 直接扣款完成；alipay/wechat 返回模拟支付链接
        grp.MapPost("/orders", async (OrderRequest req, CdnDbContext db, IConfiguration cfg) =>
        {
            var uid = await UidAsync(cfg);
            if (uid == null) return Results.Json(new { code = 401, message = "未登录" });
            var u = await db.Users.FindAsync(uid);
            if (u == null) return Results.Json(new { code = 404, message = "用户不存在" });

            decimal amount = 0; long? planId = null, pkgId = null; string kind = req.Kind ?? "recharge";
            if (kind == "plan")
            {
                var plan = await db.Plans.FindAsync(req.PlanId);
                if (plan == null) return Results.Json(new { code = 404, message = "套餐不存在" });
                planId = plan.Id;
                amount = plan.PriceCents * Math.Max(1, req.Months);
            }
            else if (kind == "flow_package")
            {
                var pkg = await db.FlowPackages.FindAsync(req.FlowPackageId);
                if (pkg == null || !pkg.Enabled) return Results.Json(new { code = 404, message = "流量包不存在" });
                pkgId = pkg.Id;
                amount = pkg.PriceCents;
            }
            else
            {
                kind = "recharge";
                amount = req.AmountCents;
                if (amount < 100) return Results.Json(new { code = 400, message = "最低充值 1 元" });
            }

            // 优惠券折扣
            string? couponCode = null;
            if (!string.IsNullOrWhiteSpace(req.CouponCode))
            {
                var coupon = await db.Coupons.FirstOrDefaultAsync(c => c.Code == req.CouponCode && c.Enabled && c.ExpireAt > DateTime.UtcNow && c.Used < c.MaxUses);
                if (coupon == null) return Results.Json(new { code = 400, message = "优惠券无效" });
                amount = Math.Max(1, Math.Round(amount * coupon.DiscountPercent / 100m));
                coupon.Used++;
                couponCode = coupon.Code;
            }

            var payMethod = string.IsNullOrWhiteSpace(req.PayMethod) ? "balance" : req.PayMethod;
            var order = new Order
            {
                UserId = uid.Value,
                OrderNo = "T" + DateTime.UtcNow.ToString("yyyyMMddHHmmss") + Random.Shared.Next(1000, 9999),
                Kind = kind, PlanId = planId, FlowPackageId = pkgId,
                Months = Math.Max(1, req.Months),
                AmountCents = amount,
                PayMethod = payMethod,
                CouponCode = couponCode,
                Status = "pending",
            };

            if (payMethod == "balance")
            {
                if (kind == "recharge") return Results.Json(new { code = 400, message = "充值订单不能选择余额支付" });
                if (u.BalanceCents < amount) return Results.Json(new { code = 400, message = "余额不足，请先充值" });
                u.BalanceCents -= amount;
                order.Status = "paid";
                order.PaidAt = DateTime.UtcNow;
                await ApplyPaidAsync(db, order);
            }
            // alipay / wechat：模拟扫码支付网关（真实对接时替换为商户预下单 + 异步回调验签）
            db.Orders.Add(order);
            await db.SaveChangesAsync();

            return Results.Ok(new
            {
                orderNo = order.OrderNo,
                amountCents = order.AmountCents,
                status = order.Status,
                payUrl = payMethod == "balance" ? null : $"/user/pay-sim?orderNo={order.OrderNo}&method={payMethod}",
            });
        });

        // 模拟支付回调（真实部署时由支付网关 POST 验签调用；此处供演示环境完成支付闭环）
        grp.MapPost("/orders/{orderNo}/pay-callback", async (string orderNo, CdnDbContext db) =>
        {
            var order = await db.Orders.FirstOrDefaultAsync(o => o.OrderNo == orderNo);
            if (order == null) return Results.Json(new { code = 404, message = "订单不存在" });
            if (order.Status == "paid") return Results.Ok(new { ok = true, message = "已支付" });
            order.Status = "paid";
            order.PaidAt = DateTime.UtcNow;
            if (order.Kind == "recharge")
            {
                var u = await db.Users.FindAsync(order.UserId);
                if (u != null) u.BalanceCents += order.AmountCents;
            }
            else
            {
                await ApplyPaidAsync(db, order);
            }
            await db.SaveChangesAsync();
            return Results.Ok(new { ok = true });
        });

        // 取消订单
        grp.MapPost("/orders/{orderNo}/cancel", async (string orderNo, CdnDbContext db, IConfiguration cfg) =>
        {
            var uid = await UidAsync(cfg);
            if (uid == null) return Results.Json(new { code = 401, message = "未登录" });
            var order = await db.Orders.FirstOrDefaultAsync(o => o.OrderNo == orderNo && o.UserId == uid);
            if (order == null) return Results.Json(new { code = 404, message = "订单不存在" });
            if (order.Status != "pending") return Results.Json(new { code = 400, message = "订单状态不可取消" });
            order.Status = "cancelled";
            await db.SaveChangesAsync();
            return Results.Ok(new { ok = true });
        });
    }

    /// <summary>支付成功后的业务落地：套餐→订阅续期/新建。</summary>
    private static async Task ApplyPaidAsync(CdnDbContext db, Order order)
    {
        if (order.Kind == "plan" && order.PlanId != null)
        {
            var existing = await db.Subscriptions.FirstOrDefaultAsync(s => s.UserId == order.UserId && s.PlanId == order.PlanId);
            if (existing != null)
            {
                existing.ExpiresAt = (existing.ExpiresAt > DateTime.UtcNow ? existing.ExpiresAt : DateTime.UtcNow).AddMonths(order.Months);
            }
            else
            {
                db.Subscriptions.Add(new Subscription
                {
                    UserId = order.UserId, PlanId = order.PlanId.Value,
                    StartsAt = DateTime.UtcNow,
                    ExpiresAt = DateTime.UtcNow.AddMonths(order.Months),
                });
            }
        }
    }

    private static async Task<long?> UidAsync(IConfiguration cfg) => await UserCdnExtraEndpoints.UserIdAsyncPublic(cfg);

    public record OrderRequest(string? Kind, long? PlanId, long? FlowPackageId, int Months, decimal AmountCents, string? PayMethod, string? CouponCode);

}
