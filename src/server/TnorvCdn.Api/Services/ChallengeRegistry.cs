using System.Collections.Concurrent;

namespace TnorvCdn.Api.Services;

/// <summary>ACME HTTP-01 挑战下发注册表：
/// 执行节点发起 LE 订单后把挑战路径+应答注册到这里，随节点配置快照下发全部节点；
/// 所有节点在 80 端口监听该路径应答（10 分钟 TTL，防止长期占用用户路径）。验证完成后由节点上报清除。</summary>
public class ChallengeRegistry
{
    public record Challenge(string Path, string KeyAuth, DateTime ExpiresAt);

    private readonly ConcurrentDictionary<long, ConcurrentDictionary<string, Challenge>> _byCluster = new();

    // 挑战就绪回执：path → 已确认监听的节点 ID 集合
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<long, byte>> _ready = new();

    /// <summary>节点上报"已收到挑战并在监听该路径"。</summary>
    public void MarkReady(long nodeId, string path)
    {
        _ready.GetOrAdd(path, _ => new()).TryAdd(nodeId, 0);
    }

    /// <summary>查询挑战的就绪节点集合。</summary>
    public HashSet<long> ReadyNodes(string path)
        => _ready.TryGetValue(path, out var set) ? new HashSet<long>(set.Keys) : [];

    /// <summary>清除挑战（同时清就绪回执）。</summary>
    public void Clear(long clusterId, string path)
    {
        if (_byCluster.TryGetValue(clusterId, out var dict)) dict.TryRemove(path, out _);
        _ready.TryRemove(path, out _);
    }

    /// <summary>注册挑战（执行节点经主控 API 调用）。</summary>
    public void Register(long clusterId, string path, string keyAuth)
    {
        var dict = _byCluster.GetOrAdd(clusterId, _ => new());
        dict[path] = new Challenge(path, keyAuth, DateTime.UtcNow.AddMinutes(10));
    }



    /// <summary>取某集群当前有效（未过期）的挑战列表（节点配置快照下发用）。</summary>
    public List<Challenge> DrainValid(long clusterId)
    {
        if (!_byCluster.TryGetValue(clusterId, out var dict)) return [];
        var now = DateTime.UtcNow;
        var valid = new List<Challenge>();
        foreach (var kv in dict)
        {
            if (kv.Value.ExpiresAt > now) valid.Add(kv.Value);
            else dict.TryRemove(kv.Key, out _); // 10 分钟自动失效
        }
        return valid;
    }
}
