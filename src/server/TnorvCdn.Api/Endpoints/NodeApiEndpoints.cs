using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TnorvCdn.Api.Core;
using TnorvCdn.Api.Data;
using TnorvCdn.Api.Services;

namespace TnorvCdn.Api.Endpoints;

/// <summary>
/// 节点通信 HTTP 端点（/grpc/*）。
/// M1 阶段节点走 HTTP/JSON；语义与 proto/tnorv.proto 完全同构，P1 切真 gRPC 时仅改节点传输层。
/// </summary>
public static class NodeApiEndpoints
{
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    public static void MapNodeApiEndpoints(this WebApplication app)
    {
        var grp = app.MapGroup("/grpc");

        grp.MapPost("/register", async (RegisterBody req, CdnDbContext db) =>
        {
            if (string.IsNullOrWhiteSpace(req.InstallToken))
                return Results.BadRequest(new { error = "install_token required" });

            var node = await db.Nodes.FirstOrDefaultAsync(n => n.InstallToken == req.InstallToken);
            if (node == null)
                return Results.Json(new { error = "invalid install token" }, statusCode: 403);

            node.InstallToken = null;                       // 一次性消耗，防重放
            node.Token = Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
            node.Hostname = req.Hostname;
            node.Version = req.Version;
            node.PublicIps = req.PublicIps ?? [];
            node.IsOnline = true;
            node.LastHeartbeat = DateTime.UtcNow;
            await db.SaveChangesAsync();

            return Results.Ok(new { nodeId = node.Id, nodeToken = node.Token });
        });

        grp.MapPost("/config", async (ConfigBody req, CdnDbContext db, ConfigPublisher publisher, HttpContext http) =>
        {
            var node = await AuthorizeAsync(db, req.NodeToken);
            if (node == null) return Results.Json(new { error = "invalid node token" }, statusCode: 401);

            // 持久化版本比对：节点版本 = 集群已发布版本 → 无变化，不重复下发（节点大量轮询零成本）
            var clusterVersion = await db.Clusters.Where(c => c.Id == node.ClusterId)
                .Select(c => (long?)c.ConfigVersion).FirstOrDefaultAsync() ?? 0;
            if (req.ConfigVersion > 0 && req.ConfigVersion == clusterVersion)
                return Results.Ok(new { configVersion = clusterVersion, configJson = (string?)null, changed = false });

            var json = await publisher.BuildNodeConfigAsync(node.Id);
            if (json == null) return Results.Json(new { error = "config unavailable" }, statusCode: 404);

            // 节点可能是 NAT 环境（内网探测不到公网 IP）：把主控看到的连接来源 IP 下发，供 ACME DNS 预校验使用
            var remoteIp = http.Connection.RemoteIpAddress?.ToString();
            if (remoteIp != null && (remoteIp.StartsWith("::ffff:") || remoteIp == "::1")) remoteIp = remoteIp.Replace("::ffff:", "");
            if (remoteIp == "::1") remoteIp = null;
            return Results.Ok(new
            {
                configVersion = clusterVersion == 0 ? DateTimeOffset.UtcNow.ToUnixTimeSeconds() : clusterVersion,
                configJson = json,
                changed = true,
                publicIp = remoteIp
            });
        });

        // 受控签发：执行节点注册 HTTP-01 挑战（随配置下发全部节点监听应答）
        grp.MapPost("/challenge-register", async (ChallengeRegisterBody req, CdnDbContext db, Services.ChallengeRegistry challenges) =>
        {
            var node = await AuthorizeAsync(db, req.NodeToken);
            if (node == null) return Results.Json(new { error = "invalid node token" }, statusCode: 401);
            if (string.IsNullOrWhiteSpace(req.Path) || string.IsNullOrWhiteSpace(req.KeyAuth))
                return Results.BadRequest("path/keyAuth required");
            challenges.Register(node.ClusterId, req.Path, req.KeyAuth);
            // 关键：bump 集群配置版本，让全部节点在下个拉取周期（10s）内收到挑战并回执 challenge-ready
            var cluster = await db.Clusters.FindAsync(node.ClusterId);
            if (cluster != null)
            {
                cluster.ConfigVersion = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                await db.SaveChangesAsync();
            }
            return Results.Ok(new { ok = true });
        });

        // 节点确认"已收到挑战并在监听该路径"（主控聚合就绪回执，供执行节点 Accept 前确认全节点就绪）
        grp.MapPost("/challenge-ready", async (ChallengeRegisterBody req, CdnDbContext db, Services.ChallengeRegistry challenges) =>
        {
            var node = await AuthorizeAsync(db, req.NodeToken);
            if (node == null) return Results.Json(new { error = "invalid node token" }, statusCode: 401);
            if (!string.IsNullOrEmpty(req.Path)) challenges.MarkReady(node.Id, req.Path);
            return Results.Ok(new { ok = true });
        });

        // 执行节点查询：某挑战路径的就绪状态（全部在线节点是否已监听）
        grp.MapPost("/challenge-status", async (ChallengeRegisterBody req, CdnDbContext db, Services.ChallengeRegistry challenges) =>
        {
            var node = await AuthorizeAsync(db, req.NodeToken);
            if (node == null) return Results.Json(new { error = "invalid node token" }, statusCode: 401);
            var path = req.Path ?? "";
            var ready = challenges.ReadyNodes(path);
            var onlineNodeIds = await db.Nodes.Where(n => n.ClusterId == node.ClusterId && n.IsOnline).Select(n => n.Id).ToListAsync();
            var allReady = onlineNodeIds.Count > 0 && onlineNodeIds.All(ready.Contains);
            return Results.Ok(new { ok = true, readyCount = ready.Count, onlineCount = onlineNodeIds.Count, allReady });
        });

        grp.MapPost("/challenge-clear", async (ChallengeRegisterBody req, CdnDbContext db, Services.ChallengeRegistry challenges) =>
        {
            var node = await AuthorizeAsync(db, req.NodeToken);
            if (node == null) return Results.Json(new { error = "invalid node token" }, statusCode: 401);
            challenges.Clear(node.ClusterId, req.Path ?? "");
            return Results.Ok(new { ok = true });
        });

        // 受控签发：执行节点签发完成后把证书上报主控（主控存库并下发全部节点 SNI）
        grp.MapPost("/cert-issued", async (CertIssuedBody req, CdnDbContext db, ConfigPublishQueue publishQueue) =>
        {
            var node = await AuthorizeAsync(db, req.NodeToken);
            if (node == null) return Results.Json(new { error = "invalid node token" }, statusCode: 401);
            if (string.IsNullOrWhiteSpace(req.Domain) || string.IsNullOrWhiteSpace(req.CertPem))
                return Results.BadRequest("domain/certPem required");

            var domain = req.Domain.Trim().ToLowerInvariant();
            DateTime? expiresAt = null;
            try
            {
                var cert = System.Security.Cryptography.X509Certificates.X509Certificate2.CreateFromPem(req.CertPem, req.KeyPem ?? req.CertPem);
                expiresAt = cert.NotAfter.ToUniversalTime();
            }
            catch { }

            // 存证书（Source=acme，随 manualCerts 通道下发全部节点 SNI）
            var clusterId = node.ClusterId;
            var existing = await db.Certificates.Where(c => c.Source == "acme" && c.ClusterId == clusterId && c.Domains.Contains(domain)).ToListAsync();
            foreach (var old in existing) db.Certificates.Remove(old);
            db.Certificates.Add(new Certificate
            {
                ClusterId = clusterId,
                Domains = domain,
                CertPem = req.CertPem,
                KeyPem = req.KeyPem ?? "",
                ExpiresAt = expiresAt,
                Source = "acme",
                CreatedAt = DateTime.UtcNow,
            });

            // 更新站点状态
            var servers = await db.Servers.Where(s => s.SslMode == "auto").ToListAsync();
            foreach (var srv in servers)
            {
                if (srv.Domains.Any(d => d.Trim().Equals(domain, StringComparison.OrdinalIgnoreCase)))
                {
                    srv.SslState = "issued";
                    srv.SslExpiresAt = expiresAt;
                    srv.SslLastError = "";
                    srv.SslRenewRequestedAt = null;
                    db.CertEvents.Add(new CertEvent { ServerId = srv.Id, NodeId = node.Id, Domain = domain, Status = "issued", Error = "", CreatedAt = DateTime.UtcNow });
                }
            }
            await db.SaveChangesAsync();
            publishQueue.Enqueue(clusterId); // 证书随 manualCerts 下发全部节点
            return Results.Ok(new { ok = true, expiresAt });
        });

        grp.MapPost("/cert-failed", async (CertFailedBody req, CdnDbContext db, Services.SmsService sms) =>
        {
            var node = await AuthorizeAsync(db, req.NodeToken);
            if (node == null) return Results.Json(new { error = "invalid node token" }, statusCode: 401);
            var domain = (req.Domain ?? "").Trim().ToLowerInvariant();
            var servers = await db.Servers.Where(s => s.SslMode == "auto").ToListAsync();
            foreach (var srv in servers)
            {
                if (srv.Domains.Any(d => d.Trim().Equals(domain, StringComparison.OrdinalIgnoreCase)) && srv.SslState != "issued")
                {
                    srv.SslState = "failed";
                    srv.SslLastError = req.Error ?? "";
                    srv.SslRenewRequestedAt = null;
                    db.CertEvents.Add(new CertEvent { ServerId = srv.Id, NodeId = node.Id, Domain = domain, Status = "failed", Error = req.Error ?? "", CreatedAt = DateTime.UtcNow });
                    // 短信提醒站点主人（内容不带域名，短信风控禁止）
                    _ = sms.NotifyAsync(srv.UserId, "cert_failed", $"您的网站证书签发失败，请登录控制台在证书页查看原因并重试。", $"cert_failed:{srv.Id}");
                }
            }
            await db.SaveChangesAsync();
            return Results.Ok(new { ok = true });
        });

        grp.MapPost("/heartbeat", async (HeartbeatBody req, CdnDbContext db) =>
        {
            var node = await AuthorizeAsync(db, req.NodeToken);
            if (node == null) return Results.Json(new { error = "invalid node token" }, statusCode: 401);

            node.IsOnline = true;
            node.LastHeartbeat = DateTime.UtcNow;
            await db.SaveChangesAsync();

            // 实时指标入库内存（看板 /admin/metrics/realtime 聚合展示）；MetricsEnabled=false 时跳过采集
            if (req.Stats != null)
            {
                var cluster = await db.Clusters.AsNoTracking().FirstOrDefaultAsync(c => c.Id == node.ClusterId);
                var opsFlag = string.IsNullOrEmpty(cluster?.OpsJson)
                    ? new Core.OpsConfig()
                    : System.Text.Json.JsonSerializer.Deserialize<Core.OpsConfig>(cluster.OpsJson, (System.Text.Json.JsonSerializerOptions?)null) ?? new Core.OpsConfig();
                if (opsFlag.MetricsEnabled)
                    Services.NodeMetricsRegistry.Update(node.Id, req.Stats.CpuUsage, req.Stats.MemUsage, req.Stats.Load, req.Stats.Connections, req.ConfigVersion);
                if (opsFlag.AlertsEnabled)
                    await CheckThresholdsAsync(db, node, req.Stats);
            }

            // 就近回源拨测数据更新（动态回源路径计算）
            if (req.Stats?.OriginProbes is { Count: > 0 } || req.Stats?.PeerProbes is { Count: > 0 })
            {
                RoutingSchedulerService.UpdateProbes(
                    node.Id,
                    (req.Stats?.OriginProbes ?? new List<OriginProbeBody>()).Select(o2 => (o2.Origin, o2.LatencyMs)),
                    (req.Stats?.PeerProbes ?? new List<PeerProbeBody>()).Select(p2 => (p2.Peer, p2.LatencyMs)));
            }

            // ACME 证书签发状态上报：更新 Server.SslState（用户侧只读展示）+ issued 时写 Certificates 表
            if (req.Stats?.CertStates is { Count: > 0 })
            {
                // EF 无法翻译 JSON 数组列的 Contains，改为内存匹配（_domains 列为 string[] 值转换器）
                var autoServers = await db.Servers.Where(s => s.SslMode == "auto").ToListAsync();
                foreach (var cs in req.Stats.CertStates)
                {
                    if (string.IsNullOrWhiteSpace(cs.Domain)) continue;
                    var domain = cs.Domain.Trim().ToLowerInvariant();
                    var normalizedStatus = cs.Status switch
                    {
                        "pending_dns" => "pending_dns",
                        "issuing" => "pending_issuing",
                        "issued" => "issued",
                        "failed" => "failed",
                        _ => null,
                    };
                    if (normalizedStatus == null) continue;
                    // 精确/通配匹配：不再按注册域模糊匹配（同注册域的其它站点会被错误归并状态）
                    var target = autoServers.FirstOrDefault(s => s.Domains.Any(d =>
                    {
                        var dn = d.Trim();
                        if (dn.StartsWith('*')) dn = dn[1..].TrimStart('.');
                        return dn.Equals(domain, StringComparison.OrdinalIgnoreCase);
                    }));
                    if (target == null) continue;
                    // 多节点上报保护：域名已由其它节点签发成功（issued）时，未解析节点上报的
                    // pending_dns/failed 不覆盖成功状态（域名解析指向哪个节点，哪个节点的状态才算数）
                    if (normalizedStatus != "issued" && target.SslState == "issued") continue;
                    // 签发刚尝试过（2 分钟内）：未解析/失败等中间态不覆盖 pending_issuing（多节点状态互踩保护）
                    if (normalizedStatus is "pending_dns" && target.SslState == "pending_issuing" &&
                        target.SslLastAttemptAt != null && DateTime.UtcNow - target.SslLastAttemptAt < TimeSpan.FromMinutes(2)) continue;
                    target.SslState = normalizedStatus;
                    target.SslLastAttemptAt = DateTime.UtcNow;
                    target.SslLastError = cs.Error ?? "";
                    if (normalizedStatus != null && target.SslState != normalizedStatus)
                    {
                        db.CertEvents.Add(new CertEvent
                        {
                            ServerId = target.Id, NodeId = node.Id, Domain = domain,
                            Status = normalizedStatus, Error = cs.Error ?? "", CreatedAt = DateTime.UtcNow,
                        });
                    }
                    // 状态推进成功（issued/pending_dns/failed）：清除续期标记，允许再次发起
                    if (normalizedStatus is "issued" or "failed" or "pending_dns")
                        target.SslRenewRequestedAt = null;
                    if (cs.Status == "issued" && !string.IsNullOrEmpty(cs.ExpiresAt) &&
                        DateTime.TryParse(cs.ExpiresAt, null, System.Globalization.DateTimeStyles.RoundtripKind, out var exp))
                    {
                        target.SslExpiresAt = exp;
                        target.SslIssuedDomains = domain;
                        // ACME 签发成功的证书落库（用户证书页只读可见）
                        var exists = await db.Certificates.AnyAsync(c => c.Source == "acme" && c.Domains.Contains(domain) && c.ClusterId == node.ClusterId);
                        if (!exists)
                        {
                            db.Certificates.Add(new Certificate
                            {
                                ClusterId = node.ClusterId,
                                Domains = domain,
                                CertPem = "", // 证书实体留在节点；主控仅记录元数据（到期/域名）
                                KeyPem = "",
                                ExpiresAt = exp,
                                Source = "acme",
                                CreatedAt = DateTime.UtcNow,
                            });
                        }
                    }
                }
                await db.SaveChangesAsync();
            }

            // 区域探测结果入库（最多保留最近 1000 条/表，后台由仪表盘聚合）
            if (req.Stats?.Probes is { Count: > 0 })
            {
                var probeRecords = req.Stats.Probes.Select(p2 => new ProbeRecord
                {
                    NodeId = node.Id,
                    ServerId = p2.ServerId,
                    Ok = p2.Ok,
                    Status = p2.Status,
                    LatencyMs = p2.LatencyMs,
                    Error = p2.Error ?? "",
                    Timestamp = DateTime.UtcNow,
                }).ToList();
                db.ProbeRecords.AddRange(probeRecords);
                await db.SaveChangesAsync();
            }

            return Results.Ok(new { ok = true, latestVersion = DateTimeOffset.UtcNow.ToUnixTimeSeconds() });
        });

        grp.MapPost("/logs", async (HttpRequest http, CdnDbContext db) =>
        {
            // 用 JsonDocument 解析，避免集合元素类型参与 EF 模型校验
            LogsBody req;
            try
            {
                req = await System.Text.Json.JsonSerializer.DeserializeAsync<LogsBody>(
                    http.Body, JsonOpts) ?? new LogsBody("", null);
            }
            catch (System.Text.Json.JsonException)
            {
                return Results.BadRequest(new { error = "invalid json" });
            }

            var node = await AuthorizeAsync(db, req.NodeToken);
            if (node == null) return Results.Json(new { error = "invalid node token" }, statusCode: 401);

            if (req.Logs is { Count: > 0 })
            {
                var records = req.Logs.Select(l => new AccessLogRecord
                {
                    ServerId = l.ServerId,
                    NodeId = node.Id,
                    RemoteAddr = l.RemoteAddr ?? "",
                    Method = l.Method ?? "",
                    Host = l.Host ?? "",
                    Uri = l.Uri ?? "",
                    Status = l.Status,
                    BytesSent = (long)l.BytesSent,
                    Referer = l.Referer ?? "",
                    UserAgent = l.UserAgent ?? "",
                    RequestTimeMs = l.RequestTimeMs,
                    CacheStatus = l.CacheStatus ?? "",
                    UpstreamAddr = string.IsNullOrEmpty(l.UpstreamAddr) ? null : l.UpstreamAddr,
                    RequestId = l.RequestId ?? "",
                    WafAction = l.WafAction ?? "",
                    BlockReason = string.IsNullOrEmpty(l.BlockReason) ? null : l.BlockReason,
                    Timestamp = DateTimeOffset.FromUnixTimeSeconds(l.Timestamp).UtcDateTime,
                }).ToList();
                db.AccessLogs.AddRange(records);
                await db.SaveChangesAsync();
            }
            return Results.Ok(new { ok = true });
        });
    }

    /// <summary>集群阈值告警：CPU/内存/负载越限写 Notification（管理员广播），同一节点同一类型 30 分钟节流。</summary>
    private static async Task CheckThresholdsAsync(CdnDbContext db, Node node, NodeStatsBody stats)
    {
        var opsJson = await db.Clusters.Where(c => c.Id == node.ClusterId).Select(c => c.OpsJson).FirstOrDefaultAsync();
        var ops = string.IsNullOrEmpty(opsJson) ? null : System.Text.Json.JsonSerializer.Deserialize<Core.OpsConfig>(opsJson, (System.Text.Json.JsonSerializerOptions?)null);
        if (ops == null) return;
        var alerts = new List<(string Type, string Title, string Content)>();
        if (ops.CpuPercent > 0 && stats.CpuUsage >= ops.CpuPercent)
            alerts.Add(("cpu:" + node.Id, $"节点 {node.Name} CPU 过高", $"CPU 使用率 {stats.CpuUsage:F0}% ≥ 阈值 {ops.CpuPercent}%"));
        if (ops.MemPercent > 0 && stats.MemUsage >= ops.MemPercent)
            alerts.Add(("mem:" + node.Id, $"节点 {node.Name} 内存过高", $"内存使用率 {stats.MemUsage:F0}% ≥ 阈值 {ops.MemPercent}%"));
        if (ops.Load > 0 && stats.Load >= ops.Load)
            alerts.Add(("load:" + node.Id, $"节点 {node.Name} 负载过高", $"1min 负载 {stats.Load:F2} ≥ 阈值 {ops.Load}"));
        foreach (var (type, title, content) in alerts)
        {
            if (!AlertThrottle.Allow(type)) continue;
            db.Notifications.Add(new Notification { UserId = null, Title = title, Content = content, Type = 1 });
        }
        if (alerts.Count > 0) await db.SaveChangesAsync();
    }

    private static async Task<Node?> AuthorizeAsync(CdnDbContext db, string? nodeToken)
    {
        if (string.IsNullOrWhiteSpace(nodeToken)) return null;
        return await db.Nodes.FirstOrDefaultAsync(n => n.Token == nodeToken);
    }

    public record RegisterBody(string InstallToken, string? Hostname, string? Version, string[]? PublicIps, string? Os, string? Arch);
    public record ConfigBody(string NodeToken, long ConfigVersion);
    public record HeartbeatBody(string NodeToken, long ConfigVersion, NodeStatsBody? Stats);
    public record NodeStatsBody(double CpuUsage, double MemUsage, double Load, uint Connections, List<ProbeBody>? Probes, List<OriginProbeBody>? OriginProbes, List<PeerProbeBody>? PeerProbes, List<CertStateBody>? CertStates);
    public record CertStateBody(string Domain, string Status, string? Error, string? ExpiresAt);
    public record ChallengeRegisterBody(string NodeToken, string? Path, string? KeyAuth);
    public record CertIssuedBody(string NodeToken, string? Domain, string? CertPem, string? KeyPem);
    public record CertFailedBody(string NodeToken, string? Domain, string? Error);
    public record OriginProbeBody(string Origin, bool Reachable, double LatencyMs);
    public record PeerProbeBody(string Peer, bool Reachable, double LatencyMs);
    public record ProbeBody(long ServerId, string? Url, bool Ok, int Status, double LatencyMs, string? Error);

    public record LogsBody(string NodeToken, List<LogBody>? Logs);
    public record LogBody(
        long ServerId, string? RemoteAddr, string? Method, string? Scheme, string? Host, string? Uri,
        string? Proto, int Status, ulong BytesSent, string? Referer, string? UserAgent,
        double RequestTimeMs, string? CacheStatus, string? UpstreamAddr, string? RequestId,
        string? NodeId, long Timestamp, string? WafAction, string? BlockReason);
}

/// <summary>告警节流：同 key 30 分钟内只发一次。</summary>
public static class AlertThrottle
{
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, DateTime> _last = new();
    public static bool Allow(string key)
    {
        var now = DateTime.UtcNow;
        if (_last.TryGetValue(key, out var t) && now - t < TimeSpan.FromMinutes(30)) return false;
        _last[key] = now;
        return true;
    }
}
