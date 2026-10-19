using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using TnorvCdn.Api.Data;

namespace TnorvCdn.Api.Services;

/// <summary>
/// 配置发布队列：所有配置变更经此入队，后台单线程按集群串行发布（去重合并）。
/// 两条通道：
/// - 即时（Enqueue/EnqueueAll）：管理端操作、节点回调、NS DNS 记录等关键路径，≤500ms 内发布。
/// - 延迟批（EnqueueDeferred）：用户端配置变更默认走此通道，按 DeferredIntervalSeconds 周期批量发布，
///   把高频保存合并为每周期 ≤1 次发布/集群；站点 RealtimePublish=true 时仍走即时。
/// 队列保证同一集群按最新内容一次发布，节点按版本号拉取，零重复下发。
/// </summary>
public class ConfigPublishQueue
{
    private readonly ConcurrentDictionary<long, byte> _pending = new();
    private readonly ConcurrentDictionary<long, byte> _deferred = new();

    /// <summary>延迟批量发布的周期（秒）。可由配置 Publish:DeferredIntervalSeconds 覆盖，默认 300。</summary>
    public int DeferredIntervalSeconds { get; set; } = 300;

    /// <summary>请求发布某集群（幂等：已在队列则忽略）。立即返回，不等发布完成。</summary>
    public void Enqueue(long clusterId)
    {
        if (clusterId <= 0) return;
        _pending.TryAdd(clusterId, 0);
        _deferred.TryRemove(clusterId, out _); // 即时发布覆盖同集群的待批量发布
    }

    /// <summary>用户侧变更入队：站点开启实时下发 → 立即；否则进入延迟批。</summary>
    public void EnqueueUser(long clusterId, bool realtime)
    {
        if (realtime) Enqueue(clusterId);
        else if (clusterId > 0) _deferred.TryAdd(clusterId, 0);
    }

    /// <summary>取走当前全部待发布集群 ID（发布线程消费）。</summary>
    public long[] Drain()
    {
        var ids = _pending.Keys.ToArray();
        foreach (var id in ids) _pending.TryRemove(id, out _);
        return ids;
    }

    /// <summary>到期取走全部延迟批集群（未到期返回空）。</summary>
    public long[] DrainDeferred(DateTime now)
    {
        if (_lastDeferredFlush.AddSeconds(DeferredIntervalSeconds) > now) return [];
        _lastDeferredFlush = now;
        var ids = _deferred.Keys.ToArray();
        foreach (var id in ids) _deferred.TryRemove(id, out _);
        return ids;
    }

    private DateTime _lastDeferredFlush = DateTime.UtcNow;

    /// <summary>全部集群入队（ACME 账户等全局配置变更时用）。</summary>
    public void EnqueueAll()
    {
        _allClusters = true;
    }

    /// <summary>全局发布标记：Drain 时若置位则由 Worker 补齐所有集群。</summary>
    public volatile bool _allClusters;
}

/// <summary>后台发布线程：串行消费发布队列，落库版本号。节点轮询/拉取时按版本比对增量下发。</summary>
public class ConfigPublishWorker(ConfigPublishQueue queue, IServiceScopeFactory scopes, ILogger<ConfigPublishWorker> logger) : BackgroundService
{
    private DateTime _lastSslSweep = DateTime.MinValue;

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        // 启动兜底：全集群发布一次（内存队列重启丢失后恢复节点一致性）
        queue.EnqueueAll();
        // 从配置读取延迟批周期
        try
        {
            await using var bootScope = scopes.CreateAsyncScope();
            var cfg = bootScope.ServiceProvider.GetRequiredService<IConfiguration>();
            if (int.TryParse(cfg["Publish:DeferredIntervalSeconds"], out var sec) && sec is >= 30 and <= 86400)
                queue.DeferredIntervalSeconds = sec;
        }
        catch { /* 默认 300s */ }

        while (!ct.IsCancellationRequested)
        {
            try
            {
                // 证书状态巡检：签发中超过 15 分钟无心跳推进 → 判定失败（节点无人访问/签发卡死），记录事件，允许重试
                if ((DateTime.UtcNow - _lastSslSweep).TotalSeconds >= 60)
                {
                    _lastSslSweep = DateTime.UtcNow;
                    await using var sweepScope = scopes.CreateAsyncScope();
                    var db2 = sweepScope.ServiceProvider.GetRequiredService<CdnDbContext>();
                    var stale = await db2.Servers
                        .Where(s => s.SslMode == "auto" && s.SslState == "pending_issuing" &&
                                    s.SslLastAttemptAt != null && DateTime.UtcNow - s.SslLastAttemptAt > TimeSpan.FromMinutes(15))
                        .ToListAsync(ct);
                    foreach (var s2 in stale)
                    {
                        s2.SslState = "failed";
                        s2.SslLastError = "签发超时（15 分钟无进展），已自动标记失败，可重试";
                        s2.SslRenewRequestedAt = null;
                        db2.CertEvents.Add(new TnorvCdn.Api.Core.CertEvent
                        {
                            ServerId = s2.Id, NodeId = 0, Domain = string.Join(",", s2.Domains),
                            Status = "timeout", Error = s2.SslLastError, CreatedAt = DateTime.UtcNow,
                        });
                    }
                    if (stale.Count > 0) await db2.SaveChangesAsync(ct);
                }

                if (queue._allClusters)
                {
                    queue._allClusters = false;
                    await using var scope0 = scopes.CreateAsyncScope();
                    var db0 = scope0.ServiceProvider.GetRequiredService<CdnDbContext>();
                    foreach (var cid in await db0.Clusters.Select(c => c.Id).ToListAsync(ct)) queue.Enqueue(cid);
                }
                var ids = queue.Drain();
                if (ids.Length == 0) ids = queue.DrainDeferred(DateTime.UtcNow);
                if (ids.Length == 0)
                {
                    await Task.Delay(500, ct);
                    continue;
                }
                foreach (var clusterId in ids)
                {
                    try
                    {
                        await using var scope = scopes.CreateAsyncScope();
                        var publisher = scope.ServiceProvider.GetRequiredService<ConfigPublisher>();
                        var version = await publisher.PublishAsync(clusterId);
                        logger.LogInformation("发布队列：集群 {ClusterId} → 版本 {Version}", clusterId, version);
                    }
                    catch (Exception ex)
                    {
                        logger.LogError(ex, "发布队列：集群 {ClusterId} 发布失败，稍后重试", clusterId);
                        queue.Enqueue(clusterId); // 失败重回即时队列
                    }
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                logger.LogError(ex, "发布线程异常");
                try { await Task.Delay(2000, ct); } catch { }
            }
        }
    }
}
