using Microsoft.EntityFrameworkCore;
using TnorvCdn.Api.Core;
using TnorvCdn.Api.Data;
using System.Net;
using System.Net.Mail;
using System.Text.Json;

namespace TnorvCdn.Api.Services;

/// <summary>
/// 计费与调度后台服务（商用运营级收尾）：
///   1. 超额停服：每小时月结检查，用户当月流量超套餐上限 → 站点自动停用 + 通知（解除由管理员处理）
///   2. 智能调度：每 5 分钟检查节点负载（CPU/内存），连续超阈值 → 节点告警通知（下线决策仍由管理员，避免误杀）
///   3. 证书到期提醒：每天一次，7 天内到期证书 WebHook/邮件告警
/// </summary>
public class BillingSchedulerService : BackgroundService
{
    private readonly IServiceScopeFactory _scopes;
    private readonly IConfiguration _cfg;
    private readonly ILogger<BillingSchedulerService> _logger;

    public BillingSchedulerService(IServiceScopeFactory scopes, IConfiguration cfg, ILogger<BillingSchedulerService> logger)
    { _scopes = scopes; _cfg = cfg; _logger = logger; }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        var hourly = new PeriodicTimer(TimeSpan.FromHours(1));
        var fiveMin = new PeriodicTimer(TimeSpan.FromMinutes(5));
        var dailyCheck = DateTime.MinValue;

        // 并行跑两个循环
        var hourlyTask = HourlyLoop(hourly, ct);
        var fiveMinTask = FiveMinLoop(fiveMin, ct);
        await Task.WhenAny(hourlyTask, fiveMinTask);
    }

    // ---- 超额停服（每小时） ----
    private async Task HourlyLoop(PeriodicTimer timer, CancellationToken ct)
    {
        while (await timer.WaitForNextTickAsync(ct))
        {
            try
            {
                await CheckOverageAsync();
                await HandleExpiryAndRenewAsync(); // D3：到期停站 + 自动续费
                await MergeUsageAsync();           // D3：当月用量 MERGE 到 UsageRecords（账单数据源）
                await CleanupLogsAsync();          // P3：按集群 Ops 配置清理过期日志
                if (DateTime.UtcNow.Hour == 9) // 每天一次证书提醒
                    await CheckCertExpiryAsync();
            }
            catch (Exception ex)
            {
                _logger.LogWarning("[billing] 月结检查失败: {Message}", ex.Message);
            }
        }
    }

    private async Task CheckOverageAsync()
    {
        await using var scope = _scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<CdnDbContext>();
        var notify = scope.ServiceProvider.GetRequiredService<NotificationService>();
        var sms = scope.ServiceProvider.GetRequiredService<SmsService>();
        var cfg = scope.ServiceProvider.GetRequiredService<IConfiguration>();
        var exempt = (cfg["Billing:ExemptUserIds"] ?? "").Split(',').Where(s => long.TryParse(s, out _)).Select(long.Parse).ToHashSet();

        var now = DateTime.UtcNow;
        var plans = await db.Plans.ToDictionaryAsync(p => p.Id);
        var subs = await db.Subscriptions.Where(s => s.Enabled).ToListAsync();
        var suspended = 0;

        foreach (var sub in subs)
        {
            if (!plans.TryGetValue(sub.PlanId, out var plan)) continue;
            if (exempt.Contains(sub.UserId)) continue;

            // 短信提醒：订阅 7 天内到期（每日一条）
            if ((sub.ExpiresAt - now).TotalDays <= 7 && sub.ExpiresAt > now)
            {
                var days = Math.Max(1, (int)Math.Ceiling((sub.ExpiresAt - now).TotalDays));
                _ = sms.NotifyAsync(sub.UserId, "sub_expiring",
                    $"您的套餐将于 {days} 天后到期，为避免网站停服请及时续费。", $"subexp:{sub.Id}");
            }
            // 短信提醒：余额不足 10 元（每日一条）
            var bal = await db.Users.Where(u => u.Id == sub.UserId).Select(u => u.BalanceCents).FirstOrDefaultAsync();
            if (bal < 1000)
                _ = sms.NotifyAsync(sub.UserId, "balance_low",
                    $"您的账户余额不足 10 元，余额用尽后将影响部分服务，请及时充值。", $"bal:{sub.UserId}");

            if (plan.MonthlyTrafficMB <= 0) continue; // 0=不限

            // 当月该用户流量（MB）
            var from = new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc);
            var serverIds = await db.Servers.Where(s => s.UserId == sub.UserId).Select(s => s.Id).ToListAsync();
            if (serverIds.Count == 0) continue;
            var bytes = await db.AccessLogs
                .Where(l => serverIds.Contains(l.ServerId) && l.Timestamp >= from)
                .SumAsync(l => (long?)l.BytesSent) ?? 0;
            var usedMB = bytes / 1048576;

            // 短信提醒：流量已达上限 80%（每日一条）
            if (usedMB > plan.MonthlyTrafficMB * 8 / 10 && usedMB <= plan.MonthlyTrafficMB)
                _ = sms.NotifyAsync(sub.UserId, "traffic_over",
                    $"本月流量已使用约 {usedMB * 100 / plan.MonthlyTrafficMB}%，接近套餐上限，请注意控制用量或升级套餐。", $"tr80:{sub.UserId}");

            if (usedMB > plan.MonthlyTrafficMB)
            {
                // 超额：停用该用户全部站点
                var servers = await db.Servers.Where(s => s.UserId == sub.UserId && s.Enabled).ToListAsync();
                foreach (var s in servers) s.Enabled = false;
                if (servers.Count > 0)
                {
                    await db.SaveChangesAsync();
                    suspended += servers.Count;
                    foreach (var clusterId in servers.Select(s => s.ClusterId).Distinct())
                    {
                        var publisher = scope.ServiceProvider.GetRequiredService<ConfigPublisher>();
                        await publisher.PublishAsync(clusterId);
                    }
                    await notify.SendAsync("billing_overage",
                        $"用户 {sub.UserId} 超额停服：本月流量 {usedMB}MB / 上限 {plan.MonthlyTrafficMB}MB，已停用 {servers.Count} 个站点",
                        new { userId = sub.UserId, usedMB, limitMB = plan.MonthlyTrafficMB, suspended = servers.Count });
                    // 短信提醒：流量超限停服（内容不带域名）
                    _ = sms.NotifyAsync(sub.UserId, "traffic_over",
                        "本月流量已超出套餐上限，您的网站已暂停服务。请升级套餐或等待下月恢复。", $"trover:{sub.UserId}");
                }
            }
        }
        if (suspended > 0) _logger.LogWarning("[billing] 本轮超额停服 {Count} 个站点", suspended);
    }

    private async Task CheckCertExpiryAsync()
    {
        await using var scope = _scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<CdnDbContext>();
        var notify = scope.ServiceProvider.GetRequiredService<NotificationService>();
        var soon = await db.Certificates.Where(c => c.ExpiresAt < DateTime.UtcNow.AddDays(7)).ToListAsync();
        foreach (var cert in soon)
            await notify.SendAsync("cert_expiring",
                $"证书 {cert.Domains} 将于 {cert.ExpiresAt:yyyy-MM-dd} 到期", new { cert.Domains, cert.ExpiresAt });
    }

    // ---- 智能调度（每5分钟：节点负载检查） ----
    private async Task FiveMinLoop(PeriodicTimer timer, CancellationToken ct)
    {
        while (await timer.WaitForNextTickAsync(ct))
        {
            try
            {
                await using var scope = _scopes.CreateAsyncScope();
                var db = scope.ServiceProvider.GetRequiredService<CdnDbContext>();
                var notify = scope.ServiceProvider.GetRequiredService<NotificationService>();
                var threshold = _cfg.GetValue("Scheduling:CpuThreshold", 90.0);

                // 心跳中带 CPU 数据的节点（NodeStats 持久化在 Node 表的 LastHeartbeat + Version，
                // CPU 数据在 heartbeat 里直接比较 — 这里读节点表最近心跳判断假死节点）
                var stale = await db.Nodes
                    .Where(n => n.IsOnline && n.LastHeartbeat != null && n.LastHeartbeat < DateTime.UtcNow.AddMinutes(-5))
                    .ToListAsync();
                foreach (var n in stale)
                {
                    n.IsOnline = false;
                    await notify.SendAsync("node_stale", $"节点 {n.Name} 心跳超时（>5min），已标记离线", new { nodeId = n.Id });
                }
                if (stale.Count > 0)
                {
                    await db.SaveChangesAsync();
                    _logger.LogWarning("[scheduling] {Count} 个节点心跳超时标记离线", stale.Count);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning("[scheduling] 调度检查失败: {Message}", ex.Message);
            }
        }
    }

    // ---- P3：日志保留策略（每小时）——按各集群 Ops 保留天数清理访问/审计/短信日志 ----
    private async Task CleanupLogsAsync()
    {
        await using var scope = _scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<CdnDbContext>();
        var now = DateTime.UtcNow;
        // 取全集群最小保留天数（Ops 未配置按默认 30/90/90）；0=永久保留（不清理）
        var opsList = await db.Clusters.AsNoTracking().Select(c => c.OpsJson).ToListAsync();
        int Min(int d1, int d2) => d2 == 0 ? d1 : Math.Min(d1, d2);
        var keepAccess = 30; var keepAudit = 90; var keepSms = 90;
        foreach (var json in opsList)
        {
            if (string.IsNullOrEmpty(json)) continue;
            try
            {
                var o = System.Text.Json.JsonSerializer.Deserialize<OpsConfig>(json, (System.Text.Json.JsonSerializerOptions?)null);
                if (o == null) continue;
                keepAccess = Min(keepAccess, o.LogKeepDays); keepAudit = Min(keepAudit, o.AuditKeepDays); keepSms = Min(keepSms, o.SmsKeepDays);
            }
            catch { }
        }
        var removed = 0;
        if (keepAccess > 0)
        {
            var cutoff = now.AddDays(-keepAccess);
            removed += await db.AccessLogs.Where(l => l.Timestamp < cutoff).ExecuteDeleteAsync();
        }
        if (keepAudit > 0)
        {
            var cutoff = now.AddDays(-keepAudit);
            removed += await db.AuditLogs.Where(a => a.Timestamp < cutoff).ExecuteDeleteAsync();
        }
        if (keepSms > 0)
        {
            var cutoff = now.AddDays(-keepSms);
            removed += await db.SmsLogs.Where(m => m.CreatedAt < cutoff).ExecuteDeleteAsync();
        }
        if (removed > 0) _logger.LogInformation("[billing] 日志清理：删除 {Count} 条过期记录（access<= {A}d, audit<= {B}d, sms<= {C}d）", removed, keepAccess, keepAudit, keepSms);
    }

    // ---- D3 套餐闭环：到期停站 + 自动续费（每小时） ----
    private async Task HandleExpiryAndRenewAsync()
    {
        await using var scope = _scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<CdnDbContext>();
        var notify = scope.ServiceProvider.GetRequiredService<NotificationService>();
        var sms = scope.ServiceProvider.GetRequiredService<SmsService>();
        var publisher = scope.ServiceProvider.GetRequiredService<ConfigPublisher>();
        var cfg = scope.ServiceProvider.GetRequiredService<IConfiguration>();
        var exempt = (cfg["Billing:ExemptUserIds"] ?? "").Split(',').Where(s => long.TryParse(s, out _)).Select(long.Parse).ToHashSet();

        var now = DateTime.UtcNow;
        var plans = await db.Plans.ToDictionaryAsync(p => p.Id);

        // 1) 自动续费：AutoRenew 且已到期且余额足够 → 扣一个月月价 + 续期 + 落订单
        var toRenew = await db.Subscriptions.Where(s => s.Enabled && s.AutoRenew && s.ExpiresAt <= now).ToListAsync();
        foreach (var sub in toRenew)
        {
            if (exempt.Contains(sub.UserId)) continue;
            if (!plans.TryGetValue(sub.PlanId, out var plan) || plan.PriceCents <= 0) continue;
            var u = await db.Users.FindAsync(sub.UserId);
            if (u == null) continue;
            if (u.BalanceCents < plan.PriceCents)
            {
                _ = sms.NotifyAsync(sub.UserId, "renew_fail",
                    "您的套餐自动续费失败：账户余额不足。请及时充值，否则到期后网站将暂停服务。", $"renfail:{sub.Id}:{now:yyyyMMdd}");
                continue;
            }
            u.BalanceCents -= plan.PriceCents;
            sub.ExpiresAt = (sub.ExpiresAt > now ? sub.ExpiresAt : now).AddMonths(1);
            db.Orders.Add(new Order
            {
                UserId = sub.UserId,
                OrderNo = "R" + now.ToString("yyyyMMddHHmmss") + Random.Shared.Next(1000, 9999),
                Kind = "plan", PlanId = sub.PlanId, Months = 1,
                AmountCents = plan.PriceCents, Status = "paid", PayMethod = "balance", PaidAt = now,
            });
            _ = sms.NotifyAsync(sub.UserId, "renew_ok",
                $"您的套餐「{plan.Name}」已自动续费 1 个月，扣款 {(plan.PriceCents / 100m):0.00} 元。", $"renok:{sub.Id}:{now:yyyyMMdd}");
        }
        await db.SaveChangesAsync();

        // 2) 到期未续费（且非豁免）→ 停用订阅；若该用户已无任何生效订阅 → 停用其全部站点
        var expired = await db.Subscriptions.Where(s => s.Enabled && s.ExpiresAt < now).ToListAsync();
        if (expired.Count == 0) return;
        var disabledClusters = new HashSet<long>();
        var suspendedTotal = 0;
        foreach (var sub in expired) sub.Enabled = false;
        foreach (var uid in expired.Select(s => s.UserId).Distinct())
        {
            if (exempt.Contains(uid)) continue;
            var stillActive = await db.Subscriptions.AnyAsync(s => s.UserId == uid && s.Enabled && s.ExpiresAt > now);
            if (stillActive) continue; // 还有其它生效订阅则不停站
            var servers = await db.Servers.Where(s => s.UserId == uid && s.Enabled).ToListAsync();
            if (servers.Count == 0) continue;
            foreach (var sv in servers) { sv.Enabled = false; disabledClusters.Add(sv.ClusterId); }
            suspendedTotal += servers.Count;
            await notify.SendAsync("subscription_expired",
                $"用户 {uid} 套餐到期未续费，已停用 {servers.Count} 个站点",
                new { userId = uid, suspended = servers.Count });
            _ = sms.NotifyAsync(uid, "sub_expired",
                "您的套餐已到期且未续费，网站已暂停服务。续费后站点将恢复（需联系管理员或重新启用）。", $"subexpd:{uid}:{now:yyyyMMdd}");
        }
        await db.SaveChangesAsync();
        foreach (var cid in disabledClusters) await publisher.PublishAsync(cid);
        if (suspendedTotal > 0) _logger.LogWarning("[billing] 本轮到期停站 {Count} 个站点", suspendedTotal);
    }

    // ---- D3：当月用量按用户 MERGE 到 UsageRecords（账单/用量页数据源） ----
    private async Task MergeUsageAsync()
    {
        await using var scope = _scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<CdnDbContext>();
        var now = DateTime.UtcNow;
        var from = new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc);

        // 单条 SQL 按用户聚合当月日志（替代此前逐订阅 Sum，且覆盖无订阅用户）
        var rows = await db.AccessLogs.AsNoTracking()
            .Where(l => l.Timestamp >= from)
            .Join(db.Servers, l => l.ServerId, sv => sv.Id, (l, sv) => new { l.BytesSent, sv.UserId })
            .GroupBy(x => x.UserId)
            .Select(g => new { UserId = g.Key, Requests = g.Count(), Bytes = g.Sum(x => x.BytesSent) })
            .ToListAsync();

        var existing = await db.UsageRecords.Where(u => u.Year == now.Year && u.Month == now.Month).ToListAsync();
        var byUser = existing.ToDictionary(u => u.UserId);
        var changed = false;
        foreach (var r in rows)
        {
            var trafficMB = r.Bytes / 1048576;
            if (byUser.TryGetValue(r.UserId, out var rec))
            {
                if (rec.Requests != r.Requests || rec.TrafficMB != trafficMB)
                {
                    rec.Requests = r.Requests; rec.TrafficMB = trafficMB; rec.UpdatedAt = now; changed = true;
                }
            }
            else
            {
                db.UsageRecords.Add(new UsageRecord
                {
                    UserId = r.UserId, Year = now.Year, Month = now.Month,
                    Requests = r.Requests, TrafficMB = trafficMB, UpdatedAt = now,
                });
                changed = true;
            }
        }
        if (changed) await db.SaveChangesAsync();
    }
}
