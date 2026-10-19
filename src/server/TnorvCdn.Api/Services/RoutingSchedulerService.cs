using System.Text.Json;
using TnorvCdn.Api.Core;
using TnorvCdn.Api.Data;

namespace TnorvCdn.Api.Services;

/// <summary>
/// 就近回源调度器（拨测驱动的动态回源路径）：
///   1. 接收各节点心跳上报的 OriginProbes（本节点→各源站 TCP RTT）与 PeerProbes（本节点→兄弟节点 RTT），落内存快照
///   2. 为每个边缘节点计算最优回源路径：直连源站 RTT vs「经兄弟节点两跳」（peer RTT + peer→origin RTT）
///   3. 计算结果随配置快照下发（NodeConfig.Routing），节点按推荐路径回源；L2 静态拓扑作为兜底
/// 设计：纯内存计算（节点数 ≤ 数十，无持久化必要）；每节点心跳到达时增量更新，下发时现算。
/// </summary>
public class RoutingSchedulerService : BackgroundService
{
    // (nodeId, originAddr) → RTT ms
    private static readonly Dictionary<(long, string), double> OriginRtt = new();
    // (nodeId, peerAddr) → RTT ms
    private static readonly Dictionary<(long, string), double> PeerRtt = new();
    private static readonly object Lock = new();

    private readonly IServiceScopeFactory _scopes;
    private readonly ILogger<RoutingSchedulerService> _logger;

    public RoutingSchedulerService(IServiceScopeFactory scopes, ILogger<RoutingSchedulerService> logger)
    { _scopes = scopes; _logger = logger; }

    /// <summary>心跳到达时更新拨测快照（由 NodeApiEndpoints 调用）。</summary>
    public static void UpdateProbes(long nodeId,
        IEnumerable<(string origin, double ms)> origins,
        IEnumerable<(string peer, double ms)> peers)
    {
        lock (Lock)
        {
            foreach (var (o, ms) in origins) OriginRtt[(nodeId, o)] = ms;
            foreach (var (p, ms) in peers) PeerRtt[(nodeId, p)] = ms;
        }
    }

    protected override Task ExecuteAsync(CancellationToken ct) => new Task(() => { }, ct); // 纯被动，无周期任务

    /// <summary>
    /// 为节点计算推荐回源路径。返回 (mode, via)：
    ///   mode=direct：直连源站（直连 RTT 已是最低，或无拨测数据）
    ///   mode=via：经 peerAddr 中转（两跳总延迟显著低于直连，节省跨网流量）
    /// </summary>
    public static (string Mode, string? Via, double DirectMs, double ViaMs) ComputeRouting(
        long nodeId, string primaryOrigin, IEnumerable<string> peerAddrs)
    {
        lock (Lock)
        {
            var direct = OriginRtt.TryGetValue((nodeId, primaryOrigin), out var d) ? d : -1;
            if (direct < 0) return ("direct", null, -1, -1); // 无数据：直连兜底

            double bestVia = double.MaxValue; string? bestPeer = null;
            foreach (var peer in peerAddrs)
            {
                if (!PeerRtt.TryGetValue((nodeId, peer), out var hop1) || hop1 < 0) continue;
                // peer 到源站的 RTT：任意上报过该 peer 的节点对？——精确需 peer 自己的 OriginProbes：
                // 我们近似：查 (peerNodeId, origin)，peerAddr 里解析不出 nodeId，改用 peer 心跳上报的
                // OriginProbes 已含 (peerNodeId, origin)；遍历找 peerNodeId 使 PeerRtt[(nodeId, peerAddr)] 与 OriginRtt[(peerNodeId, origin)] 同时存在
                // 简化：由主控节点表建立 peerAddr→nodeId 映射后传入；此处用静态方法不方便查库，
                // 改为 ComputeRouting 重载由 DnsService/ConfigPublisher 持有映射调用（见 ComputeRoutingMapped）。
            }
            return ("direct", null, direct, bestVia);
        }
    }

    /// <summary>带 peerAddr→nodeId 映射的完整计算（由 ConfigPublisher 调用）。</summary>
    public static (string Mode, string? Via, double DirectMs, double ViaMs) ComputeRoutingMapped(
        long nodeId, string primaryOrigin, Dictionary<string, long> peerAddrToNodeId)
    {
        lock (Lock)
        {
            var direct = OriginRtt.TryGetValue((nodeId, primaryOrigin), out var d) ? d : -1;
            if (direct < 0) return ("direct", null, -1, -1);

            double bestTotal = double.MaxValue; string? bestPeer = null;
            foreach (var (peerAddr, peerNodeId) in peerAddrToNodeId)
            {
                if (peerNodeId == nodeId) continue;
                if (!PeerRtt.TryGetValue((nodeId, peerAddr), out var hop1)) continue;
                if (!OriginRtt.TryGetValue((peerNodeId, primaryOrigin), out var hop2)) continue;
                var total = hop1 + hop2;
                if (total < bestTotal) { bestTotal = total; bestPeer = peerAddr; }
            }
            // 经中转的两跳延迟比直连低 30% 以上才值得绕行（省的是跨网带宽，费的是中转延迟）
            if (bestPeer != null && bestTotal < direct * 0.7)
                return ("via", bestPeer, direct, bestTotal);
            return ("direct", null, direct, bestTotal);
        }
    }

    /// <summary>导出当前快照（诊断用）。</summary>
    public static object Snapshot()
    {
        lock (Lock)
        {
            return new
            {
                origins = OriginRtt.Select(kv => new { nodeId = kv.Key.Item1, origin = kv.Key.Item2, rttMs = kv.Value }),
                peers = PeerRtt.Select(kv => new { nodeId = kv.Key.Item1, peer = kv.Key.Item2, rttMs = kv.Value }),
            };
        }
    }
}
