using Grpc.Core;
using Microsoft.EntityFrameworkCore;
using TnorvCdn.Api.Core;
using TnorvCdn.Api.Data;

namespace TnorvCdn.Api.Grpc;

/// <summary>访问日志批量上报（节点 → 主控）。</summary>
public class LogGrpcService(CdnDbContext db, ILogger<LogGrpcService> logger)
    : TnorvCdn.Grpc.LogService.LogServiceBase
{
    public override async Task<TnorvCdn.Grpc.ReportAccessLogsResponse> ReportAccessLogs(
        TnorvCdn.Grpc.ReportAccessLogsRequest request, ServerCallContext context)
    {
        if (string.IsNullOrWhiteSpace(request.NodeToken))
            throw new RpcException(new Status(StatusCode.Unauthenticated, "node_token required"));

        var node = await db.Nodes.AsNoTracking().FirstOrDefaultAsync(n => n.Token == request.NodeToken);
        if (node == null)
            throw new RpcException(new Status(StatusCode.Unauthenticated, "invalid node token"));

        var records = request.Logs.Select(l => new AccessLogRecord
        {
            ServerId = l.ServerId,
            NodeId = node.Id,
            RemoteAddr = l.RemoteAddr,
            Method = l.Method,
            Host = l.Host,
            Uri = l.Uri,
            Status = l.Status,
            BytesSent = (long)l.BytesSent,
            Referer = l.Referer,
            UserAgent = l.UserAgent,
            RequestTimeMs = l.RequestTimeMs,
            CacheStatus = l.CacheStatus,
            UpstreamAddr = string.IsNullOrEmpty(l.UpstreamAddr) ? null : l.UpstreamAddr,
            RequestId = l.RequestId,
            WafAction = l.WafAction,
            BlockReason = string.IsNullOrEmpty(l.BlockReason) ? null : l.BlockReason,
            Timestamp = DateTimeOffset.FromUnixTimeSeconds(l.Timestamp).UtcDateTime,
        }).ToList();

        if (records.Count > 0)
        {
            db.AccessLogs.AddRange(records);
            await db.SaveChangesAsync();
            logger.LogDebug("收到 {Count} 条访问日志（节点 {NodeId}）", records.Count, node.Id);
        }

        return new TnorvCdn.Grpc.ReportAccessLogsResponse { Ok = true };
    }
}
