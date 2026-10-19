using Microsoft.EntityFrameworkCore;
using TnorvCdn.Api.Data;

namespace TnorvCdn.Api.Services;

/// <summary>GeoIP 启动加载：服务启动即加载/下载 ip2region xdb（离线库优先，ip-api 仅兜底）。
/// 周期任务保留：每 5 分钟对近 24h 日志中 xdb 未命中的独立 IP 批量走 ip-api 兜底。</summary>
public class GeoIpBackgroundService(GeoIpService geo, IServiceScopeFactory scopes, ILogger<GeoIpBackgroundService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        // 启动即加载 xdb（失败不阻塞主服务，ip-api 兜底仍可用）
        try { await Task.Delay(TimeSpan.FromSeconds(5), ct); } catch { return; }
        var loaded = await geo.EnsureLoadedAsync(ct);
        if (!loaded) logger.LogWarning("GeoIP xdb 未能加载（下载源均失败），仅 ip-api 兜底可用");

        while (!ct.IsCancellationRequested)
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var db = scope.ServiceProvider.GetRequiredService<CdnDbContext>();
                var since = DateTime.UtcNow.AddHours(-24);
                // 每轮取 400 个未解析 IP（4 批 × 100）
                var ips = await db.AccessLogs.AsNoTracking()
                    .Where(l => l.Timestamp >= since)
                    .Select(l => l.RemoteAddr)
                    .Distinct()
                    .Take(400)
                    .ToListAsync(ct);
                var unresolved = ips.Where(ip => geo.Resolve(ip).Country == "未知").ToList();
                for (int i = 0; i < unresolved.Count; i += 100)
                {
                    await geo.EnqueueBatchAsync(unresolved.Skip(i).Take(100));
                    try { await Task.Delay(TimeSpan.FromSeconds(15), ct); } catch { return; }
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "GeoIP 后台解析异常");
            }
            try { await Task.Delay(TimeSpan.FromMinutes(5), ct); } catch { return; }
        }
    }
}
