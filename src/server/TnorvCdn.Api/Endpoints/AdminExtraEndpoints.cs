using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using TnorvCdn.Api.Core;
using TnorvCdn.Api.Data;
using TnorvCdn.Api.Services;

namespace TnorvCdn.Api.Endpoints;

/// <summary>
/// 管理端扩展端点：仪表盘聚合、WAF/CC 统计、用量趋势，以及工单/优惠券/邀请码/智能DNS域名/SSH授权/流量包等轻量域 CRUD。
/// 支撑控制台全部页面真实数据（商用运营级）。
/// </summary>
public static class AdminExtraEndpoints
{
    public static void MapAdminExtraEndpoints(this WebApplication app)
    {
        var admin = app.MapGroup("/api/admin").RequireAuthorization();

        // ---- A2 财务完整版 ----
        // 月收入汇总：本月/上月/环比 + 近 12 月按月聚合 + 订单类型分布
        admin.MapGet("/finance/income-summary", async (CdnDbContext db) =>
        {
            var now = DateTime.UtcNow;
            var monthStart = new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc);
            var lastMonthStart = monthStart.AddMonths(-1);
            var paid = db.Orders.Where(o => o.Status == "paid");
            var monthPaid = await paid.Where(o => o.PaidAt >= monthStart).ToListAsync();
            var lastPaid = await paid.Where(o => o.PaidAt >= lastMonthStart && o.PaidAt < monthStart).ToListAsync();
            var monthCents = monthPaid.Sum(o => o.AmountCents);
            var lastCents = lastPaid.Sum(o => o.AmountCents);
            var growth = lastCents > 0 ? Math.Round((double)(monthCents - lastCents) * 100.0 / (double)lastCents, 1) : (monthCents > 0 ? 100.0 : 0);
            // 近 12 月
            var allPaid = await paid.Where(o => o.PaidAt >= monthStart.AddMonths(-11)).Select(o => new { o.PaidAt, o.AmountCents, o.Kind }).ToListAsync();
            var months = new List<object>();
            for (var i = 11; i >= 0; i--)
            {
                var ms = monthStart.AddMonths(-i);
                var me = ms.AddMonths(1);
                var mc = allPaid.Where(o => o.PaidAt >= ms && o.PaidAt < me).Sum(o => o.AmountCents);
                months.Add(new { month = ms.ToString("yyyy-MM"), cents = mc });
            }
            var byKind = allPaid.GroupBy(o => o.Kind).Select(g => new { kind = g.Key, count = g.Count(), cents = g.Sum(o => o.AmountCents) }).ToList();
            return Results.Ok(new
            {
                monthCents, lastMonthCents = lastCents, growth,
                months, byKind,
                totalOrders = await db.Orders.CountAsync(),
                pendingOrders = await db.Orders.CountAsync(o => o.Status == "pending"),
            });
        });

        // 月结账单：用户 × 月份 × 流量/请求/金额（按 UsageRecords 聚合 + 套餐价）
        admin.MapGet("/finance/bills", async (CdnDbContext db, string? month, int? page, int? pageSize) =>
        {
            var now = DateTime.UtcNow;
            var y = now.Year; var m = now.Month;
            if (!string.IsNullOrWhiteSpace(month) && month.Length == 7 && int.TryParse(month.AsSpan(0, 4), out var py) && int.TryParse(month.AsSpan(5, 2), out var pm)) { y = py; m = pm; }
            var usages = await db.UsageRecords.AsNoTracking().Where(u => u.Year == y && u.Month == m).ToListAsync();
            var userIds = usages.Select(u => u.UserId).Distinct().ToList();
            var users = await db.Users.Where(u => userIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id);
            var subs = await db.Subscriptions.Where(s2 => userIds.Contains(s2.UserId) && s2.Enabled).ToListAsync();
            var planIds = subs.Select(s2 => s2.PlanId).Distinct().ToList();
            var plans = await db.Plans.Where(pl => planIds.Contains(pl.Id)).ToDictionaryAsync(pl => pl.Id);
            var items = usages.GroupBy(u => u.UserId).Select(g =>
            {
                var trafficMB = g.Sum(x => x.TrafficMB);
                var requests = g.Sum(x => x.Requests);
                users.TryGetValue(g.Key, out var u);
                var sub = subs.Where(s2 => s2.UserId == g.Key).OrderByDescending(s2 => s2.ExpiresAt).FirstOrDefault();
                var planName = sub != null && plans.TryGetValue(sub.PlanId, out var pl) ? pl.Name : null;
                var planTrafficMB = sub != null && plans.TryGetValue(sub.PlanId, out var pl2) ? pl2.MonthlyTrafficMB : 0;
                var overMB = Math.Max(0, trafficMB - planTrafficMB);
                var overCents = (long)Math.Round(overMB / 1024m * 0.20m * 100m); // 超出部分 ¥0.20/GB
                return new
                {
                    userId = g.Key,
                    username = u?.Username ?? ("用户#" + g.Key),
                    balanceCents = u?.BalanceCents ?? 0,
                    planName, trafficMB, requests,
                    planTrafficMB, overMB,
                    overCents, month = $"{y:D4}-{m:D2}",
                };
            }).OrderByDescending(x => x.trafficMB).ToList();
            var p = Math.Clamp(page ?? 1, 1, 1000); var ps = Math.Clamp(pageSize ?? 50, 1, 500);
            return Results.Ok(new { total = items.Count, page = p, pageSize = ps, items = items.Skip((p - 1) * ps).Take(ps) });
        });

        // 余额调整（原因必填，写审计）
        admin.MapPost("/finance/adjust-balance", async (AdjustBalanceRequest req, CdnDbContext db, AuditService audit) =>
        {
            if (req.Reason is null || req.Reason.Trim().Length < 2) return Results.Json(new { code = 400, message = "调整原因必填（至少 2 字）" });
            if (req.DeltaCents == 0) return Results.Json(new { code = 400, message = "调整额度不能为 0" });
            var u = await db.Users.FindAsync(req.UserId);
            if (u == null) return Results.Json(new { code = 404, message = "用户不存在" });
            u.BalanceCents += req.DeltaCents;
            await db.SaveChangesAsync();
            _ = audit.LogAsync("finance.adjust-balance", $"uid={req.UserId} delta={req.DeltaCents} reason={req.Reason.Trim()} newBalance={u.BalanceCents}");
            return Results.Ok(new { ok = true, balanceCents = u.BalanceCents });
        });

        // ---- A5 用户展开（站点数/余额/套餐聚合）----
        admin.MapGet("/users/{id:long}/summary", async (long id, CdnDbContext db) =>
        {
            var u = await db.Users.FindAsync(id);
            if (u == null) return Results.NotFound();
            var servers = await db.Servers.Where(s => s.UserId == id)
                .Select(s => new { s.Id, s.Domains, s.Enabled, s.ServerType, status = s.SslState }).ToListAsync();
            var subs = await db.Subscriptions.Where(x => x.UserId == id && x.Enabled && x.ExpiresAt > DateTime.UtcNow).ToListAsync();
            var planIds = subs.Select(x => x.PlanId).ToList();
            var plans = await db.Plans.Where(pl => planIds.Contains(pl.Id)).ToDictionaryAsync(pl => pl.Id);
            return Results.Ok(new
            {
                u.Id, u.Username, u.BalanceCents, u.Enabled, u.Features, u.CreatedAt,
                servers,
                plans = subs.Select(x => new
                {
                    planName = plans.TryGetValue(x.PlanId, out var p1) ? p1.Name : "套餐#" + x.PlanId,
                    expiresAt = x.ExpiresAt,
                }),
            });
        });

        // ---- 仪表盘聚合 ----
        admin.MapGet("/overview", async (CdnDbContext db) =>
        {
            var since24h = DateTime.UtcNow.AddHours(-24);
            var requests24h = await db.AccessLogs.CountAsync(l => l.Timestamp >= since24h);
            var blocked24h = await db.AccessLogs.CountAsync(l => l.Timestamp >= since24h && l.WafAction == "block");
            var hits24h = await db.AccessLogs.CountAsync(l => l.Timestamp >= since24h && l.CacheStatus == "HIT");
            var bytes24h = await db.AccessLogs.Where(l => l.Timestamp >= since24h).SumAsync(l => (long?)l.BytesSent) ?? 0;
            var bytesPrev24h = await db.AccessLogs.Where(l => l.Timestamp >= since24h.AddHours(-24) && l.Timestamp < since24h).SumAsync(l => (long?)l.BytesSent) ?? 0;
            var sinceToday = DateTime.UtcNow.Date;
            var todayUniqueIps = await db.AccessLogs.Where(l => l.Timestamp >= sinceToday).Select(l => l.RemoteAddr).Distinct().CountAsync();
            var sinceWeek = DateTime.UtcNow.AddDays(-7);
            var weekBytes = await db.AccessLogs.Where(l => l.Timestamp >= sinceWeek).SumAsync(l => (long?)l.BytesSent) ?? 0;
            var sinceYesterday = DateTime.UtcNow.Date.AddDays(-1);
            var yesterdayBytes = await db.AccessLogs.Where(l => l.Timestamp >= sinceYesterday && l.Timestamp < sinceToday).SumAsync(l => (long?)l.BytesSent) ?? 0;
            var servers = await db.Servers.CountAsync();
            var nodesTotal = await db.Nodes.CountAsync();
            var nodesOnline = await db.Nodes.CountAsync(n => n.IsOnline);
            var users = await db.Users.CountAsync();
            var trend = await db.AccessLogs
                .Where(l => l.Timestamp >= since24h)
                .GroupBy(l => l.Timestamp.AddMinutes(-l.Timestamp.Minute).AddSeconds(-l.Timestamp.Second))
                .Select(g => new { hour = g.Key, requests = g.Count(), blocked = g.Count(l => l.WafAction == "block") })
                .ToListAsync();
            return Results.Ok(new
            {
                servers, nodesTotal, nodesOnline, users,
                requests24h, blocked24h,
                cacheHitRate = requests24h > 0 ? Math.Round(hits24h * 100.0 / requests24h, 1) : 0,
                bytes24h, bytesPrev24h,
                bytesGrowth = bytesPrev24h > 0 ? Math.Round((bytes24h - bytesPrev24h) * 100.0 / bytesPrev24h, 1) : (bytes24h > 0 ? 100.0 : 0),
                todayUniqueIps, weekBytes, yesterdayBytes,
                trend = trend.OrderBy(t => t.hour).Select(t => new
                {
                    hour = t.hour.ToString("yyyy-MM-ddTHH:mm:ss"),
                    t.requests, t.blocked,
                }).ToList(),
            });
        });

        // ---- 地区分布（近24h 独立 IP 按国家聚合，GeoIpService 缓存） ----
        admin.MapGet("/geo/summary", async (CdnDbContext db, GeoIpService geo, int? hours) =>
        {
            var since = DateTime.UtcNow.AddHours(-(hours ?? 24));
            var ips = await db.AccessLogs.AsNoTracking()
                .Where(l => l.Timestamp >= since)
                .Select(l => l.RemoteAddr)
                .Distinct()
                .ToListAsync();
            var dist = geo.Distribution(ips);
            return Results.Ok(new
            {
                window = $"{hours ?? 24}h",
                totalIps = ips.Count,
                items = dist.Select(d => new { country = d.Country, count = d.Count }),
            });
        });

        // ---- WAF 拦截统计（近24h） ----
        admin.MapGet("/waf/summary", async (CdnDbContext db) =>
        {
            var since = DateTime.UtcNow.AddHours(-24);
            var rows = await db.AccessLogs
                .Where(l => l.Timestamp >= since && l.WafAction == "block")
                .GroupBy(l => l.BlockReason ?? "unknown")
                .Select(g => new { reason = g.Key, count = g.Count() })
                .ToListAsync();
            var cc = await db.AccessLogs
                .Where(l => l.Timestamp >= since && l.WafAction == "block" && (l.BlockReason ?? "").Contains("rate limited"))
                .GroupBy(l => l.RemoteAddr)
                .Select(g => new { ip = g.Key, count = g.Count() })
                .OrderByDescending(x => x.count)
                .Take(20)
                .ToListAsync();
            return Results.Ok(new { byReason = rows, topCcIps = cc });
        });

        // ---- 小时趋势（近 N 小时，按小时聚合，流量+访问量） ----
        admin.MapGet("/traffic/hourly", async (CdnDbContext db, int? hours) =>
        {
            var h = (hours == null || hours <= 0) ? 24 : Math.Clamp(hours.Value, 1, 72);
            var since = DateTime.UtcNow.AddHours(-h);
            var rows = await db.AccessLogs.AsNoTracking()
                .Where(l => l.Timestamp >= since)
                .GroupBy(l => l.Timestamp.AddMinutes(-l.Timestamp.Minute).AddSeconds(-l.Timestamp.Second))
                .Select(g => new { hour = g.Key, requests = g.Count(), bytes = g.Sum(x => x.BytesSent) })
                .ToListAsync();
            return Results.Ok(rows.OrderBy(r => r.hour)
                .Select(r => new { hour = r.hour.ToString("yyyy-MM-ddTHH:mm"), r.requests, r.bytes }).ToList());
        });

        // ---- 看板下钻：指定站点（按域名匹配）近 24h 小时趋势 ----
        admin.MapGet("/traffic/hourly/server", async (CdnDbContext db, string? host, int? hours) =>
        {
            var h = (hours == null || hours <= 0) ? 24 : Math.Clamp(hours.Value, 1, 72);
            var since = DateTime.UtcNow.AddHours(-h);
            var hostLc = (host ?? "").ToLower();
            var rows = await db.AccessLogs.AsNoTracking()
                .Where(l => l.Timestamp >= since && l.Host.ToLower() == hostLc)
                .GroupBy(l => l.Timestamp.AddMinutes(-l.Timestamp.Minute).AddSeconds(-l.Timestamp.Second))
                .Select(g => new { hour = g.Key, requests = g.Count(), bytes = g.Sum(x => x.BytesSent) })
                .ToListAsync();
            return Results.Ok(rows.OrderBy(r => r.hour)
                .Select(r => new { hour = r.hour.ToString("yyyy-MM-ddTHH:mm"), r.requests, r.bytes }).ToList());
        });

        // ---- 访问排行（近 N 小时 topN）：域名/节点/路径/独立IP ----
        admin.MapGet("/stats/top", async (CdnDbContext db, string dim, int? hours, int? limit) =>
        {
            var since = DateTime.UtcNow.AddHours(-(hours == null || hours <= 0 ? 24 : Math.Clamp(hours.Value, 1, 168)));
            var take = Math.Clamp(limit ?? 10, 1, 50);
            IQueryable<AccessLogRecord> q = db.AccessLogs.AsNoTracking().Where(l => l.Timestamp >= since);
            switch ((dim ?? "domain").ToLowerInvariant())
            {
                case "domain":
                {
                    var rows = await q.GroupBy(l => l.Host).OrderByDescending(g => g.Count()).Take(take)
                        .Select(g => new { key = g.Key, requests = g.Count(), bytes = g.Sum(x => x.BytesSent) }).ToListAsync();
                    return Results.Ok(rows);
                }
                case "node":
                {
                    var rows = await q.GroupBy(l => l.NodeId).OrderByDescending(g => g.Count()).Take(take)
                        .Select(g => new { key = g.Key.ToString(), requests = g.Count(), bytes = g.Sum(x => x.BytesSent) }).ToListAsync();
                    var names = await db.Nodes.ToDictionaryAsync(n => n.Id, n => n.Name);
                    return Results.Ok(rows.Select(r => new { key = names.TryGetValue(long.Parse(r.key), out var nm) ? nm : "节点" + r.key, requests = r.requests, bytes = r.bytes }));
                }
                case "path":
                {
                    var rows = await q.GroupBy(l => l.Uri).OrderByDescending(g => g.Count()).Take(take)
                        .Select(g => new { key = g.Key, requests = g.Count(), bytes = g.Sum(x => x.BytesSent) }).ToListAsync();
                    return Results.Ok(rows);
                }
                default: // ip
                {
                    var rows = await q.GroupBy(l => l.RemoteAddr).OrderByDescending(g => g.Count()).Take(take)
                        .Select(g => new { key = g.Key, requests = g.Count(), bytes = g.Sum(x => x.BytesSent) }).ToListAsync();
                    return Results.Ok(rows);
                }
            }
        });

        // ---- 状态码 / 请求方法分布（近 24h） ----
        admin.MapGet("/stats/status-codes", async (CdnDbContext db, int? hours) =>
        {
            var since = DateTime.UtcNow.AddHours(-(hours == null || hours <= 0 ? 24 : Math.Clamp(hours.Value, 1, 168)));
            var byStatus = await db.AccessLogs.AsNoTracking().Where(l => l.Timestamp >= since)
                .GroupBy(l => l.Status).Select(g => new { key = g.Key.ToString(), count = g.Count() })
                .OrderByDescending(x => x.count).ToListAsync();
            var byMethod = await db.AccessLogs.AsNoTracking().Where(l => l.Timestamp >= since)
                .GroupBy(l => l.Method).Select(g => new { key = g.Key, count = g.Count() })
                .OrderByDescending(x => x.count).ToListAsync();
            var total = byStatus.Sum(x => x.count);
            return Results.Ok(new { total, byStatus, byMethod });
        });

        // ---- 实时指标（心跳聚合 + 近 5min 日志带宽） ----
        admin.MapGet("/metrics/realtime", async (CdnDbContext db) =>
        {
            var fresh = NodeMetricsRegistry.Fresh();
            var nodes = fresh.Count;
            var since5 = DateTime.UtcNow.AddMinutes(-5);
            var bytes5 = await db.AccessLogs.Where(l => l.Timestamp >= since5).SumAsync(l => (long?)l.BytesSent) ?? 0;
            var kbps = nodes > 0 ? (long)(bytes5 * 8 / 300.0 / 1000) : 0;
            return Results.Ok(new
            {
                nodeCount = nodes,
                totalBandwidthKbps = kbps,
                avgCpu = nodes > 0 ? Math.Round(fresh.Average(f => f.E.CpuUsage), 1) : 0,
                avgMem = nodes > 0 ? Math.Round(fresh.Average(f => f.E.MemUsage), 1) : 0,
                avgLoad = nodes > 0 ? Math.Round(fresh.Average(f => f.E.Load), 2) : 0,
                connections = fresh.Sum(f => (long)f.E.Connections),
                nodes = fresh.Select(f => new { nodeId = f.NodeId, f.E.CpuUsage, f.E.MemUsage, f.E.Load, f.E.Connections }),
            });
        });

        // ---- 用量趋势（管理端，按天） ----
        admin.MapGet("/traffic/daily", async (CdnDbContext db, int? days) =>
        {
            var d = (days == null || days <= 0) ? 7 : Math.Clamp(days.Value, 1, 90);
            var since = DateTime.UtcNow.AddDays(-d);
            var rows = await db.AccessLogs
                .Where(l => l.Timestamp >= since)
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
            return Results.Ok(rows.OrderBy(r => r.date)
                .Select(r => new { date = r.date.ToString("yyyy-MM-dd"), r.requests, r.hits, r.blocked, r.bytes })
                .ToList());
        });

        // ---- 框架兼容：操作/登录日志列表（控制台日志审计页数据源）----
        admin.MapGet("/system/log/oper/list", async (CdnDbContext db, int? pageIndex, int? pageSize) =>
        {
            var pg = Math.Max(1, pageIndex ?? 1); var sz = (pageSize == null || pageSize <= 0) ? 10 : Math.Clamp(pageSize.Value, 1, 100);
            var q = db.AuditLogs.AsNoTracking().OrderByDescending(a => a.Timestamp);
            var total = await q.CountAsync();
            var items = await q.Skip((pg - 1) * sz).Take(sz)
                .Select(a => new { a.Id, a.UserId, a.Action, a.Detail, a.Ip, a.Timestamp }).ToListAsync();
            return Results.Ok(new { total, items = items.Select(a => new { id = a.Id, userId = a.UserId, title = a.Action, operName = a.Detail, operIp = a.Ip, operTime = a.Timestamp, status = 0 }) });
        });
        admin.MapGet("/system/log/login/list", async (CdnDbContext db, int? pageIndex, int? pageSize) =>
        {
            var pg = Math.Max(1, pageIndex ?? 1); var sz = (pageSize == null || pageSize <= 0) ? 10 : Math.Clamp(pageSize.Value, 1, 100);
            var q = db.AuditLogs.AsNoTracking().Where(a => a.Action.Contains("login")).OrderByDescending(a => a.Timestamp);
            var total = await q.CountAsync();
            var items = await q.Skip((pg - 1) * sz).Take(sz)
                .Select(a => new { a.Id, a.UserId, a.Action, a.Ip, a.Timestamp }).ToListAsync();
            return Results.Ok(new { total, items = items.Select(a => new { id = a.Id, userId = a.UserId, userName = a.Action, loginIp = a.Ip, loginTime = a.Timestamp, status = 0 }) });
        });


        var sysLogs = app.MapGroup("/api/system/log").RequireAuthorization();
        sysLogs.MapGet("/oper/list", async (CdnDbContext db, int? pageIndex, int? pageSize) =>
        {
            var pg = Math.Max(1, pageIndex ?? 1); var sz = (pageSize == null || pageSize <= 0) ? 10 : Math.Clamp(pageSize.Value, 1, 100);
            var q = db.AuditLogs.AsNoTracking().OrderByDescending(a => a.Timestamp);
            var total = await q.CountAsync();
            var items = await q.Skip((pg - 1) * sz).Take(sz)
                .Select(a => new { a.Id, a.UserId, a.Action, a.Detail, a.Ip, a.Timestamp }).ToListAsync();
            return Results.Ok(new { total, items = items.Select(a => new { id = a.Id, userId = a.UserId, title = a.Action, operName = a.Detail, operIp = a.Ip, operTime = a.Timestamp, status = 0 }) });
        });
        sysLogs.MapGet("/login/list", async (CdnDbContext db, int? pageIndex, int? pageSize) =>
        {
            var pg = Math.Max(1, pageIndex ?? 1); var sz = (pageSize == null || pageSize <= 0) ? 10 : Math.Clamp(pageSize.Value, 1, 100);
            var q = db.AuditLogs.AsNoTracking().Where(a => a.Action.Contains("login")).OrderByDescending(a => a.Timestamp);
            var total = await q.CountAsync();
            var items = await q.Skip((pg - 1) * sz).Take(sz)
                .Select(a => new { a.Id, a.UserId, a.Action, a.Ip, a.Timestamp }).ToListAsync();
            return Results.Ok(new { total, items = items.Select(a => new { id = a.Id, userId = a.UserId, userName = a.Action, loginIp = a.Ip, loginTime = a.Timestamp, status = 0 }) });
        });

        // ---- 工单 ----
        var tickets = admin.MapGroup("/tickets");
        tickets.MapGet("", async (CdnDbContext db, string? q, string? status, int? page, int? pageSize) =>
        {
            var query = db.Tickets.AsNoTracking().AsQueryable();
            if (!string.IsNullOrWhiteSpace(status)) query = query.Where(t => t.Status == status);
            if (!string.IsNullOrWhiteSpace(q)) { var kw = q.ToLower(); query = query.Where(t => t.Title.ToLower().Contains(kw) || t.Content.ToLower().Contains(kw)); }
            query = query.OrderByDescending(t => t.CreatedAt);
            if (page == null && pageSize == null) return Results.Ok(await query.Take(500).ToListAsync());
            var p = Math.Clamp(page ?? 1, 1, 1000); var ps = Math.Clamp(pageSize ?? 20, 1, 200);
            var total = await query.CountAsync();
            var items = await query.Skip((p - 1) * ps).Take(ps).ToListAsync();
            return Results.Ok(new { total, page = p, pageSize = ps, items });
        });
        tickets.MapPost("", async (Ticket t, CdnDbContext db) =>
        {
            if (string.IsNullOrWhiteSpace(t.Title)) return Results.BadRequest("标题必填");
            t.Id = 0; t.CreatedAt = DateTime.UtcNow;
            db.Tickets.Add(t);
            await db.SaveChangesAsync();
            return Results.Created($"/api/admin/tickets/{t.Id}", t);
        });
        tickets.MapPut("/{id:long}", async (long id, Ticket input, CdnDbContext db, Services.SmsService sms) =>
        {
            var t = await db.Tickets.FindAsync(id);
            if (t == null) return Results.NotFound();
            t.Title = input.Title; t.Content = input.Content; t.Status = input.Status;
            t.Priority = input.Priority; t.Reply = input.Reply;
            await db.SaveChangesAsync();
            // 工单有新回复 → 短信提醒工单主人（内容不带域名）
            if (!string.IsNullOrWhiteSpace(input.Reply))
                _ = sms.NotifyAsync(t.UserId, "ticket_reply", "您的工单有新的客服回复，请登录控制台查看详情。", $"ticket:{t.Id}");
            return Results.Ok();
        });
        tickets.MapDelete("/{id:long}", async (long id, CdnDbContext db) =>
        {
            var t = await db.Tickets.FindAsync(id);
            if (t == null) return Results.NotFound();
            db.Tickets.Remove(t);
            await db.SaveChangesAsync();
            return Results.Ok();
        });

        // ---- 优惠券 ----
        var coupons = admin.MapGroup("/coupons");
        coupons.MapGet("", async (CdnDbContext db, string? q, int? page, int? pageSize) =>
        {
            var query = db.Coupons.AsNoTracking()
                .Where(c => q == null || q == "" || c.Code.ToLower().Contains(q.ToLower()))
                .OrderByDescending(c => c.Id);
            if (page == null && pageSize == null)
                return Results.Ok(await query.ToListAsync());
            var p = Math.Clamp(page ?? 1, 1, 1000);
            var ps = Math.Clamp(pageSize ?? 20, 1, 200);
            var total = await query.CountAsync();
            var items = await query.Skip((p - 1) * ps).Take(ps).ToListAsync();
            return Results.Ok(new { total, page = p, pageSize = ps, items });
        });
        coupons.MapPost("", async (Coupon c, CdnDbContext db) =>
        {
            if (string.IsNullOrWhiteSpace(c.Code)) return Results.BadRequest("兑换码必填");
            c.Id = 0; c.Used = 0; c.CreatedAt = DateTime.UtcNow;
            db.Coupons.Add(c);
            await db.SaveChangesAsync();
            return Results.Created($"/api/admin/coupons/{c.Id}", c);
        });
        coupons.MapDelete("/{id:long}", async (long id, CdnDbContext db) =>
        {
            var c = await db.Coupons.FindAsync(id);
            if (c == null) return Results.NotFound();
            db.Coupons.Remove(c);
            await db.SaveChangesAsync();
            return Results.Ok();
        });


        // ---- 订单管理（充值/套餐/流量包订单，只读 + 取消）----
        admin.MapGet("/orders", async (CdnDbContext db, long? userId, string? status, string? q, int? page, int? pageSize) =>
        {
            var query = db.Orders.AsNoTracking().AsQueryable();
            if (userId != null) query = query.Where(o => o.UserId == userId);
            if (!string.IsNullOrWhiteSpace(status)) query = query.Where(o => o.Status == status);
            if (!string.IsNullOrWhiteSpace(q))
            {
                var kw = q.ToLower();
                query = query.Where(o => o.OrderNo.ToLower().Contains(kw) || o.CouponCode.ToLower().Contains(kw));
            }
            query = query.OrderByDescending(o => o.Id);
            if (page == null && pageSize == null)
                return Results.Ok(await query.Take(500)
                    .Select(o => new { o.Id, o.UserId, o.OrderNo, o.Kind, o.PlanId, o.FlowPackageId, o.Months, o.AmountCents, o.Status, o.PayMethod, o.CouponCode, o.CreatedAt, o.PaidAt })
                    .ToListAsync());
            var p = Math.Clamp(page ?? 1, 1, 1000);
            var ps = Math.Clamp(pageSize ?? 20, 1, 200);
            var total = await query.CountAsync();
            var items = await query.Skip((p - 1) * ps).Take(ps)
                .Select(o => new { o.Id, o.UserId, o.OrderNo, o.Kind, o.PlanId, o.FlowPackageId, o.Months, o.AmountCents, o.Status, o.PayMethod, o.CouponCode, o.CreatedAt, o.PaidAt })
                .ToListAsync();
            return Results.Ok(new { total, page = p, pageSize = ps, items });
        });
        admin.MapPost("/orders/{orderNo}/cancel", async (string orderNo, CdnDbContext db, AuditService audit) =>
        {
            var order = await db.Orders.FirstOrDefaultAsync(o => o.OrderNo == orderNo);
            if (order == null) return Results.NotFound();
            if (order.Status != "pending") return Results.BadRequest("订单状态不可取消");
            order.Status = "cancelled";
            await db.SaveChangesAsync();
            _ = audit.LogAsync("orders.cancel " + orderNo, "ok");
            return Results.Ok();
        });

        // ---- 邀请码 ----
        var invitations = admin.MapGroup("/invitations");
        invitations.MapGet("", async (CdnDbContext db, string? q, int? page, int? pageSize) =>
        {
            var query = db.Invitations.AsNoTracking().AsQueryable();
            if (!string.IsNullOrWhiteSpace(q)) { var kw = q.ToLower(); query = query.Where(i => i.Code.ToLower().Contains(kw)); }
            query = query.OrderByDescending(i => i.CreatedAt);
            if (page == null && pageSize == null) return Results.Ok(await query.Take(500).ToListAsync());
            var p = Math.Clamp(page ?? 1, 1, 1000); var ps = Math.Clamp(pageSize ?? 20, 1, 200);
            var total = await query.CountAsync();
            var items = await query.Skip((p - 1) * ps).Take(ps).ToListAsync();
            return Results.Ok(new { total, page = p, pageSize = ps, items });
        });
        invitations.MapPost("", async (CdnDbContext db) =>
        {
            var inv = new Invitation
            {
                Code = Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(6)).ToLowerInvariant(),
                MaxUses = 1, Used = 0, Enabled = true, CreatedAt = DateTime.UtcNow, Id = 0,
            };
            db.Invitations.Add(inv);
            await db.SaveChangesAsync();
            return Results.Created($"/api/admin/invitations/{inv.Id}", inv);
        });
        invitations.MapDelete("/{id:long}", async (long id, CdnDbContext db) =>
        {
            var inv = await db.Invitations.FindAsync(id);
            if (inv == null) return Results.NotFound();
            db.Invitations.Remove(inv);
            await db.SaveChangesAsync();
            return Results.Ok();
        });

        // ---- 智能 DNS 域名 ----
        var ns = admin.MapGroup("/ns-domains");
        ns.MapGet("", async (CdnDbContext db, string? q, int? page, int? pageSize) =>
        {
            var query = db.NsDomains.AsNoTracking().AsQueryable();
            if (!string.IsNullOrWhiteSpace(q)) { var kw = q.ToLower(); query = query.Where(d => d.Name.ToLower().Contains(kw)); }
            query = query.OrderByDescending(d => d.CreatedAt);
            if (page == null && pageSize == null) return Results.Ok(await query.Take(500).ToListAsync());
            var p = Math.Clamp(page ?? 1, 1, 1000); var ps = Math.Clamp(pageSize ?? 20, 1, 200);
            var total = await query.CountAsync();
            var items = await query.Skip((p - 1) * ps).Take(ps).ToListAsync();
            return Results.Ok(new { total, page = p, pageSize = ps, items });
        });
        ns.MapPost("", async (NsDomain d, CdnDbContext db) =>
        {
            if (string.IsNullOrWhiteSpace(d.Name)) return Results.BadRequest("域名必填");
            d.Id = 0; d.CreatedAt = DateTime.UtcNow;
            db.NsDomains.Add(d);
            await db.SaveChangesAsync();
            return Results.Created($"/api/admin/ns-domains/{d.Id}", d);
        });
        ns.MapDelete("/{id:long}", async (long id, CdnDbContext db) =>
        {
            var d = await db.NsDomains.FindAsync(id);
            if (d == null) return Results.NotFound();
            db.NsDomains.Remove(d);
            await db.SaveChangesAsync();
            return Results.Ok();
        });
        ns.MapPut("/{id:long}", async (long id, NsDomain input, CdnDbContext db, ConfigPublishQueue pq) =>
        {
            var d = await db.NsDomains.FindAsync(id);
            if (d == null) return Results.NotFound();
            if (!string.IsNullOrWhiteSpace(input.Name)) d.Name = input.Name.Trim();
            if (input.Group != null) d.Group = input.Group;
            if (!string.IsNullOrWhiteSpace(input.Line)) d.Line = input.Line;
            if (!string.IsNullOrWhiteSpace(input.Type)) d.Type = input.Type;
            if (!string.IsNullOrEmpty(input.Value)) d.Value = input.Value; // 空串/null 均不覆盖，防止部分更新把记录值清掉
            if (input.Ttl > 0) d.Ttl = input.Ttl;
            if (!string.IsNullOrWhiteSpace(input.Status)) d.Status = input.Status;
            await db.SaveChangesAsync();
            pq.EnqueueAll(); // DNS 记录快照随各集群发布（dnsRecords 聚合全表）
            return Results.Ok(d);
        });

        // ---- 节点 SSH 授权 ----
        var ssh = admin.MapGroup("/ssh-grants");
        ssh.MapGet("", async (CdnDbContext db) => Results.Ok(await db.SshGrants.OrderByDescending(g => g.CreatedAt).ToListAsync()));
        ssh.MapPost("", async (SshGrant g, CdnDbContext db) =>
        {
            if (string.IsNullOrWhiteSpace(g.NodeName) || string.IsNullOrWhiteSpace(g.Host)) return Results.BadRequest("节点名与主机必填");
            g.Id = 0; g.CreatedAt = DateTime.UtcNow;
            db.SshGrants.Add(g);
            await db.SaveChangesAsync();
            return Results.Created($"/api/admin/ssh-grants/{g.Id}", g);
        });
        ssh.MapDelete("/{id:long}", async (long id, CdnDbContext db) =>
        {
            var g = await db.SshGrants.FindAsync(id);
            if (g == null) return Results.NotFound();
            db.SshGrants.Remove(g);
            await db.SaveChangesAsync();
            return Results.Ok();
        });

        // ---- 流量包 ----
        var packages = admin.MapGroup("/flow-packages");
        packages.MapGet("", async (CdnDbContext db, string? q, int? page, int? pageSize) =>
        {
            var query = db.FlowPackages.AsNoTracking().AsQueryable();
            if (!string.IsNullOrWhiteSpace(q)) { var kw = q.ToLower(); query = query.Where(p => p.Name.ToLower().Contains(kw)); }
            query = query.OrderBy(p => p.PriceCents);
            if (page == null && pageSize == null) return Results.Ok(await query.Take(500).ToListAsync());
            var p = Math.Clamp(page ?? 1, 1, 1000); var ps = Math.Clamp(pageSize ?? 20, 1, 200);
            var total = await query.CountAsync();
            var items = await query.Skip((p - 1) * ps).Take(ps).ToListAsync();
            return Results.Ok(new { total, page = p, pageSize = ps, items });
        });
        packages.MapPost("", async (FlowPackage p, CdnDbContext db) =>
        {
            if (string.IsNullOrWhiteSpace(p.Name)) return Results.BadRequest("名称必填");
            p.Id = 0;
            db.FlowPackages.Add(p);
            await db.SaveChangesAsync();
            return Results.Created($"/api/admin/flow-packages/{p.Id}", p);
        });
        packages.MapDelete("/{id:long}", async (long id, CdnDbContext db) =>
        {
            var p = await db.FlowPackages.FindAsync(id);
            if (p == null) return Results.NotFound();
            db.FlowPackages.Remove(p);
            await db.SaveChangesAsync();
            return Results.Ok();
        });

        // ---- 管理员列表 ----
        // 版本更新检查：当前版本 + 最新已知版本（GitHub release 探测失败时仅返回当前版本）
        admin.MapGet("/version/check", async () =>
        {
            var current = TnorvCdn.Api.Services.VersionInfo.Current;
            string? latest = null;
            try
            {
                using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
                http.DefaultRequestHeaders.Add("User-Agent", "TnorvCdn");
                var resp = await http.GetStringAsync("https://api.github.com/repos/tnorv/tnorv-cdn/releases/latest");
                using var doc = System.Text.Json.JsonDocument.Parse(resp);
                latest = doc.RootElement.GetProperty("tag_name").GetString();
            }
            catch { /* 无外网/仓库未发布：latest 置空 */ }
            return Results.Ok(new { current, latest, upToDate = latest == null || latest == current });
        });

        admin.MapGet("/admins", async (CdnDbContext db, bool? hasWeakPassword) =>
        {
            var admins = await db.Users.Where(u => u.IsAdmin).Select(u => new { u.Id, u.Username, u.Enabled, u.CreatedAt }).ToListAsync();
            if (hasWeakPassword == true)
            {
                // 弱密码检测：BCrypt 验证常见弱密码 top 列表
                string[] weak = ["123456", "admin", "admin123", "password", "12345678", "admin@123", "888888", "666666", "admin888", "123123", "abc123", "111111", "000000", "1234567890"];
                var result = new List<object>();
                foreach (var a in admins)
                {
                    var u = await db.Users.FirstAsync(x => x.Id == a.Id);
                    var hit = false;
                    foreach (var w in weak)
                    {
                        try { if (BCrypt.Net.BCrypt.Verify(w, u.PasswordHash)) { hit = true; break; } } catch { }
                    }
                    if (hit) result.Add(new { a.Id, a.Username, a.Enabled, a.CreatedAt });
                }
                return Results.Ok(result);
            }
            return Results.Ok(admins);
        });

        // ---- 公告管理通用设计----
        var posts = admin.MapGroup("/posts");
        posts.MapGet("", async (CdnDbContext db, string? q, int? page, int? pageSize) =>
        {
            var query = db.Posts.AsNoTracking().AsQueryable();
            if (!string.IsNullOrWhiteSpace(q)) { var kw = q.ToLower(); query = query.Where(p => p.Title.ToLower().Contains(kw)); }
            query = query.OrderByDescending(p => p.CreatedAt);
            if (page == null && pageSize == null) return Results.Ok(await query.Take(500).ToListAsync());
            var p = Math.Clamp(page ?? 1, 1, 1000); var ps = Math.Clamp(pageSize ?? 20, 1, 200);
            var total = await query.CountAsync();
            var items = await query.Skip((p - 1) * ps).Take(ps).ToListAsync();
            return Results.Ok(new { total, page = p, pageSize = ps, items });
        });
        posts.MapPost("", async (Post p, CdnDbContext db) =>
        {
            if (string.IsNullOrWhiteSpace(p.Title)) return Results.BadRequest("标题必填");
            p.Id = 0; p.CreatedAt = DateTime.UtcNow;
            db.Posts.Add(p);
            await db.SaveChangesAsync();
            return Results.Created($"/api/admin/posts/{p.Id}", p);
        });
        posts.MapPut("/{id:long}", async (long id, Post input, CdnDbContext db) =>
        {
            var p = await db.Posts.FindAsync(id);
            if (p == null) return Results.NotFound();
            p.Title = input.Title; p.Content = input.Content;
            p.Enabled = input.Enabled; p.Popup = input.Popup; p.Audience = input.Audience;
            await db.SaveChangesAsync();
            return Results.Ok(p);
        });
        posts.MapDelete("/{id:long}", async (long id, CdnDbContext db) =>
        {
            var p = await db.Posts.FindAsync(id);
            if (p == null) return Results.NotFound();
            db.Posts.Remove(p);
            await db.SaveChangesAsync();
            return Results.Ok();
        });

        // ---- IP 盒查询通用设计----
        admin.MapGet("/ipbox", async (CdnDbContext db, GeoIpService geo, string ip, int? hours) =>
        {
            if (string.IsNullOrWhiteSpace(ip)) return Results.BadRequest("请输入 IP");
            var since = DateTime.UtcNow.AddHours(-(hours ?? 24));
            var logs = await db.AccessLogs.AsNoTracking()
                .Where(l => l.RemoteAddr == ip && l.Timestamp >= since)
                .OrderByDescending(l => l.Timestamp).Take(50)
                .Select(l => new { l.ServerId, l.Host, l.Uri, l.Method, l.Status, l.CacheStatus, l.Timestamp })
                .ToListAsync();
            var (country, province) = geo.Resolve(ip);
            var domains = await db.Servers.Where(s => logs.Select(l => l.ServerId).Contains(s.Id))
                .Select(s => new { s.Id, domains = s.Domains }).ToListAsync();
            return Results.Ok(new
            {
                ip, country, province,
                requestCount = logs.Count,
                servers = domains,
                recent = logs,
            });
        });

        // ---- 数据库备份通用设计----
        admin.MapGet("/database/backup", async (CdnDbContext db, IConfiguration cfg, AuditService audit) =>
        {
            var conn = db.Database.GetConnectionString() ?? "";
            var builder = new Npgsql.NpgsqlConnectionStringBuilder(conn);
            var tmp = Path.Combine(Path.GetTempPath(), $"tnorv-backup-{DateTime.UtcNow:yyyyMMddHHmmss}.sql.gz");
            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "/usr/bin/pg_dump",
                Arguments = $"-h {builder.Host} -p {builder.Port} -U {builder.Username} -d {builder.Database} -F p",
                RedirectStandardOutput = true, RedirectStandardError = true,
            };
            psi.Environment["PGPASSWORD"] = builder.Password ?? "";
            try
            {
                using var proc = System.Diagnostics.Process.Start(psi)!;
                var stdout = proc.StandardOutput.BaseStream;
                using var fs = File.Create(tmp);
                // 管道经 gzip 压缩落盘。gzip 的 stdout 必须并发消费：
                // 若先等 pg_dump→gzip 写完再读 gzip 输出，gzip 输出缓冲写满后会反压死锁（已实测）
                var gzip = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "/usr/bin/gzip", Arguments = "-c",
                    RedirectStandardInput = true, RedirectStandardOutput = true,
                })!;
                var pumpIn = stdout.CopyToAsync(gzip.StandardInput.BaseStream);
                var pumpOut = gzip.StandardOutput.BaseStream.CopyToAsync(fs);
                await pumpIn;
                gzip.StandardInput.Close();
                await pumpOut;
                await fs.FlushAsync();
                proc.WaitForExit(60_000); gzip.WaitForExit(60_000);
                // 先关闭文件句柄再读取（await using 会在块尾才释放，ReadAllBytes 会撞共享冲突）
                if (proc.ExitCode != 0)
                {
                    var err = await proc.StandardError.ReadToEndAsync();
                    return Results.Problem("pg_dump 失败: " + err);
                }
                _ = audit.LogAsync("database.backup", "pg_dump 导出");
                await fs.DisposeAsync();
                var bytes = await File.ReadAllBytesAsync(tmp);
                return Results.File(bytes, "application/gzip",
                    $"tnorv-{builder.Database}-{DateTime.UtcNow:yyyyMMddHHmmss}.sql.gz");
            }
            finally
            {
                try { if (File.Exists(tmp)) File.Delete(tmp); } catch { }
            }
        });

        // ---- 数据库备份列表（近 7 天审计记录，真实操作留痕）----
        admin.MapGet("/database/backup-logs", async (CdnDbContext db) =>
        {
            var since = DateTime.UtcNow.AddDays(-7);
            var logs = await db.AuditLogs.AsNoTracking()
                .Where(a => a.Action == "database.backup" && a.Timestamp >= since)
                .OrderByDescending(a => a.Timestamp).Take(50)
                .Select(a => new { a.Id, a.UserId, a.Action, a.Detail, a.Timestamp })
                .ToListAsync();
            return Results.Ok(logs);
        });

        // ---- 全局搜索通用设计----
        admin.MapGet("/search", async (CdnDbContext db, string q) =>
        {
            if (string.IsNullOrWhiteSpace(q)) return Results.Ok(new { servers = Array.Empty<object>(), users = Array.Empty<object>(), nodes = Array.Empty<object>(), tickets = Array.Empty<object>(), orders = Array.Empty<object>() });
            var kw = q.Trim().ToLower();
            // Domains 为转换列（JSON 文本），EF 无法翻译字符串匹配——先取近期候选再内存过滤
            var candidates = await db.Servers.AsNoTracking().OrderByDescending(s => s.Id).Take(500).ToListAsync();
            var servers = candidates.Where(s => s.Domains.Any(d => d.ToLower().Contains(kw)))
                .Take(8).Select(s => new { type = "server", id = s.Id, title = s.Domains.First(d => d.ToLower().Contains(kw)), sub = "站点 #" + s.Id, url = "/cdn/servers/config/" + s.Id }).ToList();
            var users = await db.Users.AsNoTracking().Where(u => u.Username.ToLower().Contains(kw))
                .Take(8).Select(u => new { type = "user", id = u.Id, title = u.Username, sub = "用户 #" + u.Id, url = "/cdn/users" }).ToListAsync();
            var nodes = await db.Nodes.AsNoTracking().Where(n => n.Name.ToLower().Contains(kw) || (n.Hostname != null && n.Hostname.ToLower().Contains(kw)))
                .Take(8).Select(n => new { type = "node", id = n.Id, title = n.Name, sub = n.Hostname ?? "", url = "/cdn/clusters" }).ToListAsync();
            var tickets = await db.Tickets.AsNoTracking().Where(t => t.Title.ToLower().Contains(kw))
                .Take(8).Select(t => new { type = "ticket", id = t.Id, title = t.Title, sub = "工单 #" + t.Id, url = "/cdn/tickets" }).ToListAsync();
            var orders = await db.Orders.AsNoTracking().Where(o => o.OrderNo.ToLower().Contains(kw))
                .Take(8).Select(o => new { type = "order", id = o.Id, title = o.OrderNo, sub = "订单 " + o.Status, url = "/cdn/finance/orders" }).ToListAsync();
            return Results.Ok(new { servers, users, nodes, tickets, orders });
        });

        // ---- 用户实名认证通用设计----
        var identities = admin.MapGroup("/identities");
        identities.MapGet("", async (CdnDbContext db, string? status) =>
        {
            var q = db.UserIdentities.AsNoTracking().OrderByDescending(i => i.CreatedAt);
            var list = string.IsNullOrEmpty(status) ? await q.Take(200).ToListAsync() : await q.Where(i => i.Status == status).ToListAsync();
            var uids = list.Select(i => i.UserId).Distinct().ToList();
            var names = await db.Users.Where(u => uids.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.Username);
            return Results.Ok(list.Select(i => new
            {
                i.Id, i.UserId, username = names.GetValueOrDefault(i.UserId, "#" + i.UserId),
                i.Type, i.RealName, i.IdNumber, i.EnterpriseName, i.Status, i.Note, i.CreatedAt, i.ReviewedAt,
            }));
        });
        identities.MapPost("/{id:long}/review", async (long id, IdentityReviewRequest req, CdnDbContext db, AuditService audit) =>
        {
            var i = await db.UserIdentities.FindAsync(id);
            if (i == null) return Results.NotFound();
            if (req.Status is not ("approved" or "rejected")) return Results.BadRequest("status 须为 approved/rejected");
            i.Status = req.Status;
            i.Note = req.Note ?? "";
            i.ReviewedAt = DateTime.UtcNow;
            await db.SaveChangesAsync();
            _ = audit.LogAsync($"identity.review {id}", $"{req.Status} user={i.UserId}");
            return Results.Ok(new { ok = true });
        });

        // ---- 用户功能开关通用设计----
        admin.MapGet("/users/{id:long}/features", async (long id, CdnDbContext db) =>
        {
            var u = await db.Users.FindAsync(id);
            if (u == null) return Results.NotFound();
            if (string.IsNullOrEmpty(u.Features)) return Results.Ok(new { ns = true, antiDdos = true, lb = true, tickets = true, api = true, finance = true });
            return Results.Ok(System.Text.Json.JsonDocument.Parse(u.Features).RootElement);
        });
        admin.MapPut("/users/{id:long}/features", async (long id, System.Text.Json.JsonElement input, CdnDbContext db, AuditService audit) =>
        {
            var u = await db.Users.FindAsync(id);
            if (u == null) return Results.NotFound();
            u.Features = input.GetRawText();
            await db.SaveChangesAsync();
            _ = audit.LogAsync($"users.features {id}", input.GetRawText());
            return Results.Ok(new { ok = true });
        });

        // ---- 集群服务状态通用设计----
        admin.MapGet("/clusters/{id:long}/services", async (long id, CdnDbContext db) =>
        {
            var nodes = await db.Nodes.AsNoTracking().Where(n => n.ClusterId == id).OrderBy(n => n.Id).ToListAsync();
            return Results.Ok(nodes.Select(n => new
            {
                nodeId = n.Id,
                name = n.Name,
                role = n.Role,
                version = n.Version ?? "",
                online = n.IsOnline,
                uptime = n.LastHeartbeat.HasValue ? (long)(DateTime.UtcNow - n.LastHeartbeat.Value).TotalSeconds : 0,
                lastHeartbeat = n.LastHeartbeat,
                ips = n.PublicIps,
            }));
        });

        // ---- DDoS 防护状态通用设计----
        admin.MapGet("/clusters/{id:long}/ddos-status", async (long id, CdnDbContext db) =>
        {
            var c = await db.Clusters.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
            if (c == null) return Results.NotFound();
            var nodes = await db.Nodes.AsNoTracking().Where(n => n.ClusterId == id).ToListAsync();
            var metrics = Services.NodeMetricsRegistry.Fresh().ToDictionary(m => m.NodeId, m => m.E.ConfigVersion);
            var applied = nodes.Count(n => n.IsOnline && (!metrics.TryGetValue(n.Id, out var v) || v == 0 || v == c.ConfigVersion));
            return Results.Ok(new
            {
                enabled = c.Ddos?.Enabled ?? false,
                connLimit = c.Ddos?.ConnLimit ?? 0,
                connRate = c.Ddos?.ConnRate ?? 0,
                synFloodLimit = c.Ddos?.SynFloodLimit ?? 0,
                ports = c.Ddos?.Ports ?? [],
                clusterConfigVersion = c.ConfigVersion,
                nodeTotal = nodes.Count,
                nodeOnline = nodes.Count(n => n.IsOnline),
                nodeApplied = applied,
            });
        });

        // ---- 集群 DNS 记录通用设计----
        admin.MapGet("/clusters/{id:long}/dns-records", async (long id, CdnDbContext db) =>
        {
            var c = await db.Clusters.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
            if (c == null) return Results.NotFound();
            var records = new List<object>();
            if (!string.IsNullOrEmpty(c.DnsDomain))
            {
                try
                {
                    foreach (var ip in await System.Net.Dns.GetHostAddressesAsync(c.DnsDomain))
                        records.Add(new { type = "A/" + (ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6 ? "AAAA" : "A"), name = c.DnsDomain, value = ip.ToString() });
                }
                catch (Exception ex)
                {
                    records.Add(new { type = "error", name = c.DnsDomain, value = "解析失败: " + ex.Message });
                }
            }
            var nsDomains = await db.NsDomains.AsNoTracking().OrderBy(d => d.Name).Take(100).ToListAsync();
            return Results.Ok(new
            {
                dnsDomain = c.DnsDomain ?? "",
                records,
                nsDomains = nsDomains.Select(d => new { d.Id, d.Name, d.Type, d.SyncStatus }),
            });
        });

        // ---- IP 库清单管理（手动上传 IP 段，发布期合并进全集群安全配置）----
        var iplib = admin.MapGroup("/iplib");
        iplib.MapGet("", async (CdnDbContext db) =>
            Results.Ok(await db.IpLibrary.OrderBy(i => i.Id).ToListAsync()));
        iplib.MapPost("", async (TnorvCdn.Api.Core.IpLibraryItem input, CdnDbContext db, ConfigPublishQueue pq, AuditService audit) =>
        {
            if (string.IsNullOrWhiteSpace(input.Name) || string.IsNullOrWhiteSpace(input.Cidrs))
                return Results.BadRequest("名称与 IP 段必填");
            input.Id = 0; input.CreatedAt = DateTime.UtcNow;
            db.IpLibrary.Add(input);
            await db.SaveChangesAsync();
            // 全集群重发布使清单立即生效
            var clusterIds = await db.Clusters.Select(c => c.Id).ToListAsync();
            foreach (var cid in clusterIds) pq.Enqueue(cid);
            _ = audit.LogAsync("iplib.add", $"{input.Kind}:{input.Name}");
            return Results.Created($"/api/admin/iplib/{input.Id}", input);
        });
        iplib.MapDelete("/{id:long}", async (long id, CdnDbContext db, ConfigPublishQueue pq, AuditService audit) =>
        {
            var item = await db.IpLibrary.FindAsync(id);
            if (item == null) return Results.NotFound();
            db.IpLibrary.Remove(item);
            await db.SaveChangesAsync();
            var clusterIds = await db.Clusters.Select(c => c.Id).ToListAsync();
            foreach (var cid in clusterIds) pq.Enqueue(cid);
            _ = audit.LogAsync("iplib.delete", $"{item.Kind}:{item.Name}");
            return Results.Ok();
        });

        // ---- IP 库状态通用设计----
        admin.MapGet("/geoip/status", async (CdnDbContext db, GeoIpService geo, string? testIp) =>
        {
            var xdbReady = File.Exists(GeoIpService.XdbPath);
            var recent = await db.AccessLogs.AsNoTracking().OrderByDescending(l => l.Timestamp).Take(500)
                .Select(l => l.RemoteAddr).Distinct().Take(50).ToListAsync();
            var resolved = recent.Count(ip => geo.Resolve(ip).Country != "未知");
            string? testResult = null;
            if (!string.IsNullOrEmpty(testIp))
            {
                await geo.EnsureLoadedAsync();
                await geo.EnqueueBatchAsync([testIp]);
                testResult = geo.Resolve(testIp).Country + (string.IsNullOrEmpty(geo.Resolve(testIp).Province) ? "" : " / " + geo.Resolve(testIp).Province);
            }
            return Results.Ok(new
            {
                provider = xdbReady ? "ip2region xdb 离线库（本地，ip-api 兜底）" : "ip-api.com（自动批量解析，本地永久缓存）",
                xdbReady,
                sampledRecent = recent.Count,
                resolvedRecent = resolved,
                coverage = recent.Count > 0 ? Math.Round(resolved * 100.0 / recent.Count, 1) : 0,
                testIp, testResult,
            });
        });

        // ---- 数据迁移通用设计----
        admin.MapGet("/database/export", async (CdnDbContext db, AuditService audit) =>
        {
            var export = new
            {
                exportedAt = DateTime.UtcNow,
                clusters = await db.Clusters.AsNoTracking().ToListAsync(),
                servers = await db.Servers.AsNoTracking().ToListAsync(),
                plans = await db.Plans.AsNoTracking().ToListAsync(),
                flowPackages = await db.FlowPackages.AsNoTracking().ToListAsync(),
                posts = await db.Posts.AsNoTracking().ToListAsync(),
                siteConfigTemplates = await db.SiteConfigTemplates.AsNoTracking().ToListAsync(),
                acmeAccounts = await db.AcmeAccounts.AsNoTracking().ToListAsync(),
            };
            _ = audit.LogAsync("database.export", "配置 JSON 导出");
            var camel = new System.Text.Json.JsonSerializerOptions { PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase };
            return Results.File(System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(export, camel),
                "application/json", $"tnorv-config-export-{DateTime.UtcNow:yyyyMMddHHmmss}.json");
        });

        admin.MapPost("/database/import", async (System.Text.Json.JsonElement body, CdnDbContext db, AuditService audit) =>
        {
            // 仅导入缺失的行（按 Id 冲突跳过），安全幂等
            var imported = new List<string>();
            try
            {
                if (Prop(body, "clusters", "Clusters") is { } clusters)
                    foreach (var el in clusters.EnumerateArray())
                    {
                        var id = PropId(el);
                        if (id > 0 && !await db.Clusters.AnyAsync(c => c.Id == id))
                        {
                            var c = FromRaw<Cluster>(el);
                            if (c != null) { db.Clusters.Add(c); imported.Add($"cluster:{id}"); }
                        }
                    }
                if (Prop(body, "plans", "Plans") is { } plans)
                    foreach (var el in plans.EnumerateArray())
                    {
                        var id = PropId(el);
                        if (id > 0 && !await db.Plans.AnyAsync(p => p.Id == id))
                        {
                            var p2 = FromRaw<Plan>(el);
                            if (p2 != null) { db.Plans.Add(p2); imported.Add($"plan:{id}"); }
                        }
                    }
                if (Prop(body, "servers", "Servers") is { } servers)
                    foreach (var el in servers.EnumerateArray())
                    {
                        var id = PropId(el);
                        if (id > 0 && !await db.Servers.AnyAsync(s2 => s2.Id == id))
                        {
                            var sv = FromRaw<Server>(el);
                            if (sv != null) { db.Servers.Add(sv); imported.Add($"server:{id}"); }
                        }
                    }
                await db.SaveChangesAsync();
                _ = audit.LogAsync("database.import", $"imported=[{string.Join(',', imported)}]");
                return Results.Ok(new { ok = true, imported });
            }
            catch (Exception ex)
            {
                return Results.Problem("导入失败: " + ex.Message);
            }
        });
        admin.MapGet("/stats/clients", async (CdnDbContext db, int? hours) =>
        {
            var since = DateTime.UtcNow.AddHours(-(hours ?? 24));
            var uas = await db.AccessLogs.AsNoTracking()
                .Where(l => l.Timestamp >= since && l.UserAgent != "")
                .OrderByDescending(l => l.Timestamp)
                .Take(20000)
                .Select(l => l.UserAgent).ToListAsync();
            var browsers = new Dictionary<string, int>();
            var systems = new Dictionary<string, int>();
            foreach (var ua in uas)
            {
                browsers[ClassifyBrowser(ua)] = browsers.GetValueOrDefault(ClassifyBrowser(ua)) + 1;
                systems[ClassifyOs(ua)] = systems.GetValueOrDefault(ClassifyOs(ua)) + 1;
            }
            return Results.Ok(new
            {
                sample = uas.Count,
                browsers = browsers.OrderByDescending(kv => kv.Value).Select(kv => new { name = kv.Key, count = kv.Value }),
                systems = systems.OrderByDescending(kv => kv.Value).Select(kv => new { name = kv.Key, count = kv.Value }),
            });
        });
    }

    private static string ClassifyBrowser(string ua)
    {
        if (ua.Contains("Edg/")) return "Edge";
        if (ua.Contains("OPR/") || ua.Contains("Opera")) return "Opera";
        if (ua.Contains("Chrome/") && !ua.Contains("Chromium")) return "Chrome";
        if (ua.Contains("Chromium")) return "Chromium";
        if (ua.Contains("Firefox/")) return "Firefox";
        if (ua.Contains("Safari/") && ua.Contains("Version/")) return "Safari";
        if (ua.Contains("curl/")) return "curl";
        if (ua.Contains("bot") || ua.Contains("spider") || ua.Contains("Baiduspider") || ua.Contains("Googlebot")) return "爬虫";
        return "其他";
    }

    private static string ClassifyOs(string ua)
    {
        if (ua.Contains("Windows")) return "Windows";
        if (ua.Contains("iPhone") || ua.Contains("iPad")) return "iOS";
        if (ua.Contains("Mac OS") || ua.Contains("Macintosh")) return "macOS";
        if (ua.Contains("Android")) return "Android";
        if (ua.Contains("Linux")) return "Linux";
        return "其他";
    }

    private static System.Text.Json.JsonElement? Prop(System.Text.Json.JsonElement el, string camel, string pascal)
    {
        if (el.ValueKind != System.Text.Json.JsonValueKind.Object) return null;
        if (el.TryGetProperty(camel, out var v)) return v;
        if (el.TryGetProperty(pascal, out var v2)) return v2;
        return null;
    }

    private static string PropRaw(System.Text.Json.JsonElement el, string camel, string pascal)
        => Prop(el, camel, pascal)?.GetRawText() ?? "";

    private static long PropId(System.Text.Json.JsonElement el)
        => Prop(el, "id", "Id")?.GetInt64() ?? 0;

    private static T? FromRaw<T>(System.Text.Json.JsonElement el) where T : class
        => System.Text.Json.JsonSerializer.Deserialize<T>(el.GetRawText(), (System.Text.Json.JsonSerializerOptions?)null);

    public record IdentityReviewRequest(string Status, string? Note);
    public sealed record AdjustBalanceRequest(long UserId, decimal DeltaCents, string Reason);
}
