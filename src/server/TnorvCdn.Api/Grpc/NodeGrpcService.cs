using System.Security.Cryptography;
using Grpc.Core;
using Microsoft.EntityFrameworkCore;
using TnorvCdn.Api.Core;
using TnorvCdn.Api.Data;
using TnorvCdn.Api.Services;

namespace TnorvCdn.Api.Grpc;

/// <summary>
/// 节点 gRPC 服务实现。
/// 安全模型：注册用一次性 InstallToken；注册后换发 NodeToken；所有后续调用校验 NodeToken。
/// </summary>
public class NodeGrpcService(CdnDbContext db, ConfigPublisher publisher, ILogger<NodeGrpcService> logger)
    : TnorvCdn.Grpc.NodeService.NodeServiceBase
{
    public override async Task<TnorvCdn.Grpc.RegisterResponse> Register(
        TnorvCdn.Grpc.RegisterRequest request, ServerCallContext context)
    {
        if (string.IsNullOrWhiteSpace(request.InstallToken))
            throw new RpcException(new Status(StatusCode.InvalidArgument, "install_token required"));

        var node = await db.Nodes.FirstOrDefaultAsync(n => n.InstallToken == request.InstallToken);
        if (node == null)
            throw new RpcException(new Status(StatusCode.PermissionDenied, "invalid install token"));

        // 一次性消耗安装令牌，防止重放
        node.InstallToken = null;
        node.Token = GenerateToken();
        node.Hostname = request.Hostname;
        node.Version = request.Version;
        node.PublicIps = request.PublicIps.ToArray();
        node.IsOnline = true;
        node.LastHeartbeat = DateTime.UtcNow;

        await db.SaveChangesAsync();
        logger.LogInformation("节点注册成功: {Name} ({NodeId})", node.Name, node.Id);

        return new TnorvCdn.Grpc.RegisterResponse
        {
            NodeId = node.Id,
            NodeToken = node.Token,
        };
    }

    public override async Task<TnorvCdn.Grpc.GetConfigResponse> GetConfig(
        TnorvCdn.Grpc.GetConfigRequest request, ServerCallContext context)
    {
        var node = await AuthorizeAsync(request.NodeToken);

        // 与 HTTP 路径一致的版本比对：版本相同直接 changed=false，避免每次全量重建快照
        var clusterVersion = await db.Clusters.Where(c => c.Id == node.ClusterId)
            .Select(c => (long?)c.ConfigVersion).FirstOrDefaultAsync() ?? 0;
        var requested = request.ConfigVersion;
        if (requested > 0 && requested == clusterVersion)
            return new TnorvCdn.Grpc.GetConfigResponse { ConfigVersion = clusterVersion, Changed = false };

        var json = await publisher.BuildNodeConfigAsync(node.Id);
        if (json == null)
            throw new RpcException(new Status(StatusCode.NotFound, "node config unavailable"));

        return new TnorvCdn.Grpc.GetConfigResponse
        {
            ConfigVersion = clusterVersion == 0 ? DateTimeOffset.UtcNow.ToUnixTimeSeconds() : clusterVersion,
            ConfigJson = json,
            Changed = true
        };
    }

    public override async Task<TnorvCdn.Grpc.HeartbeatResponse> Heartbeat(
        TnorvCdn.Grpc.HeartbeatRequest request, ServerCallContext context)
    {
        var node = await AuthorizeAsync(request.NodeToken);

        node.IsOnline = true;
        node.LastHeartbeat = DateTime.UtcNow;
        node.Version = request.Stats != null ? node.Version : node.Version;

        await db.SaveChangesAsync();

        return new TnorvCdn.Grpc.HeartbeatResponse
        {
            Ok = true,
            LatestVersion = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
        };
    }

    private async Task<Core.Node> AuthorizeAsync(string nodeToken)
    {
        if (string.IsNullOrWhiteSpace(nodeToken))
            throw new RpcException(new Status(StatusCode.Unauthenticated, "node_token required"));

        var node = await db.Nodes.FirstOrDefaultAsync(n => n.Token == nodeToken);
        if (node == null)
            throw new RpcException(new Status(StatusCode.Unauthenticated, "invalid node token"));

        return node;
    }

    private static string GenerateToken() =>
        Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
}
